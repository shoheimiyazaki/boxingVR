using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Foundation;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using Aigis.Render;
using MiniRing.Audio;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>診断用。手動の骨折・肉の削除を明示し、同じ実物理でぶら下がりから落下まで撮る。</summary>
[TestClass]
[TestCategory("visual")]
public sealed class FractureVisualProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void KatarSeeds_SearchForASever()
    {
        foreach (var (red, blue) in new[] { ("swarmer", "champion"), ("slugger", "swarmer"), ("champion", "slugger") })
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 613 })
            {
                using var game = new BoxingGame(seed: seed, corners: new BoxingCorners(blue,
                    s => BotCatalog.Create(blue, s), s => BotCatalog.Create(red, s), PlayerWear: HandWear.Katar, CpuWear: HandWear.Katar));
                for (var frame = 0; frame < 12 * 60 && !game.RoundOver; frame++)
                {
                    game.Step(default, 1f / 60f);
                    if (game.RecentSevers.Count > 0)
                    {
                        TestContext.WriteLine($"KATAR SEVER red={red} blue={blue} seed={seed} time={(float)frame / 60f:F3}s count={game.RecentSevers.Count}");
                        return;
                    }
                }
                TestContext.WriteLine($"no sever red={red} blue={blue} seed={seed}");
            }
        }
    }

    private sealed class ReachControl : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            var r = sense.ArmReach;
            return new FighterCommand(Vector3.Zero, Vector3.Normalize(face),
                new HandCommand(new Vector3(0.15f * r, 0.1f * r, 0.7f * r), false),
                new HandCommand(new Vector3(-0.15f * r, 0.1f * r, 0.45f * r), false));
        }
    }

    [TestMethod]
    public void FullThenThinThenNoFlesh_RendersDiagnostic()
    {
        const int Width = 960, Height = 540, Fps = 30, Seconds = 8;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }
        Assert.IsNotNull(root);
        var output = Path.Combine(root.FullName, "docs", "video", "gen11_flesh_bridge_diagnostic.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var silent = Path.ChangeExtension(output, ".silent.mp4");
        var pcmPath = Path.ChangeExtension(output, ".pcm");
        var audio = new byte[Seconds * HitSound.SampleRate * 2];
        foreach (var second in new[] { 1, 3, 5 })
        {
            var clip = SeverSound.Synthesize(second == 5 ? 1f : 0.5f, second);
            Buffer.BlockCopy(clip, 0, audio, second * HitSound.SampleRate * 2, Math.Min(clip.Length, audio.Length - second * HitSound.SampleRate * 2));
        }
        File.WriteAllBytes(pcmPath, audio);
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("reach", _ => new ReachControl(), _ => new StandGuard()));
        using var renderer = new HeadlessRenderer(Width, Height);
        using var ffmpeg = Start("-y -loglevel error -f rawvideo -pix_fmt rgba -s 960x540 -r 30 -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset fast \"" + silent + "\"", input: true);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var upper = game.Cpu.UpperArmId(Hand.Left);
        BoneBreak? broken = null;
        Vector3? focus = null;
        var thin = false;
        var severed = false;
        for (var frame = 0; frame < Fps * Seconds; frame++)
        {
            var t = (float)frame / Fps;
            if (frame == Fps)
            {
                var grid = game.Cpu.Voxels!.PartOf(forearm)!.Body.Grid;
                broken = game.Cpu.Voxels.BreakBent(forearm, new VoxelBreak(0, grid.Dimensions.X / 2, true, 2f))!;
                game.BreakBone(game.Cpu, broken, 1f);
                focus = broken.Point;
            }
            if (broken?.Bridge is { } bridge && (frame == 3 * Fps || frame == 5 * Fps))
            {
                var keep = frame == 3 * Fps ? Math.Max(1, bridge.Cells.Count / 8) : 0;
                var removed = new List<RemovedVoxel>();
                foreach (var cell in bridge.Cells.Skip(keep))
                {
                    bridge.Stub.Grid.Break(cell.StubIndex, removed);
                }
                bridge.Stub.UpdateMass();
                thin = true;
                severed = keep == 0;
            }
            game.Step(default, 1f / Fps);
            focus ??= game.World.GetState(forearm).Position;
            var target = focus.Value - new Vector3(0f, severed ? 0.12f : 0.02f, 0f);
            // 左腕の外側から見る。体・ロープ・相手は描かず、実際の上腕・前腕・手だけを抽出する。
            var outward = game.World.GetState(forearm).Position - game.Cpu.ChestPosition;
            outward.Y = 0f;
            var eye = target + Vector3.Normalize(outward) * (severed ? 0.6f : 0.28f) + new Vector3(0f, 0.025f, 0f);
            var vp = Camera.ViewProjection(eye, target, Vector3.UnitY, 55f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var voxels = game.Cpu.Voxels!.Parts.Where(p => p.Body.BodyId == forearm || p.Body.BodyId == upper)
                .Concat(game.Cpu.Voxels.Fragments.Where(p => p.FragmentOf == forearm)).Select(p => p.Draw()).ToList();
            voxels.Add(game.GloveOf(game.Cpu, Hand.Left)!.Draw());
            var label = broken is null ? "正常な前腕" : severed ? "肉 0 → 拘束解除・自由落下" : thin ? "肉の断面 1/8 → 弱いバネ・振り子" : "骨折・肉の断面 100% → 関節として保持";
            ScreenTextDraw[] text =
            [
                new("診断映像：骨折・肉の削除は時刻指定で人為的に実行", new Vector2(25f, 30f), 24f, Vector4.One, TextAnchor.Left),
                new("実物理・前腕を抽出表示（体とロープは非表示）", new Vector2(25f, 64f), 22f, Vector4.One, TextAnchor.Left),
                new(label, new Vector2(25f, 103f), 27f, new Vector4(1f, 0.8f, 0.3f, 1f), TextAnchor.Left),
                new(t.ToString("0.00", CultureInfo.InvariantCulture) + " s", new Vector2(25f, Height - 35f), 24f, Vector4.One, TextAnchor.Left),
            ];
            var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], text,
                lighting: game.BuildLighting(), cameraPosition: eye, voxels: voxels);
            ffmpeg.StandardInput.BaseStream.Write(rgba);
            if (frame is 60 or 120 or 150 or 165 or 180)
            {
                PngWriter.WriteRgba(Path.ChangeExtension(output, $".t{frame / (float)Fps:0.0}.png"), Width, Height, rgba);
            }
        }
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        Assert.AreEqual(0, ffmpeg.ExitCode);
        using var mux = Start($"-y -loglevel error -i \"{silent}\" -f s16le -ar {HitSound.SampleRate} -ac 1 -i \"{pcmPath}\" -c:v copy -c:a aac -b:a 96k -t 8 \"{output}\"", input: false);
        mux.WaitForExit();
        Assert.AreEqual(0, mux.ExitCode);
        Assert.IsFalse(game.Cpu.HangsByFlesh(broken!.Fragment!.Body.BodyId));
        File.Delete(silent);
        File.Delete(pcmPath);
    }

    private static Process Start(string arguments, bool input) => Process.Start(new ProcessStartInfo("ffmpeg", arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = input,
    }) ?? throw new IOException("ffmpeg を起動できません。");
}
