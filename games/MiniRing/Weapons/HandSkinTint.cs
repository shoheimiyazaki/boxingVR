using System.Numerics;
using Aigis.Render.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 手の皮膚（<see cref="BoxerAnatomy.HandSkin"/>）を、持ち主の体の肌の色（<see cref="BoxerBody.SkinColor"/>）で描くための塗り直し（見た目だけ。
/// 材質・物性・チェックサムには関わらない）。手の格子は共通の材質の表（<see cref="BoxerAnatomy.Palette"/>）から作るので、色は描くときに替える。
/// 顔・首・体の肌の色と手の肌の色がずれないよう、肌の色の出どころは一つ（名簿の顔の肌: <see cref="FaceLayer.LookWith"/>）。
/// </summary>
public sealed class HandSkinTint
{
    private readonly VoxelRecolor _recolor;

    public HandSkinTint(Vector4 skin)
    {
        Key = HashCode.Combine(skin.X, skin.Y, skin.Z) | 1; // 0 は「塗り直し無し」の印なので避ける
        var color = skin with { W = 1f };
        _recolor = (material, original, _, _, _) => material == BoxerAnatomy.HandSkin ? color with { W = original.W } : original;
    }

    /// <summary><c>VoxelVolume.Sync</c> に渡す、塗り直しの名前（肌の色が違えば違う値）。</summary>
    public int Key { get; }

    public VoxelRecolor Recolor => _recolor;
}
