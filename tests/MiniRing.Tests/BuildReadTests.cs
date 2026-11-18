using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 体格を読む層（docs/BOT_PDCA.md「体格に応じた戦い方」）: 見えている寸法の比から連続に出る倍率であって、キャラ・体格の種類で分けない。
/// 体格が同じなら効かない（従来と同じ試合になる）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class BuildReadTests
{
    private const float Reach = 0.188f;

    private static FighterSense Sense(BodyBuild? self, BodyBuild? other, float ownReach = Reach, float otherReach = Reach)
        => new(new BodyFrame(Vector3.Zero, 0f), ownReach, new Vector3(0f, 1f, 0.3f), new Vector3(0f, 1f, 0.3f), true, SelfBuild: self, OpponentBuild: other, OpponentArmReach: otherReach);

    /// <summary>全部の係数を入れたスタイル（体格が同じなら効かないはず）。</summary>
    private static BotStyle Full(BotStyle style) => style with { BuildStandOff = 0.8f, BuildInside = 0.6f, BuildPush = 0.5f, BuildDuck = 1f, BuildAimBody = 3f };

    [TestMethod]
    public void MassRatio_IsHeightCubedTimesMuscleSquared()
    {
        Assert.AreEqual(1f, BodyBuild.Standard.MassRatio, 1e-6f);
        var marcus = new BodyBuild(1.12f, 1.1f, 1f, 1.15f);
        Assert.AreEqual(1.12f * 1.12f * 1.12f * 1.15f * 1.15f, marcus.MassRatio, 1e-5f);
        var akira = new BodyBuild(0.95f, 1f, 1f, 1.05f);
        Assert.AreEqual(0.95f * 0.95f * 0.95f * 1.05f * 1.05f, akira.MassRatio, 1e-5f);
        Assert.IsGreaterThan(akira.MassRatio * 1.5f, marcus.MassRatio, "大きく太い体の見積もりは重い");
    }

    [TestMethod]
    public void EqualBuilds_GiveUnitMultipliers()
    {
        var read = BuildRead.From(Sense(BodyBuild.Standard, BodyBuild.Standard));
        var style = Full(BotCatalog.Style("champion"));
        Assert.AreEqual(BuildRead.Even, read);
        Assert.AreEqual(1f, read.StandOff(style));
        Assert.AreEqual(1f, read.Inside(style));
        Assert.AreEqual(1f, read.Tempo(style));
        Assert.AreEqual(1f, read.Leave(style));
        Assert.AreEqual(1f, read.DefenseScale(style, Defense.Duck));
        Assert.AreEqual(1f, read.DefenseScale(style, Defense.Sway));
        Assert.AreEqual(0f, read.BodyAim(style));
        Assert.IsNull(read.WithBody(style, null), "胴の重みを足さないなら、狙いの重みは null のまま（乱数も使わない）");
    }

    [TestMethod]
    public void UnknownBuilds_ReadAsEven()
    {
        Assert.AreEqual(BuildRead.Even, BuildRead.From(Sense(null, null, otherReach: 0f)));
    }

    [TestMethod]
    public void LongerArm_StandsCloserInOwnReach_ShorterArmFarther()
    {
        var style = Full(BotCatalog.Style("boxer"));
        var longArm = BuildRead.From(Sense(new BodyBuild(1.12f, 1.1f, 1f, 1.15f), new BodyBuild(0.95f, 1f, 1f, 1.05f), Reach * 1.23f, Reach * 0.95f));
        var shortArm = BuildRead.From(Sense(new BodyBuild(0.95f, 1f, 1f, 1.05f), new BodyBuild(1.12f, 1.1f, 1f, 1.15f), Reach * 0.95f, Reach * 1.23f));
        Assert.IsLessThan(1f, longArm.StandOff(style));
        Assert.IsGreaterThan(1f, shortArm.StandOff(style));
        Assert.AreEqual(1f, longArm.Inside(style), "長い腕は打つ間合いを変えない");
        Assert.IsLessThan(1f, shortArm.Inside(style), "短い腕は相手の腕の内側へ深く入る");
        Assert.AreEqual(1f, longArm.StandOff(style) * shortArm.StandOff(style), 1e-5f, "比の対数で対称");
    }

    [TestMethod]
    public void HeavierAndTaller_ReadsAsPushingAndGoingForTheBody_LighterShorterDives()
    {
        var style = Full(BotCatalog.Style("boxer"));
        var big = new BodyBuild(1.15f, 1f, 1f, 1.2f);
        var small = new BodyBuild(0.92f, 0.95f, 1f, 1.1f);
        var tall = BuildRead.From(Sense(big, small));
        var low = BuildRead.From(Sense(small, big));
        Assert.IsGreaterThan(1f, tall.Tempo(style), "重い方は攻めの間隔が速く縮む");
        Assert.IsLessThan(1f, tall.Leave(style), "重い方は打って離れにくい");
        Assert.IsLessThan(1f, low.Tempo(style));
        Assert.IsGreaterThan(1f, low.Leave(style));
        Assert.IsGreaterThan(1f, low.DefenseScale(style, Defense.Duck), "頭が低いほど潜る");
        Assert.IsLessThan(1f, low.DefenseScale(style, Defense.Sway), "頭が低いほど引かない");
        Assert.IsLessThan(1f, tall.DefenseScale(style, Defense.Duck));
        Assert.IsGreaterThan(1f, tall.DefenseScale(style, Defense.Sway));
        Assert.AreEqual(1f, low.DefenseScale(style, Defense.Block), "沈みも引きもしない守りは変えない");
        Assert.IsGreaterThan(0f, low.BodyAim(style), "相手が高いほど胴の重みを足す");
        Assert.AreEqual(0f, tall.BodyAim(style));
        var weights = low.WithBody(style, null);
        Assert.IsNotNull(weights);
        Assert.AreEqual(weights!.Liver, weights.Heart, 1e-6f);
        Assert.AreEqual(low.BodyAim(style), weights.Liver + weights.SolarPlexus + weights.Heart, 1e-5f);
    }

    /// <summary>体格が同じなら、係数を入れても試合は 1 歩も変わらない（比が 0 で、どの倍率もちょうど 1）。</summary>
    [TestMethod]
    public void EqualBuilds_ReproduceTheSameBoutWithOrWithoutTheLayer()
    {
        foreach (var name in new[] { "champion", "swarmer" })
        {
            var plain = BotCatalog.Style(name);
            var a = BotArena.Run(new BoutSpec(Contender.Of(plain), Contender.Named("counter"), Seed: 5, Seconds: 8f));
            var b = BotArena.Run(new BoutSpec(Contender.Of(Full(plain)), Contender.Named("counter"), Seed: 5, Seconds: 8f));
            Assert.AreEqual(a.Red.DamageDealt, b.Red.DamageDealt, name);
            Assert.AreEqual(a.Blue.DamageDealt, b.Blue.DamageDealt, name);
            Assert.AreEqual(a.Red.Landed, b.Red.Landed, name);
            Assert.AreEqual(a.Blue.Landed, b.Blue.Landed, name);
        }
    }

    /// <summary>平均の胸と胸の距離 / 自分の腕の長さ（両方が立っている間）。</summary>
    private static float MeanDistanceInOwnReach(BotStyle style, BodyBuild build, float seconds)
    {
        var opponent = BotCatalog.Style("boxer");
        var corners = new BoxingCorners("opp", s => new BoxerBot(opponent.Validated(), s), s => new BoxerBot(style.Validated(), s), PlayerBody: build);
        using var game = new BoxingGame(null, 3, corners);
        var sum = 0.0;
        var n = 0;
        for (var i = 0; i < (int)(seconds * 60f); i++)
        {
            game.Step(default, 1f / 60f);
            if (game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing)
            {
                var d = game.Player.ChestPosition - game.Cpu.ChestPosition;
                d.Y = 0f;
                sum += d.Length() / game.Player.ArmReach;
                n++;
            }
        }

        Assert.IsGreaterThan(100, n, "立っていた歩数");
        return (float)(sum / n);
    }

    /// <summary>
    /// 同じスタイルで、腕の長い体と短い体を同じ相手に当てると、居る距離（自分の腕の長さで割った値）が違う向きに変わる:
    /// 層が入っていれば、長い腕のほうが自分の腕に対して近く（相手の届かない所の端）、短い腕のほうが遠く（相手の届く所の外）に立つ。
    /// </summary>
    [TestMethod]
    public void SameStyle_LongArmsStandCloserInOwnReachThanShortArms()
    {
        var style = BotCatalog.Style("boxer") with { BuildStandOff = 1f, BuildInside = 0f };
        var longBuild = new BodyBuild(1f, 1.3f, 1f, 1f);
        var shortBuild = new BodyBuild(1f, 0.8f, 1f, 1f);

        var withLayerLong = MeanDistanceInOwnReach(style, longBuild, 12f);
        var withLayerShort = MeanDistanceInOwnReach(style, shortBuild, 12f);
        var plain = BotCatalog.Style("boxer") with { BuildStandOff = 0f, BuildInside = 0f, BuildPush = 0f, BuildDuck = 0f, BuildAimBody = 0f }; // 層なし（従来）
        var plainLong = MeanDistanceInOwnReach(plain, longBuild, 12f);
        var plainShort = MeanDistanceInOwnReach(plain, shortBuild, 12f);

        Assert.IsLessThan(withLayerShort, withLayerLong, $"層あり: 長い腕 {withLayerLong:0.00} < 短い腕 {withLayerShort:0.00}");
        // 2026-11-17（休む間も回り込む）: 層なし 0.51・層あり 0.54。差は広がるが前の余裕 0.05 には届かない。
        Assert.IsGreaterThan(plainShort - plainLong + 0.02f, withLayerShort - withLayerLong, $"層なしの差 {plainShort - plainLong:0.00} より、層ありの差 {withLayerShort - withLayerLong:0.00} が広がる");
    }
}
