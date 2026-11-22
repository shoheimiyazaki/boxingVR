using System.Numerics;
using Aigis.Physics.Voxels;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class HybridCombinationImpactProbe
{
    [TestMethod]
    [DataRow(240)]
    [DataRow(480)]
    public void MeasureCombinationImpacts(int hz)
    {
        foreach (var gap in new[] { 0.58f, 0.62f, 0.66f, 0.74f, 0.82f, 0.90f })
        {
            foreach (var recovery in new[] { 0.06f, 0.12f, 0.20f })
            {
                foreach (var pattern in new[] { "RRR", "RLR", "LLL", "LRL" })
                {
                    Run(hz, gap, recovery, pattern);
                }
            }
        }
    }

    private static void Run(int hz, float gap, float recovery, string pattern)
    {
        var tuning = FighterTuning.HybridPunch with { StaminaFatigue = false };
        var passive = new GuardPosture("torso exposed", Vector3.Zero, new Vector3(.50f, -.38f, .14f));
        var corners = new BoxingCorners("combination impact", _ => new StandGuard(passive),
            Ring: RingLayout.DrillBoxing with { StartGap = gap }, VoxelGloves: true);
        using var game = new BoxingGame(tuning, seed: 1, corners: corners) { SubstepRate = hz };
        var dt = 1f / hz;
        var striker = game.Player;
        var victim = game.Cpu;
        var forward = Vector3.UnitZ;
        var guard = new FighterCommand(Vector3.Zero, forward,
            new HandCommand(new Vector3(.14f, .05f, .27f), false),
            new HandCommand(new Vector3(-.14f, .05f, .24f), false));
        var first = WhichHand(pattern[0]);
        var loaded = SetHand(guard, first, .13f);
        loaded = SetHand(loaded, first == Hand.Right ? Hand.Left : Hand.Right, .35f);
        var throwSteps = ToSteps(JointPunchSeriesTests.ThrowSeconds);
        var recoverSteps = ToSteps(recovery);
        var startRightGlove = striker.WieldedBody(Hand.Right);
        var startLeftGlove = striker.WieldedBody(Hand.Left);
        var startVictimParts = victim.AllParts.ToHashSet();
        Span<int> lastScore = [-1, -1];
        var stepIndex = 0;
        var startupContacts = 0;
        var startupHits = 0;
        for (var i = 0; i < ToSteps(.30f); i++)
        {
            Step(loaded);
            startupHits += game.RecentHits.Count(h => h.Attacker == striker);
            startupContacts += Contacts(startRightGlove, startVictimParts).Count() + Contacts(startLeftGlove, startVictimParts).Count();
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == striker && !h.Kick && !h.Elbow))
            {
                lastScore[(int)hit.Hand] = stepIndex;
            }
        }

        for (var index = 0; index < pattern.Length; index++)
        {
            var hand = WhichHand(pattern[index]);
            var start = (index == 0 ? loaded : guard).For(hand).TargetLocal.Z;
            var origin = striker.CenterOfMass;
            var initialSeparation = victim.ChestPosition.Z - striker.ChestPosition.Z;
            var peakFist = 0f;
            var peakFistComAdvance = 0f;
            var impulse = 0f;
            var maxImpulse = 0f;
            float? peakContactMs = null;
            float? peakContactAfterPreviousScoreMs = null;
            var touches = 0;
            var hits = 0;
            var totalEnergy = 0f;
            HitEvent? best = null;
            var bestComAdvance = 0f;
            var bestComVelocity = 0f;
            var bestShoulderVelocity = 0f;
            var bestMs = 0f;
            for (var i = 1; i <= throwSteps + recoverSteps; i++)
            {
                var u = Math.Min(1f, i / (float)throwSteps);
                var eased = u * u * (3f - 2f * u);
                var command = i <= throwSteps ? SetHand(guard, hand, start + (.72f - start) * eased) : guard;
                var comAdvance = striker.CenterOfMass.Z - origin.Z;
                var comVelocity = striker.CenterOfMassVelocity.Z;
                var chest = game.World.GetState(striker.Parts.Chest);
                var shoulderOffset = striker.ShoulderPosition(hand) - chest.Position;
                var shoulderVelocity = (chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulderOffset)).Z;
                var fist = striker.GloveVelocity(hand).Z;
                if (fist > peakFist)
                {
                    peakFist = fist;
                    peakFistComAdvance = comAdvance;
                }

                var glove = striker.WieldedBody(hand);
                var victimParts = victim.AllParts.Where(victim.Owns).ToHashSet();
                Step(command);
                foreach (var contact in Contacts(glove, victimParts))
                {
                    touches++;
                    var j = contact.B.BodyId == glove ? -contact.ImpulseOnB.Z : contact.ImpulseOnB.Z;
                    impulse += j;
                    if (j > maxImpulse)
                    {
                        maxImpulse = j;
                        peakContactMs = i * dt * 1000f;
                        var previousScore = lastScore[(int)hand];
                        peakContactAfterPreviousScoreMs = previousScore < 0 ? null : (stepIndex - previousScore) * dt * 1000f;
                    }
                }

                foreach (var hit in game.RecentHits)
                {
                    if (hit.Attacker != striker || hit.Kick || hit.Elbow)
                    {
                        continue;
                    }

                    lastScore[(int)hit.Hand] = stepIndex;
                    if (hit.Hand != hand)
                    {
                        continue;
                    }

                    hits++;
                    totalEnergy += hit.Energy;
                    if (best is null || hit.Energy > best.Value.Energy)
                    {
                        best = hit;
                        bestComAdvance = comAdvance;
                        bestComVelocity = comVelocity;
                        bestShoulderVelocity = shoulderVelocity;
                        bestMs = i * dt * 1000f;
                    }
                }
            }

            Console.WriteLine(FormattableString.Invariant(
                $"LOAD,{hz},{gap:F2},{recovery * 1000:F0},{(float)hz / (throwSteps + recoverSteps):F4},{pattern},{index + 1},{hand},{initialSeparation:F4},{peakFist:F4},{peakFistComAdvance * 100:F3},{touches},{impulse:F5},{maxImpulse:F5},{peakContactMs:F3},{peakContactAfterPreviousScoreMs:F3},{hits},{totalEnergy:F4},{best?.Zone},{best?.Speed:F4},{best?.ImpactMass:F4},{best?.BodyShare:F5},{best?.Momentum:F4},{best?.Energy:F4},{best?.Damage:F4},{bestMs:F3},{bestComAdvance * 100:F3},{bestComVelocity:F4},{bestShoulderVelocity:F4},{startupContacts},{startupHits},{striker.State},{victim.State}"));
        }

        int ToSteps(float seconds) => (int)MathF.Round(seconds * hz);
        void Step(in FighterCommand command)
        {
            game.Step(new BoxingInput(command), dt);
            stepIndex++;
        }

        IEnumerable<VoxelContactResult> Contacts(int? glove, HashSet<int> victimParts)
            => game.RecentCuts.Where(c => c.ApproachSpeed > 0f &&
                ((c.A.BodyId == glove && victimParts.Contains(c.B.BodyId)) ||
                 (c.B.BodyId == glove && victimParts.Contains(c.A.BodyId))));
    }

    private static Hand WhichHand(char c) => c == 'R' ? Hand.Right : Hand.Left;

    private static FighterCommand SetHand(in FighterCommand command, Hand hand, float z)
    {
        var target = command.For(hand);
        target = target with { TargetLocal = target.TargetLocal with { Z = z } };
        return hand == Hand.Right ? command with { Right = target } : command with { Left = target };
    }
}
