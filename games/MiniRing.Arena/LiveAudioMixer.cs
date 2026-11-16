using System.Globalization;
using System.Text.Json;

namespace MiniRing.Arena;

/// <summary>
/// 生配信の音の混ぜ: ゲームの音（打撃・歓声）に、前もって作った実況の声を重ねる。
/// ゲームの音は声の間だけ下げる（<c>tools/commentary/Mix.ps1</c> と同じ値・同じ形: 声の <see cref="DuckAttackSeconds"/> 前から下げ始め、
/// 声が終わって <see cref="DuckReleaseSeconds"/> で戻す。台形の包絡を声ごとに足して 1 で頭打ち）。
/// 出力 = ゲームの音 × 下げ ＋ 声、最後にファイルの道（<c>VideoMaker.WriteWav</c>）と同じ <c>tanh</c> の柔らかい頭打ち。
/// ファイルの道（dub.ps1 → Mix.ps1）はリミッター（alimiter）なので、声のある所の数値は完全には一致しない。
/// </summary>
internal sealed class LiveAudioMixer
{
    /// <summary>声の間にゲームの音を下げる量 [dB]。Mix.ps1 の既定（声が聞こえ、歓声の空気は残る）。</summary>
    public const float DuckDb = 6f;

    /// <summary>声の始まりの何秒前から下げ始めるか [s]（声の出だしを歓声に埋もれさせない）。</summary>
    public const float DuckAttackSeconds = 0.15f;

    /// <summary>声が終わって何秒かけて戻すか [s]（急に戻すと歓声が跳ねて聞こえる）。</summary>
    public const float DuckReleaseSeconds = 0.4f;

    /// <summary>出力の頭打ちの係数（<c>VideoMaker.WriteWav</c> と同じ。16 bit の満点の少し手前）。</summary>
    private const float OutputPeak = 32000f;

    private readonly int _sampleRate;
    private readonly float[] _voice;
    private readonly float[] _envelope;
    private readonly float _depth = 1f - MathF.Pow(10f, -DuckDb / 20f);

    /// <param name="sampleRate">出力の標本の速さ [Hz]。</param>
    /// <param name="length">出力の長さ（標本数。ゲームの音の配列と同じ）。</param>
    public LiveAudioMixer(int sampleRate, int length)
    {
        _sampleRate = sampleRate;
        _voice = new float[length];
        _envelope = new float[length];
    }

    /// <summary>置いた声の数。</summary>
    public int VoiceCount { get; private set; }

    /// <summary>manifest.json（Synthesize.ps1 の出力）の台詞をすべて読み、声の線に置く。wav の標本の速さはヘッダから読む。</summary>
    public void LoadManifest(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        foreach (var line in document.RootElement.GetProperty("lines").EnumerateArray())
        {
            var file = line.GetProperty("file").GetString() ?? throw new FormatException($"{manifestPath}: file がありません。");
            var path = Path.IsPathRooted(file) ? file : Path.Combine(directory, file);
            var start = (float)line.GetProperty("start").GetDouble();
            var end = (float)line.GetProperty("end").GetDouble();
            var (samples, rate) = ReadWav(path);
            AddVoice(Resample(samples, rate, _sampleRate), start, end);
        }
    }

    /// <summary>声（出力と同じ標本の速さ）を <paramref name="start"/> 秒に置き、<paramref name="end"/> 秒までゲームの音を下げる。</summary>
    public void AddVoice(float[] samples, float start, float end)
    {
        var from = (int)(start * _sampleRate);
        for (var i = 0; i < samples.Length; i++)
        {
            var at = from + i;
            if (at >= 0 && at < _voice.Length)
            {
                _voice[at] += samples[i];
            }
        }

        var first = Math.Max(0, (int)((start - DuckAttackSeconds) * _sampleRate));
        var last = Math.Min(_envelope.Length - 1, (int)((end + DuckReleaseSeconds) * _sampleRate));
        for (var i = first; i <= last; i++)
        {
            var t = i / (float)_sampleRate;
            var attack = Math.Clamp((t - (start - DuckAttackSeconds)) / DuckAttackSeconds, 0f, 1f);
            var release = Math.Clamp((end + DuckReleaseSeconds - t) / DuckReleaseSeconds, 0f, 1f);
            _envelope[i] += attack * release;
        }

        VoiceCount++;
    }

    /// <summary>標本 <paramref name="index"/> でのゲームの音の倍率（声の外は 1、声の間は −<see cref="DuckDb"/> dB、傾きの途中はその間）。</summary>
    public float GameGain(int index) => index >= 0 && index < _envelope.Length ? 1f - (_depth * MathF.Min(1f, _envelope[index])) : 1f;

    /// <summary>標本 <paramref name="index"/> の出力（16 bit）。<paramref name="game"/> はゲームの音の標本。</summary>
    public short Output(float game, int index)
    {
        var voice = index >= 0 && index < _voice.Length ? _voice[index] : 0f;
        return (short)(MathF.Tanh((game * GameGain(index)) + voice) * OutputPeak);
    }

    /// <summary>直線補間で標本の速さを変える（16 kHz の声 → 44.1 kHz）。長さは <c>入力の長さ × 出力の速さ / 入力の速さ</c>。</summary>
    public static float[] Resample(float[] source, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || source.Length == 0)
        {
            return source;
        }

        var length = (int)((long)source.Length * targetRate / sourceRate);
        var output = new float[length];
        var step = sourceRate / (double)targetRate;
        for (var i = 0; i < length; i++)
        {
            var position = i * step;
            var index = (int)position;
            var fraction = (float)(position - index);
            var a = source[Math.Min(index, source.Length - 1)];
            var b = source[Math.Min(index + 1, source.Length - 1)];
            output[i] = a + ((b - a) * fraction);
        }

        return output;
    }

    /// <summary>
    /// 16 bit PCM の wav を読む（チャンネルが複数なら平均して 1 つに）。標本の速さを返す。
    /// 声の wav は 16 kHz mono だが、ヘッダから読む（別の速さで作り直しても動く）。
    /// </summary>
    public static (float[] Samples, int SampleRate) ReadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
        {
            throw new FormatException($"{path}: wav ではありません。");
        }

        var channels = 0;
        var rate = 0;
        var bits = 0;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BitConverter.ToInt32(bytes, offset + 4);
            var body = offset + 8;
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                if (bits != 16 || channels < 1 || rate < 1)
                {
                    throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"{path}: 16 bit PCM ではありません（{bits} bit, {channels} ch, {rate} Hz）。"));
                }

                var available = Math.Min(size < 0 ? int.MaxValue : size, bytes.Length - body);
                var frames = available / (2 * channels);
                var samples = new float[frames];
                for (var f = 0; f < frames; f++)
                {
                    var sum = 0f;
                    for (var c = 0; c < channels; c++)
                    {
                        sum += BitConverter.ToInt16(bytes, body + (((f * channels) + c) * 2)) / 32768f;
                    }

                    samples[f] = sum / channels;
                }

                return (samples, rate);
            }

            offset = body + size + (size & 1);
        }

        throw new FormatException($"{path}: data チャンクがありません。");
    }
}
