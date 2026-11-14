using System.Numerics;
using MiniRing.Desktop;
using MiniRing.Replay;

namespace MiniRing.Tests;

[TestClass]
public class GamepadIntentTests
{
    [TestMethod]
    public void ButtonsAndHalfStick_MapToIntent()
    {
        var intent = GamepadIntent.Map(new Vector2(0f, -0.5f), true, false, true, 0f);
        Assert.IsTrue(intent.Light);
        Assert.IsFalse(intent.Heavy);
        Assert.IsTrue(intent.Evade);
        Assert.AreEqual(-0.5f, intent.World!.Value.Y, 0.001f);
    }

    [TestMethod]
    public void DeadZoneAndDiagonal_AreBounded()
    {
        Assert.AreEqual(Vector2.Zero, GamepadIntent.Stick(new Vector2(0.05f, 0f)));
        Assert.AreEqual(1f, GamepadIntent.Stick(new Vector2(1f, 1f)).Length(), 0.001f);
        Assert.AreEqual(0.5f, GamepadIntent.Stick(new Vector2(0.5f, 0f)).Length(), 0.001f);
    }

    [TestMethod]
    public void TapAndHold_UseTheSameIntentControlAsKeyboard()
    {
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.2f,
            new(0f, 0.06f, 0.27f), new(0f, 0f, 0.27f), true, new(0f, 0.06f, 0f));
        var pad = new IntentControl();
        var keyboard = new IntentControl();
        for (var step = 0; step < 60; step++)
        {
            var light = step is 1 or 2 or >= 15 and < 30;
            pad.Input = GamepadIntent.Map(Vector2.Zero, light, false, false, 0f);
            keyboard.Input = new FighterIntent(Vector2.Zero, Light: light, World: Vector2.Zero);
            Assert.AreEqual(keyboard.Read(sense, 1f / 60f), pad.Read(sense, 1f / 60f));
        }
        Assert.AreEqual(2, pad.AttacksStarted);
    }

    [TestMethod]
    public void GamepadCommands_RecordAndReplayEveryChecksum()
    {
        using var game = new BoxingGame(seed: 31);
        var control = new IntentControl();
        using var stream = new MemoryStream();
        var checksums = new List<ulong>();
        using (var recorder = new ReplayRecorder(stream, game, "gamepad-test"))
        {
            for (var step = 0; step < 120; step++)
            {
                control.Input = GamepadIntent.Map(new Vector2(0.5f, -0.2f), step is >= 10 and < 15,
                    step is >= 35 and < 55, step is >= 80 and < 90, 0.3f);
                recorder.Step(game, new BoxingInput(control.Read(game.PlayerSense, 1f / 60f)), 1f / 60f);
                checksums.Add(game.StateChecksum());
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        using var replay = new ReplayPlayer(data);
        foreach (var checksum in checksums)
        {
            replay.StepOnce();
            Assert.AreEqual(checksum, replay.Game.StateChecksum());
        }
    }
}
