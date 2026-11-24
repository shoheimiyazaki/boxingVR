namespace MiniRing.Characters;

/// <summary>
/// 身長 [cm]・体重 [kg]・腕と脚の長さの比から、体格（<see cref="BodyBuild"/>）を解く（キャラメイクの体の段）。強さは体から物理で出る（CLAUDE.md §1.7）ので、
/// 能力値の倍率は持たない: 決めるのは寸法だけ。
/// <para>
/// 体重は名簿の体重の式（<see cref="CharacterProfile.WeightOf"/>: 72 kg × 身長の比² × 太さの比。計量・階級・プロフィールが使う一つの式）を
/// 太さ（<see cref="BodyBuild.Muscle"/>）について解いて合わせる。身長は <see cref="CharacterProfile.HeightOf"/> を身長の比について解く。
/// どちらも、解いた体格から式で出し直すと指定の値にちょうど戻る（<see cref="Solve"/> が確かめる）。
/// </para>
/// <para>
/// 物理の体の質量（部位の体積に比例: <see cref="FighterTuning.Masses"/>）は別の量として <see cref="PhysicalMassKg"/> が出す。名簿の体重の式とは指数が違う
/// （H²M と H³M²。§8 の近道「キャラクター」）ので、ツールは両方を並べて見せる。
/// </para>
/// </summary>
public static class BodySizing
{
    /// <summary>体格の比の下限・上限（<see cref="BodyBuild.Validate"/> と同じ）。</summary>
    public const float MinRatio = 0.6f;

    /// <inheritdoc cref="MinRatio"/>
    public const float MaxRatio = 1.5f;

    /// <summary>身長・体重・腕・脚から体格を解く。範囲の外（体格の比が 0.6〜1.5 に入らない）なら、どの値が外れたかを書いた例外。</summary>
    public static BodyBuild Solve(int heightCm, int weightKg, float arms = 1f, float legs = 1f)
    {
        var height = HeightRatio(heightCm);
        var muscle = MathF.Round(weightKg / (CharacterProfile.BaseWeightKg * height * height), 4, MidpointRounding.AwayFromZero);
        if (!float.IsFinite(muscle) || muscle is < MinRatio or > MaxRatio)
        {
            var (lo, hi) = WeightRange(heightCm);
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, $"身長 {heightCm} cm で作れる体重は {lo}〜{hi} kg です（筋肉の太さが基準の {MinRatio}〜{MaxRatio} 倍）。");
        }

        foreach (var (value, name, param) in new[] { (arms, "腕の長さ", nameof(arms)), (legs, "脚の長さ", nameof(legs)) })
        {
            if (!float.IsFinite(value) || value is < MinRatio or > MaxRatio)
            {
                throw new ArgumentOutOfRangeException(param, value, $"{name}は基準の {MinRatio}〜{MaxRatio} 倍にしてください。");
            }
        }

        var build = new BodyBuild(height, MathF.Round(arms, 4), MathF.Round(legs, 4), muscle).Validate();
        if (CharacterProfile.HeightOf(build) != heightCm || CharacterProfile.WeightOf(build) != weightKg)
        {
            throw new InvalidOperationException($"体格を解いたが、式で出し直すと {CharacterProfile.HeightOf(build)} cm・{CharacterProfile.WeightOf(build)} kg になった（{heightCm} cm・{weightKg} kg のはず）。");
        }

        return build;
    }

    /// <summary>身長 [cm] の比（<see cref="CharacterProfile.HeightOf"/> の逆）。範囲の外は例外。</summary>
    public static float HeightRatio(int heightCm)
    {
        var height = MathF.Round(heightCm / CharacterProfile.BaseHeightCm, 4, MidpointRounding.AwayFromZero);
        if (height is < MinRatio or > MaxRatio)
        {
            var lo = (int)MathF.Ceiling(CharacterProfile.BaseHeightCm * MinRatio);
            var hi = (int)MathF.Floor(CharacterProfile.BaseHeightCm * MaxRatio);
            throw new ArgumentOutOfRangeException(nameof(heightCm), heightCm, $"身長は {lo}〜{hi} cm にしてください。");
        }

        return height;
    }

    /// <summary>この身長で作れる体重の範囲 [kg]（筋肉の太さが 0.6〜1.5 倍）。</summary>
    public static (int Min, int Max) WeightRange(int heightCm)
    {
        var h = HeightRatio(heightCm);
        var w = CharacterProfile.BaseWeightKg * h * h;
        return ((int)MathF.Ceiling(w * MinRatio), (int)MathF.Floor(w * MaxRatio));
    }

    /// <summary>体型（太さの比）とこの身長での体重 [kg]（<see cref="CharacterProfile.WeightOf"/>）。体型を選んだときの体重の初めの値。</summary>
    public static int WeightFor(int heightCm, float muscle)
        => CharacterProfile.WeightOf(new BodyBuild(HeightRatio(heightCm), 1f, 1f, muscle).Validate());

    /// <summary>
    /// 物理の体の質量 [kg]: 部位ごとの質量（<see cref="FighterTuning.Masses"/>、体積に比例）の和に、左右のつま先（足の質量の残り）を足したもの。
    /// 試合の体（<see cref="Fighter.TotalMass"/>）と同じ値（<c>BodySizingTests</c> が確かめる）。
    /// </summary>
    public static float PhysicalMassKg(BodyBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var masses = (FighterTuning.Default with { Build = build }).Masses;
        var toes = masses.Foot / (1f - FootGeometry.ToeShare) * FootGeometry.ToeShare;
        return masses.Total + (2f * toes);
    }
}
