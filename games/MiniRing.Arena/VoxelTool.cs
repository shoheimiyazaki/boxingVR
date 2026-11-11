using MiniRing.Voxels;

namespace MiniRing.Arena;

/// <summary>
/// ボクサーの中身をボクセル編集ソフト（Vengi・MagicaVoxel）で直すための道具（docs/VOXEL_EDITING.md）。
/// <list type="bullet">
/// <item><c>voxexport</c>: 今の設定の中身を .vox に書き出す（組み込んだ直しがあればそれを当てたもの、<c>--from generated</c> で生成のまま）。</item>
/// <item><c>voxcheck</c>: 直した .vox を読み、使えるか（部位の名前・色・大きさ）と、生成からどれだけ変わったかを材質ごとに出す。
/// <c>--install yes</c> で games/MiniRing/Voxels/boxer.vox へ置く（次のビルドからゲームに組み込まれる）。</item>
/// </list>
/// </summary>
internal static class VoxelTool
{
    /// <summary>ゲームに組み込む .vox の置き場所（リポジトリの根から）。</summary>
    public const string InstallPath = "games/MiniRing/Voxels/boxer.vox";

    /// <summary>今のフォルダから上へたどって、リポジトリの根（games/MiniRing/MiniRing.csproj がある所）を探す。</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "games", "MiniRing", "MiniRing.csproj")))
            {
                return dir.FullName;
            }
        }

        throw new IOException("Run voxcheck --install from inside the repository (games/MiniRing/MiniRing.csproj was not found above the current folder).");
    }

    public static int Export(string output, bool generated)
    {
        var overrides = generated ? null : BoxerVoxelOverrides.Shipped;
        var scene = BoxerVoxelModel.ToVox(BoxerVoxelModel.Parts(FighterTuning.Default, overrides));
        using (var stream = File.Create(output))
        {
            MagicaVox.Write(stream, scene);
        }

        Console.WriteLine($"{output}: {scene.Models.Count} parts ({string.Join(", ", scene.Models.Select(m => $"{m.Name} {m.Size.X}×{m.Size.Y}×{m.Size.Z}"))}), " +
            $"{(overrides is null ? "generated" : "with the installed edits")}");
        Console.WriteLine("colours: " + string.Join(", ", Enumerable.Range(1, BoxerAnatomy.Palette.Count - 1).Select(m => $"{m} {BoxerAnatomy.Palette[(byte)m].Name}")));
        return 0;
    }

    public static int Check(string input, bool install)
    {
        BoxerVoxelOverrides edited;
        try
        {
            edited = BoxerVoxelOverrides.Load(input);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"{input}: {e.Message}");
            return 1;
        }

        var generated = BoxerVoxelModel.Parts(FighterTuning.Default, overrides: null);
        var fits = true;
        foreach (var kind in Enum.GetValues<BodyPartKind>())
        {
            var part = edited[kind];
            if (part is null)
            {
                Console.WriteLine($"{kind,-8} not in the file: stays generated");
                continue;
            }

            var grid = generated[kind];
            if (part.Dimensions != grid.Dimensions)
            {
                fits = false;
                Console.WriteLine($"{kind,-8} is {part.Dimensions} cells, the game needs {grid.Dimensions} (do not crop or resize the model)");
                continue;
            }

            var changes = new Dictionary<(byte From, byte To), int>();
            var (dx, dy, dz) = grid.Dimensions;
            for (var z = 0; z < dz; z++)
            {
                for (var y = 0; y < dy; y++)
                {
                    for (var x = 0; x < dx; x++)
                    {
                        var from = grid.At(x, y, z);
                        var to = part.Materials[x + (dx * (y + (dy * z)))];
                        if (from != to)
                        {
                            changes[(from, to)] = changes.GetValueOrDefault((from, to)) + 1;
                        }
                    }
                }
            }

            var summary = changes.Count == 0 ? "unchanged"
                : string.Join(", ", changes.OrderByDescending(c => c.Value).Take(6).Select(c => $"{Name(c.Key.From)} → {Name(c.Key.To)} × {c.Value}"));
            Console.WriteLine($"{kind,-8} {summary}");
        }

        if (!fits)
        {
            Console.Error.WriteLine("Some parts do not fit the current settings; the game would use the generated body for them.");
            return 1;
        }

        if (install)
        {
            var target = Path.Combine(RepositoryRoot(), InstallPath);
            File.Copy(input, target, overwrite: true);
            Console.WriteLine($"installed to {target} (rebuild to use it)");
        }

        return 0;
    }

    private static string Name(byte material) => BoxerAnatomy.Palette[material].Name;
}
