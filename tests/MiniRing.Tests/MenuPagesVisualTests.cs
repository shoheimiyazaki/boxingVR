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

    private static void Render(HeadlessRenderer renderer, MatchMenu menu, string name)
    {
        var size = new Vector2(Width, Height);
        var text = menu.Text(size);
        Assert.IsTrue(ScreenText.BuildQuads(text).Count > 0);
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
        using var menu = new MatchMenu(replays, settings, loadPortraits: true);
        menu.Portraits.Complete();
        Assert.AreEqual(menu.State.Roster.Count, menu.Portraits.ReadyCount, "every character has a portrait");
        using var renderer = new HeadlessRenderer(Width, Height);

        // 最初の画面（カーソルは観戦）。
        menu.Handle(Move(y: 1));
        Render(renderer, menu, "menu_01_top.png");

        // 設定（レフェリーなしに切り替えた後）。
        menu.Handle(Move(y: 1));
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
        for (var i = 0; i < 4; i++) { menu.Handle(Move(y: 1)); }
        Render(renderer, menu, "menu_07_select_practice_scrolled.png");
        menu.Handle(new MenuInput(Confirm: true));
        menu.Handle(Move(x: 1));
        Render(renderer, menu, "menu_08_select_practice_blue.png");

        // IVF の大会: 赤を決めると、青は同じ階級の人だけ。
        menu.State.Return();
        menu.Handle(new MenuInput(Confirm: true));
        while (menu.State.ModeAt(menu.State.ModeIndex)?.Org?.Id != "ivf") { menu.Handle(Move(y: 1)); }
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_09_select_ivf_red.png");
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_10_select_ivf_blue.png");

        // VFC の大会（武器あり・檻）。
        menu.State.Return();
        menu.Handle(new MenuInput(Confirm: true));
        while (menu.State.ModeAt(menu.State.ModeIndex)?.Org?.Id != "vfc") { menu.Handle(Move(y: 1)); }
        Render(renderer, menu, "menu_11_mode_vfc.png");
        menu.Handle(new MenuInput(Confirm: true));
        Render(renderer, menu, "menu_12_select_vfc.png");

        // 対戦（練習試合・赤だけ決定）。
        menu.State.Return();
        menu.Handle(Move(x: 1));
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
    }
}
