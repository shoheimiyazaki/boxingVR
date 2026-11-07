using System.Numerics;
using Aigis.Foundation;
using Aigis.Physics;
using Aigis.Render;
using MiniRing.Visual;

namespace MiniRing.Tests;

/// <summary>
/// 見た目の PDCA 用: 1 体だけ構えさせ、正面・斜め・横・寄りで撮る（<c>.visualcheck/boxer_*.png</c>）。
/// 物理の箱を半透明の線で重ねた版も出し、見た目が当たり判定から大きく外れていないかを確かめる。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class BoxerLookTests
{
    private const int Size = 720;
    private static readonly Vector4 Body = new(0.78f, 0.74f, 0.70f, 1f);
    private static readonly Vector4 Glove = new(0.85f, 0.1f, 0.1f, 1f);

    [TestMethod]
    public void Boxer_Turntable()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(3f, 0.5f, 3f));
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var reach = fighter.ArmReach;
        var guard = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(AiControl.Guard(Hand.Left, reach), false),
            new HandCommand(AiControl.Guard(Hand.Right, reach), false));
        for (var i = 0; i < 240 * 2; i++)
        {
            fighter.ApplyControl(guard, 1f / 240f);
            world.Step(1f / 240f);
        }

        var instances = new List<PrimitiveInstance>
        {
            new(PrimitiveKind.Cube, Matrix4x4.CreateScale(new Vector3(2f, 0.02f, 2f)) * Matrix4x4.CreateTranslation(0f, -0.01f, 0f), Vector3.Zero, new Vector4(0.5f, 0.5f, 0.52f, 1f)),
        };
        BoxerRenderer.Add(instances, world, fighter, Body, Glove);
        var boxes = PhysicsBoxes(world, fighter);

        using var renderer = new HeadlessRenderer(Size, Size);
        var center = new Vector3(0f, 0.3f, 0f);
        (string Name, Vector3 Eye, Vector3 Target, float Fov)[] views =
        [
            ("front", new Vector3(0f, 0.36f, 0.95f), center, 40f),
            ("three_quarter", new Vector3(0.65f, 0.45f, 0.7f), center, 40f),
            ("side", new Vector3(0.95f, 0.34f, 0f), center, 40f),
            ("back", new Vector3(-0.3f, 0.5f, -0.9f), center, 40f),
            ("upper_closeup", new Vector3(0.18f, 0.52f, 0.42f), new Vector3(0f, 0.46f, 0.02f), 40f),
            // 部位ごとの寄り（PDCA 用）: 部位の実際の位置を狙う。
            ..Around("part_head", fighter.HeadPosition + new Vector3(0f, 0.01f, 0f), 0.24f),
            ..Around("part_torso", fighter.ChestPosition, 0.36f),
            ..Around("part_pelvis", fighter.PelvisPosition, 0.3f),
            ..Around("part_glove", fighter.GloveCenter(Hand.Left), 0.2f),
            ..Around("part_arm", (fighter.ShoulderCenter + fighter.GloveCenter(Hand.Left)) * 0.5f, 0.3f),
            ..Around("part_legs", world.GetState(fighter.Parts.LeftShin).Position + new Vector3(0.03f, 0.04f, 0f), 0.42f),
        ];
        foreach (var (name, eye, target, fov) in views)
        {
            var viewProjection = Camera.ViewProjection(eye, target, Vector3.UnitY, fov * MathF.PI / 180f, 1f, 0.02f, 20f);
            Save(renderer, outDir, $"boxer_{name}", viewProjection, instances, [], eye);
            if (name == "front" || name == "side")
            {
                Save(renderer, outDir, $"boxer_{name}_hitboxes", viewProjection, instances, boxes, eye);
            }
        }
    }

    /// <summary>対象を正面・斜め前・横から、距離 <paramref name="distance"/> で撮る構図。</summary>
    private static (string, Vector3, Vector3, float)[] Around(string name, Vector3 target, float distance) =>
    [
        (name + "_front", target + new Vector3(0f, 0.15f, 1f) * distance, target, 32f),
        (name + "_quarter", target + new Vector3(0.7f, 0.2f, 0.7f) * distance, target, 32f),
        (name + "_side", target + new Vector3(1f, 0.1f, 0f) * distance, target, 32f),
    ];

    private static void Save(HeadlessRenderer renderer, string dir, string name, Matrix4x4 viewProjection, List<PrimitiveInstance> instances, List<DebugLine> lines, Vector3 eye)
    {
        var rgba = renderer.RenderPrimitiveScene(new Vector4(0.16f, 0.17f, 0.2f, 1f), viewProjection, instances, lines, cameraPosition: eye);
        PngWriter.WriteRgba(Path.Combine(dir, name + ".png"), Size, Size, rgba);
    }

    /// <summary>物理の箱の辺（当たり判定）を線で。</summary>
    private static List<DebugLine> PhysicsBoxes(PhysicsWorld world, Fighter fighter)
    {
        var lines = new List<DebugLine>();
        var color = new Vector4(0.2f, 1f, 0.4f, 1f);
        foreach (var id in fighter.Parts.All)
        {
            var s = world.GetState(id);
            var h = fighter.HalfExtentsOf(id);
            Vector3 Corner(int i) => s.Position + Vector3.Transform(new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z), s.Orientation);
            foreach (var (a, b) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
            {
                lines.Add(new DebugLine(Corner(a), Corner(b), color));
            }
        }

        return lines;
    }

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
