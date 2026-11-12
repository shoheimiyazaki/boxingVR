using System.Globalization;
using System.Text;
using MiniRing.Audio;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 打撃音と当たった音を WAV に並べる（耳で確かめる用）。
/// <list type="bullet">
/// <item>SOUND_DEMO（無ければ一時フォルダ）: 打撃音（グローブ・素手・鉄 × ボディ・頭・ガード × 弱→強、クリーンヒット）、続いて組ごとに弱→強。</item>
/// <item>SOUND_REF_DIR があれば、その中の手本の録音（16-bit モノラル 44.1 kHz の WAV）と合成音を交互に並べた compare.wav と、
/// 合成音だけの synth_*.wav を SOUND_DEMO と同じフォルダに書く（録音は比べるために読むだけ。合成には使わない）。</item>
/// </list>
/// 数値（長さ・重心・高域の割合・減衰）も出す。
/// </summary>
[TestClass]
public sealed class ContactSoundDemoProbe
{
    private const int Rate = ContactSound.SampleRate;

    /// <summary>手本の録音のファイル名と、それに当てる合成音（強さ 1・重さ 1 が手本の重さ）。</summary>
    private static readonly (string Reference, string Name, Func<byte[]> Synth)[] Pairs =
    [
        ("軽いキック2.wav", "glove", () => HitSound.Synthesize(new HitTone(1f, 0.5f, HitTarget.Body, HitSurface.Glove), 7)),
        ("重いパンチ2.wav", "bare", () => HitSound.Synthesize(new HitTone(1f, 0.5f, HitTarget.Body, HitSurface.Bare), 7)),
        ("剣で打ち合う4.wav", "sword", () => ContactSound.Synthesize(ContactPair.MetalMetal, 1f, 7)),
    ];

    [TestMethod]
    public void WriteDemo()
    {
        var path = Environment.GetEnvironmentVariable("SOUND_DEMO") ?? Path.Combine(Path.GetTempPath(), "sound_demo.wav");
        var pcm = new List<byte>();
        void Silence(float seconds) => pcm.AddRange(new byte[2 * (int)(Rate * seconds)]);
        void Add(string label, byte[] clip)
        {
            Describe(label, clip);
            pcm.AddRange(clip);
            Silence(0.3f);
        }

        foreach (var surface in Enum.GetValues<HitSurface>())
        {
            foreach (var target in new[] { HitTarget.Body, HitTarget.Head, HitTarget.Guard })
            {
                foreach (var intensity in new[] { 0.1f, 0.4f, 0.7f, 1f })
                {
                    var clean = target == HitTarget.Guard ? 0.1f : 0.5f;
                    Add($"hit {surface} {target} I={intensity:F1}", HitSound.Synthesize(new HitTone(intensity, clean, target, surface), 7));
                }

                Silence(0.5f);
            }

            foreach (var intensity in new[] { 0.3f, 1f })
            {
                Add($"hit {surface} Head I={intensity:F1} C=1.0", HitSound.Synthesize(new HitTone(intensity, 1f, HitTarget.Head, surface), 7));
            }

            Silence(0.8f);
        }

        foreach (var pair in Enum.GetValues<ContactPair>())
        {
            foreach (var strength in new[] { 0.1f, 0.5f, 1f })
            {
                Add($"{pair} s={strength:F1}", ContactSound.Synthesize(pair, strength, 7));
            }

            Silence(0.5f);
        }

        WriteWav(path, pcm);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"wrote {path} ({pcm.Count / 2f / Rate:F1} s)"));

        var references = Environment.GetEnvironmentVariable("SOUND_REF_DIR");
        if (references is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var compare = new List<byte>();
        foreach (var (reference, name, synth) in Pairs)
        {
            var clip = synth();
            WriteWav(Path.Combine(folder, $"synth_{name}.wav"), [.. clip]);
            compare.AddRange(ReadMono16(Path.Combine(references, reference)));
            compare.AddRange(new byte[2 * (int)(Rate * 0.6f)]);
            compare.AddRange(clip);
            compare.AddRange(new byte[2 * (int)(Rate * 1.2f)]);
        }

        WriteWav(Path.Combine(folder, "compare.wav"), compare);
        Console.WriteLine($"wrote {Path.Combine(folder, "compare.wav")}");
    }

    private static void Describe(string label, byte[] clip)
    {
        var samples = SoundAnalysis.ToFloats(clip);
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label,-30} len {samples.Length / (float)Rate:F3} s  peak {SoundAnalysis.PeakOf(clip):F2}  " +
            $"centroid {SoundAnalysis.SpectralCentroid(samples, Rate),6:F0} Hz  >1k {SoundAnalysis.HighBandShare(samples, Rate, 1000f):P0}  " +
            $"-20dB {SoundAnalysis.TimeToFall(samples, Rate, 0.1f):F3} s"));
    }

    /// <summary>16-bit モノラル 44.1 kHz の WAV の中身（data の塊）を読む。形が違えば例外。</summary>
    private static byte[] ReadMono16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var channels = BitConverter.ToInt16(bytes, 22);
        var rate = BitConverter.ToInt32(bytes, 24);
        var bits = BitConverter.ToInt16(bytes, 34);
        if (channels != 1 || rate != Rate || bits != 16)
        {
            throw new InvalidDataException($"{path}: {channels} ch {rate} Hz {bits} bit (16-bit mono {Rate} Hz expected)");
        }

        for (var at = 12; at + 8 <= bytes.Length;)
        {
            var size = BitConverter.ToInt32(bytes, at + 4);
            if (Encoding.ASCII.GetString(bytes, at, 4) == "data")
            {
                return bytes[(at + 8)..Math.Min(bytes.Length, at + 8 + size)];
            }

            at += 8 + size + (size & 1);
        }

        throw new InvalidDataException($"{path}: no data chunk");
    }

    private static void WriteWav(string path, List<byte> pcm)
    {
        using var file = new BinaryWriter(File.Create(path), Encoding.ASCII);
        file.Write("RIFF"u8);
        file.Write(36 + pcm.Count);
        file.Write("WAVEfmt "u8);
        file.Write(16);
        file.Write((short)1);
        file.Write((short)1);
        file.Write(Rate);
        file.Write(Rate * 2);
        file.Write((short)2);
        file.Write((short)16);
        file.Write("data"u8);
        file.Write(pcm.Count);
        file.Write(pcm.ToArray());
    }
}
