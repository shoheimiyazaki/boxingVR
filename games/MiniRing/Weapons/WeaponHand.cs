using System.Numerics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 武器（はめる物・握る物）の剛体に重ねた手の格子 1 つ。手は 5 mm の格子（<see cref="HandSkeleton"/>）で、物の格子（<see cref="Weapon.Body"/>）とは
/// 別の格子だが、同じ剛体に乗る（<see cref="VoxelBody.AddCompanion"/>）: 質量と当たりの箱は両方の格子の和、物が折れても手は切り離されない。
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

    /// <summary>手の組織の台帳（骨・肉・皮膚ごとの残り）。</summary>
    public BareHandState State { get; }

    /// <summary>描画用の 3D テクスチャ。</summary>
    public VoxelVolume Volume { get; }

    /// <summary>手の格子の中心が、武器のローカル座標のどこにあるか。</summary>
    public Vector3 GridCenterLocal => Body.GridCenterLocal;

    /// <summary>この手を描く指示（格子の変化をテクスチャへ写す）。</summary>
    internal VoxelVolumeDraw Draw()
    {
        Volume.Sync(Body.Grid);
        return new VoxelVolumeDraw(Volume, VoxelVolume.CellToWorld(Body.Grid, Body.GridToWorld()));
    }
}
