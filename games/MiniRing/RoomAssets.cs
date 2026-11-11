using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 部屋の素材（無料・CC0。出典は Assets/CREDITS.md）。ゲームに埋め込んであり、1 度だけ読む。
/// <list type="bullet">
/// <item>色のテクスチャ（ambientCG）: 木の床・漆喰の壁・布（ラグ）・木（テーブルと台座）。エンジンの <see cref="SurfaceTextureSet.Builder.AddColour"/>
/// で R・G・B の 3 層にして、壁や床へ世界の向きから貼る（UV は要らない）。</item>
/// <item>家具（Kenney Furniture Kit）: OBJ と MTL。材質ごとに 1 色のメッシュ。キットの寸法は実物のおよそ半分なので
/// <see cref="FurnitureScale"/> 倍にして実物の大きさにする（ソファの幅 2 m、本棚の高さ 1.8 m）。</item>
/// </list>
/// </summary>
public sealed class RoomAssets
{
    /// <summary>Kenney の家具を実物の大きさにする倍率。</summary>
    public const float FurnitureScale = 2.05f;

    private static readonly Lazy<RoomAssets> Shared = new(Load);
    private readonly Dictionary<string, ObjModel> _furniture;

    private RoomAssets(SurfaceTextureSet textures, ColourTexture floor, ColourTexture wall, ColourTexture fabric, ColourTexture wood, Dictionary<string, ObjModel> furniture)
    {
        Textures = textures;
        Floor = floor;
        Wall = wall;
        Fabric = fabric;
        Wood = wood;
        _furniture = furniture;
    }

    public static RoomAssets Instance => Shared.Value;

    /// <summary>テクスチャの層（<see cref="SceneLighting.SurfaceTextures"/> に渡す）。</summary>
    public SurfaceTextureSet Textures { get; }

    public ColourTexture Floor { get; }

    public ColourTexture Wall { get; }

    public ColourTexture Fabric { get; }

    public ColourTexture Wood { get; }

    /// <summary>家具の名前（Kenney のファイル名）から、そのモデル。</summary>
    public ObjModel Furniture(string name) => _furniture.TryGetValue(name, out var model)
        ? model
        : throw new ArgumentException($"家具 '{name}' は埋め込まれていません（{string.Join(", ", _furniture.Keys)}）。", nameof(name));

    /// <summary>家具の名前の一覧。</summary>
    public static IReadOnlyList<string> FurnitureNames { get; } =
    [
        "loungeSofa", "loungeChair", "bookcaseOpen", "books", "cabinetTelevision", "televisionModern", "speaker",
        "pottedPlant", "plantSmall1", "lampRoundFloor", "sideTable",
    ];

    private static RoomAssets Load()
    {
        var builder = new SurfaceTextureSet.Builder();
        var floor = builder.AddColour(Png("WoodFloor041"));
        var wall = builder.AddColour(Png("Plaster001"));
        var fabric = builder.AddColour(Png("Fabric026"));
        var wood = builder.AddColour(Png("Wood049"));
        var furniture = FurnitureNames.ToDictionary(n => n, Obj);
        return new RoomAssets(builder.Build(), floor, wall, fabric, wood, furniture);
    }

    private static TextureData Png(string name)
    {
        using var stream = Resource($"MiniRing.Assets.Textures.{name}.png");
        return TextureData.FromPng(stream);
    }

    private static ObjModel Obj(string name)
    {
        using var obj = new StreamReader(Resource($"MiniRing.Assets.Furniture.{name}.obj"));
        using var mtl = new StreamReader(Resource($"MiniRing.Assets.Furniture.{name}.mtl"));
        return ObjModel.Parse(obj, mtl, name);
    }

    private static Stream Resource(string name)
        => typeof(RoomAssets).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"埋め込みのリソース '{name}' がありません（games/MiniRing/Assets と MiniRing.csproj を確かめてください）。");
}

/// <summary>部屋に置いた家具 1 つ: どのモデルを、床のどこに、どちらへ向けて置くか。</summary>
/// <param name="Name">Kenney のモデルの名前。</param>
/// <param name="Position">置く位置（モデルの底の真ん中が来る所、m）。</param>
/// <param name="Yaw">Y 軸回りの回転 [rad]（0 でモデルの正面が +Z）。</param>
/// <param name="Scale">倍率（<see cref="RoomAssets.FurnitureScale"/> にさらに掛ける。1 で実物）。</param>
public readonly record struct PlacedFurniture(string Name, Vector3 Position, float Yaw, float Scale = 1f)
{
    /// <summary>このモデルの材質ごとの描画（モデルの底の真ん中を <see cref="Position"/> に合わせる）。</summary>
    public IEnumerable<PrimitiveInstance> Instances(RoomAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var model = assets.Furniture(Name);
        var (min, max) = model.Bounds;
        var pivot = new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
        var transform = Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateScale(RoomAssets.FurnitureScale * Scale)
            * Matrix4x4.CreateRotationY(Yaw)
            * Matrix4x4.CreateTranslation(Position);
        foreach (var part in model.Parts)
        {
            yield return new PrimitiveInstance(PrimitiveKind.Cube, transform, Vector3.Zero, part.Color, Mesh: part.Mesh);
        }
    }
}
