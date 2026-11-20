using System.Text.Json;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>調査用: 顔の部品と名簿の顔を文字の格子で書き出す（<see cref="FacePartLibraryTests"/> から移した。スイートの外）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FacePartDumpProbe
{
    /// <summary>調査用: FACE_DUMP_DIR に、組み立てた顔の格子を文字で書く（tools/face-parts/render.py が絵にする）。</summary>
    [TestMethod]
    public void Probe_DumpFaces()
    {
        var dir = Environment.GetEnvironmentVariable("FACE_DUMP_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            Assert.Inconclusive("FACE_DUMP_DIR を指定してください。");
        }

        Directory.CreateDirectory(dir);
        const string symbols = ".shbwilenft";
        void Dump(string name, FaceSpec spec)
        {
            var v = FaceComposer.Compose(spec, 20, 28, 20);
            var colors = FacePalette.Resolve(spec).ToDictionary(c => symbols[(int)c.Key].ToString(), c => $"#{(int)((c.Value.X * 255f) + 0.5f):X2}{(int)((c.Value.Y * 255f) + 0.5f):X2}{(int)((c.Value.Z * 255f) + 0.5f):X2}");
            File.WriteAllText(Path.Combine(dir, name + ".txt"), $"{v.Nx} {v.Ny} {v.Nz}\n{JsonSerializer.Serialize(colors)}\n" + new string([.. v.Cells.Select(c => symbols[(int)c])]));
        }

        foreach (var character in MiniRing.Characters.CharacterRoster.All)
        {
            Dump("roster_" + character.Id, character.Face);
        }

        foreach (var name in FacePartLibraryTests.Names)
        {
            Dump(name, FaceSpec.For(name));
        }

        foreach (var category in new[] { "eyes", "brows", "mouth", "nose", "ears", "jaw", "hair", "beard", "browridge" })
        {
            for (var i = 1; i <= FacePartLibrary.Default.Parts(category).Count; i++)
            {
                Dump($"part_{category}_{i}", FacePartLibraryTests.Spec((category, $"{category}.{i}", 1f)));
            }
        }
    }
}
