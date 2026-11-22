using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（疲れの見た目、2026-11-23）: 連打 10 秒 → 構えるだけ、の同じ体を、胸の高さの斜め前から近く撮り、時刻ごとに並べる。
/// .visualcheck/fatigue_look_timeline.png（構え・あご・胸が時間でどう変わるか）と fatigue_look_breath.png（息の周期 1 回ぶん）。
/// 的の相手は倒れない設定（FatigueLookProbe と同じ）。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class FatigueLookVisualTests
{
    private const float Dt = 1f / 60f;
    private const int Size = 420;

    private static FighterIntent Rush(float t)
    {
        var phase = t / 0.2014f;
        var index = (int)MathF.Floor(phase);
        var down = phase - index < 0.67f;
        return new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
    }

    private static byte[] Shot(HeadlessRenderer renderer, BoxingGame game)
    {
        var p = game.Player;
        var frame = Matrix4x4.CreateFromQuaternion(p.TorsoOrientation);
        var forward = Vector3.Normalize(new Vector3(frame.M31, 0f, frame.M33));
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var chest = p.ChestPosition;
        var eye = chest + (forward * 0.1f) + (right * 1.15f) + new Vector3(0f, 0.08f, 0f); // 真横（相手は前にいるので、斜め前だと視界をふさぐ）
        var (vp, displayEye) = game.Display.Camera(eye, chest + new Vector3(0f, 0.1f, 0f) + (forward * 0.1f), Vector3.UnitY, 45f * MathF.PI / 180f, 1f, 0.02f, 30f);
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, false, instances, lines);
        return renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: displayEye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
    }

    private static void Sheet(string name, float[] times, int columns)
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        using var renderer = new HeadlessRenderer(Size, Size);
        var rows = (times.Length + columns - 1) / columns;
        var sheet = new byte[Size * columns * Size * rows * 4];
        var next = 0;
        for (var step = 0; next < times.Length; step++)
        {
            var t = step * Dt;
            control.Input = t < 10f ? Rush(t) : default;
            game.Step(new BoxingInput(default), Dt);
            if (t + Dt / 2f >= times[next])
            {
                var shot = Shot(renderer, game);
                var cx = next % columns;
                var cy = next / columns;
                for (var y = 0; y < Size; y++)
                {
                    Buffer.BlockCopy(shot, y * Size * 4, sheet, ((((cy * Size) + y) * Size * columns) + (cx * Size)) * 4, Size * 4);
                }

                next++;
            }
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var dir = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(dir);
        PngWriter.WriteRgba(Path.Combine(dir, name), Size * columns, Size * rows, sheet);
    }

    [TestMethod]
    public void Timeline_RushThenGuard() => Sheet("fatigue_look_timeline.png", [0.3f, 3f, 7f, 10f, 13f, 17f, 23f, 30f], 4);

    [TestMethod]
    public void Breath_OneCycleWhenExhausted() => Sheet("fatigue_look_breath.png", [24f, 24.15f, 24.3f, 24.45f, 24.6f, 24.75f, 24.9f, 25.05f], 4);
}
