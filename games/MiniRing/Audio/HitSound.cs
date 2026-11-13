using MiniRing.Voxels;

namespace MiniRing.Audio;

/// <summary>何に当たったか（音の芯の高さと質感が変わる）。</summary>
public enum HitTarget
{
    Head,
    Body,
    Guard,
}

/// <summary>打った面の材質（打撃音の作り方が変わる）。</summary>
public enum HitSurface
{
    /// <summary>グローブ（表の革・中の詰め物）: 「ドッ・バシッ」に、遅れて沈む低い「ドォン」。</summary>
    Glove,

    /// <summary>素手（皮膚）: 鋭い「バキッ」に、下がっていく低い「ゴッ」。</summary>
    Bare,

    /// <summary>鉄（ナックル・籠手）: 素手の作りに、短い金属の「ギッ」。</summary>
    Metal,
    /// <summary>木の打面。芯は乾いた肉の衝撃、木の響きは接触層で重ねる。</summary>
    Wood,
}

/// <summary>
/// 一撃の音色。<paramref name="Intensity"/> はダメージの大きさ 0..1、<paramref name="Clean"/> は芯を食った度合い 0..1
/// （体重が乗っているほど・実際のダメージが大きいほど高く、ガードの上からは低い）。
/// </summary>
/// <param name="Intensity">ダメージの大きさ 0..1。低く太く・長く・大きくなる。</param>
/// <param name="Clean">芯を食った度合い 0..1。鋭い「パキッ」が乗る。</param>
/// <param name="Target">当たった場所の種類。</param>
/// <param name="Surface">打った面の材質。</param>
public readonly record struct HitTone(float Intensity, float Clean, HitTarget Target, HitSurface Surface = HitSurface.Glove)
{
    /// <summary>この程度のダメージで最も重い音になる。</summary>
    public const float FullDamage = 28f;

    public static HitTone Of(in HitEvent hit)
    {
        var target = hit.Zone switch
        {
            HitZone.Guard or HitZone.Shoulder or HitZone.Elbow => HitTarget.Guard,
            HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck => HitTarget.Head,
            _ => HitTarget.Body,
        };
        var intensity = Math.Clamp(hit.Damage / FullDamage, 0f, 1f);
        // 部位名で「急所」の音を上乗せせず、実際に伝わったダメージと連結度で連続に決める。
        var clean = target == HitTarget.Guard ? 0.1f * hit.BodyShare : 0.35f + (0.5f * hit.BodyShare) + (0.25f * intensity);
        return new HitTone(intensity, Math.Clamp(clean, 0f, 1f), target, SurfaceOf(hit.Face));
    }

    /// <summary>打った面の材質の番号（<see cref="BoxerAnatomy"/>）から音の作り方を選ぶ。分からない（0）・その他はグローブ。</summary>
    public static HitSurface SurfaceOf(byte face) => face switch
    {
        BoxerAnatomy.Skin => HitSurface.Bare,
        BoxerAnatomy.Iron => HitSurface.Metal,
        BoxerAnatomy.Wood => HitSurface.Wood,
        _ => HitSurface.Glove,
    };
}

/// <summary>
/// 打撃音の合成（素材ファイルなし・決定的）。打った面の材質ごとの作り（<see cref="StrikeRecipes"/>）を、強さ・芯・当たった場所で鳴らす。
/// <para><b>強さと高さの対応（このゲームの約束。<see cref="ContactSound"/> も同じ向き）</b>:
/// 軽い・腕だけの当たりほど<b>高く・細く・短く</b>、体重の乗った重い当たりほど<b>低く・太く・長い</b>（低い「ドォン／ゴッ」が深く長く残る）。
/// 重い当たりほど大きな質量（相手の体ごと）が一緒に押されて動くので、鳴る山が低くなる、という読み。
/// 強さ 1 の音が、手本にした録音（グローブ = 軽いキック、素手 = 重いパンチ）の手触りに合わせてある。</para>
/// </summary>
public static class HitSound
{
    public const int SampleRate = 44100;

    /// <summary>頭はボディより硬く、後ろの質量が小さいので少し高い（×1.15）。ガードは腕で受けて体まで届かないのでさらに高く細い（×1.35）。</summary>
    private const float HeadPitch = 1.15f;

    private const float GuardPitch = 1.35f;

    /// <summary>
    /// 柔らかいサチュレーションの深さ（tanh の駆動）: 軽い 1.3 → 重い 2.2。低い山に倍音を足し、小さなスピーカーでも重さが聞こえるようにする。
    /// その後 <see cref="ToPcm16"/> がもう一度軽くクリップする。
    /// </summary>
    private const float LightDrive = 1.3f;

    private const float HeavyDrive = 2.2f;

    /// <summary>部屋の響きの量。手本の録音はほぼ乾いているので、ごく少し（ガードはさらに少し）。</summary>
    private const float Room = 0.05f;

    private const float GuardRoom = 0.03f;

    /// <summary>16-bit モノラル PCM（リトルエンディアン）。</summary>
    public static byte[] Synthesize(HitTone tone, int seed, bool immediate = false)
    {
        var intensity = Math.Clamp(tone.Intensity, 0f, 1f);
        var clean = Math.Clamp(tone.Clean, 0f, 1f);
        var guard = tone.Target == HitTarget.Guard;
        var random = new Random(seed);
        var pitch = tone.Target switch { HitTarget.Head => HeadPitch, HitTarget.Guard => GuardPitch, _ => 1f };
        var strike = new Strike(intensity, guard ? 0f : clean, guard, pitch);

        var samples = tone.Surface switch
        {
            HitSurface.Bare => StrikeRecipes.Bare(strike, random),
            HitSurface.Metal => StrikeRecipes.Metal(strike, random),
            HitSurface.Wood => StrikeRecipes.Bare(strike, random),
            _ => StrikeRecipes.Glove(strike, random, immediate),
        };

        Saturate(samples, Lerp(LightDrive, HeavyDrive, intensity));
        AddRoom(samples, guard ? GuardRoom : Room);
        return ToPcm16(samples, Lerp(0.35f, 0.95f, intensity) * (guard ? 0.75f : 1f));
    }

    /// <summary>小さな部屋の響き（2 本のフィードバック・コム）。打撃が乾きすぎず、耳に心地よく残る。</summary>
    internal static void AddRoom(float[] samples, float amount)
    {
        var d1 = (int)(0.029f * SampleRate);
        var d2 = (int)(0.043f * SampleRate);
        for (var n = 0; n < samples.Length; n++)
        {
            var echo = (n >= d1 ? samples[n - d1] : 0f) * amount;
            echo += (n >= d2 ? samples[n - d2] : 0f) * amount * 0.7f;
            samples[n] += echo;
        }
    }

    /// <summary>
    /// 柔らかいサチュレーション: ピークを 1 にそろえてから tanh(drive·x)。大きな山ほど潰れて倍音が増え、音が詰まって太く聞こえる
    /// （<paramref name="drive"/> が 1 付近ならほぼ素直）。音量は後の <see cref="ToPcm16"/> がそろえる。
    /// </summary>
    internal static void Saturate(float[] samples, float drive)
    {
        var peak = 1e-6f;
        foreach (var s in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(s));
        }

        for (var n = 0; n < samples.Length; n++)
        {
            samples[n] = MathF.Tanh(drive * samples[n] / peak);
        }
    }

    /// <summary>ピークを <paramref name="loudness"/> に揃え、柔らかくクリップし、末尾をフェードして 16-bit にする。</summary>
    internal static byte[] ToPcm16(float[] samples, float loudness)
    {
        var peak = 1e-6f;
        foreach (var s in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(s));
        }

        const float drive = 1.4f;
        var norm = MathF.Tanh(drive);
        var fade = (int)(0.012f * SampleRate);
        var pcm = new byte[samples.Length * 2];
        for (var n = 0; n < samples.Length; n++)
        {
            var y = MathF.Tanh(drive * samples[n] / peak) / norm * loudness;
            y *= MathF.Min(1f, (samples.Length - n) / (float)fade);
            var v = (short)Math.Clamp(MathF.Round(y * short.MaxValue), short.MinValue, short.MaxValue);
            pcm[2 * n] = (byte)(v & 0xFF);
            pcm[(2 * n) + 1] = (byte)((v >> 8) & 0xFF);
        }

        return pcm;
    }

    /// <summary>対数の補間（高さは耳に割合で効くので、強さに対して一定の割合で動かす）。</summary>
    internal static float LogLerp(float a, float b, float t) => a * MathF.Pow(b / a, Math.Clamp(t, 0f, 1f));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}

/// <summary>
/// 2 次の IIR フィルタ（RBJ の式）。一次のフィルタを重ねるより帯域の肩が立ち、ノイズを「革」「金属」の芯の帯域だけに絞れる。
/// 状態を持つので 1 つの音の合成の中だけで使う。
/// </summary>
internal struct Biquad
{
    private readonly float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    private Biquad(float b0, float b1, float b2, float a0, float a1, float a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
        _x1 = _x2 = _y1 = _y2 = 0f;
    }

    /// <summary>帯域通過（中心で 0 dB）。<paramref name="q"/> が大きいほど狭い。</summary>
    public static Biquad BandPass(float centerHz, float q)
    {
        var (sin, cos) = MathF.SinCos(2f * MathF.PI * Nyquistless(centerHz) / HitSound.SampleRate);
        var alpha = sin / (2f * q);
        return new Biquad(alpha, 0f, -alpha, 1f + alpha, -2f * cos, 1f - alpha);
    }

    /// <summary>2 次の高域通過（Q = 1/√2: 肩が平ら）。</summary>
    public static Biquad HighPass(float cutoffHz)
    {
        var (sin, cos) = MathF.SinCos(2f * MathF.PI * Nyquistless(cutoffHz) / HitSound.SampleRate);
        var alpha = sin / (2f * MathF.Sqrt(0.5f));
        return new Biquad((1f + cos) / 2f, -(1f + cos), (1f + cos) / 2f, 1f + alpha, -2f * cos, 1f - alpha);
    }

    public float Process(float x)
    {
        var y = (_b0 * x) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    /// <summary>ナイキストの手前（0.45 倍）までに抑える（それより上では式が不安定になる）。</summary>
    private static float Nyquistless(float hz) => Math.Clamp(hz, 10f, 0.45f * HitSound.SampleRate);
}

/// <summary>
/// 一撃の鳴らし方の入力。
/// </summary>
/// <param name="Weight">重さ 0..1（1 = 手本の録音の重さ。低い層の高さ・長さ・大きさが決まる）。</param>
/// <param name="Clean">芯を食った度合い 0..1（鋭い「パキッ」の大きさ）。</param>
/// <param name="Muffled">ガードの上・腕で受けた（高い音がこもり、低い沈みが小さい）。</param>
/// <param name="Pitch">当たった場所による高さの倍率（頭・ガードは少し高い）。</param>
internal readonly record struct Strike(float Weight, float Clean, bool Muffled, float Pitch);

/// <summary>
/// 打った面の材質ごとの打撃の作り。手本の録音を測った形（帯域の割合・低い音の高さの動き・減衰）を、層の和で作り直したもの
/// （録音は使わない。種で決まるノイズと減衰正弦だけ）。<see cref="HitSound"/> と <see cref="ContactSound"/>（グローブ・肌の当たり）が使う。
/// <para>強さの効き方はどの作りも同じ: 低い層（ドッ・ドォン・ゴッ）は重さ 1→0 で <see cref="LowOctaves"/> 上がり、
/// 中高域の帯（バシッ・バキッ）は <see cref="BandOctaves"/> 上がる。低い沈みの長さと大きさも重さで縮む。</para>
/// </summary>
internal static class StrikeRecipes
{
    private const int SampleRate = HitSound.SampleRate;

    /// <summary>低い層が重さ 1→0 で上がる幅 [オクターブ]（1.5: 手本の 80 Hz 前後の沈みが、軽い当たりでは 230 Hz の短い「ドッ」になる）。</summary>
    private const float LowOctaves = 1.5f;

    /// <summary>中高域の帯が重さ 1→0 で上がる幅 [オクターブ]。材質の聞き分けが崩れない程度に、低い層より狭い。</summary>
    private const float BandOctaves = 1f;

    /// <summary>ガードの上: 帯域を下げる倍率（腕の肉と袖で高い音がこもる）、中高域の大きさ、低い沈みの大きさ。</summary>
    private const float MuffledBand = 0.6f;

    private const float MuffledBandGain = 0.6f;

    private const float MuffledLowGain = 0.5f;

    /// <summary>帯域を絞ったノイズの振幅の持ち上げ（帯域通過で白色雑音の振幅は 1/4 前後に落ちるので、他の層と同じ高さにそろえる）。</summary>
    private const float BandNoiseLevel = 4f;

    /// <summary>芯を食った「パキッ」: 高域通過の下端 [Hz]（軽い 7 kHz → 重い 4 kHz。どの重さでも「バシッ」「バキッ」より上に乗る）・減衰 [s]・振幅の持ち上げ。</summary>
    private const float LightCrackHz = 7000f;

    private const float HeavyCrackHz = 4000f;

    private const float CrackDecay = 0.003f;

    private const float CrackLevel = 6f;

    /// <summary>「パキッ」が鳴り始める芯の度合い。これより下は普通の当たりで、上に行くほど強く乗る。</summary>
    private const float CrackFromClean = 0.5f;

    /// <summary>ノイズの層（バシッ・バキッ）の立ち上がり [s]。0.5 ms: クリックの角だけ落とし、頭の鋭さは残す。</summary>
    private const float NoiseAttack = 0.0005f;

    /// <summary>グローブの「ドッ」の立ち上がり [s] と、打った瞬間の高さの上乗せ（1.3 倍から）・その戻る速さ [s]（詰め物が押されて張りが抜ける）。</summary>
    private const float WhumpAttack = 0.001f;

    private const float WhumpPitchDrop = 0.3f;

    private const float WhumpPitchDropTime = 0.01f;

    /// <summary>「バシッ」の低い帯・中域の帯の鋭さ（Q）。どちらも 1 オクターブ余りの幅で、革の面の広い当たり。</summary>
    private const float SmackLowQ = 0.8f;

    private const float SmackMidQ = 0.6f;

    /// <summary>素手の「バキッ」の帯の鋭さ（Q）と、濁りの帯の鋭さ（0.5: 0.8〜3.2 kHz をまるごと覆う）。</summary>
    private const float BareCrackQ = 0.7f;

    private const float BareMushQ = 0.5f;

    /// <summary>長さの上限 [s]（いちばん重い当たりの沈みが聞こえなくなるまで。手本の録音も 0.35〜0.45 s で消える）。</summary>
    private const float MaxSeconds = 0.6f;

    /// <summary>低い沈みの減衰が、聞こえなくなる（−40 dB）までに要る時定数の数（ln 100）。</summary>
    private const float TimeConstantsToSilence = 4.6f;

    // ── グローブ（手本: 軽いキック。打った瞬間を 0 として測った値）──
    // 0〜30 ms: 100〜200 Hz の「ドッ」が 66 %。30〜90 ms: 「バシッ」200〜400 Hz 28 %・0.4〜3.2 kHz 34 %・3.2 kHz より上 22 %。
    // 90〜300 ms: 100 Hz より下が 84 % の「ドォン」（90 → 65 Hz に下がる）。包絡は 40〜220 ms でほぼ頭打ち、−20 dB は 340 ms。

    /// <summary>グローブの「ドッ」（押しつぶされた詰め物が空気を押す）: 高さ [Hz]・減衰 [s]・大きさ。</summary>
    private const float GloveWhumpHz = 140f;

    private const float GloveWhumpDecay = 0.02f;

    private const float GloveWhumpGain = 0.8f;

    /// <summary>「バシッ」が「ドッ」から遅れる時間 [s]: 軽い 8 ms → 重い 30 ms（詰め物がつぶれきってから革の面が張る。手本では「ドッ」の 30 ms 後）。</summary>
    private const float LightGloveSmackDelay = 0.008f;

    private const float HeavyGloveSmackDelay = 0.03f;

    /// <summary>
    /// 「バシッ」の 3 つの帯の中心 [Hz]（重さ 1 のとき）と大きさ: 革の面の低い張り・中域・高域の擦れ。減衰 [s]。
    /// 帯の幅が違う（高域ほど広く、同じ大きさならエネルギーが多い）ので、大きさは手本の 30〜90 ms の帯の割合（200〜800 Hz 40 %・0.8〜6.4 kHz 29 %・それより上 15 %）に合うように決めた。
    /// </summary>
    private const float GloveSmackLowHz = 330f;

    private const float GloveSmackMidHz = 1500f;

    private const float GloveSmackHighHz = 6000f;

    private const float GloveSmackLowGain = 3.5f;

    private const float GloveSmackMidGain = 0.8f;

    private const float GloveSmackHighGain = 0.23f;

    private const float GloveSmackDecay = 0.025f;

    /// <summary>
    /// グローブの「ドォン」（打たれた体ごと揺れる低い沈み）: 最後に落ち着く高さ [Hz]・打った瞬間の上乗せ [Hz]（98 → 80 Hz。手本は 90 → 83 Hz）・
    /// 下がる速さ [s]・立ち上がり [s]（手本では「バシッ」が引いた後、打撃から 100〜220 ms でいちばん大きい）。
    /// </summary>
    private const float GloveBoomHz = 78f;

    private const float GloveBoomGlide = 20f;

    private const float GloveBoomGlideTime = 0.12f;

    private const float GloveBoomAttack = 0.1f;

    /// <summary>「ドォン」がいちばん大きいまま保つ時間 [s] と、その後の減衰 [s]: 軽い 20 / 40 ms → 重い 200 / 50 ms（手本の −6 dB 280 ms・−20 dB 340 ms・−40 dB 420 ms に合わせた）。大きさ: 軽い 0.3 → 重い 1。</summary>
    private const float LightGloveBoomHold = 0.02f;

    private const float HeavyGloveBoomHold = 0.2f;

    private const float LightGloveBoomDecay = 0.04f;

    private const float HeavyGloveBoomDecay = 0.05f;

    private const float LightGloveBoomGain = 0.3f;

    // ── 素手（手本: 重いパンチ）──
    // 0〜20 ms: 広い帯の「バキッ」（0.4〜0.8 kHz 27 %・6.4〜12.8 kHz 17 %、重心 2.6 kHz）。20〜80 ms: 100 Hz より下 54 % に 0.8〜3.2 kHz 17 % の濁り。
    // 低い音が 155 → 120 → 70 → 50 Hz と 250 ms かけて下がり、いちばん大きいのは 60〜160 ms、−20 dB は 300 ms。

    /// <summary>素手の「バキッ」（骨と皮膚がじかに当たる）: 3 つの帯の中心 [Hz]（重さ 1 のとき）・大きさ・減衰 [s]。大きさは手本の 0〜30 ms の帯の割合に合わせた。</summary>
    private const float BareCrackLowHz = 600f;

    private const float BareCrackMidHz = 2500f;

    private const float BareCrackHighHz = 6500f;

    private const float BareCrackLowGain = 3f;

    private const float BareCrackMidGain = 0.4f;

    private const float BareCrackHighGain = 0.6f;

    private const float BareCrackDecay = 0.008f;

    /// <summary>打った後の濁り（肉が揺れて擦れる）: 帯の中心 [Hz]・立ち上がり [s]・減衰 [s]・大きさ。</summary>
    private const float BareMushHz = 1600f;

    private const float BareMushAttack = 0.005f;

    private const float BareMushDecay = 0.04f;

    private const float BareMushGain = 1.5f;

    /// <summary>
    /// 素手の「ゴッ」（打たれた体の低い揺れ）: 最後に落ち着く高さ [Hz]・打った瞬間の上乗せ [Hz]（143 → 48 Hz。手本は 155 → 120 → 70 → 50 Hz）・下がる速さ [s]・立ち上がり [s]。
    /// </summary>
    private const float BareLowHz = 48f;

    private const float BareLowGlide = 95f;

    private const float BareLowGlideTime = 0.09f;

    private const float BareLowAttack = 0.03f;

    /// <summary>「ゴッ」を保つ時間 [s] と減衰 [s]: 軽い 20 / 30 ms → 重い 200 / 40 ms（手本の −6 dB 240 ms・−20 dB 300 ms・−30 dB 320 ms に合わせた）。大きさ: 軽い 0.4 → 重い 1。</summary>
    private const float LightBareLowHold = 0.02f;

    private const float HeavyBareLowHold = 0.2f;

    private const float LightBareLowDecay = 0.03f;

    private const float HeavyBareLowDecay = 0.04f;

    private const float LightBareLowGain = 0.4f;

    // ── 鉄（素手の作り＋刃の鳴りの短いもの）──

    /// <summary>鉄の面の金属の「ギッ」: 刃の鳴り（<see cref="ContactSound.AddBladeRing"/>）を減衰 60 ms・大きさ 1.2 で足す（素手の「バキッ」の上で金属と聞き分けられる大きさ）。</summary>
    private const float MetalEdgeDecay = 0.06f;

    private const float MetalEdgeGain = 1.2f;

    /// <summary>鉄の面の頭の「チャッ」の大きさ（刃の打ち合いの 0.9 より小さい: 当たる相手は肉で、鉄どうしではない）。</summary>
    private const float MetalEdgeHeadGain = 0.5f;

    /// <summary>グローブ: 「ドッ」→ 遅れて「バシッ」→ 体ごと沈む「ドォン」。</summary>
    public static float[] Glove(Strike strike, Random random, bool immediate = false)
    {
        var w = Math.Clamp(strike.Weight, 0f, 1f);
        var low = strike.Pitch * MathF.Pow(2f, LowOctaves * (1f - w));
        var band = MathF.Pow(2f, BandOctaves * (1f - w)) * (strike.Muffled ? MuffledBand : 1f);
        var bandGain = strike.Muffled ? MuffledBandGain : 1f;
        var lowGain = strike.Muffled ? MuffledLowGain : 1f;

        // v7主音は鋭い層もイベントと同時に始める。従来レシピは音デモ・v6比較用に残す。
        var smackAt = immediate ? 0f : Lerp(LightGloveSmackDelay, HeavyGloveSmackDelay, w);
        var boomHold = Lerp(LightGloveBoomHold, HeavyGloveBoomHold, w);
        var boomDecay = Lerp(LightGloveBoomDecay, HeavyGloveBoomDecay, w);
        var boomGain = Lerp(LightGloveBoomGain, 1f, w) * lowGain;
        var samples = new float[Length(GloveBoomAttack + boomHold + (boomDecay * TimeConstantsToSilence))];

        var smackLow = Biquad.BandPass(GloveSmackLowHz * band, SmackLowQ);
        var smackMid = Biquad.BandPass(GloveSmackMidHz * band, SmackMidQ);
        var smackHigh = Biquad.HighPass(GloveSmackHighHz * band);
        var crackFilter = Biquad.HighPass(HitSound.LogLerp(LightCrackHz, HeavyCrackHz, w));
        float whumpPhase = 0f, boomPhase = 0f;
        for (var n = 0; n < samples.Length; n++)
        {
            var t = n / (float)SampleRate;
            whumpPhase += 2f * MathF.PI * GloveWhumpHz * low * (1f + (WhumpPitchDrop * MathF.Exp(-t / WhumpPitchDropTime))) / SampleRate;
            var whump = MathF.Sin(whumpPhase) * Envelope(t, WhumpAttack, 0f, GloveWhumpDecay) * GloveWhumpGain * lowGain;

            var white = (random.NextSingle() * 2f) - 1f;
            var smackNoise = (smackLow.Process(white) * GloveSmackLowGain) + (smackMid.Process(white) * GloveSmackMidGain)
                + (smackHigh.Process(white) * GloveSmackHighGain);
            var crackNoise = crackFilter.Process(white);
            var ts = t - smackAt;
            var smack = ts < 0f ? 0f : smackNoise * BandNoiseLevel * Envelope(ts, NoiseAttack, 0f, GloveSmackDecay) * bandGain;
            var crack = ts < 0f ? 0f : crackNoise * CrackLevel * MathF.Exp(-ts / CrackDecay) * CrackAmount(strike.Clean);

            boomPhase += 2f * MathF.PI * (GloveBoomHz + (GloveBoomGlide * MathF.Exp(-t / GloveBoomGlideTime))) * low / SampleRate;
            var boom = MathF.Sin(boomPhase) * Envelope(t, GloveBoomAttack, boomHold, boomDecay) * boomGain;

            samples[n] = whump + smack + crack + boom;
        }

        return samples;
    }

    /// <summary>素手: 広い帯の鋭い「バキッ」→ 濁り → 下がっていく低い「ゴッ」。</summary>
    public static float[] Bare(Strike strike, Random random)
    {
        var w = Math.Clamp(strike.Weight, 0f, 1f);
        var low = strike.Pitch * MathF.Pow(2f, LowOctaves * (1f - w));
        var band = MathF.Pow(2f, BandOctaves * (1f - w)) * (strike.Muffled ? MuffledBand : 1f);
        var bandGain = strike.Muffled ? MuffledBandGain : 1f;
        var lowHold = Lerp(LightBareLowHold, HeavyBareLowHold, w);
        var lowDecay = Lerp(LightBareLowDecay, HeavyBareLowDecay, w);
        var lowGain = Lerp(LightBareLowGain, 1f, w) * (strike.Muffled ? MuffledLowGain : 1f);
        var samples = new float[Length(BareLowAttack + lowHold + (lowDecay * TimeConstantsToSilence))];

        var crackLow = Biquad.BandPass(BareCrackLowHz * band, BareCrackQ);
        var crackMid = Biquad.BandPass(BareCrackMidHz * band, BareCrackQ);
        var crackHigh = Biquad.HighPass(BareCrackHighHz * band);
        var mush = Biquad.BandPass(BareMushHz * band, BareMushQ);
        var snapFilter = Biquad.HighPass(HitSound.LogLerp(LightCrackHz, HeavyCrackHz, w));
        var phase = 0f;
        for (var n = 0; n < samples.Length; n++)
        {
            var t = n / (float)SampleRate;
            var white = (random.NextSingle() * 2f) - 1f;
            var crackNoise = (crackLow.Process(white) * BareCrackLowGain) + (crackMid.Process(white) * BareCrackMidGain)
                + (crackHigh.Process(white) * BareCrackHighGain);
            var crack = crackNoise * BandNoiseLevel * Envelope(t, NoiseAttack, 0f, BareCrackDecay) * bandGain;
            var murk = mush.Process(white) * BandNoiseLevel * Envelope(t, BareMushAttack, 0f, BareMushDecay) * BareMushGain * bandGain;
            var snap = snapFilter.Process(white) * CrackLevel * MathF.Exp(-t / CrackDecay) * CrackAmount(strike.Clean);

            phase += 2f * MathF.PI * (BareLowHz + (BareLowGlide * MathF.Exp(-t / BareLowGlideTime))) * low / SampleRate;
            var thud = MathF.Sin(phase) * Envelope(t, BareLowAttack, lowHold, lowDecay) * lowGain;

            samples[n] = crack + murk + snap + thud;
        }

        return samples;
    }

    /// <summary>鉄の面（ナックル・籠手）: 素手の作りに、刃の鳴りの短いもの（金属の「ギッ」）を足す。</summary>
    public static float[] Metal(Strike strike, Random random)
    {
        var samples = Bare(strike, random);
        var pitch = MathF.Pow(2f, BandOctaves * 0.5f * (1f - Math.Clamp(strike.Weight, 0f, 1f)));
        ContactSound.AddBladeRing(samples, pitch, hold: 0f, MetalEdgeDecay, MetalEdgeGain * (strike.Muffled ? MuffledBandGain : 1f), headGain: MetalEdgeHeadGain, random);
        return samples;
    }

    /// <summary>立ち上がり <paramref name="attack"/>（1 − e^(−t/attack)）、<paramref name="hold"/> まで保ち、その後 e^(−(t−hold)/decay) で減る包絡。</summary>
    internal static float Envelope(float t, float attack, float hold, float decay) =>
        (1f - MathF.Exp(-t / attack)) * MathF.Exp(-MathF.Max(0f, t - hold) / decay);

    private static int Length(float seconds) => (int)(SampleRate * Math.Clamp(seconds, 0.09f, MaxSeconds));

    /// <summary>「パキッ」の大きさ: 芯 <see cref="CrackFromClean"/> から乗り始め、芯 1 で 1（普通の当たりには乗らない）。</summary>
    private static float CrackAmount(float clean) => MathF.Max(0f, (clean - CrackFromClean) / (1f - CrackFromClean));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
