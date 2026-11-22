using System.Globalization;
using Aigis.Physics.Voxels;
using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
[DoNotParallelize]
public sealed class WvcMatchPacingProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Measure()
    {
        var args = (Environment.GetEnvironmentVariable("MINIRING_WVC_CASE") ?? "albert ramon 9").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var a = CharacterRoster.Get(args[0]);
        var b = CharacterRoster.Get(args[1]);
        var seed = int.Parse(args[2], CultureInfo.InvariantCulture);
        var red = RosterLeague.ContenderOf(a, b.Id, null);
        var blue = RosterLeague.ContenderOf(b, a.Id, null);
        var wvc = Organizations.Get("wvc");
        var corners = TvShowRules.Stage(VideoMaker.WithCharacters(VideoMaker.Corners(
            red, blue, seed, a.Wear, b.Wear, a.Weapon, b.Weapon, blue.Create.Invoke, red.Create.Invoke), a, b), wvc);
        var rules = TvShowRules.Rules(3, 20f, wvc);
        if (args.Length == 5)
        {
            rules = rules with
            {
                ConcussionSpinScale = float.Parse(args[3], CultureInfo.InvariantCulture) / FighterTuning.Default.ConcussionSpin,
                LimbBendStrengthScale = float.Parse(args[4], CultureInfo.InvariantCulture),
            };
        }

        var spin = FighterTuning.Default.ConcussionSpin * rules.ConcussionSpinScale;
        using var game = new BoxingGame(seed: seed, corners: corners, rules: rules, mood: wvc.ArenaMood);
        var rounds = new Dictionary<int, float>();
        var fractured = new HashSet<int>();
        var breaks = new List<BodyPartKind>();
        const float dt = 1f / 60f;
        var active = 0f;
        var round = 1;
        var hits = 0;
        var switches = 0;
        var lastCleanAt = float.NegativeInfinity;
        var lastRed = false;
        var maxNeck = 0f;
        var samples = 0;
        VoxelContact.BendObserved = sample =>
        {
            if (sample.Worst is not { } worst)
            {
                return;
            }

            var part = game.Player.Voxels?.PartOf(sample.Beam.BodyId) ?? game.Cpu.Voxels?.PartOf(sample.Beam.BodyId);
            if (part?.Kind is BodyPartKind.Forearm or BodyPartKind.UpperArm or BodyPartKind.Shin or BodyPartKind.Thigh)
            {
                samples++;
                rounds[round] = MathF.Max(rounds.GetValueOrDefault(round), worst.Ratio);
            }
            else if (part?.Kind == BodyPartKind.Head)
            {
                maxNeck = MathF.Max(maxNeck, worst.Ratio);
            }
        };

        var time = 0f;
        try
        {
            for (; time < rules.TotalSeconds + 9f && !game.MatchOver; time += dt)
            {
                round = game.MatchRound;
                var live = !game.Resting && !game.Counting && !game.Holding && !game.RoundOver;
                if (!live)
                {
                    lastCleanAt = float.NegativeInfinity;
                }
                else
                {
                    active += dt;
                }

                game.Step(default, dt);
                foreach (var hit in game.RecentHits)
                {
                    hits++;
                    if (hit.Zone is HitZone.Guard or HitZone.Elbow or HitZone.Shoulder || !live)
                    {
                        continue;
                    }

                    var isRed = hit.Attacker == game.Player;
                    if (isRed != lastRed && time - lastCleanAt <= 1f)
                    {
                        switches++;
                    }

                    lastRed = isRed;
                    lastCleanAt = time;
                }

                foreach (var broken in game.BrokenBones)
                {
                    fractured.Add(round);
                    breaks.Add(broken.Part);
                }
            }
        }
        finally
        {
            VoxelContact.BendObserved = null;
        }

        var maxima = string.Join("/", Enumerable.Range(1, game.MatchRound)
            .Select(r => rounds.GetValueOrDefault(r).ToString("F3", CultureInfo.InvariantCulture)));
        var result = FormattableString.Invariant($"{a.Id}/{b.Id}/{seed} spin={spin:F2} seconds={time:F1} active={active:F1} hits={hits} switches={switches} R={game.MatchRound} sampled={samples} limb={maxima} neck={maxNeck:F3} brokenRounds={fractured.Count} parts={string.Join(',', breaks)} finish={game.Decision ?? game.Message}");
        TestContext.WriteLine(result);
        if (Environment.GetEnvironmentVariable("MINIRING_WVC_RESULT") is { } output)
        {
            File.WriteAllText(output, result + Environment.NewLine);
        }
    }
}
