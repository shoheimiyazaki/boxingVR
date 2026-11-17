using MiniRing.Bots;
using MiniRing.Desktop;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 試合の刀・ロングソードは、その武器の型で動く（CPU もデスクトップのプレイヤーの意図操作も）。
/// 以前は刀もロングソードも片手剣の汎用の振り（<see cref="SwordTechnique.Basics"/>）だった。
/// </summary>
[TestClass]
public sealed class FormSwordsmanTests
{
    private const float Dt = 1f / 60f;

    private static readonly string[] KatanaNames = ["tsuki", "kesa", "gyaku-kesa", "nuki-do", "kiriage", "harai-men", "hiki-men", "shomen", "migi-nagi", "hidari-nagi", "gyaku-kiriage", "gyakufu", "kote", "kote-men", "do-tsuki", "katate-tsuki", "tsuka-ate", "oshi-giri", "hiki-do", "mae-geri"];
    private static readonly string[] LongswordNames = ["oberhau", "zornhau", "zwerchhau", "unterhau", "stich", "scheitelhau", "schielhau", "krumphau", "zwerchhau-left", "unterhau-left", "stich-low", "knaufschlag", "schnitt", "tritt"];

    private static (string[] Own, string[] Other) Names(WeaponKind kind) => kind == WeaponKind.Katana ? (KatanaNames, LongswordNames) : (LongswordNames, KatanaNames);

    [TestMethod]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    public void BothCornersFightWithTheirOwnWeaponForms(WeaponKind kind)
    {
        var corners = BoxingCorners.Default with
        {
            RedBotStyle = BotCatalog.Style("champion"),
            PlayerWeapon = kind,
            CpuWeapon = kind,
            PlayerTwoHanded = true,
            CpuTwoHanded = true,
            UseWeaponBots = true,
        };
        using var game = new BoxingGame(seed: 3, corners: corners);
        var used = new HashSet<string>();
        for (var step = 0; step < 40 * 60 && !game.Player.IsDead && !game.Cpu.IsDead; step++)
        {
            game.Step(default, Dt);
            foreach (var control in new[] { game.CpuControl, game.PlayerControl })
            {
                Assert.IsInstanceOfType<DisarmedFallback>(control, "武器のボットは、武器を落としたら拳で戦う入れ物に入っている");
                if (((DisarmedFallback)control!).Armed is SwordsmanBot bot)
                {
                    used.UnionWith(bot.Used);
                }
                else
                {
                    Assert.Fail("刀・ロングソードは剣士のボット（型で戦う）。片手剣の汎用の振りではない");
                }
            }
        }

        var (own, other) = Names(kind);
        Console.WriteLine($"{kind} used: {string.Join(",", used)}");
        Assert.IsNotEmpty(used, "40 秒の間に、少なくとも 1 つは技を出す");
        CollectionAssert.IsSubsetOf(used.ToList(), own, $"出した技 {string.Join(",", used)} は {kind} の型の名前");
        Assert.IsFalse(used.Overlaps(other), $"{kind} に他の武器の型の名前が出ない");
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    public void TechniquesComeFromTheForms(WeaponKind kind)
    {
        using var game = new BoxingGame();
        var techniques = FormSwordsman.Techniques(kind, game.Player.ArmReach);
        var (own, _) = Names(kind);
        Console.WriteLine($"{kind}: {string.Join(", ", techniques.Select(t => $"{t.Name}@{t.Range:0.00}"))}");
        Assert.IsNotEmpty(techniques);
        CollectionAssert.IsSubsetOf(techniques.Select(t => t.Name).ToList(), own);
        Assert.IsTrue(techniques.All(t => t.Weapon == kind && t.Range >= FormSwordsman.MinReach), "間合いは物打ちが届く距離で、体が触れ合う近さではない");
        Assert.IsTrue(techniques.All(t => t.Motion.Frames.All(f => !float.IsNaN(f.Right.X))));
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana, "tsuki", "kesa")]
    [DataRow(WeaponKind.Longsword, "stich", "oberhau")]
    public void PlayerLightAndHeavyAreTheWeaponsForms(WeaponKind kind, string light, string heavy)
    {
        var forms = FormSwordsman.PlayerForms(kind);
        Assert.AreEqual(light, forms.Light.Name);
        Assert.AreEqual(heavy, forms.Heavy.Name);
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana, true)]
    [DataRow(WeaponKind.Katana, false)]
    [DataRow(WeaponKind.Longsword, true)]
    public void PlayerSwingsWithBothHandsKeepingTheGripSpacing(WeaponKind kind, bool heavy)
    {
        using var game = new BoxingGame(corners: BoxingCorners.Default with { PlayerWeapon = kind, PlayerTwoHanded = true });
        var control = new DesktopFighterControl(kind);
        var sense = game.PlayerSense;
        var spacing = FormSwordsman.Spacing(kind);
        var before = control.Read(default, sense, Dt);
        var start = new FighterIntent(default, Light: !heavy, Heavy: heavy);
        var seen = new List<Vector3Pair>();
        var command = control.Read(start, sense, Dt);
        seen.Add(new(command.Left.TargetLocal, command.Right.TargetLocal));
        var moved = 0f;
        for (var i = 0; i < 90; i++)
        {
            command = control.Read(default, sense, Dt);
            seen.Add(new(command.Left.TargetLocal, command.Right.TargetLocal));
            moved = MathF.Max(moved, (command.Right.TargetLocal - before.Right.TargetLocal).Length());
        }

        Assert.IsGreaterThan(0.3f * sense.ArmReach, moved, "型を出すと右手が構えから大きく動く");
        foreach (var pair in seen)
        {
            Assert.AreEqual(spacing, (pair.Right - pair.Left).Length() / sense.ArmReach, 0.01f, "両手は握りの間隔を保つ");
        }
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    public void EveryFormFrameKeepsTheGripSpacing(WeaponKind kind)
    {
        var spacing = FormSwordsman.Spacing(kind);
        foreach (var form in FormSwordsman.Forms(kind))
        {
            foreach (var frame in form.Frames)
            {
                Assert.AreEqual(spacing, (frame.Right - frame.Left).Length(), 0.005f, form.Name);
            }
        }
    }

    private readonly record struct Vector3Pair(System.Numerics.Vector3 Left, System.Numerics.Vector3 Right);
}
