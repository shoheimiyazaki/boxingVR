using System.Numerics;
using MiniRing.Voxels;

namespace MiniRing.Characters;

/// <summary>
/// 顔写真から、ボクセルの顔の値（<see cref="FaceSpec"/>: 部品の重み・顔の値・肌・髪・瞳の色）を作る（キャラメイクの「写真から」）。
/// <para>
/// 入力は写真の画素（RGBA）と、顔の目印の点（<see cref="PointNames"/>、画素の座標）。点は作る画面が出す: ブラウザの中の顔のランドマーク検出
/// （MediaPipe Face Landmarker、Apache-2.0）か、手で置いた点。ここは点と画素から測るだけで、外部の何にも送らない（写真は個人情報: この PC の中だけで扱う）。
/// </para>
/// <para>
/// 形は似顔絵: 顔の高さと幅に対する目・鼻・口の位置と大きさ（<see cref="FaceProportions"/>）を測り、平均からのずれを誇張して顔の値と部品へ写す
/// （<see cref="Caricature"/>）。
/// 髪型・耳・眉の形・眉の張り出しは正面の写真から測れないので、元の顔（<c>basis</c>）のまま。色は頬・額の上・目の中の画素の中央値。
/// </para>
/// 見た目だけで強さに関係しない（体格は変えない。CLAUDE.md §1.7）。
/// </summary>
public static class PhotoFace
{
    /// <summary>目印の点の名前（写真の左右: Left = 画像の左側）。</summary>
    public static readonly IReadOnlyList<string> PointNames =
    [
        "leftEyeOuter", "leftEyeInner", "leftEyeTop", "leftEyeBottom",
        "rightEyeInner", "rightEyeOuter", "rightEyeTop", "rightEyeBottom",
        "leftBrow", "rightBrow", "noseBridge", "noseTip", "noseLeft", "noseRight",
        "mouthLeft", "mouthRight", "lipTop", "lipBottom", "chin",
        "cheekLeft", "cheekRight", "jawLeft", "jawRight", "forehead",
    ];

    /// <summary>
    /// 肌との違い（<see cref="Unlike"/>: 色み（明るさで割った rgb）の距離 × <see cref="ChromaWeight"/> ＋ 明るさの比の対数の大きさ）がこれを超えたら、肌ではない（髪・ひげ）と見る。
    /// 白髪・灰色のひげは暗くないが赤みが無いので色みで、黒い髪・ひげは明るさで分かる。影の肌（明るさ 0.75 倍 → 0.29）はこれより下。
    /// </summary>
    private const float NotSkin = 0.5f;

    /// <summary>
    /// あご先の肌との違いがこれを超えたら無精ひげ・<see cref="FullBeard"/> を超えたら短いひげ。生成の顔写真 4 枚（2026-11-24）で、ひげの無いあご 0.06〜0.17、
    /// 白髪まじりの短いひげ 0.41 だったので、その間に置いた（あごの下の影は色みが肌のままで明るさだけ落ちる）。
    /// </summary>
    private const float Stubble = 0.3f;

    /// <inheritdoc cref="Stubble"/>
    private const float FullBeard = 0.55f;

    /// <summary>色みの距離の重み（肌の色みの差 0.01 が明るさの比の対数 0.1 と同じ重さ）。</summary>
    private const float ChromaWeight = 10f;

    /// <summary>髪を探す所: 額の目印（MediaPipe の 10 番は額の中ほど）から上へ、IOD のこの倍まで 0.1 ずつ。ここまで肌のままなら髪が無い（坊主・剃った頭）と見る（生え際は普通この内側。頭の上の端より下）。</summary>
    private const float HairSearchIod = 0.8f;

    /// <summary>目の中で、肌の明るさのこれ倍より暗い画素を瞳（虹彩・瞳孔）と見る（白目と肌を外す）。</summary>
    private const float IrisDarkerThanSkin = 0.8f;

    /// <summary>測った量: 顔の比（<see cref="FaceProportions"/>）と、色から読んだ量（あご先・髪の肌との違い、生え際の高さ [IOD]、髪が無ければ NaN）。画面に出して、何を読み取ったかを見せる。</summary>
    public sealed record Measurements(FaceProportions Proportions, float ChinUnlike, float HairUnlike, float HairAbove);

    /// <summary>写真から作った顔と、測った量。</summary>
    public sealed record Result(FaceSpec Face, Measurements Measured);

    /// <summary>
    /// 写真（RGBA、行は上から）と目印の点から顔を作る。<paramref name="basis"/> は測れない部位（髪型・耳・眉・張り出し）の元の顔。
    /// 点が足りない・画像の外・目が重なっているなら <see cref="InvalidDataException"/>。同じ入力からはいつも同じ出力。
    /// </summary>
    public static Result FromPhoto(int width, int height, ReadOnlySpan<byte> rgba, IReadOnlyDictionary<string, Vector2> points, FaceSpec basis,
        IReadOnlyDictionary<string, Norm>? norms = null, float gain = Caricature.DefaultGain)
    {
        norms ??= CharacterMaker.PhotoNorms;
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(basis);
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            throw new InvalidDataException($"写真の画素の数が合いません（{width} x {height} の RGBA なら {width * height * 4} バイト、来たのは {rgba.Length}）。");
        }

        foreach (var name in PointNames)
        {
            if (!points.TryGetValue(name, out var p) || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < 0f || p.Y < 0f || p.X > width || p.Y > height)
            {
                throw new InvalidDataException($"目印の点 '{name}' が無いか、写真の外です。");
            }
        }

        Vector2 P(string name) => points[name];
        var leftEye = (P("leftEyeOuter") + P("leftEyeInner")) / 2f;
        var rightEye = (P("rightEyeOuter") + P("rightEyeInner")) / 2f;
        var iod = Vector2.Distance(leftEye, rightEye);
        if (iod < 4f)
        {
            throw new InvalidDataException("両目の点が近すぎます（写真の顔が小さすぎるか、点が重なっています）。");
        }

        var chin = P("chin");

        // 色: 頬（目の下と口の端の間を外へ寄せた所）・額の上（髪）・目の中（瞳）・あご先（ひげ）。
        var image = new Sampler(width, height, rgba);
        var radius = 0.12f * iod;
        var cheekL = Vector2.Lerp(P("leftEyeBottom"), P("mouthLeft"), 0.5f) + new Vector2(-0.15f * iod, 0f);
        var cheekR = Vector2.Lerp(P("rightEyeBottom"), P("mouthRight"), 0.5f) + new Vector2(0.15f * iod, 0f);
        var skin = image.Median([(cheekL, radius), (cheekR, radius)]);
        // 髪: 額の目印から上へ、肌でない色が初めて出る所（生え際）の少し上の色。最後まで肌なら髪が無い（額の上が肌のまま頭の上まで）。
        var (hair, hairUnlike, hairAbove) = (skin, 0f, float.NaN);
        Vector2? hairline = null;
        for (var up = 0.05f; up <= HairSearchIod + 1e-4f; up += 0.05f)
        {
            var center = P("forehead") - new Vector2(0f, up * iod);
            if (center.Y < 0f)
            {
                break;
            }

            var patch = image.Median([(center, 0.1f * iod)]);
            if (Unlike(patch, skin) > NotSkin)
            {
                var above = center - new Vector2(0f, 0.15f * iod);
                hair = above.Y >= 0f ? image.Median([(above, 0.1f * iod)]) : patch;
                (hairUnlike, hairAbove, hairline) = (Unlike(patch, skin), up, center); // 中央値が髪に変わった所 ≒ 生え際（窓の真ん中）
                break;
            }

            hairUnlike = MathF.Max(hairUnlike, Unlike(patch, skin));
        }

        var irisLuma = IrisDarkerThanSkin * Luma(skin);
        var iris = image.IrisOf(irisLuma, (P("leftEyeOuter"), P("leftEyeInner"), P("leftEyeTop"), P("leftEyeBottom")),
            (P("rightEyeOuter"), P("rightEyeInner"), P("rightEyeTop"), P("rightEyeBottom"))) ?? basis.Iris ?? new Vector3(0.19f, 0.13f, 0.09f);
        var chinPatch = image.Median([(Vector2.Lerp(P("lipBottom"), chin, 0.55f), 0.14f * iod)]);
        var chinUnlike = Unlike(chinPatch, skin);

        var proportions = FaceProportions.Measure(points, hairline);
        var measured = new Measurements(proportions, chinUnlike, hairUnlike, hairAbove);
        var (v, parts) = Caricature.Apply(basis, proportions, norms, gain);
        if (float.IsNaN(hairAbove))
        {
            parts["hair"] = new Dictionary<string, float> { ["hair.3"] = 1f };
        }

        parts["beard"] = chinUnlike > FullBeard ? new Dictionary<string, float> { ["beard.4"] = 1f }
            : chinUnlike > Stubble ? new Dictionary<string, float> { ["beard.1"] = 1f }
            : new Dictionary<string, float>();

        var face = new FaceSpec(parts, v) { Skin = Clamp01(skin), Hair = Clamp01(hair), Iris = Clamp01(iris) }.Validate();
        return new Result(face, measured);
    }

    private static float Clamp(float value, float lo, float hi) => float.IsFinite(value) ? Math.Clamp(value, lo, hi) : (lo + hi) / 2f;

    private static Vector3 Clamp01(Vector3 c) => Vector3.Clamp(c, Vector3.Zero, Vector3.One);

    private static float Luma(Vector3 c) => (0.299f * c.X) + (0.587f * c.Y) + (0.114f * c.Z);

    /// <summary>肌との違い: 色み（明るさで割った rgb）の距離 × <see cref="ChromaWeight"/> ＋ |ln(明るさの比)|。0 = 同じ肌。</summary>
    private static float Unlike(Vector3 c, Vector3 skin)
    {
        static Vector3 Chroma(Vector3 v) => v / MathF.Max(1e-3f, v.X + v.Y + v.Z);
        var shade = MathF.Max(1e-3f, Luma(c)) / MathF.Max(1e-3f, Luma(skin));
        return (ChromaWeight * Vector3.Distance(Chroma(c), Chroma(skin))) + MathF.Abs(MathF.Log(shade));
    }


    /// <summary>画素を読む（範囲の外は読まない）。</summary>
    private readonly ref struct Sampler(int width, int height, ReadOnlySpan<byte> rgba)
    {
        private readonly ReadOnlySpan<byte> _rgba = rgba;

        private Vector3 At(int x, int y)
        {
            var i = ((y * width) + x) * 4;
            return new Vector3(_rgba[i], _rgba[i + 1], _rgba[i + 2]) / 255f;
        }

        /// <summary>円の中の画素の、明るさの中央値の色（明るさで並べた真ん中の画素の色: 影や光りに引っ張られない）。円が全部画像の外なら灰色。</summary>
        public Vector3 Median(IReadOnlyList<(Vector2 Center, float Radius)> circles)
        {
            var colors = new List<Vector3>();
            foreach (var (center, radius) in circles)
            {
                var r = MathF.Max(1f, radius);
                for (var y = (int)MathF.Floor(center.Y - r); y <= (int)MathF.Ceiling(center.Y + r); y++)
                {
                    for (var x = (int)MathF.Floor(center.X - r); x <= (int)MathF.Ceiling(center.X + r); x++)
                    {
                        if (x >= 0 && y >= 0 && x < width && y < height && Vector2.DistanceSquared(new Vector2(x + 0.5f, y + 0.5f), center) <= r * r)
                        {
                            colors.Add(At(x, y));
                        }
                    }
                }
            }

            return colors.Count == 0 ? new Vector3(0.5f) : MedianByLuma(colors, 0.5f);
        }

        /// <summary>
        /// 両目の中（目頭・目尻・上・下で囲んだ、真ん中の幅の半分）で、明るさが <paramref name="below"/> より暗い画素（白目と肌を外した虹彩・瞳孔）の、
        /// 明るさの中央値の色。そういう画素が無ければ null（写真の目が小さすぎる・閉じている）。
        /// </summary>
        public Vector3? IrisOf(float below, params (Vector2 Outer, Vector2 Inner, Vector2 Top, Vector2 Bottom)[] eyes)
        {
            var colors = new List<Vector3>();
            foreach (var (outer, inner, top, bottom) in eyes)
            {
                var center = (outer + inner) / 2f;
                var halfWidth = Vector2.Distance(outer, inner) * 0.25f;
                var halfHeight = MathF.Max(1f, Vector2.Distance(top, bottom) * 0.5f);
                for (var y = (int)MathF.Floor(center.Y - halfHeight); y <= (int)MathF.Ceiling(center.Y + halfHeight); y++)
                {
                    for (var x = (int)MathF.Floor(center.X - halfWidth); x <= (int)MathF.Ceiling(center.X + halfWidth); x++)
                    {
                        if (x >= 0 && y >= 0 && x < width && y < height && At(x, y) is var c && Luma(c) < below)
                        {
                            colors.Add(c);
                        }
                    }
                }
            }

            return colors.Count == 0 ? null : MedianByLuma(colors, 0.5f);
        }

        private static Vector3 MedianByLuma(List<Vector3> colors, float quantile)
        {
            // 同じ明るさの画素の並びは色の成分で決める（入力の並びに依らず同じ結果）。
            colors.Sort((a, b) =>
            {
                var c = Luma(a).CompareTo(Luma(b));
                return c != 0 ? c : a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
            });
            return colors[Math.Clamp((int)(quantile * (colors.Count - 1)), 0, colors.Count - 1)];
        }
    }
}
