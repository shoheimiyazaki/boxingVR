using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

internal static class AlienAnatomy
{
    private const byte Fang = BoxerAnatomy.GloveTrim + 1;

    public static VoxelPalette Palette { get; } = ArmorPalette();

    private static VoxelPalette ArmorPalette()
    {
        var original = BoxerAnatomy.Palette;
        var enamel = original[BoxerAnatomy.Bone] with { Name = "fang" };
        var cells = Enumerable.Range(0, original.Count).Select(i => original[(byte)i]).Append(enamel).ToArray();
        var shell = new Vector4(0.52f, 0.49f, 0.47f, 1f);
        cells[BoxerAnatomy.Bone] = cells[BoxerAnatomy.Bone] with { SurfaceColor = shell };
        cells[BoxerAnatomy.HandBone] = cells[BoxerAnatomy.HandBone] with { SurfaceColor = new Vector4(0.15f, 0.12f, 0.11f, 1f) };
        // 筋肉・心臓・肺はエイリアンの材質（見た目と削れ方は人と同じ、働きの値は TissuePhysiology）。
        cells[BoxerAnatomy.Muscle] = cells[BoxerAnatomy.Muscle] with { Name = TissuePhysiology.AlienNames.Muscle };
        cells[BoxerAnatomy.Heart] = cells[BoxerAnatomy.Heart] with { Name = TissuePhysiology.AlienNames.Heart };
        cells[BoxerAnatomy.Lung] = cells[BoxerAnatomy.Lung] with { Name = TissuePhysiology.AlienNames.Lung };
        var eye = cells[BoxerAnatomy.Eye];
        cells[BoxerAnatomy.Eye] = eye with { SurfaceColor = eye.SurfaceColor with { W = 0.5f } };
        return new VoxelPalette(cells);
    }

    public static VoxelGrid Head(Vector3 half, VoxelPalette palette)
    {
        return new VoxelGrid(palette, half, 0.0038f, (_, n) =>
        {
            var x = MathF.Abs(n.X);
            var y = n.Y;
            var z = n.Z;
            var brow = 0.99f - (0.67f * x) - (0.55f * MathF.Abs(y - 0.19f));
            // Four eye sockets on each face of the wedge. The pupils and whites occupy the same surface.
            var eyeX = x - (x > 0.45f ? 0.61f : 0.29f);
            var eyeY = y - (y > 0.26f ? 0.41f : 0.11f);
            var eyeRadius = new Vector2(eyeX / 0.129f, eyeY / 0.112f).LengthSquared();
            if (eyeRadius < 1f && z >= brow - 0.12f && z <= brow + (0.085f * (1f - eyeRadius)))
            {
                return eyeRadius < 0.33f ? BoxerAnatomy.Pupil : BoxerAnatomy.Eye;
            }

            var back = -0.82f + (0.28f * x) + (0.12f * MathF.Abs(y));
            var roof = 0.85f - (0.28f * MathF.Max(0f, z + 0.16f)) - (0.33f * x);
            var upperJaw = -0.32f + (0.23f * MathF.Max(0f, z + 0.24f));
            var width = 0.83f - (0.46f * MathF.Max(0f, z - 0.33f)) - (0.17f * MathF.Abs(y - 0.21f));
            var cranium = x < width && z > back && z < brow && y > upperJaw;
            if (cranium && y <= roof)
            {
                var brain = new Vector3(x / 0.61f, (y - 0.12f) / 0.45f, (z + 0.27f) / 0.48f);
                return brain.LengthSquared() < 1f ? BoxerAnatomy.Brain : BoxerAnatomy.Bone;
            }

            var ridge = PlateRelief(z + (0.26f * x), x, 3);
            if (cranium && y <= roof + (0.15f * ridge))
            {
                return BoxerAnatomy.Bone;
            }

            var jawWidth = 0.61f - (0.41f * z);
            if (z is > -0.20f and < 0.94f && x < jawWidth)
            {
                var chin = -0.77f + (0.29f * z);
                var lowerJaw = chin + 0.14f;
                if (y > chin && y <= lowerJaw)
                {
                    return BoxerAnatomy.Bone;
                }

                // The two rows interleave along the open jaw. Tooth height tapers away from each root.
                if (z > 0.06f && x > 0.55f * jawWidth && y > lowerJaw && y < upperJaw)
                {
                    var phase = (6f * z) - MathF.Floor(6f * z);
                    var rib = 1f - (2f * MathF.Abs(phase - 0.5f));
                    if ((rib > 0.43f && z < brow && y > upperJaw - (0.26f * rib))
                        || (rib < 0.52f && y < lowerJaw + (0.14f * (1f - rib))))
                    {
                        return Fang;
                    }
                }
            }

            var neck = (x * x) + (2.2f * (z + 0.19f) * (z + 0.19f));
            return y < -0.24f && neck < 0.18f
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

    // Each plate thickens toward its free lip, then drops into the seam of the next plate.
    private static float PlateRelief(float along, float across, int count)
    {
        var band = (along + 1f) * (count * 0.5f);
        var phase = band - MathF.Floor(band);
        var width = 0.93f - (0.24f * phase * phase);
        var edge = Math.Clamp((width - MathF.Abs(across)) / 0.24f, 0f, 1f);
        return phase is > 0.13f and < 0.91f ? edge * MathF.Min(1f, phase * 1.3f) : 0f;
    }

    public static VoxelGrid Plate(BodyPartKind kind, VoxelGrid grid, VoxelPalette palette, bool mirrorX) =>
        new(palette, grid.HalfExtents, grid.Dimensions, (_, n) =>
        {
            var (nx, ny, nz) = grid.Dimensions;
            static int Cell(float value, int size) => Math.Clamp((int)((value + 1f) * 0.5f * size), 0, size - 1);
            byte At(Vector3 point) => grid.At(Cell(point.X, nx), Cell(point.Y, ny), Cell(point.Z, nz));
            var material = At(n);
            // The lower thorax meets the front dorsal shell across their overlap.
            if (material == 0 && kind == BodyPartKind.Chest && n.Y < -0.55f
                && (n.X * n.X) + (1.5f * n.Z * n.Z) < 0.48f)
            {
                return BoxerAnatomy.Bone;
            }
            if (material is not (0 or BoxerAnatomy.Skin))
            {
                return material;
            }

            var arm = kind is BodyPartKind.UpperArm or BodyPartKind.Forearm;
            var lateral = MathF.Abs(n.X);
            var (along, across, radial, plateCount, covered) = kind switch
            {
                BodyPartKind.Pelvis => (n.Z + (0.26f * lateral), lateral, new Vector3(n.X, n.Y, 0f), 5, n.Y > -0.14f),
                BodyPartKind.Chest => (-n.Y - (0.29f * lateral), lateral, new Vector3(n.X, 0f, n.Z), 3, n.Z > 0.10f),
                _ when arm => (n.X * (mirrorX ? 1f : -1f) + (0.16f * MathF.Abs(n.Y)), n.Y, new Vector3(0f, n.Y, n.Z), 3, n.Z > -0.15f),
                _ => (-n.Y + (0.24f * lateral), lateral, new Vector3(n.X, 0f, n.Z), 3, n.Z > -0.17f),
            };
            var relief = covered ? PlateRelief(along, across, plateCount) : 0f;
            if (relief <= 0f)
            {
                return material;
            }

            var inside = At(n - (0.42f * relief * radial));
            return material == BoxerAnatomy.Skin || inside != 0 ? BoxerAnatomy.Bone : material;
        });
}
