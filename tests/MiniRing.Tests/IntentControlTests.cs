using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
public sealed class IntentControlTests
{
    private const float Dt = 1f / 60f;
    private static FighterSense Sense => new(new BodyFrame(Vector3.Zero, 0f), 0.2f,
        new(0f, 0.06f, 0.27f), new(0f, 0f, 0.27f), true, new(0f, 0.06f, 0f));

    [TestMethod]
    public void Press_ChangesTargetOnSameStep()
    {
        var control = new IntentControl();
        var guard = control.Read(Sense, Dt);
        control.Input = new FighterIntent(Vector2.Zero, Light: true);
        var pressed = control.Read(Sense, Dt);
        Assert.AreNotEqual(guard.Left.TargetLocal, pressed.Left.TargetLocal);
        Assert.AreEqual(Technique.Jab, control.CurrentTechnique);
    }

    [TestMethod]
    public void ForwardHeavy_SelectsCrossAndMovesFeet()
    {
        var control = new IntentControl { Input = new FighterIntent(Vector2.UnitY, Heavy: true) };
        var command = control.Read(Sense, Dt);
        Assert.AreEqual(Technique.Cross, control.CurrentTechnique);
        Assert.IsGreaterThan(0f, command.MoveWorld.Z);
    }

    [TestMethod]
    public void Hold_DeepensLoad_ReleaseLaunches()
    {
        var control = new IntentControl { Input = new FighterIntent(Vector2.Zero, Heavy: true) };
        var first = control.Read(Sense, Dt);
        FighterCommand loaded = default;
        for (var i = 0; i < 20; i++) { loaded = control.Read(Sense, Dt); }
        Assert.IsLessThan(first.TwistBias, loaded.TwistBias);
        Assert.IsLessThan(first.Right.TargetLocal.Z, loaded.Right.TargetLocal.Z);
        control.Input = default;
        var launch = control.Read(Sense, Dt);
        Assert.AreEqual(1f, launch.Right.Spring);
        Assert.IsGreaterThan(loaded.Right.TargetLocal.Z, launch.Right.TargetLocal.Z);
    }

    [TestMethod]
    public void BackEvade_Sways_AndInterruptsAttack()
    {
        var control = new IntentControl { Input = new FighterIntent(Vector2.Zero, Heavy: true) };
        control.Read(Sense, Dt);
        control.Input = new FighterIntent(-Vector2.UnitY, Evade: true);
        var command = control.Read(Sense, Dt);
        Assert.AreEqual(Defense.Sway, control.CurrentDefense);
        Assert.IsNull(control.CurrentTechnique);
        Assert.IsLessThan(0f, command.HeadOffset.Z);
        Assert.AreEqual(0f, command.Right.Spring);
    }

    [TestMethod]
    public void ForwardEvade_ChoosesHookDefense()
    {
        var sense = Sense with { OpponentRight = new GloveSense(new(0.1f, 0.06f, 0.1f), new(-1f, 0f, -0.1f)) };
        var control = new IntentControl { Input = new FighterIntent(Vector2.UnitY, Evade: true) };
        control.Read(sense, Dt);
        Assert.IsTrue(control.CurrentDefense is Defense.Weave or Defense.Duck);
    }

    [TestMethod]
    public void NeutralEvade_Blocks_AndIdleGuards()
    {
        var control = new IntentControl { Input = new FighterIntent(Vector2.Zero, Evade: true) };
        var block = control.Read(Sense, Dt);
        Assert.AreEqual(Defense.Block, control.CurrentDefense);
        Assert.AreEqual(Vector3.Zero, block.MoveWorld);
        Assert.AreEqual(0f, block.Left.Spring);
    }

    [TestMethod]
    public void Taps_BufferAndConnectOneTwo()
    {
        var control = new IntentControl { Input = new FighterIntent(Vector2.Zero, Light: true) };
        control.Read(Sense, Dt);
        control.Input = default;
        control.Read(Sense, Dt);
        control.Input = new FighterIntent(Vector2.Zero, Heavy: true);
        control.Read(Sense, Dt);
        control.Input = default;
        for (var i = 0; i < 30; i++) { control.Read(Sense, Dt); }
        Assert.AreEqual(2, control.AttacksStarted);
        Assert.AreEqual("1-2", control.LastCombination);
    }

    [TestMethod]
    public void InvalidInput_FailsAtBoundary()
    {
        var control = new IntentControl();
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Input = new FighterIntent(new(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Read(Sense, float.NaN));
    }

    [TestMethod]
    public void Movement_UsesOpponentDirectionBeforeBodyTurns()
    {
        var sense = Sense with { Self = new BodyFrame(Vector3.Zero, MathF.PI / 2f) };
        var control = new IntentControl { Input = new FighterIntent(Vector2.UnitY) };
        var command = control.Read(sense, Dt);
        Assert.IsGreaterThan(0f, command.MoveWorld.Z);
        Assert.AreEqual(0f, command.MoveWorld.X, 1e-5f);
    }

    [TestMethod]
    public void HoldingEvade_DoesNotRestartTheInterruptedAttack()
    {
        var control = new IntentControl { Input = new FighterIntent(Vector2.UnitY, Heavy: true) };
        control.Read(Sense, Dt);
        control.Input = new FighterIntent(-Vector2.UnitY, Heavy: true, Evade: true);
        for (var i = 0; i < 60; i++)
        {
            var command = control.Read(Sense, Dt);
            Assert.IsNull(control.CurrentTechnique);
            Assert.AreEqual(0f, command.Right.Spring);
        }
    }

    [TestMethod]
    public void OneHandCounter_DoesNotThrowForOtherAttackButton()
    {
        var style = BotCatalog.Boxer with
        {
            Defenses = DefenseBook.Default with { Sway = DefenseBook.Default.Sway with { CounterStraight = "1" } },
        };
        var control = new IntentControl(style: style) { Input = new FighterIntent(-Vector2.UnitY, Evade: true) };
        control.Read(Sense, Dt);
        control.Input = new FighterIntent(Vector2.Zero, Heavy: true);
        control.Read(Sense, Dt);
        Assert.AreEqual(Technique.Cross, control.CurrentTechnique);
    }

    [TestMethod]
    public void Script_ValidatesOrderAndSamplesHeldStates()
    {
        Assert.Throws<ArgumentException>(() => new IntentScript([new(1f), new(0f)]));
        Assert.Throws<ArgumentException>(() => new IntentScript([new(0f, X: float.NaN)]));
        var script = new IntentScript([new(0f), new(1f, Heavy: true), new(2f)]);
        Assert.IsFalse(script.At(0.9f).Heavy);
        Assert.IsTrue(script.At(1.5f).Heavy);
        Assert.IsFalse(script.At(2f).Heavy);
    }
}
