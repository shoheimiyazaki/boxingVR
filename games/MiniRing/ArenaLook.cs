using System.Collections.Immutable;
using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 会場の雰囲気（描画のみ。試合には響かない。団体のファイルの <c>arenaMood</c>、2026-11-18）。既定（<see cref="Default"/>）は今の明るい昼の部屋で、
/// <see cref="ArenaLook.Lighting"/> に渡さなければ同じ値になる。倍率は 1 が今の明るさ、<see cref="CageSpotIntensity"/> は 0 なら檻（リング）の真上のスポットを足さない。
/// </summary>
public sealed record ArenaMood
{
    /// <summary>今の明るい昼の部屋。</summary>
    public static ArenaMood Default { get; } = new();

    /// <summary>空の環境光（上は空色・下は床の照り返し）にかける倍率。</summary>
    public float Ambient { get; init; } = 1f;

    /// <summary>窓からの昼の主光（影を作る光）にかける倍率。</summary>
    public float Key { get; init; } = 1f;

    /// <summary>天井の照明（スポットと点光源）にかける倍率。</summary>
    public float Lamp { get; init; } = 1f;

    /// <summary>窓から斜めに差す光にかける倍率。</summary>
    public float Window { get; init; } = 1f;

    /// <summary>リングの真上からリング全体へ絞るスポットの明るさ（0 なら足さない）。暗い会場で檻だけを照らす。</summary>
    public float CageSpotIntensity { get; init; }

    /// <summary>檻のスポットの色（RGB、0〜1）。</summary>
    public float[] CageSpotColor { get; init; } = [1f, 0.96f, 0.9f];

    /// <summary>画面の隅の陰り（0 なし、1 で隅が半分の明るさ）。</summary>
    public float Vignette { get; init; } = 0.5f;

    /// <summary>仕上げの露出（1 が変更なし）。</summary>
    public float Exposure { get; init; } = 0.97f;

    /// <summary>値の範囲を確かめる（団体のファイルの読み込みから呼ぶ）。範囲外は例外で止める。</summary>
    public void Validate()
    {
        foreach (var (name, value) in new[] { ("ambient", Ambient), ("key", Key), ("lamp", Lamp), ("window", Window), ("cageSpotIntensity", CageSpotIntensity), ("vignette", Vignette), ("exposure", Exposure) })
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new InvalidDataException($"arenaMood.{name} は 0 以上の有限値にしてください。");
            }
        }

        if (Vignette > 1f)
        {
            throw new InvalidDataException("arenaMood.vignette は 0〜1 にしてください。");
        }

        if (CageSpotColor is not { Length: 3 } || CageSpotColor.Any(v => !float.IsFinite(v) || v is < 0f or > 1f))
        {
            throw new InvalidDataException("arenaMood.cageSpotColor は 0〜1 の有限値 3 つ（RGB）で指定してください。");
        }
    }
}

/// <summary>
/// 試合会場の「画づくり」（描画のみ。試合には響かない）: 昼の部屋（<see cref="LivingRoom"/>）の明かり（窓の光・天井の照明・テーブルの上のスポット）、
/// リングに落ちる影、光の中に漂う空気の粒、仕上げ（光のにじみ・隅の陰り・コントラスト・溝の陰）。
/// 体（ボクセル）の陰影は <c>VoxelVolumePipeline</c> のシェーダーが同じ真上寄りの光で付ける。
/// </summary>
public static class ArenaLook
{
    /// <summary>照明（リングの真上やや手前から）。ボクセルのシェーダーの主光と同じ向き。</summary>
    public static Vector3 KeyDirection { get; } = Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f));

    /// <summary>背景の色（部屋に囲まれているので、ほぼ見えない。見えたときに目立たない壁の色）。</summary>
    public static Vector4 Background { get; } = new(0.78f, 0.76f, 0.72f, 1f);

    private const int Motes = 70;

    /// <summary>
    /// 部屋の明かり: 昼の空の環境光（上は空色・下は床の照り返し）、主光（窓からの昼の光。ボクセルのシェーダーと同じ向き）、
    /// 天井の照明（部屋全体へ）、テーブルの真上からリングへ絞ったスポット（箱庭を明るく見せる）、窓から斜めに差す光。
    /// <paramref name="mood"/>（団体の雰囲気。null なら今の明るい昼の部屋）で光ごとの強さを変え、暗い会場では檻の真上のスポットを足す。
    /// </summary>
    public static SceneLighting Lighting(Vector3 ringCenter, float ringSize, float surfaceHeight, ArenaMood? mood = null)
    {
        var m = mood ?? ArenaMood.Default;
        var target = new Vector3(ringCenter.X, surfaceHeight, ringCenter.Z);
        var lamp = LivingRoom.CeilingLamp(ringCenter);
        var window = LivingRoom.WindowCenter;
        var spots = ImmutableArray.CreateBuilder<SpotLight>();
        spots.Add(new SpotLight(lamp - new Vector3(0f, 0.05f, 0f), -Vector3.UnitY, new Vector3(1f, 0.93f, 0.82f), 0.55f * m.Lamp, 3.5f, 30f));
        spots.Add(new SpotLight(window - new Vector3(0f, 0f, 0.2f), Vector3.Normalize(target - window), new Vector3(0.85f, 0.92f, 1f), 0.3f * m.Window, 6f, 40f));
        if (m.CageSpotIntensity > 0f)
        {
            spots.Add(CageSpot(ringCenter, ringSize, surfaceHeight, m));
        }

        return new SceneLighting(
            new DirectionalLight(KeyDirection, new Vector3(1f, 0.97f, 0.92f), 0.45f * m.Key),
            new AmbientLight(new Vector3(0.85f, 0.88f, 0.95f), new Vector3(0.62f, 0.56f, 0.5f), 0.72f * m.Ambient),
            ImmutableArray.Create(new PointLight(lamp - new Vector3(0f, 0.1f, 0f), new Vector3(1f, 0.92f, 0.8f), 0.35f * m.Lamp, 4.5f)),
            new SpecularConfig(40f, 0.25f),
            ToneMapping.Legacy,
            spots.ToImmutable())
        {
            // 床・壁・布・木の色のテクスチャ（RoomAssets）。
            SurfaceTextures = RoomAssets.Instance.Textures,
        };
    }

    /// <summary>
    /// 檻（リング）の真上のスポット: リングの縁がちょうど円錐の縁に来る広さで真下へ絞る（暗い会場で檻だけを照らす）。
    /// 高さはリングの大きさの 0.9 倍。範囲は床まで届く 3 倍。
    /// </summary>
    public static SpotLight CageSpot(Vector3 ringCenter, float ringSize, float surfaceHeight, ArenaMood mood)
    {
        ArgumentNullException.ThrowIfNull(mood);
        var height = ringSize * 0.9f;
        var cone = 2f * MathF.Atan(ringSize * 0.62f / height) * 180f / MathF.PI;
        return new SpotLight(new Vector3(ringCenter.X, surfaceHeight + height, ringCenter.Z), -Vector3.UnitY,
            new Vector3(mood.CageSpotColor[0], mood.CageSpotColor[1], mood.CageSpotColor[2]), mood.CageSpotIntensity, height * 3f, cone);
    }

    /// <summary>リングとその周りに影を落とす範囲（主光の影）。</summary>
    public static ShadowMapConfig Shadow(Vector3 ringCenter, float ringSize, float surfaceHeight)
    {
        // ローテーブルの影が床とラグに落ちるところまで。
        var half = (ringSize * 0.5f) + 0.4f; // テーブルの縁（リングの縁から 15 cm）の 25 cm 外まで
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

    /// <summary>画面用の仕上げを、団体の雰囲気（隅の陰りと露出）で作る。<see cref="ArenaMood.Default"/> なら <see cref="ScreenFinish"/> と同じ値。</summary>
    public static PostProcessSettings ScreenFinishFor(ArenaMood mood)
    {
        ArgumentNullException.ThrowIfNull(mood);
        return Finish(mood.Vignette) with { Exposure = mood.Exposure };
    }

    /// <summary>
    /// 照明の光の中を漂う空気の粒（ほこり）と、光の筋。時刻 <paramref name="time"/> [s] だけで決まる（乱数なし・再現できる）。
    /// どれもごく淡い加算の光で、照らされた空気のように見せる。
    /// </summary>
    public static void WriteAtmosphere(Vector3 ringCenter, float ringSize, float surfaceHeight, float time, ICollection<ParticleInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var target = new Vector3(ringCenter.X, surfaceHeight, ringCenter.Z);
        var corner = ringSize * 0.75f;

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

    /// <summary>リングの外まわりの飾り（エプロンを回る光る帯）。</summary>
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
