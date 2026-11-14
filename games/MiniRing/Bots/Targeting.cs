using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 狙いどころの重み（戦略: どこを打ちに行くか）。拳を出すたびに、その拳の種類（頭へ / 胴へ）で打てる狙いどころから、
/// 重みの比で 1 つ選ぶ（<see cref="Targeting.Pick"/>）。真ん中（従来どおり）の重みは常に 1。全部 0 なら従来どおり真ん中だけ。
/// <list type="bullet">
/// <item>頭へ: <see cref="Chin"/>（あご）・<see cref="Temple"/>（こめかみ）・<see cref="ExposedBrain"/>（頭蓋が削れて露出した脳。露出しているときだけ）。</item>
/// <item>胴へ: <see cref="Liver"/>（肝臓）・<see cref="SolarPlexus"/>（みぞおち）・<see cref="Heart"/>（心臓）。</item>
/// <item>どちらの拳でも: <see cref="GuardArm"/>（ガードの腕を打って壊す。傷んでいる方を）。</item>
/// </list>
/// </summary>
public sealed record AimWeights(float Chin = 0f, float Temple = 0f, float ExposedBrain = 0f, float Liver = 0f, float SolarPlexus = 0f, float Heart = 0f, float GuardArm = 0f, float Eye = 0f, float BlindSide = 0f)
{
    /// <summary>真ん中だけを狙う（従来どおり）。</summary>
    public static AimWeights None { get; } = new();

    /// <summary>重みを 0〜10 に収める（読めない値は 0）。</summary>
    public AimWeights Validated()
    {
        static float W(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 10f) : 0f;
        return new AimWeights(W(Chin), W(Temple), W(ExposedBrain), W(Liver), W(SolarPlexus), W(Heart), W(GuardArm), W(Eye), W(BlindSide));
    }

    /// <summary>狙いどころの重み。</summary>
    public float Of(AimGoal goal) => goal switch
    {
        AimGoal.Center => 1f,
        AimGoal.Chin => Chin,
        AimGoal.Temple => Temple,
        AimGoal.ExposedBrain => ExposedBrain,
        AimGoal.Liver => Liver,
        AimGoal.SolarPlexus => SolarPlexus,
        AimGoal.Heart => Heart,
        AimGoal.GuardArm => GuardArm,
        AimGoal.Eye => Eye,
        AimGoal.BlindSide => BlindSide,
        _ => 0f,
    };
}

/// <summary>
/// 狙いどころの選び方と、無理のない範囲（パンチに速さと体重が乗る範囲）への収め方。
/// <para>
/// 技（経路の形・溜め・踏み込み）はそのままで、狙いの点だけを動かす。動かせる範囲は、その技の本来の狙い（頭・胴の真ん中）から
/// 腕の長さの <see cref="MaxShift"/> 倍まで、かつ肩から見た向きのずれが <see cref="MaxAngleDegrees"/> 度まで: これを越えると、
/// 体の軸と力の作用線が離れて体重が乗らず（<see cref="PunchMechanics"/> の連結度が落ちる）、経路の終わりが腕の届く先を外れて
/// 速さも乗らない。狙いがその外なら、範囲の縁まで寄せる（届くところで、いちばん近い点を打つ）。
/// </para>
/// </summary>
public static class Targeting
{
    /// <summary>本来の狙いから動かせる距離（腕の長さに対する割合）。頭の幅・胴の半分ほど: あご・こめかみ・肝臓まで届き、肩の外までは追わない。</summary>
    public const float MaxShift = 0.4f;

    /// <summary>肩から見た狙いの向きのずれの上限 [度]。体重の乗る向き（体の軸の近く）から外れすぎない。</summary>
    public const float MaxAngleDegrees = 28f;

    /// <summary>頭へ打つ拳で狙える狙いどころ。</summary>
    public static IReadOnlyList<AimGoal> HeadGoals { get; } = [AimGoal.Center, AimGoal.Chin, AimGoal.Temple, AimGoal.ExposedBrain, AimGoal.GuardArm, AimGoal.Eye, AimGoal.BlindSide];

    public static float Weight(AimWeights weights, AimGoal goal, TargetMap map) => weights.Of(goal) * (goal switch
    {
        AimGoal.Eye => 2f - map.Vision.Binocular,
        AimGoal.BlindSide => 1f - MathF.Min(map.Vision.Left, map.Vision.Right),
        _ => 1f,
    });

    /// <summary>胴へ打つ拳で狙える狙いどころ。</summary>
    public static IReadOnlyList<AimGoal> BodyGoals { get; } = [AimGoal.Center, AimGoal.Liver, AimGoal.SolarPlexus, AimGoal.Heart, AimGoal.GuardArm];

    /// <summary>
    /// 拳 1 発の狙いどころを、頭・胴を問わず選ぶ（今使えるものから重みの比で。真ん中の重みは 1）。選んだ狙いが拳の向き（頭 / 胴）と違えば、
    /// 呼び出し側は <see cref="Retarget"/> で同じ手・同じ形の拳を高さだけ打ち分ける（ジャブ ↔ ボディジャブ、フック ↔ ボディフック）。
    /// </summary>
    public static AimGoal PickAny(AimWeights weights, TargetMap? map, float roll)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (map is null)
        {
            return AimGoal.Center;
        }

        var goals = HeadGoals.Concat(BodyGoals).Distinct().ToArray();
        var total = goals.Sum(g => map.Available(g) ? Weight(weights, g, map) : 0f);
        var pick = roll * total;
        foreach (var goal in goals)
        {
            var w = map.Available(goal) ? Weight(weights, goal, map) : 0f;
            if (pick < w)
            {
                return goal;
            }

            pick -= w;
        }

        return AimGoal.Center;
    }

    /// <summary>狙いどころが胴か（肝臓・みぞおち・心臓）。</summary>
    public static bool IsBodyGoal(AimGoal goal) => goal is AimGoal.Liver or AimGoal.SolarPlexus or AimGoal.Heart;

    /// <summary>狙いどころが頭か（あご・こめかみ・露出した脳）。</summary>
    public static bool IsHeadGoal(AimGoal goal) => goal is AimGoal.Chin or AimGoal.Temple or AimGoal.ExposedBrain or AimGoal.Eye or AimGoal.BlindSide;

    /// <summary>
    /// 狙いの高さに合わせて、同じ手・同じ形の拳へ打ち分ける（頭の拳を胴へ、胴の拳を頭へ）。アッパーとオーバーハンドは頭のまま
    /// （胴へ下ろす形が無い）、フェイントは変えない。打ち分けられなければ元の拳のまま。
    /// </summary>
    public static Technique Retarget(Technique technique, bool toBody) => (technique, toBody) switch
    {
        (Technique.Jab, true) => Technique.BodyJab,
        (Technique.Cross, true) => Technique.BodyCross,
        (Technique.LeadHook, true) => Technique.LeadBodyHook,
        (Technique.RearHook, true) => Technique.RearBodyHook,
        (Technique.BodyJab, false) => Technique.Jab,
        (Technique.BodyCross, false) => Technique.Cross,
        (Technique.LeadBodyHook, false) => Technique.LeadHook,
        (Technique.RearBodyHook, false) => Technique.RearHook,
        _ => technique,
    };

    /// <summary>
    /// 拳 1 発の狙いどころを選ぶ: その拳で打てる狙いどころのうち、今使えるもの（<see cref="TargetMap.Available"/>）から重みの比で。
    /// <paramref name="roll"/> は 0..1 の乱数（ボットの種付きの乱数から）。
    /// </summary>
    public static AimGoal Pick(AimWeights weights, bool toBody, TargetMap? map, float roll)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (map is null)
        {
            return AimGoal.Center;
        }

        var goals = toBody ? BodyGoals : HeadGoals;
        var total = 0f;
        foreach (var goal in goals)
        {
            total += map.Available(goal) ? Weight(weights, goal, map) : 0f;
        }

        var pick = roll * total;
        foreach (var goal in goals)
        {
            var w = map.Available(goal) ? Weight(weights, goal, map) : 0f;
            if (pick < w)
            {
                return goal;
            }

            pick -= w;
        }

        return AimGoal.Center;
    }

    /// <summary>
    /// 狙いの点 <paramref name="desired"/> を、本来の狙い <paramref name="natural"/> から無理のない範囲へ収める（<paramref name="shoulder"/> は打つ側の肩の近似 —
    /// ボットは自分の頭の位置を渡す。肩とは数 cm しか離れず、向きのずれの見積もりには十分）、
    /// <paramref name="reach"/> は腕の長さ）。
    /// </summary>
    public static Vector3 Clamp(Vector3 natural, Vector3 desired, Vector3 shoulder, float reach)
    {
        var shift = desired - natural;
        var maxShift = MaxShift * reach;
        if (shift.Length() > maxShift)
        {
            shift = Vector3.Normalize(shift) * maxShift;
        }

        var aim = natural + shift;
        var a = natural - shoulder;
        var b = aim - shoulder;
        if (a.LengthSquared() < 1e-10f || b.LengthSquared() < 1e-10f)
        {
            return aim;
        }

        var max = MaxAngleDegrees * MathF.PI / 180f;
        float AngleAt(float scale)
        {
            var c = natural + (shift * scale) - shoulder;
            return c.LengthSquared() < 1e-10f ? 0f : MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(c)), -1f, 1f));
        }

        if (AngleAt(1f) <= max)
        {
            return aim;
        }

        // 向きのずれが大きすぎれば、ずらす量を縮めて縁へ（角度はずらす量に比例しないので、二分法で縁を探す）。
        float lo = 0f, hi = 1f;
        for (var i = 0; i < 20; i++)
        {
            var mid = 0.5f * (lo + hi);
            if (AngleAt(mid) <= max)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return natural + (shift * lo);
    }
}
