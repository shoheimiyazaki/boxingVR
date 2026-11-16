using System.Numerics;
using Aigis.Render;
using MiniRing.Characters;

namespace MiniRing.Desktop;

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
internal sealed class MenuView(MenuState state, PortraitSet portraits, Func<IReadOnlyList<string>> replayNames)
{
    private static readonly Vector4 Shadow = new(0.02f, 0.04f, 0.12f, 1f);
    private static readonly Vector4 Pale = new(0.85f, 0.92f, 0.95f, 1f);
    private static readonly Vector4 Dim = new(0.55f, 0.66f, 0.72f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.79f, 0.30f, 1f);
    private static readonly Vector4 Cyan = new(0.26f, 0.8f, 1f, 1f);
    private static readonly Vector4 RedSide = new(1f, 0.40f, 0.34f, 1f);
    private static readonly Vector4 BlueSide = new(0.40f, 0.62f, 1f, 1f);

    /// <summary>肖像の 1 ピクセル（全身はボクセル 1 つ = 実寸 5 mm）の設計の画面での大きさ。</summary>
    internal const float BodyPixel = 1.5f;

    /// <summary>顔の 1 ピクセル（ボクセル 1 つ）の設計の画面での大きさ。</summary>
    internal const float FacePixel = 3.7f;

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
        void Label(string value, float x, float y, float height, Vector4 ink, TextAnchor anchor = TextAnchor.Left, bool shadow = true)
        {
            var position = canvas.At(x, y);
            if (shadow) { text.Add(new(value, position + (new Vector2(2f) * s), height * s, Shadow, anchor)); }
            text.Add(new(value, position, height * s, ink, anchor));
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
            MenuPage.Select => "キャラクターセレクト",
            MenuPage.Replays => "リプレイセレクト",
            _ => "ゲームセレクト",
        };
        Label("MINI RING BOXING", 320, 20, 19, Pale, TextAnchor.Center);
        Label(heading, 320, 51, 31, Gold, TextAnchor.Center);

        switch (state.Page)
        {
            case MenuPage.Top: DrawTop(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Settings: DrawSettings(shapes, canvas, Plate, Frame, Label); break;
            case MenuPage.Select: DrawSelect(shapes, canvas, Plate, Frame, Label); break;
            default: DrawReplays(Plate, Frame, Label); break;
        }

        if (Notice is { } notice) { Label(notice, 320, 392, 16, Gold, TextAnchor.Center); }
        Label(Hint(), 320, 420, 15, Pale, TextAnchor.Center);
        return (shapes, text);
    }

    private delegate void PlateFn(float x, float y, float w, float h, Vector4 top, Vector4 bottom, float radius = 0f);
    private delegate void FrameFn(float x, float y, float w, float h, Vector4 color, float thickness, float radius = 0f);
    private delegate void LabelFn(string value, float x, float y, float height, Vector4 ink, TextAnchor anchor = TextAnchor.Left, bool shadow = true);

    private string Hint() => state.Page switch
    {
        MenuPage.Top => "WASD / 矢印 / 十字キー: 選ぶ   Enter / A / クリック: 決定   Esc / B: 終了へ",
        MenuPage.Settings => "← →  Enter / A: 切り替え   Esc / B: 戻る",
        MenuPage.Select => state.Mode == MatchMode.Versus
            ? "赤: WASD / 青: 矢印・2 つ目のパッド   Enter / A: 決定   Esc / B: 戻る"
            : "WASD / 矢印 / 十字キー: 選ぶ   Enter / A / クリック: 決定   Esc / B: 戻る",
        _ => "↑ ↓ / ホイール: 選ぶ   Enter / A / クリック: 再生   Esc / B: 戻る",
    };

    // ── 最初の画面
    private static readonly string[] TopLabels = ["シングルプレイ", "対戦", "観戦モード", "リプレイ", "ゲームの設定", "終了"];
    private static readonly string[] TopDetails = ["人 VS CPU", "人 VS 人", "CPU VS CPU", "過去の試合を再生", "レフェリーなど", ""];

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
            if (TopDetails[i].Length > 0) { label(TopDetails[i], c.X - 48, c.Y + 6, 14, selected ? Pale : Dim); }
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

    // ── キャラクター選択
    private void DrawSelect(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        var roster = state.Roster;
        for (var i = 0; i < roster.Count; i++)
        {
            var c = MenuLayout.TileCenter(i);
            var tile = MenuLayout.TileSize;
            plate(c.X + 2, c.Y + 3, tile.X, tile.Y, Shadow, Shadow, 3);
            plate(c.X, c.Y, tile.X, tile.Y, new(0.12f, 0.30f, 0.42f, 1), new(0.05f, 0.14f, 0.26f, 1), 3);
            if (portraits.TryGet(i) is { } portrait)
            {
                DrawPortrait(shapes, canvas, portrait.Face, c + new Vector2(0f, 1f), FacePixel, blue: false, anchorBottom: false);
            }
            else
            {
                label("…", c.X, c.Y - 8, 16, Dim, TextAnchor.Center);
            }

            frame(c.X, c.Y, tile.X, tile.Y, new(0.19f, 0.38f, 0.51f, 1), 1.5f, 3);
        }

        // カーソル（赤・青）。決めたら太く塗る。同じ人を指していれば青を外側へ。
        void Cursor(int index, Vector4 color, bool locked, float grow)
        {
            var c = MenuLayout.TileCenter(index);
            var size = MenuLayout.TileSize + new Vector2(grow);
            if (locked)
            {
                plate(c.X, c.Y, size.X, size.Y, color with { W = 0.28f }, color with { W = 0.28f }, 4);
            }

            frame(c.X, c.Y, size.X, size.Y, color, locked ? 4f : 3f, 4);
        }

        var same = state.RedCursor == state.BlueCursor;
        Cursor(state.RedCursor, RedSide, state.RedLocked, 3f);
        Cursor(state.BlueCursor, BlueSide, state.BlueLocked, same ? 9f : 3f);

        var (redTag, blueTag) = state.Mode switch
        {
            MatchMode.Single => ("1P（あなた）", "CPU"),
            MatchMode.Versus => ("1P", "2P"),
            _ => ("CPU（赤）", "CPU（青）"),
        };
        var prompt = !state.RedLocked ? $"赤  {redTag}  を選んでください"
            : !state.BlueLocked ? $"青  {blueTag}  を選んでください" : "試合を始めます";
        label(prompt, 320, 236, 18, Gold, TextAnchor.Center);

        DrawSide(shapes, canvas, plate, frame, label, state.RedCursor, left: true, redTag, state.RedLocked, !state.RedLocked || state.Mode == MatchMode.Versus);
        DrawSide(shapes, canvas, plate, frame, label, state.BlueCursor, left: false, blueTag, state.BlueLocked, state.RedLocked || state.Mode == MatchMode.Versus);
    }

    private void DrawSide(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label,
        int index, bool left, string tag, bool locked, bool active)
    {
        var character = state.Roster[index];
        var color = left ? RedSide : BlueSide;
        var panelX = left ? 90f : 550f;
        var x0 = left ? 16f : 324f;
        // 全身の絵の枠。
        plate(panelX, 186, 150, 172, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(panelX, 186, 150, 172, active ? color : color with { W = 0.45f }, active ? 2.5f : 1.5f, 6);
        label(tag.Split('（')[0], panelX - 68, 104, 13, color); // 肩書き（「1P（あなた）」は 1P だけ。頭の上に重ならない左の隅へ）
        if (locked) { label("決定", panelX + 68, 104, 13, Gold, TextAnchor.Right); }
        plate(panelX, 263, 110, 5, Shadow, Shadow, 3); // 足元の影
        if (portraits.TryGet(index) is { } portrait)
        {
            DrawPortrait(shapes, canvas, portrait.Body, new Vector2(panelX, 262f), BodyPixel, blue: !left, anchorBottom: true);
        }
        else
        {
            label("…", panelX, 170, 22, Dim, TextAnchor.Center);
        }

        // 国旗・出身。
        MenuIcons.DrawFlag(shapes, canvas.At(x0 + 16, 289), 30 * canvas.Scale, character.Country.Flag);
        label(character.Origin, x0 + 40, 281, 14, Pale);
        label(character.Tagline, x0 + 296, 281, 14, Gold, TextAnchor.Right);
        // 名前（長いものは文字を縮めて収める）。
        var nameHeight = FitHeight(character.Name, 296f, 20f);
        label(character.Name, x0, 304, nameHeight, Pale);
        // 武器。
        MenuIcons.DrawEquipment(shapes, canvas.At(x0 + 28, 337), 20 * canvas.Scale, character.Weapon, character.Wear);
        label(character.EquipmentName, x0 + 62, 328, 15, Pale);
        // 体格（体の寸法そのもの。基準 = 1.00）。
        var build = character.Build;
        (string Name, float Value)[] rows = [("身長", build.Height), ("腕", build.Arms), ("脚", build.Legs), ("筋肉", build.Muscle)];
        for (var i = 0; i < rows.Length; i++)
        {
            var (bx, by) = (x0 + ((i % 2) * 150f), 356f + ((i / 2) * 19f));
            label(rows[i].Name, bx, by, 13, Dim);
            const float TrackX = 30f, TrackW = 62f;
            plate(bx + TrackX + (TrackW / 2), by + 8, TrackW, 6, new(0.03f, 0.08f, 0.16f, 1), new(0.03f, 0.08f, 0.16f, 1), 3);
            var fill = Math.Clamp((rows[i].Value - 0.6f) / 0.9f, 0f, 1f) * TrackW;
            plate(bx + TrackX + (fill / 2), by + 8, MathF.Max(fill, 1f), 6, color, color with { X = color.X * 0.7f, Y = color.Y * 0.7f, Z = color.Z * 0.7f }, 3);
            plate(bx + TrackX + ((1f - 0.6f) / 0.9f * TrackW), by + 8, 1.5f, 10, Pale, Pale); // 基準（1.00）の目盛り
            label($"×{rows[i].Value:0.00}", bx + TrackX + TrackW + 4, by, 13, Pale);
        }
    }

    /// <summary>文字の幅（全角 = 高さ、半角 = 高さの約半分）が <paramref name="maxWidth"/> に収まる文字の高さ。</summary>
    internal static float FitHeight(string value, float maxWidth, float maxHeight)
    {
        var units = value.Sum(ch => ch < 0x80 ? 0.55f : 1f);
        return units <= 0f ? maxHeight : MathF.Min(maxHeight, maxWidth / units);
    }

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
