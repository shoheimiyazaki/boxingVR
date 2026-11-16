using System.Globalization;
using System.Numerics;
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
}

/// <summary>
/// リプレイの候補になる場面: 当たった歩・打たれた側・当たった所・見どころの重み（ダメージに、ダウン・骨折・切断を足す）・説明。
/// </summary>
internal sealed record TvHighlight(int Step, bool RedVictim, Vector3 Point, float Score, string Text, int Round);

/// <summary>休みの間の 1 つのカット（休みの始まりからの映像の秒）。</summary>
internal readonly record struct TvSegment(float Start, float End, TvShot Shot, bool Red, TvHighlight? Highlight);

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

    private readonly List<TvHighlight> _candidates = [];
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
            if (hit.Damage >= 8f)
            {
                _candidates.Add(new TvHighlight(step, hit.Victim == game.Player, hit.Point, hit.Damage,
                    string.Create(CultureInfo.InvariantCulture, $"{Name(hit.Attacker)}の{MiniRing.Audio.Broadcast.Strength(hit.Damage)}{(hit.Kick ? "蹴り" : hit.Hand == Hand.Left ? "左" : "右")}が{Name(hit.Victim)}の{MiniRing.Audio.Broadcast.ZoneName(hit.Zone)}に"), game.MatchRound));
            }
        }

        foreach (var hit in game.WeaponHits)
        {
            // 刃は数字のダメージが小さくても深く削る（削れたセルの数）ので、両方で重みを付ける。
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
            Boost(broken.Victim == game.Player, step, $"→ {part}骨折", broken.Position, game.MatchRound, name: Name(broken.Victim));
        }

        foreach (var sever in game.RecentSevers)
        {
            var victim = sever.Victim ?? (Vector3.DistanceSquared(sever.Position, game.Player.PelvisPosition) < Vector3.DistanceSquared(sever.Position, game.Cpu.PelvisPosition) ? game.Player : game.Cpu);
            var part = sever.Body >= 0 ? MiniRing.Audio.Broadcast.PartName(victim, sever.Body) : "";
            Boost(victim == game.Player, step, $"→ {part}切断", sever.Position, game.MatchRound, name: Name(victim));
        }
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
    /// 休みのカット割り（休みの始まりからの映像の秒）。いつものカメラ 2.5 秒 → コーナー → リプレイ → 反対のコーナー → リプレイ → コーナーを交互 → 最後の 2.5 秒はいつものカメラ。
    /// 先に映すコーナーは、そのラウンドで多く打たれた側。
    /// </summary>
    public IReadOnlyList<TvSegment> PlanBreak(float breakVideoSeconds, IReadOnlyList<TvHighlight> highlights, bool redFirst)
    {
        const float Lead = 2.5f;
        const float CornerShot = 3.5f;
        var plan = new List<TvSegment> { new(0f, Lead, TvShot.Main, redFirst, null) };
        var t = Lead;
        var red = redFirst;
        var end = breakVideoSeconds - Lead;
        foreach (var highlight in highlights)
        {
            if (t + CornerShot + ReplaySeconds > end)
            {
                break;
            }

            plan.Add(new TvSegment(t, t + CornerShot, TvShot.Corner, red, null));
            t += CornerShot;
            red = !red;
            plan.Add(new TvSegment(t, t + ReplaySeconds, TvShot.Replay, highlight.RedVictim, highlight));
            t += ReplaySeconds;
        }

        while (t < end - 0.5f)
        {
            var next = MathF.Min(end, t + CornerShot);
            plan.Add(new TvSegment(t, next, TvShot.Corner, red, null));
            red = !red;
            t = next;
        }

        plan.Add(new TvSegment(t, breakVideoSeconds + 5f, TvShot.Main, red, null));
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
