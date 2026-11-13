using System.Globalization;
using System.Numerics;
using MiniRing.Audio;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
public sealed class SoundImpactProbe
{
    [TestMethod]
    public void MeasureSoundSourcesProbe()
    {
        foreach (var wear in new[] { HandWear.Glove, HandWear.KnuckleDuster })
        {
            using var game = new BoxingGame(seed: 613, corners: new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), PlayerWear: wear, CpuWear: wear));
            int hits = 0, contacts = 0, shatters = 0, grains = 0, grazes = 0;
            int voices = 0, audible = 0, hitVoices = 0;
            var sound = new ImpactAudio();
            var metalContacts = new List<ContactSoundEvent>();
            var rows = new List<string> { "seconds,kind,strength,damage,x,y,z" };
            var voiceRows = new List<string> { "seconds,kind,source,peak,intensity,onset_ms" };
            void NoteVoice(ImpactVoice voice)
            {
                voices++;
                var peak = SoundAnalysis.PeakOf(voice.Pcm);
                audible += peak >= 0.02f ? 1 : 0;
                hitVoices += voice.Cue.Hit is not null ? 1 : 0;
                var onset = Enumerable.Range(0, voice.Pcm.Length / 2).FirstOrDefault(i => Math.Abs((int)BitConverter.ToInt16(voice.Pcm, i * 2)) > 32) * 1000d / HitSound.SampleRate;
                var kind = voice.Cue.Hit is not null ? "hit" : voice.Cue.Shatter is not null ? "shatter" : "contact";
                voiceRows.Add(string.Create(CultureInfo.InvariantCulture, $"{voice.OccurredAt:F4},{kind},{voice.Cue.Source},{peak},{voice.Cue.Hit?.Intensity ?? 0},{onset}"));
            }
            for (var step = 0; step < 720; step++)
            {
                game.Step(default, 1f / 60f);
                hits += game.RecentHits.Count;
                contacts += game.RecentContacts.Count;
                metalContacts.AddRange(game.RecentContacts.Where(c => c.Pair == ContactPair.MetalMetal));
                shatters += game.RecentShatters.Count;
                grains += game.RecentShatters.Sum(s => s.Grains.Count);
                grazes += game.RecentGrazes.Count;
                foreach (var voice in sound.Push((step + 1) / 60d, ImpactAudio.Gather(game.RecentHits, game.RecentContacts, game.RecentShatters)))
                {
                    NoteVoice(voice);
                }
                foreach (var c in game.RecentContacts)
                {
                    rows.Add(string.Create(CultureInfo.InvariantCulture, $"{(step + 1) / 60f:F4},{c.Pair},{c.Strength},0,{c.Position.X},{c.Position.Y},{c.Position.Z}"));
                }
                foreach (var h in game.RecentHits)
                {
                    rows.Add(string.Create(CultureInfo.InvariantCulture, $"{(step + 1) / 60f:F4},hit,{h.Speed},{h.Damage},{h.Point.X},{h.Point.Y},{h.Point.Z}"));
                }
            }
            foreach (var voice in sound.Flush())
            {
                NoteVoice(voice);
            }
            Console.WriteLine($"{wear}: 12s hits={hits}, contacts={contacts}, shatters={shatters}, grains={grains}, grazes={grazes}; sources/s={(contacts + shatters + grazes) / 12f:F2}");
            Console.WriteLine($"new voices={voices}, audible(-34dB)={audible} ({audible / 12f:F2}/s), hit voices={hitVoices}, final checksum={game.StateChecksum()}");
            if (metalContacts.FirstOrDefault(c => c.Resonance is not null).Resonance is { } shape)
            {
                var samples = SoundAnalysis.ToFloats(ContactSound.Synthesize(ContactPair.MetalMetal, 1f, 7, shape));
                Console.WriteLine($"observed metal contacts={metalContacts.Count}, shape decay scale={shape.DecayScale:F4}, -20dB={SoundAnalysis.FallTime(samples, HitSound.SampleRate, -20f):F3}s");
            }
            var folder = Path.Combine(FindRoot(), "docs", "arena");
            File.WriteAllLines(Path.Combine(folder, $"sound_v7_{wear}_sources.csv"), rows);
            File.WriteAllLines(Path.Combine(folder, $"sound_v7_{wear}_voices.csv"), voiceRows);
        }
        var metal = SoundAnalysis.ToFloats(ContactSound.Synthesize(ContactPair.MetalMetal, 1f, 7));
        Console.WriteLine($"Legacy metal -20dB={SoundAnalysis.FallTime(metal, HitSound.SampleRate, -20f):F3}s");
        using var fixture = new BoxingGame(seed: 613);
        var blade = Weapon.Create(fixture.World, WeaponKind.Katana, Vector3.One, Quaternion.Identity);
        var half = blade.Body.Grid.OccupiedBounds()!.Value.HalfExtents;
        var dimensions = new[] { half.X * 2, half.Y * 2, half.Z * 2 }.OrderDescending().ToArray();
        var profile = ContactResonance.Of(dimensions[0], dimensions[1], dimensions[2], fixture.World.GetMass(blade.Body.BodyId), true);
        var ringing = SoundAnalysis.ToFloats(ContactSound.Synthesize(ContactPair.MetalMetal, 1f, 7, profile));
        Console.WriteLine($"Actual katana geometry: scale={profile.DecayScale:F4}, -20dB={SoundAnalysis.FallTime(ringing, HitSound.SampleRate, -20f):F3}s");
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CLAUDE.md")))
        {
            folder = folder.Parent;
        }
        return folder?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
