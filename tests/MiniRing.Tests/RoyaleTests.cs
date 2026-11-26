using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>バトルロイヤルで狙う相手の選び方（<see cref="RivalChoice"/>）: 見える特徴の連続の量の重み付きの和の最大。</summary>
[TestClass]
public sealed class RivalChoiceTests
{
    private static RivalView At(float x, float z, Vector3 forward, float injury = 0f, float lowness = 0f, float grudge = 0f)
        => new(new Vector3(x, 1f, z), forward, injury, lowness, grudge);

    [TestMethod]
    public void TheNearerRival_IsChosen()
    {
        RivalView[] rivals = [At(0f, 3f, -Vector3.UnitZ), At(0f, 1.5f, -Vector3.UnitZ)];
        Assert.AreEqual(1, RivalChoice.Choose(new Vector3(0f, 1f, 0f), 0.7f, rivals, -1, RivalWeights.Default));
    }

    [TestMethod]
    public void Grudge_TurnsTowardTheOneWhoHit()
    {
        // 同じ距離・同じ向きの 2 人: 恨みのある方へ。
        RivalView[] rivals = [At(-1f, 1f, Vector3.UnitX), At(1f, 1f, -Vector3.UnitX, grudge: 0.8f)];
        Assert.AreEqual(1, RivalChoice.Choose(new Vector3(0f, 1f, 0f), 0.7f, rivals, 0, RivalWeights.Default));
    }

    [TestMethod]
    public void Weakness_AndLowness_RaiseTheScore_Continuously()
    {
        var self = new Vector3(0f, 1f, 0f);
        var weights = RivalWeights.Default;
        var healthy = RivalChoice.Score(self, 0.7f, At(0f, 2f, -Vector3.UnitZ), false, weights);
        var hurt = RivalChoice.Score(self, 0.7f, At(0f, 2f, -Vector3.UnitZ, injury: 0.3f), false, weights);
        var hurtMore = RivalChoice.Score(self, 0.7f, At(0f, 2f, -Vector3.UnitZ, injury: 0.6f), false, weights);
        var down = RivalChoice.Score(self, 0.7f, At(0f, 2f, -Vector3.UnitZ, lowness: 0.7f), false, weights);
        Assert.IsGreaterThan(healthy, hurt);
        Assert.IsGreaterThan(hurt, hurtMore);
        Assert.IsGreaterThan(healthy, down);
    }

    [TestMethod]
    public void Stick_KeepsTheCurrentRival_WhenTheOtherIsOnlySlightlyBetter()
    {
        RivalView[] rivals = [At(0f, 2.0f, -Vector3.UnitZ), At(0f, 1.9f, -Vector3.UnitZ)];
        var self = new Vector3(0f, 1f, 0f);
        Assert.AreEqual(1, RivalChoice.Choose(self, 0.7f, rivals, -1, RivalWeights.Default));
        Assert.AreEqual(0, RivalChoice.Choose(self, 0.7f, rivals, 0, RivalWeights.Default), "a little nearer is not worth turning away");
    }

    [TestMethod]
    public void NegativeFacing_PrefersTheRivalWhoShowsHisBack()
    {
        // 同じ距離: 1 人はこちらを向き、1 人は背中を向けている。
        RivalView[] rivals = [At(0f, 2f, -Vector3.UnitZ), At(0f, -2f, -Vector3.UnitZ)];
        var self = new Vector3(0f, 1f, 0f);
        Assert.AreEqual(0, RivalChoice.Choose(self, 0.7f, rivals, -1, RivalWeights.Default with { Facing = 1f }), "facing the threat");
        Assert.AreEqual(1, RivalChoice.Choose(self, 0.7f, rivals, -1, RivalWeights.Default with { Facing = -1f }), "taking the back");
    }

    [TestMethod]
    public void NoRival_IsMinusOne_AndTiesGoToTheFirst()
    {
        Assert.AreEqual(-1, RivalChoice.Choose(Vector3.Zero, 0.7f, [], -1, RivalWeights.Default));
        RivalView[] twins = [At(1f, 0f, -Vector3.UnitX), At(-1f, 0f, Vector3.UnitX)];
        Assert.AreEqual(0, RivalChoice.Choose(new Vector3(0f, 1f, 0f), 0.7f, twins, -1, RivalWeights.Default));
    }

    [TestMethod]
    public void Weights_AreValidated_AndTravelWithTheStyle()
    {
        var odd = new RivalWeights(Near: float.NaN, RevengeHalfLife: -1f).Validated();
        Assert.AreEqual(1f, odd.Near);
        Assert.AreEqual(0.1f, odd.RevengeHalfLife);
        var style = BotCatalog.Boxer with { Rival = new RivalWeights(Weak: 3f) };
        Assert.AreEqual(3f, BotStyle.FromJson(style.ToJson()).Rival!.Weak, "the weights are part of the textbook/education");
    }
}

/// <summary>席の立つ所（<see cref="RingLayout.SeatCorner"/>）: 2 人なら赤・青のコーナー、6 人の檻なら 6 つの頂点。</summary>
[TestClass]
public sealed class SeatCornerTests
{
    [TestMethod]
    public void TwoSeats_AreTheRedAndBlueCorners()
    {
        foreach (var layout in new[] { RingLayout.WeaponsCage, RingLayout.Boxing })
        {
            var (red, blue) = layout.IsCage ? (240f, 60f) : (225f, 45f);
            Assert.AreEqual(layout.CornerFor(red, []), layout.SeatCorner(0, 2, []));
            Assert.AreEqual(layout.CornerFor(blue, []), layout.SeatCorner(1, 2, []));
            Assert.AreEqual(layout.RestCorner(false, []), layout.SeatCorner(1, 2, []));
        }
    }

    [TestMethod]
    public void SixSeats_StandOnTheSixVertices_InsideTheCage()
    {
        var layout = RingLayout.RoyaleCage;
        var seats = Enumerable.Range(0, 6).Select(k => layout.SeatCorner(k, 6, [])).ToList();
        for (var i = 0; i < 6; i++)
        {
            Assert.IsGreaterThanOrEqualTo(RingLayout.CornerClearance - 1e-4f, layout.EdgeDistance(seats[i]));
            var flat = seats[i] - layout.Center;
            var degrees = MathF.Atan2(flat.Z, flat.X) * 180f / MathF.PI;
            var off = MathF.Abs(MathF.IEEERemainder(degrees, 60f));
            Assert.IsLessThan(0.01f, off, "on a vertex direction");
            for (var j = i + 1; j < 6; j++)
            {
                Assert.IsGreaterThan(0.9f * layout.Circumradius, Vector3.Distance(seats[i], seats[j]), "neighbours are a side apart");
            }
        }
    }
}

/// <summary>3 人のバトルロイヤル: 決定的・記録から再現・最後の 1 人の勝ち・狙いの恨み。2 人の試合は追加の席が空でも今と同じ。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class RoyaleTests
{
    private static readonly MatchRules Rules = MatchRules.Vfc with { IntroSeconds = 0f, EndSeconds = 0f };

    private static readonly string[] ThreeIds = ["jin", "joao", "vikram"];

    private static BoxingCorners Three() => RoyaleCorners.From(ThreeIds.Select(CharacterRoster.Get).ToList());

    [TestMethod]
    public void ThreeFighters_StartOnTheirVertices_FacingTheCenter()
    {
        using var game = new BoxingGame(seed: 3, corners: Three(), rules: Rules);
        Assert.IsTrue(game.IsRoyale);
        Assert.HasCount(3, game.AllFighters);
        var center = game.Layout.Center;
        foreach (var fighter in game.AllFighters)
        {
            Assert.IsTrue(game.InPlay(fighter));
            var toCenter = center - fighter.PelvisPosition;
            toCenter.Y = 0f;
            Assert.IsGreaterThan(0.95f, Vector3.Dot(fighter.Frame.Forward, Vector3.Normalize(toCenter)), "faces the middle of the cage");
            Assert.IsGreaterThan(2f, toCenter.Length(), "starts out at a vertex");
        }

        Assert.AreNotSame(game.TargetOf(game.Player), game.Player);
    }

    [TestMethod]
    public void ThreeFighters_ReplayBitForBit()
    {
        using var stream = new MemoryStream();
        using (var game = new BoxingGame(seed: 5, corners: Three(), rules: Rules))
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval * 4; i++)
            {
                recorder.Step(game, default, 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.HasCount(1, data.Header.MatchSettings!.Others!);
        Assert.AreEqual("vikram", CharacterRoster.All.First(c => c.HudName == data.Header.MatchSettings.Others![0].Label).Id);
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced, $"diverged at {result.FirstDivergence}");
        Assert.AreEqual(4, result.ChecksumsMatched);
    }

    [TestMethod]
    public void ExtraSeats_WithAReferee_AreRejected()
    {
        // ダウンの数え（レフェリー）は 2 人の決まり: 死体が倒れたまま数え続けて試合が止まる。
        Assert.ThrowsExactly<ArgumentException>(() => new BoxingGame(corners: Three() with { Referee = true }, rules: Rules));
    }

    [TestMethod]
    public void SameSeed_SameMatch()
    {
        ulong Run()
        {
            using var game = new BoxingGame(seed: 9, corners: Three(), rules: Rules);
            for (var i = 0; i < 150; i++)
            {
                game.Step(default, 1f / 60f);
            }

            return game.StateChecksum();
        }

        Assert.AreEqual(Run(), Run());
    }

    [TestMethod]
    public void TwoFighters_WithNoExtraSeats_AreTheSameMatch()
    {
        // 2 人の試合: 追加の席が null でも空でも、同じビルドの中で 1 ビットも違わない（2 人の試合の道は追加の席を見ない）。
        var corners = VfcPair();
        ulong Run(BoxingCorners c)
        {
            using var game = new BoxingGame(seed: 4, corners: c, rules: Rules);
            Assert.IsFalse(game.IsRoyale);
            for (var i = 0; i < 180; i++)
            {
                game.Step(default, 1f / 60f);
            }

            Assert.AreSame(game.Cpu, game.TargetOf(game.Player));
            return game.StateChecksum();
        }

        Assert.AreEqual(Run(corners), Run(corners with { Others = [] }));
    }

    private static BoxingCorners VfcPair()
    {
        var jin = CharacterRoster.Get("jin");
        var joao = CharacterRoster.Get("joao");
        return BotCatalog.Corners(joao.Style) with
        {
            RedBotStyle = jin.Style, PlayerWeapon = jin.Weapon, PlayerTwoHanded = FormSwordsman.Handles(jin.Weapon), PlayerWear = jin.Wear,
            CpuWear = joao.Wear, UseWeaponBots = true, Referee = false, Ring = RingLayout.WeaponsCage.AtCorners(),
        };
    }

    [TestMethod]
    public void TheLastOneStanding_Wins()
    {
        using var game = new BoxingGame(seed: 2, corners: Three(), rules: Rules);
        game.Step(default, 1f / 60f);
        foreach (var victim in new[] { game.AllFighters[2], game.Cpu })
        {
            for (var i = 0; i < 300 && !victim.IsDead; i++)
            {
                var s = game.World.GetState(victim.Parts.Head);
                var from = (i % 3) switch { 0 => Vector3.UnitZ, 1 => -Vector3.UnitZ, _ => Vector3.UnitY };
                var point = s.Position + (Vector3.Transform(from, s.Orientation) * 0.2254f);
                game.Strike(victim, victim.Parts.Head, point, s.Position - point, 30f);
            }

            Assert.IsTrue(victim.IsDead);
            game.Step(default, 1f / 60f);
            Assert.IsFalse(game.InPlay(victim), "a dead fighter is out");
        }

        Assert.IsTrue(game.MatchOver);
        Assert.AreSame(game.Player, game.Winner);
        CollectionAssert.AreEqual(new[] { game.AllFighters[2], game.Cpu }, game.Eliminations.Select(e => e.Fighter).ToArray());
    }

private static readonly string[] FourTeamIds = ["galva", "galva", "jin", "joao"];    private static BoxingCorners Teams() => RoyaleCorners.From(FourTeamIds.Select(CharacterRoster.Get).ToList()) with { Teams = [0, 0, 1, 1] };    [TestMethod]    public void Teammates_NeverAimAtEachOther()    {        using var game = new BoxingGame(seed: 3, corners: Teams(), rules: Rules);        for (var i = 0; i < 120; i++)        {            game.Step(default, 1f / 60f);            foreach (var fighter in game.AllFighters)            {                var seat = game.SeatOf(fighter);                Assert.AreNotEqual(game.InitialCorners.TeamOf(seat), game.InitialCorners.TeamOf(game.SeatOf(game.TargetOf(fighter))), "a teammate is not a rival");            }        }    }    [TestMethod]    public void TheLastTeamStanding_Wins()    {        using var game = new BoxingGame(seed: 2, corners: Teams(), rules: Rules);        game.Step(default, 1f / 60f);        foreach (var victim in new[] { game.AllFighters[2], game.AllFighters[3] })        {            for (var i = 0; i < 300 && !victim.IsDead; i++)            {                var s = game.World.GetState(victim.Parts.Head);                var point = s.Position + (Vector3.Transform(i % 2 == 0 ? Vector3.UnitZ : Vector3.UnitY, s.Orientation) * 0.2254f);                game.Strike(victim, victim.Parts.Head, point, s.Position - point, 30f);            }            game.Step(default, 1f / 60f);        }        Assert.IsTrue(game.MatchOver);        Assert.AreEqual(0, game.WinningTeam);        Assert.IsNull(game.Winner, "two alien teammates survive together");        Assert.ThrowsExactly<ArgumentException>(() => new BoxingGame(corners: Teams() with { Teams = [0, 1] }, rules: Rules), "one team per seat");    }
    [TestMethod]
    public void A_Blow_TurnsTheVictimTowardTheAttacker()
    {
        using var game = new BoxingGame(seed: 6, corners: Three(), rules: Rules);
        game.Step(default, 1f / 60f);
        var red = game.Player;
        var third = game.AllFighters[2];
        var before = game.TargetOf(red);
        var attacker = before == third ? game.Cpu : third; // 今狙っていない方から打たれる
        var head = game.World.GetState(red.Parts.Head).Position;
        game.ApplyHit(new HitEvent(attacker, Hand.Right, red, HitZone.Head, head, 6f, 3f, 0.3f, HitLook.FullDamage), red.Parts.Head, Vector3.UnitX, carve: false, carryBody: false);
        game.Step(default, 1f / 60f);
        Assert.AreSame(attacker, game.TargetOf(red), "turned to the one who hit");
    }
}

/// <summary>記録の設定（<see cref="ReplayMatchSettings"/>）がバトルロイヤルの席と休みの動作の有無を持つ。</summary>
[TestClass]
public sealed class RoyaleReplaySettingsTests
{
    private static readonly string[] FourIds = ["jin", "joao", "vikram", "sean"];
    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { IncludeFields = true };

    [TestMethod]
    public void OthersAndBreakActions_RoundTrip()
    {
        var corners = RoyaleCorners.From(FourIds.Select(CharacterRoster.Get).ToList()) with { BreakActions = false };
        var json = System.Text.Json.JsonSerializer.Serialize(ReplayMatchSettings.From(corners, MatchRules.Vfc), Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ReplayMatchSettings>(json, Json)!.Apply(BotCatalog.Corners(corners.CpuStyle!));
        Assert.IsFalse(back.BreakActions, "a match recorded without break actions replays without them (it diverged in the break before)");
        Assert.HasCount(2, back.Others!);
        Assert.AreEqual(corners.Others![1].Weapon, back.Others![1].Weapon);
        Assert.AreEqual(corners.Others[1].Style.ToJson(), back.Others[1].Style.ToJson());
        Assert.AreEqual(4, back.Seats);
    }
}
