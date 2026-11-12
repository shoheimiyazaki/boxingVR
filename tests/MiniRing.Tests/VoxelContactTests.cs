using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// ボクセル同士の衝突（エンジン <see cref="VoxelContact"/>）: 壊れるかどうかは、触れている面積（形）と材質の強さだけで決まる。
/// 刃は切り、平らな面は押すだけ、切っ先は深く刺さり、柔らかい方だけが壊れ、細い木は折れる。
/// </summary>
[TestClass]
public sealed class VoxelContactTests
{
    private const byte Steel = 1;
    private const byte Wood = 2;
    private const byte Gel = 3;

    private static readonly VoxelPalette Palette = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("steel", VoxelTissue.Solid, 50f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(7800f, 600e6f, 0.0002f, Tensile: 1.5e9f, Stiffness: 200e9f)),
        new VoxelMaterial("wood", VoxelTissue.Solid, 5f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(650f, 8e6f, 0.001f, Tensile: 60e6f, Stiffness: 10e9f)),
        new VoxelMaterial("gel", VoxelTissue.Flesh, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1000f, 0.3e6f, 0.004f)),
    ]);

    private const float Dt = 1f / 60f;

    /// <summary>無重力の世界に、材質 <paramref name="material"/> で埋まった箱の剛体（質量は密度 × 体積）。</summary>
    private static VoxelBody Box(PhysicsWorld world, byte material, Vector3 half, float cell, Vector3 position, Quaternion? orientation = null)
    {
        var grid = new VoxelGrid(Palette, half, cell, (_, _) => material);
        var mass = grid.FilledCount * grid.CellVolume * Palette[material].Mechanics!.Density;
        var id = world.AddDynamicBox(position, orientation ?? Quaternion.Identity, half, mass);
        return new VoxelBody(world, id, grid);
    }

    /// <summary>
    /// 1 歩ずつ進め、毎歩の後で 2 つの間の接触を解く。<paramref name="steps"/> 歩ぶん。壊れたセルの合計（a・b）と、最後の接触の結果。
    /// </summary>
    private static (int RemovedA, int RemovedB, List<VoxelContactResult> Results) Run(PhysicsWorld world, VoxelBody a, VoxelBody b, int steps = 30)
    {
        world.SetPairCollisionEnabled(a.BodyId, b.BodyId, false);
        int ra = 0, rb = 0;
        var results = new List<VoxelContactResult>();
        for (var i = 0; i < steps; i++)
        {
            var sa = world.GetState(a.BodyId);
            var sb = world.GetState(b.BodyId);
            world.Step(Dt);
            var r = VoxelContact.Resolve(a, sa, b, sb);
            ra += r.RemovedA.Count;
            rb += r.RemovedB.Count;
            if (r.Touched)
            {
                results.Add(r);
            }
        }

        return (ra, rb, results);
    }

    private static PhysicsWorld World() => new(gravity: Vector3.Zero);

    /// <summary>ゲルの塊（6cm 角、5mm のセル）を原点に。</summary>
    private static VoxelBody GelBlock(PhysicsWorld world) => Box(world, Gel, new Vector3(0.03f), 0.005f, Vector3.Zero);

    /// <summary>
    /// 鋼の板（長さ 8cm × 幅 3cm × 厚さ 2.5mm、1.25mm のセル）を、ゲルの上 5cm から −Y へ <paramref name="speed"/> で落とす。
    /// <paramref name="edgeOn"/> なら薄い縁から、そうでなければ平らな面から当たる。
    /// </summary>
    private static VoxelBody Plate(PhysicsWorld world, bool edgeOn, float speed, byte material = Steel)
    {
        // 板は X に長く、Y に幅、Z に厚さ。縁から当てるなら幅が上下（Y）、面から当てるなら厚さが上下になるよう X まわりに回す。
        var orientation = edgeOn ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        var plate = Box(world, material, new Vector3(0.04f, 0.015f, 0.00125f), 0.00125f, new Vector3(0f, 0.05f, 0f), orientation);
        world.SetLinearVelocity(plate.BodyId, new Vector3(0f, -speed, 0f));
        return plate;
    }

    [TestMethod]
    public void AnEdge_Cuts_ButTheFlatOfTheSameBlade_OnlyPushes()
    {
        using var edgeWorld = World();
        var edgeBlock = GelBlock(edgeWorld);
        var edge = Run(edgeWorld, Plate(edgeWorld, edgeOn: true, speed: 6f), edgeBlock);

        using var flatWorld = World();
        var flatBlock = GelBlock(flatWorld);
        var flat = Run(flatWorld, Plate(flatWorld, edgeOn: false, speed: 6f), flatBlock);

        Assert.IsGreaterThan(20, edge.RemovedB, "the edge cuts into the gel");
        Assert.AreEqual(0, flat.RemovedB, "the flat does not break the gel");
        Assert.IsLessThan(-0.3f, flatWorld.GetState(flatBlock.BodyId).LinearVelocity.Y, "the flat knocks the block away instead");
        Assert.AreEqual(0, edge.RemovedA + flat.RemovedA, "steel is not hurt by gel");
    }

    [TestMethod]
    public void APoint_PiercesDeeperThanAnEdge_EvenWithLessEnergy()
    {
        // 同じ鋼・同じ速さで、切っ先（2.5mm 角の棒の先、軽い）と縁（3cm 幅の板の縁、約 12 倍重い）。
        using var spikeWorld = World();
        var spikeBlock = GelBlock(spikeWorld);
        var spike = Box(spikeWorld, Steel, new Vector3(0.00125f, 0.04f, 0.00125f), 0.00125f, new Vector3(0f, 0.08f, 0f));
        spikeWorld.SetLinearVelocity(spike.BodyId, new Vector3(0f, -6f, 0f));
        var pierce = Run(spikeWorld, spike, spikeBlock);

        using var edgeWorld = World();
        var edgeBlock = GelBlock(edgeWorld);
        var edge = Run(edgeWorld, Plate(edgeWorld, edgeOn: true, speed: 6f), edgeBlock);

        Assert.IsGreaterThan(Depth(edge.Results, edgeBlock), Depth(pierce.Results, spikeBlock), "the point goes deeper");
    }

    [TestMethod]
    public void OnlyTheSofterSide_Breaks()
    {
        // 鋼の縁で木を打つ: 木が切れ、鋼は無傷。
        using var w1 = World();
        var woodBlock = Box(w1, Wood, new Vector3(0.03f), 0.005f, Vector3.Zero);
        var steelOnWood = Run(w1, Plate(w1, edgeOn: true, speed: 14f), woodBlock);
        Assert.IsGreaterThan(0, steelOnWood.RemovedB, "steel bites into wood");
        Assert.AreEqual(0, steelOnWood.RemovedA, "and stays whole");

        // 木の板の縁を、上向きに立てた鋼の板の縁へ十字に打ち下ろす: 触れるのは縁と縁の交わり（2.5mm 角）だけ。木の縁が潰れ、鋼は無傷。
        using var w2 = World();
        var steelEdge = Box(w2, Steel, new Vector3(0.00125f, 0.015f, 0.04f), 0.00125f, Vector3.Zero);
        var woodOnSteel = Run(w2, Plate(w2, edgeOn: true, speed: 10f, material: Wood), steelEdge);
        Assert.IsGreaterThan(0, woodOnSteel.RemovedA, "the wooden edge is crushed");
        Assert.AreEqual(0, woodOnSteel.RemovedB, "the steel is not");
    }

    [TestMethod]
    public void TheOutcome_DoesNotDependOnWhichBodyIsPassedFirst()
    {
        using var w1 = World();
        var block1 = GelBlock(w1);
        var plate1 = Plate(w1, edgeOn: true, speed: 6f);
        var forward = Run(w1, plate1, block1);

        using var w2 = World();
        var block2 = GelBlock(w2);
        var plate2 = Plate(w2, edgeOn: true, speed: 6f);
        var backward = Run(w2, block2, plate2);

        Assert.AreEqual(forward.RemovedB, backward.RemovedA);
        Assert.AreEqual(forward.RemovedA, backward.RemovedB);
    }

    [TestMethod]
    public void TheBlow_KeepsItsMomentum_SharedBetweenTheTwo()
    {
        using var world = World();
        var block = GelBlock(world);
        var plate = Plate(world, edgeOn: false, speed: 6f);
        float Momentum() => (world.GetMass(block.BodyId) * world.GetState(block.BodyId).LinearVelocity.Y) + (world.GetMass(plate.BodyId) * world.GetState(plate.BodyId).LinearVelocity.Y);
        var before = Momentum();

        Run(world, plate, block);

        Assert.AreEqual(before, Momentum(), MathF.Abs(before) * 0.03f);
    }

    [TestMethod]
    public void AFasterBlade_CutsMore()
    {
        int Cut(float speed)
        {
            using var world = World();
            var block = GelBlock(world);
            return Run(world, Plate(world, edgeOn: true, speed: speed), block).RemovedB;
        }

        var slow = Cut(2.5f);
        var fast = Cut(6f);
        Assert.IsGreaterThan(slow, fast);
    }

    [TestMethod]
    public void AThinWoodenStick_Snaps_WhereASteelOneHolds()
    {
        static VoxelContactResult? Strike(byte material)
        {
            using var world = World();
            // 細い棒（長さ 20cm × 5mm 角、1.25mm のセル）を X に寝かせ、真ん中を上から重い鋼の塊の平らな面で25m/s で叩く（硬く重い鋼は、同じ打撃で木の約 15 倍の曲げ応力を受けるので、鋼の引張強さは焼き入れ鋼の 1.5GPa）。
            var stick = Box(world, material, new Vector3(0.1f, 0.0025f, 0.0025f), 0.00125f, Vector3.Zero);
            var hammer = Box(world, Steel, new Vector3(0.01f), 0.0025f, new Vector3(0f, 0.03f, 0f));
            world.SetLinearVelocity(hammer.BodyId, new Vector3(0f, -25f, 0f));
            return Run(world, hammer, stick, steps: 20).Results.FirstOrDefault(r => r.SnappedB is not null);
        }

        var wood = Strike(Wood);
        Assert.IsNotNull(wood, "the wooden stick snaps");
        Assert.IsGreaterThan(0, wood.SnappedB!.Grid.FilledCount);
        Assert.IsNull(Strike(Steel), "the steel one holds");
    }

    [TestMethod]
    public void AStickStruckInTheMiddle_SnapsAtItsNotch_WhereTheSectionIsThin()
    {
        // 長さ 20cm × 5mm 角の木の棒（1.25mm のセル）。中心から +5cm に、高さの 3/4 を削った切り欠き（残りは下の 1 セル）。
        const float cell = 0.00125f;
        var grid = new VoxelGrid(Palette, new Vector3(0.1f, 0.0025f, 0.0025f), cell, (p, _) =>
            MathF.Abs(p.X - 0.05f) < cell * 0.5f && p.Y > -0.00125f ? (byte)0 : Wood);
        var notch = (int)((0.05f + 0.1f) / cell);

        // 自由な棒の真ん中を上から押す: 曲げモーメントは真ん中で最大（切り欠きの所の 4 倍）でも、断面の曲げにくさは切り欠きで 1/16。
        var cut = VoxelBending.FindBreak(grid, new Vector3(0f, 0.0025f, 0f), new Vector3(0f, -200f, 0f));

        Assert.IsNotNull(cut);
        Assert.AreEqual(0, cut.Value.Axis);
        Assert.AreEqual(notch, cut.Value.Slice, 1);
        Assert.IsTrue(cut.Value.TakeHigh, "the shorter piece beyond the notch breaks off");
        Assert.IsNull(VoxelBending.FindBreak(grid, new Vector3(0f, 0.0025f, 0f), new Vector3(0f, -2f, 0f)), "a light push does not snap it");
    }

    [TestMethod]
    public void ABeam_IsStifferWhenBentAcrossItsDepth()
    {
        // 幅 2cm × 厚さ 2.5mm の板: 厚さの向き（Z）に押すより、幅の向き（Y）に押す方が (2cm / 2.5mm)² = 64 倍ほど曲げにくい。
        var plate = new VoxelGrid(Palette, new Vector3(0.1f, 0.01f, 0.00125f), 0.00125f, (_, _) => Steel);
        var flat = VoxelBending.BeamStiffness(plate, Vector3.UnitZ)!.Value;
        var edge = VoxelBending.BeamStiffness(plate, Vector3.UnitY)!.Value;
        Assert.AreEqual(64f, edge / flat, 64f * 0.1f);
        Assert.IsNull(VoxelBending.BeamStiffness(plate, Vector3.UnitX), "pushing along the length does not bend it");
    }

    [TestMethod]
    public void BodiesApart_DoNotTouch()
    {
        using var world = World();
        var block = GelBlock(world);
        var plate = Box(world, Steel, new Vector3(0.04f, 0.015f, 0.00125f), 0.00125f, new Vector3(0f, 0.3f, 0f));
        var (a, b, results) = Run(world, plate, block, steps: 5);
        Assert.AreEqual(0, a + b);
        Assert.IsEmpty(results);
    }

    /// <summary>ゲルの上面（Y = +3cm）から、壊れたセルのいちばん深い所までの深さ [m]。</summary>
    private static float Depth(IEnumerable<VoxelContactResult> results, VoxelBody block)
    {
        var deepest = 0f;
        foreach (var r in results)
        {
            foreach (var v in r.RemovedFor(block))
            {
                deepest = MathF.Max(deepest, block.Grid.HalfExtents.Y - v.LocalCenter.Y);
            }
        }

        return deepest;
    }

    /// <summary>
    /// 鋼の板の縁を、ゲルへ手で押すように一定のゆっくりした速さ <paramref name="speed"/> で <paramref name="steps"/> 歩押し続け、ゲルから
    /// 斬り取ったセルの数を返す。<paramref name="drive"/> は手が押し込む力（<see cref="VoxelContact.Resolve"/> の drive）。
    /// </summary>
    private static int Pressed(float speed, float drive, int steps = 30)
    {
        using var world = World();
        var gel = GelBlock(world);
        var plate = Plate(world, edgeOn: true, speed);
        world.SetPairCollisionEnabled(plate.BodyId, gel.BodyId, false);
        var removed = 0;
        for (var i = 0; i < steps; i++)
        {
            world.SetLinearVelocity(plate.BodyId, new Vector3(0f, -speed, 0f)); // 手が同じ速さで押し続ける
            var sp = world.GetState(plate.BodyId);
            var sg = world.GetState(gel.BodyId);
            world.Step(Dt);
            removed += VoxelContact.Resolve(plate, sp, gel, sg, new Vector3(0f, -drive, 0f)).RemovedB.Count;
        }

        return removed;
    }

    [TestMethod]
    public void AHeldEdge_PressedSlowly_SlicesWithTheHandsForce_ButNotWithoutIt()
    {
        var free = Pressed(speed: 0.5f, drive: 0f);
        var held = Pressed(speed: 0.5f, drive: 100f); // 縁 8 cm × 2.5 mm をゲル（0.3 MPa）へ押し込むには約 60 N
        Assert.IsGreaterThan(free + 10, held, $"a hand pressing the edge on slices the gel (free {free}, held {held})");
    }

    [TestMethod]
    public void AHandPullingAway_DoesNotDriveTheEdgeIn()
    {
        // 押す向きが当たる向きと逆（手は引いている）なら、押し込みは効かない。
        var free = Pressed(speed: 0.5f, drive: 0f);
        var pulling = Pressed(speed: 0.5f, drive: -100f);
        Assert.AreEqual(free, pulling);
    }

    [TestMethod]
    public void TheHandsForce_DoesNotPushAFlatFaceThroughFlesh()
    {
        // 平らな面は触れる面積が広いので、同じ手の力では押し切れない（形が効く）。
        using var world = World();
        var gel = GelBlock(world);
        var plate = Plate(world, edgeOn: false, 0.5f);
        world.SetPairCollisionEnabled(plate.BodyId, gel.BodyId, false);
        var removed = 0;
        for (var i = 0; i < 30; i++)
        {
            world.SetLinearVelocity(plate.BodyId, new Vector3(0f, -0.5f, 0f));
            var sp = world.GetState(plate.BodyId);
            var sg = world.GetState(gel.BodyId);
            world.Step(Dt);
            removed += VoxelContact.Resolve(plate, sp, gel, sg, new Vector3(0f, -100f, 0f)).RemovedB.Count; // 面 8 cm × 3 cm は約 720 N 要る
        }

        Assert.AreEqual(0, removed);
    }

    [TestMethod]
    public void AnOverlapThatCannotCut_IsPushedOutWithinAFewSteps_NotLeftEmbedded()
    {
        // 再発防止: 腕が押し込んで、斬れないまま体にめり込んだ刃が、そのまま残っていた（試合で刃の 1〜2 割の歩がめり込んでいた）。
        // 歩の始めからめり込んでいる重なりは、サンプル半分ずつではなく、めり込んだ深さだけ押し戻す（深ければ数歩に分けて）。
        using var world = World();
        var gel = GelBlock(world);
        // 平らな面を下に、ゲルの上面から 1 cm 沈めて止めて置く（平らな面は押し切れない）。
        var flat = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        var plate = Box(world, Steel, new Vector3(0.04f, 0.015f, 0.00125f), 0.00125f, new Vector3(0f, 0.03f + 0.00125f - 0.01f, 0f), flat);
        world.SetPairCollisionEnabled(plate.BodyId, gel.BodyId, false);
        var removed = 0;
        for (var i = 0; i < 3; i++)
        {
            var sp = world.GetState(plate.BodyId);
            var sg = world.GetState(gel.BodyId);
            world.Step(Dt);
            var r = VoxelContact.Resolve(plate, sp, gel, sg);
            removed += r.RemovedB.Count;
            Assert.IsTrue(i > 0 || r.Touched);
        }

        Assert.AreEqual(0, removed, "a flat face at rest cuts nothing");
        var bottom = world.GetState(plate.BodyId).Position.Y - 0.00125f;
        Assert.IsGreaterThan(0.03f - 0.005f, bottom, $"the plate is out of the gel, but for less than one gel cell (bottom at {bottom * 1000f:0.0} mm, top of the gel at 30 mm)");
        Assert.IsLessThan(0.03f + 0.005f, bottom, "and not thrown clear of it");
    }
}
