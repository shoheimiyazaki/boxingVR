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
/// 測る量はどれも両目の中心の間の距離（<c>IOD</c>）を単位にした比で、平均的な顔の比（下の <c>Ref*</c>、成人の顔の計測の平均の概数）からのずれを顔の値へ写す
/// （連続の写し。範囲は <see cref="FaceParts.Validate"/> の幅に収める）。部品（目・あご・口・鼻）は、測った比にいちばん近い部品の重みにする。
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

    // 平均的な顔の比（IOD = 両目の中心の間 ≒ 62 mm を 1 とする。成人の顔の計測の平均の概数）。
    private const float RefCheekWidth = 2.25f;   // 頬骨の高さの顔の幅 ≒ 140 mm
    private const float RefJawShare = 0.78f;     // あごの角の幅 / 頬の幅（下顎角幅 ≒ 頬骨弓幅の 0.75〜0.8）
    private const float RefEyeWidth = 0.5f;      // 目の幅 ≒ 30 mm
    private const float RefInnerGap = 1.05f;     // 目頭の間 / 目の幅（≒ 目 1 つぶん）
    private const float RefEyeAspect = 0.32f;    // 目の高さ / 幅
    private const float RefBrowAbove = 0.33f;    // 目の中心から眉まで ≒ 20 mm
    private const float RefNoseWidth = 0.58f;    // 小鼻の幅 ≒ 36 mm
    private const float RefNoseLength = 0.75f;   // 鼻根から鼻先まで（縦）≒ 46 mm
    private const float RefMouthWidth = 0.82f;   // 口の幅 ≒ 50 mm
    private const float RefLipShare = 0.33f;     // 唇の厚み（上唇の上〜下唇の下）/ 口の幅
    private const float RefEyeToChin = 1.95f;    // 目の高さからあご先まで ≒ 120 mm
    private const float RefMouthShare = 0.35f;   // 鼻先〜口の中心 / 鼻先〜あご先
    private const float RefForehead = 1.05f;     // 目の高さから生え際まで ≒ 65 mm

    /// <summary>頭の箱の正規化座標 1 の実寸 / IOD（顔の層の頭の高さの半分 ≒ 115 mm = 1.85 IOD）。位置のずれの値を IOD の比から写す。</summary>
    private const float NormalizedPerIod = 1f / 1.85f;

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

    /// <summary>測った量（IOD を 1 とする比）。画面に出して、何を読み取ったかを見せる。</summary>
    public sealed record Measurements(float CheekWidth, float JawShare, float EyeWidth, float InnerGap, float EyeAspect, float EyeTilt, float BrowAbove,
        float NoseWidth, float NoseLength, float MouthWidth, float LipShare, float EyeToChin, float MouthShare, float Forehead, float ChinUnlike, float HairUnlike, float HairAbove);

    /// <summary>写真から作った顔と、測った量。</summary>
    public sealed record Result(FaceSpec Face, Measurements Measured);

    /// <summary>
    /// 写真（RGBA、行は上から）と目印の点から顔を作る。<paramref name="basis"/> は測れない部位（髪型・耳・眉・張り出し）の元の顔。
    /// 点が足りない・画像の外・目が重なっているなら <see cref="InvalidDataException"/>。同じ入力からはいつも同じ出力。
    /// </summary>
    public static Result FromPhoto(int width, int height, ReadOnlySpan<byte> rgba, IReadOnlyDictionary<string, Vector2> points, FaceSpec basis)
    {
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

        float Dist(string a, string b) => Vector2.Distance(P(a), P(b)) / iod;
        var eyeY = (leftEye.Y + rightEye.Y) / 2f;
        var cheekWidth = Dist("cheekLeft", "cheekRight");
        var eyeWidth = (Dist("leftEyeOuter", "leftEyeInner") + Dist("rightEyeOuter", "rightEyeInner")) / 2f;
        var eyeHeight = (Dist("leftEyeTop", "leftEyeBottom") + Dist("rightEyeTop", "rightEyeBottom")) / 2f;
        var tiltLeft = MathF.Atan2(P("leftEyeInner").Y - P("leftEyeOuter").Y, MathF.Abs(P("leftEyeInner").X - P("leftEyeOuter").X));
        var tiltRight = MathF.Atan2(P("rightEyeInner").Y - P("rightEyeOuter").Y, MathF.Abs(P("rightEyeInner").X - P("rightEyeOuter").X));
        var noseTip = P("noseTip");
        var chin = P("chin");
        var mouthY = (P("lipTop").Y + P("lipBottom").Y) / 2f;
        var mouthWidth = Dist("mouthLeft", "mouthRight");

        // 色: 頬（目の下と口の端の間を外へ寄せた所）・額の上（髪）・目の中（瞳）・あご先（ひげ）。
        var image = new Sampler(width, height, rgba);
        var radius = 0.12f * iod;
        var cheekL = Vector2.Lerp(P("leftEyeBottom"), P("mouthLeft"), 0.5f) + new Vector2(-0.15f * iod, 0f);
        var cheekR = Vector2.Lerp(P("rightEyeBottom"), P("mouthRight"), 0.5f) + new Vector2(0.15f * iod, 0f);
        var skin = image.Median([(cheekL, radius), (cheekR, radius)]);
        // 髪: 額の目印から上へ、肌でない色が初めて出る所（生え際）の少し上の色。最後まで肌なら髪が無い（額の上が肌のまま頭の上まで）。
        var (hair, hairUnlike, hairAbove) = (skin, 0f, float.NaN);
        for (var up = 0.1f; up <= HairSearchIod + 1e-4f; up += 0.1f)
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
                (hairUnlike, hairAbove) = (Unlike(patch, skin), up);
                break;
            }

            hairUnlike = MathF.Max(hairUnlike, Unlike(patch, skin));
        }

        var irisLuma = IrisDarkerThanSkin * Luma(skin);
        var iris = image.IrisOf(irisLuma, (P("leftEyeOuter"), P("leftEyeInner"), P("leftEyeTop"), P("leftEyeBottom")),
            (P("rightEyeOuter"), P("rightEyeInner"), P("rightEyeTop"), P("rightEyeBottom"))) ?? basis.Iris ?? new Vector3(0.19f, 0.13f, 0.09f);
        var chinPatch = image.Median([(Vector2.Lerp(P("lipBottom"), chin, 0.55f), 0.14f * iod)]);
        var chinUnlike = Unlike(chinPatch, skin);

        var measured = new Measurements(
            CheekWidth: cheekWidth,
            JawShare: Dist("jawLeft", "jawRight") / MathF.Max(1e-3f, cheekWidth),
            EyeWidth: eyeWidth,
            InnerGap: Dist("leftEyeInner", "rightEyeInner") / MathF.Max(1e-3f, eyeWidth),
            EyeAspect: eyeHeight / MathF.Max(1e-3f, eyeWidth),
            EyeTilt: (tiltLeft + tiltRight) / 2f,
            BrowAbove: (eyeY - ((P("leftBrow").Y + P("rightBrow").Y) / 2f)) / iod,
            NoseWidth: Dist("noseLeft", "noseRight"),
            NoseLength: (noseTip.Y - P("noseBridge").Y) / iod,
            MouthWidth: mouthWidth,
            LipShare: (P("lipBottom").Y - P("lipTop").Y) / iod / MathF.Max(1e-3f, mouthWidth),
            EyeToChin: (chin.Y - eyeY) / iod,
            MouthShare: (mouthY - noseTip.Y) / MathF.Max(1e-3f, chin.Y - noseTip.Y),
            Forehead: (eyeY - P("forehead").Y) / iod,
            ChinUnlike: chinUnlike,
            HairUnlike: hairUnlike,
            HairAbove: hairAbove);

        var v = basis.Values with
        {
            FaceWidth = Clamp(1f + (0.8f * ((measured.CheekWidth / RefCheekWidth) - 1f)), 0.85f, 1.2f),
            JawWidth = Clamp(measured.JawShare / RefJawShare, 0.8f, 1.25f),
            Cheekbones = Clamp(1.3f + (3f * (RefJawShare - measured.JawShare)), 0.5f, 1.8f),
            EyeSize = Clamp(measured.EyeWidth / RefEyeWidth, 0.7f, 1.35f),
            EyeSpacing = Clamp(measured.InnerGap / RefInnerGap, 0.75f, 1.25f),
            EyeTilt = Clamp(measured.EyeTilt, -0.45f, 0.45f),
            EyeHeight = Clamp((measured.EyeToChin - RefEyeToChin) * 0.5f * NormalizedPerIod, -0.1f, 0.1f),
            BrowHeight = Clamp((measured.BrowAbove - RefBrowAbove) * NormalizedPerIod, -0.08f, 0.08f),
            NoseWidth = Clamp(measured.NoseWidth / RefNoseWidth, 0.7f, 1.5f),
            NoseLength = Clamp(measured.NoseLength / RefNoseLength, 0.7f, 1.4f),
            MouthWidth = Clamp(measured.MouthWidth / RefMouthWidth, 0.6f, 1.4f),
            MouthHeight = Clamp((RefMouthShare - measured.MouthShare) * 0.4f, -0.07f, 0.07f),
            Lips = Clamp(measured.LipShare / RefLipShare, 0.5f, 1.8f),
            Hairline = Clamp((measured.Forehead - RefForehead) * 0.5f * NormalizedPerIod, -0.12f, 0.12f),
        };

        var parts = basis.Parts.ToDictionary(p => p.Key, p => p.Value);
        parts["eyes"] = Nearest(measured.EyeAspect, ("eyes.1", 0.22f), ("eyes.6", 0.27f), ("eyes.3", 0.32f), ("eyes.5", 0.4f));
        parts["mouth"] = Blend(measured.LipShare, ("mouth.7", 0.22f), ("mouth.8", 0.33f), ("mouth.9", 0.44f), ("mouth.10", 0.55f));
        parts["nose"] = Blend(measured.NoseWidth, ("nose.4", 0.48f), ("nose.1", 0.58f), ("nose.2", 0.7f));
        parts["jaw"] = Blend(measured.JawShare, ("jaw.1", 0.66f), ("jaw.10", 0.76f), ("jaw.9", 0.86f));
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

    /// <summary>いちばん近い 1 部品（目は 1 部品: 名簿の決まり）。</summary>
    private static Dictionary<string, float> Nearest(float value, params (string Id, float At)[] parts)
        => new Dictionary<string, float> { [parts.MinBy(p => MathF.Abs(p.At - value)).Id] = 1f };

    /// <summary>並んだ 2 つの部品の間を直線で混ぜる（端の外は端の部品だけ）。</summary>
    private static Dictionary<string, float> Blend(float value, params (string Id, float At)[] parts)
    {
        if (value <= parts[0].At)
        {
            return new Dictionary<string, float> { [parts[0].Id] = 1f };
        }

        for (var i = 1; i < parts.Length; i++)
        {
            if (value <= parts[i].At)
            {
                var t = (value - parts[i - 1].At) / (parts[i].At - parts[i - 1].At);
                var w = MathF.Round(t, 2);
                return w <= 0f ? new Dictionary<string, float> { [parts[i - 1].Id] = 1f }
                    : w >= 1f ? new Dictionary<string, float> { [parts[i].Id] = 1f }
                    : new Dictionary<string, float> { [parts[i - 1].Id] = 1f - w, [parts[i].Id] = w };
            }
        }

        return new Dictionary<string, float> { [parts[^1].Id] = 1f };
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
