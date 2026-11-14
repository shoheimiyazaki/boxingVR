using System.Numerics;
using System.Text;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 肩と胴の間に、ボクセルの隙間があるか。体の部位（胸・上腕）のセルを世界へ置き、肩の周りを上から・前から見た地図に描く
/// （C = 胸、A = 上腕、. = どの部位もない）。あわせて、肩から胴へ通る細い線（刃の厚みの線）が、どの部位にも触れずに通り抜けられる位置を数える。
/// </summary>
[TestClass]
public sealed class ShoulderGapProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void MapTheShoulder()
    {
        using var game = new BoxingGame(VersusLab_Tuning(), 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard(), CpuWeapon: WeaponKind.Katana, PlayerWeapon: WeaponKind.Katana, CpuTwoHanded: true, PlayerTwoHanded: true));
        for (var i = 0; i < 120; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        var cells = new List<(Vector3 P, BodyPartKind Kind)>();
        foreach (var part in fighter.Voxels!.Parts)
        {
            var grid = part.Body.Grid;
            var toWorld = part.Body.GridToWorld();
            var (nx, ny, nz) = grid.Dimensions;
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        if (grid.At(x, y, z) != 0)
                        {
                            cells.Add((Vector3.Transform(grid.CellCenter(x, y, z), toWorld), part.Kind));
                        }
                    }
                }
            }
        }

        var frame = fighter.Frame;
        TestContext.WriteLine($"cell size {fighter.Voxels.Parts[0].Body.Grid.CellSize}");
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var upper = fighter.UpperArmId(hand);
            var shoulder = game.World.GetState(upper).Position;
            TestContext.WriteLine($"{hand} upper-arm centre {shoulder}  chest {game.World.GetState(fighter.Parts.Chest).Position}");
            // 体の枠の座標（X = 体の右、Y = 上、Z = 前）で、上腕の中心から ±15 cm を見る。
            var local = cells.Select(c => (P: ToLocal(frame, c.P), c.Kind)).ToList();
            var centre = ToLocal(frame, shoulder);
            Map(local, centre, "front view (X across, Y up), looking along Z", p => (p.X, p.Y), 0.005f);
            Map(local, centre, "top view (X across, Z forward), looking down Y", p => (p.X, p.Z), 0.005f);
        }
    }

    /// <summary>肩の関節を通る断面（体の枠の 3 平面）に、体の部位のセルを描く。関節の周りに空きがあるか（目で見る）と、球の中の空きの割合。</summary>
    [TestMethod]
    public void SlicesThroughTheShoulderJoint()
    {
        using var game = new BoxingGame(VersusLab_Tuning(), 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard(), CpuWeapon: WeaponKind.Katana, PlayerWeapon: WeaponKind.Katana, CpuTwoHanded: true, PlayerTwoHanded: true));
        for (var i = 0; i < 120; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        var tuning = VersusLab_Tuning();
        var cells = new List<(Vector3 P, BodyPartKind Kind)>();
        foreach (var part in fighter.Voxels!.Parts)
        {
            var grid = part.Body.Grid;
            var toWorld = part.Body.GridToWorld();
            var (nx, ny, nz) = grid.Dimensions;
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        if (grid.At(x, y, z) != 0)
                        {
                            cells.Add((Vector3.Transform(grid.CellCenter(x, y, z), toWorld), part.Kind));
                        }
                    }
                }
            }
        }

        var frame = fighter.Frame;
        var local = cells.Select(c => (P: ToLocal(frame, c.P), c.Kind)).ToList();
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var u = game.World.GetState(fighter.UpperArmId(hand));
            var xAxis = Vector3.Transform(Vector3.UnitX, u.Orientation);
            var halfX = tuning.HalfExtents.UpperArm.X - tuning.JointOverlap;
            var side = hand.LocalSide();
            var joint = u.Position - (xAxis * side * halfX);
            var j = ToLocal(frame, joint);
            TestContext.WriteLine($"=== {hand} shoulder joint at {j} (body frame)");
            foreach (var radius in new[] { 0.01f, 0.02f, 0.03f, 0.04f })
            {
                int total = 0, filled = 0;
                for (var dz = -radius; dz <= radius; dz += 0.005f)
                {
                    for (var dy = -radius; dy <= radius; dy += 0.005f)
                    {
                        for (var dx = -radius; dx <= radius; dx += 0.005f)
                        {
                            if ((dx * dx) + (dy * dy) + (dz * dz) > radius * radius)
                            {
                                continue;
                            }

                            total++;
                            var p = j + new Vector3(dx, dy, dz);
                            filled += local.Any(c => MathF.Abs(c.P.X - p.X) < 0.0025f && MathF.Abs(c.P.Y - p.Y) < 0.0025f && MathF.Abs(c.P.Z - p.Z) < 0.0025f) ? 1 : 0;
                        }
                    }
                }

                TestContext.WriteLine($"  radius {radius * 100:0} cm: filled {filled}/{total} = {100f * filled / total:0}%");
            }

            SliceMap(local, j, "front slice (X across, Y up) at joint Z", p => (p.X, p.Y), p => p.Z, j.Z);
            SliceMap(local, j, "top slice (X across, Z forward) at joint Y", p => (p.X, p.Z), p => p.Y, j.Y);
            SliceMap(local, j, "side slice (Z forward, Y up) at joint X", p => (p.Z, p.Y), p => p.X, j.X);
        }
    }

    private void SliceMap(List<(Vector3 P, BodyPartKind Kind)> cells, Vector3 centre, string title, Func<Vector3, (float A, float B)> axes, Func<Vector3, float> depth, float plane)
    {
        const int half = 16;
        const float cell = 0.005f;
        var (ca, cb) = axes(centre);
        var sb = new StringBuilder();
        sb.AppendLine(title);
        for (var j = half; j >= -half; j--)
        {
            var row = new StringBuilder();
            for (var i = -half; i <= half; i++)
            {
                var kinds = new HashSet<BodyPartKind>();
                foreach (var (p, kind) in cells)
                {
                    var (a, b) = axes(p);
                    if (MathF.Abs(depth(p) - plane) < cell * 0.5f && MathF.Abs(a - (ca + (i * cell))) < cell * 0.5f && MathF.Abs(b - (cb + (j * cell))) < cell * 0.5f)
                    {
                        kinds.Add(kind);
                    }
                }

                row.Append(i == 0 && j == 0 && kinds.Count == 0 ? '+' : kinds.Count == 0 ? '.' : kinds.Count > 1 ? '#' : kinds.First() switch
                {
                    BodyPartKind.Chest => 'C',
                    BodyPartKind.UpperArm => 'A',
                    BodyPartKind.Forearm => 'F',
                    BodyPartKind.Head => 'H',
                    BodyPartKind.Pelvis => 'P',
                    _ => '?',
                });
            }

            sb.AppendLine(row.ToString());
        }

        TestContext.WriteLine(sb.ToString());
    }

    [TestMethod]
    public void BodyBoxesInTheBodyFrame()
    {
        using var game = new BoxingGame(VersusLab_Tuning(), 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard(), CpuWeapon: WeaponKind.Katana, PlayerWeapon: WeaponKind.Katana, CpuTwoHanded: true, PlayerTwoHanded: true));
        for (var i = 0; i < 120; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        TestContext.WriteLine($"arm reach {fighter.ArmReach}");
        var frame = fighter.Frame;
        foreach (var part in fighter.Voxels!.Parts)
        {
            var grid = part.Body.Grid;
            var toWorld = part.Body.GridToWorld();
            var (nx, ny, nz) = grid.Dimensions;
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        if (grid.At(x, y, z) != 0)
                        {
                            var p = ToLocal(frame, Vector3.Transform(grid.CellCenter(x, y, z), toWorld));
                            lo = Vector3.Min(lo, p);
                            hi = Vector3.Max(hi, p);
                        }
                    }
                }
            }

            TestContext.WriteLine($"{part.Kind,-10} lo {lo:0.000} hi {hi:0.000}");
        }
    }

    private static FighterTuning VersusLab_Tuning() => FighterTuning.Default with { WristServoHz = SwordStance.Tuning.WristServoHz };

    private static Vector3 ToLocal(BodyFrame frame, Vector3 world)
    {
        return frame.WorldToLocal(world);
    }

    private void Map(List<(Vector3 P, BodyPartKind Kind)> cells, Vector3 centre, string title, Func<Vector3, (float A, float B)> axes, float cell)
    {
        const int half = 30;
        var (ca, cb) = axes(centre);
        var sb = new StringBuilder();
        sb.AppendLine(title);
        // 縦の列を先に見て、同じ投影位置に複数の部位があれば重なりと示す。
        for (var j = half; j >= -half; j--)
        {
            var row = new StringBuilder();
            for (var i = -half; i <= half; i++)
            {
                var kinds = new HashSet<BodyPartKind>();
                foreach (var (p, kind) in cells)
                {
                    var (a, b) = axes(p);
                    if (MathF.Abs(a - (ca + (i * cell))) < cell * 0.5f && MathF.Abs(b - (cb + (j * cell))) < cell * 0.5f)
                    {
                        kinds.Add(kind);
                    }
                }

                row.Append(kinds.Count == 0 ? '.' : kinds.Count > 1 ? '#' : kinds.First() switch
                {
                    BodyPartKind.Chest => 'C',
                    BodyPartKind.UpperArm => 'A',
                    BodyPartKind.Forearm => 'F',
                    BodyPartKind.Head => 'H',
                    BodyPartKind.Pelvis => 'P',
                    _ => '?',
                });
            }

            sb.AppendLine(row.ToString());
        }

        TestContext.WriteLine(sb.ToString());
    }
}
