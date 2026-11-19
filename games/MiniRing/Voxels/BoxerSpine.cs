using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>脊髄が通る高さ（神経が体のどこまでを動かすか）。上から順に、下の高さはその上の高さが生きていてはじめて働く。</summary>
public enum CordLevel
{
    /// <summary>頸（首）。腕・息・首の筋を動かす。頭の下の端の、頸椎の中。</summary>
    Cervical,

    /// <summary>胸。胴の筋を動かす。胸の背骨の中。</summary>
    Thoracic,

    /// <summary>腰。腰・脚を動かす。腰の背骨の中。</summary>
    Lumbar,
}

/// <summary>
/// 首の骨（頸椎）と背骨の中の脊髄を、組み立てた部位の格子へ足す（<see cref="BoxerBody.BuildPartGrid"/> の最後。編集した頭・直した中身の
/// 外見のセルには触れず、中の肉・骨のセルの材質だけを置き換える。だから数も形も質量も変わらない）。
/// <list type="bullet">
/// <item>頭: 首（頭の箱の下の端、<see cref="NeckFirstRow"/> から <see cref="NeckRows"/> 段）に、頸椎（骨 <see cref="BoxerAnatomy.Bone"/>）を前後 2 本、
/// その間に脊髄（<see cref="BoxerAnatomy.Cord"/>）を 2 × 2 セル。</item>
/// <item>胸・腰: 背骨の柱（背中側の骨の柱）の真ん中を脊髄に置き換える（外の骨の柱は残る）。表面のセルは置き換えない（脊髄は皮膚の下）。</item>
/// </list>
/// </summary>
public static class BoxerSpine
{
    /// <summary>首の骨が始まる、頭の格子の下から数えた段（0 が箱の底。底の段は空、次の段は皮膚だけなので、骨は 2 段目から）。</summary>
    public const int NeckFirstRow = 2;

    /// <summary>首の骨・脊髄の段数。</summary>
    public const int NeckRows = 2;

    /// <summary>頭の中で、首が折れうる段（含む）。頭の骨は頭蓋の殻で、折れるのは首の細い所だけ。</summary>
    public static (int Low, int High) NeckSlices => (NeckFirstRow, NeckFirstRow + NeckRows - 1);

    /// <summary>その部位の脊髄の高さ（脊髄が無い部位は null）。</summary>
    public static CordLevel? LevelOf(BodyPartKind kind) => kind switch
    {
        BodyPartKind.Head => CordLevel.Cervical,
        BodyPartKind.Chest => CordLevel.Thoracic,
        BodyPartKind.Pelvis => CordLevel.Lumbar,
        _ => null,
    };

    /// <summary>
    /// 部位 <paramref name="kind"/> の格子に、頸椎・脊髄を置く（作った直後、削れる前に 1 度だけ）。胸・腰は、中身にもう脊髄がある格子（CT から作った体:
    /// 脊髄は CT の位置にある）には置かない（2 本目の脊髄を重ねて塗らない）。頭の首の段は頭の箱の下の端に置くゲームの作りで（CT の頭の部位は頭蓋の底より上
    /// なので、本物の頸椎はそこに写っていない）、いつも置く。
    /// </summary>
    public static void Place(BodyPartKind kind, VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        switch (kind)
        {
            case BodyPartKind.Head:
                PlaceNeck(grid);
                break;
            case BodyPartKind.Chest or BodyPartKind.Pelvis when !HasCord(grid):
                PlaceCord(grid);
                break;
        }
    }

    private static bool HasCord(VoxelGrid grid)
    {
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.AtIndex(i) == BoxerAnatomy.Cord)
            {
                return true;
            }
        }

        return false;
    }

    private static void PlaceNeck(VoxelGrid grid)
    {
        var (nx, ny, nz) = grid.Dimensions;
        if (ny < NeckFirstRow + NeckRows + 4)
        {
            return;
        }

        var cx = nx / 2;
        var cz = nz * 4 / 10; // 首は頭の前後の真ん中より少し後ろ
        grid.Remap((c, material, surface) =>
        {
            if (surface || c.Y < NeckFirstRow || c.Y >= NeckFirstRow + NeckRows || c.X < cx - 1 || c.X > cx || c.Z < cz - 1 || c.Z > cz + 2
                || material is not (BoxerAnatomy.Bone or BoxerAnatomy.Muscle))
            {
                return material;
            }

            return c.Z == cz - 1 || c.Z == cz + 2 ? BoxerAnatomy.Bone : BoxerAnatomy.Cord;
        });
    }

    private static void PlaceCord(VoxelGrid grid)
    {
        var (nx, _, nz) = grid.Dimensions;
        var cx = nx / 2;
        var back = nz / 7; // 背骨の柱は背中側（体の奥行きの 1/7 のあたりから 2 セル）
        // 柱の幅が奇数なら真ん中の 1 列、偶数なら真ん中の 2 列。
        var wide = nx % 2 == 0;
        grid.Remap((c, material, surface) =>
            material == BoxerAnatomy.Bone && !surface && c.Z >= back && c.Z <= back + 1 && (c.X == cx || (wide && c.X == cx - 1))
                ? BoxerAnatomy.Cord
                : material);
    }
}
