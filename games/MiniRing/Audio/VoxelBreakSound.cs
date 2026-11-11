using Aigis.Render.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Audio;

/// <summary>砕けたボクセルの音の種類（材質から決まる）。</summary>
public enum GrainKind
{
    Skin,
    Flesh,
    Bone,
    Organ,
    Brain,
    Eye,
}

/// <summary>
/// ボクセルが砕ける音と、飛び散ったものが床に落ちる音（素材ファイルなし・合成）。ボクセル 1 個に 1 粒。
/// <para>
/// 音の作り方は方式（<see cref="BreakStyle"/>）ごと: ガラス・崩れる石・生々しく砕ける・電子機器のショート・金属（最初の音、比較用）。
/// どの方式も、材質ごとに粒を変える（皮膚は軽く、骨は硬く、臓器と脳は重く大きく、目は高く）。金属以外は
/// <see cref="BreakGrainSynth"/> の部品（ノイズの破裂・ごく短い共鳴の割れ・クリックの雨・低い打撃・電気）で作り、響きを短くして
/// 「たたいた楽器」ではなく「壊れた物」に聞こえるようにしている。
/// </para>
/// 1 発ぶんの粒（<see cref="ShatterEvent"/>）を、砕けた順に数ミリ秒ずつずらし、音程を少しずつ変えて重ねる — 粒だって聞こえる。
/// 致命傷には <see cref="Fatal"/>: 重低音と一気に上がる「シュワッ」（心地よさ）に、その方式の脳が砕ける音を何重にも重ね、
/// 電源が落ちていくような下降音と不協和なうなり（ヤバさ）を足す。
/// </summary>
public static class VoxelBreakSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>1 つの種類につき作っておく粒の変種（同じ粒の繰り返しに聞こえないように）。</summary>
    private const int Variants = 6;

    /// <summary>1 発で鳴らす粒の上限（それより多ければ間引いて、少し大きく鳴らす）。</summary>
    private const int MaxGrains = 48;

    /// <summary>ゲームが使う方式（起動オプション <c>--break-sound</c> で変えられる）。</summary>
    public const BreakStyle DefaultStyle = BreakStyle.Glass;

    /// <summary>破片の印（<see cref="VoxelDebris.Spawn"/> の tag）: 材質の番号のほかに、血・粉・火花。</summary>
    /// <summary>床に落ちる音のいちばん大きいときの音量（砕ける音は 1 前後）。</summary>
    public const float LandingLoudness = 0.035f;

    public const byte BloodTag = 250;
    public const byte DustTag = 251;
    public const byte SparkTag = 252;

    /// <summary>1 フレームに鳴らす落下音の粒の上限（速く落ちたものから）。</summary>
    private const int MaxLandings = 24;

    private static readonly Dictionary<BreakStyle, float[][][]> Banks = [];
    private static readonly Dictionary<BreakStyle, float[][][]> LandingBanks = [];

    /// <summary>方式ごとの粒の変種（種類 × 変種）。初めて使うときに作る。</summary>
    private static float[][][] Bank(BreakStyle style)
    {
        lock (Banks)
        {
            if (!Banks.TryGetValue(style, out var bank))
            {
                bank = [.. Enum.GetValues<GrainKind>().Select(kind => Enumerable.Range(0, Variants).Select(v => Grain(kind, (1009 * ((int)kind + 1)) + v, style)).ToArray())];
                Banks[style] = bank;
            }

            return bank;
        }
    }

    private static float[][][] LandingBank(BreakStyle style)
    {
        lock (LandingBanks)
        {
            if (!LandingBanks.TryGetValue(style, out var bank))
            {
                bank = [.. Enum.GetValues<LandingKind>().Select(kind => Enumerable.Range(0, Variants)
                    .Select(v => kind == LandingKind.Silent ? [] : BreakGrainSynth.Render(BreakGrainSynth.Landing(style, kind), (7919 * ((int)kind + 1)) + v)).ToArray())];
                LandingBanks[style] = bank;
            }

            return bank;
        }
    }

    /// <summary>破片の印の、床に落ちる音の種類。</summary>
    public static LandingKind LandingKindOf(byte tag) => tag switch
    {
        BloodTag => LandingKind.Blood,
        DustTag => LandingKind.Dust,
        SparkTag => LandingKind.Silent,
        BoxerAnatomy.Bone or BoxerAnatomy.Eye => LandingKind.Hard,
        _ => LandingKind.Chunk,
    };

    /// <summary>方式の粒（砕ける・落ちる）を前もって作っておく（試合中の最初の 1 回で引っかからないように）。</summary>
    public static void Prewarm(BreakStyle style)
    {
        _ = Bank(style);
        _ = LandingBank(style);
    }

    /// <summary>材質（<see cref="BoxerAnatomy"/> の番号）の音の種類。</summary>
    public static GrainKind KindOf(byte material) => material switch
    {
        BoxerAnatomy.Skin => GrainKind.Skin,
        _ when BoxerAnatomy.IsClothing(material) => GrainKind.Skin, // 布と革は皮膚の代わりの層
        BoxerAnatomy.Bone => GrainKind.Bone,
        BoxerAnatomy.Brain => GrainKind.Brain,
        BoxerAnatomy.Heart or BoxerAnatomy.Lung or BoxerAnatomy.Liver or BoxerAnatomy.Gut => GrainKind.Organ,
        BoxerAnatomy.Eye => GrainKind.Eye,
        _ => GrainKind.Flesh,
    };

    /// <summary>1 発ぶんの砕ける音（16-bit モノラル）。<paramref name="seed"/> で粒の変種と音程の揺れが決まる。</summary>
    public static byte[] Synthesize(ShatterEvent shatter, int seed, BreakStyle style = DefaultStyle)
    {
        ArgumentNullException.ThrowIfNull(shatter);
        var mix = Mix(shatter.Grains, seed, style);
        var organs = shatter.Grains.Count(g => KindOf(g.Material) is GrainKind.Organ or GrainKind.Brain);
        HitSound.AddRoom(mix, 0.18f);
        return HitSound.ToPcm16(mix, organs > 0 ? 0.95f : 0.75f);
    }

    /// <summary>粒を重ねた生の波形（クリップ前）。</summary>
    public static float[] Mix(IReadOnlyList<ShatterGrain> grains, int seed, BreakStyle style = DefaultStyle)
    {
        ArgumentNullException.ThrowIfNull(grains);
        var random = new Random(seed);
        var bank = Bank(style);
        var step = grains.Count > MaxGrains ? (float)grains.Count / MaxGrains : 1f;
        var used = new List<ShatterGrain>();
        for (var i = 0f; i < grains.Count; i += step)
        {
            used.Add(grains[(int)i]);
        }

        var boost = MathF.Sqrt(step); // 間引いた分、残った粒を少し大きく
        var end = used.Count == 0 ? 0f : used.Max(g => g.Delay) + 0.4f;
        var mix = new float[(int)(end * SampleRate) + 1];
        foreach (var grain in used)
        {
            var kind = KindOf(grain.Material);
            var source = bank[(int)kind][random.Next(Variants)];
            var pitch = 0.85f + (0.3f * random.NextSingle());
            var gain = boost * (kind switch
            {
                GrainKind.Skin => 0.35f,
                GrainKind.Flesh => 0.5f,
                GrainKind.Bone => 0.6f,
                GrainKind.Eye => 0.6f,
                _ => 0.9f, // 臓器・脳: ダメージが大きそうに重く
            }) * (0.75f + (0.25f * random.NextSingle()));
            AddPitched(mix, source, (int)(grain.Delay * SampleRate), pitch, gain);
        }

        return mix;
    }

    /// <summary>
    /// 1 フレームぶんの、床に落ちた破片の音（16-bit モノラル）。落ちたものごとに 1 粒を、そのフレームの中で少しずつずらして重ねる:
    /// 小さいものほど高く、速く落ちたものほど大きく。火花は鳴らない。鳴らすものが無ければ null。
    /// </summary>
    public static byte[]? Landings(IReadOnlyList<DebrisLanding> landings, int seed, BreakStyle style = DefaultStyle)
    {
        ArgumentNullException.ThrowIfNull(landings);
        var audible = landings.Where(l => LandingKindOf(l.Tag) != LandingKind.Silent).OrderByDescending(l => l.Speed).Take(MaxLandings).ToList();
        if (audible.Count == 0)
        {
            return null;
        }

        var random = new Random(seed);
        var bank = LandingBank(style);
        var grains = audible.Select(landing => (
            Source: bank[(int)LandingKindOf(landing.Tag)][random.Next(Variants)],
            Pitch: Math.Clamp(0.005f / MathF.Max(1e-4f, landing.Size), 0.7f, 1.6f) * (0.9f + (0.2f * random.NextSingle())),
            Gain: Math.Clamp(landing.Speed / 2.5f, 0.1f, 1f) * Math.Clamp(landing.Size / 0.005f, 0.3f, 1.5f) * 0.5f,
            Start: (int)(random.NextSingle() * 0.016f * SampleRate))).ToList();
        // 低く伸ばした粒も最後まで入る長さ（途中で切ると「プツッ」と鳴る）。
        var mix = new float[grains.Max(g => g.Start + (int)(g.Source.Length / g.Pitch) + 1)];
        foreach (var (source, pitch, gain, start) in grains)
        {
            AddPitched(mix, source, start, pitch, gain);
        }

        // 背景の「パラパラ」として、砕ける音よりずっと小さく。そっと落ちただけのフレームはさらに小さく（粒の音量で）。
        var strongest = Math.Clamp(grains.Max(g => g.Gain) / 0.5f, 0.3f, 1f);
        return HitSound.ToPcm16(mix, LandingLoudness * strongest * Math.Clamp(0.5f + (0.05f * audible.Count), 0.5f, 1f));
    }

    /// <summary>
    /// 致命傷の音（約 1.8 秒）。心地よさ: 深い重低音の「ドン」と、一気に上がる「シュワッ」。破壊: その方式の脳が砕ける音を
    /// 8 重に重ねる。ヤバさ: 機械の電源が落ちていくような下降音（量子化と、つまずくような繰り返し付き）と、三全音の不協和なうなり。
    /// </summary>
    public static byte[] Fatal(int seed, BreakStyle style = DefaultStyle)
    {
        var random = new Random(seed);
        var length = (int)(1.8f * SampleRate);
        var s = new float[length];
        float boomPhase = 0f, downPhase = 0f, held = 0f, sweepLow = 0f, sweepBand = 0f;
        for (var n = 0; n < length; n++)
        {
            var t = n / (float)SampleRate;

            // ドン: 52Hz から下がっていく重低音。
            var boomHz = 32f + (20f * MathF.Exp(-t / 0.25f));
            boomPhase += 2f * MathF.PI * boomHz / SampleRate;
            var boom = MathF.Sin(boomPhase) * MathF.Exp(-t / 0.55f) * MathF.Min(1f, t / 0.004f);

            // シュワッ: 400Hz から 6kHz へ一気に上がる帯域のノイズ（決まった感じ）。
            var sweepHz = 400f * MathF.Pow(15f, MathF.Min(1f, t / 0.22f));
            var a = 1f - MathF.Exp(-2f * MathF.PI * sweepHz / SampleRate);
            var noise = (2f * random.NextSingle()) - 1f;
            sweepLow += a * (noise - sweepLow);
            sweepBand += (a * 0.5f) * (sweepLow - sweepBand);
            var sweep = (sweepLow - sweepBand) * 2f * MathF.Min(1f, t / 0.05f) * MathF.Exp(-MathF.Max(0f, t - 0.2f) / 0.12f) * 0.5f;

            // 電源が落ちる: 420Hz から下がる矩形とのこぎり。だんだん粗く（量子化・間引き）なる。
            var down = 0f;
            if (t > 0.12f)
            {
                var td = t - 0.12f;
                var hz = 25f + (420f * MathF.Exp(-td / 0.42f));
                downPhase += hz / SampleRate;
                var frac = downPhase - MathF.Floor(downPhase);
                var raw = (0.6f * (frac < 0.5f ? 1f : -1f)) + (0.4f * ((2f * frac) - 1f));
                var decimate = 1 + (int)(td * 40f);
                if (n % decimate == 0)
                {
                    held = Crush(raw, Math.Max(2, 12 - (int)(td * 8f)));
                }

                down = held * MathF.Exp(-td / 0.7f) * 0.4f;
            }

            // 三全音のうなり（55Hz と 77.8Hz）、ゆっくり揺れる。
            var dread = (MathF.Sin(2f * MathF.PI * 55f * t) + MathF.Sin(2f * MathF.PI * 77.78f * t)) * 0.18f
                * MathF.Min(1f, t / 0.25f) * MathF.Exp(-MathF.Max(0f, t - 0.6f) / 0.5f) * (0.75f + (0.25f * MathF.Sin(2f * MathF.PI * 5f * t)));

            s[n] = boom + sweep + down + dread;
        }

        // 破壊: その方式の脳が砕ける音を 8 重に、最初の 0.25 秒へ散らして。
        var brain = Bank(style)[(int)GrainKind.Brain];
        for (var k = 0; k < 8; k++)
        {
            AddPitched(s, brain[k % Variants], (int)(0.25f * random.NextSingle() * random.NextSingle() * SampleRate), 0.8f + (0.4f * random.NextSingle()), 0.6f);
        }

        // つまずき: 0.45〜0.9 秒で、直前の 25ms を 3 回ずつ繰り返す（壊れた機械の再生のように）。
        var chunk = (int)(0.025f * SampleRate);
        for (var start = (int)(0.45f * SampleRate); start + (4 * chunk) < (int)(0.9f * SampleRate); start += 4 * chunk)
        {
            for (var r = 1; r < 3; r++)
            {
                Array.Copy(s, start, s, start + (r * chunk), chunk);
            }
        }

        HitSound.AddRoom(s, 0.25f);
        return HitSound.ToPcm16(s, 1f);
    }

    /// <summary><paramref name="source"/> を <paramref name="start"/> から、音程を <paramref name="pitch"/> 倍に変えて足す（線形補間）。</summary>
    private static void AddPitched(float[] mix, float[] source, int start, float pitch, float gain)
    {
        var length = (int)(source.Length / pitch);
        for (var n = 0; n < length && start + n < mix.Length; n++)
        {
            var at = n * pitch;
            var i0 = (int)at;
            var frac = at - i0;
            var a = source[i0];
            var b = i0 + 1 < source.Length ? source[i0 + 1] : 0f;
            mix[start + n] += gain * (a + ((b - a) * frac));
        }
    }

    /// <summary>1 粒の波形。金属は最初の合成、ほかは <see cref="BreakGrainSynth"/> の作り方で。</summary>
    internal static float[] Grain(GrainKind kind, int seed, BreakStyle style = DefaultStyle)
        => UsesMetal(style, kind) ? MetalGrain(kind, seed) : BreakGrainSynth.Render(BreakGrainSynth.Break(style, kind), seed);

    /// <summary>金属の粒で鳴らすか。ガラスでも、臓器と脳は金属（重く壊れた手応えに）。</summary>
    private static bool UsesMetal(BreakStyle style, GrainKind kind)
        => style == BreakStyle.Metal || (style == BreakStyle.Glass && kind is GrainKind.Organ or GrainKind.Brain);

    /// <summary>金属の 1 粒（最初の音: 倍音の響き。約 30〜320ms）。</summary>
    private static float[] MetalGrain(GrainKind kind, int seed)
    {
        var random = new Random(seed);
        float J() => 0.94f + (0.12f * random.NextSingle()); // 変種ごとの少しの違い
        var length = (int)(kind switch
        {
            GrainKind.Skin => 0.03f,
            GrainKind.Flesh => 0.07f,
            GrainKind.Bone => 0.16f,
            GrainKind.Organ => 0.26f,
            GrainKind.Brain => 0.32f,
            _ => 0.12f,
        } * SampleRate);
        var s = new float[length];
        var hp = 0f;
        float phase = 0f, held = 0f;
        var holdEvery = kind == GrainKind.Brain ? SampleRate / 1600 : SampleRate / 2400;
        var (f1, f2, f3, f4) = (J(), J(), J(), J());
        for (var n = 0; n < length; n++)
        {
            var t = n / (float)SampleRate;
            var noise = Noise(random);
            hp += 0.25f * (noise - hp);
            var bright = noise - hp; // 高域だけ残したノイズ
            var v = 0f;
            switch (kind)
            {
                case GrainKind.Skin:
                    v = (bright * MathF.Exp(-t / 0.0015f) * 0.8f)
                        + (Partial(3150f * f1, t, 0.008f, 0.4f) + Partial(4730f * f2, t, 0.007f, 0.25f) + Partial(6290f * f3, t, 0.005f, 0.15f));
                    break;
                case GrainKind.Flesh:
                {
                    phase += 2f * MathF.PI * (100f + (50f * MathF.Exp(-t / 0.01f))) * f1 / SampleRate;
                    var clunk = MathF.Sin(phase) * MathF.Exp(-t / 0.03f) * 0.7f;
                    var gear = (MathF.Sin(2f * MathF.PI * 220f * f2 * t) > 0f ? 1f : 0f) * noise * MathF.Exp(-t / 0.02f) * 0.6f;
                    v = clunk + gear + Partial(1900f * f3, t, 0.01f, 0.25f);
                    break;
                }

                case GrainKind.Bone:
                    v = (Crush(bright, 6) * MathF.Exp(-t / 0.003f) * 0.9f)
                        + Partial(1240f * f1, t, 0.09f, 0.6f) + Partial(2960f * f2, t, 0.06f, 0.5f)
                        + Partial(4710f * f3, t, 0.04f, 0.35f) + Partial(6830f * f4, t, 0.025f, 0.2f)
                        + Partial(1253f * f1, t, 0.09f, 0.3f); // わずかにずれた倍音でうなる（金属板）
                    break;
                case GrainKind.Organ:
                case GrainKind.Brain:
                {
                    var brain = kind == GrainKind.Brain;
                    phase += 2f * MathF.PI * ((brain ? 40f : 50f) + ((brain ? 30f : 35f) * MathF.Exp(-t / 0.03f))) * f1 / SampleRate;
                    var thump = MathF.Sin(phase) * MathF.Exp(-t / (brain ? 0.12f : 0.1f));
                    if (n % holdEvery == 0)
                    {
                        held = Crush(noise, 8); // 粗く量子化した砕ける音
                    }

                    var crunch = held * MathF.Exp(-t / (brain ? 0.06f : 0.05f)) * 0.7f;
                    var groan = MathF.Tanh(2.5f * (MathF.Sin(2f * MathF.PI * 210f * f2 * t) + MathF.Sin(2f * MathF.PI * 223f * f2 * t))) * MathF.Exp(-t / 0.12f) * 0.3f;
                    v = thump + crunch + groan;
                    if (brain)
                    {
                        var saw = 2f * ((96f * f3 * t) - MathF.Floor(0.5f + (96f * f3 * t)));
                        var buzz = MathF.Tanh(3f * saw) * MathF.Exp(-t / 0.15f) * 0.3f;
                        var chirpHz = 300f + (2700f * MathF.Exp(-t / 0.025f)); // 下がっていく放電
                        var zap = MathF.Sin(2f * MathF.PI * chirpHz * t) * MathF.Exp(-t / 0.08f) * 0.3f;
                        v += buzz + zap;
                    }

                    break;
                }

                default: // Eye
                    v = Partial(4400f * f1, t, 0.05f, 0.45f) + Partial(6100f * f2, t, 0.04f, 0.3f) + Partial(7900f * f3, t, 0.03f, 0.2f)
                        + (bright * MathF.Exp(-t / 0.01f) * 0.35f)
                        + (MathF.Sin(2f * MathF.PI * 300f * t) * MathF.Exp(-t / 0.015f) * 0.3f);
                    break;
            }

            s[n] = v * MathF.Min(1f, t / 0.0006f);
        }

        return s;
    }

    private static float Partial(float hz, float t, float decay, float gain) => MathF.Sin(2f * MathF.PI * hz * t) * MathF.Exp(-t / decay) * gain;

    private static float Noise(Random random) => (2f * random.NextSingle()) - 1f;

    /// <summary>振幅を <paramref name="levels"/> 段に量子化する（ビットクラッシュ: デジタルに壊れた感じ）。</summary>
    private static float Crush(float v, int levels) => MathF.Round(v * levels) / levels;
}
