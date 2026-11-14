using System.Numerics;
using System.Text;
using MiniRing.Replay;

namespace MiniRing.Tests;

[TestClass]
public sealed class IntentReplayTests
{
    [TestMethod]
    public void ChargedIntent_RecordsAndReplaysEveryChecksum()
    {
        using var game = new BoxingGame(seed: 31);
        var control = new IntentControl();
        var stream = new MemoryStream();
        var checksums = new List<ulong>();
        using (var recorder = new ReplayRecorder(stream, game, "intent-test"))
        {
            for (var step = 0; step < 480; step++)
            {
                var phase = step % 120;
                control.Input = new FighterIntent(phase < 60 ? Vector2.UnitY : -Vector2.UnitY,
                    Light: phase is >= 20 and < 24, Heavy: phase is >= 50 and < 75, Evade: phase is >= 90 and < 100);
                recorder.Step(game, new BoxingInput(control.Read(game.PlayerSense, 1f / 60f)), 1f / 60f);
                checksums.Add(game.StateChecksum());
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.IsTrue(data.Steps.Any(s => s.Input.Player.TwistBias != 0f));
        Assert.IsTrue(data.Steps.Any(s => s.Input.Player.Right.Spring > 0f));
        using var replay = new ReplayPlayer(data);
        foreach (var checksum in checksums)
        {
            replay.StepOnce();
            Assert.AreEqual(checksum, replay.Game.StateChecksum());
        }
    }

    [TestMethod]
    public void VersionFour_RoundTripsFullHandCommand()
    {
        using var game = new BoxingGame();
        var output = new MemoryStream();
        var command = new FighterCommand(Vector3.UnitZ, Vector3.UnitZ,
            new(new(0.1f, 0.2f, 0.3f), true, 0.7f, Quaternion.Identity, true),
            new(new(-0.1f, 0.2f, 0.3f), false, 1f), new(0f, -0.1f, 0f), 0.2f);
        using (var recorder = new ReplayRecorder(output, game, "test"))
        {
            recorder.Step(game, new BoxingInput(command), 1f / 60f);
        }

        var data = ReplayFile.Read(new MemoryStream(output.ToArray()));
        Assert.AreEqual(command, data.Steps[0].Input.Player);
    }

    [TestMethod]
    public void VersionThree_ReadsWithZeroSpringAndTwist()
    {
        using var game = new BoxingGame();
        var output = new MemoryStream();
        using (var recorder = new ReplayRecorder(output, game, "test"))
        {
            recorder.Step(game, new BoxingInput(default), 1f / 60f);
        }

        // v6 の設定を除いた見出しと、v4/v5 の追加を除いたステップから v3 を作る。
        var bytes = output.ToArray();
        var header = ReplayFile.Read(new MemoryStream(bytes)).Header;
        using var v3 = new MemoryStream();
        using (var writer = new BinaryWriter(v3, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("MRRP"u8.ToArray());
            writer.Write(3);
            writer.Write(header.Seed);
            writer.Write(header.TuningJson);
            writer.Write(header.Host);
            writer.Write(header.AppVersion);
            writer.Write(header.CreatedUtc.Ticks);
            writer.Write(header.CpuName);
            writer.Write(header.CpuStyleJson);
            writer.Write(header.CpuLogic);
            writer.Write(header.Physics);
            const int v3StepBytes = 4 + 1 + (15 * 4) + 3;
            const int extraBytes = 26;
            writer.Write(bytes.AsSpan(bytes.Length - v3StepBytes - extraBytes, v3StepBytes));
        }

        v3.Position = 0;
        var data = ReplayFile.Read(v3);
        Assert.HasCount(1, data.Steps);
        Assert.AreEqual(0f, data.Steps[0].Input.Player.TwistBias);
        Assert.AreEqual(0f, data.Steps[0].Input.Player.Left.Spring);
        Assert.AreEqual(0f, data.Steps[0].Input.Player.Right.Spring);
    }
}
