using System.Numerics;
using Aigis.Foundation.Voxels;

namespace Aigis.Physics.Voxels;

/// <summary>
/// ひとつの打撃・接触で剥がれたセルを、いっしょに剥がれる塊に分ける（2026-11-25、ユーザーの指定「なるべく塊で剥がれてほしい」）。
/// 格子の上で面で接し、同じ組織（<see cref="VoxelTissue"/>: 皮膚と筋肉の肉・骨・臓器・固い物）のセルは 1 つの塊のまま剥がれる。
/// 切れ目は材料から: 面で接していない所と、組織が変わる所（違う組織どうしのつながりは弱く、肉と骨はそこで分かれる）。
/// 数・大きさ・割合の閾値は持たない（1 セルだけの塊は欠片として飛ぶ）。刃で斬っても、拳・鈍器で潰しても、同じ分け方。
/// 決定的: 塊の並びは、各塊のいちばん若いセル（<c>removed</c> の順）の順、塊の中は幅優先でたどった順。
/// </summary>
public static class VoxelClumps
{
    /// <summary><paramref name="removed"/>（<paramref name="grid"/> から剥がれたセル）の塊: 塊ごとの、<paramref name="removed"/> の添字。</summary>
    public static List<List<int>> Of(VoxelGrid grid, IReadOnlyList<RemovedVoxel> removed)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(removed);
        var palette = grid.Palette;
        var cells = new int[removed.Count];
        var index = new Dictionary<int, int>(removed.Count);
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = grid.CellAt(removed[i].LocalCenter);
            if (cells[i] >= 0)
            {
                index.TryAdd(cells[i], i);
            }
        }

        var (nx, ny, nz) = grid.Dimensions;
        var group = new int[removed.Count];
        Array.Fill(group, -1);
        var groups = new List<List<int>>();
        for (var i = 0; i < cells.Length; i++)
        {
            if (group[i] >= 0)
            {
                continue;
            }

            var tissue = palette[removed[i].Material].Tissue;
            var members = new List<int> { i };
            group[i] = groups.Count;
            for (var m = 0; m < members.Count; m++)
            {
                var c = cells[members[m]];
                if (c < 0)
                {
                    continue;
                }

                var (x, y, z) = grid.Coordinates(c);
                for (var f = 0; f < 6; f++)
                {
                    var (ax, ay, az) = f switch
                    {
                        0 => (x + 1, y, z),
                        1 => (x - 1, y, z),
                        2 => (x, y + 1, z),
                        3 => (x, y - 1, z),
                        4 => (x, y, z + 1),
                        _ => (x, y, z - 1),
                    };
                    if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz)
                    {
                        continue;
                    }

                    if (index.TryGetValue(grid.Index(ax, ay, az), out var j) && group[j] < 0 && palette[removed[j].Material].Tissue == tissue)
                    {
                        group[j] = groups.Count;
                        members.Add(j);
                    }
                }
            }

            groups.Add(members);
        }

        return groups;
    }

    /// <summary>
    /// 塊 <paramref name="clump"/> のセル <paramref name="localCenter"/> の剥がれた断面（+X, −X, +Y, −Y, +Z, −Z の 1 ビットずつ）:
    /// 体に残った埋まったセルか、別の塊に行ったセル（<paramref name="clumpAt"/>: 格子の番号 → 塊）に面していた面。
    /// </summary>
    public static byte TornFaces(VoxelGrid grid, Vector3 localCenter, IReadOnlyDictionary<int, int> clumpAt, int clump)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(clumpAt);
        var at = grid.CellAt(localCenter);
        if (at < 0)
        {
            return 0;
        }

        var (x, y, z) = grid.Coordinates(at);
        var (nx, ny, nz) = grid.Dimensions;
        byte torn = 0;
        for (var f = 0; f < 6; f++)
        {
            var (ax, ay, az) = f switch
            {
                0 => (x + 1, y, z),
                1 => (x - 1, y, z),
                2 => (x, y + 1, z),
                3 => (x, y - 1, z),
                4 => (x, y, z + 1),
                _ => (x, y, z - 1),
            };
            if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz)
            {
                continue;
            }

            var neighbour = grid.Index(ax, ay, az);
            if (grid.MaterialAt(neighbour) != 0 || (clumpAt.TryGetValue(neighbour, out var other) && other != clump))
            {
                torn |= (byte)(1 << f);
            }
        }

        return torn;
    }

    /// <summary>塊ごとの格子の番号の表（<see cref="TornFaces"/> 用）。</summary>
    public static Dictionary<int, int> CellsOf(VoxelGrid grid, IReadOnlyList<RemovedVoxel> removed, List<List<int>> clumps)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(clumps);
        var at = new Dictionary<int, int>(removed.Count);
        for (var g = 0; g < clumps.Count; g++)
        {
            foreach (var i in clumps[g])
            {
                if (grid.CellAt(removed[i].LocalCenter) is var c and >= 0)
                {
                    at.TryAdd(c, g);
                }
            }
        }

        return at;
    }
}
