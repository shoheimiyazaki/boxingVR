namespace MiniRing.Tests;

/// <summary>合成音のテストで使う測り方（PCM の読み・RMS・スペクトルの重心と平坦さ・減衰の尾）。</summary>
internal static class SoundAnalysis
{
    /// <summary>スペクトルを見る窓の長さ（4096 サンプル = 44.1 kHz で約 93 ms。打撃の芯がすべて入る）。</summary>
    public const int FrameSize = 4096;

    public static float[] ToFloats(byte[] pcm)
    {
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(pcm, 2 * i) / (float)short.MaxValue;
        }

        return samples;
    }

    public static float PeakOf(byte[] pcm) => ToFloats(pcm).Max(MathF.Abs);

    public static float Rms(byte[] pcm)
    {
        var samples = ToFloats(pcm);
        return MathF.Sqrt(samples.Sum(s => s * s) / samples.Length);
    }

    /// <summary>
    /// 最初の <see cref="FrameSize"/> サンプルのパワースペクトル（ビン k の周波数は k·rate/FrameSize）。窓はハン窓の後ろ半分
    /// （頭で 1、末尾で 0）: 打撃の音は頭の数 ms に鋭い成分が集まるので、頭を 0 に絞る普通のハン窓では頭の「パシッ」が消えてしまう。
    /// </summary>
    public static double[] PowerSpectrum(float[] samples)
    {
        var re = new double[FrameSize];
        var im = new double[FrameSize];
        for (var n = 0; n < FrameSize && n < samples.Length; n++)
        {
            re[n] = samples[n] * (0.5 + (0.5 * Math.Cos(Math.PI * n / FrameSize)));
        }

        Fft(re, im);
        var power = new double[FrameSize / 2];
        for (var k = 0; k < power.Length; k++)
        {
            power[k] = (re[k] * re[k]) + (im[k] * im[k]);
        }

        return power;
    }

    /// <summary>パワーで重み付けした平均の周波数 [Hz]（明るさ・高さの目安）。</summary>
    public static float SpectralCentroid(float[] samples, int rate)
    {
        var power = PowerSpectrum(samples);
        double weighted = 0, total = 0;
        for (var k = 1; k < power.Length; k++)
        {
            weighted += power[k] * k * rate / (double)FrameSize;
            total += power[k];
        }

        return (float)(weighted / Math.Max(total, 1e-12));
    }

    /// <summary>
    /// <paramref name="lowHz"/>〜<paramref name="highHz"/> のスペクトルの平坦さ（幾何平均 / 算術平均。白色雑音で 1 近く、
    /// 数本の純音の線だけなら 0 近く）。音が「線」か「詰まった帯」かの目安。
    /// </summary>
    public static float SpectralFlatness(float[] samples, int rate, float lowHz, float highHz)
    {
        var power = PowerSpectrum(samples);
        var from = Math.Max(1, (int)(lowHz * FrameSize / rate));
        var to = Math.Min(power.Length - 1, (int)(highHz * FrameSize / rate));
        double logSum = 0, sum = 0;
        for (var k = from; k <= to; k++)
        {
            var p = power[k] + 1e-20;
            logSum += Math.Log(p);
            sum += p;
        }

        var count = to - from + 1;
        return (float)(Math.Exp(logSum / count) / (sum / count));
    }

    /// <summary>
    /// <paramref name="lowHz"/>〜<paramref name="highHz"/> のエネルギーのうち、強い方から <paramref name="bins"/> 本のビンが持つ割合。
    /// 数本の純音の線（風鈴）なら 1 に近く、詰まった帯なら小さい。
    /// </summary>
    public static float StrongestBinsShare(float[] samples, int rate, float lowHz, float highHz, int bins)
    {
        var power = PowerSpectrum(samples);
        var from = Math.Max(1, (int)(lowHz * FrameSize / rate));
        var to = Math.Min(power.Length - 1, (int)(highHz * FrameSize / rate));
        var band = power[from..(to + 1)];
        var total = band.Sum();
        var strongest = band.OrderDescending().Take(bins).Sum();
        return (float)(strongest / Math.Max(total, 1e-20));
    }

    /// <summary><paramref name="fromHz"/> より上にあるエネルギーの割合（最初の窓の中で）。明るさ・鋭さの目安。</summary>
    public static float HighBandShare(float[] samples, int rate, float fromHz)
    {
        var power = PowerSpectrum(samples);
        var from = (int)(fromHz * FrameSize / rate);
        return (float)(power[from..].Sum() / Math.Max(power.Sum(), 1e-20));
    }

    /// <summary>
    /// <paramref name="fromMs"/>〜<paramref name="toMs"/> の区間（ハン窓）のパワーのうち、<paramref name="lowHz"/>〜<paramref name="highHz"/> にある割合。
    /// 手本の録音を測ったのと同じ測り方（区間ごとの帯の割合）。
    /// </summary>
    public static float BandShare(float[] samples, int rate, float fromMs, float toMs, float lowHz, float highHz)
    {
        var (power, binHz) = SegmentPower(samples, rate, fromMs, toMs);
        double band = 0, total = 0;
        for (var k = 0; k < power.Length; k++)
        {
            total += power[k];
            var hz = k * binHz;
            if (hz >= lowHz && hz < highHz)
            {
                band += power[k];
            }
        }

        return (float)(band / Math.Max(total, 1e-30));
    }

    /// <summary>区間のパワーで重み付けした平均の周波数 [Hz]。</summary>
    public static float SegmentCentroid(float[] samples, int rate, float fromMs, float toMs)
    {
        var (power, binHz) = SegmentPower(samples, rate, fromMs, toMs);
        double weighted = 0, total = 0;
        for (var k = 0; k < power.Length; k++)
        {
            weighted += power[k] * k * binHz;
            total += power[k];
        }

        return (float)(weighted / Math.Max(total, 1e-30));
    }

    /// <summary>
    /// 20 ms ごとの RMS の包絡が、いちばん大きい窓から <paramref name="db"/> [dB]（負の数）の上にいる最後の時刻 [s]（その窓の終わり）。
    /// </summary>
    public static float FallTime(float[] samples, int rate, float db)
    {
        var window = rate / 50;
        var levels = new List<float>();
        for (var start = 0; start + window <= samples.Length; start += window)
        {
            var sum = 0f;
            for (var i = start; i < start + window; i++)
            {
                sum += samples[i] * samples[i];
            }

            levels.Add(MathF.Sqrt(sum / window));
        }

        var floor = levels.Max() * MathF.Pow(10f, db / 20f);
        return (levels.FindLastIndex(level => level >= floor) + 1) * window / (float)rate;
    }

    /// <summary>
    /// 区間の低い音（220 Hz の 4 次の低域通過）の高さ [Hz]: 上向きのゼロ交差の間隔から測った周期のうち、振幅が全体の山の 1 割を越えるものの中央値。
    /// 測れなければ NaN。
    /// </summary>
    public static float LowPitch(float[] samples, int rate, float fromMs, float toMs)
    {
        var low = LowPass(LowPass(samples, rate, 220f), rate, 220f);
        var peak = low.Max(MathF.Abs);
        var periods = new List<float>();
        var previous = -1;
        for (var n = 1; n < low.Length; n++)
        {
            if (low[n - 1] < 0f && low[n] >= 0f)
            {
                if (previous >= 0)
                {
                    var ms = previous * 1000f / rate;
                    var amplitude = 0f;
                    for (var i = previous; i < n; i++)
                    {
                        amplitude = MathF.Max(amplitude, MathF.Abs(low[i]));
                    }

                    if (ms >= fromMs && ms < toMs && amplitude > 0.1f * peak)
                    {
                        periods.Add(rate / (float)(n - previous));
                    }
                }

                previous = n;
            }
        }

        if (periods.Count == 0)
        {
            return float.NaN;
        }

        periods.Sort();
        return periods[periods.Count / 2];
    }

    private static (double[] Power, double BinHz) SegmentPower(float[] samples, int rate, float fromMs, float toMs)
    {
        var from = Math.Min(samples.Length, (int)(fromMs * rate / 1000f));
        var to = Math.Min(samples.Length, (int)(toMs * rate / 1000f));
        var length = Math.Max(0, to - from);
        var size = 16384;
        while (size < length)
        {
            size <<= 1;
        }

        var re = new double[size];
        var im = new double[size];
        for (var n = 0; n < length; n++)
        {
            re[n] = samples[from + n] * (0.5 - (0.5 * Math.Cos(2 * Math.PI * n / Math.Max(1, length - 1))));
        }

        Fft(re, im);
        var power = new double[size / 2];
        for (var k = 0; k < power.Length; k++)
        {
            power[k] = (re[k] * re[k]) + (im[k] * im[k]);
        }

        return (power, rate / (double)size);
    }

    /// <summary>2 次の低域通過（RBJ、Q = 1/√2）。</summary>
    private static float[] LowPass(float[] x, int rate, float cutoffHz)
    {
        var w = 2 * Math.PI * cutoffHz / rate;
        var (sin, cos) = Math.SinCos(w);
        var alpha = sin / (2 * Math.Sqrt(0.5));
        var a0 = 1 + alpha;
        double b0 = (1 - cos) / 2 / a0, b1 = (1 - cos) / a0, b2 = (1 - cos) / 2 / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
        var y = new float[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var n = 0; n < x.Length; n++)
        {
            var v = (b0 * x[n]) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2);
            x2 = x1;
            x1 = x[n];
            y2 = y1;
            y1 = v;
            y[n] = (float)v;
        }

        return y;
    }

    /// <summary>振幅が初めてピークの <paramref name="ratio"/> に届く時刻 [s]（立ち上がりの鋭さ。頭が先頭に立つ音ほど短い）。</summary>
    public static float TimeToReach(float[] samples, int rate, float ratio)
    {
        var peak = samples.Max(MathF.Abs);
        var index = Array.FindIndex(samples, s => MathF.Abs(s) >= ratio * peak);
        return index / (float)rate;
    }

    /// <summary><paramref name="fromSeconds"/> 以降にあるエネルギーの割合。</summary>
    public static float TailShare(float[] samples, int rate, float fromSeconds)
    {
        var from = Math.Min(samples.Length, (int)(fromSeconds * rate));
        var total = samples.Sum(s => s * s);
        var tail = samples.Skip(from).Sum(s => s * s);
        return tail / MathF.Max(total, 1e-12f);
    }

    /// <summary>5 ms の窓の RMS がピークの窓の <paramref name="ratio"/> を最後に越えている時刻 [s]。</summary>
    public static float TimeToFall(float[] samples, int rate, float ratio)
    {
        var window = rate / 200;
        var levels = new List<float>();
        for (var start = 0; start + window <= samples.Length; start += window)
        {
            var sum = 0f;
            for (var i = start; i < start + window; i++)
            {
                sum += samples[i] * samples[i];
            }

            levels.Add(MathF.Sqrt(sum / window));
        }

        var peak = levels.Max();
        var last = levels.FindLastIndex(level => level >= ratio * peak);
        return (last + 1) * window / (float)rate;
    }

    /// <summary>基数 2 の FFT（その場で）。</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            for (var i = 0; i < n; i += len)
            {
                for (var k = 0; k < len / 2; k++)
                {
                    var (s, c) = Math.SinCos(angle * k);
                    var ur = re[i + k];
                    var ui = im[i + k];
                    var vr = (re[i + k + (len / 2)] * c) - (im[i + k + (len / 2)] * s);
                    var vi = (re[i + k + (len / 2)] * s) + (im[i + k + (len / 2)] * c);
                    re[i + k] = ur + vr;
                    im[i + k] = ui + vi;
                    re[i + k + (len / 2)] = ur - vr;
                    im[i + k + (len / 2)] = ui - vi;
                }
            }
        }
    }
}
