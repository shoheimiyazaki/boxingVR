using System.Numerics;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Highlights;

/// <summary>見どころの種類。定義は <see cref="MatchHighlights"/> と docs/EXCITEMENT.md。</summary>
public enum HighlightKind
{
    Exchange,
    NearMiss,
    Graze,
    Counter,
    Flurry,
    Knockdown,
    Rise,
    KO,
    OnePunch,
    BoneBreak,
    LimbLoss,
    VoxelLoss,
    Shatter,
    HeadSnap,
}

/// <summary>1 つの見どころ。<paramref name="Time"/> は観測に渡した時計の秒、<paramref name="Red"/> は主役が赤（プレイヤー側）か。
/// <paramref name="Intensity"/> は物理量から連続に出した強さ（1 が基準）。</summary>
public readonly record struct Highlight(HighlightKind Kind, float Time, bool Red, float Intensity, string Note);

/// <summary>
/// 試合の見どころを拾う観測（2026-11-19）。<b>読むだけ</b>: 物理・判断・乱数に触れないので、チェックサムも再生も変わらない
/// （<c>HighlightTests</c> が確かめる）。1 歩ごとに <see cref="Observe"/> を呼ぶ。
/// <para>
/// 種類ごとの「強さ」は、§1.3 に従い種類の加点ではなく、その出来事を表す物理量（拳の速さ・外れた距離・よけた頭の速さ・打撃のエネルギー・
/// 頭のねじれの角速度・失ったセルの割合・リードの大きさ）から連続に出す。種類ごとの重みは総合点の側（<see cref="ExcitementTuning.Weights"/>）。
/// </para>
/// </summary>
public sealed class MatchHighlights
{
    private const float HumanHeight = 1.75f;

    private readonly ExcitementTuning _t;
    private readonly List<Highlight> _events = [];
    private readonly Side _red = new();
    private readonly Side _blue = new();
    private readonly List<(float Time, bool Red)> _chain = [];
    private readonly Dictionary<VoxelBody, (int Version, byte[] Cells)> _snapshots = [];
    private readonly Dictionary<VoxelBody, (int Start, int Last)> _partCounts = [];
    private float _redDealt;
    private float _blueDealt;
    private int _playerWins;
    private int _cpuWins;
    private bool _deathKoPending; // 死で KO を数えた後、勝ち数の増加を同じ KO として数えない
    private float _first = float.NaN;
    private float _last;
    private float _nextBelief;
    private readonly float _matchSeconds;
    private readonly List<BeliefSample> _beliefs = [];

    /// <param name="tuning">測り方の値（省略で既定）。</param>
    /// <param name="matchSeconds">試合の長さの見込み [s]（見込みのモデルの「試合の進み」に使う）。</param>
    public MatchHighlights(ExcitementTuning? tuning = null, float matchSeconds = 67.14f)
    {
        _t = tuning ?? ExcitementTuning.Default;
        _matchSeconds = matchSeconds;
    }

    public IReadOnlyList<Highlight> Events => _events;

    /// <summary>試合の結果（BotArena と同じ決め方）: +1 赤の勝ち、−1 青の勝ち、0 引き分け。取ったラウンドの数、同じなら与えたダメージの差（5 % か 1 以内は引き分け）。</summary>
    public int Winner => _playerWins != _cpuWins ? Math.Sign(_playerWins - _cpuWins)
        : MathF.Abs(_redDealt - _blueDealt) <= MathF.Max(1f, 0.05f * (_redDealt + _blueDealt) / 2f) ? 0 : MathF.Sign(_redDealt - _blueDealt);

    /// <summary>赤・青の与えた生のダメージの合計。</summary>
    public float RawDamage => _redDealt + _blueDealt;

    private sealed class Side
    {
        public Fighter? Fighter;
        public float Scale = 1f; // 試合の長さ → 人の m
        public readonly Vector3[] Fist = new Vector3[2];
        public Vector3 Head;
        public Vector3 Chest;
        public Vector3 HeadVelocity;
        public readonly Flight?[] Flights = new Flight?[2];
        public readonly bool[] Armed = [false, false];
        public bool Down;
        public float DownSince;
        public bool Dead;
        public int StartCells;
        public float Consciousness = 1f;
        public float PreviousTwist;
        public readonly List<(float Time, float ConsciousnessBefore, float Energy)> HitsTaken = [];
        public readonly List<float> Landed = [];
        public float FlurryUntil = float.NegativeInfinity;
    }

    private sealed class Flight
    {
        public float Start;
        public float MinDistance = float.MaxValue;
        public float SpeedAtMin;
        public float EvadeAtMin;
        public bool Landed;
    }

    /// <summary>1 歩ごとに見る。<paramref name="time"/> は記録に使う時計（試合の秒でも映像の秒でもよい）、<paramref name="dt"/> は歩の長さ。</summary>
    public void Observe(BoxingGame game, float time, float dt)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (float.IsNaN(_first))
        {
            _first = time;
        }

        _last = time;
        if (dt <= 0f)
        {
            return;
        }

        var fresh = Bind(_red, game.Player) | Bind(_blue, game.Cpu);
        var hits = HighlightSource.Hits(game).ToList();
        var lost = Carved(game);
        // 当たりを先に: 当たった拳の飛行を「当たり」にしてから、飛行の終わり（紙一重かどうか）を見る。
        foreach (var hit in hits)
        {
            OnHit(game, hit, time, lost);
        }

        if (!fresh)
        {
            Kinematics(_red, _blue, time, dt);
            Kinematics(_blue, _red, time, dt);
        }

        Bodies(game, time);
        Downs(game, _red, true, time);
        Downs(game, _blue, false, time);
        Wins(game, time);
        SampleBelief(game, time);
        foreach (var s in new[] { _red, _blue })
        {
            s.Consciousness = s.Fighter!.Consciousness;
        }
    }

    /// <summary>試合の終わり: 続いていた打ち合いを閉じる。<see cref="Summarize"/> の前に 1 回。</summary>
    public void Finish() => CloseChain();

    /// <summary>見えている状態の記録（<see cref="ExcitementTuning.BeliefSample"/> 秒ごと）。見込みのモデルの当てはめにも使う。</summary>
    public IReadOnlyList<BeliefSample> Beliefs => _beliefs;

    /// <summary>見込み p_red(t)（<paramref name="model"/> 省略で <see cref="WinModel.Default"/>）。</summary>
    public IReadOnlyList<(float Time, float P)> Belief(WinModel? model = null)
    {
        var m = model ?? WinModel.Default;
        return [.. _beliefs.Select(b => (b.Time, m.Predict(b.Features)))];
    }

    public ExcitementSummary Summarize(WinModel? model = null) => float.IsNaN(_first)
        ? ExcitementSummary.From(_events, RawDamage, 0f, 0f, [], _t)
        : ExcitementSummary.From(_events, RawDamage, _first, _last - _first, Belief(model), _t);

    /// <summary>見えている状態を記録する（§1.7: 観客に見える物だけ）。赤の有利を正にそろえる。</summary>
    private void SampleBelief(BoxingGame game, float time)
    {
        if (time + 1e-4f < _nextBelief)
        {
            return;
        }

        _nextBelief = time + _t.BeliefSample;
        float Loss(Side s, Fighter f)
        {
            var filled = HighlightSource.Grids(f).Sum(g => g.Grid.FilledCount);
            if (s.StartCells <= 0)
            {
                s.StartCells = filled;
            }

            return s.StartCells > 0 ? Math.Clamp(1f - (filled / (float)s.StartCells), 0f, 1f) : 0f;
        }

        var red = game.Player;
        var blue = game.Cpu;
        var progress = Math.Clamp((time - _first) / MathF.Max(1f, _matchSeconds), 0f, 1f);
        var total = _redDealt + _blueDealt;
        _beliefs.Add(new BeliefSample(time, WinModel.Features(
            game.PlayerWins - game.CpuWins,
            red.Consciousness - blue.Consciousness,
            (blue.IsKnockedDown ? 1f : 0f) - (red.IsKnockedDown ? 1f : 0f),
            Loss(_blue, blue) - Loss(_red, red),
            red.Stamina - blue.Stamina,
            (_redDealt - _blueDealt) / (total + _t.DamageShareSoftening),
            progress)));
    }

    private static bool Bind(Side s, Fighter f)
    {
        if (ReferenceEquals(s.Fighter, f))
        {
            return false;
        }

        // ラウンドごとにワールドが作り直されるので、選手の物も入れ替わる。速さの差分は次の歩から。
        s.Fighter = f;
        var floor = MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);
        var height = MathF.Max(1e-3f, (f.HeadPosition.Y - floor) / 0.93f); // 頭の中心は身長の約 93 %
        s.Scale = HumanHeight / height;
        s.Fist[0] = f.GloveCenter(Hand.Left);
        s.Fist[1] = f.GloveCenter(Hand.Right);
        s.Head = f.HeadPosition;
        s.Chest = f.ChestPosition;
        s.HeadVelocity = Vector3.Zero;
        s.Flights[0] = s.Flights[1] = null;
        s.HitsTaken.Clear();
        s.Landed.Clear();
        s.FlurryUntil = float.NegativeInfinity;
        s.PreviousTwist = f.LastHeadTwist;
        s.DownSince = 0f;
        // 生まれた直後の腕は生成の姿勢から構えへ動いている途中で、拳が相手の方へ速く動いても打ち出しではない（2026-11-27: 基準の体の腕が長く速くなり、
        // 構えへ動く途中の 0.03 秒で 4 本の拳が打ち出しの速さを超えて「打ち合い」が出た）。拳が一度止まってから（打ち出しの速さの半分より遅く）数える。
        s.Armed[0] = s.Armed[1] = false;
        s.Down = f.IsKnockedDown;
        s.Dead = f.IsDead;
        s.Consciousness = f.Consciousness;
        return true;
    }

    private static float Flat(Vector3 v) => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));

    /// <summary>拳の打ち出し・飛んでいる拳と相手の頭の一番近い距離（紙一重）・打ち合いの鎖。</summary>
    private void Kinematics(Side me, Side other, float time, float dt)
    {
        var f = me.Fighter!;
        var opp = other.Fighter!;
        var head = f.HeadPosition;
        me.HeadVelocity = (head - me.Head) / dt;
        me.Head = head;
        var chest = f.ChestPosition;
        var chestVelocity = (chest - me.Chest) / dt; // 体ごと動いた分は打ち出しに数えない
        me.Chest = chest;
        var range = Flat(chest - opp.ChestPosition) * me.Scale;
        for (var h = 0; h < 2; h++)
        {
            var hand = h == 0 ? Hand.Left : Hand.Right;
            var fist = f.GloveCenter(hand);
            var v = (fist - me.Fist[h]) / dt;
            me.Fist[h] = fist;
            var toward = opp.HeadPosition - fist;
            var dist = toward.Length();
            var dir = dist > 1e-6f ? toward / dist : Vector3.UnitZ;
            var relative = v - chestVelocity;
            var drive = Vector3.Dot(relative, dir) * me.Scale;
            if (me.Armed[h] && drive >= _t.LaunchSpeed && !me.Down)
            {
                me.Armed[h] = false;
                me.Flights[h] = new Flight { Start = time };
                if (range <= _t.ExchangeRange)
                {
                    Launch(time, ReferenceEquals(me, _red));
                }
            }
            else if (!me.Armed[h] && drive < 0.5f * _t.LaunchSpeed)
            {
                me.Armed[h] = true;
            }

            if (me.Flights[h] is not { } flight)
            {
                continue;
            }

            var gap = dist * me.Scale;
            if (gap < flight.MinDistance)
            {
                flight.MinDistance = gap;
                flight.SpeedAtMin = v.Length() * me.Scale;
                flight.EvadeAtMin = Evade(other.HeadVelocity * other.Scale, v);
            }

            if (time - flight.Start > _t.FlightSeconds || (me.Armed[h] && time - flight.Start > 4 * dt))
            {
                me.Flights[h] = null;
                NearMiss(flight, ReferenceEquals(me, _red), time);
            }
        }
    }

    /// <summary>よけの速さ: 拳の進む向きに対して、頭が横へ逃げる速さと奥へ引く速さ（拳の方へ寄る動きは数えない）[人の m/s]。</summary>
    private static float Evade(Vector3 headVelocity, Vector3 fistVelocity)
    {
        var len = fistVelocity.Length();
        if (len < 1e-6f)
        {
            return 0f;
        }

        var u = fistVelocity / len;
        var along = Vector3.Dot(headVelocity, u);
        var perp = (headVelocity - (along * u)).Length();
        var back = MathF.Max(0f, along);
        return MathF.Sqrt((perp * perp) + (back * back));
    }

    /// <summary>
    /// 紙一重（NearMiss）: 打ち出した拳が当たらずに終わり、飛んでいる間に拳の中心が相手の頭の中心へ <see cref="ExcitementTuning.NearMissDistance"/>
    /// より近づいた。強さ = (その時の拳の速さ / 基準) × (外れの近さ: 当たる距離で 1、帯の外で 0) × (よけた頭の速さ / 基準、上限 1)。
    /// </summary>
    private void NearMiss(Flight flight, bool red, float time)
    {
        if (flight.Landed || flight.MinDistance >= _t.NearMissDistance)
        {
            return;
        }

        var closeness = Math.Clamp((_t.NearMissDistance - flight.MinDistance) / (_t.NearMissDistance - _t.ContactDistance), 0f, 1f);
        var intensity = flight.SpeedAtMin / _t.NearMissSpeedRef * closeness * MathF.Min(1f, flight.EvadeAtMin / _t.EvadeSpeedRef);
        if (intensity >= 0.25f)
        {
            // 主役はよけた側。
            Add(HighlightKind.NearMiss, time, !red, intensity, $"拳 {flight.SpeedAtMin:0.0} m/s が頭の {flight.MinDistance * 100f:0} cm を通過、よけ {flight.EvadeAtMin:0.0} m/s");
        }
    }

    /// <summary>
    /// 打ち合い（Exchange）: 間合いの中で、打ち出しと打ち出しの間が <see cref="ExcitementTuning.ExchangeGap"/> 以内に続いた鎖。
    /// 強さ = 打つ側が入れ替わった回数（行き来の回数）。入れ替わりが 1 回以上で 1 つ。
    /// </summary>
    private void Launch(float time, bool red)
    {
        if (_chain.Count > 0 && time - _chain[^1].Time > _t.ExchangeGap)
        {
            CloseChain();
        }

        _chain.Add((time, red));
    }

    private void CloseChain()
    {
        var turns = 0;
        for (var i = 1; i < _chain.Count; i++)
        {
            turns += _chain[i].Red != _chain[i - 1].Red ? 1 : 0;
        }

        if (turns >= 1)
        {
            Add(HighlightKind.Exchange, _chain[0].Time, _chain[0].Red, turns, $"{_chain.Count} 発・行き来 {turns} 回（{_chain[^1].Time - _chain[0].Time:0.0} 秒）");
        }

        _chain.Clear();
    }

    private void OnHit(BoxingGame game, ObservedHit hit, float time, Dictionary<Fighter, (int Soft, int Hard)> lost)
    {
        var red = ReferenceEquals(hit.Attacker, game.Player);
        var (me, other) = red ? (_red, _blue) : (_blue, _red);
        if (red)
        {
            _redDealt += hit.Damage;
        }
        else
        {
            _blueDealt += hit.Damage;
        }

        // 打った拳の飛行は「当たった」（紙一重ではない）。どちらの手か分からない武器の当たりも、両方の飛行を当たりにする。
        foreach (var flight in me.Flights)
        {
            if (flight is not null)
            {
                flight.Landed = true;
            }
        }

        if (hit.Zone is HitZone.Guard or HitZone.Elbow or HitZone.Shoulder) // 腕で受けた当たりはクリーンではない
        {
            return;
        }

        other.HitsTaken.Add((time, other.Consciousness, hit.Energy));
        me.Landed.Add(time);
        me.Landed.RemoveAll(t => time - t > _t.FlurrySeconds);
        if (me.Landed.Count >= _t.FlurryHits && time > me.FlurryUntil)
        {
            // 連打（Flurry）: 1 秒の中にクリーンヒット 3 発以上。強さ = 発数 / 3。続く連打は 1 秒あくまで 1 つ。
            Add(HighlightKind.Flurry, me.Landed[0], red, me.Landed.Count / (float)_t.FlurryHits, $"{me.Landed.Count} 発 / {_t.FlurrySeconds:0} 秒");
            me.FlurryUntil = time + _t.FlurrySeconds;
        }

        var energy = hit.Energy / _t.EnergyRef;
        // カウンター（Counter）: 相手の拳が飛んでいる（打ち出した後・まだ当たっていない）間に当てた。強さ = √(エネルギー / 基準)。
        if (other.Flights.Any(f => f is not null && !f.Landed && f.Start < time))
        {
            Add(HighlightKind.Counter, time, red, MathF.Sqrt(MathF.Max(0f, energy)), $"相手の拳が飛んでいる間に {HitZones.Label(hit.Zone)}、{hit.Energy:0.00} J");
        }

        // 皮一枚（Graze）: 強い当たり（エネルギーが基準の半分以上）なのに、その歩で削れたのが皮膚・髪だけ。打たれた側の頭がよけていた速さを掛ける。
        // 強さ = (エネルギー / 基準) × (よけ / 基準、上限 1)。
        if (energy >= _t.GrazeEnergyShare && lost.TryGetValue(hit.Victim, out var cut) && cut.Soft > 0 && cut.Hard == 0)
        {
            var evade = MathF.Min(1f, Evade(other.HeadVelocity * other.Scale, other.Head - hit.Point) / _t.EvadeSpeedRef);
            if (evade > 0.25f)
            {
                Add(HighlightKind.Graze, time, !red, energy * evade, $"皮膚だけ {cut.Soft} セル（{hit.Energy:0.00} J、よけ {evade:0.00}）");
            }
        }
    }


    /// <summary>骨折・切断・大きな欠け・派手な砕け・頭のねじれ。</summary>
    private void Bodies(BoxingGame game, float time)
    {
        bool IsRed(Fighter? victim, Vector3 point) => victim is not null
            ? ReferenceEquals(victim, game.Player)
            : Vector3.DistanceSquared(point, game.Player.PelvisPosition) < Vector3.DistanceSquared(point, game.Cpu.PelvisPosition);

        // 骨折（BoneBreak）・切断（LimbLoss）: 起きたら強さ 1（主役は壊された側）。
        foreach (var (victim, point) in HighlightSource.BrokenBones(game))
        {
            Add(HighlightKind.BoneBreak, time, IsRed(victim, point), 1f, "骨が断たれた");
        }

        foreach (var (victim, point) in HighlightSource.Severs(game))
        {
            Add(HighlightKind.LimbLoss, time, IsRed(victim, point), 1f, "部位が切り離された");
        }

        // 派手な砕け（Shatter）: 1 回で飛んだ粒が多い。強さ = 粒の数 / 閾値。
        foreach (var (point, grains) in HighlightSource.Shatters(game))
        {
            if (grains >= _t.ShatterGrains)
            {
                Add(HighlightKind.Shatter, time, IsRed(null, point), grains / (float)_t.ShatterGrains, $"{grains} 粒");
            }
        }

        // 頭のねじれ（HeadSnap）: 打撃が頭を首の上でねじった角速度が閾値を超えた瞬間。強さ = (ω / 閾値)²（意識の落ち方と同じ 2 乗）。
        foreach (var (s, red) in new[] { (_red, true), (_blue, false) })
        {
            var twist = HighlightSource.HeadTwist(s.Fighter!);
            // LastHeadTwist は次のねじれまで値が残るので、値が変わった（新しいねじれが起きた）歩だけを見る。
            if (twist >= _t.HeadSnapTwist && twist != s.PreviousTwist)
            {
                Add(HighlightKind.HeadSnap, time, red, twist * twist / (_t.HeadSnapTwist * _t.HeadSnapTwist), $"頭のねじれ {twist:0} rad/s");
            }

            s.PreviousTwist = twist;
        }
    }

    /// <summary>
    /// この歩で消えたセルを選手ごとに数え（柔らかい = 皮膚・髪、硬い = それ以外）、部位の大きな欠け（VoxelLoss: 始めのセルの
    /// <see cref="ExcitementTuning.VoxelLossShare"/> を失うごとに 1 つ、強さ = 失った割合 / 閾値）を出す。
    /// </summary>
    private Dictionary<Fighter, (int Soft, int Hard)> Carved(BoxingGame game)
    {
        var result = new Dictionary<Fighter, (int Soft, int Hard)>();
        foreach (var f in new[] { game.Player, game.Cpu })
        {
            foreach (var body in HighlightSource.Grids(f))
            {
                var grid = body.Grid;
                var filled = grid.FilledCount;
                if (!_partCounts.TryGetValue(body, out var counts))
                {
                    _partCounts[body] = counts = (filled, filled);
                }
                else if (counts.Start > 0 && (counts.Last - filled) >= _t.VoxelLossShare * counts.Start)
                {
                    var share = (counts.Last - filled) / (float)counts.Start;
                    Add(HighlightKind.VoxelLoss, _last, ReferenceEquals(f, game.Player), share / _t.VoxelLossShare, $"部位のセルの {share * 100f:0}% が欠けた（{body.BodyId}）");
                    _partCounts[body] = (counts.Start, filled);
                }

                if (_snapshots.TryGetValue(body, out var snap) && snap.Version == grid.Version && snap.Cells.Length == grid.CellCount)
                {
                    continue;
                }

                var cells = new byte[grid.CellCount];
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = grid.AtIndex(i);
                }

                if (snap.Cells is { } old && old.Length == cells.Length)
                {
                    var (soft, hard) = result.GetValueOrDefault(f);
                    for (var i = 0; i < cells.Length; i++)
                    {
                        if (old[i] != 0 && cells[i] == 0)
                        {
                            if (old[i] is BoxerAnatomy.Skin or BoxerAnatomy.Hair)
                            {
                                soft++;
                            }
                            else
                            {
                                hard++;
                            }
                        }
                    }

                    result[f] = (soft, hard);
                }

                _snapshots[body] = (grid.Version, cells);
            }
        }

        return result;
    }

    /// <summary>
    /// ダウン（Knockdown: 審判が数え始めた瞬間。強さ = 直前の打撃のエネルギー / 基準、上限 1・下限 <see cref="ExcitementTuning.UnforcedKnockdown"/>）・立ち上がり（Rise: 数えられた後、同じラウンドの中で立った。強さ = 倒れていた秒 / 基準）・
    /// 一撃（OnePunch: ダウン・KO の前の窓にクリーンヒットが 1 つだけで、その直前の意識がほぼ満タン。強さ = その打撃の前の意識 − 今の意識）。
    /// </summary>
    private void Downs(BoxingGame game, Side s, bool red, float time)
    {
        var f = s.Fighter!;
        var down = f.IsKnockedDown;
        if (down && !s.Down)
        {
            s.DownSince = time;

            // 強さ = 倒れる前の窓に受けたクリーンヒットのエネルギーの合計 / 基準（上限 1）。打たれずに崩れたダウンは下限の値。
            var cause = s.HitsTaken.Where(h => time - h.Time <= _t.OnePunchWindow).Sum(h => h.Energy) / _t.EnergyRef;
            Add(HighlightKind.Knockdown, time, red, MathF.Max(_t.UnforcedKnockdown, MathF.Min(1f, cause)), $"意識 {f.Consciousness * 100f:0}%、直前の打撃 {cause * _t.EnergyRef:0.00} J");
            OnePunch(s, red, time, f.Consciousness);
        }
        else if (!down && s.Down && !f.IsDead && !game.RoundOver)
        {
            var seconds = time - s.DownSince;
            Add(HighlightKind.Rise, time, red, seconds / _t.RiseRefSeconds, $"{seconds:0.0} 秒倒れてから立った");
        }

        if (f.IsDead && !s.Dead)
        {
            if (!_deathKoPending)
            {
                _deathKoPending = true; // 続く勝ち数の増加は同じ KO
                Add(HighlightKind.KO, time, !red, 1f, "死");
            }

            OnePunch(s, red, time, 0f);
        }

        s.Down = down;
        s.Dead = f.IsDead;
        s.HitsTaken.RemoveAll(h => time - h.Time > _t.OnePunchWindow);
    }

    private void OnePunch(Side victim, bool victimRed, float time, float consciousnessNow)
    {
        var recent = victim.HitsTaken.Where(h => time - h.Time <= _t.OnePunchWindow).ToList();
        if (recent.Count == 1 && recent[0].ConsciousnessBefore >= _t.OnePunchConsciousness)
        {
            Add(HighlightKind.OnePunch, time, !victimRed, MathF.Max(0f, recent[0].ConsciousnessBefore - consciousnessNow),
                $"意識 {recent[0].ConsciousnessBefore * 100f:0}% → {consciousnessNow * 100f:0}% を 1 発で");
        }
        victim.HitsTaken.Clear(); // 同じ打撃でダウンと死の両方に数えない
    }

    /// <summary>KO（ラウンドを取った: 勝ち数が増えた）と、ダウンした側が後でラウンドを取った逆転。</summary>
    private void Wins(BoxingGame game, float time)
    {
        if (game.PlayerWins > _playerWins)
        {
            Won(true, time);
        }

        if (game.CpuWins > _cpuWins)
        {
            Won(false, time);
        }

        _playerWins = game.PlayerWins;
        _cpuWins = game.CpuWins;
    }

    private void Won(bool red, float time)
    {
        if (!_deathKoPending)
        {
            Add(HighlightKind.KO, time, red, 1f, "ラウンドを取った");
        }
        else
        {
            _deathKoPending = false;
        }
    }

    private void Add(HighlightKind kind, float time, bool red, float intensity, string note) =>
        _events.Add(new Highlight(kind, time, red, intensity, note));
}
