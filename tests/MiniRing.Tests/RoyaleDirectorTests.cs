using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>バトルロイヤルの中継のカメラの切り替え（見せ方だけ）: 面白い戦いへ寄る・頻繁に切り替えない・全体も見せる。</summary>
[TestClass]
public sealed class RoyaleDirectorTests
{
    private const float Dt = 1f / 60f;
    private static readonly (int, int, float)[] NoHits = [];

    /// <summary>4 人: 0 と 1 が向き合って打ち合える距離、2 と 3 も別の所で向き合う。</summary>
    private static DirectorFighter[] TwoFights() =>
    [
        new(new Vector3(-2f, 1f, 0f), true, false, 1),
        new(new Vector3(-1.2f, 1f, 0f), true, false, 0),
        new(new Vector3(2f, 1f, 0f), true, false, 3),
        new(new Vector3(2.8f, 1f, 0f), true, false, 2),
    ];

    private static float Run(RoyaleDirector director, float from, float seconds, DirectorFighter[] fighters, Func<float, IEnumerable<(int, int, float)>>? hits = null)
    {
        var t = from;
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            t += Dt;
            director.Update(t, Dt, fighters, hits?.Invoke(t) ?? NoHits);
        }

        return t;
    }

    [TestMethod]
    public void HeavyExchange_PullsTheCameraIn()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        var fighters = TwoFights();
        Run(director, 0f, 6f, fighters, t => (int)(t * 60f) % 30 == 0 ? [(2, 3, 15f)] : NoHits);
        Assert.AreEqual(new Shot(2, 3), director.Current, "打ち合っている 2 と 3 へ寄る");
    }

    [TestMethod]
    public void Shot_IsHeldForTheMinimumLength_EvenWhenAnotherFightFlaresUp()
    {
        var tuning = new DirectorTuning();
        var director = new RoyaleDirector(tuning);
        var fighters = TwoFights();
        var t = Run(director, 0f, 6f, fighters, t => (int)(t * 60f) % 30 == 0 ? [(2, 3, 15f)] : NoHits);
        Assert.AreEqual(new Shot(2, 3), director.Current);
        var cutAt = director.ShotStart;
        // 寄った直後に、別の戦いで強い打撃が続く。
        var hold = Math.Max(0f, cutAt + tuning.MinShot - t - 0.2f);
        var cuts = 0;
        for (var i = 0; i < (int)(hold / Dt); i++)
        {
            t += Dt;
            director.Update(t, Dt, fighters, [(0, 1, 40f)]);
            cuts += director.Cut ? 1 : 0;
        }

        Assert.AreEqual(0, cuts, "最短の長さの間は切り替えない");
        Run(director, t, 1.5f, fighters, _ => [(0, 1, 40f)]);
        Assert.AreEqual(new Shot(0, 1), director.Current, "最短の長さの後は、ずっと激しい戦いへ移る");
    }

    [TestMethod]
    public void SimilarFights_DoNotFlipBackAndForth()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        var fighters = TwoFights();
        var cuts = 0;
        var t = 0f;
        for (var i = 0; i < (int)(30f / Dt); i++)
        {
            t += Dt;
            // 2 つの戦いが同じくらいの激しさで、交互に当てる。
            var frame = (int)(t * 60f);
            (int, int, float)[] hits = frame % 40 == 0 ? [(0, 1, 10f)] : frame % 40 == 20 ? [(2, 3, 11f)] : NoHits;
            director.Update(t, Dt, fighters, hits);
            cuts += director.Cut ? 1 : 0;
        }

        // 30 秒で、寄る・全体へ引く・また寄るの数回まで（似た 2 つの戦いを行き来しない）。
        Assert.IsLessThanOrEqualTo(4, cuts, $"cuts {cuts}");
    }

    [TestMethod]
    public void LongFocus_GivesWayToTheWholeCage()
    {
        var tuning = new DirectorTuning();
        var director = new RoyaleDirector(tuning);
        var fighters = TwoFights();
        var sawWide = false;
        var t = Run(director, 0f, 5f, fighters, _ => [(0, 1, 2f)]);
        Assert.IsFalse(director.Current.IsWide, "打ち合いへ寄った");
        for (var i = 0; i < (int)(40f / Dt); i++)
        {
            t += Dt;
            director.Update(t, Dt, fighters, (int)(t * 60f) % 30 == 0 ? [(0, 1, 12f)] : NoHits); // ずっと続く激しい打ち合い
            sawWide |= director.Current.IsWide;
        }

        Assert.IsTrue(sawWide, "寄った絵が長く続いたら、全体を見せる");
    }

    [TestMethod]
    public void QuietCage_StaysWide()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        // 全員が離れていて、誰も打っていない。
        DirectorFighter[] apart =
        [
            new(new Vector3(-4f, 1f, 0f), true, false, 1),
            new(new Vector3(4f, 1f, 0f), true, false, 0),
            new(new Vector3(0f, 1f, 4f), true, false, 3),
            new(new Vector3(0f, 1f, -4f), true, false, 2),
        ];
        Run(director, 0f, 10f, apart);
        Assert.IsTrue(director.Current.IsWide);
    }

    [TestMethod]
    public void EliminatedFighter_EndsTheShotAfterAShortHold()
    {
        var tuning = new DirectorTuning();
        var director = new RoyaleDirector(tuning);
        var fighters = TwoFights();
        var t = Run(director, 0f, 6f, fighters, t => (int)(t * 60f) % 30 == 0 ? [(2, 3, 15f)] : NoHits);
        Assert.AreEqual(new Shot(2, 3), director.Current);
        fighters[3] = fighters[3] with { InPlay = false };
        t = Run(director, t, tuning.AfterElimination - 0.5f, fighters);
        Assert.AreEqual(new Shot(2, 3), director.Current, "倒れた所を少し映す");
        Run(director, t, 1f, fighters);
        Assert.IsTrue(director.Current.IsWide, "その後は全体へ引く");
    }

    [TestMethod]
    public void FinishingADownedFighter_OutranksAnEqualStandoff()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        var fighters = TwoFights();
        fighters[3] = fighters[3] with { Down = true };
        Assert.IsGreaterThan(director.Score(fighters, 0, 1), director.Score(fighters, 2, 3), "倒れた相手へ詰めている組の方が見どころが大きい");
    }

    [TestMethod]
    public void Break_ShowsTheWholeCage()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        var fighters = TwoFights();
        var t = Run(director, 0f, 6f, fighters, t => (int)(t * 60f) % 30 == 0 ? [(2, 3, 15f)] : NoHits);
        Assert.IsFalse(director.Current.IsWide);
        director.Update(t + Dt, Dt, fighters, NoHits, wholeCage: true);
        Assert.IsTrue(director.Current.IsWide, "休みの間は全体");
    }

    [TestMethod]
    public void CloseStandoff_IsShownEvenBeforeAnyHit()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        DirectorFighter[] fighters =
        [
            new(new Vector3(-0.4f, 1f, 0f), true, false, 1),
            new(new Vector3(0.4f, 1f, 0f), true, false, 0),
            new(new Vector3(4f, 1f, 0f), true, false, -1),
        ];
        Run(director, 0f, 5f, fighters);
        Assert.AreEqual(new Shot(0, 1), director.Current, "狙い合って詰めた 2 人へは、打撃が無くても寄る");
    }

    [TestMethod]
    public void NonFiniteDamage_DoesNotFreezeTheCamera()
    {
        var director = new RoyaleDirector(new DirectorTuning());
        var fighters = TwoFights();
        var t = Run(director, 0f, 6f, fighters, t => (int)(t * 60f) % 30 == 0 ? [(2, 3, float.NaN), (2, 3, 15f)] : NoHits);
        Assert.IsTrue(float.IsFinite(director.Score(fighters, 2, 3)));
        var sawWide = false;
        for (var i = 0; i < (int)(40f / Dt); i++)
        {
            t += Dt;
            director.Update(t, Dt, fighters, (int)(t * 60f) % 30 == 0 ? [(2, 3, 15f)] : NoHits);
            sawWide |= director.Current.IsWide;
        }

        Assert.IsTrue(sawWide);
    }
}
