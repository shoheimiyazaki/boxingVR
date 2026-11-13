namespace MiniRing.Bots;

/// <summary>
/// 守りの効き目の記憶（docs/LEARNING_PLAN.md の B1）: 拳の種類 × 守りの技ごとに、「この守りでどれだけ被ダメージを抑えられたか」の
/// 確からしさをベータ分布で持つ。事前分布は防御ドリルで測った効き目（<see cref="DefenseMove.Against"/>）を平均に、
/// <see cref="BotStyle.LearnPrior"/> 回ぶんの経験として信じる。守るたびに、受けたダメージから決まる 0..1 の報酬を割合で足す。
/// <para>
/// 選ぶときは各守りの分布から 1 つずつ抜き（トンプソン抽出）、その値を効き目の代わりに重みへ掛ける。分からない守りは抜く値がばらつくので、
/// ときどき試される。乱数はボットの種の乱数を使い、記憶は配列で持つ（決定的、§1.6）。ボットはラウンドごとに作り直されるので、
/// ラウンドをまたいで覚えるときは同じ記憶を渡す。
/// </para>
/// </summary>
public sealed class DefenseMemory
{
    private static readonly int Kinds = Enum.GetValues<PunchKind>().Length;
    private static readonly int Defenses = Enum.GetValues<Defense>().Length;

    private readonly float[] _alpha = new float[Kinds * Defenses];
    private readonly float[] _beta = new float[Kinds * Defenses];

    /// <summary>事前分布の重み（経験の回数を数えるとき差し引く）。</summary>
    private readonly float[] _prior = new float[Kinds * Defenses];

    public DefenseMemory(BotStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var strength = MathF.Max(0.5f, style.LearnPrior);
        foreach (var kind in Enum.GetValues<PunchKind>())
        {
            foreach (var defense in DefenseBook.Techniques)
            {
                // 効き目 0 の組（選ばない守り）も分布は持つが、選ぶ側で外す。端の 0・1 は抜く値が動かなくなるので少し内へ。
                var mean = Math.Clamp(style.Defenses.Of(defense).Against(kind), 0.05f, 0.95f);
                var i = Index(kind, defense);
                _alpha[i] = mean * strength;
                _beta[i] = (1f - mean) * strength;
                _prior[i] = strength;
            }
        }
    }

    /// <summary>この組で守った回数（事前分布の分を除く、報酬の重みの合計）。</summary>
    public float Experience(PunchKind kind, Defense defense)
    {
        var i = Index(kind, defense);
        return _alpha[i] + _beta[i] - _prior[i];
    }

    /// <summary>効き目の見込み（事後の平均、0..1）。</summary>
    public float Mean(PunchKind kind, Defense defense)
    {
        var i = Index(kind, defense);
        return _alpha[i] / (_alpha[i] + _beta[i]);
    }

    /// <summary>効き目を 1 つ抜く（トンプソン抽出）。</summary>
    public float Sample(PunchKind kind, Defense defense, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var i = Index(kind, defense);
        var x = Gamma(_alpha[i], random);
        var y = Gamma(_beta[i], random);
        return x + y > 0f ? x / (x + y) : 0.5f;
    }

    /// <summary>守り 1 回ぶんの結果を覚える。<paramref name="reward"/> は 0（守れなかった）… 1（何も受けなかった）。</summary>
    public void Update(PunchKind kind, Defense defense, float reward)
    {
        if (defense == Defense.None || !float.IsFinite(reward))
        {
            return;
        }

        var r = Math.Clamp(reward, 0f, 1f);
        var i = Index(kind, defense);
        _alpha[i] += r;
        _beta[i] += 1f - r;
    }

    private static int Index(PunchKind kind, Defense defense) => ((int)kind * Defenses) + (int)defense;

    /// <summary>
    /// ガンマ分布から 1 つ抜く（Marsaglia–Tsang。形 &lt; 1 は形 + 1 から抜いて U^(1/形) を掛ける）。正規乱数は Box–Muller。
    /// </summary>
    private static float Gamma(float shape, Random random)
    {
        if (shape <= 0f)
        {
            return 0f;
        }

        if (shape < 1f)
        {
            return Gamma(shape + 1f, random) * MathF.Pow(MathF.Max(1e-7f, random.NextSingle()), 1f / shape);
        }

        var d = shape - (1f / 3f);
        var c = 1f / MathF.Sqrt(9f * d);
        while (true)
        {
            var u1 = MathF.Max(1e-7f, random.NextSingle());
            var u2 = random.NextSingle();
            var z = MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
            var v = 1f + (c * z);
            if (v <= 0f)
            {
                continue;
            }

            v = v * v * v;
            var u = MathF.Max(1e-7f, random.NextSingle());
            if (MathF.Log(u) < (0.5f * z * z) + (d * (1f - v + MathF.Log(v))))
            {
                return d * v;
            }
        }
    }
}
