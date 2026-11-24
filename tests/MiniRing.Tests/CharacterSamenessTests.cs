using System.Text.Json.Nodes;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 教育はキャラの差分だけで、空なら教科書そのもの・届くべきボットにだけ届く（CLAUDE.md §1.8・§1.6）。
/// <para>
/// 2026-11-20: 名簿の試合 14 組の最終チェックサムを持つ 4 つの表（金型）をやめ、同じビルドの中の相対比較にした。金型は PhysicsVersion を上げるたびに
/// <see cref="CharacterSamenessProbe"/> で全部を取り直し、枝のマージのたびにぶつかっていた。ここで確かめるのは、版に依らない次の関係だけ:
/// (a) 教育が空のキャラの試合 ≡ 教科書を直接渡した試合、(b) 戦略の層の重みを 0 にした試合 ≡ 層が無い試合、
/// (c) 教育（戦略の層）は拳のボットにだけ届き、剣・こんぼうのボットと人が操る側の試合は変えない。
/// 決定性そのもの（同じ種・同じ入力ならビット単位で同じ）は ReplayTests・FixedSimulationTests が見る。
/// 「名簿を 1 人 1 ファイルにする前の試合とビット単位で同じ」という歴史の証明は捨てた（古い試合が再現できなくなるのは正しい: §4.3）。
/// </para>
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class CharacterSamenessTests
{
    /// <summary>試合の長さ [s]。拳のボットの戦略の層が試合を変えるのに足りる長さ（人形の単位で 20 秒の試合の途中 10 秒で。いまは 16.78 秒、層の有無でチェックサムが分かれていた）。</summary>
    private const float Seconds = 16.78f;

    /// <summary>代表の組: 拳同士（層を使う marcus と使わない dmitri）、剣同士、こんぼう対 拳（層を外した albert）、人が操る側に層のある 1P。</summary>
    private static readonly CharacterSamenessCases.Case FistPair = new("fist-fist", MatchMode.Spectate, "marcus", "dmitri", false, Seconds);
    private static readonly CharacterSamenessCases.Case SwordPair = new("katana-longsword", MatchMode.Spectate, "jin", "konrad", false, Seconds);
    private static readonly CharacterSamenessCases.Case ClubPair = new("club-fist", MatchMode.Spectate, "sean", "albert", false, Seconds);
    private static readonly CharacterSamenessCases.Case PlayerPair = new("single-player-vs-fist", MatchMode.Single, "marcus", "dmitri", false, Seconds);

    /// <summary>同じ組・同じ教育の試合は何度回しても同じなので、テストの間で使い回す（クラスの中だけ）。</summary>
    private static readonly Dictionary<(string Case, string Variant), (ulong Half, ulong End)> Runs = [];

    private static (ulong Half, ulong End) Run(CharacterSamenessCases.Case c, string variant, Func<FighterCharacter, FighterCharacter> educate)
    {
        lock (Runs)
        {
            if (!Runs.TryGetValue((c.Name, variant), out var result))
            {
                var selection = new MatchSelection(c.Mode, educate(CharacterRoster.Get(c.Red)), educate(CharacterRoster.Get(c.Blue)), c.Referee);
                result = CharacterSamenessCases.Play(selection.Corners, c.Seconds);
                Runs[(c.Name, variant)] = result;
            }

            return result;
        }
    }

    private static (ulong Half, ulong End) AsInTheRoster(CharacterSamenessCases.Case c) => Run(c, "roster", ch => ch);

    private static (ulong Half, ulong End) WithoutGamePlan(CharacterSamenessCases.Case c) => Run(c, "no-plan", CharacterSamenessCases.WithoutGamePlan);

    private static FighterCharacter WithEducation(FighterCharacter c, Action<JsonObject> edit)
    {
        var style = (JsonObject)c.Education.Style.DeepClone();
        edit(style);
        return c with { Education = c.Education with { Style = style } };
    }

    /// <summary>
    /// 倒れた相手への攻め（GroundStrike）の好みは、名前で引いた教科書そのものではなく、解決して JSON を通した（検証済みの）スタイルから読む。
    /// その好みの値が、教科書（キャラなら教科書に教育を重ねた物）と同じ（値域の丸めで変わらない）ことを全スタイル・全キャラで確かめる。
    /// 2026-11-24: VFC のキャラは教育に倒れた相手への攻めの好みを持つ（LogicVersion 66）ので、比べる先を教育を重ねたスタイルにした。
    /// </summary>
    [TestMethod]
    public void GroundPreferences_OfTheResolvedStyle_EqualTheTextbooks()
    {
        static float[] Ground(BotStyle s) => [s.GroundDrive, s.GroundLoad, s.GroundCut, s.GroundStomp, s.GroundKick, s.GroundHeel, s.GroundRest, s.Southpaw ? 1f : 0f];
        foreach (var textbook in BotCatalog.Styles)
        {
            var viaCorners = BotCatalog.Corners(textbook).CpuStyle!;
            CollectionAssert.AreEqual(Ground(textbook), Ground(viaCorners), textbook.Name);
        }

        foreach (var c in CharacterRoster.All)
        {
            var viaCorners = new MatchSelection(MatchMode.Spectate, c, CharacterRoster.Get("albert")).Corners;
            CollectionAssert.AreEqual(Ground(c.Resolve("albert")), Ground(viaCorners.RedBotStyle!), c.Id);
            CollectionAssert.AreEqual(Ground(c.Resolve("albert")), Ground(viaCorners.PlayerStyle!), c.Id);
        }
    }

    /// <summary>
    /// (a) 教育を空にしたキャラの試合（名簿・メニューと同じ道: <see cref="MatchSelection.Corners"/>）は、<see cref="MatchSelection"/> を通さずに
    /// 教科書のスタイル・武器の教科書と、キャラの体・顔・装備だけをボットのコーナーへ直接渡した試合とビット単位で同じ。
    /// キャラの道が、体・顔・装備のほかに何かをボットへ足したら（例: 体から引いたスタイルの調整）ここで分かれる。
    /// </summary>
    [TestMethod]
    public void EmptyEducation_PlaysTheSameMatch_AsTheTextbookPassedDirectly()
    {
        foreach (var c in new[] { FistPair, SwordPair })
        {
            var empty = Run(c, "empty", ch => ch with { Education = CharacterEducation.Empty });

            var red = CharacterRoster.Get(c.Red);
            var blue = CharacterRoster.Get(c.Blue);
            var redTextbook = BotCatalog.Style(red.StyleName);
            var direct = BotCatalog.Corners(BotCatalog.Style(blue.StyleName)) with
            {
                RedBotStyle = c.Mode == MatchMode.Spectate ? redTextbook : null,
                PlayerStyle = redTextbook,
                PlayerName = red.StyleName,
                PlayerBody = red.Build,
                PlayerFace = red.Look.Parts,
                PlayerLook = red.Look,
                PlayerFaceSpec = red.Face,
                PlayerLabel = red.HudName,
                PlayerWear = red.Wear,
                PlayerWeapon = red.Weapon,
                PlayerWeaponStyle = WeaponTextbook(red.Weapon),
                PlayerTwoHanded = FormSwordsman.Handles(red.Weapon),
                CpuBody = blue.Build,
                CpuFace = blue.Look.Parts,
                CpuLook = blue.Look,
                CpuFaceSpec = blue.Face,
                CpuLabel = blue.HudName,
                CpuWear = blue.Wear,
                CpuWeapon = blue.Weapon,
                CpuWeaponStyle = WeaponTextbook(blue.Weapon),
                CpuTwoHanded = FormSwordsman.Handles(blue.Weapon),
                UseWeaponBots = true,
                Referee = c.Referee,
            };

            Assert.AreEqual(empty, CharacterSamenessCases.Play(direct, c.Seconds), c.Name);
        }

        static WeaponStyle? WeaponTextbook(Weapons.WeaponKind? weapon) => WeaponCatalog.HasBot(weapon) ? WeaponCatalog.Textbook(weapon!.Value) : null;
    }

    /// <summary>(b) 戦略の層（<c>education.style.GamePlan</c>）の重みを 0 にすると、層が無い試合とビット単位で同じ（相手の表を読んでも判断を変えない）。</summary>
    [TestMethod]
    public void StrategyLayerTurnedOff_PlaysTheSameMatch_AsNoLayer()
    {
        var off = Run(FistPair, "plan-weight-0", ch => WithEducation(ch, style =>
        {
            if (style["GamePlan"] is JsonObject plan)
            {
                plan["Weight"] = 0;
            }
        }));
        Assert.AreEqual(WithoutGamePlan(FistPair), off);
        Assert.IsNotNull(CharacterRoster.Get(FistPair.Red).Education.Style["GamePlan"], "the case needs a fighter whose education has a game plan");
    }

    /// <summary>
    /// (c) 名簿の教育の戦略の層は、層を使う拳のボット（marcus）の試合を変え、剣・こんぼうのボット（jin・konrad・sean）、層を外した拳のボット（albert）、
    /// 人が操る側（1P の marcus）の試合は変えない。
    /// </summary>
    [TestMethod]
    public void GamePlanEducation_ReachesOnlyTheFistBotsThatUseIt()
    {
        Assert.AreNotEqual(WithoutGamePlan(FistPair).End, AsInTheRoster(FistPair).End, "the game plan reaches the fist bot");
        foreach (var c in new[] { SwordPair, ClubPair, PlayerPair })
        {
            Assert.AreEqual(WithoutGamePlan(c), AsInTheRoster(c), "no fist bot using the layer, no change: " + c.Name);
        }
    }
}
