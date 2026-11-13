namespace MiniRing.Audio;

/// <summary>音専用の材料値。物理の剛性・破壊強度とは独立した振動の減衰。</summary>
public readonly record struct AcousticMaterial(float FreeDecay, float SupportLoss)
{
    public static AcousticMaterial Steel { get; } = new(0.13f, 8f);
}

/// <summary>形状・質量・支持から求める共鳴。武器名による分岐は持たない。</summary>
public readonly record struct ContactResonance(float DecayScale)
{
    private const float ReferenceDecaySeconds = 0.13f;

    public static ContactResonance Of(float length, float width, float thickness, float mass, bool supported)
    {
        if (!float.IsFinite(length + width + thickness + mass) || length <= 0 || width <= 0 || thickness <= 0 || mass <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "寸法と質量は有限の正数が必要です。");
        }
        // 33 cm / 120 g の細長い刃を基準に、長さと慣性で尾を延ばす。
        // 支持損失は断面/長さに比例。手に密着した小塊は、細い柄で支持する刃より強く減衰する。
        var material = AcousticMaterial.Steel;
        var coupling = supported ? MathF.Sqrt(width * thickness) / length : 0f;
        var decay = material.FreeDecay * (length / 0.33f) * MathF.Sqrt(mass / 0.12f) / (1f + (material.SupportLoss * coupling));
        return new ContactResonance(Math.Clamp(decay / ReferenceDecaySeconds, 0.015f, 1.5f));
    }
}
