using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// メニューの各ページを GPU（Vulkan）で PNG に描く（2026-11-16）。出力: リポジトリ直下の <c>.visualcheck/menu_*.png</c>。
/// PNG があること ≠ 正しい。中身を見て（顔が顔に見えるか・背の高さ太さが見て分かるか・文字が収まるか）確かめる。ローカル専用・CI 外。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class MenuPagesVisualTests
{
    private const int Width = 960;
    private const int Height = 540;

    private static string OutDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, ".visualcheck");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>撮った画面で、1 倍（16 px）でも収まらずに末尾を切った文字・OS のフォントの文字（ドットのフォントでない）。配置の直し漏れ。</summary>
    private static readonly List<string> Unfit = [];

    private static void Render(HeadlessRenderer renderer, MatchMenu menu, string name)
    {
        var size = new Vector2(Width, Height);
        var text = menu.Text(size);
        Assert.IsTrue(ScreenText.BuildQuads(text).Count > 0);
        Unfit.AddRange(text.Where(t => t.Font != TextFont.Pixel || t.Text.Contains(MenuText.Cut, StringComparison.Ordinal) || t.PixelHeight < UiFont.Cell)
            .Select(t => $"{name}: {t.Text} ({t.Font}, {t.PixelHeight} px)").Distinct());
        var pixels = renderer.RenderPrimitiveScene(Vector4.Zero, Matrix4x4.Identity, [], [], text, shapes: menu.Shapes(size));
        PngWriter.WriteRgba(Path.Combine(OutDir(), name), Width, Height, pixels);
    }

    private static MenuInput Move(int x = 0, int y = 0) => new(x, y);

    /// <summary>肖像（顔・全身）を大きな PNG に書き出す（ドット絵そのものを目で確かめるため）。</summary>
    [TestMethod]
    public void Portraits_AreWrittenLarge_ForInspection()
    {
        const int Scale = 6;
        var ids = new[] { "jin", "marcus", "ramon", "konrad", "akira", "joao" };
        var images = ids.Select(id => Characters.CharacterPortraits.Build(Characters.CharacterRoster.Get(id)).Body).ToList();
        var faces = ids.Select(id => Characters.CharacterPortraits.Build(Characters.CharacterRoster.Get(id)).Face).ToList();
        var cellW = (images.Max(i => i.Width) + 2) * Scale / 2;
        var width = ids.Length * cellW;
        var height = (images.Max(i => i.Height) * Scale / 2) + (faces.Max(f => f.Height) * Scale * 2) + 16;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (20, 40, 60, 255); }
        void Blit(Characters.PortraitImage image, int originX, int originY, int scale, bool blue)
        {
            for (var y = 0; y < image.Height * scale; y++)
            {
                for (var x = 0; x < image.Width * scale; x++)
                {
                    var c = image.ColorAt(x / scale, y / scale, blue);
                    if (c.W == 0f) { continue; }
                    var at = (((originY + y) * width) + originX + x) * 4;
                    (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = ((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), 255);
                }
            }
        }

        for (var n = 0; n < ids.Length; n++)
        {
            Blit(faces[n], (n * cellW) + 4, 4, Scale * 2, false);
            Blit(images[n], (n * cellW) + 4, (faces.Max(f => f.Height) * Scale * 2) + 8, Scale / 2, n % 2 == 1);
        }

        PngWriter.WriteRgba(Path.Combine(OutDir(), "menu_portraits_large.png"), width, height, pixels);
    }

    [TestMethod]
    public void EveryMenuPage_Renders()
    {
        var outDir = OutDir();
        var replays = Path.Combine(outDir, "menu-test-replays");
        Directory.CreateDirectory(replays);
        foreach (var name in new[] { "miniring_desktop_20261116_101500", "miniring_desktop_20261115_220102", "miniring_vr_20261114_193344" })
        {
            File.WriteAllBytes(Path.Combine(replays, name + ".mrrep"), []);
        }

        var settings = Path.Combine(outDir, "menu-test-settings.json");
        File.Delete(settings);
        var campaign = Path.Combine(outDir, "menu-test-campaign");
        if (Directory.Exists(campaign)) { Directory.Delete(campaign, recursive: true); }
        using var menu = new MatchMenu(replays, settings, loadPortraits: true, campaignDirectory: campaign);
        menu.Portraits.Complete();
        menu.Previews.Synchronous = true; // 見込みの全身の絵（作る・減量）をその場で作る
        Assert.AreEqual(menu.State.Roster.Count, menu.Portraits.ReadyCount, "every character has a portrait");
        using var renderer = new HeadlessRenderer(Width, Height);

        // 最初の画面（カーソルはキャンペーン）。
        menu.Handle(Move(x: 1));
        Render(renderer, menu, "menu_01_top.png");
        menu.Handle(new MenuInput(Pointer: MenuLayout.TopButtonCenter((int)TopItem.Replays))); // マウスで指す（ホバー）
        Render(renderer, menu, "menu_01b_top_hover.png");
        menu.Handle(Move(y: -1));
        menu.Handle(Move(x: 1));
        Assert.AreEqual((int)TopItem.Campaign, menu.State.TopIndex);

        // 設定（レフェリーなしに切り替えた後）。
        menu.Handle(Move(y: 1)); // 設定（2 行目の右）
        Assert.AreEqual((int)TopItem.Settings, menu.State.TopIndex);
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Settings, menu.State.Page);
        Render(renderer, menu, "menu_02_settings_referee_on.png");
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_03_settings_referee_off.png");
        menu.Handle(new MenuInput(Cancel: true));

        // ゲームモード（シングル → 練習試合の行・IVF の大会の行）。
        menu.State.Return();
        while (menu.State.TopIndex != 0) { menu.Handle(Move(y: -1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Free, menu.State.Page);
        Render(renderer, menu, "menu_03b_free_battle.png");
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Mode, menu.State.Page);
        menu.Handle(Move(x: 1)); // 練習試合の決まり: 標準 → 次の団体
        Render(renderer, menu, "menu_04_mode_practice.png");
        menu.Handle(Move(x: -1));
        while (menu.State.ModeAt(menu.State.ModeIndex)?.Org?.Id != "ivf") { menu.Handle(Move(y: 1)); }
        Render(renderer, menu, "menu_05_mode_ivf.png");

        // キャラ選択（練習試合: 全員。赤を選んでいるところ・下へ送ったところ）。
        while (menu.State.ModeIndex != 0) { menu.Handle(Move(y: -1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Select, menu.State.Page);
        Render(renderer, menu, "menu_06_select_practice_top.png");
        menu.Handle(new MenuInput(Pointer: MouseLayout.CornerBackCenter)); // 隅の戻るを指す
        Render(renderer, menu, "menu_06b_select_back_hover.png");
        for (var i = 0; i < 4; i++) { menu.Handle(Move(y: 1)); }
        Render(renderer, menu, "menu_07_select_practice_scrolled.png");
        menu.Handle(new MenuInput(Confirm: true));
        menu.Handle(Move(x: 1));
        Render(renderer, menu, "menu_08_select_practice_blue.png");

        // IVF の大会: 赤を決めると、青は同じ階級の人だけ。
        menu.State.Return();
        menu.Handle(new MenuInput(Confirm: true));
        menu.Handle(new MenuInput(Confirm: true));
        while (menu.State.ModeAt(menu.State.ModeIndex)?.Org?.Id != "ivf") { menu.Handle(Move(y: 1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_09_select_ivf_red.png");
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_10_select_ivf_blue.png");

        // VFC の大会（武器あり・檻）。
        menu.State.Return();
        menu.Handle(new MenuInput(Confirm: true));
        menu.Handle(new MenuInput(Confirm: true));
        while (menu.State.ModeAt(menu.State.ModeIndex)?.Org?.Id != "vfc") { menu.Handle(Move(y: 1)); }
        Render(renderer, menu, "menu_11_mode_vfc.png");
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_12_select_vfc.png");

        // 対戦（練習試合・赤だけ決定）。
        menu.State.Return();
        menu.Handle(new MenuInput(Confirm: true));
        menu.Handle(Move(y: 1));
        menu.Handle(new MenuInput(Confirm: true));
        while (menu.State.ModeIndex != 0) { menu.Handle(Move(y: -1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MatchMode.Versus, menu.State.Mode);
        menu.Handle(Move(x: 2));
        menu.Handle(new MenuInput(Confirm: true));
        menu.Handle(Move(x: 1, y: 1));
        Render(renderer, menu, "menu_13_select_versus.png");

        // リプレイ。
        menu.State.Return();
        while (menu.State.TopIndex != (int)TopItem.Replays) { menu.Handle(Move(y: 1)); if (menu.State.TopIndex == (int)TopItem.Replays) { break; } menu.Handle(Move(x: 1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Replays, menu.State.Page);
        Assert.AreEqual(3, menu.State.ReplayCount);
        Render(renderer, menu, "menu_14_replays.png");

        // キャンペーン: 空の一覧 → 作る（重量級・高め）→ ボクサーのページ（大会の並び・出られない理由・体重）。
        menu.Handle(new MenuInput(Cancel: true));
        while (menu.State.TopIndex != (int)TopItem.Campaign) { menu.Handle(Move(x: 1)); if (menu.State.TopIndex == (int)TopItem.Campaign) { break; } menu.Handle(Move(y: 1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Campaign, menu.State.Page);
        Render(renderer, menu, "menu_15_campaign_empty.png");
        menu.Handle(new MenuInput(Confirm: true)); // 新しく作る
        Assert.AreEqual(MenuPage.Create, menu.State.Page);
        menu.Handle(Move(x: 3)); // 体型を回す
        menu.Handle(Move(y: 1));
        menu.Handle(Move(x: 4)); // 身長 +4 cm
        Render(renderer, menu, "menu_16_campaign_create.png");
        menu.Handle(new MenuInput(Pointer: MouseLayout.CreateArrow(0, 1), Click: true, Held: true)); // マウス: 体型の ▶ を押して指したまま
        Render(renderer, menu, "menu_16b_campaign_create_mouse.png");
        menu.Handle(new MenuInput());
        menu.Handle(Move(y: 1)); // 身長の行へ戻す（後のキーボードの手順と同じ位置）
        menu.Handle(Move(y: 2));
        menu.Handle(new MenuInput(Confirm: true)); // 作る
        Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
        var now = 0.0;
        menu.Clock = () => now; // 札の動きは決めた時刻で撮る（見せ始めから 5 秒: 動き終わった絵）
        Render(renderer, menu, "menu_17_campaign_home.png");

        // マウスだけで: 団体と大会 → VFC → 武器を選べる段を次の試合に → 札の ▶ を指す（武器の ◀ ▶）。
        menu.Handle(new MenuInput(Pointer: CampaignLayout.HomeCenter(MenuState.HomeOrgs), Click: true));
        menu.Handle(new MenuInput(Pointer: CampaignLayout.OrgCenter(CampaignPlan.Orgs.ToList().FindIndex(o => o.Id == "vfc")), Click: true));
        Assert.AreEqual(MenuPage.Path, menu.State.Page);
        var armed = menu.State.PathStops.ToList().FindIndex(s => s.Open && s.Entry is { Weapons.Count: > 1 });
        Assert.IsGreaterThanOrEqualTo(0, armed, "VFC lets a new boxer choose a weapon");
        menu.Handle(new MenuInput(Pointer: CampaignLayout.NodeCenter(armed), Click: true));
        Assert.IsTrue(menu.State.WeaponChoosable);
        menu.Handle(new MenuInput(Pointer: MouseLayout.WeaponArrow(1), Click: true));
        Render(renderer, menu, "menu_17b_campaign_home_weapon_mouse.png");
        menu.State.OpenBoxer(menu.State.Boxer!.Id); // 次の試合を自動へ戻す（後の手順はキーボードで同じ位置から）
        menu.Handle(Move(y: 1));
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Orgs, menu.State.Page);
        Render(renderer, menu, "menu_18_campaign_orgs.png");
        menu.Handle(new MenuInput(Cancel: true));
        menu.Handle(Move(x: 1)); // 体重を変える
        menu.Handle(new MenuInput(Confirm: true));
        Assert.AreEqual(MenuPage.Weight, menu.State.Page);
        menu.Handle(Move(x: -9));
        Render(renderer, menu, "menu_19_campaign_weight_cut.png");
        // マウス: スライダーを右へドラッグ（増量）して放し、+5 kg を指す。
        menu.Handle(new MenuInput(Pointer: new Vector2(CampaignLayout.SliderLeft, CampaignLayout.SliderY), Click: true, Held: true));
        var (lo, hi) = menu.State.Boxer!.WeightRange;
        menu.Handle(new MenuInput(Pointer: new Vector2(CampaignLayout.SliderX(menu.State.Boxer!.WeightKg + 12, lo, hi), CampaignLayout.SliderY), Held: true));
        menu.Handle(new MenuInput(Pointer: CampaignLayout.StepCenter(3)));
        Render(renderer, menu, "menu_19b_campaign_weight_gain_mouse.png");
        menu.Handle(new MenuInput(Cancel: true));

        // 戦績のあるボクサー（IVF の自分の階級で 3 勝・5 位）: 道の開いた所と閉じた所が混ざる。
        var store = new MiniRing.Arena.Campaign.CampaignStore(campaign, [.. menu.State.Roster.Select(c => c.Id)]);
        var fresh = menu.State.Boxer!;
        var ivf = MiniRing.Arena.Organizations.Get("ivf");
        var division = ivf.DivisionOf(fresh.WeightKg).Id;
        var bouts = Enumerable.Range(0, 3).Select(i => new MiniRing.Arena.Campaign.CampaignBout($"ivf-rookie-{division}", "ivf", division,
            menu.State.Roster[i].Id, MiniRing.Arena.Campaign.BoutResult.Win, i == 0, fresh.WeightKg, null, null));
        var seasoned = fresh with { Career = fresh.Career.WithSeat("ivf", division, 5) with { Bouts = [.. bouts] } };
        store.Save(seasoned);
        menu.State.SetBoxers(store.LoadAll());
        menu.State.OpenBoxer(seasoned.Id);
        Render(renderer, menu, "menu_20_campaign_home_seasoned.png");
        menu.Handle(Move(y: 1));
        menu.Handle(new MenuInput(Confirm: true));
        while (menu.State.PathOrg.Id != "ivf") { menu.Handle(Move(y: -1)); }
        menu.Handle(new MenuInput(Confirm: true)); // IVF の道
        Assert.AreEqual(MenuPage.Path, menu.State.Page);
        Assert.IsTrue(menu.State.PathStops.Any(s => s.Open) && menu.State.PathStops.Any(s => !s.Open), "the path mixes open and closed steps");
        Render(renderer, menu, "menu_21_campaign_path_ivf.png");
        menu.Handle(Move(x: 1));
        Render(renderer, menu, "menu_21b_campaign_path_ivf_title_closed.png");
        menu.Handle(new MenuInput(Cancel: true));
        menu.Handle(new MenuInput(Cancel: true));

        // 試合の後の札（前後の差から作る物を、決めた中身で撮る）。
        var rookie = MiniRing.Arena.Campaign.CampaignEvents.Get($"ivf-ranking-{division}");
        menu.State.Celebrate([
            new CampaignCard.Result(rookie.Name, MiniRing.Arena.Campaign.BoutResult.Win, true, menu.State.Roster[3].HudName, (3, 0, 0), (4, 0, 0), "5 位 → 1 位で挑戦: " + rookie.Name),
            new CampaignCard.RankUp($"IVF {ivf.DivisionOf(fresh.WeightKg).Name}", 12, 8, menu.State.Roster[4].HudName),
            new CampaignCard.Unlocked([rookie, MiniRing.Arena.Campaign.CampaignEvents.Get("wvc-ranking")]),
            new CampaignCard.Title($"IVF {ivf.DivisionOf(fresh.WeightKg).Name}", menu.State.Roster[5].HudName, false),
            new CampaignCard.Result(rookie.Name, MiniRing.Arena.Campaign.BoutResult.Loss, false, menu.State.Roster[6].HudName, (4, 0, 0), (4, 1, 0), "あと 1 勝で開く: WVC ランキング戦"),
        ]);
        string[] cards = ["menu_22_campaign_card_win.png", "menu_23_campaign_card_rankup.png", "menu_24_campaign_card_unlocked.png", "menu_25_campaign_card_title.png", "menu_26_campaign_card_loss.png"];
        foreach (var name in cards)
        {
            _ = menu.Shapes(new Vector2(Width, Height)); // この札を見せ始める
            now += 5.0;
            Render(renderer, menu, name);
            menu.Handle(new MenuInput(Confirm: true));
        }

        Assert.AreEqual(0, menu.State.Cards.Count);
        menu.Handle(new MenuInput(Cancel: true));
        Assert.AreEqual(MenuPage.Campaign, menu.State.Page);
        Render(renderer, menu, "menu_27_campaign_list.png");
        Assert.AreEqual(0, Unfit.Count, "every label fits at 1× (16 px) or larger:" + Environment.NewLine + string.Join(Environment.NewLine, Unfit.Distinct()));
    }
}
