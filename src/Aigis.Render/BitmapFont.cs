using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// A minimal, dependency-free "text rendering" capability — the engine has none today
/// (tasks/board.md's TASK-040). This is deliberately scoped down from a true screen-space
/// HUD pipeline (font atlas texture, orthographic overlay pass, its own shader) to a
/// world-space dot-matrix readout built entirely from existing <see cref="PrimitiveInstance"/>
/// cubes — the same primitive pipeline every game already uses, no new Vulkan pipeline,
/// shader, or coordinate-transform work required. Each "on" cell of a 3-wide x 5-tall
/// glyph becomes one thin flat cube lying in the XZ plane (same convention as the existing
/// zone-marker pattern in <c>games/Game.Triage</c>), laid out left-to-right along +X.
/// <para>
/// This is a genuine interim step, not the final answer: text rendered this way is part of
/// the 3D scene (subject to the camera's perspective/orbit, depth-tested against other
/// geometry, not resolution-independent) rather than a screen-locked overlay. TASK-041/
/// TASK-051 have since landed that screen-space HUD pipeline — <see cref="ScreenText"/> +
/// <see cref="GlyphRasterizer"/> (real OS-font glyphs, any anchor, resolution/camera
/// independent) — but this class is <b>not</b> superseded/removed: <c>games/Game.Triage</c>
/// still uses it for its score/timer readout (world-space floor markers are a deliberate
/// aesthetic there, not just a stopgap), and it remains the lighter-weight choice for a
/// diegetic (in-world) numeric readout that doesn't need real typography. Prefer
/// <see cref="ScreenText"/> for any <em>new</em> screen-locked HUD text.
/// </para>
/// <para>Supported characters: uppercase letters <c>A-Z</c>, digits <c>0-9</c>, <c>-</c>
/// (for negative numbers), <c>.</c>
/// (for decimal readouts), and space (rendered as a blank cell, useful for spacing between
/// numbers). Any other
/// character is silently skipped (rendered as blank) — a HUD readout should never crash a
/// game over an unsupported label character.</para>
/// </summary>
public static class BitmapFont
{
    public const int GlyphWidth = 3;
    public const int GlyphHeight = 5;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["111", "101", "101", "101", "111"],
        ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["111", "001", "111", "100", "111"],
        ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"],
        ['7'] = ["111", "001", "010", "010", "010"],
        ['8'] = ["111", "101", "111", "101", "111"],
        ['9'] = ["111", "101", "111", "001", "111"],
        ['-'] = ["000", "000", "111", "000", "000"],
        ['.'] = ["000", "000", "000", "000", "010"],
        [' '] = ["000", "000", "000", "000", "000"],
        ['A'] = ["010", "101", "111", "101", "101"],
        ['B'] = ["110", "101", "110", "101", "110"],
        ['C'] = ["111", "100", "100", "100", "111"],
        ['D'] = ["110", "101", "101", "101", "110"],
        ['E'] = ["111", "100", "110", "100", "111"],
        ['F'] = ["111", "100", "110", "100", "100"],
        ['G'] = ["111", "100", "101", "101", "111"],
        ['H'] = ["101", "101", "111", "101", "101"],
        ['I'] = ["111", "010", "010", "010", "111"],
        ['J'] = ["001", "001", "001", "101", "111"],
        ['K'] = ["101", "101", "110", "101", "101"],
        ['L'] = ["100", "100", "100", "100", "111"],
        ['M'] = ["101", "111", "111", "101", "101"],
        ['N'] = ["101", "111", "111", "111", "101"],
        ['O'] = ["111", "101", "101", "101", "111"],
        ['P'] = ["111", "101", "111", "100", "100"],
        ['Q'] = ["111", "101", "101", "111", "001"],
        ['R'] = ["111", "101", "111", "110", "101"],
        ['S'] = ["111", "100", "111", "001", "111"],
        ['T'] = ["111", "010", "010", "010", "010"],
        ['U'] = ["101", "101", "101", "101", "111"],
        ['V'] = ["101", "101", "101", "101", "010"],
        ['W'] = ["101", "101", "111", "111", "101"],
        ['X'] = ["101", "101", "010", "101", "101"],
        ['Y'] = ["101", "101", "010", "010", "010"],
        ['Z'] = ["111", "001", "010", "100", "111"],
    };

    /// <summary>Builds one flat, thin <see cref="PrimitiveInstance"/> cube per "on" glyph
    /// cell for <paramref name="text"/>, lying in the XZ plane (matching the existing
    /// zone-marker convention — Y is the thin/up axis). Glyphs are laid out left-to-right
    /// along +X starting at <paramref name="origin"/>'s X, with glyph rows descending along
    /// +Z from <paramref name="origin"/>'s Z (row 0, the glyph's top row, is nearest
    /// <paramref name="origin"/>). One blank column separates glyphs.
    /// <para><b>Orientation is calibrated against a specific viewing convention</b>: an
    /// elevated camera behind the scene on the +Z side looking down and back toward the
    /// origin (<c>Aigis.Player.OrbitCamera</c>'s own default yaw=0/pitch=0.4, the same
    /// convention every existing <c>games/Game.*</c> uses) — the "natural" way a player
    /// looks at this kind of scene. Text is <em>not</em> billboarded, so it reads correctly
    /// only from roughly this viewing side; orbiting to view from the opposite side (or a
    /// very different pitch) will show it mirrored/reversed, an inherent limitation of
    /// unbillboarded world-space text (see this file's own top-level doc comment).</para></summary>
    /// <param name="text">Digits, decimal point, minus, and spaces; any other character renders as blank.</param>
    /// <param name="origin">World position of the top-left cell of the first glyph.</param>
    /// <param name="cellSize">Edge length (world units) of one glyph cell's XZ footprint.</param>
    /// <param name="thickness">Y-axis (up) extent of each cell's cube — kept thin, like a
    /// zone marker, so it reads as a flat floor readout rather than a solid block.</param>
    /// <param name="color">Uniform color for every cell.</param>
    public static IReadOnlyList<PrimitiveInstance> BuildFlatInstances(
        string text, Vector3 origin, float cellSize, float thickness, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cellSize, 0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(thickness, 0f);

        var instances = new List<PrimitiveInstance>();
        var glyphSpacing = (GlyphWidth + 1) * cellSize; // one blank column between glyphs

        for (var charIndex = 0; charIndex < text.Length; charIndex++)
        {
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[charIndex]), out var rows))
            {
                continue;
            }

            var glyphOriginX = origin.X + charIndex * glyphSpacing;
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var col = 0; col < GlyphWidth; col++)
                {
                    if (rows[row][col] != '1')
                    {
                        continue;
                    }

                    var cellPosition = new Vector3(glyphOriginX + col * cellSize, origin.Y, origin.Z + row * cellSize);
                    instances.Add(new PrimitiveInstance(
                        PrimitiveKind.Cube,
                        Matrix4x4.CreateScale(new Vector3(cellSize * 0.85f, thickness, cellSize * 0.85f)) *
                        Matrix4x4.CreateTranslation(cellPosition),
                        Vector3.Zero,
                        color));
                }
            }
        }

        return instances;
    }
}
