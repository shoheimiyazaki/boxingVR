using MiniRing.Characters;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: 顔の層と 5 mm の頭の正面を文字で書く。2026-11-20 に <see cref="FaceLayerTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FaceLayerProbe
{
    /// <summary>調査用: 顔の層と 5 mm の頭の正面（どちらの格子が表か: 小文字 = 顔の層の材質、大文字の I = 5 mm の頭が前に出ている）。FACE_IDS で絞る。</summary>
    [TestMethod]
    public void Probe_FrontOwner()
    {
        var only = Environment.GetEnvironmentVariable("FACE_IDS");
        foreach (var id in string.IsNullOrEmpty(only) ? FaceLayerTests.Ids : only.Split(','))
        {
            var c = CharacterRoster.Get(id);
            var full = FaceLayerTests.FullHead(c);
            var (inner, face) = FaceLayer.Split(full, c.Face);
            var (nx, ny, nz) = face.Dimensions;
            Console.WriteLine(id + " 5mm full head front (material@z) then kept");
            var (hx, hy, hz) = full.Dimensions;
            for (var y = hy - 1; y >= 0; y--)
            {
                var a = new System.Text.StringBuilder();
                var b = new System.Text.StringBuilder();
                for (var x = 0; x < hx; x++)
                {
                    var fz = -1;
                    for (var z = hz - 1; z >= 0 && fz < 0; z--)
                    {
                        fz = full.At(x, y, z) is not (0 or BoxerAnatomy.Skin or BoxerAnatomy.Hair or BoxerAnatomy.Muscle) ? z : -1;
                    }

                    a.Append(fz < 0 ? "  .  " : $"{full.At(x, y, fz),2}@{fz} ");
                    var kz = -1;
                    for (var z = hz - 1; z >= 0 && kz < 0; z--)
                    {
                        kz = inner.At(x, y, z) != 0 ? z : -1;
                    }

                    b.Append(kz < 0 ? "  .  " : $"{inner.At(x, y, kz),2}@{kz} ");
                }

                Console.WriteLine($"{hy - 1 - y,2} {a}| {b}");
            }

            Console.WriteLine(id);
            for (var y = ny - 1; y >= 0; y--)
            {
                var row = new System.Text.StringBuilder();
                for (var x = 0; x < nx; x++)
                {
                    var fz = -1;
                    for (var z = nz - 1; z >= 0 && fz < 0; z--)
                    {
                        fz = face.At(x, y, z) != 0 ? z : -1;
                    }

                    var iz = -1;
                    for (var z = nz - 1; z >= 0 && iz < 0; z--)
                    {
                        iz = inner.At(x / 2, y / 2, z / 2) != 0 ? z : -1;
                    }

                    row.Append(iz > fz ? "I " : fz < 0 ? ". " : $"{fz,2}"[..2]);
                    row.Append(' ');
                }

                Console.WriteLine($"{ny - 1 - y,2} {row}");
            }
        }
    }
}
