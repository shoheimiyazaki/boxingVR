namespace MiniRing.Audio;

/// <summary>砕ける音の方式（聞き比べて選ぶ。<c>--break-sound</c>）。</summary>
public enum BreakStyle
{
    /// <summary>ガラス: 多数の高い共鳴の破裂と、遅れてチリチリ落ちる欠片。</summary>
    Glass,

    /// <summary>崩れる石・陶器: 乾いたクリックの雨と、こもった胴鳴り。響かない。</summary>
    Crumble,

    /// <summary>生々しく砕ける: 乾いた「バキ」「グシャ」、臓器は湿った重い潰れ。</summary>
    Crunch,

    /// <summary>電子機器のショート: パチパチ、唸り、放電。</summary>
    Circuit,

    /// <summary>金属（最初の音）: 倍音の響き。比較用。</summary>
    Metal,
}

/// <summary>
/// 1 粒の作り方。部品を組み合わせる:
/// <list type="bullet">
/// <item>胴（Body）: 帯域を下から絞ったノイズの破裂。</item>
/// <item>割れ（Cracks）: 不規則な周波数の共鳴器を、ごく短く（数〜十数 ms）鳴らす細かい破裂の連打。響きを短くして鉄琴にしない。</item>
/// <item>欠片（Tinkles）: 遅れて落ちる小さな割れ（ガラスの欠片）。</item>
/// <item>打撃（Thump）: 重さを出す低い音（音程が下がる）。</item>
/// <item>クリック（Crackle）: ばらばらと鳴る広帯域のクリックの雨（石・電気）。</item>
/// <item>唸り（Buzz）・放電（Zap）: 電気。量子化（Crush）: デジタルに壊れた感じ。</item>
/// </list>
/// </summary>
internal readonly record struct GrainRecipe(
    float Length,
    float BodyGain = 0f, float BodyCutoff = 1000f, float BodyDecay = 0.01f,
    int Cracks = 0, float CrackWindow = 0.01f, float CrackLow = 1000f, float CrackHigh = 5000f, float CrackDecay = 0.006f, float CrackGain = 0f,
    int Tinkles = 0, float TinkleWindow = 0.1f, float TinkleLow = 4000f, float TinkleHigh = 9000f, float TinkleDecay = 0.012f, float TinkleGain = 0f,
    float ThumpHz = 0f, float ThumpDrop = 0.5f, float ThumpDecay = 0.05f, float ThumpGain = 0f,
    float CrackleRate = 0f, float CrackleDecay = 0.02f, float CrackleGain = 0f,
    float BuzzHz = 0f, float BuzzDecay = 0.05f, float BuzzGain = 0f,
    float ZapGain = 0f,
    int Crush = 0);

/// <summary>粒の合成（<see cref="GrainRecipe"/> の部品を足し合わせる）と、方式 × 材質ごとの作り方。</summary>
internal static class BreakGrainSynth
{
    private const int SampleRate = HitSound.SampleRate;

    public static float[] Render(GrainRecipe r, int seed)
    {
        var random = new Random(seed);
        float U() => random.NextSingle();
        float N() => (2f * U()) - 1f;
        var length = Math.Max(1, (int)(r.Length * SampleRate));
        var s = new float[length];

        // 割れと欠片: 共鳴器ごとに開始時刻・周波数（対数で一様）・減衰・強さを決める。割れは始まりに固まる。
        var res = new List<(int Start, float A1, float A2, float Gain)>();
        void AddResonators(int count, float window, float low, float high, float decay, float gain, bool early)
        {
            for (var i = 0; i < count; i++)
            {
                var when = early ? window * U() * U() : window * (0.2f + (0.8f * U()));
                var hz = low * MathF.Pow(high / low, U());
                var d = decay * (0.5f + U());
                var rr = MathF.Exp(-1f / (d * SampleRate));
                res.Add(((int)(when * SampleRate), 2f * rr * MathF.Cos(2f * MathF.PI * hz / SampleRate), -rr * rr, gain * (0.4f + (0.6f * U())) * (1f - rr) * 4f));
            }
        }

        AddResonators(r.Cracks, r.CrackWindow, r.CrackLow, r.CrackHigh, r.CrackDecay, r.CrackGain, early: true);
        AddResonators(r.Tinkles, r.TinkleWindow, r.TinkleLow, r.TinkleHigh, r.TinkleDecay, r.TinkleGain, early: false);
        var y1 = new float[res.Count];
        var y2 = new float[res.Count];

        var bodyA = 1f - MathF.Exp(-2f * MathF.PI * r.BodyCutoff / SampleRate);
        float lp1 = 0f, lp2 = 0f, phase = 0f, buzzPhase = 0f, held = 0f;
        for (var n = 0; n < length; n++)
        {
            var t = n / (float)SampleRate;
            var v = 0f;

            if (r.BodyGain > 0f)
            {
                lp1 += bodyA * (N() - lp1);
                lp2 += bodyA * (lp1 - lp2);
                v += lp2 * 2.5f * MathF.Exp(-t / r.BodyDecay) * r.BodyGain;
            }

            for (var i = 0; i < res.Count; i++)
            {
                // 励起は 0.4ms の短いノイズ（クリック）: 共鳴器の音にざらつきを足し、純音にしない。
                var since = n - res[i].Start;
                var x = since is >= 0 and < 18 ? N() * res[i].Gain : 0f;
                var y = x + (res[i].A1 * y1[i]) + (res[i].A2 * y2[i]);
                y2[i] = y1[i];
                y1[i] = y;
                v += y;
            }

            if (r.ThumpGain > 0f)
            {
                phase += 2f * MathF.PI * r.ThumpHz * (1f + (r.ThumpDrop * MathF.Exp(-t / 0.012f))) / SampleRate;
                v += MathF.Sin(phase) * MathF.Exp(-t / r.ThumpDecay) * r.ThumpGain;
            }

            if (r.CrackleGain > 0f && U() < r.CrackleRate / SampleRate)
            {
                v += N() * MathF.Exp(-t / r.CrackleDecay) * r.CrackleGain * 3f;
            }

            if (r.BuzzGain > 0f)
            {
                buzzPhase += r.BuzzHz / SampleRate;
                var square = (buzzPhase - MathF.Floor(buzzPhase)) < 0.5f ? 1f : -1f;
                v += MathF.Tanh(2f * (square + (0.3f * N()))) * MathF.Exp(-t / r.BuzzDecay) * r.BuzzGain;
            }

            if (r.ZapGain > 0f)
            {
                var hz = 300f + (2700f * MathF.Exp(-t / 0.012f));
                v += MathF.Sin(2f * MathF.PI * hz * t) * MathF.Exp(-t / 0.04f) * r.ZapGain;
            }

            if (r.Crush > 0)
            {
                if (n % 3 == 0)
                {
                    held = MathF.Round(v * r.Crush) / r.Crush; // 量子化して標本も間引く
                }

                v = held;
            }

            s[n] = v * MathF.Min(1f, t / 0.0004f) * MathF.Min(1f, (length - n) / (0.004f * SampleRate));
        }

        return s;
    }

    /// <summary>方式 × 材質の砕ける粒。</summary>
    public static GrainRecipe Break(BreakStyle style, GrainKind kind) => (style, kind) switch
    {
        (BreakStyle.Glass, GrainKind.Skin) => new(0.06f, BodyGain: 0.15f, BodyCutoff: 3000f, BodyDecay: 0.004f,
            Cracks: 5, CrackWindow: 0.008f, CrackLow: 2500f, CrackHigh: 7000f, CrackDecay: 0.006f, CrackGain: 0.5f,
            Tinkles: 2, TinkleWindow: 0.04f, TinkleLow: 5000f, TinkleHigh: 9000f, TinkleDecay: 0.01f, TinkleGain: 0.12f),
        (BreakStyle.Glass, GrainKind.Flesh) => new(0.09f, BodyGain: 0.35f, BodyCutoff: 1200f, BodyDecay: 0.012f,
            Cracks: 6, CrackWindow: 0.015f, CrackLow: 1500f, CrackHigh: 5000f, CrackDecay: 0.006f, CrackGain: 0.45f,
            Tinkles: 2, TinkleWindow: 0.05f, TinkleLow: 4000f, TinkleHigh: 8000f, TinkleDecay: 0.01f, TinkleGain: 0.1f,
            ThumpHz: 160f, ThumpDecay: 0.02f, ThumpGain: 0.3f),
        (BreakStyle.Glass, GrainKind.Bone) => new(0.22f, BodyGain: 0.4f, BodyCutoff: 5000f, BodyDecay: 0.006f,
            Cracks: 18, CrackWindow: 0.03f, CrackLow: 1800f, CrackHigh: 9000f, CrackDecay: 0.012f, CrackGain: 0.7f,
            Tinkles: 8, TinkleWindow: 0.15f, TinkleLow: 3500f, TinkleHigh: 10000f, TinkleDecay: 0.015f, TinkleGain: 0.2f),
        (BreakStyle.Glass, GrainKind.Organ) => new(0.3f, BodyGain: 0.6f, BodyCutoff: 900f, BodyDecay: 0.04f,
            Cracks: 12, CrackWindow: 0.03f, CrackLow: 900f, CrackHigh: 4500f, CrackDecay: 0.01f, CrackGain: 0.5f,
            Tinkles: 5, TinkleWindow: 0.18f, TinkleLow: 2500f, TinkleHigh: 7000f, TinkleDecay: 0.02f, TinkleGain: 0.15f,
            ThumpHz: 75f, ThumpDrop: 0.6f, ThumpDecay: 0.09f, ThumpGain: 0.9f, Crush: 10),
        (BreakStyle.Glass, GrainKind.Brain) => new(0.34f, BodyGain: 0.7f, BodyCutoff: 900f, BodyDecay: 0.05f,
            Cracks: 16, CrackWindow: 0.04f, CrackLow: 900f, CrackHigh: 5000f, CrackDecay: 0.012f, CrackGain: 0.55f,
            Tinkles: 8, TinkleWindow: 0.22f, TinkleLow: 2500f, TinkleHigh: 8000f, TinkleDecay: 0.02f, TinkleGain: 0.18f,
            ThumpHz: 55f, ThumpDrop: 0.7f, ThumpDecay: 0.12f, ThumpGain: 1f, Crush: 10),
        (BreakStyle.Glass, GrainKind.Eye) => new(0.2f, BodyGain: 0.2f, BodyCutoff: 6000f, BodyDecay: 0.003f,
            Cracks: 14, CrackWindow: 0.012f, CrackLow: 3000f, CrackHigh: 11000f, CrackDecay: 0.015f, CrackGain: 0.6f,
            Tinkles: 10, TinkleWindow: 0.17f, TinkleLow: 5000f, TinkleHigh: 12000f, TinkleDecay: 0.02f, TinkleGain: 0.25f),

        (BreakStyle.Crumble, GrainKind.Skin) => new(0.05f, BodyGain: 0.4f, BodyCutoff: 2500f, BodyDecay: 0.006f,
            CrackleRate: 4000f, CrackleDecay: 0.01f, CrackleGain: 0.3f),
        (BreakStyle.Crumble, GrainKind.Flesh) => new(0.09f, BodyGain: 0.6f, BodyCutoff: 1200f, BodyDecay: 0.02f,
            CrackleRate: 3000f, CrackleDecay: 0.025f, CrackleGain: 0.35f, ThumpHz: 140f, ThumpDecay: 0.025f, ThumpGain: 0.4f),
        (BreakStyle.Crumble, GrainKind.Bone) => new(0.16f, BodyGain: 0.7f, BodyCutoff: 2200f, BodyDecay: 0.02f,
            Cracks: 10, CrackWindow: 0.03f, CrackLow: 700f, CrackHigh: 2500f, CrackDecay: 0.004f, CrackGain: 0.5f,
            CrackleRate: 6000f, CrackleDecay: 0.05f, CrackleGain: 0.5f),
        (BreakStyle.Crumble, GrainKind.Organ) => new(0.28f, BodyGain: 0.8f, BodyCutoff: 700f, BodyDecay: 0.06f,
            Cracks: 8, CrackWindow: 0.05f, CrackLow: 400f, CrackHigh: 1500f, CrackDecay: 0.004f, CrackGain: 0.4f,
            ThumpHz: 70f, ThumpDrop: 0.6f, ThumpDecay: 0.1f, ThumpGain: 1f, CrackleRate: 3500f, CrackleDecay: 0.06f, CrackleGain: 0.5f),
        (BreakStyle.Crumble, GrainKind.Brain) => new(0.32f, BodyGain: 0.85f, BodyCutoff: 700f, BodyDecay: 0.07f,
            Cracks: 10, CrackWindow: 0.06f, CrackLow: 400f, CrackHigh: 1500f, CrackDecay: 0.004f, CrackGain: 0.45f,
            ThumpHz: 55f, ThumpDrop: 0.7f, ThumpDecay: 0.12f, ThumpGain: 1f, CrackleRate: 4500f, CrackleDecay: 0.08f, CrackleGain: 0.55f),
        (BreakStyle.Crumble, GrainKind.Eye) => new(0.08f, BodyGain: 0.4f, BodyCutoff: 4000f, BodyDecay: 0.005f,
            Cracks: 6, CrackWindow: 0.01f, CrackLow: 2000f, CrackHigh: 5000f, CrackDecay: 0.004f, CrackGain: 0.4f,
            CrackleRate: 8000f, CrackleDecay: 0.02f, CrackleGain: 0.4f),

        (BreakStyle.Crunch, GrainKind.Skin) => new(0.04f, BodyGain: 0.5f, BodyCutoff: 3500f, BodyDecay: 0.005f,
            CrackleRate: 9000f, CrackleDecay: 0.006f, CrackleGain: 0.4f, Crush: 6),
        (BreakStyle.Crunch, GrainKind.Flesh) => new(0.1f, BodyGain: 0.8f, BodyCutoff: 900f, BodyDecay: 0.03f,
            CrackleRate: 5000f, CrackleDecay: 0.02f, CrackleGain: 0.5f, ThumpHz: 120f, ThumpDrop: 0.6f, ThumpDecay: 0.03f, ThumpGain: 0.5f, Crush: 8),
        (BreakStyle.Crunch, GrainKind.Bone) => new(0.14f, BodyGain: 0.6f, BodyCutoff: 2500f, BodyDecay: 0.015f,
            Cracks: 14, CrackWindow: 0.02f, CrackLow: 600f, CrackHigh: 3000f, CrackDecay: 0.003f, CrackGain: 0.8f,
            ThumpHz: 110f, ThumpDecay: 0.02f, ThumpGain: 0.5f, CrackleRate: 12000f, CrackleDecay: 0.03f, CrackleGain: 0.7f, Crush: 5),
        (BreakStyle.Crunch, GrainKind.Organ) => new(0.3f, BodyGain: 1f, BodyCutoff: 600f, BodyDecay: 0.07f,
            ThumpHz: 65f, ThumpDrop: 0.7f, ThumpDecay: 0.1f, ThumpGain: 1f, CrackleRate: 6000f, CrackleDecay: 0.07f, CrackleGain: 0.6f, Crush: 6),
        (BreakStyle.Crunch, GrainKind.Brain) => new(0.34f, BodyGain: 1f, BodyCutoff: 600f, BodyDecay: 0.08f,
            ThumpHz: 50f, ThumpDrop: 0.8f, ThumpDecay: 0.13f, ThumpGain: 1f, CrackleRate: 9000f, CrackleDecay: 0.09f, CrackleGain: 0.7f, Crush: 6),
        (BreakStyle.Crunch, GrainKind.Eye) => new(0.1f, BodyGain: 0.7f, BodyCutoff: 1800f, BodyDecay: 0.02f,
            Cracks: 5, CrackWindow: 0.01f, CrackLow: 2500f, CrackHigh: 6000f, CrackDecay: 0.005f, CrackGain: 0.5f,
            CrackleRate: 6000f, CrackleDecay: 0.02f, CrackleGain: 0.4f),

        (BreakStyle.Circuit, GrainKind.Skin) => new(0.05f, CrackleRate: 12000f, CrackleDecay: 0.008f, CrackleGain: 0.5f, ZapGain: 0.2f, Crush: 4),
        (BreakStyle.Circuit, GrainKind.Flesh) => new(0.1f, CrackleRate: 9000f, CrackleDecay: 0.03f, CrackleGain: 0.5f,
            BuzzHz: 120f, BuzzDecay: 0.04f, BuzzGain: 0.4f, ThumpHz: 150f, ThumpDecay: 0.02f, ThumpGain: 0.3f, Crush: 5),
        (BreakStyle.Circuit, GrainKind.Bone) => new(0.16f, Cracks: 8, CrackWindow: 0.02f, CrackLow: 1500f, CrackHigh: 6000f, CrackDecay: 0.004f, CrackGain: 0.5f,
            CrackleRate: 15000f, CrackleDecay: 0.04f, CrackleGain: 0.6f, ZapGain: 0.3f, Crush: 4),
        (BreakStyle.Circuit, GrainKind.Organ) => new(0.32f, ThumpHz: 60f, ThumpDrop: 0.6f, ThumpDecay: 0.1f, ThumpGain: 0.9f,
            CrackleRate: 10000f, CrackleDecay: 0.1f, CrackleGain: 0.6f, BuzzHz: 60f, BuzzDecay: 0.15f, BuzzGain: 0.6f, ZapGain: 0.4f, Crush: 4),
        (BreakStyle.Circuit, GrainKind.Brain) => new(0.38f, ThumpHz: 45f, ThumpDrop: 0.7f, ThumpDecay: 0.12f, ThumpGain: 1f,
            CrackleRate: 14000f, CrackleDecay: 0.14f, CrackleGain: 0.7f, BuzzHz: 96f, BuzzDecay: 0.2f, BuzzGain: 0.7f, ZapGain: 0.6f, Crush: 3),
        (BreakStyle.Circuit, GrainKind.Eye) => new(0.1f, Cracks: 6, CrackWindow: 0.01f, CrackLow: 3000f, CrackHigh: 9000f, CrackDecay: 0.006f, CrackGain: 0.4f,
            CrackleRate: 16000f, CrackleDecay: 0.02f, CrackleGain: 0.5f, ZapGain: 0.3f),

        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "金属は VoxelBreakSound の元の合成で作る。"),
    };

    /// <summary>方式 × 落ちたものの、床に落ちる粒（短く小さい）。</summary>
    public static GrainRecipe Landing(BreakStyle style, LandingKind kind) => kind switch
    {
        // 血はどの方式でも湿った「ピチャ」。粉はかすかな「サッ」。
        LandingKind.Blood => new(0.04f, BodyGain: 0.5f, BodyCutoff: 500f, BodyDecay: 0.012f, CrackleRate: 3000f, CrackleDecay: 0.008f, CrackleGain: 0.15f),
        LandingKind.Dust => new(0.03f, CrackleRate: 6000f, CrackleDecay: 0.01f, CrackleGain: 0.2f),
        _ => (style, kind == LandingKind.Hard) switch
        {
            (BreakStyle.Glass, false) => new(0.06f, BodyGain: 0.2f, BodyCutoff: 1500f, BodyDecay: 0.005f,
                Cracks: 2, CrackWindow: 0.004f, CrackLow: 3000f, CrackHigh: 7000f, CrackDecay: 0.008f, CrackGain: 0.4f),
            (BreakStyle.Glass, true) => new(0.09f, Cracks: 4, CrackWindow: 0.006f, CrackLow: 4000f, CrackHigh: 10000f, CrackDecay: 0.012f, CrackGain: 0.5f),
            (BreakStyle.Crumble, false) => new(0.04f, BodyGain: 0.4f, BodyCutoff: 1500f, BodyDecay: 0.006f, CrackleRate: 3000f, CrackleDecay: 0.012f, CrackleGain: 0.3f),
            (BreakStyle.Crumble, true) => new(0.05f, Cracks: 2, CrackWindow: 0.004f, CrackLow: 1200f, CrackHigh: 3000f, CrackDecay: 0.003f, CrackGain: 0.5f,
                CrackleRate: 4000f, CrackleDecay: 0.01f, CrackleGain: 0.3f),
            (BreakStyle.Crunch, false) => new(0.05f, BodyGain: 0.6f, BodyCutoff: 700f, BodyDecay: 0.015f, CrackleRate: 2000f, CrackleDecay: 0.01f, CrackleGain: 0.2f),
            (BreakStyle.Crunch, true) => new(0.05f, BodyGain: 0.4f, BodyCutoff: 2000f, BodyDecay: 0.006f, Cracks: 2, CrackWindow: 0.003f, CrackLow: 800f, CrackHigh: 2500f, CrackDecay: 0.003f, CrackGain: 0.5f),
            (BreakStyle.Circuit, _) => new(0.04f, Cracks: 1, CrackWindow: 0.001f, CrackLow: 2500f, CrackHigh: 6000f, CrackDecay: 0.004f, CrackGain: 0.4f,
                CrackleRate: 8000f, CrackleDecay: 0.008f, CrackleGain: 0.3f),
            _ => new(0.05f, Cracks: 1, CrackWindow: 0.001f, CrackLow: 2000f, CrackHigh: 3500f, CrackDecay: 0.01f, CrackGain: 0.4f), // 金属
        },
    };
}

/// <summary>床に落ちたものの音の種類。</summary>
public enum LandingKind
{
    /// <summary>皮膚・肉・臓器の欠片。</summary>
    Chunk,

    /// <summary>骨・目の欠片（硬い）。</summary>
    Hard,

    Blood,
    Dust,

    /// <summary>火花（音を出さない）。</summary>
    Silent,
}
