namespace MiniRing.Characters;

/// <summary>
/// 体格（<see cref="BodyBuild"/>）と、名簿の紙の身長・体重を 1 つの式でつなぐ（CLAUDE.md §1.5・§1.7: 紙の数字は見えている体から）。
/// <para>
/// 身長 = 物理の体の立った高さ（<see cref="PhysicalHeightMeters"/>: 足の裏から頭の上まで、部位の箱を生成の並びで積んだ高さ）、
/// 体重 = 物理の体の質量（<see cref="PhysicalMassKg"/>: 部位の体積に比例。試合の体の <see cref="Fighter.TotalMass"/> と同じ）。
/// 名簿のプロフィール（<see cref="CharacterProfile.HeightOf"/>・<see cref="CharacterProfile.WeightOf"/>）、計量・階級、キャラメイクはこの値だけを使う
/// （2026-11-24 まで紙は 175 cm × 比・72 kg × 比² × 太さという別の式で、基準で 29 cm・4 kg ずれていた）。
/// </para>
/// <para>
/// キャラメイクの逆向き（<see cref="Solve"/>）: 指定の身長になる身長の比を、次に指定の体重になる太さ（<see cref="BodyBuild.Muscle"/>）を二分法で解く
/// （高さは身長の比に、質量は太さに単調に増える）。解いた体格から出し直すとちょうど指定の値に戻る。強さの倍率は持たない（決めるのは寸法だけ）。
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
        foreach (var (value, name, param) in new[] { (arms, "腕の長さ", nameof(arms)), (legs, "脚の長さ", nameof(legs)) })
        {
            if (!float.IsFinite(value) || value is < MinRatio or > MaxRatio)
            {
                throw new ArgumentOutOfRangeException(param, value, $"{name}は基準の {MinRatio}〜{MaxRatio} 倍にしてください。");
            }
        }

        (arms, legs) = (MathF.Round(arms, 4), MathF.Round(legs, 4));
        var height = HeightRatio(heightCm, legs);
        var (lo, hi) = WeightRange(heightCm, arms, legs);
        if (weightKg < lo || weightKg > hi)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, $"身長 {heightCm} cm で作れる体重は {lo}〜{hi} kg です（筋肉の太さが基準の {MinRatio}〜{MaxRatio} 倍）。");
        }

        var muscle = Bisect(m => PhysicalMassKg(new BodyBuild(height, arms, legs, m)), weightKg, m => CharacterProfile.WeightOf(new BodyBuild(height, arms, legs, m)) == weightKg)
            ?? throw new InvalidOperationException($"身長 {heightCm} cm で体重 {weightKg} kg になる太さが見つかりませんでした。");
        return new BodyBuild(height, arms, legs, muscle).Validate();
    }

    /// <summary>この身長 [cm] になる身長の比（脚の長さの比 <paramref name="legs"/> のとき）。範囲の外は例外。</summary>
    public static float HeightRatio(int heightCm, float legs = 1f)
    {
        float Cm(float h) => PhysicalHeightMeters(new BodyBuild(h, 1f, legs, 1f)) * 100f;
        var (lo, hi) = ((int)MathF.Ceiling(Cm(MinRatio)), (int)MathF.Floor(Cm(MaxRatio)));
        if (heightCm < lo || heightCm > hi)
        {
            throw new ArgumentOutOfRangeException(nameof(heightCm), heightCm, $"身長は {lo}〜{hi} cm にしてください（脚の長さ {legs:0.00}）。");
        }

        return Bisect(Cm, heightCm, h => CharacterProfile.HeightOf(new BodyBuild(h, 1f, legs, 1f)) == heightCm)
            ?? throw new InvalidOperationException($"身長 {heightCm} cm になる身長の比が見つかりませんでした。");
    }

    /// <summary>この身長で作れる体重の範囲 [kg]（筋肉の太さが 0.6〜1.5 倍）。</summary>
    public static (int Min, int Max) WeightRange(int heightCm, float arms = 1f, float legs = 1f)
    {
        var h = HeightRatio(heightCm, legs);
        return ((int)MathF.Ceiling(PhysicalMassKg(new BodyBuild(h, arms, legs, MinRatio))), (int)MathF.Floor(PhysicalMassKg(new BodyBuild(h, arms, legs, MaxRatio))));
    }

    /// <summary>体型（太さの比）とこの身長での体重 [kg]。体型を選んだときの体重の初めの値。</summary>
    public static int WeightFor(int heightCm, float muscle, float arms = 1f, float legs = 1f)
        => CharacterProfile.WeightOf(new BodyBuild(HeightRatio(heightCm, legs), arms, legs, muscle).Validate());

    /// <summary>
    /// 物理の体の質量 [kg]: 部位ごとの質量（<see cref="FighterTuning.Masses"/>、体積に比例）の和に、左右のつま先（足の質量の残り）を足したもの。
    /// 試合の体（<see cref="Fighter.TotalMass"/>）と同じ値（<c>CharacterMakerTests</c> が確かめる）。
    /// </summary>
    public static float PhysicalMassKg(BodyBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var masses = (FighterTuning.Default with { Build = build }).Masses;
        var toes = masses.Foot / (1f - FootGeometry.ToeShare) * FootGeometry.ToeShare;
        return masses.Total + (2f * toes);
    }

    /// <summary>
    /// 物理の体の立った高さ [m]: 足の裏から頭の上まで。生成（<see cref="Fighter.Spawn"/> → <c>HumanoidRagdoll</c>）が足・すね・もも・骨盤・胸・頭の箱を
    /// 縦に積み、つなぎ目ごとに <see cref="FighterTuning.JointOverlap"/> だけ重ねる並びと同じ式（<c>CharacterMakerTests</c> が生成した体と比べる）。
    /// </summary>
    public static float PhysicalHeightMeters(BodyBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var tuning = FighterTuning.Default with { Build = build };
        var h = tuning.HalfExtents;
        var stacked = 2f * (h.Foot.Y + h.Shin.Y + h.Thigh.Y + h.Pelvis.Y + h.Chest.Y + h.Head.Y);
        return stacked - (5f * tuning.JointOverlap);
    }

    /// <summary>
    /// 単調に増える <paramref name="f"/> が <paramref name="target"/> になる比（0.6〜1.5）を二分法で探し、4 桁に丸めた値のうち <paramref name="exact"/>（丸めた紙の数字が合う）を満たす物。
    /// 見つからなければ null。
    /// </summary>
    private static float? Bisect(Func<float, float> f, float target, Func<float, bool> exact)
    {
        var (lo, hi) = (MinRatio, MaxRatio);
        for (var i = 0; i < 40; i++)
        {
            var mid = (lo + hi) / 2f;
            (lo, hi) = f(mid) < target ? (mid, hi) : (lo, mid);
        }

        var x = MathF.Round((lo + hi) / 2f, 4, MidpointRounding.AwayFromZero);
        for (var step = 0; step <= 20; step++)
        {
            foreach (var candidate in new[] { x + (step * 1e-4f), x - (step * 1e-4f) })
            {
                var c = MathF.Round(candidate, 4, MidpointRounding.AwayFromZero);
                if (c is >= MinRatio and <= MaxRatio && exact(c))
                {
                    return c;
                }
            }
        }

        return null;
    }
}
