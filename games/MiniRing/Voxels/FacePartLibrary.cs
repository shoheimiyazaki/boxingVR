using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Voxels;

/// <summary>顔の格子のセルの材質（組み立ての出力。ゲームの材質の表への写しは顔の層が決める）。</summary>
public enum FaceMaterial : byte
{
    Empty = 0,
    Skin,
    Hair,
    Brow,
    White,
    Iris,
    Lip,

    /// <summary>影になった皮膚（耳の内側・まぶたの影・深い窪み）。</summary>
    Shadow,
    Nostril,

    /// <summary>そばかす（皮膚の上の小さな点）。</summary>
    Freckle,
}

/// <summary>
/// 部品の 1 つの見え方（正面・横・上のどれか）の文字の格子を材質にしたもの。大きさは参考図の格子（1 セル = 2.5 mm、頭の箱 20 x 28 x 20）で、
/// 座標は左上が原点（ピクセルの縁の座標: 0..Width, 0..Height）。標本化は双一次で、格子の解像度に依らない。
/// </summary>
public sealed class FaceRaster
{
    public FaceRaster(int width, int height, FaceMaterial[] cells)
    {
        if (width <= 0 || height <= 0 || cells.Length != width * height)
        {
            throw new InvalidDataException($"格子の大きさ {width}x{height} とセルの数 {cells.Length} が合いません。");
        }

        Width = width;
        Height = height;
        Cells = cells;
    }

    public int Width { get; }

    public int Height { get; }

    public FaceMaterial[] Cells { get; }

    /// <summary>(px, py) での、材質 <paramref name="material"/> の割合（0..1、双一次。外は 0）。</summary>
    public float Coverage(float px, float py, FaceMaterial material) => Sample(px, py, (int)material, 1);

    /// <summary>(px, py) での、空でないセルの割合。</summary>
    public float Solid(float px, float py) => Sample(px, py, 0, -1);

    // mode 1: 材質 == match、mode -1: 材質 != match（割り当てなしにするため、関数を渡さず 2 つの形だけ持つ）。
    private float Sample(float px, float py, int match, int mode)
    {
        var fx = px - 0.5f;
        var fy = py - 0.5f;
        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        float At(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? 0f : ((Cells[x + (Width * y)] == (FaceMaterial)match) == (mode > 0)) ? 1f : 0f;
        var top = (At(x0, y0) * (1f - tx)) + (At(x0 + 1, y0) * tx);
        var bottom = (At(x0, y0 + 1) * (1f - tx)) + (At(x0 + 1, y0 + 1) * tx);
        return (top * (1f - ty)) + (bottom * ty);
    }
}

/// <summary>部品 1 つ: 正面・横・上の格子と、留める点（正面の格子の座標。耳だけ横の格子の座標。目・眉・口は材質の重心、鼻は鼻筋の上の端、耳は横の格子の重心）。</summary>
public sealed class FacePart
{
    public FacePart(string id, string name, (float X, float Y) anchor, FaceRaster front, FaceRaster? side, FaceRaster? top)
    {
        Id = id;
        Name = name;
        Anchor = anchor;
        Front = front;
        Side = side;
        Top = top;
    }

    public string Id { get; }

    public string Name { get; }

    public (float X, float Y) Anchor { get; }

    public FaceRaster Front { get; }

    public FaceRaster? Side { get; }

    public FaceRaster? Top { get; }
}

/// <summary>基準の顔の目印（参考図の格子の座標。右・下がプラス）。</summary>
public sealed record FaceLayout(
    (float X, float Y) EyeLeftImage, (float X, float Y) EyeRightImage, (float X, float Y) BrowLeftImage, (float X, float Y) BrowRightImage,
    (float X, float Y) Mouth, (float X, float Y) EarSide, float NoseTopRow, int NoseRowFrom, int NoseRowTo, int ProfileCap, int NoseSurface);

/// <summary>
/// 顔の部品の表（<c>games/MiniRing/Voxels/FaceParts/*.json</c>、組み込み資源）。部位ごとに部品の一覧を持つ。ファイルは Codex の参考図から
/// <c>tools/face-parts/build_face_parts.py</c> で作る。読み込みは検証し、壊れていれば <see cref="InvalidDataException"/> で止まる（黙って続けない）。
/// </summary>
public sealed class FacePartLibrary
{
    /// <summary>部位の名前。</summary>
    public static readonly IReadOnlyList<string> Categories = ["jaw", "hair", "beard", "eyes", "brows", "mouth", "nose", "ears"];

    /// <summary>
    /// 後から足せる部位（データだけで足す: <c>FaceParts/&lt;名前&gt;.json</c> を置くと読み込まれる。まぶた・髪質・眉の張り出し・唇・顔の形）。
    /// 組み立ての対応は <see cref="FaceComposer"/> に書いた物だけ（その他は読み込んで検証するだけ）。
    /// </summary>
    public static readonly IReadOnlyList<string> Extra = ["eyelids", "hairtexture", "browridge", "lips", "faceshape"];

    /// <summary>顔の格子の大きさ（参考図、セル）。</summary>
    public const int RefWidth = 20;

    public const int RefHeight = 28;

    public const int RefDepth = 20;

    private static readonly Lazy<FacePartLibrary> Shipped = new(() => LoadEmbedded());

    private readonly Dictionary<string, IReadOnlyList<FacePart>> _byCategory;

    private FacePartLibrary(Dictionary<string, IReadOnlyList<FacePart>> byCategory, FaceRaster baseFront, FaceRaster baseSide, FaceRaster baseTop, FaceLayout layout)
    {
        _byCategory = byCategory;
        BaseFront = baseFront;
        BaseSide = baseSide;
        BaseTop = baseTop;
        Layout = layout;
    }

    /// <summary>組み込みの表。</summary>
    public static FacePartLibrary Default => Shipped.Value;

    public FaceRaster BaseFront { get; }

    public FaceRaster BaseSide { get; }

    /// <summary>基準の頭を上から見た輪郭（上の端が顔の前。参考図の「鼻が上」）。</summary>
    public FaceRaster BaseTop { get; }

    public FaceLayout Layout { get; }

    /// <summary>この表にある全ての部位（必須 + 読み込んだ後から足せる部位）。</summary>
    public IEnumerable<string> AllCategories => _byCategory.Keys;

    public IReadOnlyList<FacePart> Parts(string category)
        => _byCategory.TryGetValue(category, out var parts) ? parts : throw new KeyNotFoundException($"顔の部位 '{category}' は知りません。");

    public FacePart Get(string id)
    {
        var dot = id.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && _byCategory.TryGetValue(id[..dot], out var parts) && parts.FirstOrDefault(p => p.Id == id) is { } part)
        {
            return part;
        }

        throw new KeyNotFoundException($"顔の部品 '{id}' は表にありません。");
    }

    /// <summary>組み込みの JSON から表を作る。</summary>
    public static FacePartLibrary LoadEmbedded()
    {
        string Text(string name)
        {
            using var stream = typeof(FacePartLibrary).Assembly.GetManifestResourceStream($"MiniRing.Voxels.FaceParts.{name}.json")
                ?? throw new InvalidDataException($"顔の部品の表 '{name}.json' が組み込まれていません。");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var names = typeof(FacePartLibrary).Assembly.GetManifestResourceNames();
        var present = Categories.Concat(Extra.Where(c => names.Contains($"MiniRing.Voxels.FaceParts.{c}.json")));
        return Load(Text("base"), present.ToDictionary(c => c, Text));
    }

    /// <summary>JSON の文字列から表を作る（テスト用に公開）。壊れていれば <see cref="InvalidDataException"/>。</summary>
    public static FacePartLibrary Load(string baseJson, IReadOnlyDictionary<string, string> categoryJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        try
        {
            var baseDto = JsonSerializer.Deserialize<BaseDto>(baseJson, options) ?? throw new InvalidDataException("base.json が空です。");
            var legend = Legend(baseDto.Legend);
            FaceRaster View(string name, int width, int height)
            {
                var view = baseDto.Views?.GetValueOrDefault(name) ?? throw new InvalidDataException($"base.json に {name} がありません。");
                var raster = Raster(view, legend, $"base.{name}");
                return raster.Width == width && raster.Height == height ? raster : throw new InvalidDataException($"base.{name} は {width}x{height} にしてください。");
            }

            var layout = baseDto.Layout ?? throw new InvalidDataException("base.json に layout がありません。");
            if (layout.NoseRows is not { Length: 2 } || layout.NoseRows[0] >= layout.NoseRows[1] || layout.ProfileCap is < 1 or > RefDepth || layout.NoseSurface is < 0 or >= RefDepth || !float.IsFinite(layout.NoseTopRow))
            {
                throw new InvalidDataException("base.json の layout（noseRows・profileCap・noseSurface・noseTopRow）が不正です。");
            }

            var byCategory = new Dictionary<string, IReadOnlyList<FacePart>>();
            foreach (var category in Categories.Concat(Extra.Where(categoryJson.ContainsKey)))
            {
                var json = categoryJson.GetValueOrDefault(category) ?? throw new InvalidDataException($"部位 '{category}' の JSON がありません。");
                var dto = JsonSerializer.Deserialize<CategoryDto>(json, options) ?? throw new InvalidDataException($"{category}.json が空です。");
                if (dto.Category != category)
                {
                    throw new InvalidDataException($"{category}.json の category が '{dto.Category}' です。");
                }

                var cats = Legend(dto.Legend);
                var parts = new List<FacePart>();
                foreach (var p in dto.Parts ?? [])
                {
                    var id = p.Id ?? throw new InvalidDataException($"{category}.json に id の無い部品があります。");
                    if (!id.StartsWith(category + ".", StringComparison.Ordinal) || parts.Any(q => q.Id == id))
                    {
                        throw new InvalidDataException($"部品 id '{id}' が不正か重複です。");
                    }

                    FaceRaster? Find(string view) => p.Views is { } v && v.TryGetValue(view, out var d) ? Raster(d, cats, $"{id}.{view}") : null;
                    var front = Find("front") ?? throw new InvalidDataException($"{id} に front がありません。");
                    var anchor = Pair(p.Anchor, $"{id}.anchor");
                    var anchored = category == "ears" ? Find("side") ?? front : front; // 耳の留める点は横の格子の座標
                    if (anchor.X < 0 || anchor.Y < 0 || anchor.X > anchored.Width || anchor.Y > anchored.Height)
                    {
                        throw new InvalidDataException($"{id} の留める点 ({anchor.X}, {anchor.Y}) が格子の外です。");
                    }

                    parts.Add(new FacePart(id, p.Name ?? id, anchor, front, Find("side"), Find("top")));
                }

                if (parts.Count == 0)
                {
                    throw new InvalidDataException($"部位 '{category}' に部品がありません。");
                }

                byCategory[category] = parts;
            }

            // 部位ごとに要る見え方: 輪郭・髪・ひげ・鼻・耳は横も要る。髪は上も使う。輪郭・髪・ひげは頭全体の格子。
            foreach (var (category, parts) in byCategory)
            {
                var needsSide = category is "jaw" or "hair" or "beard" or "nose" or "ears";
                foreach (var part in parts)
                {
                    if (needsSide && part.Side is null)
                    {
                        throw new InvalidDataException($"{part.Id} に side がありません。");
                    }

                    if (category == "hair" && part.Top is null)
                    {
                        throw new InvalidDataException($"{part.Id} に top がありません。");
                    }

                    if (category is "jaw" or "hair" or "beard" && (part.Front.Width != RefWidth || part.Front.Height != RefHeight || part.Side!.Width != RefDepth || part.Side.Height != RefHeight))
                    {
                        throw new InvalidDataException($"{part.Id} は頭全体の格子（{RefWidth}x{RefHeight}）にしてください。");
                    }
                }
            }

            var result = new FaceLayout(
                Pair(layout.EyeLeftImage, "eyeLeftImage"), Pair(layout.EyeRightImage, "eyeRightImage"),
                Pair(layout.BrowLeftImage, "browLeftImage"), Pair(layout.BrowRightImage, "browRightImage"),
                Pair(layout.Mouth, "mouth"), Pair(layout.EarSide, "earSide"), layout.NoseTopRow, layout.NoseRows[0], layout.NoseRows[1], layout.ProfileCap, layout.NoseSurface);
            return new FacePartLibrary(byCategory, View("front", RefWidth, RefHeight), View("side", RefDepth, RefHeight), View("top", RefWidth, RefDepth), result);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"顔の部品の JSON が読めません: {ex.Message}", ex);
        }
    }

    private static (float X, float Y) Pair(float[]? value, string field)
        => value is { Length: 2 } && float.IsFinite(value[0]) && float.IsFinite(value[1]) ? (value[0], value[1]) : throw new InvalidDataException($"{field} は 2 つの有限値にしてください。");

    private static Dictionary<char, FaceMaterial> Legend(Dictionary<string, string>? legend)
    {
        var result = new Dictionary<char, FaceMaterial>();
        foreach (var (symbol, name) in legend ?? throw new InvalidDataException("legend がありません。"))
        {
            if (symbol.Length != 1 || !Enum.TryParse<FaceMaterial>(name, ignoreCase: true, out var material) || !Enum.IsDefined(material))
            {
                throw new InvalidDataException($"legend の '{symbol}' → '{name}' が不正です。");
            }

            result[symbol[0]] = material;
        }

        return result;
    }

    private static FaceRaster Raster(ViewDto view, Dictionary<char, FaceMaterial> legend, string what)
    {
        var rows = view.Rows ?? throw new InvalidDataException($"{what} に rows がありません。");
        if (rows.Length != view.Height || view.Width <= 0 || rows.Any(r => r.Length != view.Width))
        {
            throw new InvalidDataException($"{what} の rows が width {view.Width} x height {view.Height} と合いません。");
        }

        var cells = new FaceMaterial[view.Width * view.Height];
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < view.Width; x++)
            {
                cells[x + (view.Width * y)] = legend.TryGetValue(rows[y][x], out var m) ? m : throw new InvalidDataException($"{what} の文字 '{rows[y][x]}' が legend にありません。");
            }
        }

        return new FaceRaster(view.Width, view.Height, cells);
    }

    private sealed class ViewDto
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public string[]? Rows { get; init; }
    }

    private sealed class PartDto
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public float[]? Anchor { get; init; }
        public Dictionary<string, ViewDto>? Views { get; init; }
    }

    private sealed class CategoryDto
    {
        public string? Category { get; init; }
        public Dictionary<string, string>? Legend { get; init; }
        public List<PartDto>? Parts { get; init; }
    }

    private sealed class LayoutDto
    {
        public float[]? EyeLeftImage { get; init; }
        public float[]? EyeRightImage { get; init; }
        public float[]? BrowLeftImage { get; init; }
        public float[]? BrowRightImage { get; init; }
        public float[]? Mouth { get; init; }
        public float[]? EarSide { get; init; }
        public float NoseTopRow { get; init; }
        public int[]? NoseRows { get; init; }
        public int ProfileCap { get; init; }
        public int NoseSurface { get; init; }
    }

    private sealed class BaseDto
    {
        public LayoutDto? Layout { get; init; }
        public Dictionary<string, string>? Legend { get; init; }
        public Dictionary<string, ViewDto>? Views { get; init; }
    }
}
