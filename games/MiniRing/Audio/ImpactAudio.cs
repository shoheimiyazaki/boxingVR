using System.Numerics;
using MiniRing.Voxels;

namespace MiniRing.Audio;

/// <summary>同一の打撃について重複して届く表示用イベント。Source は攻撃者・手の識別子（0 は不明）。</summary>
public sealed record ImpactCue(Vector3 Position, int Source, HitTone? Hit = null, ContactSoundEvent? Contact = null, ShatterEvent? Shatter = null)
{
    public static ImpactCue Touch(ContactSoundEvent contact) => new(contact.Position, 0, Contact: contact);
    public int Priority => Hit is not null ? 2 : Shatter is not null ? 1 : 0;
    public float Weight => Hit?.Intensity ?? (Shatter is { } s ? s.Grains.Count / (s.Grains.Count + 20f) : Contact is { } c ? ImpactAudio.ContactGain(c) : 0);
}

public sealed record ImpactVoice(ImpactCue Cue, byte[] Pcm, double OccurredAt = 0);

/// <summary>確定した打撃は同じ歩で1声。接触・破片は予算付きの背景層。試合の状態は変更しない。</summary>
public sealed class ImpactAudio
{
    public const double WindowSeconds = 0; // 互換用。打撃を保留しない。
    public const float RadiusMeters = 0.07f;
    public const int MaxBackgroundVoices = 2;
    public const float BackgroundPeakBudget = 0.12f;
    // 人形の衝撃の基準エネルギー[J]。半分の音量になる値で、発音を切る閾値ではない。
    public const float ContactEnergyScale = 0.08f;
    private sealed record Background(double Ends, float Peak);
    private IReadOnlyList<Background> _background = [];
    private double _time;
    private int _seed;

    public IReadOnlyList<ImpactVoice> Flush(BreakStyle style = VoxelBreakSound.DefaultStyle) => Push(_time, [], style);

    public static float HitGain(float intensity) => MathF.Pow(Math.Clamp(intensity, 0, 1), 0.7f);

    public static float ContactGain(ContactSoundEvent contact) => contact.Energy >= 0
        ? contact.Energy / (ContactEnergyScale + contact.Energy)
        : MathF.Pow(Math.Clamp(contact.Strength, 0, 1), 2);

    public static IReadOnlyList<ImpactCue> Gather(IReadOnlyList<HitEvent> hits, IReadOnlyList<ContactSoundEvent> contacts,
        IReadOnlyList<ShatterEvent> shatters, bool playHits = true, IReadOnlyList<WeaponHitEvent>? weaponHits = null) =>
        [.. hits.Where(_ => playHits).Select(h => new ImpactCue(h.Point, h.Attacker.ForearmId(h.Hand) + 1, HitTone.Of(h))),
         .. (weaponHits ?? []).Where(_ => playHits).Select(h => new ImpactCue(h.Point, h.Attacker.ForearmId(BoxingGame.WeaponHand) + 1,
             HitTone.Of(new HitEvent(h.Attacker, BoxingGame.WeaponHand, h.Victim, h.Zone, h.Point, h.Speed,
                 h.Momentum / MathF.Max(h.Speed, 1e-6f), 0f, h.Damage, h.Face)), h.Sound)),
         .. shatters.Where(s => s.Grains.Count > 0).Select(s => new ImpactCue(s.Point, 0, Shatter: s)),
         .. contacts.Select(ImpactCue.Touch)];

    public IReadOnlyList<ImpactVoice> Push(double time, IReadOnlyList<ImpactCue> cues, BreakStyle style = VoxelBreakSound.DefaultStyle)
    {
        ArgumentNullException.ThrowIfNull(cues);
        if (!double.IsFinite(time) || time < _time)
        {
            throw new ArgumentOutOfRangeException(nameof(time), "音の時刻は有限で単調に増える必要があります。");
        }
        foreach (var cue in cues)
        {
            if (!float.IsFinite(cue.Position.X + cue.Position.Y + cue.Position.Z + cue.Weight) ||
                (cue.Hit is { } h && !float.IsFinite(h.Clean)) ||
                (cue.Contact is { } c && (!float.IsFinite(c.Energy) || !float.IsFinite(c.Strength) || (c.Resonance is { } r && (!float.IsFinite(r.DecayScale) || r.DecayScale <= 0)))) ||
                (cue.Shatter is { } s && s.Grains.Any(g => !float.IsFinite(g.Delay) || g.Delay < 0)))
            {
                throw new ArgumentException("音の入力に有限でない値があります。", nameof(cues));
            }
        }
        _time = time;
        _background = [.. _background.Where(b => b.Ends > time)];
        var ready = new List<ImpactVoice>();
        var hits = cues.Where(c => c.Hit is not null).ToArray();
        // HitEvent は打撃側で確定済み。近接した別の拳や次の歩の打撃を潰さない。
        foreach (var hit in hits)
        {
            var contact = hit.Contact ?? cues.Where(c => c.Contact is not null && Nearby(c, hit)).MaxBy(c => c.Weight)?.Contact;
            if (Render(hit with { Contact = contact }, ++_seed, style) is { } voice)
            {
                ready.Add(voice with { OccurredAt = time });
            }
        }
        var selected = new List<ImpactCue>();
        foreach (var cue in cues.Where(c => c.Hit is null).OrderByDescending(c => c.Priority).ThenByDescending(c => c.Weight))
        {
            if (_background.Count >= MaxBackgroundVoices)
            {
                break;
            }
            // 同じ歩の打撃に属する微小接触は主音へ吸収。破片は独立した小さな層。
            if ((cue.Contact is not null && hits.Any(h => Nearby(h, cue))) || selected.Any(c => Nearby(c, cue)))
            {
                continue;
            }
            if (Render(cue, ++_seed, style) is not { } voice)
            {
                continue;
            }
            if (LimitBackground(time, voice.Pcm) is not { } pcm)
            {
                break;
            }
            ready.Add(voice with { Pcm = pcm, OccurredAt = time });
            selected.Add(cue);
        }
        return ready;
    }

    /// <summary>破片の着地も接触・破壊と同じ同時数・ピーク和予算を共有する。待たず、満杯なら譲る。</summary>
    public byte[]? LimitBackground(double time, byte[] pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        if (!double.IsFinite(time) || time < _time)
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }
        if (pcm.Length % 2 != 0)
        {
            throw new ArgumentException("16-bit PCMの長さは偶数である必要があります。", nameof(pcm));
        }
        _time = time;
        _background = [.. _background.Where(b => b.Ends > time)];
        if (_background.Count >= MaxBackgroundVoices || pcm.Length == 0)
        {
            return null;
        }
        var budget = MathF.Max(0, BackgroundPeakBudget - _background.Sum(b => b.Peak));
        if (budget <= 0)
        {
            return null;
        }
        var gain = MathF.Min(0.06f, budget) / MathF.Max(Peak(pcm), 1e-6f);
        var scaled = Scale(pcm, MathF.Min(1f, gain));
        _background = [.. _background, new Background(time + scaled.Length / (2d * HitSound.SampleRate), Peak(scaled))];
        return scaled;
    }

    private static bool Nearby(ImpactCue a, ImpactCue b) => Vector3.DistanceSquared(a.Position, b.Position) <= RadiusMeters * RadiusMeters;

    private static float Peak(byte[] pcm) => Enumerable.Range(0, pcm.Length / 2).Max(i => MathF.Abs(BitConverter.ToInt16(pcm, i * 2) / 32768f));

    private static byte[] Scale(byte[] pcm, float gain)
    {
        var scaled = new byte[pcm.Length];
        for (var at = 0; at < pcm.Length; at += 2)
        {
            var sample = (short)Math.Clamp(BitConverter.ToInt16(pcm, at) * gain, short.MinValue, short.MaxValue);
            scaled[at] = (byte)sample;
            scaled[at + 1] = (byte)(sample >> 8);
        }
        return scaled;
    }

    private static ImpactVoice? Render(ImpactCue cue, int seed, BreakStyle style)
    {
        byte[] pcm;
        float gain;
        if (cue.Hit is { } hit)
        {
            pcm = HitSound.Synthesize(hit, seed, immediate: true);
            gain = HitGain(hit.Intensity);
        }
        else if (cue.Shatter is { } shatter)
        {
            // セルごとの遅延を10 ms内に圧縮し、1つの破壊音の立ち上がりにする。
            var compact = shatter with { Grains = [.. shatter.Grains.Select(g => g with { Delay = MathF.Min(g.Delay, 0.01f) })] };
            pcm = VoxelBreakSound.Synthesize(compact, seed, style);
            gain = 0.55f * HitGain(cue.Weight);
        }
        else if (cue.Contact is { } contact)
        {
            pcm = ContactSound.Synthesize(contact.Pair, contact.Strength, seed, contact.Resonance);
            // 接近速度の二乗は運動エネルギーに比例。微小な接触を固定音量に正規化しない。
            gain = ContactGain(contact);
        }
        else
        {
            return null;
        }
        var scaled = Scale(pcm, gain);
        if (cue.Hit is { } tone && cue.Contact is { } surface)
        {
            // 材質の響きは同時に混ぜ、別の声や別の立ち上がりを作らない。
            var layer = ContactSound.Synthesize(surface.Pair, surface.Strength, seed, surface.Resonance);
            var mixed = new byte[Math.Max(scaled.Length, layer.Length)];
            for (var at = 0; at < mixed.Length; at += 2)
            {
                var core = at < scaled.Length ? BitConverter.ToInt16(scaled, at) : 0;
                var face = at < layer.Length ? BitConverter.ToInt16(layer, at) : 0;
                var sample = (short)Math.Clamp(core + face * 0.12f * HitGain(tone.Intensity), short.MinValue, short.MaxValue);
                mixed[at] = (byte)sample;
                mixed[at + 1] = (byte)(sample >> 8);
            }
            scaled = mixed;
        }
        return scaled.Any(b => b != 0) ? new ImpactVoice(cue, scaled) : null;
    }
}
