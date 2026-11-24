using System.Numerics;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 写真から顔（<see cref="PhotoFace"/>）: 合成の顔の絵（本物の写真は使わない: 個人情報）から、描いた色を読み取る・目印の点の比が顔の値へ同じ向きに写る・
/// あごが暗ければひげ・髪が肌と同じ色なら坊主・同じ入力から同じ顔・作った顔で名簿のファイルが作れる。
/// </summary>
[TestClass]
public sealed class PhotoFaceTests
{
    private const int W = 400;
    private const int H = 500;
    private static readonly Vector3 Skin = new(0.72f, 0.52f, 0.38f);
    private static readonly Vector3 Hair = new(0.3f, 0.18f, 0.1f);
    private static readonly Vector3 Iris = new(0.25f, 0.4f, 0.55f);
    private static readonly Vector3 BeardColor = new(0.12f, 0.09f, 0.07f);

    /// <summary>合成の顔の目印（IOD = 100 px）。</summary>
    private static Dictionary<string, Vector2> Points(float mouthHalf = 40f, float jawHalf = 85f) => new()
    {
        ["leftEyeOuter"] = new(125, 230), ["leftEyeInner"] = new(175, 230), ["leftEyeTop"] = new(150, 222), ["leftEyeBottom"] = new(150, 238),
        ["rightEyeInner"] = new(225, 230), ["rightEyeOuter"] = new(275, 230), ["rightEyeTop"] = new(250, 222), ["rightEyeBottom"] = new(250, 238),
        ["leftBrow"] = new(150, 197), ["rightBrow"] = new(250, 197), ["noseBridge"] = new(200, 225), ["noseTip"] = new(200, 300),
        ["noseLeft"] = new(171, 300), ["noseRight"] = new(229, 300),
        ["mouthLeft"] = new(200 - mouthHalf, 350), ["mouthRight"] = new(200 + mouthHalf, 350), ["lipTop"] = new(200, 340), ["lipBottom"] = new(200, 362),
        ["chin"] = new(200, 420), ["cheekLeft"] = new(88, 250), ["cheekRight"] = new(312, 250),
        ["jawLeft"] = new(200 - jawHalf, 360), ["jawRight"] = new(200 + jawHalf, 360), ["forehead"] = new(200, 135),
    };

    /// <summary>合成の顔の絵（背景・顔の楕円・額の上の髪・白目と瞳と瞳孔・唇・ひげ）。</summary>
    private static byte[] Picture(Vector3 hair, bool beard)
    {
        var rgba = new byte[W * H * 4];
        void Put(int x, int y, Vector3 c)
        {
            var i = ((y * W) + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = ((byte)(c.X * 255f), (byte)(c.Y * 255f), (byte)(c.Z * 255f), 255);
        }

        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var c = new Vector3(0.55f, 0.6f, 0.65f);
                if (Sq((x - 200) / 125f) + Sq((y - 270) / 170f) <= 1f)
                {
                    c = Skin;
                }

                if (y is >= 60 and < 130 && x is >= 80 and < 320)
                {
                    c = hair;
                }

                foreach (var ex in new[] { 150, 250 })
                {
                    if (Sq((x - ex) / 25f) + Sq((y - 230) / 8f) <= 1f)
                    {
                        c = Vector3.One * 0.95f;
                    }

                    var d = Sq(x - ex) + Sq(y - 230);
                    if (d <= 49)
                    {
                        c = d <= 4 ? Vector3.Zero : Iris;
                    }
                }

                if (y is >= 340 and < 362 && x is >= 160 and < 240)
                {
                    c = new Vector3(0.7f, 0.3f, 0.3f);
                }

                if (beard && y is >= 368 and < 425 && x is >= 140 and < 260)
                {
                    c = BeardColor;
                }

                Put(x, y, c);
            }
        }

        return rgba;
    }

    private static float Sq(float v) => v * v;

    private static PhotoFace.Result Read(Vector3 hair, bool beard, Dictionary<string, Vector2>? points = null)
        => PhotoFace.FromPhoto(W, H, Picture(hair, beard), points ?? Points(), CharacterMaker.RootOf("europe-south").Face());

    [TestMethod]
    public void TheDrawnColors_AreReadBack_FromTheCheeksHairAndEyes()
    {
        var face = Read(Hair, beard: false).Face;
        AssertNear(Skin, face.Skin!.Value, "肌");
        AssertNear(Hair, face.Hair!.Value, "髪");
        AssertNear(Iris, face.Iris!.Value, "瞳");
        Assert.IsEmpty(face.Parts["beard"], "あごが肌の色ならひげは無い");
        Assert.AreNotEqual("hair.3", face.Parts["hair"].Keys.Single(), "髪があれば元の髪型のまま");
    }

    [TestMethod]
    public void ADarkChin_IsABeard_AndHairTheColourOfSkin_IsABaldHead()
    {
        var face = Read(Skin, beard: true).Face;
        Assert.IsNotEmpty(face.Parts["beard"]);
        Assert.AreEqual("hair.3", face.Parts["hair"].Keys.Single());
    }

    [TestMethod]
    public void WiderLandmarks_GiveWiderFaceValues_InTheSameDirection()
    {
        var narrow = Read(Hair, false, Points(mouthHalf: 32f, jawHalf: 70f));
        var wide = Read(Hair, false, Points(mouthHalf: 48f, jawHalf: 95f));
        Assert.IsGreaterThan(narrow.Face.Values.MouthWidth, wide.Face.Values.MouthWidth, "広い口");
        Assert.IsGreaterThan(narrow.Face.Values.JawWidth, wide.Face.Values.JawWidth, "広いあご");
        Assert.IsLessThan(narrow.Face.Values.Cheekbones, wide.Face.Values.Cheekbones, "あごが広いほど頬骨は目立たない");
        Assert.AreEqual(1f, Read(Hair, false).Measured.EyeWidth / 0.5f, 1e-4f, "目の幅 50 px / IOD 100 px");
    }

    [TestMethod]
    public void TheSameInput_GivesTheSameFace_AndItMakesALoadableFile()
    {
        var a = Read(Hair, true);
        var b = Read(Hair, true);
        Assert.AreEqual(a.Measured, b.Measured);
        Assert.AreEqual(CharacterFile.Face(a.Face).ToJsonString(), CharacterFile.Face(b.Face).ToJsonString());

        var draft = CharacterMaker.NewDraft() with
        {
            Id = "photo-test", Name = "写真のテスト", Country = "france",
            Face = FaceDraft.From(a.Face, CharacterMaker.LookOf(a.Face)),
        };
        var (character, _) = CharacterMaker.ToFile(draft);
        Assert.AreEqual(a.Face.Values, character.Face.Values);
    }

    [TestMethod]
    public void MissingOrOutsidePoints_AreRefused()
    {
        var points = Points();
        points.Remove("chin");
        Assert.Contains("chin", Assert.ThrowsExactly<InvalidDataException>(() => Read(Hair, false, points)).Message);
        var outside = Points();
        outside["forehead"] = new Vector2(200, -5);
        Assert.Contains("forehead", Assert.ThrowsExactly<InvalidDataException>(() => Read(Hair, false, outside)).Message);
        Assert.ThrowsExactly<InvalidDataException>(() => PhotoFace.FromPhoto(W, H, new byte[10], Points(), CharacterMaker.Roots[0].Face()));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, string what)
        => Assert.IsLessThan(0.02f, Vector3.Distance(expected, actual), $"{what}: {expected} と読んだ {actual}");
}
