using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>拳の技表（<see cref="PunchBook"/>）: 既定は移す前の値と同じ、JSON の往復、範囲、試合に本当に効く。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class PunchBookTests
{
    /// <summary>移す前（<c>PunchSpec.Of</c> に直書きだった頃）の値の写し。Default が変わっていないことの物差し。</summary>
    private static PunchSpec Original(Technique technique) => technique switch
    {
        Technique.Jab => new(true, 0f, 0f, 0.25f, 0.30f, false, new(0.05f, 0f, 0.08f), 0.6f),
        Technique.Cross => new(false, 0.1f, 0f, 0.35f, 0.40f, false, new(0.12f, -0.02f, 0.14f), 0.35f),
        Technique.LeadHook => new(true, 0.85f, -0.3f, 0.05f, 0.42f, false, new(-0.10f, -0.03f, 0.05f), 0.1f, Sweep: 0.3f),
        Technique.RearHook => new(false, 0.9f, -0.3f, 0.05f, 0.46f, false, new(0.12f, -0.03f, 0.06f), 0.1f, Sweep: 0.3f),
        Technique.LeadUppercut => new(true, 0.15f, 0.9f, 0.2f, 0.42f, false, new(-0.04f, -0.15f, 0.06f), 0.15f),
        Technique.RearUppercut => new(false, 0.2f, 1.0f, 0.2f, 0.46f, false, new(0.06f, -0.15f, 0.06f), 0.15f),
        Technique.BodyJab => new(true, 0f, 0.3f, 0.25f, 0.32f, true, new(0.04f, -0.35f, 0.12f), 0.7f),
        Technique.BodyCross => new(false, 0.1f, 0.3f, 0.3f, 0.42f, true, new(0.10f, -0.35f, 0.14f), 0.4f),
        Technique.LeadBodyHook => new(true, 0.8f, 0.35f, 0.15f, 0.44f, true, new(0.10f, -0.3f, 0.06f), 0.15f),
        Technique.RearBodyHook => new(false, 0.85f, 0.35f, 0.15f, 0.48f, true, new(-0.08f, -0.3f, 0.06f), 0.15f),
        Technique.Overhand => new(false, 0.55f, -0.7f, 0.25f, 0.50f, false, new(0.15f, -0.08f, 0.2f), 0.3f),
        Technique.Feint => new(true, 0f, 0f, 0f, 0.20f, false, new(0.03f, -0.04f, 0.06f), 0.2f, Extend: 0.35f),
        _ => throw new ArgumentOutOfRangeException(nameof(technique), technique, null),
    };

    [TestMethod]
    public void Default_IsAlreadyInsideItsOwnRanges()
    {
        Assert.AreEqual(PunchBook.Default, PunchBook.Default.Validated());
    }

    [TestMethod]
    public void StyleJson_RoundTripsEveryValueOfEveryTechnique_IncludingHeadShift()
    {
        // 既定とは違う値（HeadShift を含む）にして往復させる。
        var book = PunchBook.Default;
        var i = 0;
        foreach (var technique in Enum.GetValues<Technique>())
        {
            i++;
            var m = book.MoveOf(technique);
            book = book.With(technique, m with
            {
                Lateral = Math.Min(1.2f, m.Lateral + (0.011f * i)),
                Low = Math.Min(1.2f, m.Low + (0.012f * i)),
                Overshoot = Math.Min(0.6f, m.Overshoot + (0.013f * i)),
                Duration = m.Duration + (0.014f * i),
                HeadShiftX = m.HeadShiftX + (0.015f * i),
                HeadShiftY = m.HeadShiftY + (0.016f * i),
                HeadShiftZ = m.HeadShiftZ - (0.017f * i),
                StepIn = Math.Min(1f, m.StepIn + (0.018f * i)),
                Sweep = Math.Min(0.6f, m.Sweep + (0.019f * i)),
            });
        }

        var style = (BotCatalog.Champion with { Punches = book }).Validated();
        var back = BotStyle.FromJson(style.ToJson());
        foreach (var technique in Enum.GetValues<Technique>())
        {
            Assert.AreEqual(style.Punches.Of(technique), back.Punches.Of(technique), technique.ToString());
            Assert.AreEqual(style.Punches.MoveOf(technique), back.Punches.MoveOf(technique), technique.ToString());
        }

        Assert.AreNotEqual(PunchBook.Default.Of(Technique.Jab).HeadShift, back.Punches.Of(Technique.Jab).HeadShift, "既定から変えた HeadShift が読み戻されること");
        Assert.AreNotEqual(Vector3.Zero, back.Punches.Of(Technique.Cross).HeadShift, "HeadShift が 0 に戻らないこと");
    }

    [TestMethod]
    public void PartialJson_KeepsDefaultsForWhatIsNotWritten()
    {
        var style = BotStyle.FromJson("{\"Name\":\"x\",\"Punches\":{\"Cross\":{\"Duration\":0.55}}}");

        var cross = style.Punches.Of(Technique.Cross);
        Assert.AreEqual(0.55f, cross.Duration);
        var expected = PunchBook.Default.Of(Technique.Cross) with { Duration = 0.55f };
        Assert.AreEqual(expected, cross, "書いていない値（HeadShift・Extend など）は既定のまま");
        foreach (var technique in Enum.GetValues<Technique>().Where(t => t != Technique.Cross))
        {
            Assert.AreEqual(PunchBook.Default.Of(technique), style.Punches.Of(technique), technique + " は書いていないので既定");
        }
    }

    [TestMethod]
    public void StyleJsonWithoutPunches_IsTheDefaultBook()
    {
        var style = BotStyle.FromJson("{\"Name\":\"x\"}");

        Assert.AreEqual(PunchBook.Default, style.Punches);
    }

    [TestMethod]
    public void Validated_ClampsOutOfRangeValues_AndRestoresIdentity()
    {
        var wild = PunchBook.Default
            .With(Technique.Cross, PunchBook.Default.Cross with
            {
                Lead = true, ToBody = true, Extend = 0.3f, Duration = 9f, Lateral = -3f, Low = 9f, Overshoot = 5f,
                HeadShiftX = 3f, HeadShiftY = -3f, HeadShiftZ = float.NaN, StepIn = 7f, Sweep = 4f,
            })
            .With(Technique.Feint, PunchBook.Default.Feint with { Extend = 5f, Lead = false });

        var cross = wild.Validated().MoveOf(Technique.Cross);
        Assert.IsFalse(cross.Lead, "技の正体（前の手か）は既定へ戻る");
        Assert.IsFalse(cross.ToBody, "技の正体（胴か）は既定へ戻る");
        Assert.AreEqual(1f, cross.Extend, "フェイント以外の Extend は 1");
        Assert.AreEqual(PunchRanges.Duration.Max, cross.Duration);
        Assert.AreEqual(PunchRanges.Lateral.Min, cross.Lateral);
        Assert.AreEqual(PunchRanges.Low.Max, cross.Low);
        Assert.AreEqual(PunchRanges.Overshoot.Max, cross.Overshoot);
        Assert.AreEqual(PunchRanges.HeadShiftX.Max, cross.HeadShiftX);
        Assert.AreEqual(PunchRanges.HeadShiftY.Min, cross.HeadShiftY);
        Assert.AreEqual(PunchBook.Default.Cross.HeadShiftZ, cross.HeadShiftZ, "有限でない値は既定へ");
        Assert.AreEqual(PunchRanges.StepIn.Max, cross.StepIn);
        Assert.AreEqual(PunchRanges.Sweep.Max, cross.Sweep);

        var feint = wild.Validated().MoveOf(Technique.Feint);
        Assert.AreEqual(PunchRanges.FeintExtend.Max, feint.Extend, "フェイントの Extend は 0.2〜0.6");
        Assert.IsTrue(feint.Lead, "フェイントも正体は既定へ");
    }

    [TestMethod]
    public void DifferentCrossDuration_ChangesTheMatch_ButNotTheKindOfTechnique()
    {
        var slowCross = BotCatalog.Champion with
        {
            Punches = PunchBook.Default.With(Technique.Cross, PunchBook.Default.Cross with { Duration = 0.6f }),
        };

        var plain = Play(BotCatalog.Champion);
        var tuned = Play(slowCross.Validated());

        Assert.AreNotEqual(plain, tuned, "技表を変えたスタイルの試合は結果が変わる（技表が使われている）");
        Assert.AreEqual(plain, Play(BotCatalog.Champion), "同じスタイルなら同じ結果（決定的）");
        foreach (var technique in Enum.GetValues<Technique>())
        {
            // 拳の種類は技の番号で決まる（磨いた値では変わらない）: 移す前の値から求めた種類と同じ。
            var o = Original(technique);
            var expected = o.ToBody ? PunchKind.Body : o.Lateral >= 0.5f ? PunchKind.Hook : o.Low >= 0.5f ? PunchKind.Uppercut : PunchKind.Straight;
            Assert.AreEqual(expected, BoxerBot.KindOf(technique), technique.ToString());
        }
    }

    private static ulong Play(BotStyle champion)
    {
        using var game = new BoxingGame(seed: 5, corners: new BoxingCorners("champion",
            s => new BoxerBot(champion, s), s => BotCatalog.Create("swarmer", s)));
        for (var step = 0; step < 1200; step++)
        {
            game.Step(default, 1f / 60f);
        }

        return game.StateChecksum();
    }
}
