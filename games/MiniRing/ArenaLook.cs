using System.Collections.Immutable;
using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 試合会場の「画づくり」（描画のみ。試合には響かない）: 暗い会場にリングだけが照らされる照明、
/// リングに落ちる影、照明の光の中に漂う空気の粒、仕上げ（光のにじみ・隅の陰り・コントラスト・溝の陰）。
/// 体（ボクセル）の陰影は <c>VoxelVolumePipeline</c> のシェーダーが同じ真上寄りの光で付ける。
/// </summary>
public static class ArenaLook
{
    /// <summary>照明（リングの真上やや手前から）。ボクセルのシェーダーの主光と同じ向き。</summary>
    public static Vector3 KeyDirection { get; } = Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f));

    /// <summary>リングの外の暗さ（背景の色）。</summary>
    public static Vector4 Background { get; } = new(0.025f, 0.028f, 0.04f, 1f);

    private const int Motes = 70;
    private const float LightHeight = 1.35f;

    /// <summary>会場の照明: 弱い環境光、リングを上から照らす主光、四隅の上からリングへ向けたスポット。</summary>
    public static SceneLighting Lighting(Vector3 ringCenter, float ringSize, float surfaceHeight)
    {
        var target = new Vector3(ringCenter.X, surfaceHeight, ringCenter.Z);
        var spots = ImmutableArray.CreateBuilder<SpotLight>();
        var corner = ringSize * 0.75f;
        foreach (var (x, z, color) in new[]
                 {
                     (-1f, -1f, new Vector3(1f, 0.92f, 0.82f)), (1f, -1f, new Vector3(0.85f, 0.9f, 1f)),
                     (-1f, 1f, new Vector3(0.85f, 0.9f, 1f)), (1f, 1f, new Vector3(1f, 0.92f, 0.82f)),
                 })
        {
            var position = target + new Vector3(x * corner, LightHeight, z * corner);
            spots.Add(new SpotLight(position, Vector3.Normalize(target - position), color, 0.32f, 3.2f, 34f));
        }

        return new SceneLighting(
            new DirectionalLight(KeyDirection, new Vector3(1f, 0.97f, 0.92f), 0.5f),
            new AmbientLight(new Vector3(0.5f, 0.56f, 0.72f), new Vector3(0.16f, 0.13f, 0.12f), 0.32f),
            ImmutableArray<PointLight>.Empty,
            new SpecularConfig(40f, 0.25f),
            ToneMapping.Legacy,
            spots.ToImmutable());
    }

    /// <summary>リングとその周りに影を落とす範囲（主光の影）。</summary>
    public static ShadowMapConfig Shadow(Vector3 ringCenter, float ringSize, float surfaceHeight)
    {
        var half = (ringSize * 0.5f) + 0.4f;
        return new ShadowMapConfig(
            new Vector3(ringCenter.X - half, 0f, ringCenter.Z - half),
            new Vector3(ringCenter.X + half, surfaceHeight + 0.9f, ringCenter.Z + half));
    }

    /// <summary>
    /// 仕上げ: 明るい所のにじみ、溝や接地の陰、隅の陰り、少し強いコントラスト。ピントぼかしはしない
    /// （ミニチュアに見せたいわけではない。VR でもぼかすと酔う）。<paramref name="vignette"/> は画面の隅の陰り（VR では 0）。
    /// </summary>
    public static PostProcessSettings Finish(float vignette) => new(
        FocusCenter: 0.5f,
        FocusHalfHeight: 1f,
        FocusFalloff: 1f,
        MaxBlurPixels: 0f,
        BloomThreshold: 0.68f,
        BloomIntensity: 0.55f,
        BloomRadiusPixels: 28f,
        Vignette: vignette,
        Saturation: 1.08f,
        Contrast: 1.12f,
        Warmth: 0.12f,
        Exposure: 0.97f,
        AmbientOcclusion: 1.4f);

    /// <summary>画面（動画・デスクトップ）用の仕上げ。</summary>
    public static PostProcessSettings ScreenFinish { get; } = Finish(vignette: 0.5f);

    /// <summary>
    /// 照明の光の中を漂う空気の粒（ほこり）と、光の筋。時刻 <paramref name="time"/> [s] だけで決まる（乱数なし・再現できる）。
    /// どれもごく淡い加算の光で、照らされた空気のように見せる。
    /// </summary>
    public static void WriteAtmosphere(Vector3 ringCenter, float ringSize, float surfaceHeight, float time, ICollection<ParticleInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var target = new Vector3(ringCenter.X, surfaceHeight, ringCenter.Z);
        var corner = ringSize * 0.75f;

        // 光の筋: 四隅の照明からリングへ、大きくて極く淡い粒を並べる（遠いほど広がる円すい）。
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var from = target + new Vector3(x * corner, LightHeight, z * corner);
            for (var k = 1; k <= 7; k++)
            {
                var f = k / 8f;
                var at = Vector3.Lerp(from, target, f * 0.85f);
                into.Add(new ParticleInstance(at, 0.08f + (0.45f * f), new Vector4(1f, 0.96f, 0.9f, 0.018f * (1f - (0.5f * f))), Blend: ParticleBlend.Additive));
            }
        }

        // ほこり: リングの上の空間をゆっくり漂い、ときどき光を受けてきらめく。
        for (var i = 0; i < Motes; i++)
        {
            var a = Hash(i, 1);
            var b = Hash(i, 2);
            var c = Hash(i, 3);
            var phase = Hash(i, 4) * 6.283f;
            var span = ringSize * 0.75f;
            var position = new Vector3(
                ringCenter.X + ((a - 0.5f) * 2f * span) + (0.05f * MathF.Sin((time * 0.21f) + phase)),
                surfaceHeight + 0.05f + (b * 0.9f) + Wrap((time * (0.006f + (0.01f * c))) + c, 0.12f),
                ringCenter.Z + ((c - 0.5f) * 2f * span) + (0.05f * MathF.Cos((time * 0.17f) + phase)));
            var twinkle = 0.5f + (0.5f * MathF.Sin((time * (0.6f + c)) + phase));
            into.Add(new ParticleInstance(position, 0.006f + (0.006f * a), new Vector4(1f, 0.95f, 0.85f, 0.12f + (0.22f * twinkle)), Blend: ParticleBlend.Additive));
        }
    }

    /// <summary>リングの外まわりの飾り（暗い会場で光るエプロンの帯、コーナーマット）。</summary>
    public static IEnumerable<PrimitiveInstance> Dressing(Vector3 ringCenter, float ringSize, float surfaceHeight)
    {
        var half = (ringSize * 0.5f) + 0.025f;
        var y = surfaceHeight - 0.045f;
        // エプロン（台のふち）を回る、細い光の帯。赤コーナー側が赤、青コーナー側が青、その間は白。
        var strips = new (Vector3 Center, Vector3 Half, Vector4 Color)[]
        {
            (new Vector3(ringCenter.X, y, ringCenter.Z - half), new Vector3(half, 0.004f, 0.002f), new Vector4(1.0f, 0.18f, 0.12f, 1f)),
            (new Vector3(ringCenter.X, y, ringCenter.Z + half), new Vector3(half, 0.004f, 0.002f), new Vector4(0.15f, 0.35f, 1.0f, 1f)),
            (new Vector3(ringCenter.X - half, y, ringCenter.Z), new Vector3(0.002f, 0.004f, half), new Vector4(0.9f, 0.9f, 0.95f, 1f)),
            (new Vector3(ringCenter.X + half, y, ringCenter.Z), new Vector3(0.002f, 0.004f, half), new Vector4(0.9f, 0.9f, 0.95f, 1f)),
        };
        foreach (var (center, halfExtents, color) in strips)
        {
            yield return new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(halfExtents * 2f) * Matrix4x4.CreateTranslation(center), Vector3.Zero, color, MaterialKind.Emissive);
        }

        // 天井の照明（四隅の上、光る小さな灯体）。画面に入れば光がにじむ。
        var corner = ringSize * 0.75f;
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var at = new Vector3(ringCenter.X + (x * corner), surfaceHeight + LightHeight, ringCenter.Z + (z * corner));
            yield return new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(at), new Vector3(0.03f, 0f, 0f), new Vector4(1f, 0.97f, 0.9f, 1f), MaterialKind.Emissive);
        }
    }

    private static float Hash(int i, int salt)
    {
        var h = (uint)((i * 374761393) + (salt * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    private static float Wrap(float value, float range) => (value % 1f) * range;
}
