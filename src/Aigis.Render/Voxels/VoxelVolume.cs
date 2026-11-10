using System.Numerics;
using Aigis.Foundation.Voxels;

namespace Aigis.Render.Voxels;

/// <summary>
/// A voxel volume as a 3D texture, ready to draw with <see cref="VoxelVolumeDraw"/>: one RGBA8 texel per cell, alpha 255
/// where the cell is filled and 0 where it is empty, RGB the cell's colour. The GPU builds the visible surface from this
/// texture alone (a face between every filled cell and an empty or outside neighbour — see <see cref="VoxelVolumePipeline"/>),
/// so the CPU never touches geometry: changing a cell is one texel.
/// <para>Keyed by instance identity on the GPU side, like <see cref="DynamicTexture"/>: build one per volume and keep it;
/// <see cref="Revision"/> tells the renderer when to upload again.</para>
/// </summary>
public sealed class VoxelVolume
{
    private int _sourceVersion = -1;
    private VoxelGrid? _source;

    public VoxelVolume(int width, int height, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        Width = width;
        Height = height;
        Depth = depth;
        Rgba = new byte[width * height * depth * 4];
    }

    public int Width { get; }

    public int Height { get; }

    public int Depth { get; }

    /// <summary>Texels, x fastest then y then z, four bytes each (RGBA).</summary>
    public byte[] Rgba { get; }

    public int CellCount => Width * Height * Depth;

    /// <summary>Bumped by every change; the renderer uploads when the revision it holds differs.</summary>
    public int Revision { get; private set; }

    /// <summary>What a fully stained cell looks like (dried blood by default) — see <see cref="VoxelGrid.Stain"/>.</summary>
    public Vector4 StainColor { get; init; } = new(0.45f, 0.02f, 0.03f, 1f);

    /// <summary>A volume the size of <paramref name="grid"/>, filled from it.</summary>
    public static VoxelVolume For(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var volume = new VoxelVolume(grid.Dimensions.X, grid.Dimensions.Y, grid.Dimensions.Z);
        volume.Sync(grid);
        return volume;
    }

    /// <summary>
    /// Copies <paramref name="grid"/> into the texels if it changed since the last call (its <see cref="VoxelGrid.Version"/>):
    /// each filled cell in its material's colour — the original outer surface in <see cref="VoxelMaterial.SurfaceColor"/>,
    /// anything exposed by damage in <see cref="VoxelMaterial.Color"/> — tinted toward <see cref="StainColor"/> by its stain.
    /// </summary>
    public void Sync(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Dimensions != (Width, Height, Depth))
        {
            throw new ArgumentException($"Grid {grid.Dimensions} does not match the volume ({Width}, {Height}, {Depth}).", nameof(grid));
        }

        if (ReferenceEquals(grid, _source) && grid.Version == _sourceVersion)
        {
            return;
        }

        _source = grid;
        _sourceVersion = grid.Version;
        for (var z = 0; z < Depth; z++)
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var t = 4 * (x + (Width * (y + (Height * z))));
                    var m = grid.At(x, y, z);
                    if (m == 0)
                    {
                        Rgba[t] = Rgba[t + 1] = Rgba[t + 2] = Rgba[t + 3] = 0;
                        continue;
                    }

                    var material = grid.Palette[m];
                    var color = grid.IsOriginalSurface(x, y, z) ? material.SurfaceColor : material.Color;
                    color = Vector4.Lerp(color, StainColor, 0.85f * grid.Stain(x, y, z));
                    Rgba[t] = ToByte(color.X);
                    Rgba[t + 1] = ToByte(color.Y);
                    Rgba[t + 2] = ToByte(color.Z);
                    Rgba[t + 3] = 255;
                }
            }
        }

        Revision++;
    }

    /// <summary>
    /// Where cell coordinates (cell (x, y, z) spans x..x+1 and so on) land in world space, for a grid placed by
    /// <paramref name="gridToWorld"/> (grid-local metres → world, rotation and translation).
    /// </summary>
    public static Matrix4x4 CellToWorld(VoxelGrid grid, Matrix4x4 gridToWorld)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return Matrix4x4.CreateScale(grid.CellSize) * Matrix4x4.CreateTranslation(-grid.HalfExtents) * gridToWorld;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}

/// <summary>One voxel volume to draw this frame: the texture and where its cell coordinates land in the world.</summary>
/// <param name="Volume">The volume (uploaded on first use and whenever its revision changes).</param>
/// <param name="CellToWorld">Cell coordinates → world (see <see cref="VoxelVolume.CellToWorld"/>).</param>
public readonly record struct VoxelVolumeDraw(VoxelVolume Volume, Matrix4x4 CellToWorld);
