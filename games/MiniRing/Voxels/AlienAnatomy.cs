using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

internal static class AlienAnatomy
{
    public static VoxelPalette Palette { get; } = ArmorPalette();

    private static VoxelPalette ArmorPalette()
    {
        var original = BoxerAnatomy.Palette;
        var cells = Enumerable.Range(0, original.Count).Select(i => original[(byte)i]).ToArray();
        var shell = new Vector4(0.29f, 0.23f, 0.21f, 1f);
        cells[BoxerAnatomy.Bone] = cells[BoxerAnatomy.Bone] with { SurfaceColor = shell };
        cells[BoxerAnatomy.HandBone] = cells[BoxerAnatomy.HandBone] with { SurfaceColor = new Vector4(0.15f, 0.12f, 0.11f, 1f) };
        cells[BoxerAnatomy.Eye] = cells[BoxerAnatomy.Eye] with { SurfaceColor = new Vector4(0.95f, 0.38f, 0.09f, 1f) };
        return new VoxelPalette(cells);
    }

    public static VoxelGrid Head(Vector3 half, VoxelPalette palette)
    {
        return new VoxelGrid(palette, half, 0.006f, (_, n) =>
        {
            var x = MathF.Abs(n.X);
            var skull = new Vector3(n.X / 0.80f, (n.Y + 0.03f) / 0.69f, (n.Z + 0.05f) / 0.78f);
            var dome = skull.LengthSquared();
            var spine = n.Y > 0.35f && n.Z < 0.2f && n.Z > -0.89f
                && MathF.Abs(x - (0.28f + 0.51f * (n.Y - 0.35f))) < 0.055f + 0.13f * (1f - n.Y);
            if (spine)
            {
                return BoxerAnatomy.Bone;
            }

            // 同じ脳・頸椎の組織で、頭蓋の前に開いた顎と左右の眼を作る。
            if (n.Z > 0.21f && x is > 0.42f and < 0.71f && n.Y > -0.31f && n.Y < -0.07f + 0.17f * x)
            {
                return BoxerAnatomy.Eye;
            }

            var jaw = n.Z is > 0.19f and < 0.93f && x < 0.56f - 0.27f * n.Z && n.Y is > -0.78f and < -0.36f;
            if (jaw)
            {
                var fang = x > 0.16f && (n.Z < 0.44f || n.Z > 0.61f) && n.Y < -0.47f - 0.22f * n.Z;
                return fang || n.Y < -0.65f ? BoxerAnatomy.Bone : (byte)0;
            }

            if (dome < 1f)
            {
                return dome < 0.60f && n.Y > -0.45f ? BoxerAnatomy.Brain : BoxerAnatomy.Bone;
            }

            var neck = x * x + 1.5f * (n.Z + 0.19f) * (n.Z + 0.19f);
            return n.Y < -0.43f && neck < 0.18f
                ? neck < 0.10f ? BoxerAnatomy.Muscle : BoxerAnatomy.Skin
                : (byte)0;
        });
    }

    public static VoxelGrid LegSegment(BodyPartKind kind, Vector3 half, VoxelPalette palette)
    {
        // The distal tarsus converges to a tip. The other shells have a muscle core and broad joint ends.
        return new VoxelGrid(palette, half, 0.0035f, (_, n) =>
        {
            var radial = kind == BodyPartKind.Coxa ? new Vector2(n.Y, n.Z) : new Vector2(n.X, n.Z);
            var length = kind == BodyPartKind.Coxa ? n.X : n.Y;
            var width = kind == BodyPartKind.Toe ? 0.26f + (0.71f * (length + 1f) * 0.5f)
                : 0.70f + (0.24f * MathF.Sin((length + 1f) * MathF.PI * 0.5f));
            var r = radial.LengthSquared() / (width * width);
            if (r > 1f)
            {
                return 0;
            }

            return kind == BodyPartKind.Toe || r > 0.65f || MathF.Abs(length) > 0.82f ? BoxerAnatomy.Bone : BoxerAnatomy.Muscle;
        });
    }

    public static VoxelGrid Plate(BodyPartKind kind, VoxelGrid grid, VoxelPalette palette) =>
        new(palette, grid.HalfExtents, grid.Dimensions, (_, n) =>
        {
            var (nx, ny, nz) = grid.Dimensions;
            static int Cell(float value, int size) => Math.Clamp((int)((value + 1f) * 0.5f * size), 0, size - 1);
            var material = grid.At(Cell(n.X, nx), Cell(n.Y, ny), Cell(n.Z, nz));
            var line = kind is BodyPartKind.Pelvis or BodyPartKind.Chest ? n.Z : kind is BodyPartKind.UpperArm or BodyPartKind.Forearm ? MathF.Abs(n.X) : n.Y;
            var seam = MathF.Sin((line + 0.16f * MathF.Abs(n.X)) * 13f);
            var back = kind is BodyPartKind.Pelvis or BodyPartKind.Chest ? n.Y > 0.12f || n.Z < -0.44f : n.Z < 0.40f;
            // The lower thorax meets the front dorsal shell across their overlap.
            if (material == 0 && kind == BodyPartKind.Chest && n.Y < -0.55f
                && (n.X * n.X) + (1.5f * n.Z * n.Z) < 0.48f)
            {
                return BoxerAnatomy.Bone;
            }
            if (material == BoxerAnatomy.Skin && back && seam > -0.25f)
            {
                return BoxerAnatomy.Bone;
            }

            var dorsal = kind is BodyPartKind.Chest or BodyPartKind.Pelvis && material == 0 && n.Y > 0.32f
                && n.Z < -0.40f && MathF.Abs(n.X) < 0.32f * (1f - n.Y) && seam > 0.68f;
            return dorsal ? BoxerAnatomy.Bone : material;
        });
}
