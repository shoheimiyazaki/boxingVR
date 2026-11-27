using System.Numerics;

namespace MiniRing;

/// <summary>基準体格に対する寸法。太さは長さと独立、筋力は断面積から求める。</summary>
public sealed record BodyBuild(float Height = 1f, float Arms = 1f, float Legs = 1f, float Muscle = 1f, bool FourLegged = false)
{
    public static BodyBuild Standard { get; } = new();

    internal static Vector3 CoxaHalf(Vector3 pelvisHalf) => pelvisHalf * new Vector3(0.45f, 0.50f, 0.10f);

    public BodyBuild Validate()
    {
        foreach (var value in new[] { Height, Arms, Legs, Muscle })
        {
            if (!float.IsFinite(value) || value is < 0.6f or > 1.5f)
            {
                throw new ArgumentOutOfRangeException(nameof(BodyBuild), "体格は基準の0.6〜1.5倍の有限値で指定してください。");
            }
        }
        return this;
    }

    public Vector3 ScaleOf(BodyPartKind kind) => kind switch
    {
        // 四足の体の形の比は、基準の人の体の箱に対する比。2026-11-27 に基準の人の体を 175 cm の実物の比へ移したとき、四足の体の寸法が変わらないよう
        // 前の基準の箱 / 今の基準の箱を掛けて書き直した（前の比: 腕 0.87・0.92、脚 0.72・0.94・0.76、頭 1.22・0.97・1.96、骨盤 0.80・0.88・3.2、
        // 胴 0.61・1・0.95、足 0.46・1.12・0.32。四足の体は人の基準とは別の生き物で、人の基準の移し替えで大きさが変わる理由が無い）。
        BodyPartKind.UpperArm when FourLegged => new(0.862f * Height * Arms, 0.911f * Height * Muscle, 0.964f * Height * Muscle),
        BodyPartKind.Forearm when FourLegged => new(0.994f * Height * Arms, 0.87f * Height * Muscle, 0.92f * Height * Muscle),
        BodyPartKind.UpperArm or BodyPartKind.Forearm => new(Height * Arms, Height * Muscle, Height * Muscle),
        BodyPartKind.Thigh when FourLegged => new(0.566f * Height * Muscle, 0.817f * Height * Legs, 0.629f * Height * Muscle),
        BodyPartKind.Shin when FourLegged => new(0.732f * Height * Muscle, 0.794f * Height * Legs, 0.80f * Height * Muscle),
        BodyPartKind.Thigh or BodyPartKind.Shin => new(Height * Muscle, Height * Legs, Height * Muscle),
        BodyPartKind.Head when FourLegged => new(1.098f * Height, 0.819f * Height, 1.411f * Height),
        BodyPartKind.Head => new(Height),
        BodyPartKind.Pelvis when FourLegged => new(0.651f * Height * Muscle, 0.792f * Height, 2.617f * Height * Muscle),
        BodyPartKind.Chest when FourLegged => new(0.4575f * Height * Muscle, 0.746f * Height, 0.777f * Height * Muscle),
        BodyPartKind.Foot when FourLegged => new(0.552f * Height * Muscle, 1.26f * Height, 0.308f * Height * Muscle),
        // 人の足（かかと・つま先）は骨格の大きさ: 太さ（筋肉・脂肪の厚み）で長さも幅も変えない（細い人の足が短くならない。2026-11-27）。
        BodyPartKind.Foot or BodyPartKind.Toe => new(Height),
        _ => new(Height * Muscle, Height, Height * Muscle),
    };

    public float SectionRatio => Height * Height * Muscle * Muscle;

    /// <summary>
    /// 見えている大きさから見積もる体の重さの比 H³ × M²（基準体格で 1）。部位の質量は体積（<see cref="ScaleOf"/> の 3 辺の積）に比例させて
    /// いるので、胴・頭は H³M² / H³、腕・脚は長さの比が 1 つ掛かる。ボットが相手の重さを「見た目の大きさから」見積もるときの 1 つの式で、他で書き直さない。
    /// </summary>
    public float MassRatio => Height * Height * Height * Muscle * Muscle;

    /// <summary>脚の関節トルクの比: 断面積の比 × 脚の長さの比（てこの腕）。基準体格で 1。</summary>
    public float LegTorqueRatio => SectionRatio * Height * Legs;

    /// <summary>脚の対の数（人は前の 1 対、四足は前後の 2 対。後ろの対は前の対と同じ寸法の脚: <c>Fighter.AddRearRig</c>）。</summary>
    public int LegPairs => FourLegged ? 2 : 1;
}
