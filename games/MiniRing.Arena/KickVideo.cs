using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 膝の制御の回し蹴りの動画（kickvideo、2026-11-17）: 蹴る人（名簿のテコンドー家の体）が、立つだけの相手へ、間合いを変えて回し蹴りを 1 本ずつ蹴る。
/// 真横から撮り、金の線は膝の制御点（股 → 膝の目標）、画面の隅に膝の曲げ・ねじりと、当たった点（膝から足首の割合）・渡したエネルギーを出す。
/// 試合と同じ道（<see cref="KickSpec.Evaluate"/> → <see cref="FighterCommand"/> → 体）で、動画のためだけの姿勢は書かない。
/// </summary>
internal static class KickVideo
{
    private const float Dt = 1f / 240f;
    private const float TitleSeconds = 1.2f;
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);

    public static void Make(string bodyId, KickTechnique kind, IReadOnlyList<float> distances, Hand side, float slow, int width, int height, int fps, string outPath, TextWriter log)
    {
        var body = bodyId == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(bodyId).Build;
        var spec = KickSpec.Of(kind);
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        foreach (var distance in distances)
        {
            Shot(renderer, ffmpeg, body, spec, distance, side, slow, width, height, fps, log);
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }
    }

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    private static void Shot(HeadlessRenderer renderer, Process ffmpeg, BodyBuild body, KickSpec spec, float standoff, Hand side, float slow, int width, int height, int fps, TextWriter log)
    {
        using var game = new BoxingGame(seed: 2, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
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

        var actual = Distance();
        var title = $"回し蹴り（膝の制御）  間合い {F(actual)}（腕の長さ倍）  {(side == Hand.Left ? "左脚" : "右脚")}";
        log.WriteLine($"shot: standoff {F(standoff)} -> actual {F(actual)}");
        for (var k = 0f; k < TitleSeconds; k += 1f / fps)
        {
            Emit(renderer, ffmpeg, width, height, game, [
                new("膝の制御の回し蹴り", new Vector2(width * 0.5f, height * 0.3f), 34f, Grey, TextAnchor.Center),
                new(title, new Vector2(width * 0.5f, height * 0.46f), 40f, White, TextAnchor.Center),
                new($"{F(slow, "0.##")} 倍スロー", new Vector2(width * 0.5f, height * 0.6f), 28f, Gold, TextAnchor.Center),
            ], [], 0.55f);
        }

        var aimWorld = spec.AimWorld(game.PlayerSense);
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var duration = BotCatalog.Taekwondo.KickDuration;
        var steps = (int)((duration + 0.6f) / Dt);
        var nextFrame = 0f;
        var simPerFrame = slow / fps;
        var lastHit = "";
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var yaw = t <= duration ? spec.FaceYaw(side, t, duration) : 0f;
            var twist = t <= duration ? spec.TorsoTwist(side, t, duration) : 0f;
            var sense = game.PlayerSense;
            var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, sense.Self.WorldToLocal(aimWorld), reach, sense.SelfLeg(side)) : (LegCommand?)null;
            game.Step(new BoxingInput(Command(Vector3.Zero, leg, yaw, twist)), Dt);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Kick && hit.Attacker == attacker)
                {
                    var knee = attacker.KneePosition(side);
                    var axis = attacker.AnklePosition(side) - knee;
                    lastHit = $"当たり: {hit.KickerPart}  すねの点 {F(Vector3.Dot(hit.Point - knee, axis) / MathF.Max(1e-6f, axis.LengthSquared()))}  {F(hit.Energy)} J  {F(hit.Speed)} m/s";
                    log.WriteLine($"  t {F(t)} {lastHit}");
                }
            }

            if (t < nextFrame)
            {
                continue;
            }

            nextFrame = t + simPerFrame;
            var lines = new List<DebugLine>();
            if (leg is { KneeLocal: { } kneeTarget })
            {
                lines.Add(new DebugLine(attacker.HipPosition(side), attacker.Frame.LocalToWorld(kneeTarget), Gold));
            }

            Emit(renderer, ffmpeg, width, height, game, [
                new(title, new Vector2(40f, 40f), 30f, White, TextAnchor.Left),
                new($"膝の曲げ {F(attacker.KneeBend(side))} rad  ねじり {F(attacker.ThighTwist(side))} rad   金の線 = 膝の制御点の目標", new Vector2(40f, 80f), 22f, Gold, TextAnchor.Left),
                new(lastHit, new Vector2(40f, 114f), 24f, Grey, TextAnchor.Left),
                new($"{F(t)} s", new Vector2(width - 40f, height - 40f), 24f, Grey, TextAnchor.Right),
            ], lines, 0f);
        }
    }

    private static void Emit(HeadlessRenderer renderer, Process ffmpeg, int width, int height, BoxingGame game, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<DebugLine> extra, float dim)
    {
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
        lines.AddRange(extra);
        var a = game.Player.ChestPosition;
        var v = game.Cpu.ChestPosition;
        var middle = (a + v) * 0.5f + new Vector3(0f, -0.14f, 0f);
        var direction = Vector3.Normalize((v - a) with { Y = 0f });
        var side = Vector3.Cross(Vector3.UnitY, direction); // 二人の間の線に垂直（真横から）
        var eye = middle + (side * 1.05f) + new Vector3(0f, 0.1f, 0f);
        var vp = Camera.ViewProjection(eye, middle, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
        UiShapeDraw[] shapes = dim > 0f ? [new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim))] : [];
        var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: eye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
        ffmpeg.StandardInput.BaseStream.Write(rgba);
    }
}
