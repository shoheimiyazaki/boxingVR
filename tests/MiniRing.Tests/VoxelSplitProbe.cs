namespace MiniRing.Tests;

/// <summary>調べもの: 関節の接し目の組の数（<see cref="Voxels.BoxerBody.JointContactCount"/>）と、部位の格子の寸法・材質ごとの高さ（切る段を選ぶため）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class VoxelSplitProbe
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void Probe_Geometry()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        var f = game.Cpu;
        var v = f.Voxels!;
        foreach (var (joint, parent, child, _) in f.BridgeJoints())
        {
            var (total, held) = v.JointContactCount(parent, child);
            Console.WriteLine($"{joint}: pairs {total} held {held}");
        }

        foreach (var part in v.Parts)
        {
            var g = part.Body.Grid;
            Console.WriteLine($"{part.Kind} id {part.Body.BodyId} dims {g.Dimensions} cell {g.CellSize.X * 1000f:0.0} mm filled {g.FilledCount} mass {game.World.GetMass(part.Body.BodyId):0.00}");
            var ranges = new SortedDictionary<string, (int Lo, int Hi, int N)>();
            for (var i = 0; i < g.CellCount; i++)
            {
                var m = g.MaterialAt(i);
                if (m == 0)
                {
                    continue;
                }

                var name = g.Palette[m].Name;
                var y = g.Coordinates(i).Y;
                ranges[name] = ranges.TryGetValue(name, out var r) ? (Math.Min(r.Lo, y), Math.Max(r.Hi, y), r.N + 1) : (y, y, 1);
            }

            Console.WriteLine("   " + string.Join(", ", ranges.Select(kv => $"{kv.Key} y{kv.Value.Lo}-{kv.Value.Hi} ({kv.Value.N})")));
        }
    }
}
