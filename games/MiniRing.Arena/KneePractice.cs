using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Arena.Evolution;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// kneelab 段 2（2026-11-17）: 当たる所（膝の位置・ねじり。段 1 の最適）を固定し、そこへ至る膝の通り道と、膝を開く拍子を、当たった瞬間に相手へ渡した運動量が
/// 最大になるように CMA-ES で磨く。調整に使わなかった間合い・脚で、始めの値（今の回し蹴り）と比べ直す。
/// </summary>
internal static class KneePractice
{
    private const float Dt = 1f / 240f;

    /// <summary>探す変数: 抱えの方位・仰角、途中の方位・仰角、抱えのねじり、当たる時刻、開き始めの先行、体の回転、胴のひねり、蹴りの長さ [s]。</summary>
    private static readonly (string Name, float Low, float High)[] Ranges =
    [
        ("抱えの方位", 0.6f, 1.6f), ("抱えの仰角", -0.1f, 0.7f), ("途中の方位", 0f, 1.4f), ("途中の仰角", -0.1f, 0.7f), ("抱えのねじり", -2f, 2f),
        ("当たる時刻", 0.35f, 0.7f), ("開く先行", 0.03f, 0.3f), ("体の回転", 0f, 1.5f), ("胴のひねり", 0f, 0.6f), ("長さ[s]", 0.5f, 1.2f),
    ];

    internal sealed record Genome(KneePath Path, float Duration, float FootPitch);

    internal sealed record Outcome(float Momentum, float Damage, float Energy, float Fraction, float Speed, float BendAtHit, float HitTime, bool Landed, bool Fell, KickPart Part, HitZone Zone);

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    internal static Genome Decode(IReadOnlyList<double> x, KneeLab.Spot impact, float footPitch)
    {
        float V(int i) => Ranges[i].Low + ((Ranges[i].High - Ranges[i].Low) * (float)Math.Clamp(x[i], 0.0, 1.0));
        var path = new KneePath(V(0), V(1), V(2), V(3), impact.Azimuth, impact.Elevation, impact.Twist, V(4), V(5), V(6), V(7), V(8));
        return new Genome(path, V(9), footPitch);
    }

    internal static double[] Encode(KneePath p, float duration)
    {
        double N(int i, float v) => (v - Ranges[i].Low) / (Ranges[i].High - Ranges[i].Low);
        return [N(0, p.ChamberAzimuth), N(1, p.ChamberElevation), N(2, p.MidAzimuth), N(3, p.MidElevation), N(4, p.ChamberTwist), N(5, p.ImpactShare), N(6, p.Lead), N(7, p.Spin), N(8, p.Torso), N(9, duration)];
    }

    /// <summary>1 本の蹴り。<paramref name="onStep"/> には毎歩（ゲーム・膝の目標のワールド位置・今の時刻）を渡す。</summary>
    internal static Outcome Kick(BodyBuild body, Genome g, Hand side, float standoff, int seed, Action<BoxingGame, Vector3?, float>? onStep = null)
    {
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
        var attacker = game.Player;
        var victim = game.Cpu;
        var reach = attacker.ArmReach;
        FighterCommand Command(Vector3 move, LegCommand? leg, float yaw, float twist)
        {
            var face = Vector3.Transform(victim.ChestPosition - attacker.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
            return new FighterCommand(move, face, new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
                TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
        }

        float Distance() => Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) / reach;

        var stable = 0;
        for (var i = 0; i < 240 * 10 && stable < 90; i++)
        {
            var error = Distance() - standoff;
            if (MathF.Abs(error) < 0.03f)
            {
                stable++;
                game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), Dt);
                continue;
            }

            stable = 0;
            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(Command(toward * MathF.Sign(error) * Math.Clamp(MathF.Abs(error) * 4f, 0.25f, 1f), null, 0f, 0f)), Dt);
        }

        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), Dt);
        }

        var spec = KickSpec.Of(KickTechnique.Roundhouse);
        var aimWorld = spec.AimWorld(game.PlayerSense);
        var side1 = side.LocalSide();
        HitEvent? first = null;
        var fraction = 0f;
        var bendAtHit = 0f;
        var hitTime = 0f;
        var steps = (int)((g.Duration + 0.3f) / Dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var share = t / g.Duration;
            var profile = KickSpec.TurnProfile(t, g.Duration);
            var yaw = share <= 1f ? -side1 * g.Path.Spin * profile : 0f;
            var twist = share <= 1f ? -side1 * g.Path.Torso * profile : 0f;
            var sense = game.PlayerSense;
            LegCommand? leg = share <= 1f ? g.Path.Evaluate(side, share, sense.Self.WorldToLocal(aimWorld), sense.SelfLeg(side), g.FootPitch) : null;
            game.Step(new BoxingInput(Command(Vector3.Zero, leg, yaw, twist)), Dt);
            onStep?.Invoke(game, leg?.KneeLocal is { } k ? attacker.Frame.LocalToWorld(k) : null, t);
            if (first is null)
            {
                foreach (var hit in game.RecentHits)
                {
                    if (hit.Kick && hit.Attacker == attacker)
                    {
                        first = hit;
                        var knee = attacker.KneePosition(side);
                        var axis = attacker.AnklePosition(side) - knee;
                        fraction = Vector3.Dot(hit.Point - knee, axis) / MathF.Max(1e-6f, axis.LengthSquared());
                        bendAtHit = attacker.KneeBend(side);
                        hitTime = t;
                        break;
                    }
                }
            }
        }

        var fell = attacker.State != FighterState.Standing;
        return first is { } h
            ? new Outcome(h.Momentum, h.Damage, h.Energy, fraction, h.Speed, bendAtHit, hitTime, true, fell, h.KickerPart, h.Zone)
            : new Outcome(0f, 0f, 0f, 0f, 0f, 0f, 0f, false, fell, KickPart.None, default);
    }

    /// <summary>始めの値（今の回し蹴り）に近い通り道。</summary>
    internal static Genome Baseline(KneeLab.Spot impact, float footPitch)
        => new(new KneePath(KickSpec.KneeChamberAzimuth, KickSpec.KneeChamberElevation, 0.6f, 0.4f, impact.Azimuth, impact.Elevation, impact.Twist, 0f, 0.53f, 0.25f, 1.0f, 0.6f), 0.9f, footPitch);

    public static void Stage2(string bodyId, KneeLab.Spot impact, float footPitch, IReadOnlyList<float> tuneStandoffs, IReadOnlyList<float> confirmStandoffs, int generations, int lambda,
        string? videoDir, Action<string> log)
    {
        var body = bodyId == "standard" ? BodyBuild.Standard : CharacterRoster.Get(bodyId).Build;
        log("# kneelab 段 2: 最適の所へ向かう膝の通り道と、開く拍子（運動量を最大に）");
        log("");
        log($"当たる所は段 1 の最適に固定（方位 {F(impact.Azimuth * 180f / MathF.PI, "0")}°・仰角 {F(impact.Elevation * 180f / MathF.PI, "0")}°・ねじり {F(impact.Twist)} rad）。体 {bodyId}、調整は左脚、間合い {string.Join(",", tuneStandoffs.Select(s => F(s)))}（目標。実測は足の運びで量子化される）、CMA-ES λ={lambda}・{generations} 世代。目的 = 最初に当たった打撃の運動量（質量 × 速さ。当たらない・倒れたは 0）の間合い平均。PhysicsVersion {Fighter.PhysicsVersion}。");
        log("");
        double Score(IReadOnlyList<double> x)
        {
            var g = Decode(x, impact, footPitch);
            var sum = 0.0;
            foreach (var s in tuneStandoffs)
            {
                var o = Kick(body, g, Hand.Left, s, 1);
                sum += o.Fell ? 0.0 : o.Momentum;
            }

            return sum / tuneStandoffs.Count;
        }

        var start = Encode(Baseline(impact, footPitch).Path, 0.9f);
        var cma = new CmaEs(start, seed: 7, initialSigma: 0.2, maximize: true, lambda: lambda, maxGenerations: generations);
        var best = (Score: Score(start), X: start);
        log($"始めの値の運動量（調整の間合い平均）: {F((float)best.Score)}");
        log("| 世代 | 世代の最良 | 平均点の運動量 | σ |");
        log("|---|---|---|---|");
        while (cma.Generation < generations)
        {
            var points = cma.Ask();
            var scores = points.Select(Score).ToArray();
            cma.Tell(scores);
            var i = Array.IndexOf(scores, scores.Max());
            if (scores[i] > best.Score)
            {
                best = (scores[i], points[i]);
            }

            log($"| {cma.Generation} | {F((float)scores.Max())} | {F((float)Score(cma.Best))} | {F((float)cma.Sigma, "0.000")} |");
        }

        var tuned = Decode(best.X, impact, footPitch);
        var baseline = Baseline(impact, footPitch);
        log("");
        log("## 磨いた通り道");
        log("| 変数 | 始めの値 | 磨いた値 |");
        log("|---|---|---|");
        string[] names = [.. Ranges.Select(r => r.Name)];
        var b = Encode(baseline.Path, baseline.Duration);
        for (var i = 0; i < names.Length; i++)
        {
            float V(IReadOnlyList<double> v) => Ranges[i].Low + ((Ranges[i].High - Ranges[i].Low) * (float)Math.Clamp(v[i], 0.0, 1.0));
            log($"| {names[i]} | {F(V(b))} | {F(V(best.X))} |");
        }

        log("");
        log("## 使っていない間合い・左右で比べ直し（調整に使わなかった条件）");
        log("| 条件 | 通り道 | 運動量 | ダメージ | エネルギー [J] | すねの点 | 当たった時の曲げ [rad] | 当たった時刻 [s] | 部位 | 相手の所 | 倒れた |");
        log("|---|---|---|---|---|---|---|---|---|---|---|");
        double[] sums = [0, 0];
        foreach (var (label, side, standoffs) in new[] { ("左脚・未使用の間合い", Hand.Left, confirmStandoffs), ("右脚・調整と同じ間合い", Hand.Right, tuneStandoffs), ("右脚・未使用の間合い", Hand.Right, confirmStandoffs) })
        {
            foreach (var s in standoffs)
            {
                var k = 0;
                foreach (var (name, g) in new[] { ("始め", baseline), ("磨いた", tuned) })
                {
                    var o = Kick(body, g, side, s, 1);
                    sums[k++] += o.Fell ? 0.0 : o.Momentum;
                    log($"| {label} {F(s)} | {name} | {F(o.Momentum)} | {F(o.Damage)} | {F(o.Energy)} | {F(o.Fraction)} | {F(o.BendAtHit)} | {F(o.HitTime)} | {o.Part} | {o.Zone} | {(o.Fell ? "倒れた" : "")} |");
                }
            }
        }

        log("");
        log($"比べ直しの運動量の合計: 始め {F((float)sums[0])}、磨いた {F((float)sums[1])}。");
        log("");
        log("## 値（KickSpec に書き込む形）");
        var p = tuned.Path;
        log($"`new KneePath({F(p.ChamberAzimuth, "0.###")}f, {F(p.ChamberElevation, "0.###")}f, {F(p.MidAzimuth, "0.###")}f, {F(p.MidElevation, "0.###")}f, {F(p.ImpactAzimuth, "0.###")}f, {F(p.ImpactElevation, "0.###")}f, {F(p.ImpactTwist, "0.###")}f, {F(p.ChamberTwist, "0.###")}f, {F(p.ImpactShare, "0.###")}f, {F(p.Lead, "0.###")}f, {F(p.Spin, "0.###")}f, {F(p.Torso, "0.###")}f)`、長さ {F(tuned.Duration, "0.###")} s、足の曲げ {F(tuned.FootPitch)}");
        if (videoDir is not null)
        {
            Directory.CreateDirectory(videoDir);
            Video(body, baseline, tuned, Path.Combine(videoDir, "stage2_roundhouse.mp4"), log);
        }
    }

    /// <summary>始めと磨いた蹴りを、真横と斜め前から 0.25 倍のスローで撮る。</summary>
    internal static void Video(BodyBuild body, Genome baseline, Genome tuned, string outPath, Action<string> log)
    {
        const int width = 1280, height = 720, fps = 30;
        const float slow = 0.25f;
        using var renderer = new HeadlessRenderer(width, height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        { RedirectStandardInput = true, UseShellExecute = false }) ?? throw new IOException("ffmpeg を起動できません。");
        foreach (var (name, g) in new[] { ("始めの値", baseline), ("磨いた", tuned) })
        {
            foreach (var oblique in new[] { false, true })
            {
                var next = 0f;
                var o = Kick(body, g, Hand.Left, 1.2f, 1, (game, goal, t) =>
                {
                    if (t < next)
                    {
                        return;
                    }

                    next = t + (slow / fps);
                    var lines = new List<DebugLine>();
                    if (goal is { } gp)
                    {
                        lines.Add(new DebugLine(game.Player.HipPosition(Hand.Left), gp, new Vector4(1f, 0.85f, 0.3f, 1f)));
                    }

                    KickVideo.Emit(renderer, ffmpeg, width, height, game, [
                        new($"{name}（{(oblique ? "斜め前" : "真横")}）  {slow:0.##} 倍スロー", new Vector2(40f, 40f), 28f, new Vector4(1f), TextAnchor.Left),
                        new($"膝の曲げ {game.Player.KneeBend(Hand.Left):0.00} rad  {t:0.00} s  金の線 = 膝の制御点", new Vector2(40f, 76f), 22f, new Vector4(1f, 0.85f, 0.3f, 1f), TextAnchor.Left),
                    ], lines, 0f, oblique);
                });
                log($"video {name} {(oblique ? "oblique" : "side")}: momentum {F(o.Momentum)} shin {F(o.Fraction)} bend at hit {F(o.BendAtHit)}");
            }
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }
}
