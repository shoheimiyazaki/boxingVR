using System.Numerics;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
public sealed class JointPunchSeriesTests
{
    [TestMethod]
    [DataRow(240, true)]
    [DataRow(480, true)]
    [DataRow(240, false)]
    public void MeasureRepeatedPunches(int hz, bool grounded)
    {
        var dt = 1f / hz;
        var same = RunSeries(alternate: false, grounded, dt);
        var alternate = RunSeries(alternate: true, grounded, dt);
        Print("same", same);
        Print("alternate", alternate);
        Assert.IsTrue(same.First.Speed > same.Second.Speed && same.Second.Speed > 0f);
        Assert.IsTrue(alternate.Second.Speed > 0f);
        Assert.AreEqual(FighterState.Standing, same.State);
        Assert.AreEqual(FighterState.Standing, alternate.State);
        Assert.AreEqual(FighterState.Standing, same.FinalState);
        Assert.AreEqual(FighterState.Standing, alternate.FinalState);
        if (grounded)
        {
            Assert.IsTrue(same.Second.Waist < same.First.Waist);
            Assert.IsTrue(same.Third.Waist < same.Second.Waist);
            Assert.IsTrue(same.Third.Speed < same.Second.Speed);
            Assert.IsTrue(alternate.Second.Speed > same.Second.Speed);
            Assert.IsTrue(alternate.Third.Speed > same.Third.Speed);
            Assert.IsTrue(alternate.Legs > same.Legs);
            Assert.IsTrue(alternate.TotalWork > same.TotalWork);
        }

        static void Print(string name, Series series) => Console.WriteLine(FormattableString.Invariant(
            $"{name}: fist {series.First.Speed:F3}/{series.Second.Speed:F3} m/s, shoulder {series.First.Shoulder:F3}/{series.Second.Shoulder:F3} m/s, pelvis sweep {series.First.Waist:F2}/{series.Second.Waist:F2} deg, pelvis yaw at onset {series.First.InitialYaw:F2}/{series.Second.InitialYaw:F2} deg, pelvis yaw speed at fist peak {series.First.PeakYawSpeed:F2}/{series.Second.PeakYawSpeed:F2} rad/s, chest sweep {series.First.Chest:F2}/{series.Second.Chest:F2} deg, work trunk/legs/arms {series.Trunk:F3}/{series.Legs:F3}/{series.Arms:F3} J, state={series.State}, third fist={series.Third.Speed:F3} m/s, third pelvis sweep={series.Third.Waist:F2} deg, third state={series.FinalState}"));
    }

    [TestMethod]
    [DataRow(240)]
    [DataRow(480)]
    public void MeasureHybridRepeatedPunches(int hz)
    {
        var tuning = FighterTuning.HybridPunch;
        var same = RunSeries(alternate: false, grounded: true, 1f / hz, tuning);
        var alternating = RunSeries(alternate: true, grounded: true, 1f / hz, tuning);
        Console.WriteLine(FormattableString.Invariant(
            $"same speed={same.First.Speed:F3}/{same.Second.Speed:F3}/{same.Third.Speed:F3}, waist={same.First.Waist:F2}/{same.Second.Waist:F2}/{same.Third.Waist:F2}, alternate speed={alternating.Second.Speed:F3}/{alternating.Third.Speed:F3}, work same/alt={same.TotalWork:F2}/{alternating.TotalWork:F2} J, legs same/alt={same.Legs:F2}/{alternating.Legs:F2} J"));
        Assert.AreEqual(FighterState.Standing, same.FinalState);
        Assert.AreEqual(FighterState.Standing, alternating.FinalState);
        Assert.IsTrue(same.Second.Speed < same.First.Speed && same.Third.Speed < same.First.Speed);
        Assert.IsTrue(alternating.Second.Speed > same.Second.Speed && alternating.Third.Speed > same.Third.Speed);
        Assert.IsGreaterThan(same.TotalWork, alternating.TotalWork);
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void MeasureMomentumDriveInABotMatch()
    {
        var corners = new BoxingCorners("joint drive probe", s => BotCatalog.Create("swarmer", s),
            s => BotCatalog.Create("champion", s), VoxelGloves: true);
        var momentum = FighterTuning.MomentumPunch;
        var wide = momentum.Footwork with { StanceWidth = 0.45f, StanceLength = 0.70f };
        var quick = momentum.Footwork with { CaptureMargin = 0.08f, StepTimeMin = 0.10f, StepTimeMax = 0.18f };
        (string Name, FighterTuning Tuning)[] settings =
        [
            ("adaptive", FighterTuning.AdaptivePunch),
            ("momentum", momentum),
            ("hybrid 50% direct contact", FighterTuning.HybridPunch with { JointLegacyGloveImpact = false }),
            ("hybrid 50%", FighterTuning.HybridPunch),
            ("hybrid 80%", FighterTuning.HybridPunch with { JointSupportAssist = 0.8f }),
            ("hybrid 100%", FighterTuning.HybridPunch with { JointSupportAssist = 1f }),
            ("wide stance", momentum with { Footwork = wide }),
            ("quick recovery step", momentum with { Footwork = quick }),
            ("smaller foot pivot", momentum with { Footwork = momentum.Footwork with { PivotShare = 0.35f } }),
        ];
        foreach (var (name, tuning) in settings)
        {
            using var game = new BoxingGame(tuning, seed: 1, corners: corners);
            int hits = 0;
            float firstDown = float.NaN;
            const float duration = 3f;
            const int framesPerSecond = 60;
            const int frames = (int)(duration * framesPerSecond);
            for (var i = 0; i < frames; i++)
            {
                game.Step(default, 1f / framesPerSecond);
                hits += game.RecentHits.Count;
                if (float.IsNaN(firstDown) && (game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing))
                {
                    firstDown = (i + 1f) / framesPerSecond;
                }
            }

            Console.WriteLine(FormattableString.Invariant(
                $"{name}, {duration:F0} s, first down={firstDown:F2} s, scored hits={hits}, final state={game.Player.State}/{game.Cpu.State}"));
        }
    }

    internal readonly record struct Strike(float Speed, float Shoulder, Vector3 Toward, Vector3 Span, float InitialYaw, float PeakYawSpeed,
        float PeakTime, FighterState EndState)
    {
        public float Waist => Toward.X;
        public float Chest => Toward.Z;
    }

    internal readonly record struct Series(Strike First, Strike Second, Strike Third, float Trunk, float Legs, float Arms,
        FighterState State, FighterState FinalState)
    {
        public float TotalWork => Trunk + Legs + Arms;
    }

    internal const float ThrowSeconds = 0.13f;

    internal static Series RunSeries(bool alternate, bool grounded, float dt, FighterTuning? preset = null, float guardSeconds = 0.06f)
    {
        using var world = new PhysicsWorld(grounded ? null : Vector3.Zero, conserveAngularMomentum: true);
        if (grounded)
        {
            world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        }

        var tuning = (preset ?? FighterTuning.MomentumPunch) with
        {
            StaminaFatigue = false,
            NoGroundFallTime = grounded ? FighterTuning.MomentumPunch.NoGroundFallTime : 3f,
        };
        var fighter = Fighter.Spawn(world, tuning, "series", Vector3.Zero, 0f);
        var guard = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(new Vector3(0.14f, 0.05f, 0.27f), false),
            new HandCommand(new Vector3(-0.14f, 0.05f, 0.24f), false));
        var loaded = guard with
        {
            Left = guard.Left with { TargetLocal = guard.Left.TargetLocal with { Z = 0.35f } },
            Right = guard.Right with { TargetLocal = guard.Right.TargetLocal with { Z = 0.13f } },
        };
        void Step(in FighterCommand command)
        {
            fighter.ApplyControl(command, dt);
            world.Step(dt);
        }

        for (var i = 0; i < MathF.Round(0.30f / dt); i++)
        {
            Step(loaded);
        }

        var trunk = fighter.RegionWork(MuscleRegion.Trunk);
        var legs = fighter.RegionWork(MuscleRegion.Legs);
        var arms = fighter.RegionWork(MuscleRegion.LeftArm) + fighter.RegionWork(MuscleRegion.RightArm);
        float Yaw(int part) => BodyFrame.YawOf(Vector3.Transform(Vector3.UnitZ, world.GetState(part).Orientation), 0f);
        var abdomen = fighter.Parts.Abdomen ?? throw new InvalidOperationException("The series probe requires a split torso.");
        Vector3 ReadYaw() => new(Yaw(fighter.Parts.Pelvis), Yaw(abdomen), Yaw(fighter.Parts.Chest));

        Strike Punch(Hand hand, FighterCommand start)
        {
            var sign = hand == Hand.Right ? 1f : -1f;
            var startYaw = ReadYaw();
            var peak = 0f;
            var shoulder = 0f;
            var peakYawSpeed = 0f;
            var peakTime = 0f;
            var mostForward = Vector3.Zero;
            var mostBackward = Vector3.Zero;
            var count = (int)MathF.Round(ThrowSeconds / dt);
            var initial = start.For(hand);
            var target = guard.For(hand).TargetLocal with { Z = 0.72f };
            for (var i = 1; i <= count; i++)
            {
                var u = (float)i / count;
                var u2 = u * u * (3f - 2f * u);
                var command = initial with { TargetLocal = Vector3.Lerp(initial.TargetLocal, target, u2) };
                Step(hand == Hand.Right ? guard with { Right = command } : guard with { Left = command });
                var forward = fighter.GloveVelocity(hand).Z;
                if (forward > peak)
                {
                    peak = forward;
                    peakTime = i * dt;
                    peakYawSpeed = sign * world.GetState(fighter.Parts.Pelvis).AngularVelocity.Y;
                    var state = world.GetState(fighter.Parts.Chest);
                    shoulder = (state.LinearVelocity + Vector3.Cross(state.AngularVelocity, fighter.ShoulderPosition(hand) - state.Position)).Z;
                }

                var yaw = ReadYaw();
                var travel = sign * new Vector3(BodyFrame.DeltaAngle(startYaw.X, yaw.X),
                    BodyFrame.DeltaAngle(startYaw.Y, yaw.Y), BodyFrame.DeltaAngle(startYaw.Z, yaw.Z));
                mostForward = Vector3.Max(mostForward, travel);
                mostBackward = Vector3.Min(mostBackward, travel);
            }

            const float degreesPerRadian = 180f / MathF.PI;
            return new Strike(peak, shoulder, mostForward * degreesPerRadian,
                (mostForward - mostBackward) * degreesPerRadian, sign * startYaw.X * degreesPerRadian, peakYawSpeed,
                peakTime, fighter.State);
        }

        void Recover()
        {
            for (var i = 0; i < MathF.Round(guardSeconds / dt); i++)
            {
                Step(guard);
            }
        }

        var first = Punch(Hand.Right, loaded);
        Recover();
        var second = Punch(alternate ? Hand.Left : Hand.Right, guard);
        var trunkWork = fighter.RegionWork(MuscleRegion.Trunk) - trunk;
        var legWork = fighter.RegionWork(MuscleRegion.Legs) - legs;
        var armWork = fighter.RegionWork(MuscleRegion.LeftArm) + fighter.RegionWork(MuscleRegion.RightArm) - arms;
        var state = fighter.State;
        Recover();
        var third = Punch(Hand.Right, guard);
        return new Series(first, second, third, trunkWork, legWork, armWork, state, fighter.State);
    }
}
