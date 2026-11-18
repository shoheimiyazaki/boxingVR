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
        Dump(Path.Combine(dir, "fist.csv"), BareHandState.InitialGrid());
        var bar = HandSkeleton.HeldBar(GripRadius);
        _ = new BareHandState().Placed(HandSkeleton.GripPose, out var grip, bar);
        Dump(Path.Combine(dir, "grip.csv"), grip, bar);

        var tuning = new FighterTuning();
        var forearm = Aigis.Physics.HumanoidRagdoll.HalfExtents.Default.Forearm * tuning.Scale;
        File.WriteAllText(Path.Combine(dir, "forearm.txt"), string.Create(CultureInfo.InvariantCulture,
            $"{forearm.X * 2f * tuning.ArmLengthScale},{forearm.Y * 2f},{forearm.Z * 2f},{HandSkeleton.Wrist.X},{HandSkeleton.Wrist.Y}"));
        Console.WriteLine($"dumped to {dir}");
    }

    private static void Dump(string path, VoxelGrid grid, Func<Vector3, bool>? held = null)
    {
        var text = new StringBuilder("x,y,z,material,cell\n");
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            var c = grid.CellCenter(x, y, z);
            var material = grid.MaterialAt(i);
            var name = material != 0 ? BoxerAnatomy.Palette[material].Name : held?.Invoke(c) == true ? "handle" : null;
            if (name is null)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"{c.X},{c.Y},{c.Z},{name},{grid.CellSize.X}\n");
        }

        File.WriteAllText(path, text.ToString());
    }
}
