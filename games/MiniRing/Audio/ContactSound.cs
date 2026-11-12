namespace MiniRing.Audio;

/// <summary>
/// ぶつかった 2 つの物の材質の組（どちらが先かは問わない）。音の芯（鳴る帯域）・減衰・当たりの硬さが変わる。
/// </summary>
public enum ContactPair
{
    /// <summary>鉄と鉄（刃と刃）: 明るく細かくきらめく「ギャィン」。部分音の詰まった鳴りが 0.6〜0.9 s 残る。</summary>
    MetalMetal,

    /// <summary>鉄と木（刃と棍棒）: 乾いた「ゴッ」に、木に押さえられてすぐ止まる金属の「ギッ」。</summary>
    MetalWood,

    /// <summary>木と木（棍棒と棍棒）: 乾いた「コンッ」。</summary>
    WoodWood,

    /// <summary>鉄と肉（刃の腹が体に止まる）: こもった「ゴッ」に、肉に吸われる金属のかすかな「チッ」。</summary>
    MetalFlesh,

    /// <summary>木と肉（棍棒が体に止まる）: 低い「ドスッ」に小さなノック。</summary>
    WoodFlesh,

    /// <summary>グローブとグローブ: グローブの打撃の作り（<see cref="StrikeRecipes.Glove"/>）を軽くした「ドッ・バシッ」。</summary>
    GloveGlove,

    /// <summary>グローブが肌に軽く触れる（傷を作らない当たり）: さらに軽いグローブの当たり。</summary>
    GloveFlesh,

    /// <summary>肌と肌: 素手の作り（<see cref="StrikeRecipes.Bare"/>）をごく軽くした「ペチ」。</summary>
    FleshFlesh,
}

/// <summary>
/// 壊れない接触の音（素材ファイルなし・合成）。2 つのボクセルの物が当たって何も削れなかったときの音を、材質の組から作る。
/// <para>組ごとの表（<see cref="Voice"/>）が、次の 3 つの部品をどれだけ使うかを決める（足し合わせる）:</para>
/// <list type="bullet">
/// <item><b>層の和</b>（木の組と、鉄と木・鉄と肉の芯）: 叩かれた物の鳴り（整数比でない部分音の群れ・木なら少なく狭い「コン」）、
/// 質量どうしが押し合う低い「ドスッ」、面がぶつかる短いノイズ、強い当たりだけの高域のクラック。</item>
/// <item><b>打撃の作り</b>（グローブ・肌の組）: 打撃音と同じ <see cref="StrikeRecipes"/> を、壊れない当たりなので軽く（重さを小さく）鳴らす。</item>
/// <item><b>刃の鳴り</b>（鉄の組）: <see cref="AddBladeRing"/>。実物の刃の打ち合いの録音から測った固有振動の表（2〜7 kHz に強い山、
/// 0.7〜14 kHz に弱い山がびっしり）を減衰正弦の和で鳴らす。鉄と鉄は長く、木や肉に押さえられると短く止まる。</item>
/// </list>
/// <para><b>強さと高さの対応（<see cref="HitSound"/> と同じ向き）</b>: 弱い当たりほど<b>高く・細く</b>、強い当たりほど<b>低く・太く</b>。
/// 強く当たるほど大きな質量（物全体・相手の体）が一緒に押されて動き、鳴る山が低くなる、という読み。
/// ドスッは強さ 0→1 で <see cref="ThumpOctaves"/>（1.5 オクターブ）、鳴りと面の当たりの帯域は <see cref="BandOctaves"/>（1 オクターブ）、
/// 刃の鳴りは <see cref="BladeOctaves"/>（0.6 オクターブ）下がる。</para>
/// <para>PCM のピークはすでに <see cref="Loudness"/> にそろえてある（他の合成音と同じ。再生は音量 1 で）。
/// 打撃音（0.35〜0.95）の範囲かその下に置き、鉄と鉄だけが最大 0.78 まで上がる。同じ組・強さ・種なら同じ音（決定的）。</para>
/// </summary>
public static class ContactSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>
    /// 聞こえる下限（PCM のピーク 1 に対する振幅。約 −38 dB）。打撃音と観客の音の下ではこれより小さい尾は聞こえないので、そこで切る。
    /// </summary>
    private const float AudibleFloor = 0.012f;

    /// <summary>どんなに弱い当たりでも鳴らす最小の長さ [s]（立ち上がりと末尾のフェード 12 ms が入る長さ）。</summary>
    private const float MinLength = 0.03f;

    /// <summary>どんな当たりでもこれより長くしない [s]（刃の打ち合いの鳴りが −40 dB まで落ちる 0.9 s 余りまで入る長さ）。</summary>
    private const float MaxLength = 0.98f;

    /// <summary>ドスッが強さ 0→1 で下がる幅 [オクターブ]（200 Hz なら 71 Hz まで）。</summary>
    private const float ThumpOctaves = 1.5f;

    /// <summary>鳴りと面の当たりの帯域が強さ 0→1 で下がる幅 [オクターブ]。材質の聞き分けが崩れない程度に、ドスッより狭くする。</summary>
    private const float BandOctaves = 1f;

    /// <summary>
    /// 刃の鳴りが強さ 1→0 で上がる幅 [オクターブ]。強さ 1 が手本の録音の高さ（いちばん強い山 5.3 kHz）で、軽い当たりは 8 kHz の細い「チン」。
    /// 鉄の明るさを保つため、他の層より狭い。
    /// </summary>
    private const float BladeOctaves = 0.6f;

    /// <summary>弱い当たりのドスッの強さ（強い当たりで 1）。弱いほど体の重さが乗らず細い音になる。</summary>
    private const float ThinThump = 0.55f;

    /// <summary>ドスッの高さが打った瞬間にどれだけ上から落ちてくるか（打撃音と同じ「沈み込み」の割合）と、その速さ [s]。</summary>
    private const float ThumpPitchDrop = 0.8f;

    private const float ThumpPitchDropTime = 0.01f;

    /// <summary>鳴りの部分音の位置の揺らぎ（隣との間隔に対する割合）。0.8 で並びの規則が聞こえない。</summary>
    private const float PartialJitter = 0.8f;

    /// <summary>高い部分音ほど小さく（振幅 ∝ (中心/f)^0.5）・早く減衰する（∝ (中心/f)^0.6）。</summary>
    private const float PartialTilt = 0.5f;

    private const float PartialDecayTilt = 0.6f;

    /// <summary>帯域を絞ったノイズの振幅の持ち上げ（帯域通過で白色雑音の振幅は 1/4 前後に落ちるので、他の層と同じ高さにそろえる）。</summary>
    private const float BandNoiseLevel = 4f;

    /// <summary>ざらつきの帯域の鋭さ（Q）。0.7 で鳴りの帯域（1 オクターブ前後）をまるごと覆う。</summary>
    private const float GritQ = 0.7f;

    /// <summary>クラックの減衰 [s]。</summary>
    private const float CrackDecay = 0.0015f;

    /// <summary>固有振動の高さの個体差（±）。同じ物でも当たる場所・握りで鳴る高さが少し変わる。</summary>
    private const float PitchSpread = 0.04f;

    /// <summary>各層の強さの個体差（±）。</summary>
    private const float GainSpread = 0.15f;

    /// <summary>打った瞬間の立ち上がり [s]（どの組でもクリックの角を落とすだけの短さ）。</summary>
    private const float Attack = 0.0004f;

    // ── 刃の鳴り（手本: 刃と刃の打ち合いの録音。強さ 1 の値）──

    /// <summary>刃の鳴りのいちばん強い山の高さ [Hz]（表の比の基準）。</summary>
    private const float BladeReferenceHz = 5348f;

    /// <summary>
    /// 刃の鳴りの強い山: 基準（5348 Hz）に対する高さの比と、いちばん強い山に対する大きさ [dB]。録音の 20〜400 ms で測った山
    /// （6540 Hz −1 dB、4166 Hz −6 dB、2685 Hz −7 dB、2128 Hz −10 dB …）。細長い刃・鍔・柄が別々に鳴るので整数比にならない。
    /// </summary>
    private static readonly (float Ratio, float Db)[] BladeModes =
    [
        (1f, 0f), (6540f / 5348f, -1f), (4166f / 5348f, -6f), (2685f / 5348f, -7f), (2128f / 5348f, -10f),
        (2577f / 5348f, -13f), (2435f / 5348f, -14f), (2762f / 5348f, -14f), (11501f / 5348f, -18f), (1869f / 5348f, -18f),
        (2993f / 5348f, -18f), (3574f / 5348f, -19f), (2322f / 5348f, -19f), (6108f / 5348f, -20f), (5852f / 5348f, -21f),
        (4792f / 5348f, -21f), (5729f / 5348f, -22f), (5970f / 5348f, -22f), (11758f / 5348f, -22f), (9579f / 5348f, -23f),
        (8040f / 5348f, -23f), (1993f / 5348f, -24f), (7019f / 5348f, -24f), (8226f / 5348f, -25f),
    ];

    /// <summary>強い山のうち、すぐ隣（+1.5〜4 Hz）にもう 1 本の山を持つ数。うなりで鳴りが細かくきらめく（録音の山も 2 本ずつ割れている）。</summary>
    private const int BladeTwins = 8;

    /// <summary>うなりの相方の大きさ（元の山に対して）。0.3: うなりで鳴りが揺れるが、打ち消し合って穴があくほどではない。</summary>
    private const float BladeTwinShare = 0.3f;

    /// <summary>うなりの相方のずれ [Hz]（1.5〜4 Hz: 0.25〜0.7 s ごとにゆっくり揺れる）。</summary>
    private const float BladeTwinMinHz = 1.5f;

    private const float BladeTwinMaxHz = 4f;

    /// <summary>弱い山の大きさの範囲 [dB]（録音の弱い山は −25〜−40 dB）。</summary>
    private const float BladeWeakLoudestDb = 25f;

    private const float BladeWeakQuietestDb = 40f;

    /// <summary>
    /// 刃の縁どうしが擦れる「シャー」: 帯域の中心 [Hz]・大きさ（鳴りに対して）。録音は最初の 300 ms、6.4〜12.8 kHz に 3 割余りの
    /// エネルギーを持ち続ける（測った山だけでは 1 割に届かない）。鳴りと同じ包絡で、減衰は鳴りの 0.8 倍（高い音から先に消える）。
    /// </summary>
    private const float BladeSizzleHz = 7000f;

    private const float BladeSizzleGain = 0.2f;

    private const float BladeSizzleDecayShare = 0.8f;

    /// <summary>
    /// 録音の 0.7〜14 kHz にびっしり並ぶ弱い山（−25〜−40 dB）の数と範囲 [Hz]。これが無いと数本の線の「風鈴」に聞こえる。
    /// </summary>
    private const int BladeWeakPartials = 60;

    private const float BladeWeakLowHz = 700f;

    private const float BladeWeakHighHz = 14000f;

    /// <summary>刃の鳴りの減衰を、高い山ほど速くする指数（∝ (基準/f)^0.35）。録音の重心が 7 kHz → 5 kHz → 4.2 kHz と下がっていく分。</summary>
    private const float BladeDecayTilt = 0.25f;

    /// <summary>刃の打ち合いの頭の「チャッ」: 高域通過の下端 [Hz]（録音の 0〜20 ms は 3.2 kHz より上が 96 %）と減衰 [s]。</summary>
    private const float BladeHeadHz = 3200f;

    private const float BladeHeadDecay = 0.008f;

    /// <summary>刃の鳴りの部分音のうち、これより高いものは鳴らさない [Hz]（ナイキストの手前。エイリアスで濁る）。</summary>
    private const float HighestPartial = 18000f;

    /// <summary>
    /// 鳴り（クランク）: 帯域の中心 [Hz]（弱い当たりのとき）・帯域の幅 [オクターブ]・部分音の数・減衰 [s]・強さ、
    /// ざらつき（Grit: 同じ帯域・同じ減衰のノイズ。縁どうしが擦れる「ジャリ」の強さ）。
    /// </summary>
    private readonly record struct Clank(float CenterHz, float Octaves, int Partials, float Decay, float Gain, float Grit)
    {
        public static readonly Clank None = new(0f, 0f, 0, 0f, 0f, 0f);
    }

    /// <summary>
    /// 刃の鳴りの使い方: 大きさ・強さ 0 と 1 で鳴りがいちばん大きいまま保つ時間 [s]・その後の減衰 [s]・頭の「チャッ」の大きさ。
    /// 鉄と鉄は手本の録音に合わせて強さ 1 で 360 ms 保ち 130 ms で減衰（録音 −6 / −20 / −40 dB: 400 / 640 / 920 ms）。木や肉に押さえられると保たず数十 ms で止まる。
    /// </summary>
    private readonly record struct Blade(float Gain, float LightHold, float HeavyHold, float Decay, float HeadGain);

    /// <summary>打撃の作りのどれを使うか（グローブ・肌の組）。</summary>
    private enum StrikeKind
    {
        None,
        Glove,
        Bare,
    }

    /// <summary>1 つの部分音: 高さ [Hz]・強さ・減衰の時定数 [s]・始まりの位相。</summary>
    private readonly record struct Partial(float Hz, float Amplitude, float Decay, float Phase);

    /// <summary>
    /// 1 つの材質の組の音色（層の周波数はどれも強さ 0 の値。強いほど下がる）。
    /// </summary>
    /// <param name="Clank">鳴り（無ければ <see cref="Clank.None"/>）。</param>
    /// <param name="ThumpHz">ドスッの高さ [Hz]。</param>
    /// <param name="ThumpDecay">ドスッの減衰 [s]。</param>
    /// <param name="ThumpGain">ドスッの強さ（0 なら鳴らさない）。</param>
    /// <param name="NoiseHz">面の当たりのノイズの帯域の中心 [Hz]。</param>
    /// <param name="NoiseQ">同じく帯域の鋭さ（大きいほど狭い）。</param>
    /// <param name="NoiseDecay">ノイズの減衰 [s]。</param>
    /// <param name="NoiseGain">ノイズの強さ（0 なら鳴らさない）。</param>
    /// <param name="CrackHz">クラックの高域の下端 [Hz]。</param>
    /// <param name="CrackGain">強さ 1 のクラックの強さ。</param>
    /// <param name="Drive">サチュレーションの深さ（<see cref="HitSound.Saturate"/>）。</param>
    /// <param name="Room">部屋の響きの量（<see cref="HitSound.AddRoom"/>）。</param>
    /// <param name="QuietLoudness">強さ 0 の音量（PCM のピーク）。</param>
    /// <param name="FullLoudness">強さ 1 の音量。</param>
    /// <param name="Strike">打撃の作り（グローブ・肌の組）。</param>
    /// <param name="StrikeWeight">強さ 1 のときの打撃の重さ（打撃音の重さ 0..1 の目盛り。壊れない当たりなので 1 より小さい）。</param>
    /// <param name="Blade">刃の鳴り（大きさ 0 なら鳴らさない）。</param>
    private sealed record Voice(
        Clank Clank,
        float ThumpHz,
        float ThumpDecay,
        float ThumpGain,
        float NoiseHz,
        float NoiseQ,
        float NoiseDecay,
        float NoiseGain,
        float CrackHz,
        float CrackGain,
        float Drive,
        float Room,
        float QuietLoudness,
        float FullLoudness,
        StrikeKind Strike = StrikeKind.None,
        float StrikeWeight = 0f,
        Blade Blade = default)
    {
        public bool HasLayers => Clank.Partials > 0 || ThumpGain > 0f || NoiseGain > 0f || CrackGain > 0f;
    }

    /// <summary>
    /// 組ごとの音色の表。数値の根拠:
    /// <list type="bullet">
    /// <item>鉄と鉄: 刃の鳴りだけ（手本の録音の形。強さ 1 で録音の高さ・長さ）。サチュレーションは浅く（1.2）、きらめきを濁さない。</item>
    /// <item>鉄と木: 木の棒の曲げの帯域 700 Hz の「コン」と 180 Hz の押しに、木に押さえられて 30 ms で止まる刃の鳴り。</item>
    /// <item>木と木: 太さ 1.5 cm 程度の木の棒の曲げの帯域 700 Hz のまわり 0.6 オクターブに 6 本・45 ms（乾いた「コン」）。</item>
    /// <item>鉄と肉: 肉が刃の腹を受け止める。170 Hz の低い押し 45 ms が主で、刃の鳴りは肉に吸われて 20 ms で止まる。</item>
    /// <item>木と肉: 棍棒の打撲の低い押し 170 Hz・55 ms に、棍棒自身の小さなノック。</item>
    /// <item>グローブとグローブ: グローブの打撃の作りを重さ 0.6 まで（傷を作らない当たり）。グローブと肌は 0.4 まで。</item>
    /// <item>肌と肌: 素手の打撃の作りを重さ 0.3 まで。いちばん小さい。</item>
    /// </list>
    /// 音量: 打撃音 0.35〜0.95 の中かその下。鉄と鉄だけが 0.78 まで上がる（刃の打ち合いは試合の中でいちばん目立ってよい音）。
    /// </summary>
    private static readonly Voice[] Voices = BuildVoices();

    /// <summary>1 回の接触の音（16-bit モノラル）。<paramref name="strength01"/> は当たりの強さ 0..1、<paramref name="seed"/> は 1 回ごとの揺らぎ。</summary>
    public static byte[] Synthesize(ContactPair pair, float strength01, int seed)
    {
        var voice = VoiceOf(pair);
        var strength = Clamp01(strength01);
        var loudness = Loudness(pair, strength);
        var random = new Random(seed);
        var pitch = 1f + (PitchSpread * Spread(random));

        var layers = voice.HasLayers ? Layers(voice, strength, pitch, loudness, random) : [];
        var strike = voice.Strike switch
        {
            StrikeKind.Glove => StrikeRecipes.Glove(new Strike(voice.StrikeWeight * strength, 0f, false, pitch), random),
            StrikeKind.Bare => StrikeRecipes.Bare(new Strike(voice.StrikeWeight * strength, 0f, false, pitch), random),
            _ => [],
        };

        var bladeHold = Lerp(voice.Blade.LightHold, voice.Blade.HeavyHold, strength);
        var bladeLength = voice.Blade.Gain > 0f ? BladeLength(bladeHold, voice.Blade.Decay, loudness) : 0;
        var samples = new float[Math.Max(Math.Max(layers.Length, strike.Length), Math.Max(bladeLength, (int)(MinLength * SampleRate)))];
        for (var n = 0; n < layers.Length; n++)
        {
            samples[n] += layers[n];
        }

        for (var n = 0; n < strike.Length; n++)
        {
            samples[n] += strike[n];
        }

        if (voice.Blade.Gain > 0f)
        {
            var bladePitch = pitch * MathF.Pow(2f, BladeOctaves * (1f - strength));
            AddBladeRing(samples, bladePitch, bladeHold, voice.Blade.Decay, voice.Blade.Gain, voice.Blade.HeadGain, random);
        }

        HitSound.Saturate(samples, voice.Drive);
        HitSound.AddRoom(samples, voice.Room);
        return HitSound.ToPcm16(samples, loudness);
    }

    /// <summary>この組・強さの音量（PCM のピーク 0..1。<see cref="Synthesize"/> の音はすでにこの大きさ）。強いほど大きい。</summary>
    public static float Loudness(ContactPair pair, float strength01)
    {
        var voice = VoiceOf(pair);
        return Lerp(voice.QuietLoudness, voice.FullLoudness, Clamp01(strength01));
    }

    /// <summary>
    /// 刃の鳴りを <paramref name="samples"/> に足す（長さは <paramref name="samples"/> のまま）。部分音は位相の回転（複素数の掛け算）で進める。
    /// </summary>
    /// <param name="samples">足し込む先。</param>
    /// <param name="pitchScale">高さの倍率（1 = 手本の録音の高さ）。</param>
    /// <param name="hold">いちばん大きいまま保つ時間 [s]。</param>
    /// <param name="decay">その後の減衰 [s]（基準の山の値。高い山ほど速い）。</param>
    /// <param name="gain">鳴りの大きさ（部分音の振幅の 2 乗和の平方根が 1 のとき、この値）。</param>
    /// <param name="headGain">頭の「チャッ」の大きさ。</param>
    /// <param name="random">揺らぎ（部分音の位置・位相・減衰）。</param>
    internal static void AddBladeRing(float[] samples, float pitchScale, float hold, float decay, float gain, float headGain, Random random)
    {
        var partials = BladePartials(pitchScale, decay, random);
        var norm = gain / MathF.Sqrt(MathF.Max(partials.Sum(p => p.Amplitude * p.Amplitude), 1e-12f));
        foreach (var partial in partials)
        {
            var step = 2.0 * Math.PI * partial.Hz / SampleRate;
            var (stepSin, stepCos) = Math.SinCos(step);
            var (im, re) = Math.SinCos(partial.Phase);
            var amplitude = partial.Amplitude * norm;
            var holdSamples = (int)(hold * SampleRate);
            var fall = MathF.Exp(-1f / (partial.Decay * SampleRate)); // 1 サンプルごとの減衰（e^(−t/τ) を掛け算で進める。部分音が多いので exp を毎回呼ばない）
            var level = 1f;
            for (var n = 0; n < samples.Length; n++)
            {
                if (n > holdSamples)
                {
                    level *= fall;
                }

                samples[n] += (float)im * amplitude * level * MathF.Min(1f, n / (Attack * SampleRate));
                var nextRe = (re * stepCos) - (im * stepSin);
                im = (re * stepSin) + (im * stepCos);
                re = nextRe;
            }
        }

        var head = Biquad.HighPass(MathF.Min(BladeHeadHz * pitchScale, HighestPartial));
        var sizzle = Biquad.BandPass(MathF.Min(BladeSizzleHz * pitchScale, HighestPartial), GritQ);
        for (var n = 0; n < samples.Length; n++)
        {
            var t = n / (float)SampleRate;
            var white = (random.NextSingle() * 2f) - 1f;
            samples[n] += head.Process(white) * BandNoiseLevel * headGain * MathF.Exp(-t / BladeHeadDecay);
            samples[n] += sizzle.Process(white) * BandNoiseLevel * gain * BladeSizzleGain
                * MathF.Exp(-MathF.Max(0f, t - hold) / (decay * BladeSizzleDecayShare)) * MathF.Min(1f, t / Attack);
        }
    }

    /// <summary>刃の鳴りの部分音: 測った強い山（うち <see cref="BladeTwins"/> 本はうなりの相方つき）と、帯域にびっしり並ぶ弱い山。</summary>
    private static Partial[] BladePartials(float pitchScale, float decay, Random random)
    {
        var list = new List<Partial>();
        void Add(float hz, float amplitude)
        {
            if (hz < HighestPartial)
            {
                var tilt = MathF.Pow(BladeReferenceHz / hz, BladeDecayTilt);
                list.Add(new Partial(hz, amplitude, decay * tilt * (0.8f + (0.4f * random.NextSingle())), random.NextSingle() * 2f * MathF.PI));
            }
        }

        for (var i = 0; i < BladeModes.Length; i++)
        {
            var hz = BladeReferenceHz * BladeModes[i].Ratio * pitchScale;
            var amplitude = MathF.Pow(10f, BladeModes[i].Db / 20f);
            Add(hz, amplitude);
            if (i < BladeTwins)
            {
                Add(hz + BladeTwinMinHz + ((BladeTwinMaxHz - BladeTwinMinHz) * random.NextSingle()), amplitude * BladeTwinShare);
            }
        }

        for (var i = 0; i < BladeWeakPartials; i++)
        {
            var hz = BladeWeakLowHz * MathF.Pow(BladeWeakHighHz / BladeWeakLowHz, random.NextSingle()) * pitchScale;
            Add(hz, MathF.Pow(10f, -(BladeWeakLoudestDb + ((BladeWeakQuietestDb - BladeWeakLoudestDb) * random.NextSingle())) / 20f));
        }

        return [.. list];
    }

    /// <summary>刃の鳴りが聞こえなくなるまでの長さ（いちばん遅い山 = 低い山の減衰で測る）[サンプル]。</summary>
    private static int BladeLength(float hold, float decay, float loudness)
    {
        var slowest = decay * MathF.Pow(BladeReferenceHz / BladeWeakLowHz, BladeDecayTilt) * 1.2f;
        return (int)(SampleRate * Math.Clamp(hold + (slowest * MathF.Log(loudness / AudibleFloor)), MinLength, MaxLength));
    }

    /// <summary>層の和（鳴り・ドスッ・面の当たり・クラック）。長さはいちばん遅い層が聞こえなくなるまで。</summary>
    private static float[] Layers(Voice voice, float strength, float pitch, float loudness, Random random)
    {
        var thumpScale = pitch * MathF.Pow(2f, -ThumpOctaves * strength);
        var bandScale = pitch * MathF.Pow(2f, -BandOctaves * strength);
        var partials = ClankPartials(voice.Clank, bandScale, random);
        var thumpGain = voice.ThumpGain * Lerp(ThinThump, 1f, strength) * (1f + (GainSpread * Spread(random)));
        var noiseGain = voice.NoiseGain * (1f + (GainSpread * Spread(random)));
        var crackGain = voice.CrackGain * strength * strength;

        var slowest = MathF.Max(voice.ThumpGain > 0f ? voice.ThumpDecay : 0f, voice.NoiseDecay);
        foreach (var partial in partials)
        {
            slowest = MathF.Max(slowest, partial.Decay);
        }

        var samples = new float[(int)(SampleRate * Math.Clamp(slowest * MathF.Log(loudness / AudibleFloor), MinLength, MaxLength))];
        var noiseFilter = Biquad.BandPass(MathF.Max(voice.NoiseHz, 1f) * bandScale, MathF.Max(voice.NoiseQ, 0.1f));
        var gritFilter = Biquad.BandPass(MathF.Max(voice.Clank.CenterHz, 1f) * bandScale, GritQ);
        var gritDecay = MathF.Max(voice.Clank.Decay, 1e-4f);
        var noiseDecay = MathF.Max(voice.NoiseDecay, 1e-4f);
        var crackFilter = Biquad.HighPass(MathF.Max(voice.CrackHz, 1f));
        var thumpHz = voice.ThumpHz * thumpScale;
        var thumpPhase = 0f;
        var partialPhases = partials.Select(partial => partial.Phase).ToArray();
        for (var n = 0; n < samples.Length; n++)
        {
            var t = n / (float)SampleRate;
            var onset = MathF.Min(1f, t / Attack);

            var ring = 0f;
            for (var m = 0; m < partials.Length; m++)
            {
                partialPhases[m] += 2f * MathF.PI * partials[m].Hz / SampleRate;
                ring += partials[m].Amplitude * MathF.Exp(-t / partials[m].Decay) * MathF.Sin(partialPhases[m]);
            }

            var white = (random.NextSingle() * 2f) - 1f;
            ring += gritFilter.Process(white) * BandNoiseLevel * voice.Clank.Grit * MathF.Exp(-t / gritDecay);
            ring *= voice.Clank.Gain;

            var sinking = 1f + (ThumpPitchDrop * MathF.Exp(-t / ThumpPitchDropTime));
            thumpPhase += 2f * MathF.PI * thumpHz * sinking / SampleRate;
            var thump = MathF.Sin(thumpPhase) * MathF.Exp(-t / MathF.Max(voice.ThumpDecay, 1e-4f)) * thumpGain;

            var noise = noiseFilter.Process(white) * BandNoiseLevel * MathF.Exp(-t / noiseDecay) * noiseGain;
            var crack = crackFilter.Process(white) * MathF.Exp(-t / CrackDecay) * crackGain;

            samples[n] = onset * (ring + thump + noise + crack);
        }

        return samples;
    }

    /// <summary>
    /// 鳴りの部分音: 帯域（中心のまわり ±Octaves/2）にほぼ等間隔（対数で）に並べ、位置を種で揺らす。高いものほど小さく早く消える。
    /// </summary>
    private static Partial[] ClankPartials(Clank clank, float scale, Random random)
    {
        var center = clank.CenterHz * scale;
        var partials = new Partial[clank.Partials];
        for (var i = 0; i < partials.Length; i++)
        {
            var slot = (i + 0.5f + (PartialJitter * (random.NextSingle() - 0.5f))) / partials.Length;
            var hz = center * MathF.Pow(2f, clank.Octaves * (slot - 0.5f));
            var lowness = center / hz;
            partials[i] = new Partial(
                Hz: hz,
                Amplitude: (0.5f + (0.5f * random.NextSingle())) * MathF.Pow(lowness, PartialTilt),
                Decay: clank.Decay * MathF.Pow(lowness, PartialDecayTilt) * (0.7f + (0.6f * random.NextSingle())),
                Phase: random.NextSingle() * 2f * MathF.PI);
        }

        return partials;
    }

    private static Voice VoiceOf(ContactPair pair)
    {
        var index = (int)pair;
        if (index < 0 || index >= Voices.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pair), pair, "未知の材質の組");
        }

        return Voices[index];
    }

    private static Voice[] BuildVoices()
    {
        var voices = new Voice[Enum.GetValues<ContactPair>().Length];
        voices[(int)ContactPair.MetalMetal] = new Voice(
            Clank.None,
            ThumpHz: 0f, ThumpDecay: 0f, ThumpGain: 0f,
            NoiseHz: 0f, NoiseQ: 0f, NoiseDecay: 0f, NoiseGain: 0f,
            CrackHz: 0f, CrackGain: 0f,
            Drive: 1.2f, Room: 0.05f, QuietLoudness: 0.30f, FullLoudness: 0.78f,
            Blade: new Blade(Gain: 1f, LightHold: 0.06f, HeavyHold: 0.36f, Decay: 0.13f, HeadGain: 0.9f));
        voices[(int)ContactPair.MetalWood] = new Voice(
            new Clank(CenterHz: 700f, Octaves: 0.6f, Partials: 6, Decay: 0.035f, Gain: 0.8f, Grit: 0.1f),
            ThumpHz: 180f, ThumpDecay: 0.025f, ThumpGain: 0.6f,
            NoiseHz: 2200f, NoiseQ: 0.8f, NoiseDecay: 0.003f, NoiseGain: 0.5f,
            CrackHz: 3500f, CrackGain: 0.5f,
            Drive: 1.6f, Room: 0.06f, QuietLoudness: 0.25f, FullLoudness: 0.62f,
            Blade: new Blade(Gain: 0.45f, LightHold: 0f, HeavyHold: 0f, Decay: 0.03f, HeadGain: 0.3f));
        voices[(int)ContactPair.WoodWood] = new Voice(
            new Clank(CenterHz: 700f, Octaves: 0.6f, Partials: 6, Decay: 0.045f, Gain: 1f, Grit: 0.1f),
            ThumpHz: 220f, ThumpDecay: 0.015f, ThumpGain: 0.4f,
            NoiseHz: 2500f, NoiseQ: 1f, NoiseDecay: 0.002f, NoiseGain: 0.5f,
            CrackHz: 3500f, CrackGain: 0.35f,
            Drive: 1.6f, Room: 0.08f, QuietLoudness: 0.22f, FullLoudness: 0.58f);
        voices[(int)ContactPair.MetalFlesh] = new Voice(
            Clank.None,
            ThumpHz: 170f, ThumpDecay: 0.045f, ThumpGain: 1f,
            NoiseHz: 1200f, NoiseQ: 0.9f, NoiseDecay: 0.005f, NoiseGain: 0.45f,
            CrackHz: 3000f, CrackGain: 0.3f,
            Drive: 1.6f, Room: 0.06f, QuietLoudness: 0.2f, FullLoudness: 0.5f,
            Blade: new Blade(Gain: 0.25f, LightHold: 0f, HeavyHold: 0f, Decay: 0.02f, HeadGain: 0.2f));
        voices[(int)ContactPair.WoodFlesh] = new Voice(
            new Clank(CenterHz: 650f, Octaves: 0.4f, Partials: 4, Decay: 0.025f, Gain: 0.3f, Grit: 0.1f),
            ThumpHz: 170f, ThumpDecay: 0.055f, ThumpGain: 1f,
            NoiseHz: 1100f, NoiseQ: 0.9f, NoiseDecay: 0.005f, NoiseGain: 0.4f,
            CrackHz: 3000f, CrackGain: 0.3f,
            Drive: 1.7f, Room: 0.08f, QuietLoudness: 0.25f, FullLoudness: 0.6f);
        voices[(int)ContactPair.GloveGlove] = Padded(StrikeKind.Glove, weight: 0.6f, quiet: 0.18f, full: 0.45f);
        voices[(int)ContactPair.GloveFlesh] = Padded(StrikeKind.Glove, weight: 0.4f, quiet: 0.12f, full: 0.32f);
        voices[(int)ContactPair.FleshFlesh] = Padded(StrikeKind.Bare, weight: 0.3f, quiet: 0.06f, full: 0.2f);
        return voices;
    }

    /// <summary>打撃の作りだけで鳴る組（グローブ・肌）。層は使わない。サチュレーションと響きは打撃音の軽い当たりに合わせる。</summary>
    private static Voice Padded(StrikeKind kind, float weight, float quiet, float full) => new(
        Clank.None,
        ThumpHz: 0f, ThumpDecay: 0f, ThumpGain: 0f,
        NoiseHz: 0f, NoiseQ: 0f, NoiseDecay: 0f, NoiseGain: 0f,
        CrackHz: 0f, CrackGain: 0f,
        Drive: 1.5f, Room: 0.04f, QuietLoudness: quiet, FullLoudness: full,
        Strike: kind, StrikeWeight: weight);

    /// <summary>−1..1 の一様な揺らぎ。</summary>
    private static float Spread(Random random) => (random.NextSingle() * 2f) - 1f;

    private static float Clamp01(float value) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
