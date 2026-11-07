using System.Numerics;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// 自動記録 → 再生で、試合が最初から完全に（ビット単位で）再現されることを確かめる。
/// </summary>
[TestClass]
public sealed class ReplayTests
{
    [TestMethod]
    public void RecordedMatch_ReplaysBitForBit()
    {
        var (bytes, finalChecksum, steps) = RecordMatch(seed: 5, seconds: 20f, variableDt: false);

        var data = ReplayFile.Read(new MemoryStream(bytes));
        Assert.AreEqual(steps, data.Steps.Count);
        Assert.IsGreaterThan(10, data.Checksums.Count);

        using var player = new ReplayPlayer(data);
        while (!player.Finished)
        {
            player.StepOnce();
        }

        Assert.IsNull(player.FirstDivergence, $"diverged at step {player.FirstDivergence}");
        Assert.AreEqual(data.Checksums.Count, player.ChecksumsMatched);
        Assert.AreEqual(finalChecksum, player.Game.StateChecksum(), "the final state must be identical");
    }

    [TestMethod]
    public void VrStyleVariableTimesteps_AlsoReplayExactly()
    {
        // VR はフレームごとに時間刻みが変わる（72〜120Hz、ASW）。刻みごと記録するので再現できる。
        var (bytes, finalChecksum, _) = RecordMatch(seed: 9, seconds: 8f, variableDt: true);

        var result = ReplayPlayer.Verify(ReplayFile.Read(new MemoryStream(bytes)));
        Assert.IsTrue(result.Reproduced, $"diverged at {result.FirstDivergence}");

        using var player = new ReplayPlayer(ReplayFile.Read(new MemoryStream(bytes)));
        while (!player.Finished)
        {
            player.StepOnce();
        }

        Assert.AreEqual(finalChecksum, player.Game.StateChecksum());
    }

    [TestMethod]
    public void TamperedInput_IsDetectedAsDivergence()
    {
        var (bytes, _, _) = RecordMatch(seed: 5, seconds: 6f, variableDt: false);
        var data = ReplayFile.Read(new MemoryStream(bytes));
        var steps = data.Steps.ToArray();
        var i = 100;
        var c = steps[i].Input.Player;
        steps[i] = steps[i] with { Input = steps[i].Input with { Player = c with { MoveWorld = c.MoveWorld + new Vector3(0.5f, 0f, 0f) } } };

        var result = ReplayPlayer.Verify(data with { Steps = steps });

        Assert.IsFalse(result.Reproduced);
        Assert.IsGreaterThan(i, result.FirstDivergence!.Value);
    }

    [TestMethod]
    public void TruncatedFile_StillReplaysUpToTheLastCompleteStep()
    {
        // 強制終了で途中までしか書けていなくても、そこまでは再現できる。
        var (bytes, _, steps) = RecordMatch(seed: 5, seconds: 4f, variableDt: false);
        var cut = bytes.AsSpan(0, bytes.Length - 37).ToArray();

        var data = ReplayFile.Read(new MemoryStream(cut));
        Assert.IsGreaterThan(steps - 3, data.Steps.Count);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void CorruptTimestep_IsRejectedInsteadOfReachingThePhysics()
    {
        var (bytes, _, _) = RecordMatch(seed: 5, seconds: 0.5f, variableDt: false);
        var data = ReplayFile.Read(new MemoryStream(bytes));
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            ReplayFile.WriteHeader(writer, data.Header);
            ReplayFile.WriteStep(writer, data.Steps[0] with { Dt = float.NaN });
        }

        Assert.ThrowsExactly<InvalidDataException>(() => ReplayFile.Read(new MemoryStream(stream.ToArray())));
    }

    [TestMethod]
    public void Header_CarriesTheSeedAndTheTuning()
    {
        var tuning = FighterTuning.Default with { WalkSpeed = 0.42f, BodyDrive = 1.1f };
        using var game = new BoxingGame(tuning, seed: 77);
        var stream = new MemoryStream();
        using (new ReplayRecorder(stream, game, "test"))
        {
        }

        var header = ReplayFile.Read(new MemoryStream(stream.ToArray())).Header;
        Assert.AreEqual(77, header.Seed);
        Assert.AreEqual("test", header.Host);
        Assert.AreEqual(tuning, header.Tuning);
    }

    /// <summary>CPU 同士（プレイヤー側も AI が操作）で打ち合い、途中でラウンドのやり直しも挟んで記録する。</summary>
    private static (byte[] Bytes, ulong FinalChecksum, int Steps) RecordMatch(int seed, float seconds, bool variableDt)
    {
        using var game = new BoxingGame(seed: seed);
        var playerAi = new AiControl(seed * 31, 0.8f);
        var random = new Random(seed);
        var stream = new MemoryStream();
        var steps = 0;
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var t = 0f; t < seconds;)
            {
                var dt = variableDt ? 1f / (72f + (48f * random.NextSingle())) : 1f / 60f;
                var reset = steps == 300;
                recorder.Step(game, new BoxingInput(playerAi.Read(game.PlayerSense, dt), reset), dt);
                t += dt;
                steps++;
            }

            return (stream.ToArray(), game.StateChecksum(), steps);
        }
    }
}
