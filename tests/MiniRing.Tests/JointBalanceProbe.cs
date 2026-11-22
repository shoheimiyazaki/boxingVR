using System.Numerics;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class JointBalanceProbe
{
    [TestMethod]
    [DataRow(0f, 0f, 0f)]
    [DataRow(0.5f, 1f, 0f)]
    [DataRow(0.5f, 1f, -4f)]
    [DataRow(0.5f, 1f, -1f)]
    [DataRow(0.5f, 1f, 1f)]
    [DataRow(0.5f, 1f, 4f)]
    public void TraceGuardAndFirstStep(float hipShare, float captureShare, float ankleGain)
    {
        foreach (var move in new[] { 0f, 0.4f })
        {
            using var world = new PhysicsWorld(conserveAngularMomentum: true);
            world.AddStaticBox(new Vector3(0f, -1f, 0f), new Vector3(7f, 1f, 7f));
            var fighter = Fighter.Spawn(world, FighterTuning.MomentumPunch with
            {
                JointStanceHipShare = hipShare,
                JointStanceAnkleGain = ankleGain,
                Footwork = FighterTuning.MomentumPunch.Footwork with { CapturePlacementShare = captureShare },
            }, "balance", Vector3.Zero, 0f);
            var idle = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(new Vector3(0.15f, 0.08f, 0.24f), false),
                new HandCommand(new Vector3(-0.15f, 0.08f, fighter.ArmReach * 0.48f), false));
            const float dt = 1f / 240f;
            for (var i = 1; i <= 960; i++)
            {
                var command = i * dt < 0.75f ? idle : idle with { MoveWorld = Vector3.UnitZ * move };
                fighter.ApplyControl(command, dt);
                world.Step(dt);
                if (i != 960 && fighter.State == FighterState.Standing && (hipShare != 0f || captureShare != 0f || i % 60 != 0))
                {
                    continue;
                }

                var pelvis = world.GetState(fighter.Parts.Pelvis);
                var left = world.GetState(fighter.Parts.LeftFoot);
                var right = world.GetState(fighter.Parts.RightFoot);
                var pitch = Vector3.Transform(Vector3.UnitY, pelvis.Orientation).Z;
                var ankle = Vector3.Transform(Vector3.UnitY, left.Orientation).Z;
                var footContacts = world.Contacts().Count(c => c.StaticB && c.Normal.Y >= 0.5f
                    && (c.A == fighter.Parts.LeftFoot || c.A == fighter.Parts.RightFoot
                        || c.A == fighter.ToeId(Footwork.Lead) || c.A == fighter.ToeId(Footwork.Rear)));
                Console.WriteLine(FormattableString.Invariant(
                    $"hipShare={hipShare:F2} captureShare={captureShare:F2} ankleGain={ankleGain:F2} move={move:F1} t={i * dt:F3} pelvis={pelvis.Position:F3} com={fighter.CenterOfMass:F3} comVz={fighter.CenterOfMassVelocity.Z:F3} upZ={pitch:F3} left={left.Position:F3} right={right.Position:F3} footUpZ={ankle:F3} contacts={footContacts} state={fighter.State}"));
                if (fighter.State != FighterState.Standing)
                {
                    break;
                }
            }
        }
    }

    [TestMethod]
    public void MatchAndRecoverySweep()
    {
        const int hz = 60;
        var candidate = FighterTuning.MomentumPunch;
        var recovery = candidate.Footwork;
        var baseline = candidate with { JointStanceHipShare = 0f, JointStanceAnkleGain = 0f, Footwork = recovery with { CapturePlacementShare = 0f } };
        var higherStep = recovery with { StepLift = 0.12f };
        (string Name, FighterTuning Tuning)[] settings =
        [
            ("previous", baseline),
            ("current", candidate),
            ("hip 85%", baseline with { JointStanceHipShare = 0.85f, Footwork = recovery }),
            ("hip 100%", baseline with { JointStanceHipShare = 1f, Footwork = recovery }),
            ("ankle -1", candidate with { JointStanceAnkleGain = -1f }),
            ("hip and placement only", candidate with { JointStanceAnkleGain = 0f }),
            ("ankle +4", candidate with { JointStanceAnkleGain = 4f }),
            ("smaller foot pivot", candidate with { Footwork = recovery with { PivotShare = 0.35f } }),
            ("higher step", baseline with { JointStanceHipShare = 1f, Footwork = higherStep }),
            ("longer step", baseline with { JointStanceHipShare = 1f, Footwork = higherStep with { StepTimeMin = 0.21f, StepTimeMax = 0.35f } }),
            ("faster joint response", baseline with { JointStanceHipShare = 1f, Footwork = higherStep, LegServoHz = 34f }),
        ];
        var corners = new BoxingCorners("balance probe", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), VoxelGloves: true);
        foreach (var (name, tuning) in settings)
        {
            foreach (var seed in new[] { 1, 2 })
            {
                using var game = new BoxingGame(tuning, seed, corners);
                var firstDown = float.NaN;
                var hits = 0;
                for (var i = 1; i <= 4 * hz; i++)
                {
                    game.Step(default, 1f / hz);
                    hits += game.RecentHits.Count;
                    if (float.IsNaN(firstDown) && (game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing))
                    {
                        firstDown = i / (float)hz;
                    }
                }

                Console.WriteLine(FormattableString.Invariant($"{name}, seed={seed}, first down={firstDown:F3}s, hits={hits}"));
            }
        }
    }
}
