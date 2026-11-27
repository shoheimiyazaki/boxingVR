using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>
/// メニューの文字の大きさ・折り返し（DotGothic16、<see cref="UiFont"/>）。高さは 16 px の整数倍（最小 16 px）に寄せ、幅の決まった所はその幅に収まる倍率へ下げる。
/// 1 倍（16 px）より小さくはしない（小さく潰れた字を出さない）: 1 倍でも収まらない文は、配置で広げるか折る（<see cref="Wrap"/>）。
/// </summary>
internal static class MenuText
{
    /// <summary>1 倍でも幅に収まらなかったときに、末尾を切って付ける印（配置の直し漏れ。visual テストが 0 件を確かめる）。</summary>
    public const char Cut = '…';

    /// <summary>
    /// <paramref name="pixels"/> の高さで描く高さ [px] と文字。<paramref name="maxWidth"/> &gt; 0 なら、その幅に収まる一番大きい整数倍
    /// （頼んだ高さに一番近い倍率から下げる）。1 倍でも収まらないときは縮めず、末尾を <see cref="Cut"/> で切る（読める大きさのまま）。
    /// </summary>
    public static (float Pixels, string Text) Fit(string value, float pixels, float maxWidth)
    {
        var dots = UiFont.Ensure();
        var scale = dots.ScaleFor(pixels);
        if (maxWidth <= 0f) { return (scale * UiFont.Cell, value); }
        var width = dots.MeasureDots(value);
        while (scale > 1 && width * scale > maxWidth) { scale--; }
        if (width * scale <= maxWidth) { return (scale * UiFont.Cell, value); }
        var keep = value.Length;
        while (keep > 0 && dots.MeasureDots(value[..keep] + Cut) > maxWidth) { keep--; }
        return (UiFont.Cell, value[..keep] + Cut);
    }

    /// <summary>
    /// 人の名前を、「・」の区切りで <paramref name="maxWidth"/> [px] の行に詰める（引用符の中の通り名 <c>"ザ・ハンマー"</c> は分けない）。
    /// 1 つの区切りだけで幅を超えるときは <see cref="Wrap"/> で字の途中で折る。
    /// </summary>
    public static IEnumerable<string> WrapName(string name, float maxWidth, float pixels)
    {
        ArgumentNullException.ThrowIfNull(name);
        var parts = new List<string>();
        var quoted = false;
        var start = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '"') { quoted = !quoted; }
            if (name[i] == '・' && !quoted) { parts.Add(name[start..(i + 1)]); start = i + 1; }
        }

        if (start < name.Length) { parts.Add(name[start..]); }
        var line = "";
        foreach (var part in parts)
        {
            if (line.Length > 0 && UiFont.Width(line + part, pixels) > maxWidth)
            {
                yield return line;
                line = "";
            }

            if (UiFont.Width(part, pixels) > maxWidth)
            {
                foreach (var piece in Wrap(part, maxWidth, pixels)) { yield return piece; }
                continue;
            }

            line += part;
        }

        if (line.Length > 0) { yield return line; }
    }

    /// <summary>
    /// 説明の文を、<paramref name="pixels"/> の高さで描いて 1 行が <paramref name="maxWidth"/> [px] に収まるように折る
    /// （実際の字形の幅で測る。行の後半に句読点・空白があればその直後で折る）。
    /// </summary>
    public static IEnumerable<string> Wrap(string text, float maxWidth, float pixels)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rest = text;
        while (rest.Length > 0 && UiFont.Width(rest, pixels) > maxWidth)
        {
            var fits = 1;
            while (fits < rest.Length && UiFont.Width(rest[..(fits + 1)], pixels) <= maxWidth) { fits++; }
            var cut = rest.LastIndexOfAny(['、', '。', '）', '・', ' '], fits - 1);
            cut = cut >= fits / 2 ? cut + 1 : fits;
            while (cut < rest.Length && rest[cut] is '。' or '、' or '）') { cut++; } // 行の頭に句読点を置かない（少しはみ出しても前の行へ）
            yield return rest[..cut];
            rest = rest[cut..].TrimStart(' ');
        }

        if (rest.Length > 0) { yield return rest; }
    }
}

/// <summary>画面の大きさに合わせて設計の画面（640 × 464）を置く。</summary>
internal readonly record struct MenuCanvas(Vector2 Size)
{
    public static readonly Vector2 DesignSize = new(MenuLayout.Width, MenuLayout.Height);
    public float Scale => MathF.Min(Size.X / DesignSize.X, Size.Y / DesignSize.Y);
    public Vector2 At(float x, float y) => (Size - (DesignSize * Scale)) / 2 + (new Vector2(x, y) * Scale);
    public Vector2 At(Vector2 point) => At(point.X, point.Y);

    /// <summary>画面の位置（ピクセル）を設計の座標へ戻す（指す位置の判定用）。</summary>
    public Vector2 ToDesign(Vector2 pixel) => Scale > 0f ? (pixel - ((Size - (DesignSize * Scale)) / 2)) / Scale : new Vector2(-1000f);
}

/// <summary>
/// メニューの描画（状態 <see cref="MenuState"/> から図形と文字を作るだけ。状態を変えない）。最初の画面・設定・キャラ選択・リプレイの各ページ。
/// 顔・全身の絵は、試合の体のボクセルを正面から写した四角の並び（<see cref="CharacterPortraits"/>）をそのまま四角で描く。
/// </summary>
internal sealed partial class MenuView(MenuState state, PortraitSet portraits, Func<IReadOnlyList<string>> replayNames, PreviewPortraits previews)
{
    private static readonly Vector4 Shadow = new(0.02f, 0.04f, 0.12f, 1f);
    private static readonly Vector4 Pale = new(0.85f, 0.92f, 0.95f, 1f);
    private static readonly Vector4 Dim = new(0.55f, 0.66f, 0.72f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.79f, 0.30f, 1f);
    private static readonly Vector4 Cyan = new(0.26f, 0.8f, 1f, 1f);
    private static readonly Vector4 RedSide = new(1f, 0.40f, 0.34f, 1f);
    private static readonly Vector4 BlueSide = new(0.40f, 0.62f, 1f, 1f);

    /// <summary>画面の下に出す、エラー・知らせ。</summary>
    public string? Notice { get; set; }

    public IReadOnlyList<UiShapeDraw> Shapes(Vector2 size)
    {
        var (shapes, _) = Build(size);
        return shapes;
    }

    public IReadOnlyList<ScreenTextDraw> Text(Vector2 size)
    {
        var (_, text) = Build(size);
        return text;
    }

    private (List<UiShapeDraw> Shapes, List<ScreenTextDraw> Text) Build(Vector2 size)
    {
        var canvas = new MenuCanvas(size);
        var shapes = new List<UiShapeDraw> { UiShapeDraw.Rounded(size / 2, size, 0f, Shadow) };
        var text = new List<ScreenTextDraw>();
        var s = canvas.Scale;

        void Plate(float x, float y, float w, float h, Vector4 top, Vector4 bottom, float radius = 0f) =>
            shapes.Add(UiShapeDraw.VerticalGradient(canvas.At(x, y), new Vector2(w, h) * s, radius * s, top, bottom));
        void Frame(float x, float y, float w, float h, Vector4 color, float thickness, float radius = 0f) =>
            shapes.Add(UiShapeDraw.Outline(canvas.At(x, y), new Vector2(w, h) * s, radius * s, color, thickness * s));
        // 文字は DotGothic16（16 px の整数倍。MenuText.Fit）。maxWidth があれば、その幅に収まる一番大きい倍率へ（1 倍より小さくはしない）。
        void Label(string value, float x, float y, float height, Vector4 ink, TextAnchor anchor = TextAnchor.Left, bool shadow = true, float maxWidth = 0f)
        {
            var position = canvas.At(x, y);
            var (pixels, fitted) = MenuText.Fit(value, height * s, maxWidth * s);
            var drop = UiFont.Height(pixels) / UiFont.Cell; // 影は 1 ドットずらす
            if (shadow) { text.Add(new(fitted, position + new Vector2(drop), pixels, Shadow, anchor, TextFont.Pixel)); }
            text.Add(new(fitted, position, pixels, ink, anchor, TextFont.Pixel));
        }

        // 共通の背景・枠・見出し。
        Plate(320, 232, 640, 464, new(0.06f, 0.24f, 0.34f, 1), new(0.02f, 0.07f, 0.16f, 1));
        for (var y = 4; y < 464; y += 8)
        {
            Plate(320, y, 640, 1, new(0.28f, 0.66f, 0.74f, 0.14f), new(0.28f, 0.66f, 0.74f, 0.14f));
        }

        Frame(320, 232, 614, 439, new(0.17f, 0.50f, 0.66f, 1), 2);
        Plate(320, 67, 532, 47, new(0.12f, 0.59f, 0.86f, 1), new(0.04f, 0.17f, 0.47f, 1));
        Frame(320, 67, 532, 47, Cyan, 2);
        Plate(320, 429, 532, 29, new(0.14f, 0.34f, 0.38f, 1), new(0.07f, 0.19f, 0.26f, 1));
        var heading = state.Page switch
        {
            MenuPage.Settings => "ゲームの設定",
            MenuPage.Mode => "ゲームモード",
            MenuPage.Select => "キャラクターセレクト",
            MenuPage.Replays => "リプレイセレクト",
            MenuPage.Free => "フリーバトル",
            MenuPage.Campaign => "キャンペーン",
            MenuPage.Create => "ボクサーを作る",
            MenuPage.Boxer => "本拠地",
            MenuPage.Orgs => "団体と大会",
            MenuPage.Path => $"{state.PathOrg.LogoText} の道",
            MenuPage.Weight => "減量・増量",
            MenuPage.Result => "試合の結果",
            _ => "ゲームセレクト",
        };
        // 一番上の行: キャラ選択ではいまのゲームモード（大会・練習試合の決まり）。
        var banner = state.Page == MenuPage.Select
            ? (state.Competition.Tournament ? state.Competition.Title : $"練習試合（{state.Competition.RulesName}の決まり）")
            : "MINI RING BOXING";
        Label(banner, 320, 20, 19, state.Page == MenuPage.Select ? Gold : Pale, TextAnchor.Center);
        Label(heading, 320, 51, 31, Gold, TextAnchor.Center);

        // 試合の後の札を出している間は、本拠地を描かずに札だけ（文字は図形の上に重なるので、下のページの字が透けて読みにくくなる）。
        var celebrating = state.Page == MenuPage.Boxer && state.Cards.Count > 0;
        switch (state.Page)
        {
            case MenuPage.Boxer when celebrating: DrawCard(Plate, Frame, Label); break;
            case MenuPage.Top: DrawTop(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Settings: DrawSettings(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Mode: DrawMode(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Select: DrawSelect(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Free: DrawFree(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Campaign: DrawCampaign(Plate, Frame, Label); break;
            case MenuPage.Create: DrawCreate(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Boxer: DrawHome(Plate, Frame, Label); break;
            case MenuPage.Orgs: DrawOrgs(Plate, Frame, Label); break;
            case MenuPage.Path: DrawPath(canvas, Plate, Frame, Label); break;
            case MenuPage.Weight: DrawWeight(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Result: DrawResult(Plate, Frame, Label); break;
            default: DrawReplays(Plate, Frame, Label); break;
        }

        if (state.Page is MenuPage.Select or MenuPage.Replays)
        {
            CornerBack(Plate, Frame, Label);
        }

        if (Notice is { } notice && !celebrating) { Label(notice, 320, 388, 15, Gold, TextAnchor.Center, maxWidth: 600f); }
        Label(Hint(), 320, 420, 15, Pale, TextAnchor.Center, maxWidth: 524f);
        return (shapes, text);
    }

    private delegate void PlateFn(float x, float y, float w, float h, Vector4 top, Vector4 bottom, float radius = 0f);
    private delegate void FrameFn(float x, float y, float w, float h, Vector4 color, float thickness, float radius = 0f);
    private delegate void LabelFn(string value, float x, float y, float height, Vector4 ink, TextAnchor anchor = TextAnchor.Left, bool shadow = true, float maxWidth = 0f);

    private string Hint() => state.Page switch
    {
        MenuPage.Top => "クリック / Enter / A: 決定   矢印 / WASD: 選ぶ   Esc / B: 終了へ",
        MenuPage.Settings => "クリック / ← → / Enter: 切り替え   右クリック / Esc: 戻る",
        MenuPage.Mode => "クリック / Enter: 決定   ◀ ▶ / ← →: 決まり   右クリック: 戻る",
        MenuPage.Free or MenuPage.Campaign => "クリック / Enter / A: 決定   ホイール / ↑ ↓: 選ぶ   右クリック: 戻る",
        MenuPage.Create => "◀ ▶ / ← →: 変える   クリック / Enter: 決定   右クリック: 戻る",
        MenuPage.Boxer when state.Cards.Count > 0 => "クリック / Enter / A: 次へ",
        MenuPage.Boxer => state.HomeIndex == MenuState.HomeNext
            ? "◀ ▶ / ← →: 武器   クリック / Enter: 試合へ   右クリック: 戻る"
            : "クリック / Enter / A: 決定   右クリック / Esc / B: 戻る",
        MenuPage.Orgs => "クリック / Enter: 道を見る   右クリック / Esc / B: 戻る",
        MenuPage.Path => "クリック / Enter: 次の試合にする   右クリック / Esc: 戻る",
        MenuPage.Weight => "ドラッグ・ボタン（押し続けて連続）/ ← →: 体重   右クリック: 戻る",
        MenuPage.Result => state.Result?.Campaign == true
            ? "クリック / Enter / A: 本拠地へ"
            : "クリック / Enter / A: 決定   ← →: 選ぶ   右クリック: メニューへ",
        MenuPage.Select => state.Mode == MatchMode.Versus
            ? "赤: WASD / 青: 矢印・2P パッド   クリック / Enter: 決定   Esc: 戻る"
            : "クリック / Enter: 決定   ホイール: 送る   右クリック / Esc: 戻る",
        _ => "クリック / Enter: 再生   ホイール / ↑ ↓: 選ぶ   右クリック / Esc: 戻る",
    };

    // ── 最初の画面
    private static readonly string[] TopLabels = ["フリーバトル", "キャンペーン", "リプレイ", "ゲームの設定", "終了"];
    private static readonly string[] TopDetails = ["シングル・対戦・観戦", "自分の選手で勝ち上がる", "過去の試合を再生", "レフェリーなど", ""];

    private void DrawTop(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        for (var i = 0; i < TopLabels.Length; i++)
        {
            var c = MenuLayout.TopButtonCenter(i);
            var selected = i == state.TopIndex;
            plate(c.X + 3, c.Y + 4, MenuLayout.TopButtonSize.X, MenuLayout.TopButtonSize.Y, Shadow, Shadow, 6);
            plate(c.X, c.Y, MenuLayout.TopButtonSize.X, MenuLayout.TopButtonSize.Y,
                selected ? new(0.18f, 0.54f, 0.78f, 1) : new(0.10f, 0.28f, 0.40f, 1), new(0.04f, 0.13f, 0.26f, 1), 6);
            frame(c.X, c.Y, MenuLayout.TopButtonSize.X, MenuLayout.TopButtonSize.Y, selected ? Cyan : new(0.19f, 0.38f, 0.51f, 1), 2, 6);
            if (selected) { plate(c.X - 129, c.Y, 5, 46, Gold, Gold, 2); }
            var ink = selected ? Gold : Pale;
            var iconAt = canvas.At(c.X - 88, c.Y);
            MenuIcons.DrawTop(shapes, iconAt, 52 * canvas.Scale, (TopItem)i, ink, selected ? Cyan : Dim, selected ? new(0.18f, 0.54f, 0.78f, 1) : new(0.10f, 0.28f, 0.40f, 1));
            label(TopLabels[i], c.X - 48, c.Y - (TopDetails[i].Length > 0 ? 24 : 12), 20, ink);
            if (TopDetails[i].Length > 0) { label(TopDetails[i], c.X - 48, c.Y + 6, 14, selected ? Pale : Dim, maxWidth: (MenuLayout.TopButtonSize.X / 2) + 40); }
        }
    }

    // ── 設定
    private void DrawSettings(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        string[] names = ["レフェリー", "戻る"];
        for (var i = 0; i < MenuState.SettingsRows; i++)
        {
            var c = MenuLayout.SettingsRowCenter(i);
            var selected = i == state.SettingsIndex;
            plate(c.X + 3, c.Y + 3, MenuLayout.SettingsRowSize.X, MenuLayout.SettingsRowSize.Y, Shadow, Shadow, 6);
            plate(c.X, c.Y, MenuLayout.SettingsRowSize.X, MenuLayout.SettingsRowSize.Y,
                selected ? new(0.16f, 0.50f, 0.72f, 1) : new(0.10f, 0.28f, 0.40f, 1), new(0.04f, 0.13f, 0.26f, 1), 6);
            frame(c.X, c.Y, MenuLayout.SettingsRowSize.X, MenuLayout.SettingsRowSize.Y, selected ? Cyan : new(0.19f, 0.38f, 0.51f, 1), 2, 6);
            var ink = selected ? Gold : Pale;
            if (i == 0) { MenuIcons.DrawReferee(shapes, canvas.At(c.X - 200, c.Y), 34 * canvas.Scale, ink); }
            label(names[i], c.X - 170, c.Y - 12, 22, ink);
            if (i == 0)
            {
                for (var option = 0; option < 2; option++)
                {
                    var on = (option == 0) == state.Settings.Referee;
                    var x = c.X + 90 + (option * 64);
                    plate(x, c.Y, 56, 28, on ? Gold : new(0.10f, 0.20f, 0.30f, 1), on ? new(0.85f, 0.55f, 0.15f, 1) : new(0.06f, 0.13f, 0.22f, 1), 14);
                    label(option == 0 ? "あり" : "なし", x, c.Y - 9, 17, on ? Shadow : Dim, TextAnchor.Center, shadow: !on);
                }
            }
        }

        label("レフェリーがいない試合は、ダウンを数えている間の攻撃も打撃に数え、", 320, 262, 14, Dim, TextAnchor.Center);
        label("立ち直った後の一呼吸もありません（10 カウントでの K.O. は同じ）。", 320, 282, 14, Dim, TextAnchor.Center);
        label("設定はこの PC に保存され、次に起動したときも残ります。", 320, 318, 14, Dim, TextAnchor.Center);
    }

    // ── リプレイ
    private void DrawReplays(PlateFn plate, FrameFn frame, LabelFn label)
    {
        var names = replayNames();
        var rows = Math.Min(MenuState.VisibleRecords, names.Count);
        for (var row = 0; row < rows; row++)
        {
            var c = MenuLayout.ListRowCenter(row);
            var selected = state.FirstRecord + row == state.ReplayFocus;
            plate(c.X + 4, c.Y + 3, MenuLayout.ListRowSize.X, MenuLayout.ListRowSize.Y, Shadow, Shadow);
            plate(c.X, c.Y, MenuLayout.ListRowSize.X, MenuLayout.ListRowSize.Y,
                selected ? new(0.16f, 0.50f, 0.72f, 1) : new(0.10f, 0.28f, 0.40f, 1), new(0.04f, 0.13f, 0.26f, 1));
            frame(c.X, c.Y, MenuLayout.ListRowSize.X, MenuLayout.ListRowSize.Y, selected ? Cyan : new(0.19f, 0.38f, 0.51f, 1), 2);
            if (selected) { plate(94, c.Y, 5, 24, Gold, Gold); }
            var name = names[state.FirstRecord + row];
            if (name.Length > 36) { name = string.Concat(name.AsSpan(0, 36), "…"); }
            label(name, 104, c.Y - 14, 20, selected ? Gold : Pale);
        }

        if (names.Count == 0)
        {
            label("保存された試合はありません", 320, 185, 23, Pale, TextAnchor.Center);
        }
        else
        {
            label($"{state.ReplayFocus + 1} / {names.Count}", 535, 360, 16, Pale, TextAnchor.Right);
        }
    }

    // ── ゲームモード
    private void DrawMode(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        for (var i = 0; i < state.ModeRows; i++)
        {
            var c = MenuLayout.ModeRowCenter(i);
            var size = MenuLayout.ModeRowSize;
            var selected = i == state.ModeIndex;
            var competition = state.ModeAt(i);
            // 団体の行は団体のファイルの色（accent）で縁取る。
            var accent = competition is { Tournament: true, Org: { } org } ? org.Colors.Accent : Cyan;
            plate(c.X + 3, c.Y + 3, size.X, size.Y, Shadow, Shadow, 6);
            plate(c.X, c.Y, size.X, size.Y, selected ? new(0.16f, 0.50f, 0.72f, 1) : new(0.10f, 0.28f, 0.40f, 1), new(0.04f, 0.13f, 0.26f, 1), 6);
            frame(c.X, c.Y, size.X, size.Y, selected ? accent : accent with { W = 0.45f }, selected ? 2.5f : 1.5f, 6);
            if (selected) { plate(c.X - (size.X / 2) + 6, c.Y, 5, 30, Gold, Gold, 2); }
            var ink = selected ? Gold : Pale;
            var left = c.X - (size.X / 2) + 16;
            if (competition is null)
            {
                label("戻る", left, c.Y - 11, 20, ink);
            }
            else if (!competition.Tournament)
            {
                label("練習試合", left, c.Y - 19, 19, ink);
                label("決まり", left, c.Y + 3, 13, selected ? Pale : Dim);
                var mid = (MouseLayout.ModeArrow(-1).X + MouseLayout.ModeArrow(1).X) / 2;
                label(competition.RulesName, mid, c.Y + 3, 13, selected ? Gold : Pale, TextAnchor.Center, maxWidth: MouseLayout.ModeArrow(1).X - MouseLayout.ModeArrow(-1).X - MouseLayout.ArrowSize.X - 4);
                ArrowButtons(plate, frame, label, MouseLayout.ModeArrow, selected);
            }
            else
            {
                var name = competition.Org!.Name;
                label(competition.Title, left, c.Y - 19, 19, ink);
                label(name, left, c.Y + 3, 12, selected ? Pale : Dim, maxWidth: size.X - 30);
            }
        }

        // 右: 選んでいる行の中身（決まりの要約はファイルの値から）。
        const float PanelX = 452f, PanelW = 312f;
        plate(PanelX, 250, PanelW, 308, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(PanelX, 250, PanelW, 308, new(0.19f, 0.38f, 0.51f, 1), 1.5f, 6);
        var x0 = PanelX - (PanelW / 2) + 14;
        var (who, whoDetail) = WhoPlays();
        label($"{who}（{whoDetail}）", x0, 104, 13, Dim);
        if (state.ModeAt(state.ModeIndex) is not { } focused)
        {
            label("最初の画面へ戻ります", x0, 128, 17, Pale);
            return;
        }

        var title = focused.Tournament ? focused.Title : $"練習試合（{focused.RulesName}の決まり）";
        label(title, x0, 124, 21, Gold, maxWidth: PanelW - 28);
        var y = 156f;
        foreach (var line in Competition.Summary(focused.Org, focused.Tournament))
        {
            // 長い要約は縮めずに折る（行の間は 16 px の字が触れない 15）。
            foreach (var part in MenuText.Wrap(line, (PanelW - 28) * canvas.Scale, 13 * canvas.Scale))
            {
                label(part, x0, y, 13, Pale);
                y += 15f;
            }

            y += 2f;
        }

        var eligible = state.Roster.Count(c => focused.Ineligible(c) is null);
        label($"出られる選手  {eligible} / {state.Roster.Count} 人", x0, y + 2, 13, Pale);
        y += 22f;
        if (focused is { Tournament: true, Org: { HasWeightClasses: true } weighed })
        {
            label("階級（計量 = 見えている体の重さ）", x0, y, 12, Dim);
            y += 16f;
            var lower = 0f;
            foreach (var division in weighed.Divisions)
            {
                var range = division.LimitKg is { } limit
                    ? string.Create(CultureInfo.InvariantCulture, $"〜{Pounds(limit)} lb（{MiniRing.Arena.OrgDivision.KgText(limit)} kg）")
                    : string.Create(CultureInfo.InvariantCulture, $"{Pounds(lower)} lb 超（{MiniRing.Arena.OrgDivision.KgText(lower)} kg 超）");
                var count = state.Roster.Count(c => focused.Ineligible(c) is null && focused.DivisionOf(c) == division);
                label(division.Name, x0 + 6, y, 12, Pale);
                label(range, x0 + PanelW - 62, y, 12, Pale, TextAnchor.Right, maxWidth: PanelW - 62 - 104);
                label($"{count} 人", x0 + PanelW - 34, y, 12, Dim, TextAnchor.Right);
                lower = division.LimitKg ?? lower;
                y += 15f;
            }
        }
        else if (!focused.Tournament)
        {
            label("◀ ▶ / ← → で決まりを切り替え（標準・各団体）", x0, y, 12, Dim);
        }
    }

    private static int Pounds(float kg) => (int)MathF.Round(kg / FighterTape.KgPerPound, MidpointRounding.AwayFromZero);

    private (string Who, string Detail) WhoPlays() => state.Mode switch
    {
        MatchMode.Single => ("シングルプレイ", "人 VS CPU"),
        MatchMode.Versus => ("対戦", "人 VS 人"),
        _ => ("観戦モード", "CPU VS CPU"),
    };

    // ── キャラクター選択
    private const float PanelWidth = 180f;
    private const float PanelTop = 92f;
    private const float BodyBoxHeight = 140f; // 下の文字（16 px の字で名前・一言を折る）の場所を空けた高さ
    private static float PanelCenterX(bool left) => left ? 104f : 536f;

    private void DrawSelect(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        var grid = state.Grid;
        var activeBlue = state.RedLocked;
        bool Allowed(int i) => activeBlue ? state.BlueAllowed(i) : state.RedAllowed(i);

        // 格子の地。
        plate(320, (SelectGrid.Top + SelectGrid.Bottom) / 2, SelectGrid.Right - SelectGrid.Left + 4, SelectGrid.Bottom - SelectGrid.Top + 4,
            new(0.03f, 0.10f, 0.18f, 0.6f), new(0.03f, 0.10f, 0.18f, 0.6f), 4);
        foreach (var (row, top) in grid.VisibleRows())
        {
            var line = grid.Rows[row];
            if (line.IsHeader)
            {
                var y = top + (SelectGrid.HeaderHeight / 2);
                plate(320, y + 1, SelectGrid.Right - SelectGrid.Left - 6, SelectGrid.HeaderHeight - 4, new(0.14f, 0.36f, 0.50f, 1), new(0.07f, 0.20f, 0.32f, 1), 3);
                label($"{line.Category.Label()}  {line.Count} 人", SelectGrid.Left + 8, y - 7, 12, Gold, shadow: false, maxWidth: SelectGrid.Right - SelectGrid.Left - 16);
                continue;
            }

            foreach (var index in line.Members)
            {
                DrawTile(shapes, canvas, plate, frame, label, index, Allowed(index));
            }
        }

        if (grid.MaxFirstRow > 0)
        {
            var (start, length) = grid.ScrollBar();
            var trackH = SelectGrid.Bottom - SelectGrid.Top;
            plate(SelectGrid.Right - 1, SelectGrid.Top + (trackH / 2), 3, trackH, new(0.02f, 0.06f, 0.12f, 1), new(0.02f, 0.06f, 0.12f, 1), 1.5f);
            plate(SelectGrid.Right - 1, SelectGrid.Top + (trackH * (start + (length / 2))), 3, trackH * length, Cyan, Cyan, 1.5f);
        }

        // カーソル（赤・青）。決めたら太く塗る。同じ人を指していれば青を外側へ。見えていない札には描かない。
        void Cursor(int index, Vector4 color, bool locked, float grow)
        {
            if (grid.TileCenter(index) is not { } c) { return; }
            var size = SelectGrid.TileSize + new Vector2(grow);
            if (locked)
            {
                plate(c.X, c.Y, size.X, size.Y, color with { W = 0.28f }, color with { W = 0.28f }, 4);
            }

            frame(c.X, c.Y, size.X, size.Y, color, locked ? 3.5f : 2.5f, 4);
        }

        var same = state.RedCursor == state.BlueCursor;
        Cursor(state.RedCursor, RedSide, state.RedLocked, 3f);
        if (state.RedLocked || state.Mode == MatchMode.Versus) { Cursor(state.BlueCursor, BlueSide, state.BlueLocked, same ? 8f : 3f); }

        var (redTag, blueTag) = state.Mode switch
        {
            MatchMode.Single => ("1P（あなた）", "CPU"),
            MatchMode.Versus => ("1P", "2P"),
            _ => ("CPU（赤）", "CPU（青）"),
        };
        var prompt = !state.RedLocked ? $"赤  {redTag}  を選んでください"
            : !state.BlueLocked ? $"青  {blueTag}  を選んでください" : "試合を始めます";
        if (Notice is null) { label(prompt, 320, SelectGrid.Bottom + 3, 15, Gold, TextAnchor.Center); }

        DrawSide(shapes, canvas, plate, frame, label, state.RedCursor, left: true, redTag, state.RedLocked, !state.RedLocked || state.Mode == MatchMode.Versus);
        DrawSide(shapes, canvas, plate, frame, label, state.BlueCursor, left: false, blueTag, state.BlueLocked, state.RedLocked || state.Mode == MatchMode.Versus);
    }

    /// <summary>
    /// 格子の 1 枚: 顔・国旗。選べない人（大会に出られない・階級が違う）は暗くする。名前は札に書かない（16 px の字では札の幅に 3 字しか入らない）:
    /// カーソルの人の名前は左右のパネルに出る。
    /// </summary>
    private void DrawTile(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label, int index, bool allowed)
    {
        if (state.Grid.TileCenter(index) is not { } c) { return; }
        var tile = SelectGrid.TileSize;
        var character = state.Roster[index];
        plate(c.X + 2, c.Y + 2, tile.X, tile.Y, Shadow, Shadow, 3);
        plate(c.X, c.Y, tile.X, tile.Y, new(0.12f, 0.30f, 0.42f, 1), new(0.05f, 0.14f, 0.26f, 1), 3);
        const float FaceBox = 54f;
        var faceCenter = c + new Vector2(0f, 2f);
        if (portraits.TryGet(index) is { } portrait)
        {
            var face = portrait.Face;
            var pixel = MathF.Min(FaceBox / face.Height, (tile.X - 4f) / face.Width);
            DrawPortrait(shapes, canvas, face, faceCenter, pixel, blue: false, anchorBottom: false);
        }
        else
        {
            label("…", faceCenter.X, faceCenter.Y - 8, 14, Dim, TextAnchor.Center);
        }

        MenuIcons.DrawFlag(shapes, canvas.At(c.X - (tile.X / 2) + 7, c.Y - (tile.Y / 2) + 5), 11 * canvas.Scale, character.Country.Flag);
        if (!allowed)
        {
            plate(c.X, c.Y, tile.X, tile.Y, new(0.01f, 0.03f, 0.08f, 0.62f), new(0.01f, 0.03f, 0.08f, 0.62f), 3);
        }

        frame(c.X, c.Y, tile.X, tile.Y, new(0.19f, 0.38f, 0.51f, 1), 1.2f, 3);
    }

    private void DrawSide(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label,
        int index, bool left, string tag, bool locked, bool active)
    {
        var character = state.Roster[index];
        var color = left ? RedSide : BlueSide;
        var panelX = PanelCenterX(left);
        var x0 = panelX - (PanelWidth / 2) + 4;
        var textW = PanelWidth - 8;
        // 全身の絵の枠（全員を同じ縮尺で描く: 背の高い人は高く、四つ足は横に広く見える）。
        var boxCenterY = PanelTop + (BodyBoxHeight / 2);
        plate(panelX, boxCenterY, PanelWidth, BodyBoxHeight, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(panelX, boxCenterY, PanelWidth, BodyBoxHeight, active ? color : color with { W = 0.45f }, active ? 2.5f : 1.5f, 6);
        label(tag.Split('（')[0], x0, PanelTop + 4, 13, color); // 肩書き（「1P（あなた）」は 1P だけ。頭の上に重ならない左の隅へ）
        if (locked) { label("決定", panelX + (PanelWidth / 2) - 6, PanelTop + 4, 13, Gold, TextAnchor.Right); }
        var feetY = PanelTop + BodyBoxHeight - 8f;
        plate(panelX, feetY + 1, 100, 5, Shadow, Shadow, 3); // 足元の影
        if (portraits.TryGet(index) is { } portrait)
        {
            var body = portrait.Body;
            var pixel = MathF.Min(body.PixelMeters * BodyPixelsPerMeter, (PanelWidth - 8f) / body.Width);
            DrawPortrait(shapes, canvas, body, new Vector2(panelX, feetY), pixel, blue: !left, anchorBottom: true);
        }
        else
        {
            label("…", panelX, boxCenterY - 12, 22, Dim, TextAnchor.Center);
        }

        // 国旗・出身。
        var y = PanelTop + BodyBoxHeight + 5f;
        MenuIcons.DrawFlag(shapes, canvas.At(x0 + 10, y + 7), 20 * canvas.Scale, character.Country.Flag);
        label(character.Origin, x0 + 24, y, 12, Pale, maxWidth: textW - 24);
        // 名前（長いものは「・」で折る。縮めない）。
        y += 17f;
        foreach (var part in MenuText.WrapName(character.Name, textW * canvas.Scale, 17 * canvas.Scale))
        {
            label(part, x0, y, 17, Pale, maxWidth: textW);
            y += 16f;
        }

        // 分け方の札・持ち物（持ち物は札の下の行。札の横では 3 字しか入らない）。
        y += 3f;
        var category = state.Grid.Categories[index].ShortLabel();
        var chipW = (ScreenText.MeasurePixelWidth(category, MenuText.Fit(category, 11 * canvas.Scale, 0f).Pixels, TextFont.Pixel) / canvas.Scale) + 8f;
        plate(x0 + (chipW / 2), y + 7, chipW, 16, color with { W = 0.85f }, color with { X = color.X * 0.6f, Y = color.Y * 0.6f, Z = color.Z * 0.6f }, 3);
        label(category, x0 + 4, y, 11, Shadow, shadow: false);
        y += 19f;
        MenuIcons.DrawEquipment(shapes, canvas.At(x0 + 14, y + 7), 11 * canvas.Scale, character.Weapon, character.Wear);
        label(character.EquipmentName, x0 + 30, y, 11, Pale, maxWidth: textW - 30);
        // 体格（ボクシングの単位: 身長は ft/in と cm、体重は lb と kg）。
        var tape = FighterTape.Of(character.Build);
        y += 17f;
        label("身長", x0, y, 12, Dim);
        label(tape.HeightText, x0 + 32, y, 12, Pale, maxWidth: textW - 32);
        y += 16f;
        label("体重", x0, y, 12, Dim);
        label(tape.WeightText, x0 + 32, y, 12, Pale, maxWidth: textW - 32);
        // 階級（階級制の団体に出られる人だけ。いまの大会が階級制ならその団体、ほかは階級制の最初の団体）。
        y += 16f;
        if (DivisionText(character) is { } division)
        {
            label("階級", x0, y, 12, Dim);
            label(division, x0 + 32, y, 12, Pale, maxWidth: textW - 32);
            y += 16f;
        }

        var why = state.Competition.Ineligible(character);
        var last = why is null ? character.Tagline : "出場できません";
        foreach (var part in MenuText.Wrap(last, textW * canvas.Scale, 11 * canvas.Scale))
        {
            label(part, x0, y, 11, why is null ? Gold : RedSide, maxWidth: textW);
            y += 16f;
        }
    }

    /// <summary>階級の表示（「IVF ミドル級」）。階級制の団体に出られなければ null。</summary>
    private string? DivisionText(FighterCharacter character)
    {
        var orgs = state.Competition is { Tournament: true, Org: { HasWeightClasses: true } current }
            ? [current]
            : state.Organizations.Where(o => o.HasWeightClasses);
        foreach (var org in orgs)
        {
            var competition = Competition.Event(org);
            if (competition.Ineligible(character) is null && competition.DivisionOf(character) is { } division)
            {
                return $"{org.LogoText} {division.Name}";
            }
        }

        return null;
    }

    /// <summary>全身の絵の縮尺 [設計の画面のピクセル / m]: 名簿でいちばん背の高い人が枠に収まる値（全員に同じ値）。</summary>
    private float BodyPixelsPerMeter => _bodyScale ??= (BodyBoxHeight - 22f)
        / MathF.Max(0.5f, state.Roster.Max(c => BodySizing.PhysicalHeightMeters(c.Build)) * 1.06f);

    private float? _bodyScale;

    /// <summary>名簿でいちばん背の高い人の高さ [m]（見込みの全身の絵の縮尺をキャラ選択とそろえる）。</summary>
    private float RosterTallestMeters => _tallest ??= state.Roster.Max(c => BodySizing.PhysicalHeightMeters(c.Build));

    private float? _tallest;

    /// <summary>
    /// 肖像（四角の並び）を設計の座標へ描く。同じ行で同じ色が続くところは 1 つの四角にまとめる（図形の数を減らす）。
    /// <paramref name="anchor"/> は足元（<paramref name="anchorBottom"/>）か中心。左右は体の中心線に合わせる。
    /// </summary>
    private static void DrawPortrait(List<UiShapeDraw> shapes, MenuCanvas canvas, PortraitImage image, Vector2 anchor, float pixel, bool blue, bool anchorBottom)
    {
        var scale = canvas.Scale;
        var px = pixel * scale;
        var top = anchorBottom ? anchor.Y - (image.Height * pixel) : anchor.Y - (image.Height * pixel / 2f);
        var origin = canvas.At(anchor.X - (image.Width * pixel / 2f), top);
        var overlap = 0.7f; // 隣の四角との継ぎ目（半透明の縁）を消す重なり [画面ピクセル]
        for (var y = 0; y < image.Height; y++)
        {
            var x = 0;
            while (x < image.Width)
            {
                var color = image.ColorAt(x, y, blue);
                if (color.W == 0f) { x++; continue; }
                var end = x + 1;
                while (end < image.Width && image.ColorAt(end, y, blue) == color) { end++; }
                var size = new Vector2(((end - x) * px) + overlap, px + overlap);
                var center = origin + new Vector2((x * px) + (((end - x) * px) / 2f), (y * px) + (px / 2f));
                shapes.Add(UiShapeDraw.Rounded(center + new Vector2(overlap / 2f), size, 0f, color));
                x = end;
            }
        }
    }
}
