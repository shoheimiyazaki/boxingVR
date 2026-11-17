using System.Globalization;
using System.Numerics;
using Aigis.Physics.Voxels;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>中継のカメラの種類。</summary>
internal enum TvShot
{
    /// <summary>いつもの中継カメラ（リングの横から 2 人を見下ろす）。</summary>
    Main,

    /// <summary>天井のカメラ（倒れた選手を真上から）。</summary>
    Ceiling,

    /// <summary>リング脇のカメラマン（倒れた選手を床の高さから）。</summary>
    Ringside,

    /// <summary>休みの間、リングの中に入ったカメラマン（コーナーで休む選手の上半身を手持ちで）。</summary>
    Corner,

    /// <summary>ハイライトのリプレイ（自由なカメラ・スロー）。</summary>
    Replay,

    /// <summary>休みの間、リングの中のカメラマンが手持ちで選手の傷にズームで寄る（切り替えずに 1 カットで）。</summary>
    Wound,
}

/// <summary>
/// リプレイの候補になる場面: 当たった歩・打たれた側・当たった所・見どころの重み（ダメージに、ダウン・骨折・切断を足す）・説明。
/// </summary>
internal sealed record TvHighlight(int Step, bool RedVictim, Vector3 Point, float Score, string Text, int Round);

/// <summary>休みの間の 1 つのカット（休みの始まりからの映像の秒）。</summary>
internal readonly record struct TvSegment(float Start, float End, TvShot Shot, bool Red, TvHighlight? Highlight, TvWound? Wound = null);

/// <summary>
/// 傷のアップの候補: どちらの選手の、どの部位（ボクセルの体 <paramref name="Body"/>）の、格子の中のどの点（<paramref name="Grid"/>、格子の中心からの座標）に、
/// どれだけ重い傷か（打撃の重さ・削れたセル・骨折・切断の和）。部位に付いた点なので、倒れても腕が動いても今の位置を追える。
/// </summary>
internal sealed record TvWound(bool Red, VoxelBody Body, Vector3 Grid, string Name, float Score)
{
    /// <summary>
    /// 傷のいまの位置（世界）。点は、部位のセルが今残っている範囲の中へ寄せる（部位が切れると先の側のセルが空になるので、
    /// 切れた先を指したままだと何も無いマットを映した）。切れた腕なら切り口のあたりになる。
    /// </summary>
    public Vector3 Position
    {
        get
        {
            var grid = Grid;
            if (Body.Grid.OccupiedBounds() is { } kept)
            {
                grid = Vector3.Clamp(grid, kept.Center - kept.HalfExtents, kept.Center + kept.HalfExtents);
            }

            return Vector3.Transform(grid, Body.GridToWorld());
        }
    }
}

/// <summary>
/// テレビ中継の演出の頭（描画と音だけ。試合には触れない）: どのカメラで映すか、休みの間のカット割り、リプレイにする場面の選び方。
/// <list type="bullet">
/// <item>ラウンド中はいつもの中継カメラ。ダウンを取られたら一呼吸おいて、天井のカメラとリング脇のカメラマンを交互に。</item>
/// <item>休みの間は、いつものカメラ → リングの中のカメラマン（コーナーの選手）→ リプレイ → もう一方のコーナー → リプレイ → コーナー → いつものカメラ。</item>
/// <item>リプレイは、そのラウンドで重かった場面（ダメージ・ダウン・骨折・切断）を 2 つまで、時刻の順に。同じ試合をもう一度回した影の試合（決定的なので同じに進む）を
/// 1 歩ごとに 1 コマで描く（30 fps・1 歩 1/80 s なら実時間の 0.375 倍のスロー）。</item>
/// </list>
/// </summary>
internal sealed class TvDirector(int width, int height, int fps, float stepDt, string redName, string blueName, string? redGear = null, string? blueGear = null)
{
    /// <summary>リプレイにする最低の重み（これより軽い場面は流さない）。</summary>
    private const float MinScore = 12f;

    /// <summary>ダウン・骨折・切断の見どころの上乗せ（重い 1 発 2 つ分ほど）。</summary>
    private const float EventBonus = 40f;

    /// <summary>リプレイで当たる前に見せる長さ [試合の s]（構え・振りかぶり）。</summary>
    public const float ReplayBefore = 1.0f;

    /// <summary>リプレイで当たった後に見せる長さ [試合の s]（崩れ・飛ぶ破片）。</summary>
    public const float ReplayAfter = 0.7f;

    /// <summary>ダウンからカメラを切り替えるまでの間 [映像の s]（倒れる瞬間はいつものカメラで見せる）。</summary>
    private const float DownCutDelay = 0.8f;

    /// <summary>ダウン中の 1 カットの長さ [映像の s]。</summary>
    private const float DownShotSeconds = 2.6f;

    /// <summary>傷のアップにする最低の重み（打撃の重さ＋失ったセル。重い 1 発 3 つ分ほど。軽い傷には寄らない）。</summary>
    private const float WoundMinScore = 30f;

    /// <summary>傷のアップ 1 カットの長さ [映像の s]: 上半身から始めて傷へ振り、ズームで寄り切って少し止める。</summary>
    public const float WoundSeconds = 4.2f;

    private readonly List<TvHighlight> _candidates = [];
    private static readonly bool[] Corners = [true, false];
    /// <summary>部位ごとの傷: 重さの和と、重さで重み付けした格子の中の点の和。</summary>
    private readonly Dictionary<VoxelBody, (bool Red, string Name, float Score, Vector3 Sum)> _wounds = [];
    private readonly Dictionary<Fighter, bool> _wasDown = [];
    private float? _downSince;
    private bool _over;

    /// <summary>1 歩ごとに試合を見て、リプレイの候補を集める。</summary>
    public void Observe(BoxingGame game, int step)
    {
        ArgumentNullException.ThrowIfNull(game);
        string Name(Fighter f) => f == game.Player ? redName : blueName;
        foreach (var hit in game.RecentHits)
        {
            AddWound(hit.Victim == game.Player, hit.Victim, hit.Point, hit.Damage);
            if (hit.Damage >= 8f)
            {
                _candidates.Add(new TvHighlight(step, hit.Victim == game.Player, hit.Point, hit.Damage,
                    string.Create(CultureInfo.InvariantCulture, $"{Name(hit.Attacker)}の{MiniRing.Audio.Broadcast.Strength(hit.Damage)}{(hit.Elbow ? "肘" : hit.Kick ? "蹴り" : hit.Hand == Hand.Left ? "左" : "右")}が{Name(hit.Victim)}の{MiniRing.Audio.Broadcast.ZoneName(hit.Zone)}に"), game.MatchRound));
            }
        }

        foreach (var hit in game.WeaponHits)
        {
            // 刃は数字のダメージが小さくても深く削る（削れたセルの数）ので、両方で重みを付ける。
            AddWound(hit.Victim == game.Player, hit.Victim, hit.Point, hit.Damage + (0.25f * hit.Cells));
            if (hit.Damage >= 8f || hit.Cells >= 15)
            {
                _candidates.Add(new TvHighlight(step, hit.Victim == game.Player, hit.Point, hit.Damage + (0.25f * hit.Cells),
                    string.Create(CultureInfo.InvariantCulture, $"{Name(hit.Attacker)}の{(hit.Attacker == game.Player ? redGear : blueGear) ?? "武器"}が{Name(hit.Victim)}の{MiniRing.Audio.Broadcast.ZoneName(hit.Zone)}に"), game.MatchRound));
            }
        }

        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            var down = fighter.IsKnockedDown;
            if (down && !_wasDown.GetValueOrDefault(fighter))
            {
                // ダウンは打たれてから数秒後に取られることがある（中腰で待つ DownFaintTime 4 秒）ので、長めにさかのぼる。
                Boost(fighter == game.Player, step, "→ ダウン", fighter.ChestPosition, game.MatchRound, 4.5f, name: Name(fighter));
            }

            _wasDown[fighter] = down;
        }

        if (game.MatchOver && !_over)
        {
            _over = true;
            if (game.Decision is null && game.PlayerWins != game.CpuWins)
            {
                // K.O. は倒れてから数秒後（動かないまま 4 秒・10 カウント。倒れたまま休みを越えることもある）に決まるので、
                // 負けた側が最後に倒された一発（前のラウンドでも）に上乗せする。倒されずに決まった（臓器が壊れた）ときは直前の一撃に。
                var loserRed = game.CpuWins > game.PlayerWins;
                var cause = new TvResult(1, game.Message, 0, 0f, [], []).Cause;
                var what = cause.Length > 0 ? $"→ K.O.（{cause}）" : "→ K.O.";
                var felled = _candidates.FindLastIndex(c => c.RedVictim == loserRed && c.Text.Contains("ダウン", StringComparison.Ordinal));
                if (felled >= 0 && step - _candidates[felled].Step < (int)(40f / stepDt))
                {
                    _candidates[felled] = _candidates[felled] with { Score = _candidates[felled].Score + 80f, Text = _candidates[felled].Text + " " + what };
                }
                else
                {
                    Boost(loserRed, step, what, game.Player.ChestPosition, game.MatchRound, 4.5f, 80f, Name(loserRed ? game.Player : game.Cpu));
                }
            }
        }

        foreach (var broken in game.BrokenBones)
        {
            var part = broken.Body >= 0 ? MiniRing.Audio.Broadcast.PartName(broken.Victim, broken.Body) : "";
            AddWound(broken.Victim == game.Player, broken.Victim, broken.Position, EventBonus);
            Boost(broken.Victim == game.Player, step, $"→ {part}骨折", broken.Position, game.MatchRound, name: Name(broken.Victim));
        }

        foreach (var sever in game.RecentSevers)
        {
            var victim = sever.Victim ?? (Vector3.DistanceSquared(sever.Position, game.Player.PelvisPosition) < Vector3.DistanceSquared(sever.Position, game.Cpu.PelvisPosition) ? game.Player : game.Cpu);
            var part = sever.Body >= 0 ? MiniRing.Audio.Broadcast.PartName(victim, sever.Body) : "";
            AddWound(victim == game.Player, victim, sever.Position, 2f * EventBonus);
            Boost(victim == game.Player, step, $"→ {part}切断", sever.Position, game.MatchRound, name: Name(victim));
        }
    }

    /// <summary>
    /// 打たれた点に一番近い体の部位（体に付いたボクセルの部位）に、その重さと、その部位の格子の中での点を足す（傷のアップの候補）。
    /// 点を部位の格子に付けておくので、休みに倒れていても腕が動いていても、カメラは今の傷の位置を狙える（体の中心から測った推定の点は、
    /// 倒れた選手・切れて飛んだ足で外れた）。点は格子の箱の中へ寄せる（表面のすぐ外に当たった点を体の上に置く）。
    /// </summary>
    private void AddWound(bool red, Fighter victim, Vector3 point, float score)
    {
        if (victim.Voxels is not { } voxels || voxels.Parts.Count == 0)
        {
            return;
        }

        static float Outside(VoxelBody body, Vector3 world)
        {
            var local = Vector3.Abs(body.WorldToGrid(world)) - body.Grid.HalfExtents;
            return Vector3.Max(local, Vector3.Zero).LengthSquared();
        }

        var part = voxels.Parts.MinBy(p => Outside(p.Body, point))!;
        var half = part.Body.Grid.HalfExtents;
        var grid = Vector3.Clamp(part.Body.WorldToGrid(point), -half, half) * 0.8f;
        (bool Red, string Name, float Score, Vector3 Sum) old = _wounds.GetValueOrDefault(part.Body, (red, MiniRing.Audio.Broadcast.PartName(victim, part.Body.BodyId), 0f, Vector3.Zero));
        _wounds[part.Body] = (old.Red, old.Name, old.Score + score, old.Sum + (grid * score));
    }

    /// <summary>ここまでの試合で、選手ごとの一番重い傷（重い順。軽い傷しか無い選手は入れない）。</summary>
    /// <remarks>
    /// 傷の重さ = その部位に当たった打撃の重さ（打撲は皮膚のあざの色で外から見える）＋ 部位が始めから失ったセルの数（削れた・切れて落ちた）。
    /// 2026-11-17: いったん「内部の傷は見えないので外傷だけ」にしたが、内部の傷を皮膚の色（赤い腫れ・青紫・まだら）で見せるようにしたので、
    /// 打撲も数える形へ戻した（ユーザーの指定）。寄る点は、その部位に当たった打撃の点を重さで重み付けした平均。
    /// </remarks>
    public IReadOnlyList<TvWound> Wounds() =>
        [.. Corners
            .Select(red => _wounds.Where(w => w.Value.Red == red)
                .Select(w => new TvWound(red, w.Key, w.Value.Score > 0f ? w.Value.Sum / w.Value.Score : Vector3.Zero, w.Value.Name, w.Value.Score + LostCells(w.Key.Grid)))
                .OrderByDescending(w => w.Score).FirstOrDefault())
            .OfType<TvWound>()
            .Where(w => w.Score >= WoundMinScore)
            .OrderByDescending(w => w.Score)];

    /// <summary>格子が始めから失ったセルの数（すべての材質の、始めの数 − 残りの数）。</summary>
    private static int LostCells(Aigis.Foundation.Voxels.VoxelGrid grid)
    {
        var lost = 0;
        for (var m = 1; m < grid.Palette.Count; m++)
        {
            lost += Math.Max(0, grid.Initial((byte)m) - grid.Remaining((byte)m));
        }

        return lost;
    }

    /// <summary>直前の 1.5 秒のうち同じ側が打たれた一番新しい場面に上乗せする（無ければその歩を場面にする）。</summary>
    private void Boost(bool redVictim, int step, string what, Vector3 point, int round, float lookBackSeconds = 1.5f, float bonus = EventBonus, string name = "")
    {
        var lookBack = (int)(lookBackSeconds / stepDt);
        for (var i = _candidates.Count - 1; i >= 0; i--)
        {
            var c = _candidates[i];
            if (c.Step < step - lookBack)
            {
                break;
            }

            if (c.RedVictim == redVictim)
            {
                _candidates[i] = c with { Score = c.Score + bonus, Text = c.Text + " " + what };
                return;
            }
        }

        _candidates.Add(new TvHighlight(step, redVictim, point, bonus, $"{name} {what.TrimStart('→', ' ')}".Trim(), round));
    }

    /// <summary>決着のリプレイの場面: K.O. を上乗せした場面（倒した一撃）。影の試合が通り過ぎていたら無し。</summary>
    public TvHighlight? Finale(int firstStep)
    {
        var before = (int)(ReplayBefore / stepDt);
        var finale = _candidates.LastOrDefault(c => c.Text.Contains("K.O.", StringComparison.Ordinal));
        return finale is not null && finale.Step - before >= firstStep ? finale : null;
    }

    /// <summary>
    /// そのラウンドのリプレイ: 重い順に、窓が重ならないものを <paramref name="count"/> まで選び、時刻の順に返す。
    /// <paramref name="firstStep"/> より前は影の試合がもう通り過ぎたので選ばない。
    /// </summary>
    public IReadOnlyList<TvHighlight> Highlights(int round, int count, int firstStep)
    {
        var before = (int)(ReplayBefore / stepDt);
        var window = before + (int)(ReplayAfter / stepDt);
        var chosen = new List<TvHighlight>();
        foreach (var c in _candidates.Where(c => c.Round == round && c.Score >= MinScore && c.Step - before >= firstStep).OrderByDescending(c => c.Score))
        {
            if (chosen.Count < count && chosen.All(o => Math.Abs(o.Step - c.Step) > window))
            {
                chosen.Add(c);
            }
        }

        return [.. chosen.OrderBy(c => c.Step)];
    }

    /// <summary>リプレイ 1 本の映像の長さ [s]（1 歩 1 コマ）。</summary>
    public float ReplaySeconds => ((ReplayBefore + ReplayAfter) / stepDt) / fps;

    /// <summary>
    /// 休みのカット割り（休みの始まりからの映像の秒）: いつものカメラ 2.5 秒 → 多く打たれた側のコーナー → リプレイを続けて（2〜3 本。間は金の帯のワイプ）
    /// → コーナー・リング脇（低い所から）・天井（真上から）を約 2.6 秒ずつ回す → 最後の 2.5 秒はいつものカメラ。
    /// リプレイの間に選手の顔（コーナー）を挟むと流れが切れて見えた（2026-11-17、ユーザーの指摘）ので続けて流し、休みの残りでカメラを多く切り替える。
    /// </summary>
    public IReadOnlyList<TvSegment> PlanBreak(float breakVideoSeconds, IReadOnlyList<TvHighlight> highlights, bool redFirst, IReadOnlyList<TvWound>? wounds = null)
    {
        const float Lead = 2.5f;
        const float CornerShot = 3f;
        const float Cut = 2.6f;
        var plan = new List<TvSegment> { new(0f, Lead, TvShot.Main, redFirst, null) };
        var t = Lead;
        var end = breakVideoSeconds - Lead;
        plan.Add(new TvSegment(t, MathF.Min(end, t + CornerShot), TvShot.Corner, redFirst, null));
        t = MathF.Min(end, t + CornerShot);
        foreach (var highlight in highlights)
        {
            if (t + ReplaySeconds > end)
            {
                break;
            }

            plan.Add(new TvSegment(t, t + ReplaySeconds, TvShot.Replay, highlight.RedVictim, highlight));
            t += ReplaySeconds;
        }

        // リプレイの後に、傷のアップ（重い傷の選手から。手持ちで寄る 1 カット。2026-11-17、ユーザーの指定）。
        foreach (var wound in wounds ?? [])
        {
            if (t + WoundSeconds > end)
            {
                break;
            }

            plan.Add(new TvSegment(t, t + WoundSeconds, TvShot.Wound, wound.Red, null, wound));
            t += WoundSeconds;
        }

        // 休みの残り: 2 人を交互に、カメラの種類を回す（同じ絵を続けない）。
        var rotation = new (TvShot Shot, bool Red)[]
        {
            (TvShot.Corner, !redFirst), (TvShot.Ringside, redFirst), (TvShot.Ceiling, !redFirst),
            (TvShot.Corner, redFirst), (TvShot.Ringside, !redFirst), (TvShot.Ceiling, redFirst),
        };
        var k = 0;
        while (t < end - 0.5f)
        {
            var next = MathF.Min(end, t + Cut);
            var (shot, red) = rotation[k++ % rotation.Length];
            plan.Add(new TvSegment(t, next, shot, red, null));
            t = next;
        }

        plan.Add(new TvSegment(t, breakVideoSeconds + 5f, TvShot.Main, redFirst, null));
        return plan;
    }

    /// <summary>ラウンド中・決着の後のカメラ: ダウンを取られた選手がいれば、一呼吸おいて天井とリング脇を交互に。</summary>
    public (TvShot Shot, Fighter? Subject) Live(BoxingGame game, float videoTime)
    {
        var downed = game.Player.IsKnockedDown ? game.Player : game.Cpu.IsKnockedDown ? game.Cpu : null;
        if (game.MatchOver && downed is null)
        {
            downed = game.PlayerWins > game.CpuWins ? game.Cpu : game.CpuWins > game.PlayerWins ? game.Player : null;
        }

        if (downed is null || game.InBreak)
        {
            _downSince = null;
            return (TvShot.Main, null);
        }

        _downSince ??= videoTime;
        var since = videoTime - _downSince.Value - DownCutDelay;
        if (since < 0f)
        {
            return (TvShot.Main, null);
        }

        return (((int)(since / DownShotSeconds)) % 2 == 0 ? TvShot.Ceiling : TvShot.Ringside, downed);
    }

    /// <summary>天井のカメラ: 倒れた選手の腰の真上から見下ろし、ゆっくり寄る。</summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Ceiling(BoxingGame game, Fighter subject, float shotTime)
    {
        var target = (subject.PelvisPosition + subject.ChestPosition) * 0.5f;
        var axis = game.Cpu.PelvisPosition - game.Player.PelvisPosition;
        axis.Y = 0f;
        var up = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(axis) : Vector3.UnitZ;
        var eye = target + new Vector3(0f, 0.9f - (0.06f * shotTime), 0f);
        return (Camera.ViewProjection(eye, target, up, 40f * MathF.PI / 180f, Aspect, 0.02f, 30f), eye);
    }

    /// <summary>
    /// リング脇のカメラマン: 倒れた選手を、リングの内側の床の高さから見る（選手はロープ際に倒れることが多いので、外からでは体の中に入る）。
    /// 選手の大きさ（頭と胸の間）に比例した距離で、ゆっくり横へ回る。
    /// </summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Ringside(BoxingGame game, Fighter subject, float shotTime)
    {
        var center = game.Layout.Center;
        var target = Vector3.Lerp(subject.PelvisPosition, subject.HeadPosition, 0.6f);
        var inward = new Vector3(center.X - target.X, 0f, center.Z - target.Z);
        inward = inward.LengthSquared() > 1e-4f ? Vector3.Normalize(inward) : Vector3.UnitX;
        // 相手の選手が手前をふさがない側へ回る（左右のうち相手の胸から遠い方）。
        var opponent = subject == game.Player ? game.Cpu : game.Player;
        var swing = 0.6f - (0.1f * shotTime);
        var left = target + (Vector3.Transform(inward, Quaternion.CreateFromAxisAngle(Vector3.UnitY, swing)) * BodyScale(subject) * 6f);
        var right = target + (Vector3.Transform(inward, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -swing)) * BodyScale(subject) * 6f);
        var eye = Vector3.DistanceSquared(left, opponent.ChestPosition) >= Vector3.DistanceSquared(right, opponent.ChestPosition) ? left : right;
        eye.Y = subject.PelvisPosition.Y + (BodyScale(subject) * 0.9f);
        return (Camera.ViewProjection(eye, target, Vector3.UnitY, 38f * MathF.PI / 180f, Aspect, 0.01f, 30f), eye);
    }

    /// <summary>選手の大きさの目安 [m]: 頭と胸の中心の間（体格が大きいほど長い）。カメラの距離をこれに比例させる。</summary>
    private static float BodyScale(Fighter f) => MathF.Max(0.03f, Vector3.Distance(f.HeadPosition, f.ChestPosition));

    /// <summary>リングの中のカメラマン（休み）: コーナーで休む選手の正面、胸の高さから手持ちで上半身を撮り、少しずつ寄る。</summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Corner(BoxingGame game, Fighter subject, float shotTime, float videoTime)
    {
        var center = game.Layout.Center;
        var chest = subject.ChestPosition;
        var inward = new Vector3(center.X - chest.X, 0f, center.Z - chest.Z);
        inward = inward.LengthSquared() > 1e-4f ? Vector3.Normalize(inward) : Vector3.UnitZ;
        var side = Vector3.Cross(Vector3.UnitY, inward);
        var distance = BodyScale(subject) * (5.2f - (0.4f * shotTime));
        // 手持ちの揺れ（決まった正弦の和。見た目だけ）。
        var sway = (side * (0.008f * MathF.Sin(1.3f * videoTime))) + new Vector3(0f, 0.006f * MathF.Sin((2.1f * videoTime) + 1f), 0f);
        var eye = chest + (inward * distance) + (side * 0.08f) + new Vector3(0f, 0.04f, 0f) + sway;
        var target = Vector3.Lerp(chest, subject.HeadPosition, 0.6f);
        return (Camera.ViewProjection(eye, target, Vector3.UnitY, 45f * MathF.PI / 180f, Aspect, 0.01f, 30f), eye);
    }

    /// <summary>
    /// 傷のアップ（休み）: リングの中のカメラマンが選手の正面に立ち、手持ちで上半身を映してから傷へ振り、ズームで寄る（切り替えない 1 カット）。
    /// 寄るほど手の揺れが大きく見えるよう、揺れは狙う点の方を動かす（見た目だけ、決まった正弦の和）。
    /// </summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Wound(BoxingGame game, Fighter subject, TvWound wound, float shotTime, float videoTime)
    {
        static float Ease(float x) => x * x * (3f - (2f * x));
        var center = game.Layout.Center;
        var at = wound.Position;
        var scale = BodyScale(subject);
        // カメラは傷から測って置く（胸から測ると、倒れたままの選手ではマットの中に潜った）: 傷からリングの中央の方へ下がり、
        // 傷より上・足首より十分上から見下ろす。ゆっくり半歩前へ出る。
        var inward = new Vector3(center.X - at.X, 0f, center.Z - at.Z);
        inward = inward.LengthSquared() > 1e-4f ? Vector3.Normalize(inward) : Vector3.UnitZ;
        var side = Vector3.Cross(Vector3.UnitY, inward);
        var floor = MathF.Min(subject.AnklePosition(Hand.Left).Y, subject.AnklePosition(Hand.Right).Y);
        var back = scale * (4.4f - (0.5f * Ease(Math.Clamp(shotTime / WoundSeconds, 0f, 1f))));
        var eye = at + (inward * back) + (side * scale * 0.6f);
        eye.Y = MathF.Max(at.Y + (scale * 1.4f), floor + (scale * 1.6f));
        eye += (side * (0.006f * MathF.Sin(1.3f * videoTime))) + new Vector3(0f, 0.005f * MathF.Sin((2.1f * videoTime) + 1f), 0f);
        // 体全体（胸と腰の間）→ 傷へ振る（0.4〜1.3 秒）、ズームは 1.0〜3.0 秒で 46° → 12°（少し行き過ぎて戻る）。
        var pan = Ease(Math.Clamp((shotTime - 0.4f) / 0.9f, 0f, 1f));
        var zoom = Ease(Math.Clamp((shotTime - 1.0f) / 2.0f, 0f, 1f));
        var settle = shotTime > 3.0f ? 0.06f * MathF.Exp(-3f * (shotTime - 3.0f)) * MathF.Sin(9f * (shotTime - 3.0f)) : 0f;
        var fov = (46f + ((12f - 46f) * zoom)) * (1f + settle);
        var distance = Vector3.Distance(eye, at);
        var shake = ((side * MathF.Sin(3.1f * videoTime)) + (Vector3.UnitY * MathF.Sin((2.3f * videoTime) + 0.7f))) * (0.004f * distance);
        var body = Vector3.Lerp(subject.ChestPosition, subject.PelvisPosition, 0.3f);
        var target = Vector3.Lerp(body, at, pan) + shake;
        return (Camera.ViewProjection(eye, target, Vector3.UnitY, fov * MathF.PI / 180f, Aspect, 0.01f, 30f), eye);
    }

    /// <summary>リプレイのカメラ: 当たった所を中心に、打った側と打たれた側を結ぶ線の横から回り込む（ダメージの所が見える近さ）。</summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Replay(BoxingGame replay, TvHighlight highlight, float progress)
    {
        var victim = highlight.RedVictim ? replay.Player : replay.Cpu;
        var attacker = highlight.RedVictim ? replay.Cpu : replay.Player;
        var focus = Vector3.Lerp(victim.ChestPosition, highlight.Point, 0.5f);
        var axis = victim.ChestPosition - attacker.ChestPosition;
        axis.Y = 0f;
        axis = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(axis) : Vector3.UnitX;
        var side = Vector3.Cross(axis, Vector3.UnitY);
        var angle = -0.9f + (1.4f * progress);
        var direction = Vector3.Transform(side, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        var eye = focus + (direction * 0.5f) + new Vector3(0f, 0.1f, 0f);
        return (Camera.ViewProjection(eye, focus, Vector3.UnitY, 40f * MathF.PI / 180f, Aspect, 0.01f, 30f), eye);
    }

    private float Aspect => (float)width / height;
}
