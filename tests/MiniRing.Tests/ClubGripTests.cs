using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 棍棒を握る拳（<see cref="Fighter.Wield"/>）: 拳は位置の目標へバネで引かれ、手首の筋肉が棍棒を角度の目標へ回す。棍棒の質量と
/// 慣性モーメントは、その拳の力と手首のトルクで動かす。
/// </summary>
[TestClass]
public sealed class ClubGripTests
{
    /// <summary>毎サブステップ、渡した関数で指令を作る操作元。</summary>
    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    private static FighterCommand Face(in FighterSense s, HandCommand left, HandCommand right)
    {
        var face = s.OpponentChest - s.Self.Origin;
        face.Y = 0f;
        return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), left, right);
    }

    private static HandCommand Guard(Hand hand, float reach) => new(new Vector3(hand.LocalSide() * 0.15f * reach, 0.1f * reach, 0.45f * reach), false);

    /// <summary>CPU に棍棒を握らせ、右手を <paramref name="right"/> の指令で動かす試合（プレイヤーは構えて立つ）。</summary>
    private static BoxingGame Armed(Func<FighterSense, HandCommand> right, WeaponKind? weapon = WeaponKind.WoodenClub, FighterTuning? tuning = null) =>
        new(tuning, seed: 1, corners: new BoxingCorners("club", _ => new Scripted(s => Face(s, Guard(Hand.Left, s.ArmReach), right(s))), _ => new StandGuard(), CpuWeapon: weapon));

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }
    }

    private static Vector3 ClubAxis(BoxingGame game) =>
        Vector3.Transform(Vector3.UnitX, game.World.GetState(game.WeaponOf(game.Cpu)!.Body.BodyId).Orientation);

    private static Vector3 GripWorld(BoxingGame game)
    {
        var club = game.WeaponOf(game.Cpu)!;
        var s = game.World.GetState(club.Body.BodyId);
        return s.Position + Vector3.Transform(Weapon.GripOf(club.Kind), s.Orientation);
    }

    [TestMethod]
    public void TheClub_StaysInTheFist_WithTheGloveOff()
    {
        using var game = Armed(s => Guard(Hand.Right, s.ArmReach));
        Play(game, 1f);

        Assert.IsTrue(game.Cpu.Wields(BoxingGame.WeaponHand));
        Assert.IsFalse(game.Cpu.Wields(Hand.Left), "the other hand keeps its glove");
        Assert.IsLessThan(0.01f, Vector3.Distance(GripWorld(game), game.Cpu.GloveCenter(BoxingGame.WeaponHand)), "the grip stays at the fist");
        Assert.IsFalse(game.Player.Wields(BoxingGame.WeaponHand));
    }

    [TestMethod]
    public void TheWrist_TurnsTheClubToTheAngleAsked()
    {
        // 棍棒を真上へ（武器のローカル X → 体フレームの上）。
        var up = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        using var game = Armed(s => Guard(Hand.Right, s.ArmReach) with { Orientation = up });
        Play(game, 1f);
        Assert.IsGreaterThan(0.9f, Vector3.Dot(ClubAxis(game), Vector3.UnitY), "the club points up");

        // 前へ（体フレームの前 = +Z）。
        var forward = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
        using var game2 = Armed(s => Guard(Hand.Right, s.ArmReach) with { Orientation = forward });
        Play(game2, 1f);
        Assert.IsGreaterThan(0.85f, Vector3.Dot(ClubAxis(game2), game2.Cpu.Frame.Forward), "the club points at the opponent");
    }

    [TestMethod]
    public void AWeakWrist_CannotSwingTheClubsInertiaUpright()
    {
        var up = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        using var weak = Armed(s => Guard(Hand.Right, s.ArmReach) with { Orientation = up }, tuning: FighterTuning.Default with { WristMaxTorque = 0.3f });
        Play(weak, 1f);
        Assert.IsLessThan(0.5f, Vector3.Dot(ClubAxis(weak), Vector3.UnitY), "a wrist too weak for the club's weight lets it hang");
    }

    [TestMethod]
    public void TheFist_IsSlowerToReachItsTarget_WhenItCarriesTheClub()
    {
        // 構えから、拳を体の前へ大きく突き出す。腕は前腕と棍棒の重さを合わせて引くので、棍棒を持つ方が遅い。
        static float TimeToReach(WeaponKind? weapon)
        {
            var go = false;
            using var game = Armed(s => go ? new HandCommand(new Vector3(-0.05f * s.ArmReach, 0.05f * s.ArmReach, 0.95f * s.ArmReach), false) : Guard(Hand.Right, s.ArmReach), weapon);
            Play(game, 1f);
            go = true;
            for (var t = 0f; t < 1f; t += 1f / 240f)
            {
                game.Step(default, 1f / 240f);
                if (Vector3.Distance(game.Cpu.GloveCenter(BoxingGame.WeaponHand), game.Cpu.HandTargetWorld(BoxingGame.WeaponHand)) < 0.02f)
                {
                    return t;
                }
            }

            return 1f;
        }

        Assert.IsGreaterThan(TimeToReach(null), TimeToReach(WeaponKind.WoodenClub));
    }

    /// <summary>CPU に棍棒を両手で握らせ、右手（上の手）を <paramref name="right"/> の指令で動かす。左手の指令は何を出しても使われない。</summary>
    private static BoxingGame TwoHands(Func<FighterSense, HandCommand> right) =>
        new(seed: 1, corners: new BoxingCorners("bat", _ => new Scripted(s => Face(s, Guard(Hand.Left, s.ArmReach), right(s))), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub, CpuTwoHanded: true));

    private static float ToGrip(BoxingGame game, Hand hand, Vector3 gripLocal)
    {
        var s = game.World.GetState(game.WeaponOf(game.Cpu)!.Body.BodyId);
        return Vector3.Distance(game.Cpu.GloveCenter(hand), s.Position + Vector3.Transform(gripLocal, s.Orientation));
    }

    [TestMethod]
    public void TwoHanded_TheLeftHandReachesTheHandle_AndBothHandsHoldIt()
    {
        using var game = TwoHands(s => WeaponSwing.GuardCommand(s.ArmReach, SwingFamily.Bat));
        Assert.IsNull(game.Cpu.SupportOf(BoxingGame.WeaponHand), "the left hand starts away from the handle");
        Play(game, 1f);

        var (top, bottom) = Weapon.TwoHandGripsOf(WeaponKind.WoodenClub);
        Assert.AreEqual(BoxingGame.SupportHand, game.Cpu.SupportOf(BoxingGame.WeaponHand));
        Assert.IsTrue(game.Cpu.Wields(Hand.Left), "both gloves are off");
        Assert.IsLessThan(0.01f, ToGrip(game, BoxingGame.WeaponHand, top), "the right hand holds the top of the handle");
        Assert.IsLessThan(0.01f, ToGrip(game, BoxingGame.SupportHand, bottom), "the left hand holds below it");
    }

    [TestMethod]
    public void TwoHanded_TheHandsTurnTheClubToTheAngleAsked()
    {
        // 右手の指令の向き（棍棒を立てる → 前へ倒す）に、左手は握りの上の位置で付いていく。
        var forward = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
        var go = false;
        using var game = TwoHands(s => go ? Guard(Hand.Right, s.ArmReach) with { Orientation = forward } : WeaponSwing.GuardCommand(s.ArmReach, SwingFamily.Bat));
        Play(game, 1f);
        go = true;
        Play(game, 1f);
        Assert.IsGreaterThan(0.85f, Vector3.Dot(ClubAxis(game), game.Cpu.Frame.Forward), "the club points at the opponent");
        Assert.AreEqual(BoxingGame.SupportHand, game.Cpu.SupportOf(BoxingGame.WeaponHand), "the left hand is still on the handle");
    }

    [TestMethod]
    public void TwoHanded_LettingGo_ReleasesTheSupportingHandToo()
    {
        using var game = TwoHands(s => WeaponSwing.GuardCommand(s.ArmReach, SwingFamily.Bat));
        Play(game, 1f);
        game.Cpu.Unwield(BoxingGame.WeaponHand);
        Assert.IsFalse(game.Cpu.Wields(Hand.Left));
        Assert.IsNull(game.Cpu.SupportOf(BoxingGame.WeaponHand));
    }

    [TestMethod]
    public void TheClub_HitsItsOwnersHead_WhenTurnedBack()
    {
        // 拳を顔の前に置いて棍棒を真上近くへ立て、そこから手首を一気に返して、棍棒の頭を自分の頭（今いる所）へ向けて振り下ろす。
        var up = WeaponSwing.Orientation(MathF.PI, 1.3f); // 少し後ろへ傾けて立てる（向きを返すとき、同じ面の中で振り下ろす）
        BoxingGame? game = null;
        Quaternion AtOwnHead(in FighterSense s)
        {
            var me = game!.Cpu;
            var toHead = me.Frame.InverseRotate(me.HeadPosition + new Vector3(0f, 0.025f, 0f) - me.GloveCenter(BoxingGame.WeaponHand)); // 棍棒の重さで垂れる分、少し上を狙う
            return WeaponSwing.Orientation(MathF.Atan2(toHead.X, toHead.Z), MathF.Asin(Math.Clamp(toHead.Y / toHead.Length(), -1f, 1f)));
        }

        var turn = false;
        game = Armed(s => new HandCommand(new Vector3(0f, 0.3f, 0.6f) * s.ArmReach, false, Orientation: turn ? AtOwnHead(s) : up));
        using var owner = game;
        Play(game, 1f);
        var before = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var twist = 0f;
        turn = true;
        var own = new List<BodyPartKind>();
        for (var t = 0f; t < 1f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            own.AddRange(game.SelfWeaponHits.Select(h => h.Part));
            twist = MathF.Max(twist, game.SelfWeaponHits.Where(h => h.Part == BodyPartKind.Head).Select(h => h.HeadTwist).DefaultIfEmpty(0f).Max());
            Assert.IsFalse(game.WeaponHits.Any(h => h.Victim == game.Cpu), "hitting yourself is not a scoring blow");
        }

        CollectionAssert.Contains(own, BodyPartKind.Head, $"the club strikes its owner's head (hit: {string.Join(",", own)})");
        Assert.IsGreaterThan(0f, twist, "and twists it (concussion; it fades again within the second)");
        Assert.IsLessThan(before, game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), "and crushes some of it");
    }

    [TestMethod]
    public void TheWieldingHand_DoesNotPunch_AndTheClubDoesNotHitItsOwner()
    {
        // 構えている間、握りは握った手の前腕に触れ続けるが、自分を打ったことにはならない（両手でも）。
        using var game = Armed(s => Guard(Hand.Right, s.ArmReach));
        using var bat = TwoHands(s => WeaponSwing.GuardCommand(s.ArmReach, SwingFamily.Bat));
        var own = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var batOwn = bat.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var selfHits = 0;
        for (var t = 0f; t < 2f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            bat.Step(default, 1f / 60f);
            selfHits += game.SelfWeaponHits.Count + bat.SelfWeaponHits.Count;
        }

        Assert.AreEqual(0, selfHits, "holding the club in guard does not strike yourself");
        Assert.AreEqual(own, game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), "the grip does not crush the hand that holds it");
        Assert.AreEqual(batOwn, bat.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), "nor the two hands on a bat");
    }
}
