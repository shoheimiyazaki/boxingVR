using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// IVF（階級制のボクシング団体、2026-11-19）と 3 団体の整理: 団体のファイルの階級・出場の条件・王座の呼び方・ランキングの点、
/// 計量（体重は見えている体から）と同じ階級だけの組み合わせ、王座とランキングの記録の読み書きと試合の結果での動き。
/// 試合を回す 3 ノックダウン制は <see cref="IvfKnockdownTests"/>。
/// </summary>
[TestClass]
public sealed class IvfTests
{
    private static Organization Ivf => Organization.Of("ivf");

    [TestMethod]
    public void Ivf_IsAWeightClassBoxingOrg_DistinctFromWvcAndVfc()
    {
        var ivf = Ivf;
        Assert.AreEqual("Interesting Voxing Federation", ivf.Name);
        Assert.AreEqual("Voxing", ivf.Sport);
        Assert.IsTrue(ivf.HasWeightClasses);
        Assert.AreEqual(3, ivf.Match.KnockdownLimit);
        Assert.AreEqual(HandWear.Glove, ivf.Entry.Gear);
        Assert.IsFalse(ivf.Entry.Weapons);
        Assert.IsFalse(ivf.Ranking.MembersOnly, "IVF は所属を問わず計量で振り分ける");

        // WVC・VFC は無差別の 1 階級、3 ノックダウン制なし、所属の選手だけの順位。
        foreach (var id in new[] { "wvc", "vfc" })
        {
            var org = Organization.Of(id);
            Assert.IsFalse(org.HasWeightClasses, id);
            Assert.AreEqual(0, org.Match.KnockdownLimit, id);
            Assert.IsTrue(org.Ranking.MembersOnly, id);
            Assert.IsNull(org.Entry.Gear, id);
        }

        Assert.AreEqual(0f, Organization.Of("vfc").Ranking.Draw, "VFC は両者生還の引き分けを点にしない");
        Assert.AreEqual(3, TvShowRules.Rules(5, 10f, ivf).KnockdownLimit);
        Assert.AreEqual(0, TvShowRules.Rules(5, 10f, Organization.Of("wvc")).KnockdownLimit);
    }

    [TestMethod]
    public void Divisions_AreLightestFirst_AndEveryWeightHasOne()
    {
        var ivf = Ivf;
        Assert.AreEqual("ライト級", ivf.DivisionOf(50).Name);
        Assert.AreEqual("ライト級", ivf.DivisionOf(64).Name, "上限ちょうどはその階級");
        Assert.AreEqual("ミドル級", ivf.DivisionOf(65).Name);
        Assert.AreEqual("ライトヘビー級", ivf.DivisionOf(80).Name);
        Assert.AreEqual("クルーザー級", ivf.DivisionOf(91).Name);
        Assert.AreEqual("ヘビー級", ivf.DivisionOf(300).Name);
        Assert.AreEqual("IVF ミドル級王座", ivf.BeltName(ivf.Division("middle")));
        Assert.AreEqual("ミドル級タイトルマッチ", ivf.BoutName(ivf.Division("middle"), titleBout: true));
        Assert.AreEqual("ミドル級", ivf.BoutName(ivf.Division("middle"), titleBout: false));
        Assert.AreEqual("", Organization.Of("wvc").BoutName(Organization.Of("wvc").Divisions[0], titleBout: false));
    }

    [TestMethod]
    public void OrgFile_RejectsUnorderedOrOpenEndedDivisions()
    {
        var json = OrgJson("ivf");
        Assert.ThrowsExactly<InvalidDataException>(() => Load(json.Replace("\"limitKg\": 73", "\"limitKg\": 60", StringComparison.Ordinal)), "軽い順でない");
        Assert.ThrowsExactly<InvalidDataException>(() => Load(json.Replace("\"limitKg\": 91", "\"limitKg\": null", StringComparison.Ordinal)), "上限なしが最後でない");
        Assert.ThrowsExactly<InvalidDataException>(() => Load(json.Replace("\"knockdownLimit\": 3", "\"knockdownLimit\": -1", StringComparison.Ordinal)));
        Assert.ThrowsExactly<InvalidDataException>(() => Load(json.Replace("\"gear\": \"glove\"", "\"gear\": \"boxing-glove\"", StringComparison.Ordinal)));
        Assert.ThrowsExactly<InvalidDataException>(() => Load(json.Replace("\"swarmer\",", "\"wrestler\",", StringComparison.Ordinal)));
        Assert.AreEqual("ivf", Load(json).Id);
    }

    [TestMethod]
    public void WeighIn_UsesTheVisibleBody_AndOnlySameClassBoutsAreMade()
    {
        var ivf = Ivf;
        var ramon = CharacterRoster.Get("ramon");
        var albert = CharacterRoster.Get("albert");
        var marcus = CharacterRoster.Get("marcus");
        Assert.AreEqual(ramon.Profile!.WeightKg, OrgBouts.WeightOf(ramon), "計量 = 見えている体の体重（プロフィールと同じ）");

        var (red, blue, division) = OrgBouts.Enter(ivf, ramon, albert);
        Assert.AreEqual("middle", division.Id);
        Assert.AreEqual(HandWear.Glove, blue.Wear, "素手のアルバートにもグローブを着ける");
        Assert.AreEqual(albert.Build, blue.Build, "体はそのまま");

        var mismatch = Assert.ThrowsExactly<ArgumentException>(() => OrgBouts.Enter(ivf, ramon, marcus));
        StringAssert.Contains(mismatch.Message, "104 kg");
        StringAssert.Contains(mismatch.Message, "ヘビー級");

        // 武器の選手・蹴る教科書の選手は IVF に出られない。WVC は無差別で、どの組み合わせも組める（今までの中継のまま）。
        Assert.IsNotNull(OrgBouts.Ineligible(ivf, CharacterRoster.Get("konrad")));
        Assert.IsNotNull(OrgBouts.Ineligible(ivf, CharacterRoster.Get("akira")));
        var (wvcRed, _, open) = OrgBouts.Enter(Organization.Of("wvc"), CharacterRoster.Get("jin"), CharacterRoster.Get("dmitri"));
        Assert.AreEqual(WeaponKind.Katana, wvcRed.Weapon);
        Assert.AreEqual("open", open.Id);
    }

    [TestMethod]
    public void Pool_SplitsTheRosterByWeight_RespectingMembership()
    {
        var ivf = OrgBouts.Pool(Ivf, CharacterRoster.All);
        Assert.AreEqual("albert,joao,ramon,vikram", string.Join(",", ivf["middle"].Select(c => c.Id)));
        Assert.AreEqual("dmitri,marcus", string.Join(",", ivf["heavy"].Select(c => c.Id)));
        Assert.IsEmpty(ivf["light"]);

        var wvc = OrgBouts.Pool(Organization.Of("wvc"), CharacterRoster.All);
        Assert.IsTrue(wvc["open"].All(c => c.Org == "wvc"));
    }

    [TestMethod]
    public void Standings_RoundTrip_AndRejectUnknownOrDuplicateFighters()
    {
        var ivf = Ivf;
        var known = CharacterRoster.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var standings = OrgStandings.Empty(ivf) with { AsOf = "2026-11-19", Basis = "test" };
        standings = standings with { Divisions = [.. standings.Divisions.Select(d => d.Division == "heavy" ? d with { Champion = "marcus", Ranking = ["dmitri"] } : d)] };
        var back = OrgStandings.FromJson(standings.ToJson(), ivf, known);
        Assert.AreEqual("marcus", back.For(ivf.Division("heavy")).Champion);
        Assert.AreEqual("王者", back.SeatOf("marcus"));
        Assert.AreEqual("1位", back.SeatOf("dmitri"));
        Assert.IsNull(back.SeatOf("ramon"));

        var json = standings.ToJson();
        Assert.ThrowsExactly<InvalidDataException>(() => OrgStandings.FromJson(json.Replace("\"dmitri\"", "\"nobody\"", StringComparison.Ordinal), ivf, known));
        Assert.ThrowsExactly<InvalidDataException>(() => OrgStandings.FromJson(json.Replace("\"dmitri\"", "\"marcus\"", StringComparison.Ordinal), ivf, known));
        Assert.ThrowsExactly<InvalidDataException>(() => OrgStandings.FromJson(json, Organization.Of("wvc"), known));

        // 組み込みの記録はどの団体も読める（無ければ空）。
        foreach (var org in Organizations.All.Values)
        {
            Assert.AreEqual(org.Divisions.Count, OrgStandings.Of(org).Divisions.Count, org.Id);
        }
    }

    [TestMethod]
    public void AfterBout_TheBeltMovesOnlyWhenTheChampionLoses()
    {
        var ivf = Ivf;
        var heavy = ivf.Division("heavy");
        var middle = ivf.Division("middle");
        var s = OrgStandings.Empty(ivf) with
        {
            Divisions = [.. OrgStandings.Empty(ivf).Divisions.Select(d => d.Division switch
            {
                "heavy" => d with { Champion = "marcus", Ranking = ["dmitri"] },
                "middle" => d with { Champion = "albert", Ranking = ["joao", "ramon", "vikram"] },
                _ => d,
            })],
        };

        var defended = s.AfterBout(heavy, "marcus", "dmitri").For(heavy);
        Assert.AreEqual("marcus", defended.Champion);
        Assert.AreEqual("dmitri", string.Join(",", defended.Ranking));

        var upset = s.AfterBout(heavy, "dmitri", "marcus").For(heavy);
        Assert.AreEqual("dmitri", upset.Champion);
        Assert.AreEqual("marcus", string.Join(",", upset.Ranking), "前の王者は 1 位へ");

        var climb = s.AfterBout(middle, "vikram", "joao").For(middle);
        Assert.AreEqual("albert", climb.Champion);
        Assert.AreEqual("vikram,joao,ramon", string.Join(",", climb.Ranking), "上の相手に勝てばそのすぐ上へ");

        var expected = s.AfterBout(middle, "joao", "vikram").For(middle);
        Assert.AreEqual("joao,ramon,vikram", string.Join(",", expected.Ranking), "上が下に勝っても順位は変わらない");
    }

    [TestMethod]
    public void Standings_NeverSeatOneFighterTwice()
    {
        var ivf = Ivf;
        var known = CharacterRoster.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var s = OrgStandings.Empty(ivf).WithSeat(new DivisionStanding("middle", "albert", ["joao"]));
        Assert.ThrowsExactly<ArgumentException>(() => s.AfterBout(ivf.Division("middle"), "albert", "albert"), "同じ選手の勝ち負けは記録しない");

        // 体重が変わって階級を移った選手（部分的な順位付け）: 新しい席に入れば、前の階級から外れる。
        var moved = s.WithSeat(new DivisionStanding("lightheavy", "joao", []));
        Assert.AreEqual("", string.Join(",", moved.For(ivf.Division("middle")).Ranking));
        Assert.AreEqual("albert", moved.For(ivf.Division("middle")).Champion);
        var movedChampion = moved.WithSeat(new DivisionStanding("heavy", null, ["albert"]));
        Assert.IsNull(movedChampion.For(ivf.Division("middle")).Champion, "移った王者の王座は空位に");
        Assert.AreEqual("heavy", OrgStandings.FromJson(movedChampion.ToJson(), ivf, known).Divisions.First(d => d.Ranking.Contains("albert")).Division);
    }

    [TestMethod]
    public void LeagueTable_UsesTheOrgPoints_AndFinishBonus()
    {
        var fighters = new[] { CharacterRoster.Get("albert"), CharacterRoster.Get("joao"), CharacterRoster.Get("ramon") };
        OrgLeague.Bout B(string red, string blue, string? winner, bool finish) => new(red, blue, 1, winner, finish, 10f, 10f, "");
        var bouts = new[]
        {
            B("albert", "joao", "albert", finish: false),
            B("joao", "ramon", "joao", finish: true),
            B("ramon", "albert", null, finish: false),
        };
        var table = OrgLeague.Table(Ivf.Ranking, fighters, bouts);
        // IVF: 勝ち 3・引き分け 1・倒せば +1 → albert 4、joao 4、ramon 1。albert と joao の直接対決は albert の勝ち。
        Assert.AreEqual("albert,joao,ramon", string.Join(",", table.Select(r => r.Id)));
        Assert.AreEqual(4f, table[1].Points);

        var seat = OrgLeague.Seat(new DivisionStanding("middle", null, []), table, crownVacant: true);
        Assert.AreEqual("albert", seat.Champion, "空位なら 1 位が王者");
        var kept = OrgLeague.Seat(new DivisionStanding("middle", "ramon", []), table, crownVacant: true);
        Assert.AreEqual("ramon", kept.Champion, "王座は総当たりでは動かない");
        Assert.AreEqual("albert,joao", string.Join(",", kept.Ranking));
    }

    [TestMethod]
    public void TvResult_NamesTheThreeKnockdownTko()
    {
        var r = new TvResult(1, "TKO — 3 KNOCKDOWNS  RED WIN", 2, 12f, [], []);
        Assert.AreEqual("T.K.O.", r.Method);
        Assert.AreEqual("1 ラウンドに 3 度のダウン", r.Cause);
        Assert.IsFalse(r.ByDecision);
    }

    [TestMethod]
    public void IvfEmblem_IsRegistered_AndDraws()
    {
        CollectionAssert.Contains(Organizations.EmblemIds.ToArray(), "ivf");
        var text = new List<Aigis.Render.ScreenTextDraw>();
        var shapes = new List<Aigis.Render.UiShapeDraw>();
        Emblems.Draw(Ivf, text, shapes, new System.Numerics.Vector2(640f, 200f), 10f);
        Assert.IsGreaterThan(100, shapes.Count);
        Assert.IsTrue(shapes.All(s => float.IsFinite(s.CenterPixels.X) && float.IsFinite(s.CenterPixels.Y)));
    }

    private static string OrgJson(string id)
    {
        var path = Path.Combine(RepoRoot(), "games", "MiniRing.Arena", "Orgs", $"{id}.json");
        return File.ReadAllText(path);
    }

    private static Organization Load(string json) => Organizations.FromFiles([("ivf.json", json)])["ivf"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("リポジトリの根が見つかりません。");
    }
}

/// <summary>3 ノックダウン制（<see cref="MatchRules.KnockdownLimit"/>）: 1 ラウンドに 3 度のダウンで TKO。0 なら止めない（WVC の今の試合のまま）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class IvfKnockdownTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void ThirdKnockdownInARound_StopsTheBout_OnlyWithTheLimit()
    {
        var (limited, downsLimited) = KnockDownRepeatedly(MatchRules.Tv with { RoundSeconds = 120f, Rounds = 1, KnockdownLimit = 3 });
        Console.WriteLine($"limit 3: {limited.Message} after {downsLimited} downs");
        Assert.IsTrue(limited.RoundOver, limited.Message);
        StringAssert.StartsWith(limited.Message, "TKO — 3 KNOCKDOWNS");
        Assert.AreEqual(3, downsLimited);
        Assert.AreEqual(1, limited.PlayerWins, "倒された青の負け");
        limited.Dispose();

        var (open, downsOpen) = KnockDownRepeatedly(MatchRules.Tv with { RoundSeconds = 120f, Rounds = 1 });
        Console.WriteLine($"no limit: {open.Message} after {downsOpen} downs");
        Assert.IsGreaterThanOrEqualTo(3, downsOpen, "同じ倒れ方で 3 度以上倒れても");
        Assert.IsFalse(open.Message.StartsWith("TKO", StringComparison.Ordinal), "上限が無ければ TKO にならない");
        open.Dispose();
    }

    /// <summary>青が立ち直るたびに軽い脳しんとうを与えて倒す（4 度まで）。取られたダウンの数を返す。</summary>
    private static (BoxingGame Game, int Downs) KnockDownRepeatedly(MatchRules rules)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)), rules: rules);
        var downs = 0;
        var wasDown = false;
        var upFor = 0f;
        for (var t = 0f; t < 100f && !game.RoundOver && downs < 4; t += Dt)
        {
            game.Step(default, Dt);
            var down = game.Cpu.IsKnockedDown;
            downs += down && !wasDown ? 1 : 0;
            wasDown = down;
            upFor = game.Cpu.State == FighterState.Standing && !down ? upFor + Dt : 0f;
            if (upFor > 1f)
            {
                game.Cpu.Concussion = 1f; // 倒れて約 4.5 秒で立てる深さ（0.8 では倒れず、1.3 では 9 秒かかる: 測った値）
                upFor = 0f;
            }
        }

        Assert.AreEqual(downs, game.RoundKnockdowns(game.Cpu), "ゲームが数えたダウンと同じ");
        return (game, downs);
    }
}
