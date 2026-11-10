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
    private readonly Dictionary<(int Intensity, int Clean, HitTarget Target), AudioClip> _clips = [];
    private readonly List<int> _voices = [];
    private readonly Random _variation = new();
    private readonly AudioClip?[] _fatal = new AudioClip?[FatalVariants];

    public HitSoundPlayer(IAudioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>既定の音声出力（Windows の既定デバイス。Quest Link 中は Link の音声出力）で鳴らす。</summary>
    public static HitSoundPlayer CreateDefault(BreakStyle breakStyle = VoxelBreakSound.DefaultStyle)
    {
        VoxelBreakSound.Prewarm(breakStyle);
        return new(new OpenAlAudioBackend()) { BreakStyle = breakStyle };
    }

    /// <summary>ボクセルが砕ける音・床に落ちる音の方式。</summary>
    public BreakStyle BreakStyle { get; init; } = VoxelBreakSound.DefaultStyle;

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
        if (!_backend.IsAvailable)
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
        var key = ((int)MathF.Round(tone.Intensity * (IntensityLevels - 1)), (int)MathF.Round(tone.Clean * (CleanLevels - 1)), tone.Target);
        if (!_clips.TryGetValue(key, out var clip))
        {
            var quantized = new HitTone(key.Item1 / (float)(IntensityLevels - 1), key.Item2 / (float)(CleanLevels - 1), key.Target);
            var name = string.Create(CultureInfo.InvariantCulture, $"hit_{key.Target}_{key.Item1}_{key.Item2}");
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
        foreach (var voice in _voices)
        {
            _backend.StopVoice(voice);
        }

        _voices.Clear();
        _backend.Dispose();
    }
}
