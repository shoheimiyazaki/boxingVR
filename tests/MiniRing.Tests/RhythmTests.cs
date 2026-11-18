using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 間の層（<see cref="Rhythm"/>）: 相手の拳の出始めからの時間と、その拳が自分に触れたか（守りの上か外か）から、測った 1 打の損得の表を引き、
/// 次の拳までの間の縮む速さを連続に変える。層が無い・重み 0 なら倍率はちょうど 1（判断はビット単位で今まで通り）。
/// </summary>
[TestClass]
public sealed class RhythmTests
{
    /// <summary>rhythmlab の拳の選手全員の値に近い形: 入った直後は返される、守りの上で止めた 0.2〜0.4 秒が返しの機、空振りの後はいつでも得。</summary>
    private static RhythmStyle Style(float weight = 1f, float seize = 1f) => new RhythmStyle
    {
        Weight = weight,
        Seize = seize,
        Quiet = 3f,
        Bins =
        [
            new RhythmBin { From = 0f, None = 3.4f, Guard = -1.1f, Clean = -7.6f },
            new RhythmBin { From = 0.1f, None = 2.6f, Guard = 0.5f, Clean = -3.2f },
            new RhythmBin { From = 0.2f, None = 3f, Guard = 2.7f, Clean = -1.7f },
            new RhythmBin { From = 0.3f, None = 2.3f, Guard = 3.5f, Clean = -0.3f },
            new RhythmBin { From = 0.6f, None = 3.7f, Guard = 2.4f, Clean = 4.1f },
            new RhythmBin { From = 0.8f, None = 4f, Guard = 2.7f, Clean = 4.5f },
        ],
    }.Validated();

    [TestMethod]
    public void NoLayer_OrZeroWeight_LeavesTheTempoAtExactlyOne()
    {
        Assert.AreEqual(1f, Rhythm.Tempo(null, 0.05f, 10f, 0f));
        Assert.AreEqual(1f, Rhythm.Tempo(Style(weight: 0f), 0.05f, 10f, 0f));
    }

    [TestMethod]
    public void RightAfterBeingHit_TheBotWaits_AndLaterItIsItsTurn()
    {
        var style = Style();
        var justHit = Rhythm.Tempo(style, 0.02f, cleanDamage: 8f, guardContacts: 0f);
        var afterTheirCombo = Rhythm.Tempo(style, 0.85f, cleanDamage: 8f, guardContacts: 0f);
        Assert.IsLessThan(0f, justHit, "right after a clean hit the cooldown stretches (their turn)");
        Assert.IsGreaterThan(1f, afterTheirCombo, "once their punches are spent it is the bot's turn");
    }

    [TestMethod]
    public void AfterBlocking_TheCounterWindowOpensAfterAMoment()
    {
        var style = Style();
        var atOnce = Rhythm.Tempo(style, 0.02f, cleanDamage: 0f, guardContacts: 1f);
        var window = Rhythm.Tempo(style, 0.3f, cleanDamage: 0f, guardContacts: 1f);
        Assert.IsLessThan(window, atOnce, "blocked: wait a moment, then return");
        Assert.IsGreaterThan(1f, window);
    }

    [TestMethod]
    public void ContactWeights_AreContinuous_AndSumToOne()
    {
        var previous = Rhythm.ContactWeights(0f, 0f);
        Assert.AreEqual(1f, previous.None, 1e-6f);
        for (var d = 0.1f; d < 20f; d += 0.1f)
        {
            var w = Rhythm.ContactWeights(d, 0f);
            Assert.AreEqual(1f, w.None + w.Guard + w.Clean, 1e-5f);
            Assert.IsLessThan(0.05f, MathF.Abs(w.Clean - previous.Clean), "no jump in the weights at " + d);
            previous = w;
        }

        var both = Rhythm.ContactWeights(3f, 2f);
        Assert.IsTrue(both is { None: >= 0f, Guard: > 0f, Clean: > 0f });
    }

    [TestMethod]
    public void Expected_IsContinuousInTime_AndFadesToQuiet()
    {
        var style = Style();
        var previous = Rhythm.Expected(style, 0f, 0f, 1f);
        for (var t = 0.005f; t < 2f; t += 0.005f)
        {
            var v = Rhythm.Expected(style, t, 0f, 1f);
            Assert.IsLessThan(0.5f, MathF.Abs(v - previous), "no jump at " + t);
            previous = v;
        }

        Assert.AreEqual(style.Quiet, Rhythm.Expected(style, Rhythm.Horizon + 0.1f, 0f, 1f), 1e-5f);
        Assert.AreEqual(style.Quiet, Rhythm.Expected(style, float.PositiveInfinity, 0f, 0f), 1e-5f, "never seen a punch: quiet");
    }

    [TestMethod]
    public void Tempo_StaysInItsRange_AndSeizeScalesTheSwing()
    {
        var calm = Style(seize: 0.5f);
        var bold = Style(seize: 2f);
        Assert.IsLessThan(MathF.Abs(1f - Rhythm.Tempo(bold, 0.02f, 8f, 0f)), MathF.Abs(1f - Rhythm.Tempo(calm, 0.02f, 8f, 0f)));
        for (var t = 0f; t < 1.5f; t += 0.01f)
        {
            var v = Rhythm.Tempo(Style(seize: 3f), t, 20f, 0f);
            Assert.IsTrue(v is >= Rhythm.MinTempo and <= Rhythm.MaxTempo, $"{v} at {t}");
        }
    }

    [TestMethod]
    public void Validated_DropsBadRows_AndSortsByTime()
    {
        var style = new RhythmStyle
        {
            Weight = 5f,
            Quiet = float.NaN,
            Bins = [new RhythmBin { From = 0.5f }, new RhythmBin { From = float.NaN }, new RhythmBin { From = 0f, None = 1f }],
        }.Validated();
        Assert.AreEqual(1f, style.Weight);
        Assert.AreEqual(0f, style.Quiet);
        Assert.HasCount(2, style.Bins);
        Assert.AreEqual(0f, style.Bins[0].From);
    }
}
