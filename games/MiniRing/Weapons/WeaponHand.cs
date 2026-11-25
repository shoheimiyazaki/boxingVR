using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 武器（はめる物・握る物）の剛体に重ねた手の格子 1 つ。手は 10.56 mm の格子（<see cref="HandSkeleton"/>）で、物の格子（<see cref="Weapon.Body"/>）とは
/// 別の格子だが、同じ剛体に乗る（<see cref="VoxelBody.AddCompanion"/>）: 質量と当たりの箱は両方の格子の和、物が折れても手は切り離されない。
/// 手に持つ物（剣・棒など）の握り手は、握っている間だけ組織を置き、放すと空にする（<see cref="Fill"/>・<see cref="Empty"/>）。
/// </summary>
public sealed class WeaponHand
{
    internal WeaponHand(VoxelBody body, BareHandState state)
    {
        Body = body;
        State = state;
        Volume = VoxelVolume.For(body.Grid);
    }

    /// <summary>手の格子（物の格子と同じ剛体 id。接触・削れ・描画はこの格子について行う）。</summary>
    public VoxelBody Body { get; }

    /// <summary>手の組織の台帳（骨・肉・皮膚ごとの残り）。握り手では、最後に置いた（または空にした）ときの台帳。</summary>
    public BareHandState State { get; private set; }

    /// <summary>描画用の 3D テクスチャ。</summary>
    public VoxelVolume Volume { get; private set; }

    /// <summary>手の格子の中心が、武器のローカル座標のどこにあるか。</summary>
    public Vector3 GridCenterLocal => Body.GridCenterLocal;

    /// <summary>いま手の組織が置いてある（握り手が握っている）。はめる物の手はいつも true。</summary>
    public bool Occupied { get; private set; } = true;

    /// <summary>
    /// 握り手に台帳 <paramref name="ledger"/>（傷のまま）の手を、姿勢 <paramref name="pose"/> で、握る物 <paramref name="held"/> を避けて置き、
    /// 格子の中心を武器のローカルの <paramref name="gridCenterLocal"/> へ動かす。剛体の質量と当たりの箱は手の分だけ増える。
    /// </summary>
    internal void Fill(BareHandState ledger, HandPose pose, Func<Vector3, bool> held, Vector3 gridCenterLocal)
    {
        State = ledger.Placed(pose, out var grid, held);
        Body.ReplaceGrid(grid);
        Volume = VoxelVolume.For(grid);
        Occupied = true;
        Body.MoveCompanion(gridCenterLocal);
        Body.UpdateMass();
    }

    /// <summary>空の格子で作った握り手を、まだ組織を置いていない（台帳は傷の無い手）ことにする。</summary>
    internal void MarkEmpty() => Occupied = false;

    /// <summary>握り手を空にする（手が物を放した）。いまの傷を読み直した台帳を返す（手は持ち主の手のまま、傷も持ち主に残る）。</summary>
    internal BareHandState Empty()
    {
        var ledger = Occupied ? State.Capture(Body.Grid) : State;
        State = ledger;
        var grid = Body.Grid;
        Body.ReplaceGrid(new VoxelGrid(grid.Palette, grid.HalfExtents, grid.Dimensions, (_, _) => 0));
        Volume = VoxelVolume.For(Body.Grid);
        Occupied = false;
        Body.UpdateMass();
        return ledger;
    }

    /// <summary>握り手の格子の中心を、武器のローカルの <paramref name="gridCenterLocal"/> へ動かす（握りを柄に沿って滑らせた）。</summary>
    internal void MoveTo(Vector3 gridCenterLocal) => Body.MoveCompanion(gridCenterLocal);

    /// <summary>この手を描く指示（格子の変化をテクスチャへ写す）。</summary>
    internal VoxelVolumeDraw Draw(HandSkinTint? skin = null)
    {
        Volume.Sync(Body.Grid, skin?.Key ?? 0, skin?.Recolor);
        return new VoxelVolumeDraw(Volume, VoxelVolume.CellToWorld(Body.Grid, Body.GridToWorld()));
    }
}
