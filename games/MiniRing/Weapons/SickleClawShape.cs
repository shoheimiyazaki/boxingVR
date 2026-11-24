using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>曲がった刀身と刃元を一つの格子に彫る。制御する弦は刃元から切っ先まで。</summary>
internal static class SickleClawShape
{
    private const float Length = 0.46f;
    private const float Root = 0.038f;
    private const float Curve = 0.226f;
    private static readonly Vector3 Half = new((Length + Root) * 0.5f, 0.035f, 0.245f);

    public static Vector3 Grip => new(Root - Half.X, 0f, 0f);
    public static Vector3 Tip => new(Half.X, 0f, Curve);
    public static Vector3 Edge(Hand hand) => (Tip - Grip) with { Z = hand == Hand.Left ? -Curve : Curve };

    private static readonly VoxelPalette BladePalette = LightenBlade();

    private static VoxelPalette LightenBlade()
    {
        var original = AlienAnatomy.Palette;
        var materials = Enumerable.Range(0, original.Count).Select(i => original[(byte)i]).ToArray();
        materials[BoxerAnatomy.Bone] = materials[BoxerAnatomy.Bone] with { SurfaceColor = new Vector4(0.59f, 0.48f, 0.39f, 1f) };
        return new VoxelPalette(materials);
    }

    public static VoxelGrid Build(bool mirrored) => new(BladePalette, Half, 0.0056f, (p, _) =>
    {
        var hand = p - Grip;
        var z = hand.Z * (mirrored ? -1f : 1f);
        var along = Math.Clamp(hand.X / Length, 0f, 1f);
        var spine = Curve * along * along * along - 0.032f * MathF.Sin(MathF.PI * along);
        var width = 0.059f * (1f - along) + 0.005f;
        var edge = MathF.Abs(z - spine) / width;
        var thickness = MathF.Max(0.0034f, 0.024f * (1f - edge) * (1f - 0.75f * along));
        if (edge <= 1f && MathF.Abs(hand.Y) <= thickness)
        {
            return z > spine && edge < 0.61f && along < 0.82f ? BoxerAnatomy.HandBone : BoxerAnatomy.Bone;
        }

        return 0;
    });
}
