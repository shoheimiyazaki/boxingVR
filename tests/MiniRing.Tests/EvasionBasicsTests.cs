using System.Numerics;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>回避の基本動作: 腰から沈む体、外側へのスリップ、ブロックの下へ入る頭、打ち出したら直せない拳。</summary>
[TestClass]
public sealed class EvasionBasicsTests
{
    private const float Reach = 0.19f;
    private const float Dt = 1f / 240f;

    private static readonly DefenseMove SlipOutside = new()
    {
        Outside = true, HeadX = 0.3f, HeadY = -0.45f, Duration = 0.4f, Rate = 26f, HandsUp = 1f, GloveAnchor = 1f, VsStraight = 1f,
    };

    [TestMethod]
    public void Crouch_BendsTheLegs_SoTheHeadReallyGoesDown()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var idle = new FighterCommand(Vector3.Zero, f.Frame.Forward,
            new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        Run(world, f, idle, 1.5f);
        var head = f.HeadPosition.Y;
        var pelvis = f.PelvisPosition.Y;

        Run(world, f, idle with { HeadOffset = new Vector3(0f, -0.6f * f.ArmReach, 0f) }, 0.3f);

        // 以前は脚が歩きの角度のまま伸びていて、0.6 腕の長さを命じても 0.12 しか沈まなかった（回避の基本動作 B1）。
        Assert.IsGreaterThan(0.4f * f.ArmReach, head - f.HeadPosition.Y, $"head drop {(head - f.HeadPosition.Y) / f.ArmReach:0.00} R");
        // 2026-11-12: 沈み込みの最大を 0.4 → 0.28 にした（膝が床近くまで落ちてダウンと見分けにくかった）。頭は腰から折る分で下がる。
        Assert.IsGreaterThan(0.15f * f.ArmReach, pelvis - f.PelvisPosition.Y, "the hips go down, not just the head");
        Assert.AreEqual(FighterState.Standing, f.State);
    }

    [TestMethod]
    public void OutsideSlip_AgainstARightStraight_GoesToTheLeft_AndAgainstAJabToTheRight()
    {
        Assert.IsGreaterThan(0.02f, HeadAfterCue(SlipOutside, Hand.Right, PunchKind.Straight).X, "outside the right hand = own left (+X)");
        Assert.IsLessThan(-0.02f, HeadAfterCue(SlipOutside, Hand.Left, PunchKind.Straight).X, "outside the left hand = own right");
    }

    [TestMethod]
    public void WithoutOutside_OrAgainstHooks_TheHeadMovesAwayFromTheGlove()
    {
        Assert.IsLessThan(-0.02f, HeadAfterCue(SlipOutside with { Outside = false }, Hand.Right, PunchKind.Straight).X, "away from the right hand");
        Assert.IsLessThan(-0.02f, HeadAfterCue(SlipOutside with { VsHook = 1f }, Hand.Right, PunchKind.Hook).X, "the outside of a hook is where it comes from");
    }

    [TestMethod]
    public void GloveAnchor_KeepsTheGloveHeight_WhileTheHeadDropsUnderIt()
    {
        var anchored = GloveYWithHeadDropped(SlipOutside with { GloveAnchor = 1f });
        var following = GloveYWithHeadDropped(SlipOutside with { GloveAnchor = 0f });

        Assert.AreEqual(0.1f, anchored - following, 0.01f, "anchored gloves stay at the stance height (the head dropped 0.1 m)");
        // 腰から沈むと肩（体フレームの原点）も一緒に下がる。拳の高さはワールドで残す（レビュー指摘: 肩の高さで覚えていた）。
        Assert.AreEqual(0.1f, GloveYWithHeadDropped(SlipOutside with { GloveAnchor = 1f }, shouldersToo: true)
            - GloveYWithHeadDropped(SlipOutside with { GloveAnchor = 0f }, shouldersToo: true), 0.01f, "the gloves stay up when the whole body squats");
    }

    [TestMethod]
    public void KindOf_ReadsThePunchFromItsPath()
    {
        Assert.AreEqual(PunchKind.Straight, BoxerBot.KindOf(Technique.Cross));
        Assert.AreEqual(PunchKind.Straight, BoxerBot.KindOf(Technique.Jab));
        Assert.AreEqual(PunchKind.Hook, BoxerBot.KindOf(Technique.LeadHook));
        Assert.AreEqual(PunchKind.Uppercut, BoxerBot.KindOf(Technique.RearUppercut));
        Assert.AreEqual(PunchKind.Body, BoxerBot.KindOf(Technique.BodyJab));
    }

    [TestMethod]
    public void GuardHeight_CanGoAboveTheChin_UpToEyeLevel()
    {
        Assert.AreEqual(1.3f, (BotCatalog.Boxer with { GuardHeight = 1.3f }).Validated().GuardHeight);
        Assert.AreEqual(BotStyle.MaxGuardHeight, (BotCatalog.Boxer with { GuardHeight = 9f }).Validated().GuardHeight);

        var bot = new BoxerBot(BotCatalog.Boxer with { GuardHeight = 1.5f }, 1);
        var low = new BoxerBot(BotCatalog.Boxer with { GuardHeight = 1f }, 1);
        var head = new Vector3(0f, 0.3f * Reach, 0f);
        Assert.IsGreaterThan(low.Guard(Hand.Right, Reach, head).Y, bot.Guard(Hand.Right, Reach, head).Y);
    }

    [TestMethod]
    public void Punches_AreAimedWhenThrown_SoAnEarlySlipBeatsThem()
    {
        // 以前は拳が当たるまで頭を追い直していて、合図と同時に回避を始めても頭に当たった（回避の基本動作 B2）。
        // 右ストレートは外側（打つ腕の肩の側）から来るので、内側へ横下に外すのがいちばん近い（基本動作編 B10）。
        var inside = new DefenseMove { HeadX = 0.5f, HeadY = -0.5f, HeadZ = -0.2f, Step = 1f, Duration = 0.3f, Rate = 60f, HandsUp = 0.3f, VsStraight = 1f };
        var book = DefenseBook.Default.With(Defense.Duck, inside);
        // 相手は打つ前に溜める（狙いは溜めの始めに決まる）。溜めの始めに外せば、拳は頭のあった所を通る（予測編 P2）。
        var open = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: false, counters: false), seed: 5, seconds: 8f, telegraph: 0.08f);
        var slipped = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: true, counters: false), seed: 5, seconds: 8f, cue: 0f, telegraph: 0.08f);
        Console.WriteLine($"open head {open.HeadRate:P0}, slipped head {slipped.HeadRate:P0} evade {slipped.EvadeRate:P0}");

        Assert.IsGreaterThan(5, open.Punches);
        // 頭を人の比率（HeadScale 0.8）に小さくしてから、打たれてのけぞった頭は溜めの始めに決めた狙いから外れやすくなった（82 % → 71 %。ダメージ編 D2）。
        Assert.IsGreaterThan(0.6f, open.HeadRate, "standing still, the right straight mostly lands");
        Assert.IsLessThan(0.6f, slipped.HeadRate, "slipping as it is thrown, the head is often missed");
        Assert.IsGreaterThan(0.3f, slipped.EvadeRate, "and the punch passes without touching anything");
    }

    /// <summary>合図（相手の拳と種類）で守りを始め、何度か読んだ後の頭の指令（体フレーム、相手は正面）。</summary>
    private static Vector3 HeadAfterCue(DefenseMove move, Hand opponentHand, PunchKind kind)
    {
        var bot = new BoxerBot(BotCatalog.Boxer with { Attacks = false, Defenses = DefenseBook.Default.With(Defense.Duck, move) }, 1, Defense.Duck);
        var sense = Facing(new Vector3(0f, 1.06f, 0.01f));
        bot.Read(sense, Dt);
        bot.Cue(opponentHand, kind);
        var command = default(FighterCommand);
        for (var i = 0; i < 30; i++)
        {
            command = bot.Read(sense, Dt);
        }

        return command.HeadOffset;
    }

    /// <summary>守りを始めた後、頭が 0.1m 下がったとき（<paramref name="shouldersToo"/> なら肩も）の左の拳の目標の高さ（体フレーム）。</summary>
    private static float GloveYWithHeadDropped(DefenseMove move, bool shouldersToo = false)
    {
        var bot = new BoxerBot(BotCatalog.Boxer with { Attacks = false, Defenses = DefenseBook.Default.With(Defense.Duck, move) }, 1, Defense.Duck);
        bot.Read(Facing(new Vector3(0f, 1.06f, 0.01f)), Dt);
        bot.Cue(Hand.Right, PunchKind.Straight);
        return bot.Read(Facing(new Vector3(0f, 0.96f, 0.01f), shouldersToo ? 0.9f : 1f), Dt).Left.TargetLocal.Y;
    }

    /// <summary>肩の高さ <paramref name="shoulderY"/> で +Z を向いた自分と、正面で構えたまま動かない相手。</summary>
    private static FighterSense Facing(Vector3 selfHead, float shoulderY = 1f) => new(new BodyFrame(new Vector3(0f, shoulderY, 0f), 0f), Reach,
        OpponentHead: new Vector3(0f, 1.06f, 0.24f), OpponentChest: new Vector3(0f, 0.98f, 0.24f), OpponentAlive: true,
        SelfHead: selfHead,
        OpponentLeft: new GloveSense(new Vector3(-0.03f, 1.0f, 0.14f), Vector3.Zero),
        OpponentRight: new GloveSense(new Vector3(0.03f, 1.0f, 0.17f), Vector3.Zero));

    private static void Run(PhysicsWorld world, Fighter fighter, FighterCommand command, float seconds)
    {
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            fighter.ApplyControl(command, Dt);
            world.Step(Dt);
        }
    }
}
