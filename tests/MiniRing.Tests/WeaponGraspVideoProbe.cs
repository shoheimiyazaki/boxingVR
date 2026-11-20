using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: 刀を両手で握る → 両手を放して落とす → 床に落ちた刀を左手のグリップで拾う → 右手が添える、を 1 本の動画にする
/// （.visualcheck/weapon_grasp.mp4。ffmpeg が要る）。小さい体の腕は床まで届かないので、床に落ちた刀は左手の所へ置き直す（物理の置き直し）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class WeaponGraspVideoProbe
{
    private sealed class Script : IFighterControl
    {
        public bool GripLeft;
        public bool GripRight;
        public bool LetGoLeft;
        public bool LetGoRight;
        public Func<Vector3>? RightAt;

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var tuning = FighterTuning.Default;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            face = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
            var command = FormSwordsman.GuardCommand(WeaponKind.Katana, sense, face, tuning.WalkSpeed, tuning.MaxTorsoTwistRadians);
            var left = command.Left with { Grip = GripLeft, LetGo = LetGoLeft };
            var right = command.Right with { Grip = GripRight, LetGo = LetGoRight };
            if (RightAt is not null)
            {
                right = right with { TargetLocal = sense.Self.WorldToLocal(RightAt()) };
            }

            return command with { Left = left, Right = right };
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void Probe_DropPickUpAndJoin()
    {
        const int width = 1280;
        const int height = 720;
        const int fps = 30;
        Directory.CreateDirectory(".visualcheck");
        var path = Path.GetFullPath(Path.Combine(".visualcheck", "weapon_grasp.mp4"));
        var script = new Script();
        using var game = new BoxingGame(seed: 1, corners: SwordTraining.Corners(WeaponKind.Katana, 3.38f) with { Player = _ => script });
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast \"{path}\"")
        { RedirectStandardInput = true, UseShellExecute = false }) ?? throw new IOException("ffmpeg を起動できません。");
        var sword = game.WeaponOf(game.Player)!;
        var (top, _) = Weapon.TwoHandGripsOf(WeaponKind.Katana);
        var caption = "両手で握る";
        var dt = 1f / 240f;
        var nextFrame = 0f;
        var simPerFrame = 1f / (fps * 0.5f); // 半分の速さ
        var log = new List<string>();

        void Frame(float t)
        {
            var instances = new List<PrimitiveInstance>();
            var lines = new List<DebugLine>();
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var frame = game.Player.Frame;
            var center = game.Player.ChestPosition + (frame.Right * 0.2817f) + new Vector3(0f, -0.2817f, 0f);
            var eye = center + (frame.Right * 1.972f) + (frame.Forward * 0.5634f) + new Vector3(0f, 0.2817f, 0f); // 物理の空間
            var (vp, displayEye) = game.Display.Camera(eye, center, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
            ScreenTextDraw[] text =
            [
                new(caption, new Vector2(40f, 40f), 34f, new Vector4(1f, 1f, 1f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{t:0.00} s（0.5 倍スロー）"), new Vector2(width - 40f, height - 40f), 24f, new Vector4(0.75f, 0.78f, 0.85f, 1f), TextAnchor.Right),
            ];
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: [], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        }

        float Hold(float seconds, float from)
        {
            var t = from;
            for (; t < from + seconds; t += dt)
            {
                game.Step(default, dt);
                if (t >= nextFrame)
                {
                    nextFrame = t + simPerFrame;
                    Frame(t);
                }
            }

            return t;
        }

        var time = Hold(1.007f, 0f);
        caption = "両手を放す（LetGo）";
        script.LetGoLeft = true;
        script.LetGoRight = true;
        time = Hold(0.05595f, time);
        script.LetGoLeft = false;
        script.LetGoRight = false;
        caption = "落ちる";
        time = Hold(2.182f, time);
        log.Add($"dropped: wields L {game.Player.Wields(Hand.Left)} R {game.Player.Wields(Hand.Right)}, sword y {game.World.GetState(sword.Body.BodyId).Position.Y:0.000}");

        caption = "床に落ちた刀を左手の所へ置き直す（腕は床まで届かないため）。左のグリップ";
        script.GripLeft = true;
        var fist = game.Player.GloveCenter(Hand.Left);
        var s0 = game.World.GetState(sword.Body.BodyId);
        game.World.SetDynamicPose(sword.Body.BodyId, fist + new Vector3(0f, 0.02817f, 0f) - Vector3.Transform(top, s0.Orientation), s0.Orientation);
        time = Hold(1.343f, time);
        log.Add($"after pickup: left wields {game.Player.Wields(Hand.Left)} body-matches {game.Player.WieldedBody(Hand.Left) == sword.Body.BodyId} gripX {game.Player.GripX(Hand.Left):0.0000}");

        caption = "右手を柄へ。右のグリップで添える";
        script.RightAt = () =>
        {
            var s = game.World.GetState(sword.Body.BodyId);
            return s.Position + Vector3.Transform(top, s.Orientation);
        };
        script.GripRight = true;
        time = Hold(2.35f, time);
        log.Add($"after join: support of left = {game.Player.SupportOf(Hand.Left)}, gripX L {game.Player.GripX(Hand.Left):0.0000} R {game.Player.GripX(Hand.Right):0.0000}, hands {Vector3.Distance(game.Player.GloveCenter(Hand.Left), game.Player.GloveCenter(Hand.Right)):0.000} m");

        caption = "右手を放す（左手が片手で持つ）";
        script.GripRight = false;
        script.RightAt = null;
        script.LetGoRight = true;
        time = Hold(0.05595f, time);
        script.LetGoRight = false;
        time = Hold(2.014f, time);
        log.Add($"after right let go: left wields {game.Player.Wields(Hand.Left)} one-handed {game.Player.HoldsOneHanded(Hand.Left)}");

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        foreach (var line in log)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine(path);
    }
}
