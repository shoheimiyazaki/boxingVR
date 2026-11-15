using Aigis.Player;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Vr;

internal sealed record VrAudioFrame(float Dt, ImpactCue[] Impacts, ShatterEvent[] Shatters,
    DebrisLanding[] Landings, SwingEvent[] Swings, RoundBell Bell, SeverEvent[] Severs)
{
    public static VrAudioFrame Capture(BoxingGame game, float dt, bool playHits)
    {
        var shatters = game.RecentShatters.Select(value => value with { Grains = value.Grains.ToArray() }).ToArray();
        return new(dt, ImpactAudio.Gather(game.RecentHits, game.RecentContacts, shatters, playHits,
            [.. game.WeaponHits, .. game.SelfWeaponHits]).ToArray(), shatters,
            game.RecentLandings.ToArray(), game.RecentSwings.ToArray(), game.RecentBell, game.RecentSevers.ToArray());
    }
}

internal sealed class VrAudioPlayback : IDisposable
{
    private readonly ReliableEventWorker<VrAudioFrame> _worker;

    public VrAudioPlayback(Action<VrAudioFrame> consume) => _worker = new(consume);

    public VrAudioPlayback(HitSoundPlayer sounds) : this(frame =>
    {
        sounds.PlayImpactCues(frame.Dt, frame.Impacts, frame.Shatters);
        sounds.PlayLandings(frame.Landings);
        sounds.PlaySwings(frame.Swings);
        sounds.PlayBell(frame.Bell);
        sounds.PlaySevers(frame.Severs);
    }) { }

    public void Submit(VrAudioFrame frame) => _worker.Enqueue(frame);
    public void ThrowIfFaulted() => _worker.ThrowIfFaulted();
    public void Dispose() => _worker.Dispose();
}
