using System.Numerics;
using System.Collections.Concurrent;
using Aigis.Audio;
using MiniRing.Audio;
using MiniRing.Presentation;
using MiniRing.Vr;

namespace MiniRing.Tests;

[TestClass]
public sealed class PresentationSimulationTests
{
    [TestMethod]
    public void QueuedAudio_OwnsEventsAcrossReset_AndDrainsEveryBell()
    {
        using var game = new BoxingGame(seed: 41);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var backend = new CapturingAudio(started, release);
        var hits = new List<HitEvent> { new(game.Player, Hand.Left, game.Cpu, HitZone.Head, Vector3.Zero, 3f, 1f, 0f, 3f) };
        using (var sound = new HitSoundGame(game, () => hits, new HitSoundPlayer(backend), bells: () => RoundBell.Start)
            { DeferredPlayback = true, CaptureCrowd = _ => [new CrowdCue(0, null, 0, 0)] })
        {
            sound.Step(default, 1f / 240f);
            try
            {
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)));
                hits.Clear();
                for (var i = 0; i < 3; i++) { sound.Step(new(default, ResetRequested: i == 0), 1f / 240f); }
                sound.PresentSounds();
            }
            finally { release.Set(); }
        }
        Assert.AreEqual(4, backend.Clips.Count(c => c.Name == "gong"));
        Assert.IsTrue(backend.Clips.Any(c => c.Name == "impact"));
        Assert.IsTrue(backend.Disposed);

        var frame = VrAudioFrame.Capture(game, 1f / 240f, true);
        using var silent = new HitSoundPlayer(new NullAudioBackend());
        using (var playback = new VrAudioPlayback(silent))
        {
            playback.Submit(frame);
            playback.ThrowIfFaulted();
        }
        var capture = new VrSceneCapture();
        var first = capture.Capture(game);
        Assert.IsNotNull(first.VoxelSnapshot);
        Assert.IsNotNull(capture.Capture(game));
    }

    private sealed class CapturingAudio(ManualResetEventSlim started, ManualResetEventSlim release) : IAudioBackend
    {
        private int _calls;
        public ConcurrentQueue<AudioClip> Clips { get; } = new();
        public bool Disposed { get; private set; }
        public bool IsAvailable => true;
        public int Play(AudioClip clip, float volume, bool shouldLoop, float pitch = 1f)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1) { started.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); }
            Clips.Enqueue(clip);
            return call;
        }
        public void StopVoice(int voiceHandle) { }
        public void SetVolume(int voiceHandle, float volume) { }
        public void SetPitch(int voiceHandle, float pitch) { }
        public bool IsPlaying(int voiceHandle) => false;
        public void Dispose() => Disposed = true;
    }

    [TestMethod]
    public void PublishedScene_IsStable_WhileSimulationContinuesAndResets()
    {
        using var game = new BoxingGame(seed: 47) { FaceCam = FaceCam.Default };
        using var host = new SnapshotMatchGame(game, game);
        host.Step(default, 1f / 60f);
        var (instances, _) = host.BuildRenderables();
        var old = instances.ToArray();
        Assert.IsTrue(SpinWait.SpinUntil(() => host.CompletedSteps >= 3, TimeSpan.FromSeconds(30)));
        host.Step(new BoxingInput(default, ResetRequested: true), 1f / 60f);
        Assert.IsTrue(SpinWait.SpinUntil(() => host.Latest.Round > 1, TimeSpan.FromSeconds(30)));
        CollectionAssert.AreEqual(old, instances.ToArray());
        var views = host.Views(new Aigis.Player.OrbitCamera(), new Vector2(960, 540));
        Assert.IsNotNull(views);
        _ = host.BuildVoxelVolumes();
        _ = host.BuildParticles();
        _ = host.BuildLighting();
        _ = host.BuildShadow();
        _ = host.BuildPostProcess();
        _ = host.BuildOverlayText(Matrix4x4.Identity, new Vector2(960, 540));
        _ = host.BuildOverlayShapes(Matrix4x4.Identity, new Vector2(960, 540));
        _ = host.World;
        _ = host.StatusLine;
        _ = host.IsOver;
    }
}
