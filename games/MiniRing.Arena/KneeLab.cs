using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 回し蹴りの段階練習（kneelab、2026-11-17）。段 1: 膝を空中の決めた所（相手に対する向き × 高さ）・決めたねじり角に止め、閉じたまま落ち着かせてから、
/// 膝を伸ばすだけで蹴る（体の向き・胴のひねりは固定。膝から先だけの蹴り）。最初に相手へ当たった打撃の、ダメージ・運動量・エネルギー・当たったすねの点・
/// 相手の当たった所を表にし、ダメージが大きい膝の位置とねじり角を探す。
/// </summary>
internal static class KneeLab
{
    private const float Dt = 1f / 240f;

    /// <summary>膝を閉じたままにする曲げ [rad]（上限 130° の近く）。</summary>
    internal const float Closed = 2.1f;

    internal sealed record Spot(float Azimuth, float Elevation, float Twist);

    internal sealed record Result(Spot Spot, Hand Side, float Distance, bool Fell, bool Landed, float Damage, float Momentum, float Energy, float Fraction, float Speed, HitZone Zone, KickPart Part, int Cells, float Settle);

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>膝の位置の目標（体フレーム）: 股から腿の長さで、前（相手の方）から外へ <paramref name="azimuth"/>、水平から上へ <paramref name="elevation"/>。</summary>
    internal static Vector3 KneeAt(LegBody leg, Hand side, float azimuth, float elevation)
        => leg.Hip + (leg.Thigh * Vector3.Normalize(new Vector3(side.LocalSide() * MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(azimuth) * MathF.Cos(elevation))));

    /// <summary>1 回の試し。<paramref name="onStep"/> には毎歩（ゲーム・段階の名前・膝の目標のワールド位置）を渡す（動画用）。</summary>
    internal static Result Trial(BodyBuild body, Spot spot, Hand side, float standoff, int seed, Action<BoxingGame, string, Vector3?>? onStep = null)
    {
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
        var attacker = game.Player;
        var victim = game.Cpu;
        var reach = attacker.ArmReach;
        FighterCommand Command(Vector3 move, LegCommand? leg) => new(move, victim.ChestPosition - attacker.ChestPosition,
            new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
            LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);

        float Distance() => Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) / reach;

        var stable = 0;
        for (var i = 0; i < 4028 && stable < 151; i++)
        {
            var error = Distance() - standoff;
            if (MathF.Abs(error) < 0.03f)
            {
                stable++;
                game.Step(new BoxingInput(Command(Vector3.Zero, null)), Dt);
                continue;
            }

            stable = 0;
            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(Command(toward * MathF.Sign(error) * Math.Clamp(MathF.Abs(error) * 4f, 0.25f, 1f), null)), Dt);
        }

        for (var i = 0; i < 201; i++)
        {
            game.Step(new BoxingInput(Command(Vector3.Zero, null)), Dt);
        }

        var distance = Distance();
        var legBody = game.PlayerSense.SelfLeg(side);
        var knee = KneeAt(legBody, side, spot.Azimuth, spot.Elevation);
        var foot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, KickSpec.Of(KickTechnique.Roundhouse).FootPitch);
        var before = Cells(victim);

        // 閉じたまま落ち着かせる（膝の位置の誤差と速さが小さくなるまで。上限 2.0 秒）。
        var settle = 0f;
        for (var i = 0; i < 483; i++)
        {
            var closed = LegCommand.Knee(knee, spot.Twist, Closed, foot);
            game.Step(new BoxingInput(Command(Vector3.Zero, closed)), Dt);
            var goal = attacker.Frame.LocalToWorld(knee);
            onStep?.Invoke(game, "閉じて落ち着く", goal);
            settle = i * Dt;
            if (i > 121 && Vector3.Distance(attacker.KneePosition(side), goal) < 0.0338f)
            {
                break;
            }
        }

        if (attacker.State != FighterState.Standing)
        {
            return new Result(spot, side, distance, true, false, 0f, 0f, 0f, 0f, 0f, default, KickPart.None, 0, settle);
        }

        // 伸ばす（膝の目標・ねじり・体は動かさず、曲げだけ 0 へ）。
        HitEvent? first = null;
        var fraction = 0f;
        for (var i = 0; i < 403; i++)
        {
            var open = LegCommand.Knee(knee, spot.Twist, 0f, foot);
            game.Step(new BoxingInput(Command(Vector3.Zero, open)), Dt);
            onStep?.Invoke(game, "伸ばす", attacker.Frame.LocalToWorld(knee));
            if (first is null)
            {
                foreach (var hit in game.RecentHits)
                {
                    if (hit.Kick && hit.Attacker == attacker)
                    {
                        first = hit;
                        var k = attacker.KneePosition(side);
                        var axis = attacker.AnklePosition(side) - k;
                        fraction = Vector3.Dot(hit.Point - k, axis) / MathF.Max(1e-6f, axis.LengthSquared());
                        break;
                    }
                }
            }
        }

        var lost = before - Cells(victim);
        var fell = attacker.State != FighterState.Standing;
        return first is { } h
            ? new Result(spot, side, distance, fell, true, h.Damage, h.Momentum, h.Energy, fraction, h.Speed, h.Zone, h.KickerPart, lost, settle)
            : new Result(spot, side, distance, fell, false, 0f, 0f, 0f, 0f, 0f, default, KickPart.None, lost, settle);
    }

    private static int Cells(Fighter f) => f.Voxels!.Parts.Concat(f.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);

    /// <summary>段 1 の 1 つの試しを真横から 0.25 倍のスローで撮る（金の線 = 膝の制御点）。</summary>
    public static void Stage1Video(string bodyId, Spot spot, Hand side, float standoff, string outPath, Action<string> log)
    {
        const int width = 1280, height = 720, fps = 30;
        const float slow = 0.25f;
        var body = bodyId == "standard" ? BodyBuild.Standard : CharacterRoster.Get(bodyId).Build;
        using var renderer = new Aigis.Render.HeadlessRenderer(width, height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        { RedirectStandardInput = true, UseShellExecute = false }) ?? throw new IOException("ffmpeg を起動できません。");
        var frame = 0;
        var r = Trial(body, spot, side, standoff, 1, (game, phase, goal) =>
        {
            // 落ち着く間は 4 歩に 1 コマ、伸ばす間は 0.25 倍スローで撮る。
            var every = phase == "伸ばす" ? (int)MathF.Round(1f / (Dt * fps / slow)) : 24;
            if (frame++ % Math.Max(1, phase == "伸ばす" ? 2 : 24) != 0)
            {
                return;
            }

            _ = every;
            var lines = new List<Aigis.Render.DebugLine>();
            if (goal is { } gp)
            {
                lines.Add(new Aigis.Render.DebugLine(game.Player.HipPosition(side), gp, new Vector4(1f, 0.85f, 0.3f, 1f)));
            }

            KickVideo.Emit(renderer, ffmpeg, width, height, game, [
                new($"段 1: 膝を止めて伸ばすだけ（{phase}）  方位 {spot.Azimuth * 180f / MathF.PI:0}° 仰角 {spot.Elevation * 180f / MathF.PI:0}° ねじり {spot.Twist:0.00}", new Vector2(40f, 40f), 26f, new Vector4(1f), Aigis.Render.TextAnchor.Left),
                new($"膝の曲げ {game.Player.KneeBend(side):0.00} rad  金の線 = 膝の制御点", new Vector2(40f, 76f), 22f, new Vector4(1f, 0.85f, 0.3f, 1f), Aigis.Render.TextAnchor.Left),
            ], lines, 0f);
        });
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        log($"stage1 video: distance {F(r.Distance)} damage {F(r.Damage)} momentum {F(r.Momentum)} energy {F(r.Energy)} shin {F(r.Fraction)} zone {r.Zone} part {r.Part}");
    }

    public static void Stage1(IReadOnlyList<string> bodies, IReadOnlyList<float> standoffs, IReadOnlyList<float> azimuths, IReadOnlyList<float> elevations,
        IReadOnlyList<float> twists, IReadOnlyList<Hand> sides, Action<string> log)
    {
        log("# kneelab 段 1: 膝を空中に止めて、伸ばすだけで蹴る");
        log("");
        log($"膝を閉じたまま（曲げ {F(Closed)} rad）、股から腿の長さの所（相手の方から外へ `方位`、水平から上へ `仰角`）・ねじり角 `ねじり` に止めて落ち着かせ、膝を 0 rad へ伸ばす。体の向き・胴のひねりは固定。PhysicsVersion {Fighter.PhysicsVersion}。");
        log("相手は構えて立つだけ（`StandGuard`）。最初に相手へ当たった打撃を記録: ダメージ（`HitEvent.Damage`）・運動量（質量 × 速さ）・エネルギー・当たったすねの点（0 = 膝、1 = 足首、1 超 = 足の甲・先）・相手の当たった所・足の部位。`削れ` = その試しで相手のセルが減った数。");
        log("");
        var all = new List<(string Body, Result R)>();
        foreach (var bodyId in bodies)
        {
            var body = bodyId == "standard" ? BodyBuild.Standard : CharacterRoster.Get(bodyId).Build;
            foreach (var standoff in standoffs)
            {
                foreach (var side in sides)
                {
                    foreach (var az in azimuths)
                    {
                        foreach (var el in elevations)
                        {
                            foreach (var tw in twists)
                            {
                                var r = Trial(body, new Spot(az, el, tw), side, standoff, seed: 1);
                                all.Add((bodyId, r));
                            }
                        }
                    }
                }
            }
        }

        foreach (var bodyGroup in all.GroupBy(a => a.Body))
        {
            // 左右の脚・間合いをならした、膝の位置とねじりごとのダメージ（片脚だけの偶然を避ける）。
            log($"## {bodyGroup.Key}: 左右の脚・間合いをならした順位（ダメージの平均。倒れた・当たらないは 0 として平均）");
            log("| 方位 [°] | 仰角 [°] | ねじり [rad] | 平均ダメージ | 平均運動量 | 平均エネルギー [J] | 当たった割合 | 当たったすねの点の平均 | 倒れた |");
            log("|---|---|---|---|---|---|---|---|---|");
            foreach (var g in bodyGroup.GroupBy(a => a.R.Spot).OrderByDescending(g => g.Average(a => a.R.Damage)).Take(10))
            {
                var landed = g.Where(a => a.R.Landed).ToList();
                log($"| {F(g.Key.Azimuth * 180f / MathF.PI, "0")} | {F(g.Key.Elevation * 180f / MathF.PI, "0")} | {F(g.Key.Twist)} | {F(g.Average(a => a.R.Damage))} | {F(g.Average(a => a.R.Momentum))} | {F(g.Average(a => a.R.Energy))} | {landed.Count}/{g.Count()} | {(landed.Count > 0 ? F(landed.Average(a => a.R.Fraction)) : "-")} | {g.Count(a => a.R.Fell)} |");
            }

            log("");
        }

        foreach (var group in all.GroupBy(a => (a.Body, a.R.Side)))
        {
            log($"## {group.Key.Body}・{group.Key.Side} 脚（試し {group.Count()}、倒れた {group.Count(g => g.R.Fell)}、当たらなかった {group.Count(g => !g.R.Fell && !g.R.Landed)}）");
            log("| 実測の間合い | 方位 [°] | 仰角 [°] | ねじり [rad] | ダメージ | 運動量 | エネルギー [J] | すねの点 | 足の速さ [m/s] | 相手の所 | 足の部位 | 削れ | 倒れた |");
            log("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var (_, r) in group.OrderByDescending(g => g.R.Damage).Take(12))
            {
                log($"| {F(r.Distance)} | {F(r.Spot.Azimuth * 180f / MathF.PI, "0")} | {F(r.Spot.Elevation * 180f / MathF.PI, "0")} | {F(r.Spot.Twist)} | {F(r.Damage)} | {F(r.Momentum)} | {F(r.Energy)} | {F(r.Fraction)} | {F(r.Speed)} | {r.Zone} | {r.Part} | {r.Cells} | {(r.Fell ? "倒れた" : "")} |");
            }

            log("");
        }
    }
}
