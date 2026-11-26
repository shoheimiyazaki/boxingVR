using System.Numerics;
using Aigis.Render;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Desktop;

/// <summary>
/// メニューの絵文字（アイコン・国旗・武器）を、<see cref="UiShapeDraw"/>（角丸の四角）の組み合わせだけで描く。
/// 外部の画像・フォントを持ち込まない（ライセンス）。回転できない図形なので、斜めの線は階段状の四角で近づける。
/// 座標は「大きさ 1 の箱」の単位（x は右が +、y は下が +、中心が原点）で、描くときに実寸へ掛ける。
/// </summary>
internal static class MenuIcons
{
    /// <summary>1 つの図形。<paramref name="Radius"/> が 99 なら円・丸薬。<paramref name="Border"/> が 0 より大きければ枠だけ。</summary>
    private readonly record struct Part(float X, float Y, float W, float H, Vector4 Color, float Radius = 0.02f, float Border = 0f);

    private static Part Rect(float x, float y, float w, float h, Vector4 color, float radius = 0.02f) => new(x, y, w, h, color, radius);
    private static Part Disc(float x, float y, float d, Vector4 color) => new(x, y, d, d, color, 99f);
    private static Part Ring(float x, float y, float d, Vector4 color, float border) => new(x, y, d, d, color, 99f, border);

    private static readonly Vector4 Steel = new(0.80f, 0.86f, 0.92f, 1f);
    private static readonly Vector4 SteelDark = new(0.42f, 0.50f, 0.58f, 1f);
    private static readonly Vector4 Gold = new(0.96f, 0.76f, 0.28f, 1f);
    private static readonly Vector4 Wood = new(0.62f, 0.38f, 0.18f, 1f);
    private static readonly Vector4 WoodDark = new(0.36f, 0.21f, 0.10f, 1f);
    private static readonly Vector4 Leather = new(0.82f, 0.22f, 0.20f, 1f);
    private static readonly Vector4 Cuff = new(0.93f, 0.93f, 0.90f, 1f);
    private static readonly Vector4 Skin = new(0.90f, 0.70f, 0.52f, 1f);

    /// <summary>最初の画面のボタンの絵。</summary>
    public static void DrawTop(List<UiShapeDraw> shapes, Vector2 center, float size, TopItem item, Vector4 ink, Vector4 accent, Vector4 plate)
        => Draw(shapes, center, size, item switch
        {
            TopItem.FreeBattle => MenuIcon.Versus,
            TopItem.Campaign => MenuIcon.Campaign,
            TopItem.Replays => MenuIcon.Replays,
            TopItem.Settings => MenuIcon.Settings,
            _ => MenuIcon.Quit,
        }, ink, accent, plate);

    /// <summary>ボタンの絵（最初の画面・フリーバトルの誰が操るか）。</summary>
    public static void Draw(List<UiShapeDraw> shapes, Vector2 center, float size, MenuIcon icon, Vector4 ink, Vector4 accent, Vector4 plate)
        => Emit(shapes, center, size, TopParts(icon, ink, accent, plate));

    /// <summary>設定の「レフェリー」の絵（縞のシャツの人）。</summary>
    public static void DrawReferee(List<UiShapeDraw> shapes, Vector2 center, float size, Vector4 ink)
    {
        var dark = new Vector4(0.08f, 0.08f, 0.1f, 1f);
        List<Part> parts = [Disc(0, -0.28f, 0.28f, Skin), Rect(0, 0.12f, 0.56f, 0.52f, Cuff, 0.18f)];
        for (var i = -2; i <= 2; i += 2)
        {
            parts.Add(Rect(i * 0.1f, 0.12f, 0.07f, 0.5f, dark, 0.01f));
        }

        parts.Add(Rect(0, -0.12f, 0.16f, 0.07f, dark, 0.03f));
        parts.Add(Ring(0.34f, -0.12f, 0.2f, ink, 0.03f)); // 笛
        Emit(shapes, center, size, parts);
    }

    private static List<Part> TopParts(MenuIcon item, Vector4 ink, Vector4 accent, Vector4 plate)
    {
        List<Part> parts = [];
        switch (item)
        {
            case MenuIcon.Single:
                parts.Add(Disc(0, -0.27f, 0.3f, ink));
                parts.Add(Rect(0, 0.14f, 0.56f, 0.5f, ink, 0.26f));
                break;
            case MenuIcon.Versus:
                parts.Add(Disc(-0.28f, -0.2f, 0.22f, ink));
                parts.Add(Rect(-0.28f, 0.12f, 0.36f, 0.38f, ink, 0.17f));
                parts.Add(Disc(0.28f, -0.2f, 0.22f, accent));
                parts.Add(Rect(0.28f, 0.12f, 0.36f, 0.38f, accent, 0.17f));
                parts.Add(Rect(0, 0.0f, 0.05f, 0.7f, Gold, 0.02f));
                break;
            case MenuIcon.Spectate:
                parts.Add(Rect(0, 0, 0.94f, 0.5f, ink, 99f));
                parts.Add(Rect(0, 0, 0.82f, 0.38f, plate, 99f));
                parts.Add(Disc(0, 0, 0.34f, accent));
                parts.Add(Disc(0, 0, 0.15f, plate));
                parts.Add(Disc(-0.05f, -0.05f, 0.06f, ink));
                break;
            case MenuIcon.Replays:
                parts.Add(Ring(0, 0, 0.9f, ink, 0.07f));
                for (var i = 0; i < 11; i++)
                {
                    var y = -0.25f + (i * 0.05f);
                    var w = 0.34f * (1f - (MathF.Abs(y) / 0.30f));
                    parts.Add(Rect(-0.1f + (w / 2f), y, w, 0.055f, accent, 0.0f));
                }

                break;
            case MenuIcon.Settings:
                parts.Add(Rect(0, 0, 0.5f, 0.5f, ink, 99f));
                foreach (var (x, y) in new[] { (0f, -0.34f), (0f, 0.34f), (-0.34f, 0f), (0.34f, 0f) })
                {
                    parts.Add(Rect(x, y, x == 0 ? 0.16f : 0.18f, y == 0 ? 0.16f : 0.18f, ink, 0.03f));
                }

                foreach (var (x, y) in new[] { (-0.24f, -0.24f), (0.24f, -0.24f), (-0.24f, 0.24f), (0.24f, 0.24f) })
                {
                    parts.Add(Disc(x, y, 0.18f, ink));
                }

                parts.Add(Disc(0, 0, 0.22f, plate));
                break;
            case MenuIcon.Campaign: // 人と、腰の金のベルト
                parts.Add(Disc(0, -0.3f, 0.28f, ink));
                parts.Add(Rect(0, 0.12f, 0.54f, 0.5f, ink, 0.24f));
                parts.Add(Rect(0, 0.1f, 0.72f, 0.16f, accent, 0.04f));
                parts.Add(Disc(0, 0.1f, 0.2f, plate));
                break;
            default: // Quit: 電源の印（輪の上を開けて、棒を立てる）
                parts.Add(Ring(0, 0.04f, 0.7f, ink, 0.08f));
                parts.Add(Rect(0, -0.3f, 0.2f, 0.26f, plate, 0.0f));
                parts.Add(Rect(0, -0.2f, 0.09f, 0.4f, ink, 99f));
                break;
        }

        return parts;
    }

    /// <summary>
    /// はめる物・武器の絵（横長: 幅は <paramref name="height"/> の 2 倍）。握る武器があればそれ、無ければはめる物。
    /// </summary>
    public static void DrawEquipment(List<UiShapeDraw> shapes, Vector2 center, float height, WeaponKind? weapon, HandWear wear)
        => Emit(shapes, center, height, weapon is { } w ? WithOffhand(WeaponParts(w), wear) : WearParts(wear));

    /// <summary>握る武器の絵に、空いた手の盾を足す（剣と盾。剣を左へ縮めて、右に盾の円を置く）。盾でなければそのまま。</summary>
    private static List<Part> WithOffhand(List<Part> weapon, HandWear wear)
    {
        if (wear != HandWear.Shield)
        {
            return weapon;
        }

        return [.. weapon.Select(p => p with { X = (p.X * 0.62f) - 0.42f, Y = p.Y * 0.62f, W = p.W * 0.62f, H = p.H * 0.62f }), .. WearParts(wear)];
    }

    private static List<Part> WeaponParts(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Katana =>
        [
            Rect(0.2f, 0.02f, 1.4f, 0.09f, Steel, 0.02f), Rect(0.2f, 0.06f, 1.4f, 0.03f, SteelDark, 0f),
            Rect(0.93f, -0.015f, 0.14f, 0.05f, Steel, 0.02f),
            Rect(-0.52f, 0f, 0.07f, 0.3f, Gold, 0.03f), Rect(-0.8f, 0f, 0.46f, 0.11f, WoodDark, 0.04f),
            Rect(-0.8f, 0f, 0.46f, 0.035f, Leather, 0f), Disc(-1.04f, 0f, 0.12f, Gold),
        ],
        WeaponKind.Longsword or WeaponKind.IronSword =>
        [
            Rect(0.34f, 0f, 1.3f, 0.15f, Steel, 0.04f), Rect(0.34f, 0f, 1.22f, 0.04f, SteelDark, 0f),
            Rect(0.98f, 0f, 0.12f, 0.08f, Steel, 0.03f),
            Rect(-0.34f, 0f, 0.1f, 0.62f, Gold, 0.04f), Rect(-0.66f, 0f, 0.42f, 0.1f, WoodDark, 0.03f),
            Disc(-0.92f, 0f, 0.17f, Gold),
        ],
        WeaponKind.Staff =>
        [
            Rect(0f, 0f, 2f, 0.1f, Wood, 0.05f), Rect(-0.9f, 0f, 0.2f, 0.1f, WoodDark, 0.05f), Rect(0.9f, 0f, 0.2f, 0.1f, WoodDark, 0.05f),
        ],
        WeaponKind.Jo =>
        [
            Rect(0f, 0f, 1.45f, 0.08f, Wood, 0.04f), Rect(-0.65f, 0f, 0.15f, 0.08f, WoodDark, 0.04f), Rect(0.65f, 0f, 0.15f, 0.08f, WoodDark, 0.04f),
        ],
        WeaponKind.WoodenClub or WeaponKind.Bat =>
        [
            Rect(-0.78f, 0f, 0.42f, 0.10f, WoodDark, 0.04f), Rect(-0.40f, 0f, 0.4f, 0.14f, Wood, 0.05f),
            Rect(0f, 0f, 0.4f, 0.19f, Wood, 0.06f), Rect(0.4f, 0f, 0.4f, 0.25f, Wood, 0.08f),
            Rect(0.82f, 0f, 0.36f, 0.30f, Wood, 0.12f), Rect(0.82f, -0.06f, 0.3f, 0.06f, new Vector4(0.78f, 0.52f, 0.28f, 1f), 0.03f),
        ],
        _ => [],
    };

    private static List<Part> WearParts(HandWear wear) => wear switch
    {
        HandWear.Glove =>
        [
            Rect(-0.62f, 0f, 0.3f, 0.5f, Cuff, 0.05f),
            Rect(0.12f, 0f, 0.92f, 0.66f, Leather, 0.28f), Rect(0.0f, -0.32f, 0.38f, 0.22f, Leather, 0.1f),
            Rect(0.12f, 0.12f, 0.7f, 0.04f, new Vector4(0.55f, 0.1f, 0.1f, 1f), 0f),
        ],
        HandWear.None =>
        [
            Rect(-0.55f, 0f, 0.42f, 0.4f, Skin, 0.05f),
            Rect(0.15f, 0f, 0.82f, 0.62f, Skin, 0.22f),
            Rect(0.0f, 0f, 0.025f, 0.5f, new Vector4(0.55f, 0.36f, 0.24f, 1f), 0f),
            Rect(0.3f, 0f, 0.025f, 0.5f, new Vector4(0.55f, 0.36f, 0.24f, 1f), 0f),
            Rect(0.55f, 0f, 0.025f, 0.5f, new Vector4(0.55f, 0.36f, 0.24f, 1f), 0f),
            Rect(-0.15f, -0.32f, 0.3f, 0.16f, Skin, 0.07f),
        ],
        HandWear.KnuckleDuster =>
        [
            Ring(-0.6f, 0f, 0.4f, Steel, 0.07f), Ring(-0.2f, 0f, 0.4f, Steel, 0.07f),
            Ring(0.2f, 0f, 0.4f, Steel, 0.07f), Ring(0.6f, 0f, 0.4f, Steel, 0.07f),
            Rect(0f, 0.2f, 1.5f, 0.08f, SteelDark, 0.02f),
        ],
        HandWear.Katar =>
        [
            Rect(-0.1f, -0.26f, 1f, 0.07f, SteelDark, 0.02f), Rect(-0.1f, 0.26f, 1f, 0.07f, SteelDark, 0.02f),
            Rect(-0.55f, 0f, 0.07f, 0.5f, Gold, 0.02f), Rect(-0.3f, 0f, 0.07f, 0.5f, Gold, 0.02f),
            Rect(0.55f, 0f, 1.1f, 0.2f, Steel, 0.04f), Rect(0.55f, 0f, 1.0f, 0.045f, SteelDark, 0f),
            Rect(1.0f, 0f, 0.14f, 0.1f, Steel, 0.03f),
        ],
        HandWear.Gauntlet =>
        [
            Rect(-0.62f, 0f, 0.3f, 0.78f, SteelDark, 0.06f),
            Rect(0.12f, 0f, 0.92f, 0.66f, Steel, 0.2f),
            Rect(-0.1f, 0f, 0.03f, 0.55f, SteelDark, 0f), Rect(0.2f, 0f, 0.03f, 0.55f, SteelDark, 0f), Rect(0.5f, 0f, 0.03f, 0.55f, SteelDark, 0f),
            Rect(-0.35f, 0f, 0.1f, 0.7f, Gold, 0.02f),
        ],
        HandWear.Tonfa =>
        [
            Rect(0.1f, 0.3f, 1.5f, 0.15f, Wood, 0.06f), Rect(-0.35f, 0.14f, 0.1f, 0.3f, WoodDark, 0.03f),
            Rect(0.1f, -0.3f, 1.5f, 0.15f, Wood, 0.06f), Rect(-0.35f, -0.14f, 0.1f, 0.3f, WoodDark, 0.03f),
        ],
        HandWear.Shield =>
        [
            Disc(0.55f, 0f, 0.95f, Wood), Ring(0.55f, 0f, 0.95f, Steel, 0.06f), Disc(0.55f, 0f, 0.34f, Steel), Disc(0.55f, 0f, 0.18f, SteelDark),
        ],
        _ => [],
    };

    /// <summary>国旗（3:2）。色の帯に丸と四角を重ねた簡略の図。外側に細い枠を付ける。</summary>
    public static void DrawFlag(List<UiShapeDraw> shapes, Vector2 center, float width, FlagSpec flag)
    {
        var height = width / CharacterRoster.FlagAspect;
        var origin = center - new Vector2(width, height) / 2f;
        var n = flag.Stripes.Count;
        for (var i = 0; i < n; i++)
        {
            var color = new Vector4(flag.Stripes[i], 1f);
            var (offset, size) = flag.VerticalStripes
                ? (new Vector2(i * width / n, 0f), new Vector2(width / n, height))
                : (new Vector2(0f, i * height / n), new Vector2(width, height / n));
            shapes.Add(UiShapeDraw.Rounded(origin + offset + (size / 2f), size + new Vector2(0.6f), 0f, color));
        }

        foreach (var mark in flag.Marks)
        {
            var size = mark.Size * new Vector2(width, height);
            var at = origin + (mark.Center * new Vector2(width, height));
            shapes.Add(UiShapeDraw.Rounded(at, size, mark.Shape == FlagShape.Disc ? 999f : 0f, new Vector4(mark.Color, 1f)));
        }

        shapes.Add(UiShapeDraw.Outline(center, new Vector2(width, height), 1f, new Vector4(0.05f, 0.1f, 0.18f, 0.9f), 1.2f));
    }

    private static void Emit(List<UiShapeDraw> shapes, Vector2 center, float size, IEnumerable<Part> parts)
    {
        foreach (var p in parts)
        {
            var at = center + (new Vector2(p.X, p.Y) * size);
            var extent = new Vector2(p.W, p.H) * size;
            var radius = MathF.Min(p.Radius * size, MathF.Min(extent.X, extent.Y) / 2f);
            shapes.Add(p.Border > 0f
                ? UiShapeDraw.Outline(at, extent, radius, p.Color, p.Border * size)
                : UiShapeDraw.Rounded(at, extent, radius, p.Color));
        }
    }
}

/// <summary>メニューのボタンの絵の種類。</summary>
internal enum MenuIcon { Single, Versus, Spectate, Replays, Settings, Quit, Campaign }
