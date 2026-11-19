using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: 素手の拳と握る手の格子を、セルの中心と材質の表（CSV）に書き出す（tools/hand_views.py が正面・横・上の 3 方向の絵にする）。
/// 書き出す所は環境変数 <c>HAND_DUMP_DIR</c>（無ければ一時フォルダ）。比べるために前腕の箱の寸法と手首の位置も書く。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HandShapeDumpProbe
{
    /// <summary>絵の柄の半径 [m]（直径 10 mm: 剣の柄 8 mm と棍棒の柄 14 mm の間）。</summary>
    private const float GripRadius = 0.005f;

    [TestMethod]
    public void Probe_DumpHandShapes()
    {
        var dir = Environment.GetEnvironmentVariable("HAND_DUMP_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        var fist = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.Seiken), out var fistGrid);
        Dump(Path.Combine(dir, "fist.csv"), fistGrid, thumb: fist);
        var bar = HandSkeleton.HeldBar(GripRadius);
        var gripState = new BareHandState().Placed(HandSkeleton.GripPose, out var grip, bar);
        Dump(Path.Combine(dir, "grip.csv"), grip, bar, gripState);

        DumpWorn(dir);
        var tuning = new FighterTuning();
        var forearm = Aigis.Physics.HumanoidRagdoll.HalfExtents.Default.Forearm * tuning.Scale;
        File.WriteAllText(Path.Combine(dir, "forearm.txt"), string.Create(CultureInfo.InvariantCulture,
            $"{forearm.X * 2f * tuning.ArmLengthScale},{forearm.Y * 2f},{forearm.Z * 2f},{HandSkeleton.Wrist.X},{HandSkeleton.Wrist.Y}"));
        Console.WriteLine($"dumped to {dir}");
    }

    /// <summary>
    /// はめる物 5 種（メリケンサック・カタール・籠手・トンファー・盾）の、物の格子と中の手の格子を、手の座標（原点は手の格子の中心）で
    /// <c>worn_&lt;種類&gt;.csv</c> に書く（<c>py -3 tools/hand_views.py &lt;dir&gt; --worn</c> で絵にする）。
    /// </summary>
    private static void DumpWorn(string dir)
    {
        foreach (var kind in new[] { WeaponKind.KnuckleDuster, WeaponKind.Katar, WeaponKind.Gauntlet, WeaponKind.Tonfa, WeaponKind.Shield })
        {
            using var world = new Aigis.Physics.PhysicsWorld(gravity: Vector3.Zero);
            var weapon = Weapon.CreateWorn(world, kind, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);
            var hand = weapon.Hands[0];
            var text = new StringBuilder("x,y,z,material,cell\n");
            void Add(VoxelGrid grid, Vector3 shift)
            {
                for (var i = 0; i < grid.CellCount; i++)
                {
                    if (grid.MaterialAt(i) == 0)
                    {
                        continue;
                    }

                    var (x, y, z) = grid.Coordinates(i);
                    var c = grid.CellCenter(x, y, z) + shift;
                    text.Append(CultureInfo.InvariantCulture, $"{c.X},{c.Y},{c.Z},{BoxerAnatomy.Palette[grid.MaterialAt(i)].Name},{grid.CellSize.X}\n");
                }
            }

            Add(hand.Body.Grid, Vector3.Zero);
            Add(weapon.Body.Grid, -hand.GridCenterLocal);
            File.WriteAllText(Path.Combine(dir, $"worn_{kind}.csv"), text.ToString());
            Console.WriteLine($"{kind}: grip {Weapon.GripOf(kind)}, tip {Weapon.TipOf(kind)}, mass {world.GetMass(weapon.Body.BodyId):F4} kg, object cells {weapon.Body.Grid.FilledCount}, hand cells {hand.Body.Grid.FilledCount}, cover {Weapon.CoverRadiusOf(kind):F4}");
        }
    }

    /// <param name="path">書き出す CSV。</param>
    /// <param name="grid">手の格子。</param>
    /// <param name="held">握っている柄の所（あれば handle として書く）。</param>
    /// <param name="thumb">渡すと、親指（<see cref="HandSkeleton.Thumb"/>）のセルの材質の名前に " thumb" を付ける（絵で色を変える）。</param>
    private static void Dump(string path, VoxelGrid grid, Func<Vector3, bool>? held = null, BareHandState? thumb = null)
    {
        var thumbCells = new HashSet<int>();
        if (thumb is not null)
        {
            for (var i = 0; i < HandSkeleton.Parents.Count; i++)
            {
                if (HandSkeleton.Parents[i].Finger == HandSkeleton.Thumb)
                {
                    thumbCells.Add(thumb.Placement[i]);
                }
            }
        }

        var text = new StringBuilder("x,y,z,material,cell\n");
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            var c = grid.CellCenter(x, y, z);
            var material = grid.MaterialAt(i);
            var name = material != 0 ? BoxerAnatomy.Palette[material].Name + (thumbCells.Contains(i) ? " thumb" : "") : held?.Invoke(c) == true ? "handle" : null;
            if (name is null)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"{c.X},{c.Y},{c.Z},{name},{grid.CellSize.X}\n");
        }

        File.WriteAllText(path, text.ToString());
    }
}
