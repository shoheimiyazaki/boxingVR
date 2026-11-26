using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 生きた組織の材質の、働きの値（2026-11-25、ユーザーの指定「エイリアンの筋肉・心臓・肺は人間と違う性能を持ってよい。同じ物理で専用の材質で身体を作る」）。
/// 体を作る材質の表（<see cref="BoxerBody.PaletteFor"/>）の筋肉・心臓・肺の材質から引き、今と同じ式（<see cref="FighterTuning"/>）の値に掛ける倍率。人の組織はどれも 1。
/// 種族で分けない: 体の材質が何かだけで決まる。
/// </summary>
/// <param name="MuscleStress">筋の断面あたりの力（人の筋肉 = 1）。関節のサーボ・拳を押す力・手首のトルク（<see cref="FighterTuning.TorqueScale"/>）に掛かる。</param>
/// <param name="OxygenStore">筋肉 1 kg あたりの酸素の蓄えと、慢性の疲れに耐える量（<see cref="FighterTuning.OxygenStorePerKg"/>・<see cref="FighterTuning.StaminaEndurance"/>）。</param>
/// <param name="VesselFlow">筋肉 1 kg あたりの血管の太さ（<see cref="FighterTuning.VesselPowerPerKg"/>）。</param>
/// <param name="HeartPower">心臓が送れる酸素の速さ（<see cref="FighterTuning.HeartPower"/>）。</param>
/// <param name="LungPower">肺が取り込める酸素の速さ（<see cref="FighterTuning.LungPower"/>）。</param>
/// <param name="MuscleSpeed">筋の縮む速さ（腕のサーボの角速度の上限 <see cref="FighterTuning.ArmMuscleSpeed"/>）。</param>
public sealed record TissuePhysiology(float MuscleStress = 1f, float OxygenStore = 1f, float VesselFlow = 1f, float HeartPower = 1f, float LungPower = 1f, float MuscleSpeed = 1f)
{
    public static TissuePhysiology Human { get; } = new();

    /// <summary>
    /// エイリアン（ガルヴァ）の筋肉・心臓・肺の材質の値。<b>仮の極端な値</b>（docs/DEBT.md「エイリアンの材質の値は仮」）: 心肺と酸素は律速にならない大きさ
    /// （1000 倍。無限は数値が壊れる）、筋の断面あたりの力は人の 4 倍、筋の縮む速さ（腕の角速度の上限）は人の 3 倍（huntlab で、これ以上は関節・接触が先に律速になるかを測って決める）。
    /// </summary>
    public static readonly (string Muscle, string Heart, string Lung) AlienNames = ("alien muscle", "alien heart", "alien lung");

    private static readonly Dictionary<string, (float Stress, float Store, float Vessel, float Speed)> Muscles = new(StringComparer.Ordinal)
    {
        ["muscle"] = (1f, 1f, 1f, 1f),
        [AlienNames.Muscle] = (4f, 1000f, 1000f, 3f),
    };

    private static readonly Dictionary<string, float> Hearts = new(StringComparer.Ordinal) { ["heart"] = 1f, [AlienNames.Heart] = 1000f };

    private static readonly Dictionary<string, float> Lungs = new(StringComparer.Ordinal) { ["lung"] = 1f, [AlienNames.Lung] = 1000f };

    /// <summary>材質の表の筋肉・心臓・肺の材質から。表に無い名前は人の値。</summary>
    public static TissuePhysiology Of(VoxelPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var muscle = Muscles.TryGetValue(palette[BoxerAnatomy.Muscle].Name, out var m) ? m : (Stress: 1f, Store: 1f, Vessel: 1f, Speed: 1f);
        return new TissuePhysiology(muscle.Stress, muscle.Store, muscle.Vessel,
            Hearts.GetValueOrDefault(palette[BoxerAnatomy.Heart].Name, 1f), Lungs.GetValueOrDefault(palette[BoxerAnatomy.Lung].Name, 1f), muscle.Speed);
    }
}
