using System.Numerics;

namespace MiniRing;

/// <summary>基準体格に対する寸法。太さは長さと独立、筋力は断面積から求める。</summary>
public sealed record BodyBuild(float Height = 1f, float Arms = 1f, float Legs = 1f, float Muscle = 1f)
{
    public static BodyBuild Standard { get; } = new();

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
        BodyPartKind.UpperArm or BodyPartKind.Forearm => new(Height * Arms, Height * Muscle, Height * Muscle),
        BodyPartKind.Thigh or BodyPartKind.Shin => new(Height * Muscle, Height * Legs, Height * Muscle),
        BodyPartKind.Head => new(Height),
        _ => new(Height * Muscle, Height, Height * Muscle),
    };

    public float SectionRatio => Height * Height * Muscle * Muscle;

    /// <summary>脚の関節トルクの比: 断面積の比 × 脚の長さの比（てこの腕）。基準体格で 1。</summary>
    public float LegTorqueRatio => SectionRatio * Height * Legs;
}
