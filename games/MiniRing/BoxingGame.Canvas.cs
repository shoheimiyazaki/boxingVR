using System.Numerics;

namespace MiniRing;

/// <summary>
/// 床（キャンバス）に落ちた衝撃（docs/GRAPPLING.md 段 5・§9）: 物理の歩の前後の全身の重心の速さの変化から、重力と近似の支え
/// （<see cref="Fighter.SupportLift"/>）の分を引いて、床が体へ与えた上向きの力積 J を出し、床に当たった部位へ、それぞれが止められた
/// 下向きの運動量（m Δv）の割合で分ける。部位の衝撃のエネルギーは ½ J_部位 Δv_部位（関節を通って乗ってくる体の重さも入る）。拳の打撃と同じ道で体へ効かせる: 頭なら、当たった点が頭の重心から
/// ずれた分だけ頭がねじられ（<see cref="Fighter.TwistHead"/>、脳しんとう）、胴・頭は当たった所が潰れ（ボクセル）、臓器が打撲する。
/// 投げの種類で分けない: 高く・速く・頭から落ちるほど効く。足（足・つま先）は着地の部位なので数えない。
/// </summary>
public sealed partial class BoxingGame
{
    private readonly Dictionary<int, Vector3> _canvasBefore = [];
    private readonly Dictionary<Fighter, Vector3> _comBefore = [];
    private readonly List<(int Id, float Stop, Vector3 Point, float Weight)> _landing = [];

    /// <summary>この歩で床に落ちた衝撃（観戦・ドリル用）。</summary>
    public IReadOnlyList<CanvasImpact> CanvasImpacts => _canvasImpacts;

    private readonly List<CanvasImpact> _canvasImpacts = [];

    /// <summary>床に落ちた衝撃が起きた（ドリル・記録用）。</summary>
    public event Action<CanvasImpact>? Landed;

    /// <summary>
    /// 背負っている相手の重さを入れる（docs/GRAPPLING.md §13）: 相手を掴み、胴（胸・腰）どうしが <see cref="CarryContactGap"/> 以内に触れていれば、
    /// 相手が自分の脚で支えきれていない分の重さを、掴んでいる側の脚が感じる重さにする。
    /// </summary>
    private void UpdateCarriedLoads()
    {
        foreach (var self in Fighters)
        {
            var other = TargetOf(self);
            var holding = self.IsHolding(Hand.Left) || self.IsHolding(Hand.Right);
            if (!holding || self.State != FighterState.Standing || !TorsosTouch(self, other))
            {
                self.CarriedLoad = 0f;
                continue;
            }

            var upper = other.SupportedMass - other.LegMass;
            self.CarriedLoad = (1f - other.VerticalBalance) * upper * -_world.Gravity.Y;
            self.CarriedCenter = other.CenterOfMass;
        }
    }

    /// <summary>背負う接触とみなす、胴の箱どうしの水平のすき間 [m]。</summary>
    private const float CarryContactGap = 0.02817f;

    private bool TorsosTouch(Fighter a, Fighter b)
    {
        foreach (var pa in (ReadOnlySpan<int>)[a.Parts.Chest, a.Parts.Pelvis])
        {
            foreach (var pb in (ReadOnlySpan<int>)[b.Parts.Chest, b.Parts.Pelvis])
            {
                var x = _world.GetState(pa).Position;
                var y = _world.GetState(pb).Position;
                var gap = new Vector2(x.X - y.X, x.Z - y.Z).Length() - a.HalfExtentsOf(pa).Z - b.HalfExtentsOf(pb).Z;
                if (gap <= CarryContactGap && MathF.Abs(x.Y - y.Y) <= a.HalfExtentsOf(pa).Y + b.HalfExtentsOf(pb).Y)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RememberCanvasVelocities()
    {
        _canvasBefore.Clear();
        foreach (var fighter in Fighters)
        {
            _comBefore[fighter] = fighter.CenterOfMassVelocity;
            foreach (var id in fighter.AllParts)
            {
                if (!fighter.IsDetachedPart(id) && fighter.KindOf(id) is not (BodyPartKind.Foot or BodyPartKind.Toe))
                {
                    _canvasBefore[id] = _world.GetState(id).LinearVelocity;
                }
            }
        }
    }

    private void DetectCanvasImpacts(float dt)
    {
        _canvasImpacts.Clear();
        if (RoundOver || Resting || Holding)
        {
            _episodes.Clear();
            return;
        }

        var touch = RingSurfaceHeight + _tuning.SupportContactGap;
        foreach (var fighter in Fighters)
        {
            _landing.Clear();
            var weights = 0f;
            foreach (var id in fighter.AllParts)
            {
                if (!_canvasBefore.TryGetValue(id, out var before) || before.Y >= 0f)
                {
                    continue;
                }

                var s = _world.GetState(id);
                var stop = MathF.Min(s.LinearVelocity.Y - before.Y, -before.Y);
                if (stop <= 0f)
                {
                    continue;
                }

                var (lowest, lowY) = LowestCorner(fighter, id, s, _tuning.SupportContactGap);
                if (lowY > touch)
                {
                    continue;
                }

                var weight = _world.GetMass(id) * stop;
                _landing.Add((id, stop, lowest, weight));
                weights += weight;
            }

            if (_landing.Count == 0 || !_comBefore.TryGetValue(fighter, out var comBefore))
            {
                continue;
            }

            // 床の衝撃の力積 = 全身の上向きの運動量の変化 − 近似の支えの分。床に乗っている体の重さを支える分（M g dt）は衝撃ではないので数えない。
            var total = MathF.Max(0f, (fighter.SupportedMass * (fighter.CenterOfMassVelocity.Y - comBefore.Y)) - fighter.SupportLift);
            _ = dt;
            foreach (var (id, stop, point, weight) in _landing)
            {
                // 柔らかい接触は数歩かけて止める: 止まり始めてから止まり終わるまでの力積と速さの変化を 1 回の衝撃として足していく。
                var share = total * weight / weights;
                var episode = _episodes.TryGetValue(id, out var e) ? e : new CanvasEpisode(fighter, id, point, 0f, 0f, true);
                _episodes[id] = episode with { Impulse = episode.Impulse + share, Stop = episode.Stop + stop, Point = point, Touched = true };
            }
        }

        // この歩で止まらなかった部位の衝撃を締めて、体へ効かせる。
        _finished.Clear();
        foreach (var (id, episode) in _episodes)
        {
            if (episode.Touched)
            {
                _episodes[id] = episode with { Touched = false };
            }
            else
            {
                _finished.Add(id);
            }
        }

        foreach (var id in _finished)
        {
            var episode = _episodes[id];
            _episodes.Remove(id);
            Land(episode);
        }
    }

    private readonly Dictionary<int, CanvasEpisode> _episodes = [];
    private readonly List<int> _finished = [];

    private readonly record struct CanvasEpisode(Fighter Fighter, int Id, Vector3 Point, float Impulse, float Stop, bool Touched);

    /// <summary>1 回の床の衝撃を体へ効かせる: エネルギー ½ J Δv、頭なら当たった点のずれで頭がねじられる。</summary>
    private void Land(CanvasEpisode episode)
    {
        var fighter = episode.Fighter;
        if (SeatOf(fighter) < 0)
        {
            return; // ラウンドが替わって作り直された前の体
        }

        var energy = 0.5f * episode.Impulse * episode.Stop;
        var carve = MathF.Min(_tuning.MaxCarve, energy * _tuning.CarvePerJoule * _tuning.CanvasCarveShare);
        if (carve < _tuning.CanvasMinCarve || fighter.IsDetachedPart(episode.Id))
        {
            return; // 1 つのボクセルにも満たない（座り込む・手を着く程度）
        }

        var twist = 0f;
        if (episode.Id == fighter.Parts.Head)
        {
            // 頭を回すのは頭そのものを止めた力積（頭の質量 × 止まった速さ）まで。部位へ分けた力積には首を通って乗ってくる体の重さも入るが、
            // その分は首から体へ伝わって体を止める力で、頭の重心まわりには回さない（全部を頭の角に掛けると、倒れただけで 280 rad/s 回り、
            // 脳しんとうが 1.2 → 3.9 に跳ねて 16 秒たっても意識が戻らなかった）。
            var head = _world.GetState(episode.Id);
            var own = MathF.Min(episode.Impulse, _world.GetMass(episode.Id) * episode.Stop);
            fighter.TwistHead(Vector3.Cross(episode.Point - head.Position, new Vector3(0f, own, 0f)));
            twist = fighter.LastHeadTwist;
        }

        fighter.BruiseOrgans(episode.Point, energy);
        Strike(fighter, episode.Id, episode.Point, -Vector3.UnitY, carve / _tuning.CarvePerDamage, carve: carve);
        var impact = new CanvasImpact(fighter, fighter.KindOf(episode.Id), episode.Point, episode.Stop, energy, twist);
        _canvasImpacts.Add(impact);
        Landed?.Invoke(impact);
    }

    /// <summary>
    /// 部位が床に触れている所: 一番低い角の高さと、床から <paramref name="gap"/> 以内にある角の真ん中（床が押す力の合力の点）。
    /// 平らに落ちた面は面の真ん中を押される（一番低い角 1 点を押されるとすると、後頭部から平らに落ちただけで頭が角を軸に強く回った）。
    /// </summary>
    private static (Vector3 Point, float Y) LowestCorner(Fighter fighter, int id, Aigis.Physics.BodyState s, float gap)
    {
        var half = fighter.HalfExtentsOf(id);
        Span<Vector3> corners = stackalloc Vector3[8];
        var lowY = float.PositiveInfinity;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
            corners[i] = s.Position + Vector3.Transform(corner, s.Orientation);
            lowY = MathF.Min(lowY, corners[i].Y);
        }

        var sum = Vector3.Zero;
        var count = 0;
        foreach (var p in corners)
        {
            if (p.Y <= lowY + gap)
            {
                sum += p;
                count++;
            }
        }

        return (sum / count, lowY);
    }
}

/// <summary>床に落ちた衝撃 1 つ: 誰の・どの部位が・どこで・どれだけの速さを止められたか、運動エネルギー、頭がねじられた速さ（頭でなければ 0）。</summary>
public readonly record struct CanvasImpact(Fighter Fighter, BodyPartKind Part, Vector3 Point, float Speed, float Energy, float HeadTwist);
