using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>フリーバトル（誰が操るか）とキャンペーン（一覧・作る・ボクサー）のページの描画（2026-11-26）。</summary>
internal sealed partial class MenuView
{
    private static readonly string[] FreeLabels = ["シングルプレイ", "対戦", "観戦モード", "戻る"];
    private static readonly string[] FreeDetails = ["人 VS CPU", "人 VS 人", "CPU VS CPU", "最初の画面へ"];

    // ── フリーバトル
    private void DrawFree(List<UiShapeDraw> shapes, MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        for (var i = 0; i < MenuState.FreeRows; i++)
        {
            var c = MenuLayout.ModeRowCenter(i);
            var size = MenuLayout.ModeRowSize;
            var selected = i == state.FreeIndex;
            Row(plate, frame, c, size, selected);
            var ink = selected ? Gold : Pale;
            if (i < 3)
            {
                MenuIcons.Draw(shapes, canvas.At(c.X - 95, c.Y), 34 * canvas.Scale, (MenuIcon)i, ink, selected ? Cyan : Dim,
                    selected ? new(0.16f, 0.50f, 0.72f, 1) : new(0.10f, 0.28f, 0.40f, 1));
            }

            label(FreeLabels[i], c.X - 66, c.Y - 19, 19, ink);
            label(FreeDetails[i], c.X - 66, c.Y + 3, 13, selected ? Pale : Dim);
        }

        Panel(plate, frame);
        label("フリーバトル", 326, 124, 21, Gold);
        label("好きな選手どうしで 1 試合。", 326, 156, 14, Pale);
        label("練習試合（標準・各団体の決まり）か、", 326, 178, 14, Pale);
        label("団体の大会（出場の条件・計量）を選べます。", 326, 200, 14, Pale);
        label("戦績は残りません。", 326, 232, 14, Dim);
    }

    // ── キャンペーン: 一覧
    private void DrawCampaign(PlateFn plate, FrameFn frame, LabelFn label)
    {
        var rows = Math.Min(state.CampaignRows - state.CampaignFirst, MenuState.CampaignVisible + 2);
        for (var r = 0; r < rows; r++)
        {
            var i = state.CampaignFirst + r;
            var c = MenuLayout.CampaignRowCenter(r);
            var selected = i == state.CampaignIndex;
            Row(plate, frame, c, MenuLayout.CampaignRowSize, selected);
            var ink = selected ? Gold : Pale;
            var left = c.X - (MenuLayout.CampaignRowSize.X / 2) + 14;
            if (i < state.Boxers.Count)
            {
                var b = state.Boxers[i];
                label(b.Character.HudName, left, c.Y - 9, 15, ink);
                label(string.Create(CultureInfo.InvariantCulture, $"{b.WeightKg} kg  {b.Career.RecordText}"), c.X + 130, c.Y - 8, 13, selected ? Pale : Dim, TextAnchor.Right);
            }
            else
            {
                label(i == state.Boxers.Count ? "＋ 新しく作る" : "戻る", left, c.Y - 9, 15, ink);
            }
        }

        Panel(plate, frame);
        if (state.CampaignIndex < state.Boxers.Count)
        {
            DrawBoxerCard(state.Boxers[state.CampaignIndex], label);
        }
        else if (state.CampaignIndex == state.Boxers.Count)
        {
            label("新しいボクサー", 326, 124, 21, Gold);
            label("体型・身長・教科書を選んで作ります。", 326, 156, 14, Pale);
            label("何人でも作れます（1 人 1 ファイル）。", 326, 178, 14, Pale);
            label("体重の階級と戦績で出られる大会が決まり、", 326, 210, 14, Pale);
            label("新人戦 → ランキング戦 → タイトルへ。", 326, 232, 14, Pale);
        }
        else
        {
            label("最初の画面へ戻ります", 326, 128, 17, Pale);
        }

        if (state.Boxers.Count == 0)
        {
            label("まだボクサーがいません", 326, 300, 14, Dim);
        }
    }

    private static void DrawBoxerCard(CampaignBoxer b, LabelFn label)
    {
        var c = b.Character;
        label(c.HudName, 326, 124, 21, Gold, maxWidth: 270);
        label(string.Create(CultureInfo.InvariantCulture, $"身長 {CharacterProfile.HeightOf(c.Build)} cm・計量 {b.WeightKg} kg"), 326, 156, 14, Pale);
        label($"教科書 {c.StyleName}・{b.Career.RecordText}", 326, 178, 14, Pale);
        var y = 206f;
        foreach (var seat in b.Career.Seats)
        {
            label($"{seat.Org.ToUpperInvariant()} {seat.Division}: {CampaignLadder.SeatName(seat.Seat)}", 326, y, 13, Pale);
            y += 18f;
        }

        foreach (var bout in b.Career.Bouts.TakeLast(5).Reverse())
        {
            var word = bout.Result switch { BoutResult.Win => "○", BoutResult.Loss => "●", _ => "△" };
            label($"{word} {bout.Opponent}（{bout.Event}）", 326, y, 12, Dim);
            y += 16f;
        }
    }

    // ── キャンペーン: 作る
    private void DrawCreate(MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        var type = CharacterMaker.BodyTypes[state.BodyTypeIndex];
        var draft = state.Draft();
        string[] rows =
        [
            $"体型  ◀ {type.Label} ▶",
            string.Create(CultureInfo.InvariantCulture, $"身長  ◀ {state.HeightCm} cm ▶"),
            $"教科書  ◀ {state.Textbooks[state.TextbookIndex]} ▶",
            "このボクサーで作る",
            "戻る",
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            var c = MenuLayout.CampaignRowCenter(i);
            var selected = i == state.CreateIndex;
            Row(plate, frame, c, MenuLayout.CampaignRowSize, selected);
            label(rows[i], c.X - (MenuLayout.CampaignRowSize.X / 2) + 14, c.Y - 9, 15, selected ? Gold : Pale);
        }

        Panel(plate, frame);
        label("見込み（体は寸法から）", 326, 124, 19, Gold);
        label(string.Create(CultureInfo.InvariantCulture, $"身長 {draft.HeightCm} cm・体重 {draft.WeightKg} kg"), 326, 154, 14, Pale);
        label(type.Note, 326, 176, 13, Dim, maxWidth: 270);
        var y = 206f;
        label("出られる新人戦（体重の階級で決まる）", 326, y, 13, Pale, maxWidth: PanelRight - 326);
        y += 22f;
        // この体重で入る階級の新人戦（団体ごとに 1 つ。身長・体型では弾かれない）。
        foreach (var ev in CampaignEvents.All.Where(e => e.Tier == CampaignTier.Rookie && e.Division == e.Organization.DivisionOf(draft.WeightKg)))
        {
            label($"・{ev.Name}", 326, y, 13, Pale);
            y += 18f;
            foreach (var line in MenuText.Wrap(ev.Note, (PanelRight - 340) * canvas.Scale, 12 * canvas.Scale))
            {
                label(line, 340, y, 12, Dim);
                y += 16f;
            }
        }
    }

    /// <summary>右の説明の板の文字が使える右端（設計の座標。板の右の縁 612 から内側へ 8）。</summary>
    private const float PanelRight = 604f;

    private static void Row(PlateFn plate, FrameFn frame, Vector2 c, Vector2 size, bool selected)
    {
        plate(c.X + 3, c.Y + 2, size.X, size.Y, Shadow, Shadow, 4);
        plate(c.X, c.Y, size.X, size.Y, selected ? new(0.16f, 0.50f, 0.72f, 1) : new(0.10f, 0.28f, 0.40f, 1), new(0.04f, 0.13f, 0.26f, 1), 4);
        frame(c.X, c.Y, size.X, size.Y, selected ? Cyan : new(0.19f, 0.38f, 0.51f, 1), selected ? 2f : 1.2f, 4);
        if (selected) { plate(c.X - (size.X / 2) + 5, c.Y, 4, size.Y - 8, Gold, Gold, 2); }
    }

    /// <summary>右の説明の板（x = 312〜612）。</summary>
    private static void Panel(PlateFn plate, FrameFn frame)
    {
        plate(462, 238, 300, 284, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(462, 238, 300, 284, new(0.19f, 0.38f, 0.51f, 1), 1.5f, 6);
    }
}
