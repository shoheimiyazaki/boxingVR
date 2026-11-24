using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>The claw's curved spine and tapered edge share one continuous bone grid.</summary>
internal static class SickleClawShape
{
    private const float Length = 0.30f;
    private const float Root = 0.025f;
    private const float Curve = 0.12f;
    private static readonly Vector3 Half = new((Length + Root) * 0.5f, 0.030f, 0.146f);

    public static Vector3 Grip => new(Root - Half.X, 0f, 0f);
    public static Vector3 Tip => new(Half.X, 0f, Curve);

    public static VoxelGrid Build(bool mirrored) => new(BoxerAnatomy.Palette, Half, (92, 17, 83), (p, _) =>
    {
        var hand = p - Grip;
        var z = hand.Z * (mirrored ? -1f : 1f);
        var along = Math.Clamp(hand.X / Length, 0f, 1f);
        var spine = Curve * along * along;
        var width = 0.052f * (1f - along) + 0.003f;
        var edge = MathF.Abs(z - spine) / width;
        var thickness = MathF.Max(0.002f, 0.024f * (1f - edge) * (1f - 0.7f * along));
        return edge <= 1f && MathF.Abs(hand.Y) <= thickness ? BoxerAnatomy.Bone : (byte)0;
    });
}
