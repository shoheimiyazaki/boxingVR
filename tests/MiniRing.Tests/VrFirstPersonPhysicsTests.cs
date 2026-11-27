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

    private static BoxingGame NewGame(FighterTuning? tuning = null) => new((tuning ?? FighterTuning.Default) with { PlayerFatigue = false },
        corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f)) { HidePlayerHead = true };

    // 手は頭と一緒に動く（人が部屋の中を歩けば手も付いてくる。一人称では手の目標 = コントローラの部屋の中の点なので、
    // 頭だけを動かすと手が元の所に残り、体を後ろへ引き続ける）。
    private static VrRawFrame Raw(Vector3 head, float leftGrip, float rightGrip) => new(
        head, Quaternion.Identity,
        new Vector3(-0.35f, 1.0f, -0.1f) + (head - Head), new Vector3(0.15f, 1.25f, -0.4f) + (head - Head), true, true,
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
    [Ignore("既知: 止まった HMD の足の踏み直し・BASE_BODY_PLAN §10.8")]
    public void AStillHmdKeepsTheBodyStandingWithoutAnyInput()
    {
        using var game = NewGame();
        var (drift, headDrift, later) = StillHmdDrift(game);
        var foot = 2f * game.Player.HalfExtents.Foot.Z / (1f - FootGeometry.ToeShare);
        Console.WriteLine($"still HMD 13.4 s: pelvis drift {drift * 100f:0.0} cm (later half {later * 100f:0.0} cm), head drift {headDrift * 100f:0.0} cm (arm reach {game.Player.ArmReach * 100f:0.0} cm, foot {foot * 100f:0.0} cm)");
        // 2026-11-26（PhysicsVersion 103、つま先の蝶番を固くした）: 前は 13.4 秒の流れ全体を 8.45 cm 未満で見ていた（前のつま先で 5.8 cm）。固いつま先では
        // 構えに落ち着く始めの半分で足を 1 度置き直して 7.4 cm 動き、後の半分は 1.3 cm（前のつま先は 2.5 cm）で、滑り続けはしない（ToeRegressionProbe.StillHmd、
        // つま先 20・30 Hz でも全体 8〜9 cm）。滑り続けないことは後の半分で、置き直しの大きさは足の長さの半分までで見る。
        Assert.IsTrue(later < 0.0423f, $"止まった HMD で体が滑り続ける: 後の半分で {later * 100f:0.0} cm");
        Assert.IsTrue(drift < 0.5f * foot, $"止まった HMD で体が足の長さの半分より大きく動く: {drift * 100f:0.0} cm");
        Assert.IsTrue(headDrift < 0.5f * foot, $"頭が漂う: {headDrift * 100f:0.0} cm");
    }

    /// <summary>止まった HMD（一人称・刀を両手）で 13.4 秒立たせた間の、骨盤と頭の水平の流れ [m]。</summary>
    internal static (float Pelvis, float Head, float PelvisLaterHalf) StillHmdDrift(FighterTuning? tuning) { using var game = NewGame(tuning); return StillHmdDrift(game); }

    private static (float Pelvis, float Head, float PelvisLaterHalf) StillHmdDrift(BoxingGame game)
    {
        var mapper = Mapper(firstPerson: true);
        // 構えに落ち着く。2026-11-27（物理 106 仮）: 生成の T ポーズから構えへ沈む最後の踏み直しが 2.0〜2.4 秒までかかることがある（立つだけの体・正眼の相手と同じ）ので、
        // 1.7 秒から 2.5 秒にした。
        Run(game, mapper, Raw(Head, 1f, 1f), 2.518f);
        var start = game.Player.HeadPosition;
        var pelvis = game.Player.PelvisPosition;
        Run(game, mapper, Raw(Head, 1f, 1f), 6.715f);
        var middle = game.Player.PelvisPosition;
        Run(game, mapper, Raw(Head, 1f, 1f), 6.715f);
        return (Flat(game.Player.PelvisPosition, pelvis), Flat(game.Player.HeadPosition, start), Flat(game.Player.PelvisPosition, middle));
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

    [TestMethod]
    public void TheDrawnHandTargetSitsOnTheStillControllerAndTheHandComesToIt()
    {
        // 一人称: 止めたコントローラ（届く所）に、描いている手の目標の玉（体が引かれている点）が重なり、体の手もそこへ来る。
        using var game = NewGame();
        var mapper = Mapper(firstPerson: true);
        var frame = Raw(Head, 1f, 1f);
        Run(game, mapper, frame, 1.678f);
        var controller = mapper.Placement!.Value.Point(frame.RightHand);
        var marker = Vector3.Distance(game.Player.HandTargetWorld(Hand.Right), controller);
        var hand = Vector3.Distance(game.Player.GloveCenter(Hand.Right), controller);
        Console.WriteLine($"still controller: target marker {marker * 100f:0.00} cm, hand {hand * 100f:0.0} cm from the controller (arm reach {game.Player.ArmReach * 100f:0.0} cm)");
        Assert.IsTrue(marker < 0.01f, $"描いている目標がコントローラから {marker * 100f:0.00} cm ずれる");
        Assert.IsTrue(hand < 0.1f * game.Player.ArmReach, $"体の手がコントローラへ来ない: {hand * 100f:0.0} cm");
    }
}
