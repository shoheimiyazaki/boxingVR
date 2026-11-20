using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// VR の刀の操作を、物理の体で確かめる（稽古の世界: 正眼の相手、実機なし）: 一人称で HMD を止めておけば体は歩かず、HMD が動けば体の頭がその点へ来て止まる。
/// グリップを離すと放す（片手 → 添えた手だけ、右手 → 刀ごと）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class VrFirstPersonPhysicsTests
{
    private const float Dt = 1f / 240f;

    private static readonly Vector3 Head = new(0f, 1.62f, 0f);

    private static BoxingGame NewGame() => new(FighterTuning.Default with { PlayerFatigue = false },
        corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f)) { HidePlayerHead = true };

    private static VrRawFrame Raw(Vector3 head, float leftGrip, float rightGrip) => new(
        head, Quaternion.Identity,
        new Vector3(-0.35f, 1.0f, -0.1f), new Vector3(0.15f, 1.25f, -0.4f), true, true,
        Vector2.Zero, Vector2.Zero, leftGrip, rightGrip, false, true, Quaternion.Identity, Quaternion.Identity);

    private static VrPlayerMapper Mapper(bool firstPerson) => new(weaponMode: true)
    {
        GripHolds = true,
        TwoHand = VrTwoHand.Right,
        FirstPerson = firstPerson,
        HeldWeapon = WeaponKind.Katana,
    };

    private static void Run(BoxingGame game, VrPlayerMapper mapper, VrRawFrame frame, float seconds)
    {
        for (var i = 0; i < (int)(seconds * 240f); i++)
        {
            game.Step(mapper.Read(frame, game.PlayerSense, Dt), Dt);
        }
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    [TestMethod]
    public void AStillHmdKeepsTheBodyStandingWithoutAnyInput()
    {
        using var game = NewGame();
        var mapper = Mapper(firstPerson: true);
        Run(game, mapper, Raw(Head, 1f, 1f), 1.678f); // 構えに落ち着く
        var start = game.Player.HeadPosition;
        var pelvis = game.Player.PelvisPosition;
        Run(game, mapper, Raw(Head, 1f, 1f), 13.43f);
        var drift = Flat(game.Player.PelvisPosition, pelvis);
        var headDrift = Flat(game.Player.HeadPosition, start);
        Console.WriteLine($"still HMD 13.4 s: pelvis drift {drift * 100f:0.0} cm, head drift {headDrift * 100f:0.0} cm (arm reach {game.Player.ArmReach * 100f:0.0} cm)");
        Assert.IsTrue(drift < 0.0845f, $"止まった HMD で体が滑る: {drift * 100f:0.0} cm");
        Assert.IsTrue(headDrift < 0.0845f, $"頭が漂う: {headDrift * 100f:0.0} cm");
    }

    [TestMethod]
    public void TheBodyWalksItsHeadToAMovedHmdAndStopsThere()
    {
        using var game = NewGame();
        var mapper = Mapper(firstPerson: true);
        Run(game, mapper, Raw(Head, 1f, 1f), 1.678f);
        var scale = mapper.Placement!.Value.Scale;
        var startHead = game.Player.HeadPosition;
        var target = mapper.Placement!.Value.Point(Head + new Vector3(0f, 0f, -0.6f)); // 人の 0.6 m 前へ
        var distance = Flat(target, startHead);
        Run(game, mapper, Raw(Head + new Vector3(0f, 0f, -0.6f), 1f, 1f), 10.07f);
        var remaining = Flat(target, game.Player.HeadPosition);
        Console.WriteLine($"scale {scale:0.000}: target {distance * 100f:0.0} cm away, after 10.1 s {remaining * 100f:0.0} cm left (dead zone {VrPlayerMapper.WalkDeadZone * game.Player.ArmReach * 100f:0.0} cm)");
        Assert.IsTrue(remaining < (VrPlayerMapper.WalkDeadZone + 0.1f) * game.Player.ArmReach, $"頭が目標へ来ない: 残り {remaining * 100f:0.0} cm");
        Assert.IsTrue(remaining < distance * 0.5f, "目標へ近づく");
        var settled = game.Player.PelvisPosition;
        Run(game, mapper, Raw(Head + new Vector3(0f, 0f, -0.6f), 1f, 1f), 5.035f);
        Assert.IsTrue(Flat(game.Player.PelvisPosition, settled) < 0.0845f, "着いたら止まる（行き過ぎ・往復しない）");
    }

    [TestMethod]
    public void WithoutGripPressesTheStartingWeaponStaysInBothHands()
    {
        using var game = NewGame();
        var mapper = Mapper(firstPerson: false);
        Run(game, mapper, Raw(Head, 0f, 0f), 3.357f);
        Assert.IsTrue(game.Player.Wields(Hand.Right) && game.Player.Wields(Hand.Left), "グリップを押していなくても、始めに握っている刀は落とさない");
    }

    [TestMethod]
    public void ReleasingTheLeftGripLetsGoOfTheSupportHandOnly()
    {
        using var game = NewGame();
        var mapper = Mapper(firstPerson: false);
        Run(game, mapper, Raw(Head, 1f, 1f), 1.678f);
        Run(game, mapper, Raw(Head, 0f, 1f), 1.678f);
        Assert.IsTrue(game.Player.Wields(Hand.Right), "右手は握ったまま");
        Assert.IsFalse(game.Player.Wields(Hand.Left), "左のグリップを離した左手は刀を放す（片手持ちに戻る）");
    }

    [TestMethod]
    public void ReleasingTheRightGripDropsTheSword()
    {
        using var game = NewGame();
        var mapper = Mapper(firstPerson: false);
        Run(game, mapper, Raw(Head, 1f, 1f), 1.678f);
        Run(game, mapper, Raw(Head, 1f, 0f), 1.678f);
        Assert.IsFalse(game.Player.Wields(Hand.Right), "右のグリップを離せば刀を放す");
    }
}
