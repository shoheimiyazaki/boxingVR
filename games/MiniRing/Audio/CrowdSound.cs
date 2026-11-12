namespace MiniRing.Audio;

/// <summary>観客の反応の種類（テレビ中継の会場の声）。</summary>
public enum CrowdReaction
{
    /// <summary>「おぉ〜」: 紙一重で外れた・大振りした（上がって下がる、約 1 s）。</summary>
    Ooh,

    /// <summary>きれいに入った: 拍手と歓声のふくらみ（約 1.5 s）。</summary>
    Cheer,

    /// <summary>強打・ダウン: 大きく広い歓声（約 2.5 s）。</summary>
    Roar,

    /// <summary>K.O.: 総立ちの大歓声に指笛と拍手（約 5 s）。</summary>
    Eruption,

    /// <summary>「あぁ…」: 同情のため息、音程が落ちる（約 1.2 s）。</summary>
    Groan,

    /// <summary>カウントに合わせた一斉の掛け声 1 回ぶん（約 0.4 s）。1 秒の拍ごとに 1 回鳴らす。</summary>
    Count,
}

/// <summary>
/// 会場の観客の声（素材ファイルなし・合成・モノラル）。白いノイズではなく「何千人」に聞こえるよう、
/// 一人ひとりの声（声帯の鋸歯状波＋息のノイズを、母音の形（フォルマント）の共鳴に通したもの）を数十本、
/// それぞれ別の高さ・母音・反応の遅れ・抑揚で重ねる。拍手（短いノイズの粒の雨）と指笛（揺れる正弦）を足す。
/// <para>
/// 返すのは浮動小数の波形（<see cref="HitSound.SampleRate"/>）で、ピークはその反応の大きさに揃えてある
/// （<see cref="ToPcm"/> は揃え直さない）。同じ種なら同じ音（決定的。<see cref="Random"/> は必ず種付き）。
/// </para>
/// </summary>
public static class CrowdSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>ざわめきのピーク（ほかのすべての音の下に敷く）。</summary>
    public const float AmbiencePeak = 0.065f;

    /// <summary>ざわめきで同時にしゃべる声の数（遠くの何千人の代表）。</summary>
    private const int AmbienceVoices = 40;

    /// <summary>フォルマントの係数を作り直す間隔 [サンプル]（0.7 ms。母音の動きにはこれで十分細かい）。</summary>
    private const int ControlBlock = 32;

    /// <summary>母音のフォルマント（F1, F2）[Hz]。成人男性の平均の近似。</summary>
    private static readonly (float F1, float F2)[] Vowels =
    [
        (730f, 1090f), // あ
        (530f, 1840f), // え
        (300f, 2200f), // い
        (570f, 840f), // お
        (320f, 900f), // う
        (640f, 1190f), // あ（曖昧）
        (660f, 1720f), // え（広い）
    ];

    /// <summary>
    /// 反応の形。<paramref name="Seconds"/> 長さ、<paramref name="Peak"/> ピーク（強さ 1 のとき）、
    /// <paramref name="Voices"/> 声の数、<paramref name="Start"/>→<paramref name="End"/> 母音の移り（F1, F2）、
    /// <paramref name="PitchScale"/> 叫ぶほど上がる声の高さ、<paramref name="Onset"/> 反応の遅れの散らばり [s]、
    /// <paramref name="Attack"/>/<paramref name="Hold"/> 立ち上がり・保つ長さ [s]（残りは消えていく）、
    /// <paramref name="Arc"/> 音程の山（上がって下がる）、<paramref name="Slope"/> 音程の傾き（負で下がる）、
    /// <paramref name="Shout"/> 叫びの節（音量の波打ち）の深さ、<paramref name="Breath"/> 息の混ざり、
    /// <paramref name="Claps"/> 拍手の粒の数 [1/s]（最大のとき）、<paramref name="Whistles"/> 指笛の数、
    /// <paramref name="Bed"/> 個々の声に分かれない群衆の厚み（息だけの声）の数。
    /// </summary>
    private readonly record struct ReactionSpec(
        float Seconds,
        float Peak,
        int Voices,
        (float F1, float F2) Start,
        (float F1, float F2) End,
        float PitchScale,
        float Onset,
        float Attack,
        float Hold,
        float Arc,
        float Slope,
        float Shout,
        float Breath,
        float Claps,
        int Whistles,
        int Bed);

    private static ReactionSpec SpecOf(CrowdReaction reaction) => reaction switch
    {
        CrowdReaction.Ooh => new(1.2f, 0.25f, 56, (330f, 760f), (520f, 880f), 1.15f, 0.12f, 0.3f, 0.15f, 0.22f, -0.05f, 0f, 0.25f, 0f, 0, 4),
        CrowdReaction.Cheer => new(1.6f, 0.35f, 60, (700f, 1500f), (640f, 1300f), 1.35f, 0.15f, 0.18f, 0.5f, 0.08f, 0f, 0.45f, 0.3f, 220f, 0, 6),
        CrowdReaction.Roar => new(2.6f, 0.55f, 72, (740f, 1150f), (680f, 1200f), 1.45f, 0.12f, 0.15f, 1.0f, 0.1f, -0.05f, 0.3f, 0.35f, 160f, 1, 10),
        CrowdReaction.Eruption => new(5.2f, 0.75f, 80, (760f, 1200f), (650f, 1250f), 1.55f, 0.2f, 0.25f, 2.8f, 0.06f, -0.04f, 0.35f, 0.35f, 420f, 8, 14),
        CrowdReaction.Groan => new(1.3f, 0.25f, 56, (620f, 1000f), (560f, 1150f), 1.05f, 0.12f, 0.15f, 0.2f, 0.04f, -0.4f, 0f, 0.3f, 0f, 0, 4),
        CrowdReaction.Count => new(0.42f, 0.3f, 56, (720f, 1180f), (560f, 900f), 1.4f, 0.03f, 0.04f, 0.1f, 0.05f, -0.1f, 0f, 0.25f, 0f, 0, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(reaction), reaction, "未知の観客の反応"),
    };

    /// <summary>
    /// 会場のざわめき（<paramref name="seconds"/> ぶん、ループなしで全体を作る）。数十人がそれぞれ音節ごとに母音を変えて話し、
    /// 間を置き、ときどき遠くで指笛や数人の拍手が鳴る。下にゆっくり帯域の動く息の層を敷く。ピークは <see cref="AmbiencePeak"/>。
    /// </summary>
    public static float[] Ambience(float seconds, int seed)
    {
        if (!(seconds > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "長さは正の秒数");
        }

        var random = new Random(seed);
        var output = new float[(int)(seconds * SampleRate)];
        for (var v = 0; v < AmbienceVoices; v++)
        {
            var voiceRandom = new Random(random.Next());
            var f0 = RandomPitch(voiceRandom);
            var gain = 0.3f + (0.7f * voiceRandom.NextSingle()); // 近い人・遠い人
            AddVoice(output, f0, gain, 0.2f + (0.2f * voiceRandom.NextSingle()), TalkingFrame(voiceRandom), voiceRandom);
        }

        // 群衆の厚み: 息だけの声を、ゆっくり動くフォルマントで 3 本。
        for (var b = 0; b < 3; b++)
        {
            var bedRandom = new Random(random.Next());
            var rate = 0.05f + (0.1f * bedRandom.NextSingle());
            var phase = bedRandom.NextSingle() * 2f * MathF.PI;
            var vowel = Vowels[bedRandom.Next(Vowels.Length)];
            AddVoice(
                output,
                120f,
                2.5f,
                1f,
                t =>
                {
                    var wander = MathF.Sin((2f * MathF.PI * rate * t) + phase);
                    return new VoiceFrame(1f, vowel.F1 * (1f + (0.25f * wander)), vowel.F2 * (1f - (0.2f * wander)), 1f);
                },
                bedRandom);
        }

        // 遠くの指笛（約 7 秒に 1 回）と、数人の短い拍手（約 5 秒に 1 回）。
        var events = new Random(random.Next());
        for (var t = 0.5f + (6f * events.NextSingle()); t < seconds - 0.5f; t += 3f + (8f * events.NextSingle()))
        {
            AddWhistle(output, (int)(t * SampleRate), 0.25f + (0.25f * events.NextSingle()), events);
        }

        for (var t = 0.3f + (4f * events.NextSingle()); t < seconds - 0.3f; t += 2f + (6f * events.NextSingle()))
        {
            var start = t;
            var span = 0.6f + (1.2f * events.NextSingle());
            AddApplause(output, u => u >= start && u < start + span ? 18f : 0f, 1.5f, events);
        }

        Level(output, AmbiencePeak);
        return output;
    }

    /// <summary>
    /// 観客の反応 1 回ぶんの波形。<paramref name="intensity01"/> が大きいほど大きく（ピーク × (0.5 + 0.5 × 強さ)）、
    /// 拍手も多い。長さは反応の種類で決まる。
    /// </summary>
    public static float[] React(CrowdReaction reaction, float intensity01, int seed)
    {
        var spec = SpecOf(reaction);
        var intensity = Math.Clamp(intensity01, 0f, 1f);
        var random = new Random(seed);
        var output = new float[(int)(spec.Seconds * SampleRate)];

        for (var v = 0; v < spec.Voices + spec.Bed; v++)
        {
            var voiceRandom = new Random(random.Next());
            var bed = v >= spec.Voices;
            var f0 = RandomPitch(voiceRandom) * spec.PitchScale;
            var gain = (0.35f + (0.65f * voiceRandom.NextSingle())) * (bed ? 1.6f : 1f);
            var breath = bed ? 1f : spec.Breath * (0.7f + (0.6f * voiceRandom.NextSingle()));
            AddVoice(output, f0, gain, breath, ReactionFrame(spec, voiceRandom), voiceRandom);
        }

        if (spec.Claps > 0f)
        {
            var clapRandom = new Random(random.Next());
            var density = spec.Claps * (0.4f + (0.6f * intensity));
            AddApplause(output, t => density * SharedEnvelope(spec, t - 0.1f), 4f, clapRandom);
        }

        var whistleRandom = new Random(random.Next());
        for (var w = 0; w < spec.Whistles; w++)
        {
            var start = (0.1f + (0.6f * whistleRandom.NextSingle())) * spec.Seconds;
            AddWhistle(output, (int)(start * SampleRate), 0.6f + (0.4f * whistleRandom.NextSingle()), whistleRandom);
        }

        FadeTail(output, 0.05f);
        Level(output, spec.Peak * (0.5f + (0.5f * intensity)));
        return output;
    }

    /// <summary>揃え済みの波形を 16-bit モノラル PCM にする（揃え直さない。±1 で切る）。</summary>
    public static byte[] ToPcm(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var pcm = new byte[samples.Length * 2];
        for (var n = 0; n < samples.Length; n++)
        {
            var y = Math.Clamp(samples[n], -1f, 1f);
            var v = (short)MathF.Round(y * short.MaxValue);
            pcm[2 * n] = (byte)(v & 0xFF);
            pcm[(2 * n) + 1] = (byte)((v >> 8) & 0xFF);
        }

        return pcm;
    }

    /// <summary>一人の声のその瞬間: 音量・F1・F2・音程の倍率。</summary>
    private readonly record struct VoiceFrame(float Level, float F1, float F2, float Pitch);

    /// <summary>観客の声の高さ [Hz]: 7 割が男性（95〜160）、3 割が女性（185〜280）。</summary>
    private static float RandomPitch(Random random) =>
        random.NextSingle() < 0.7f ? 95f + (65f * random.NextSingle()) : 185f + (95f * random.NextSingle());

    /// <summary>
    /// 反応の全員に共通の音量の形（時刻 <paramref name="t"/> [s]）: 余弦で立ち上がり、保ち、残りの長さで消えていく。
    /// </summary>
    private static float SharedEnvelope(in ReactionSpec spec, float t)
    {
        if (t <= 0f)
        {
            return 0f;
        }

        if (t < spec.Attack)
        {
            return 0.5f - (0.5f * MathF.Cos(MathF.PI * t / spec.Attack));
        }

        var release = MathF.Max(0.05f, spec.Seconds - spec.Attack - spec.Hold - spec.Onset);
        var after = t - spec.Attack - spec.Hold;
        return after <= 0f ? 1f : MathF.Max(0f, MathF.Pow(1f - MathF.Min(1f, after / release), 1.8f));
    }

    /// <summary>反応する一人: 遅れ・母音の個人差・節（叫びの波打ち）・音程の山と傾きを、共通の形の上に乗せる。</summary>
    private static Func<float, VoiceFrame> ReactionFrame(ReactionSpec spec, Random random)
    {
        var delay = spec.Onset * random.NextSingle();
        var stretch = 0.8f + (0.4f * random.NextSingle());
        var scatter1 = 0.88f + (0.24f * random.NextSingle());
        var scatter2 = 0.88f + (0.24f * random.NextSingle());
        var shoutRate = 2.5f + (3f * random.NextSingle());
        var shoutPhase = random.NextSingle() * 2f * MathF.PI;
        var span = MathF.Max(0.1f, spec.Seconds - delay);
        return t =>
        {
            var local = (t - delay) / stretch;
            var level = SharedEnvelope(spec, local);
            if (level <= 0f)
            {
                return new VoiceFrame(0f, spec.Start.F1, spec.Start.F2, 1f);
            }

            var p = Math.Clamp((t - delay) / span, 0f, 1f);
            var wave = 0.5f + (0.5f * MathF.Sin((2f * MathF.PI * shoutRate * t) + shoutPhase));
            level *= 1f - (spec.Shout * wave * wave);
            var f1 = (spec.Start.F1 + ((spec.End.F1 - spec.Start.F1) * p)) * scatter1;
            var f2 = (spec.Start.F2 + ((spec.End.F2 - spec.Start.F2) * p)) * scatter2;
            var pitch = 1f + (spec.Arc * MathF.Sin(MathF.PI * p)) + (spec.Slope * p);
            return new VoiceFrame(level, f1, f2, MathF.Max(0.4f, pitch));
        };
    }

    /// <summary>
    /// ざわめきの一人: 音節（70〜220 ms）ごとに母音と高さを変えて話し、ときどき長く黙る。母音はなめらかに移る。
    /// </summary>
    private static Func<float, VoiceFrame> TalkingFrame(Random random)
    {
        var next = random.NextSingle() * 2f;
        var talking = false;
        var target = Vowels[random.Next(Vowels.Length)];
        float f1 = target.F1, f2 = target.F2, pitch = 1f;
        var scatter = 0.9f + (0.2f * random.NextSingle());
        return t =>
        {
            if (t >= next)
            {
                talking = random.NextSingle() < 0.7f;
                target = Vowels[random.Next(Vowels.Length)];
                pitch = 0.9f + (0.25f * random.NextSingle());
                next = t + (talking
                    ? 0.07f + (0.15f * random.NextSingle())
                    : random.NextSingle() < 0.1f ? 1f + (2f * random.NextSingle()) : 0.04f + (0.2f * random.NextSingle()));
            }

            f1 += 0.06f * ((target.F1 * scatter) - f1);
            f2 += 0.06f * ((target.F2 * scatter) - f2);
            return new VoiceFrame(talking ? 1f : 0f, f1, f2, pitch);
        };
    }

    /// <summary>
    /// 一人の声を <paramref name="output"/> に足す。声帯（揺らぎのある鋸歯状波を少し丸めたもの）と息（ノイズ）を
    /// <paramref name="breath"/> の割合で混ぜ、F1・F2・F3 の 3 つの共鳴に通す。形は <see cref="ControlBlock"/> ごとに読む。
    /// 黙っている間は計算を飛ばす（長いざわめきを速く作るため）。
    /// </summary>
    private static void AddVoice(float[] output, float f0, float gain, float breath, Func<float, VoiceFrame> shape, Random random)
    {
        var r1 = new Resonator();
        var r2 = new Resonator();
        var r3 = new Resonator();
        var f3 = 2400f + (500f * random.NextSingle());
        var vibratoRate = 4.5f + (2f * random.NextSingle());
        var vibratoPhase = random.NextSingle() * 2f * MathF.PI;
        float phase = random.NextSingle(), smoothed = 0f, level = 0f, jitter = 1f;
        var frame = new VoiceFrame(0f, 500f, 1500f, 1f);
        for (var n = 0; n < output.Length; n++)
        {
            if (n % ControlBlock == 0)
            {
                var t = n / (float)SampleRate;
                frame = shape(t);
                if (frame.Level > 0f || level > 1e-4f)
                {
                    r1.Tune(frame.F1, 90f);
                    r2.Tune(frame.F2, 130f);
                    r3.Tune(f3, 200f);
                }
            }

            level += 0.004f * (frame.Level - level);
            if (frame.Level <= 0f && level < 1e-4f)
            {
                level = 0f;
                continue;
            }

            var time = n / (float)SampleRate;
            var hz = f0 * frame.Pitch * jitter * (1f + (0.012f * MathF.Sin((2f * MathF.PI * vibratoRate * time) + vibratoPhase)));
            phase += hz / SampleRate;
            if (phase >= 1f)
            {
                phase -= 1f;
                jitter = 0.98f + (0.04f * random.NextSingle()); // 一周期ごとの揺らぎ（声のかすれ）
            }

            smoothed += 0.3f * ((1f - (2f * phase)) - smoothed);
            var noise = (random.NextSingle() * 2f) - 1f;
            var source = (smoothed * (1f - breath)) + (noise * breath * 0.7f);
            var y = r1.Run(source) + (0.6f * r2.Run(source)) + (0.25f * r3.Run(source));
            output[n] += y * level * gain;
        }
    }

    /// <summary>
    /// 拍手: 1 回ごとに数 ms の帯域ノイズの破裂（手の大きさで 800〜2500 Hz）。<paramref name="density"/>（時刻 → 1 秒あたりの回数）に従って散らす。
    /// </summary>
    private static void AddApplause(float[] output, Func<float, float> density, float gain, Random random)
    {
        const int Step = 44; // 1 ms ごとに打つかどうかを決める
        for (var n = 0; n < output.Length; n += Step)
        {
            var rate = density(n / (float)SampleRate);
            var expected = rate * Step / SampleRate;
            while (expected > 0f)
            {
                if (random.NextSingle() < MathF.Min(1f, expected))
                {
                    AddClap(output, n + random.Next(Step), gain * (0.2f + (0.8f * random.NextSingle())), random);
                }

                expected -= 1f;
            }
        }
    }

    private static void AddClap(float[] output, int start, float gain, Random random)
    {
        var resonator = new Resonator();
        resonator.Tune(800f + (1700f * random.NextSingle()), 700f + (600f * random.NextSingle()));
        var decay = (0.0015f + (0.0025f * random.NextSingle())) * SampleRate;
        var end = Math.Min(output.Length, start + (int)(decay * 6f));
        for (var n = start; n < end; n++)
        {
            var noise = (random.NextSingle() * 2f) - 1f;
            output[n] += resonator.Run(noise) * MathF.Exp(-(n - start) / decay) * gain;
        }
    }

    /// <summary>指笛: 1.8〜3 kHz の正弦が上がってから少し下がる（ヒューッ）。細かい震えと息の音を少し。</summary>
    private static void AddWhistle(float[] output, int start, float gain, Random random)
    {
        var seconds = 0.3f + (0.5f * random.NextSingle());
        var hz = 1800f + (1200f * random.NextSingle());
        var rise = 0.1f + (0.25f * random.NextSingle());
        var length = (int)(seconds * SampleRate);
        var end = Math.Min(output.Length, start + length);
        var phase = 0f;
        for (var n = start; n < end; n++)
        {
            var p = (n - start) / (float)length;
            var envelope = MathF.Min(1f, p / 0.08f) * MathF.Min(1f, (1f - p) / 0.25f);
            var f = hz * (1f + (rise * MathF.Sin(MathF.PI * MathF.Min(1f, p * 1.3f)))) * (1f + (0.01f * MathF.Sin(2f * MathF.PI * 7f * p * seconds)));
            phase += 2f * MathF.PI * f / SampleRate;
            var breath = ((random.NextSingle() * 2f) - 1f) * 0.02f;
            output[n] += (MathF.Sin(phase) + breath) * envelope * gain * 0.25f;
        }
    }

    /// <summary>最後の <paramref name="seconds"/> を直線で消す（途中で切れたプツッを防ぐ）。</summary>
    private static void FadeTail(float[] samples, float seconds)
    {
        var fade = Math.Min(samples.Length, (int)(seconds * SampleRate));
        for (var i = 0; i < fade; i++)
        {
            samples[samples.Length - 1 - i] *= i / (float)fade;
        }
    }

    /// <summary>ピークを <paramref name="peak"/> に揃える（無音ならそのまま）。</summary>
    private static void Level(float[] samples, float peak)
    {
        var max = 0f;
        foreach (var s in samples)
        {
            max = MathF.Max(max, MathF.Abs(s));
        }

        if (max <= 1e-9f)
        {
            return;
        }

        var scale = peak / max;
        for (var n = 0; n < samples.Length; n++)
        {
            samples[n] *= scale;
        }
    }

    /// <summary>
    /// 帯域通過の共鳴（双二次・中心での利得 1）。直流と最高域に零点を置くので、息のノイズが低域でうなり続けない
    /// （ただの 2 極の共鳴は中心より下を平らに通してしまい、群衆が地鳴りのように聞こえた）。
    /// </summary>
    private sealed class Resonator
    {
        private float b0, a1, a2, x1, x2, y1, y2;

        public void Tune(float hz, float bandwidthHz)
        {
            var w0 = 2f * MathF.PI * Math.Clamp(hz, 50f, SampleRate * 0.45f) / SampleRate;
            var alpha = MathF.Sin(w0) * bandwidthHz / (2f * MathF.Max(hz, 50f));
            var a0 = 1f + alpha;
            b0 = alpha / a0;
            a1 = 2f * MathF.Cos(w0) / a0;
            a2 = -(1f - alpha) / a0;
        }

        public float Run(float x)
        {
            var y = (b0 * (x - x2)) + (a1 * y1) + (a2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }
    }
}
