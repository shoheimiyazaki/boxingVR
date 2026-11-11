using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// GPU（Vulkan）で試合を数秒進めて PNG に描く。Aigis と同じく visual はローカル専用・CI 外。
/// 出力: リポジトリ直下の <c>.visualcheck/miniring_*.png</c>。PNG があること ≠ 正しい（Aigis anti-pattern）なので、
/// 人／レビュー役が中身を見て判断する。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class SnapshotTests
{
    private const int Width = 960;
    private const int Height = 540;

    [TestMethod]
    public void Match_RendersFromPlayerViewpoint()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);

        // VR で立ったプレイヤーの目線（原点・目の高さ 1.55m）からリングを見下ろす。
        var eye = new Vector3(0f, 1.55f, 0.05f);
        var target = BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight + 0.15f };
        var viewProjection = Camera.ViewProjection(eye, target, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.02f, 50f);

        var time = 0f;
        foreach (var at in new[] { 0.1f, 2f, 6f })
        {
            while (time < at)
            {
                var player = game.Player;
                game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, game.Cpu.ChestPosition - player.ChestPosition,
                    new HandCommand(AiControl.Guard(Hand.Left, player.ArmReach), false),
                    new HandCommand(new Vector3(0.05f, 0.05f, MathF.Sin(time * 3f) > 0.6f ? 1f : 0.5f) * player.ArmReach, false))), 1f / 60f);
                time += 1f / 60f;
            }

            var (instances, lines) = game.BuildRenderables();
            var text = game.BuildOverlayText(viewProjection, new Vector2(Width, Height));
            var rgba = Render(renderer, game, viewProjection, eye, instances, lines, text);
            var path = Path.Combine(outDir, $"miniring_t{at:0.0}.png");
            PngWriter.WriteRgba(path, Width, Height, rgba);
            Assert.IsTrue(File.Exists(path));
        }
    }

    /// <summary>ローポリの顔・胴・腕と、打撃のへこみ（白 → 赤）を間近で見る。顔と左腕と胸を正面から決め打ちでへこませる。</summary>
    [TestMethod]
    public void CloseUp_ShowsLowPolyBodyAndDents()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        for (var i = 0; i < 30; i++)
        {
            game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(AiControl.Guard(Hand.Left, game.Player.ArmReach), false),
                new HandCommand(AiControl.Guard(Hand.Right, game.Player.ArmReach), false))), 1f / 60f);
        }

        var cpu = game.Cpu;
        var head = game.World.GetState(cpu.Parts.Head);
        var toCamera = Vector3.Normalize(game.Player.HeadPosition - head.Position) with { Y = 0f };
        var eye = head.Position + (Vector3.Normalize(toCamera) * 0.22f) + new Vector3(0.04f, 0.02f, 0f);
        var viewProjection = Camera.ViewProjection(eye, head.Position - new Vector3(0f, 0.05f, 0f), Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);

        void Shoot(string name)
        {
            var (instances, lines) = game.BuildRenderables();
            var rgba = Render(renderer, game, viewProjection, eye, instances, lines, []);
            PngWriter.WriteRgba(Path.Combine(outDir, $"miniring_closeup_{name}.png"), Width, Height, rgba);
        }

        Shoot("before");
        foreach (var (part, side, damage) in new[] { (cpu.Parts.Head, 0.3f, 18f), (cpu.Parts.Head, -0.5f, 9f), (cpu.Parts.Chest, 0.4f, 20f), (cpu.Parts.Pelvis, -0.3f, 6f) })
        {
            var s = game.World.GetState(part);
            var forward = Vector3.Transform(Vector3.UnitZ, s.Orientation);
            var left = Vector3.Transform(Vector3.UnitX, s.Orientation);
            var half = cpu.HalfExtentsOf(part).Z;
            var fist = s.Position + (forward * (half + FighterTuning.Default.GloveRadius)) + (left * side * half);
            game.CpuSkin.Dent(part, s, fist, -forward, damage);
            game.Strike(cpu, part, fist, -forward, damage * 2f); // ボクセルの体も削る（体の見た目はこちら）
        }

        for (var i = 0; i < 12; i++)
        {
            game.Step(default, 1f / 60f); // 削れた破片を飛ばしてから撮る（直後は穴の中に詰まっている）
        }

        Shoot("after");
        Assert.IsTrue(game.CpuSkin.Part(cpu.Parts.Head).IsDented);
        Assert.IsLessThan(1f, cpu.Voxels!.PartOf(cpu.Parts.Head)!.Body.Grid.Fraction(Aigis.Foundation.Voxels.VoxelTissue.Flesh), "the head's voxels were carved");
    }

    /// <summary>
    /// 身なり: 2 人を横から、CPU を斜め前から、頭から足まで入れて撮る。トランクス（コーナーの色・白い腰のゴム）が腰と太ももの上、
    /// ブーツが足とすねの下に見えるか。
    /// </summary>
    [TestMethod]
    public void Outfit_ShowsTrunksAndBoots()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        for (var i = 0; i < 30; i++)
        {
            game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(AiControl.Guard(Hand.Left, game.Player.ArmReach), false),
                new HandCommand(AiControl.Guard(Hand.Right, game.Player.ArmReach), false))), 1f / 60f);
        }

        var player = game.World.GetState(game.Player.Parts.Pelvis).Position;
        var cpu = game.World.GetState(game.Cpu.Parts.Pelvis).Position;
        var between = Vector3.Normalize((cpu - player) with { Y = 0f });
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, between));

        void Shoot(string name, Vector3 eye, Vector3 target)
        {
            var viewProjection = Camera.ViewProjection(eye, target, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);
            var (instances, lines) = game.BuildRenderables();
            PngWriter.WriteRgba(Path.Combine(outDir, $"miniring_outfit_{name}.png"), Width, Height, Render(renderer, game, viewProjection, eye, instances, lines, []));
        }

        var middle = (player + cpu) * 0.5f;
        // 横はロープの上から見下ろし、前はプレイヤーの横へ大きく外して CPU の全身を入れる。
        Shoot("side", middle + (across * 0.9f) + new Vector3(0f, 0.3f, 0f), middle - new Vector3(0f, 0.08f, 0f));
        Shoot("front", cpu - (between * 0.75f) + (across * 0.5f) + new Vector3(0f, 0.15f, 0f), cpu - new Vector3(0f, 0.1f, 0f));
        Assert.IsGreaterThan(0, game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Pelvis)!.Body.Grid.Initial(MiniRing.Voxels.BoxerAnatomy.BlueTrunks));
    }

    /// <summary>
    /// CPU の体を前後に半分に割った断面（手前半分を描かない）を正面から撮り、中の組織（皮膚・筋肉・骨・脳・心臓・肺・肝臓・腸・目）が
    /// 色で見分けられるかを見る。プレイヤーの体は描かない。
    /// </summary>
    [TestMethod]
    public void Cutaway_ShowsEachTissueInItsOwnColor()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        for (var i = 0; i < 30; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var cpuVolumes = cpu.Voxels!.Parts.Select(p => p.Volume).ToHashSet();
        var voxels = game.BuildVoxelVolumes().Where(d => cpuVolumes.Contains(d.Volume)).ToList();
        foreach (var volume in cpuVolumes)
        {
            for (var z = volume.Depth / 2; z < volume.Depth; z++)
            {
                Array.Clear(volume.Rgba, 4 * volume.Width * volume.Height * z, 4 * volume.Width * volume.Height);
            }
        }

        var chest = game.World.GetState(cpu.Parts.Chest);
        var front = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, chest.Orientation) with { Y = 0f });
        var target = chest.Position + new Vector3(0f, -0.08f, 0f);
        var eye = target + (front * 0.5f) + new Vector3(0f, 0.05f, 0f);
        var viewProjection = Camera.ViewProjection(eye, target, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);
        var rgba = renderer.RenderPrimitiveScene(IGameClear, viewProjection, [], [], [], cameraPosition: eye, voxels: voxels);
        PngWriter.WriteRgba(Path.Combine(outDir, "miniring_cutaway.png"), Width, Height, rgba);
        Assert.IsGreaterThan(0, rgba.Where((_, i) => i % 4 == 0).Count(r => r > 60), "the body was drawn");
    }

    /// <summary>重い 1 発の直後（数フレーム）を間近で: 打った側の色の光の玉・輪・筋と、拳の向きへ噴き出すボクセル。</summary>
    [TestMethod]
    public void HeavyBlow_ShowsTheImpactGlowAndTheJet()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        for (var i = 0; i < 30; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var chest = game.World.GetState(cpu.Parts.Chest);
        var forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, chest.Orientation) with { Y = 0f });
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var half = cpu.HalfExtentsOf(cpu.Parts.Chest).Z;
        // 右から斜めに入るフック。
        var blow = Vector3.Normalize(-forward + (side * 0.8f));
        var fist = chest.Position - (blow * (half + 0.02f));
        game.Strike(cpu, cpu.Parts.Chest, fist, blow * 4f, 28f, game.Player);
        for (var i = 0; i < 4; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var eye = chest.Position + (Vector3.Normalize(Vector3.Cross(blow, Vector3.UnitY) - (blow * 0.5f)) * 0.42f) + new Vector3(0f, 0.3f, 0f); // 打った側の斜め上から（輪が楕円に見え、手前の体に遮られない）
        var viewProjection = Camera.ViewProjection(eye, chest.Position, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);
        var (instances, lines) = game.BuildRenderables();
        var rgba = Render(renderer, game, viewProjection, eye, instances, lines, []);
        PngWriter.WriteRgba(Path.Combine(outDir, "miniring_impact.png"), Width, Height, rgba);
        Assert.IsGreaterThan(0, game.Impacts.Count);
    }

    /// <summary>
    /// 顔を正面から重いストレートで打った直後を真横から: 破片は顔の前（打った側）へ拳の周りに開いて跳ね、頭の後ろからは出ない。
    /// 砕けた直後の欠片は光って短い尾を引く。数フレームおきに 3 枚。
    /// </summary>
    [TestMethod]
    public void StraightToTheFace_SplashesOutOfTheFace_NotOutOfTheBackOfTheHead()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        for (var i = 0; i < 30; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var head = game.World.GetState(cpu.Parts.Head);
        var forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, head.Orientation) with { Y = 0f });
        var half = cpu.HalfExtentsOf(cpu.Parts.Head).Z;
        var blow = -forward;
        var fist = head.Position - (blow * (half + FighterTuning.Default.GloveRadius));
        game.Strike(cpu, cpu.Parts.Head, fist, blow * 4f, 28f, game.Player, recoil: blow * 0.4f);
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var eye = head.Position + (side * 0.45f) + (forward * 0.05f) + new Vector3(0f, 0.05f, 0f);
        var viewProjection = Camera.ViewProjection(eye, head.Position + (forward * 0.05f), Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);
        var stepped = 0;
        foreach (var frame in new[] { 3, 15, 30, 60 })
        {
            while (stepped < frame)
            {
                game.Step(default, 1f / 60f);
                stepped++;
            }

            var (instances, lines) = game.BuildRenderables();
            var rgba = Render(renderer, game, viewProjection, eye, instances, lines, []);
            PngWriter.WriteRgba(Path.Combine(outDir, $"miniring_face_f{frame:00}.png"), Width, Height, rgba);
            // 殴った側の斜め前から: えぐれた顔の断面が、白 → 黄 → 赤と冷めていく。
            var frontEye = head.Position + (forward * 0.3f) + (side * 0.12f) + new Vector3(0f, 0.06f, 0f);
            var front = Camera.ViewProjection(frontEye, head.Position, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);
            PngWriter.WriteRgba(Path.Combine(outDir, $"miniring_face_front_f{frame:00}.png"), Width, Height, Render(renderer, game, front, frontEye, instances, lines, []));
        }

        Assert.IsGreaterThan(0, game.Debris.Count);
    }

    private static readonly Vector4 IGameClear = new(0.10f, 0.11f, 0.14f, 1f);

    /// <summary>ゲームと同じ画づくり（会場の照明・影・空気の粒・仕上げ）で描く。</summary>
    private static byte[] Render(HeadlessRenderer renderer, BoxingGame game, Matrix4x4 viewProjection, Vector3 eye,
        IReadOnlyList<PrimitiveInstance> instances, IReadOnlyList<DebugLine> lines, IReadOnlyList<ScreenTextDraw> text)
        => renderer.RenderPrimitiveScene(ArenaLook.Background, viewProjection, instances, lines, text, particles: game.BuildParticles(),
            lighting: game.BuildLighting(), shadow: game.BuildShadow(), cameraPosition: eye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
