using MiniRing.Arena;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-26: キャラ選択の分け方（名簿のファイルの値から）・ボクシングの単位・人数が増えても崩れない格子・ゲームモード（練習試合・団体の大会）。
/// 期待はキャラ名でなく値の関係で書く（名簿が増えても壊れない）。
/// </summary>
[TestClass]
public sealed class MenuCompetitionTests
{
    private static readonly MenuInput Confirm = new(Confirm: true);
    private static MenuInput Dir(int x = 0, int y = 0) => new(x, y);

    private static MenuState AtMode(MatchMode mode = MatchMode.Single)
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default);
        while (state.TopIndex != (int)mode) { state.Apply(Dir(x: 1)); if (state.TopIndex == (int)mode) { break; } state.Apply(Dir(y: 1)); }
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Mode, state.Page);
        return state;
    }

    private static MenuState AtTournament(string orgId, MatchMode mode = MatchMode.Single)
    {
        var state = AtMode(mode);
        while (state.ModeAt(state.ModeIndex)?.Org?.Id != orgId || !state.ModeAt(state.ModeIndex)!.Tournament) { state.Apply(Dir(y: 1)); }
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Select, state.Page);
        return state;
    }

    [TestMethod]
    public void Categories_ComeFromTheFileValues_BodyThenHandsThenTextbook()
    {
        foreach (var c in CharacterRoster.All)
        {
            var category = FighterCategories.Of(c);
            var style = c.Style;
            if (c.Build.FourLegged || c.Wear == HandWear.SickleClaw)
            {
                Assert.AreEqual(FighterCategory.Creature, category, c.Id);
            }
            else if (c.Weapon is not null || c.Wear is not (HandWear.Glove or HandWear.None))
            {
                Assert.AreEqual(FighterCategory.Armed, category, c.Id);
            }
            else
            {
                var expected = style.Grapple is not null ? FighterCategory.Grappler : style.KickRate > 0f ? FighterCategory.Kicker : FighterCategory.Boxer;
                Assert.AreEqual(expected, category, c.Id);
            }
        }

        // 今の名簿には全部の分け方がいる（画面に 5 つの節が出る）。
        CollectionAssert.AreEquivalent(FighterCategories.Order.ToList(), CharacterRoster.All.Select(FighterCategories.Of).Distinct().ToList());
    }

    [TestMethod]
    public void Tape_UsesBoxingUnits_ConsistentWithTheVisibleBody()
    {
        foreach (var c in CharacterRoster.All)
        {
            var tape = FighterTape.Of(c.Build);
            Assert.AreEqual(CharacterProfile.HeightOf(c.Build), tape.HeightCm, c.Id);
            Assert.AreEqual(CharacterProfile.WeightOf(c.Build), tape.WeightKg, c.Id);
            Assert.IsInRange(0, 11, tape.Inches, c.Id);
            var inchesCm = ((tape.Feet * 12) + tape.Inches) * FighterTape.CmPerInch;
            Assert.IsLessThanOrEqualTo(FighterTape.CmPerInch, MathF.Abs(inchesCm - tape.HeightCm), $"{c.Id}: {tape.HeightText}");
            Assert.IsLessThanOrEqualTo(1.6f, MathF.Abs((tape.WeightLb * FighterTape.KgPerPound) - tape.WeightKg), $"{c.Id}: {tape.WeightText}");
        }

        var taller = CharacterRoster.All.MaxBy(c => c.Build.Height)!;
        var shorter = CharacterRoster.All.MinBy(c => c.Build.Height)!;
        Assert.IsGreaterThan(FighterTape.Of(shorter.Build).HeightCm, FighterTape.Of(taller.Build).HeightCm);
    }

    [TestMethod]
    public void Grid_HoldsEveryoneOnce_GroupedByCategory_AndScrollsToReachEveryTileWithoutOverlap()
    {
        var roster = CharacterRoster.All;
        var grid = new SelectGrid(roster);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, roster.Count).ToList(), grid.Order.ToList(), "everyone appears exactly once");
        // 節は分け方の並び（一度出た分け方は後で戻ってこない）。
        var seen = grid.Order.Select(i => grid.Categories[i]).ToList();
        CollectionAssert.AreEqual(seen.Distinct().ToList(), FighterCategories.Order.Where(seen.Contains).ToList());
        for (var i = 1; i < seen.Count; i++) { Assert.IsLessThanOrEqualTo((int)seen[i], (int)seen[i - 1], "categories never come back"); }

        foreach (var index in grid.Order)
        {
            grid.Reveal(index);
            var c = grid.TileCenter(index);
            Assert.IsNotNull(c, roster[index].Id);
            Assert.IsGreaterThanOrEqualTo(SelectGrid.Left, c.Value.X - (SelectGrid.TileSize.X / 2));
            Assert.IsLessThanOrEqualTo(SelectGrid.Right, c.Value.X + (SelectGrid.TileSize.X / 2));
            Assert.IsGreaterThanOrEqualTo(SelectGrid.Top, c.Value.Y - (SelectGrid.TileSize.Y / 2));
            Assert.IsLessThanOrEqualTo(SelectGrid.Bottom, c.Value.Y + (SelectGrid.TileSize.Y / 2));
            var shown = grid.VisibleRows().SelectMany(v => grid.Rows[v.Row].Members).Select(m => grid.TileCenter(m)!.Value).ToList();
            for (var a = 0; a < shown.Count; a++)
            {
                for (var b = a + 1; b < shown.Count; b++)
                {
                    var d = System.Numerics.Vector2.Abs(shown[a] - shown[b]);
                    Assert.IsTrue(d.X >= SelectGrid.TileSize.X || d.Y >= SelectGrid.TileSize.Y, "tiles do not overlap");
                }
            }
        }

        // 人数を 3 倍にしても、格子の中に収まって送れる（崩れない）。
        var many = roster.Concat(roster).Concat(roster).ToList();
        var big = new SelectGrid(many);
        Assert.IsGreaterThan(grid.MaxFirstRow, big.MaxFirstRow);
        big.Reveal(big.Order[^1]);
        Assert.IsNotNull(big.TileCenter(big.Order[^1]));
    }

    [TestMethod]
    public void Mode_PracticeCyclesTheRules_TournamentsComeFromTheOrgFiles()
    {
        var state = AtMode();
        Assert.AreEqual(Organizations.All.Count + 2, state.ModeRows, "practice + one row per org file + back");
        Assert.IsNull(state.ModeAt(0)!.Org, "practice starts with the standard rules");
        state.Apply(Dir(x: 1));
        Assert.AreEqual(state.Organizations[0], state.ModeAt(0)!.Org);
        Assert.IsFalse(state.ModeAt(0)!.Tournament);
        state.Apply(Dir(x: -1));
        Assert.IsNull(state.ModeAt(0)!.Org);
        CollectionAssert.AreEquivalent(Organizations.All.Keys.ToList(),
            Enumerable.Range(1, Organizations.All.Count).Select(r => state.ModeAt(r)!.Org!.Id).ToList());
        Assert.IsNull(state.ModeAt(state.ModeRows - 1), "the last row goes back");
        state.Apply(Dir(y: -1));
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Top, state.Page);
    }

    [TestMethod]
    public void Practice_WithAnOrgsRules_AnyoneCanFace_AndTheMatchGetsThatOrgsRulesAndStage()
    {
        var state = AtMode();
        var vfc = Organizations.Get("vfc");
        while (state.ModeAt(0)!.Org != vfc) { state.Apply(Dir(x: 1)); }
        state.Apply(Confirm);
        // IVF の選手（グローブ・ボクシングの教科書）と武器の選手を組める。
        var boxer = CharacterRoster.All.First(c => FighterCategories.Of(c) == FighterCategory.Boxer);
        var armed = CharacterRoster.All.First(c => c.Weapon is not null);
        Assert.IsTrue(state.RedAllowed(CharacterRoster.IndexOf(boxer.Id)));
        Assert.IsTrue(state.BlueAllowed(CharacterRoster.IndexOf(armed.Id)));
        var match = new MatchSelection(MatchMode.Single, boxer, armed, true, state.Competition);
        Assert.AreEqual(armed.Weapon, match.Corners.CpuWeapon);
        Assert.AreEqual(boxer.Wear, match.Corners.PlayerWear, "practice keeps the fighter's own hands");
        Assert.AreEqual(ArenaShape.HexCage, match.Corners.Ring!.Shape, "VFC's cage from its file");
        var rules = match.RulesOr(null)!;
        Assert.AreEqual(vfc.CountsDowns, rules.CountsDowns);
        Assert.AreEqual(vfc.Match.JudgesDecision, rules.JudgesDecision);
        Assert.IsFalse(match.Corners.Referee, "VFC has no referee");
        // 標準の練習試合は今までと同じ（決まりは渡したまま・場は変えない）。
        var standard = new MatchSelection(MatchMode.Single, boxer, armed);
        Assert.IsNull(standard.RulesOr(null));
        Assert.IsNull(standard.Corners.Ring);
    }

    [TestMethod]
    public void Ivf_OnlyEligibleFightersCanBePicked_AndBlueMustShareRedsDivision()
    {
        var state = AtTournament("ivf");
        var ivf = Organizations.Get("ivf");
        Assert.IsTrue(state.RedAllowed(state.RedCursor), "the cursor starts on someone who can enter");
        var roster = state.Roster;
        var armed = roster.Select((c, i) => (c, i)).First(p => p.c.Weapon is not null).i;
        Assert.IsFalse(state.RedAllowed(armed), "IVF has no weapons");
        // カーソルは出られない人を飛ばす。
        for (var step = 0; step < roster.Count; step++)
        {
            state.Apply(Dir(x: 1));
            Assert.IsNull(OrgBouts.Ineligible(ivf, roster[state.RedCursor]), roster[state.RedCursor].Id);
        }

        state.Apply(Confirm);
        Assert.IsTrue(state.RedLocked);
        var redDivision = ivf.DivisionOf(OrgBouts.WeightOf(roster[state.RedCursor]));
        Assert.AreEqual(redDivision, ivf.DivisionOf(OrgBouts.WeightOf(roster[state.BlueCursor])), "blue jumps to someone in red's division");
        for (var step = 0; step < roster.Count; step++)
        {
            state.Apply(Dir(x: 1));
            Assert.AreEqual(redDivision, ivf.DivisionOf(OrgBouts.WeightOf(roster[state.BlueCursor])), roster[state.BlueCursor].Id);
        }

        // 階級の違う人をポインタで指して決めても、始まらずに理由を知らせる。
        var other = Enumerable.Range(0, roster.Count).FirstOrDefault(i => i != state.RedCursor && OrgBouts.Ineligible(ivf, roster[i]) is null
            && ivf.DivisionOf(OrgBouts.WeightOf(roster[i])) != redDivision && state.Grid.TileCenter(i) is not null, -1);
        if (other >= 0)
        {
            var refused = state.Apply(new MenuInput(Pointer: state.Grid.TileCenter(other)!.Value, Click: true));
            Assert.AreEqual(MenuAction.None, refused.Action);
            Assert.IsNotNull(refused.Notice);
            Assert.IsFalse(state.BlueLocked);
            state.Apply(Dir(x: 1));
        }

        var start = state.Apply(Confirm);
        Assert.AreEqual(MenuAction.StartMatch, start.Action);
        Assert.IsTrue(start.Match!.Event.Tournament);
        Assert.AreEqual(ivf.Entry.Gear, start.Match.Corners.PlayerWear, "IVF puts its gloves on");
        Assert.AreEqual(ivf.Match.KnockdownLimit, start.Match.RulesOr(null)!.KnockdownLimit);
    }

    [TestMethod]
    public void LockingRedFarDownTheList_ScrollsToShowBluesCursor()
    {
        // 赤を下の節（最後の人）で決めると、青のカーソル（上の節のまま）が見えるところまで送る（レビューの指摘: 青が画面の外に残っていた）。
        var state = AtMode();
        state.Apply(Confirm); // 練習試合
        var last = state.Grid.Order[^1];
        while (state.RedCursor != last) { state.Apply(Dir(x: 1)); }
        Assert.IsNotNull(state.Grid.TileCenter(state.RedCursor));
        state.Apply(Confirm);
        Assert.IsTrue(state.RedLocked);
        Assert.IsNotNull(state.Grid.TileCenter(state.BlueCursor), "blue's cursor is on screen after red locks");
        // マウスが動いていても（札を指していなければ）キーの移動で送りが付いてくる。
        for (var i = 0; i < state.Grid.Order.Count; i++)
        {
            state.Apply(new MenuInput(X: 1, Pointer: new System.Numerics.Vector2(5f, 5f)));
            Assert.IsNotNull(state.Grid.TileCenter(state.BlueCursor), state.Roster[state.BlueCursor].Id);
        }
    }

    [TestMethod]
    public void Tournament_RefusesAnIneligiblePick_WithAReason()
    {
        var state = AtTournament("ivf");
        var roster = state.Roster;
        var armed = Enumerable.Range(0, roster.Count).First(i => roster[i].Weapon is not null);
        state.Grid.Reveal(armed);
        var outcome = state.Apply(new MenuInput(Pointer: state.Grid.TileCenter(armed)!.Value, Click: true));
        Assert.IsFalse(state.RedLocked);
        Assert.Contains("武器", outcome.Notice!);
    }
}
