using System.Numerics;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>部屋の素材: OBJ の読み込み、色のテクスチャ（エンジン）、埋め込んだ家具とその置き場所。</summary>
[TestClass]
public sealed class RoomAssetsTests
{
    [TestMethod]
    public void Obj_SplitsByMaterial_FansQuads_AndTakesKdColours()
    {
        const string obj = """
            mtllib x.mtl
            v 0 0 0
            v 1 0 0
            v 1 0 1
            v 0 0 1
            v 0 1 0
            vn 0 1 0
            usemtl red
            f 1//1 2//1 3//1 4//1
            usemtl blue
            f 1/1 2/1 5/1
            """;
        const string mtl = """
            newmtl red
            Kd 1 0 0
            newmtl blue
            Kd 0 0 1
            """;

        var model = ObjModel.Parse(new StringReader(obj), new StringReader(mtl));

        Assert.HasCount(2, model.Parts);
        Assert.AreEqual("red", model.Parts[0].Material);
        Assert.AreEqual(new Vector4(1f, 0f, 0f, 1f), model.Parts[0].Color);
        Assert.AreEqual(6, model.Parts[0].Mesh.VertexCount, "a quad is two triangles");
        Assert.AreEqual(new Vector4(0f, 0f, 1f, 1f), model.Parts[1].Color);
        // 法線の無い面は面の向き（1→2→5 の右手系: X × Y = +Z）。
        var v = model.Parts[1].Mesh.Vertices;
        Assert.AreEqual(1f, v[5], 1e-5f, "flat normal z");
        Assert.AreEqual((Vector3.Zero, Vector3.One), model.Bounds);
    }

    [TestMethod]
    public void Obj_RejectsBadIndices()
    {
        var error = Assert.Throws<InvalidDataException>(() => ObjModel.Parse(new StringReader("v 0 0 0\nf 1 2 3\n"), null, "bad"));
        StringAssert.Contains(error.Message, "bad:2");
    }

    [TestMethod]
    public void ColourTexture_KeepsEachChannelAroundTheSameMean_AndReportsTheImageColour()
    {
        // 左半分が (200, 100, 50)、右半分が (100, 50, 25) の 4×4 の画像。
        var rgba = new byte[4 * 4 * 4];
        for (var t = 0; t < 16; t++)
        {
            var bright = t % 4 < 2;
            rgba[t * 4] = (byte)(bright ? 200 : 100);
            rgba[(t * 4) + 1] = (byte)(bright ? 100 : 50);
            rgba[(t * 4) + 2] = (byte)(bright ? 50 : 25);
            rgba[(t * 4) + 3] = 255;
        }

        var builder = new SurfaceTextureSet.Builder();
        var detail = builder.AddDetail(TextureData.Solid(4, 4, Vector4.One));
        var colour = builder.AddColour(new TextureData(4, 4, rgba));
        var set = builder.Build();

        Assert.AreEqual(0, detail);
        Assert.AreEqual(1, colour.FirstLayer, "R, G, B follow the detail layer");
        Assert.AreEqual(4, set.LayerCount);
        Assert.AreEqual(150f / 255f, colour.Mean.X, 1e-4f);
        Assert.AreEqual(75f / 255f, colour.Mean.Y, 1e-4f);
        Assert.AreEqual(37.5f / 255f, colour.Mean.Z, 1e-4f);
        for (var c = 0; c < 3; c++)
        {
            var layer = set.Layer(colour.FirstLayer + c).ToArray();
            Assert.AreEqual(SurfaceTextureSet.NeutralLevel, layer.Average(b => b), 1.0, $"channel {c} mean");
            Assert.AreEqual(171, layer[0], 1, $"channel {c}: 4/3 of the mean where the image is bright");
        }

        var dressed = colour.On(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.Identity, Vector3.Zero, new Vector4(1f, 1f, 1f, 0.5f)), 2f);
        Assert.IsTrue(dressed.TextureInColour);
        Assert.AreEqual(1, dressed.TextureLayer);
        Assert.AreEqual(new Vector4(colour.Mean * colour.Gain, 0.5f), dressed.Color, "the image's mean colour, the instance's own opacity");
    }

    [TestMethod]
    public void ColourTexture_DoesNotClipADarkChannel_AndMakesUpTheMeanWithGain()
    {
        // 青がほとんど暗く（10）、1 つだけ明るい（200）。平均へ 128 に合わせると 200 は 255 を超えて切れる。
        var rgba = new byte[4 * 4 * 4];
        for (var t = 0; t < 16; t++)
        {
            rgba[t * 4] = 128;
            rgba[(t * 4) + 1] = 128;
            rgba[(t * 4) + 2] = (byte)(t == 0 ? 200 : 10);
            rgba[(t * 4) + 3] = 255;
        }

        var builder = new SurfaceTextureSet.Builder();
        var colour = builder.AddColour(new TextureData(4, 4, rgba));
        var blue = builder.Build().Layer(colour.FirstLayer + 2).ToArray();

        Assert.AreEqual(255, blue[0], 1, "the bright texel is the top of the range, not past it");
        Assert.AreEqual(blue[0] / 20.0, blue[1], 1.0, "the 20:1 ratio survives");
        // 描かれる平均 = 色 × 2 × 層の平均 / 255 が、画像の平均（21.875 / 255）になる。
        var drawnMean = colour.Mean.Z * colour.Gain.Z * 2.0 * blue.Average(x => x) / 255.0;
        Assert.AreEqual(21.875 / 255.0, drawnMean, 0.002);
        Assert.AreEqual(1f, colour.Gain.X, 0.01f, "an unclipped channel needs no gain");
    }

    [TestMethod]
    public void ColourTexture_RejectsLayersOfDifferentSizes()
    {
        var builder = new SurfaceTextureSet.Builder();
        builder.AddColour(TextureData.Solid(4, 4, Vector4.One));
        Assert.Throws<ArgumentException>(() => builder.AddDetail(TextureData.Solid(8, 8, Vector4.One)));
    }

    [TestMethod]
    public void EmbeddedFurniture_LoadsAndStandsInsideTheRoom_ClearOfThePlayerAndTheTable()
    {
        var assets = RoomAssets.Instance;
        Assert.IsGreaterThan(0, assets.Textures.LayerCount);
        foreach (var name in RoomAssets.FurnitureNames)
        {
            Assert.IsNotEmpty(assets.Furniture(name).Parts, name);
        }

        foreach (var (piece, layout) in LivingRoom.Furniture.SelectMany(p => new[] { (p, RingLayout.Boxing), (p, RingLayout.Weapons) }))
        {
            var (min, max) = Bounds(piece.Instances(assets));
            var label = $"{piece.Name} at {piece.Position}: {min} .. {max}";
            Assert.IsTrue(min.X >= LivingRoom.MinX - 0.01f && max.X <= LivingRoom.MaxX + 0.01f, $"inside the side walls: {label}");
            Assert.IsTrue(min.Z >= LivingRoom.MinZ - 0.01f && max.Z <= LivingRoom.MaxZ + 0.01f, $"inside the front and back walls: {label}");
            Assert.IsLessThan(LivingRoom.CeilingHeight, max.Y, $"under the ceiling: {label}");
            // 武器の試合の広いリングでも、テーブルのまわりを歩ける。
            var (table, tableHalf) = (layout.Center, layout.TableHalf);
            var clearOfTable = max.X < table.X - tableHalf - 0.3f || min.X > table.X + tableHalf + 0.3f
                || max.Z < table.Z - tableHalf - 0.3f || min.Z > table.Z + tableHalf + 0.3f;
            Assert.IsTrue(clearOfTable, $"room to walk around the table: {label}");
            var clearOfPlayer = new Vector2(MathF.Max(min.X, MathF.Min(0f, max.X)), MathF.Max(min.Z, MathF.Min(0f, max.Z))).Length() > 0.8f;
            Assert.IsTrue(clearOfPlayer, $"the player's spot (origin) is free: {label}");
        }
    }

    [TestMethod]
    public void Sofa_IsLifeSize()
    {
        var sofa = LivingRoom.Furniture.First(f => f.Name == "loungeSofa");
        var (min, max) = Bounds(sofa.Instances(RoomAssets.Instance));
        var size = max - min;
        Assert.IsTrue(size.X is > 1.8f and < 2.2f, $"a sofa about 2 m wide: {size}");
        Assert.IsTrue(size.Y is > 0.8f and < 1.0f, $"about 0.9 m high: {size}");
    }

    private static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<PrimitiveInstance> instances)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var instance in instances)
        {
            var mesh = instance.Mesh!;
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                var p = Vector3.Transform(new Vector3(mesh.Vertices[i * 6], mesh.Vertices[(i * 6) + 1], mesh.Vertices[(i * 6) + 2]), instance.Model);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        return (min, max);
    }
}
