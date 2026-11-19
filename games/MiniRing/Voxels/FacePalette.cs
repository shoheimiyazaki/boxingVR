using System.Numerics;

namespace MiniRing.Voxels;

/// <summary>
/// 顔の材質ごとの色（見た目だけ）。肌・髪・瞳の 3 色（<see cref="FaceSpec"/>）から、ほかの材質の色を一つの式で出す
/// （影 = 肌 × 0.76、鼻の穴 = 肌 × 0.48、唇 = 肌と赤茶 (0.52, 0.24, 0.24) の 55% の混ぜ、眉 = 髪に肌の暗い色を 2 割混ぜる、白目は暖かい白）。
/// 比は参考図 D の凡例（肌 #B98260 に対する耳の影 #8E5B45・鼻の穴 #593A30・唇 #995C5B・眉 #30251F）から。
/// </summary>
public static class FacePalette
{
    /// <summary>指定が無いときの肌・髪・瞳（参考図 D の凡例の既定。瞳は暗い茶色）。</summary>
    public static readonly Vector3 DefaultSkin = new(0.725f, 0.510f, 0.376f);

    public static readonly Vector3 DefaultHair = new(0.098f, 0.090f, 0.086f);

    public static readonly Vector3 DefaultIris = new(0.188f, 0.125f, 0.090f);

    private static readonly Vector3 White = new(0.953f, 0.929f, 0.894f);

    /// <summary>材質 → 色（0〜1 の RGB）。<paramref name="spec"/> に色が無ければ既定。</summary>
    public static IReadOnlyDictionary<FaceMaterial, Vector3> Resolve(FaceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var skin = spec.Skin ?? DefaultSkin;
        var hair = spec.Hair ?? DefaultHair;
        var iris = spec.Iris ?? DefaultIris;
        return new Dictionary<FaceMaterial, Vector3>
        {
            [FaceMaterial.Skin] = skin,
            [FaceMaterial.Hair] = hair,
            [FaceMaterial.Brow] = Vector3.Lerp(hair, skin * 0.3f, 0.2f),
            [FaceMaterial.White] = White,
            [FaceMaterial.Iris] = iris,
            [FaceMaterial.Lip] = Vector3.Lerp(skin, new Vector3(0.52f, 0.24f, 0.24f), 0.55f),
            [FaceMaterial.Shadow] = skin * 0.86f,
            [FaceMaterial.Nostril] = skin * 0.48f,
            [FaceMaterial.Freckle] = Vector3.Lerp(skin, new Vector3(0.53f, 0.32f, 0.2f), 0.3f),
            [FaceMaterial.Stubble] = Vector3.Lerp(skin, hair, 0.28f),
        };
    }
}
