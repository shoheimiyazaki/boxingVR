using System.Numerics;
using MiniRing.Voxels;

namespace MiniRing.Characters;

/// <summary>
/// 正面の顔の比（写真の目印から測る）。長さは顔の幅 W（頬の端から端）か顔の高さ H（額の目印からあご先）に対する比、位置は額の目印から下へ H に対する比。
/// 目の線が水平になるように回した枠で測る（少し傾いた写真でも同じ値）。名前はキャラメイクの表 <c>maker.json</c> の <c>photoNorms</c> の鍵と同じ。
/// </summary>
/// <param name="Aspect">顔の高さ / 幅（H / W）。</param>
/// <param name="JawWidth">あごの角の幅 / W。</param>
/// <param name="EyeSpan">両目の中心の間 / W。</param>
/// <param name="EyeWidth">目の幅（目頭〜目尻、左右の平均）/ W。</param>
/// <param name="EyeOpen">目の高さ / 目の幅（開き方）。</param>
/// <param name="EyeRow">目の中心の高さの位置（額から下へ / H）。</param>
/// <param name="BrowRow">眉の位置（/ H）。</param>
/// <param name="NoseRow">鼻先の位置（/ H）。</param>
/// <param name="NoseWidth">小鼻の幅 / W。</param>
/// <param name="MouthRow">口の中心の位置（/ H）。</param>
/// <param name="MouthWidth">口の幅 / W。</param>
/// <param name="LipThickness">唇の厚さ（上唇の上〜下唇の下）/ H。</param>
public sealed record FaceProportions(float Aspect, float JawWidth, float EyeSpan, float EyeWidth, float EyeOpen, float EyeRow, float BrowRow,
    float NoseRow, float NoseWidth, float MouthRow, float MouthWidth, float LipThickness)
{
    /// <summary>おでこの高さ（生え際から目の線まで / H）。生え際は髪の色で見つける（<see cref="PhotoFace"/>）ので、髪が無い・見つからなければ NaN（その写真では使わない）。</summary>
    public float Forehead { get; init; } = float.NaN;

    /// <summary>鍵の名前（<c>photoNorms</c>）と値。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<(string Key, float Value)> Entries =>
    [
        ("aspect", Aspect), ("jawWidth", JawWidth), ("eyeSpan", EyeSpan), ("eyeWidth", EyeWidth), ("eyeOpen", EyeOpen), ("eyeRow", EyeRow),
        ("browRow", BrowRow), ("noseRow", NoseRow), ("noseWidth", NoseWidth), ("mouthRow", MouthRow), ("mouthWidth", MouthWidth), ("lipThickness", LipThickness),
        ("forehead", Forehead),
    ];

    /// <summary>
    /// 目印の点（<see cref="PhotoFace.PointNames"/>、画素の座標）から測る。目の線の向きに合わせて回し、W・H が小さすぎれば <see cref="InvalidDataException"/>。
    /// <paramref name="hairline"/> は生え際の点（画素の座標。無ければおでこの高さは NaN）。
    /// </summary>
    public static FaceProportions Measure(IReadOnlyDictionary<string, Vector2> points, Vector2? hairline = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        Vector2 Raw(string name) => points.TryGetValue(name, out var p) ? p : throw new InvalidDataException($"目印の点 '{name}' がありません。");
        var leftEye = (Raw("leftEyeOuter") + Raw("leftEyeInner")) / 2f;
        var rightEye = (Raw("rightEyeOuter") + Raw("rightEyeInner")) / 2f;
        var axis = rightEye - leftEye;
        if (axis.Length() < 4f)
        {
            throw new InvalidDataException("両目の点が近すぎます（写真の顔が小さすぎるか、点が重なっています）。");
        }

        // 目の線を x 軸に、下を +y にする枠（画像の座標は y が下向き）。
        var ux = Vector2.Normalize(axis);
        var uy = new Vector2(-ux.Y, ux.X);
        var origin = (leftEye + rightEye) / 2f;
        Vector2 P(string name)
        {
            var d = Raw(name) - origin;
            return new Vector2(Vector2.Dot(d, ux), Vector2.Dot(d, uy));
        }

        float X(string a, string b) => MathF.Abs(P(b).X - P(a).X);
        var w = X("cheekLeft", "cheekRight");
        var top = P("forehead").Y;
        var h = P("chin").Y - top;
        if (w < 8f || h < 8f)
        {
            throw new InvalidDataException("顔の幅か高さが小さすぎます（頬・額・あご先の点を確かめてください）。");
        }

        float Row(float y) => (y - top) / h;
        var eyeWidth = (X("leftEyeOuter", "leftEyeInner") + X("rightEyeInner", "rightEyeOuter")) / 2f;
        var eyeHeight = ((P("leftEyeBottom").Y - P("leftEyeTop").Y) + (P("rightEyeBottom").Y - P("rightEyeTop").Y)) / 2f;
        return new FaceProportions(
            Aspect: h / w,
            JawWidth: X("jawLeft", "jawRight") / w,
            EyeSpan: axis.Length() / w,
            EyeWidth: eyeWidth / w,
            EyeOpen: eyeHeight / MathF.Max(1e-3f, eyeWidth),
            EyeRow: Row(0f),
            BrowRow: Row((P("leftBrow").Y + P("rightBrow").Y) / 2f),
            NoseRow: Row(P("noseTip").Y),
            NoseWidth: X("noseLeft", "noseRight") / w,
            MouthRow: Row((P("lipTop").Y + P("lipBottom").Y) / 2f),
            MouthWidth: X("mouthLeft", "mouthRight") / w,
            LipThickness: (P("lipBottom").Y - P("lipTop").Y) / h)
        {
            Forehead = hairline is { } line ? -Vector2.Dot(line - origin, uy) / h : float.NaN,
        };
    }
}

/// <summary>平均とばらつき（標準偏差）の 1 組。</summary>
public readonly record struct Norm(float Mean, float Sd)
{
    /// <summary>平均からのずれを <paramref name="gain"/> 倍にした値（似顔絵の誇張）。</summary>
    public float Exaggerate(float value, float gain) => Mean + (gain * (value - Mean));

    /// <summary>ずれをばらつきで割った値（z）。</summary>
    public float Z(float value) => Sd > 0f ? (value - Mean) / Sd : 0f;
}

/// <summary>
/// 似顔絵（キャラメイクの「写真から」）: 測った顔の比（<see cref="FaceProportions"/>）の平均からのずれを <c>gain</c> 倍に誇張して、ボクセルの顔の値
/// （<see cref="FaceParts"/>）と部品の選びへ写す。平均の顔（ずれ 0）は既定の顔そのもの（値はすべて既定）になる。
/// <para>
/// 写し方: 長さの比は既定の値への倍率（例: 口の幅 = 誇張した口の幅の比 / 平均）。位置は顔の層の格子（20 × 28、目 13.5 行・口 21.5 行・あご先 26 行）の
/// 行のずれにして、位置の値（<see cref="FaceParts.EyeHeight"/> など、頭の高さの半分 = 1）に直す。比 1 あたりの行数は、平均の目と口の位置の差が
/// 格子の目と口の差（8 行）になるように決める（<see cref="RowsPerFace"/>）。顔の縦横の比は顔の幅の値で表す（格子の高さはあご先までの行が固定に近い）。
/// 目の絵は引き伸ばさない（ユーザーの指示、2026-11-20）ので、目の大きさ・開き方は目の部品の選びで表す。
/// </para>
/// 平均とばらつきは、同じ目印の取り方（MediaPipe）で生成の顔（実在しない人）40 枚を測った値（<c>maker.json</c> の <c>photoNorms</c>、docs/CHARACTERS.md §12）。
/// </summary>
public static class Caricature
{
    /// <summary>既定の誇張の強さ（平均からのずれの倍率）。</summary>
    public const float DefaultGain = 2.5f;

    /// <summary>既定の鼻の長さ（顔の層の格子の行。鼻根の行から鼻先まで）。鼻の長さの値は、鼻先の行のずれをこれで割った倍率。</summary>
    private const float NoseRows = 5f;

    /// <summary>顔の層の格子の高さの半分（位置の値 1 の行数）。</summary>
    private const float HalfHeight = FacePartLibrary.RefHeight / 2f;

    /// <summary>顔の高さの比 1 あたりの格子の行数（平均の目と口の位置の差が、格子の目と口の行の差になる）。</summary>
    public static float RowsPerFace(IReadOnlyDictionary<string, Norm> norms, FaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(norms);
        ArgumentNullException.ThrowIfNull(layout);
        return (layout.Mouth.Y - layout.EyeLeftImage.Y) / MathF.Max(1e-3f, norms["mouthRow"].Mean - norms["eyeRow"].Mean);
    }

    /// <summary>
    /// 誇張した顔: <paramref name="basis"/> の値と部品のうち、測れる物（形の値・目・口・鼻・あごの部品）を、測った比の誇張から決める。
    /// 値は <see cref="FaceParts.Validate"/> の幅に収める（収まらない誇張は端で止まる）。
    /// </summary>
    public static (FaceParts Values, Dictionary<string, IReadOnlyDictionary<string, float>> Parts) Apply(
        FaceSpec basis, FaceProportions measured, IReadOnlyDictionary<string, Norm> norms, float gain, FacePartLibrary? library = null)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(norms);
        if (!float.IsFinite(gain) || gain is < 0f or > 6f)
        {
            throw new InvalidDataException($"似顔絵の強さ {gain} は 0〜6 にしてください。");
        }

        library ??= FacePartLibrary.Default;
        var rows = RowsPerFace(norms, library.Layout);
        var m = measured.Entries.ToDictionary(e => e.Key, e => e.Value);
        float Ex(string key) => norms[key].Exaggerate(m[key], gain);
        float Ratio(string key) => Ex(key) / norms[key].Mean;
        float Shift(string key) => (Ex(key) - norms[key].Mean) * rows; // 下向きの行のずれ
        float Zg(string key) => gain * norms[key].Z(m[key]);

        var eyeShift = Shift("eyeRow");
        var v = basis.Values with
        {
            FaceWidth = Clamp(1f / Ratio("aspect"), 0.8f, 1.25f),
            JawWidth = Clamp(Ratio("jawWidth"), 0.7f, 1.35f),
            EyeSpacing = Clamp(Ratio("eyeSpan"), 0.7f, 1.4f),
            EyeSize = Clamp(Ratio("eyeWidth"), 0.7f, 1.35f),
            EyeHeight = Clamp(-eyeShift / HalfHeight, -0.2f, 0.2f),
            BrowHeight = Clamp(-(Shift("browRow") - eyeShift) / HalfHeight, -0.12f, 0.12f),
            NoseLength = Clamp(1f + (Shift("noseRow") / NoseRows), 0.5f, 1.8f),
            NoseWidth = Clamp(Ratio("noseWidth"), 0.6f, 1.7f),
            MouthHeight = Clamp(-Shift("mouthRow") / HalfHeight, -0.14f, 0.14f),
            MouthWidth = Clamp(Ratio("mouthWidth"), 0.5f, 1.6f),
            Lips = Clamp(Ratio("lipThickness"), 0.4f, 2.2f),
        };
        if (float.IsFinite(m["forehead"]))
        {
            // 生え際の行のずれ（下向き）= 目のずれ − おでこの高さのずれ。生え際の値は正で髪が下がる（おでこが狭い）。
            // 上げる（おでこが広い）だけ。下げると（正）髪の絵が頭頂で途切れて地肌の点が出る（0.03 でも出た）。組み立てを直すと名簿の顔の当たりの形が
            // 変わるので、ここで止める（狭いおでこは既定の生え際のまま。docs/DEBT.md「キャラクター」）。
            v = v with { Hairline = Clamp((eyeShift - Shift("forehead")) / HalfHeight, -0.25f, 0f) };
        }

        var parts = basis.Parts.ToDictionary(p => p.Key, p => p.Value);
        // 部品の選び（z = 誇張したずれ / ばらつき）: 細い目 → 一重、開いた目 → 大きい目。唇・鼻・あごは並んだ 2 つの部品を混ぜる。
        var eyeZ = Zg("eyeOpen") + (0.5f * Zg("eyeWidth"));
        parts["eyes"] = One(eyeZ < -1.2f ? "eyes.1" : eyeZ < -0.4f ? "eyes.6" : eyeZ < 1f ? "eyes.3" : "eyes.5");
        parts["mouth"] = Blend(Zg("lipThickness"), ("mouth.7", -1.5f), ("mouth.8", 0f), ("mouth.9", 1.5f), ("mouth.10", 3f));
        parts["nose"] = Blend(Zg("noseWidth"), ("nose.4", -1.5f), ("nose.1", 0f), ("nose.2", 1.5f));
        parts["jaw"] = Blend(Zg("jawWidth"), ("jaw.1", -1.5f), ("jaw.10", 0f), ("jaw.9", 1.5f));
        return (v.Validate(), parts);
    }

    private static float Clamp(float value, float lo, float hi) => float.IsFinite(value) ? Math.Clamp(value, lo, hi) : 1f;

    private static Dictionary<string, float> One(string id) => new() { [id] = 1f };

    /// <summary>並んだ 2 つの部品の間を直線で混ぜる（端の外は端の部品だけ）。</summary>
    private static Dictionary<string, float> Blend(float value, params (string Id, float At)[] parts)
    {
        if (!float.IsFinite(value) || value <= parts[0].At)
        {
            return One(float.IsFinite(value) ? parts[0].Id : parts[parts.Length / 2].Id);
        }

        for (var i = 1; i < parts.Length; i++)
        {
            if (value <= parts[i].At)
            {
                var w = MathF.Round((value - parts[i - 1].At) / (parts[i].At - parts[i - 1].At), 2);
                return w <= 0f ? One(parts[i - 1].Id) : w >= 1f ? One(parts[i].Id) : new() { [parts[i - 1].Id] = 1f - w, [parts[i].Id] = w };
            }
        }

        return One(parts[^1].Id);
    }
}
