using System.Globalization;
using Aigis.Audio;
using Aigis.Render.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Audio;

/// <summary>
/// 打撃のたびに <see cref="HitSound"/> を鳴らす。音色は強さ 8 段 × 芯 4 段 × 当たった場所 3 種で、
/// 初めて使うときに合成してキャッシュする。毎回ピッチを少し揺らして、同じ音の繰り返しに聞こえないようにする。
/// 音の揺らぎは試合の決定性（リプレイ）とは無関係。音声デバイスがなければ何もしない。
/// </summary>
public sealed class HitSoundPlayer : IDisposable
{
    private const int IntensityLevels = 8;
    private const int CleanLevels = 4;
    private const int MaxVoices = 24;

    /// <summary>床に落ちる音が使わずに残しておく声の数（打撃・砕ける音・致命傷のため）。</summary>
    private const int ReservedForBreaks = 12;
    private const int FatalVariants = 2;

    private readonly IAudioBackend _backend;
    private readonly Dictionary<(int Intensity, int Clean, HitTarget Target, HitSurface Surface), AudioClip> _clips = [];
    private readonly List<int> _voices = [];
    private readonly Random _variation = new();
    private readonly AudioClip?[] _fatal = new AudioClip?[FatalVariants];

    public HitSoundPlayer(IAudioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>既定の音声出力（Windows の既定デバイス。Quest Link 中は Link の音声出力）で鳴らす。</summary>
    public static HitSoundPlayer CreateDefault(BreakStyle breakStyle = VoxelBreakSound.DefaultStyle, bool playHits = false)
    {
        VoxelBreakSound.Prewarm(breakStyle);
        return new(new OpenAlAudioBackend()) { BreakStyle = breakStyle, PlayHits = playHits };
    }

    /// <summary>ボクセルが砕ける音・床に落ちる音の方式。</summary>
    public BreakStyle BreakStyle { get; init; } = VoxelBreakSound.DefaultStyle;

    /// <summary>パンチが当たる音を鳴らすか（既定は鳴らさない: ボクセルの音と風切り音だけ）。</summary>
    public bool PlayHits { get; init; }

    public bool IsAvailable => _backend.IsAvailable;

    public void PlayAll(IReadOnlyList<HitEvent> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        foreach (var hit in hits)
        {
            Play(hit);
        }
    }

    public void Play(in HitEvent hit)
    {
        if (!_backend.IsAvailable || !PlayHits)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        Reap();
        if (_voices.Count >= MaxVoices)
        {
            _backend.StopVoice(_voices[0]);
            _voices.RemoveAt(0);
        }

        var tone = HitTone.Of(hit);
        var key = ((int)MathF.Round(tone.Intensity * (IntensityLevels - 1)), (int)MathF.Round(tone.Clean * (CleanLevels - 1)), tone.Target, tone.Surface); // 打った面の材質（グローブ・素手・鉄）ごとに別の音
        if (!_clips.TryGetValue(key, out var clip))
        {
            var quantized = new HitTone(key.Item1 / (float)(IntensityLevels - 1), key.Item2 / (float)(CleanLevels - 1), key.Target, key.Surface);
            var name = string.Create(CultureInfo.InvariantCulture, $"hit_{key.Surface}_{key.Target}_{key.Item1}_{key.Item2}");
            clip = new AudioClip(name, AudioSampleFormat.Mono16, HitSound.SampleRate, HitSound.Synthesize(quantized, seed: key.GetHashCode()), IsGenerated: true);
            _clips[key] = clip;
        }

        var pitch = 1f + ((_variation.NextSingle() - 0.5f) * 0.08f);
        _voices.Add(_backend.Play(clip, volume: 1f, shouldLoop: false, pitch));
    }

    /// <summary>
    /// 砕けたボクセルの音（<see cref="VoxelBreakSound"/>）: 1 発ごとに、砕けたボクセルの粒を重ねた音を合成して鳴らす。
    /// 致命傷なら、その上に致命傷の音も鳴らす。
    /// </summary>
    public void PlayShatters(IReadOnlyList<ShatterEvent> shatters)
    {
        ArgumentNullException.ThrowIfNull(shatters);
        if (!_backend.IsAvailable || shatters.Count == 0)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        foreach (var shatter in shatters)
        {
            if (shatter.Grains.Count > 0)
            {
                var pcm = VoxelBreakSound.Synthesize(shatter, _variation.Next(), BreakStyle);
                Start(new AudioClip("shatter", AudioSampleFormat.Mono16, VoxelBreakSound.SampleRate, pcm, IsGenerated: true), 1f);
            }

            if (shatter.Fatal)
            {
                var v = _variation.Next(FatalVariants);
                _fatal[v] ??= new AudioClip($"fatal_{v}", AudioSampleFormat.Mono16, VoxelBreakSound.SampleRate, VoxelBreakSound.Fatal(seed: 7 + v, BreakStyle), IsGenerated: true);
                Start(_fatal[v]!, 1f);
            }
        }
    }

    /// <summary>1 ステップぶんの、床に落ちた破片の音（パラパラ）。</summary>
    public void PlayLandings(IReadOnlyList<DebrisLanding> landings)
    {
        ArgumentNullException.ThrowIfNull(landings);
        if (!_backend.IsAvailable || landings.Count == 0)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        Reap();
        if (_voices.Count >= MaxVoices - ReservedForBreaks)
        {
            return; // 床の音は譲る: 砕ける音や致命傷の長い音を、古い順に押し出さないように
        }

        if (VoxelBreakSound.Landings(landings, _variation.Next(), BreakStyle) is { } pcm)
        {
            Start(new AudioClip("landing", AudioSampleFormat.Mono16, VoxelBreakSound.SampleRate, pcm, IsGenerated: true), 1f);
        }
    }

    /// <summary>1 ステップぶんの、振り抜いた拳の風切り音（小さく）。</summary>
    public void PlaySwings(IReadOnlyList<SwingEvent> swings)
    {
        ArgumentNullException.ThrowIfNull(swings);
        if (!_backend.IsAvailable || swings.Count == 0)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        foreach (var swing in swings)
        {
            Reap();
            if (_voices.Count >= MaxVoices - ReservedForBreaks)
            {
                return;
            }

            if (SwingSound.Synthesize(swing.Speed, _variation.Next()) is { } pcm)
            {
                Start(new AudioClip("swing", AudioSampleFormat.Mono16, SwingSound.SampleRate, pcm, IsGenerated: true), 1f);
            }
        }
    }

    /// <summary>1 ステップぶんの、拳が体をかすめた音（打撃音に隠れるほど小さく）。</summary>
    public void PlayGrazes(IReadOnlyList<GrazeEvent> grazes)
    {
        ArgumentNullException.ThrowIfNull(grazes);
        if (!_backend.IsAvailable || grazes.Count == 0)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        foreach (var graze in grazes)
        {
            Reap();
            if (_voices.Count >= MaxVoices - ReservedForBreaks)
            {
                return;
            }

            Start(new AudioClip("graze", AudioSampleFormat.Mono16, GrazeSound.SampleRate, GrazeSound.Synthesize(graze.Speed, _variation.Next()), IsGenerated: true), 1f);
        }
    }

    /// <summary>1 ステップぶんの、物どうしが何も壊さずにぶつかった音（金属どうし・グローブと皮膚など、材質の組ごと）。</summary>
    public void PlayContacts(IReadOnlyList<ContactSoundEvent> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (!_backend.IsAvailable || contacts.Count == 0)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        foreach (var contact in contacts)
        {
            Reap();
            if (_voices.Count >= MaxVoices - ReservedForBreaks)
            {
                return;
            }

            // 音量は合成した波形の山にすでに入っている（ContactSound.Loudness）ので 1 で鳴らす。
            Start(new AudioClip("contact", AudioSampleFormat.Mono16, ContactSound.SampleRate, ContactSound.Synthesize(contact.Pair, contact.Strength, _variation.Next()), IsGenerated: true), 1f);
        }
    }

    /// <summary>1 ステップぶんの、部位が切り離された音（骨が折れる音と裂ける音）。</summary>
    public void PlaySevers(IReadOnlyList<SeverEvent> severs)
    {
        ArgumentNullException.ThrowIfNull(severs);
        if (!_backend.IsAvailable || severs.Count == 0)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        foreach (var sever in severs)
        {
            Reap();
            if (_voices.Count >= MaxVoices - ReservedForBreaks)
            {
                return;
            }

            Start(new AudioClip("sever", AudioSampleFormat.Mono16, SeverSound.SampleRate, SeverSound.Synthesize(sever.Strength, _variation.Next()), IsGenerated: true), 1f);
        }
    }

    /// <summary>
    /// 会場のざわめきと反応の音を、別のスレッドで前もって作り始める（テレビ中継のモード。合成は 1 秒近くかかるので、試合の歩を止めない）。
    /// できたら <see cref="PlayCrowd"/> が鳴らし始める。反応は種類ごとに強さ 3 段 × 揺らぎ 3 通りを作っておく。
    /// </summary>
    public void PrepareCrowd(int seed, float ambienceSeconds = 20f)
    {
        if (!_backend.IsAvailable || _crowd is not null)
        {
            return;
        }

        _crowd = Task.Run(() =>
        {
            var ambience = CrowdSound.ToPcm(CrowdSound.Ambience(ambienceSeconds, seed));
            var reactions = new Dictionary<(CrowdReaction, int, int), byte[]>();
            foreach (var reaction in Enum.GetValues<CrowdReaction>())
            {
                for (var level = 0; level < CrowdLevels; level++)
                {
                    for (var variant = 0; variant < CrowdVariants; variant++)
                    {
                        reactions[(reaction, level, variant)] = CrowdSound.ToPcm(CrowdSound.React(reaction, (level + 0.5f) / CrowdLevels, seed + (31 * variant) + level));
                    }
                }
            }

            return (ambience, (IReadOnlyDictionary<(CrowdReaction, int, int), byte[]>)reactions);
        });
    }

    private const int CrowdLevels = 3;
    private const int CrowdVariants = 3;
    private Task<(byte[] Ambience, IReadOnlyDictionary<(CrowdReaction, int, int), byte[]> Reactions)>? _crowd;
    private int? _ambience;

    /// <summary>会場の反応（歓声・どよめき・カウントの掛け声）を 1 つ鳴らす。音がまだできていなければ鳴らさない（ざわめきも、できたら流し始める）。</summary>
    public void PlayCrowd(CrowdCue cue)
    {
        if (!_backend.IsAvailable || _crowd is not { IsCompletedSuccessfully: true } ready)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        var (ambience, reactions) = ready.Result;
        _ambience ??= _backend.Play(new AudioClip("crowd", AudioSampleFormat.Mono16, CrowdSound.SampleRate, ambience, IsGenerated: true), volume: 1f, shouldLoop: true);
        if (cue.Reaction is { } reaction && reactions.TryGetValue((reaction, Math.Clamp((int)(cue.Intensity * CrowdLevels), 0, CrowdLevels - 1), Math.Abs(cue.Seed) % CrowdVariants), out var pcm))
        {
            Reap();
            if (_voices.Count >= MaxVoices - ReservedForBreaks)
            {
                return; // 砕ける音・ゴングの声を奪わない
            }

            Start(new AudioClip("cheer", AudioSampleFormat.Mono16, CrowdSound.SampleRate, pcm, IsGenerated: true), 1f);
        }
    }

    /// <summary>ラウンドのゴング（<see cref="GongSound"/>）。他の音で声が埋まっていても、いちばん古い声を止めて鳴らす。</summary>
    public void PlayBell(RoundBell bell)
    {
        if (!_backend.IsAvailable || GongSound.Synthesize(bell) is not { } pcm)
        {
            return;
        }

        OpenAlAudioBackend.MakeCurrentOnThisThread();
        Start(new AudioClip("gong", AudioSampleFormat.Mono16, GongSound.SampleRate, pcm, IsGenerated: true), 1f);
    }

    private void Start(AudioClip clip, float pitch)
    {
        Reap();
        if (_voices.Count >= MaxVoices)
        {
            _backend.StopVoice(_voices[0]);
            _voices.RemoveAt(0);
        }

        _voices.Add(_backend.Play(clip, volume: 1f, shouldLoop: false, pitch));
    }

    /// <summary>鳴り終わった声を片付ける（OpenAL の音源とバッファを解放する）。</summary>
    private void Reap()
    {
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            if (!_backend.IsPlaying(_voices[i]))
            {
                _backend.StopVoice(_voices[i]);
                _voices.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        if (_ambience is { } ambience)
        {
            _backend.StopVoice(ambience);
        }

        foreach (var voice in _voices)
        {
            _backend.StopVoice(voice);
        }

        _voices.Clear();
        _backend.Dispose();
    }
}
