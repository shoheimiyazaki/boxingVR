using System.Globalization;
using System.Text;

namespace MiniRing.Highlights;

/// <summary>ある時刻の、観客に見えている状態（赤の有利 = 正）。<see cref="WinModel.Features"/> の並び。</summary>
public readonly record struct BeliefSample(float Time, float[] Features);

/// <summary>
/// 観客の「どちらが勝つか」の見込み p_red(t)（docs/EXCITEMENT.md §A）。§1.7 に従い、<b>見えている状態だけ</b>から出す
/// ロジスティック回帰: 取ったラウンドの差・意識（ふらつき・顔色の手がかり）の差・いまダウンしているか・見えている体の欠け（失ったセルの割合）の差・
/// スタミナ（息遣い）の差・与えたダメージの割合の差と、それぞれに試合の進み（0..1）を掛けた項。
/// 係数は <c>excitementfit</c> がボット同士の試合から当てはめた値（docs/arena/winmodel_*.json に当てはまりの良さと一緒に残す）。
/// </summary>
public sealed record WinModel(float[] Weights)
{
    /// <summary>特徴の名前（並び）。</summary>
    public static readonly string[] Names =
    [
        "rounds", "consciousness", "down", "voxelLoss", "stamina", "damageShare",
        "rounds×進み", "consciousness×進み", "down×進み", "voxelLoss×進み", "stamina×進み", "damageShare×進み",
    ];

    /// <summary>
    /// 当てはめた係数（最後が切片）。excitementfit（2026-11-19、6 組 × 種 8 × 40 秒・赤青の鏡つき、PhysicsVersion 59）の結果。使っていない種で Brier 0.127（五分の予想 0.25）、正解率 80 %、試合の最後の 1/4 で 92 %。
    /// 当てはまりの良さは docs/arena/winmodel_2026-11-19.json。
    /// </summary>
    public static WinModel Default { get; } = new([
        // FIT-BEGIN
        2.836f, 0.671f, 0.485f, 0.042f, -0.965f, 3.470f,
        2.046f, -0.258f, 0.377f, 0.021f, -0.051f, 3.223f,
        0f, // 切片（鏡の標本で 0: コーナーの有利は見えない）
        // FIT-END
    ]);

    public const int Count = 12;

    /// <summary>見えている状態 → 特徴（赤の有利 = 正）。<paramref name="progress"/> は試合の進み 0..1。</summary>
    public static float[] Features(int roundsDiff, float consciousnessDiff, float downDiff, float voxelLossDiff, float staminaDiff, float damageShareDiff, float progress)
    {
        float[] f = [roundsDiff, consciousnessDiff, downDiff, voxelLossDiff, staminaDiff, damageShareDiff, 0, 0, 0, 0, 0, 0];
        for (var i = 0; i < 6; i++)
        {
            f[6 + i] = f[i] * progress;
        }

        return f;
    }

    public float Predict(float[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var z = Weights[^1];
        for (var i = 0; i < features.Length && i < Weights.Length - 1; i++)
        {
            z += Weights[i] * features[i];
        }

        return 1f / (1f + MathF.Exp(-Math.Clamp(z, -30f, 30f)));
    }

    /// <summary>
    /// ロジスティック回帰（L2 正則化つきの勾配降下）。<paramref name="samples"/> は (特徴, 赤が勝ったか)。
    /// 決定的（並びと回数が同じなら同じ係数）。
    /// </summary>
    public static WinModel Fit(IReadOnlyList<(float[] X, bool RedWon)> samples, int iterations = 3000, float rate = 0.5f, float l2 = 1e-3f)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var w = new double[Count + 1];
        var n = Math.Max(1, samples.Count);
        for (var it = 0; it < iterations; it++)
        {
            var grad = new double[Count + 1];
            foreach (var (x, y) in samples)
            {
                var z = w[Count];
                for (var i = 0; i < Count; i++)
                {
                    z += w[i] * x[i];
                }

                var p = 1.0 / (1.0 + Math.Exp(-Math.Clamp(z, -30, 30)));
                var e = p - (y ? 1.0 : 0.0);
                for (var i = 0; i < Count; i++)
                {
                    grad[i] += e * x[i];
                }

                grad[Count] += e;
            }

            for (var i = 0; i <= Count; i++)
            {
                w[i] -= rate * ((grad[i] / n) + (i < Count ? l2 * w[i] : 0.0));
            }
        }

        return new WinModel([.. w.Select(v => (float)v)]);
    }

    /// <summary>当てはまりの良さ: 対数損失・ブライアー得点・正解率（p &gt; 0.5 が勝者）。</summary>
    public (float LogLoss, float Brier, float Accuracy) Quality(IReadOnlyList<(float[] X, bool RedWon)> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        double loss = 0, brier = 0, right = 0;
        foreach (var (x, y) in samples)
        {
            var p = Math.Clamp(Predict(x), 1e-6f, 1f - 1e-6f);
            loss -= y ? Math.Log(p) : Math.Log(1 - p);
            brier += (p - (y ? 1 : 0)) * (p - (y ? 1 : 0));
            right += (p > 0.5f) == y ? 1 : 0;
        }

        var n = Math.Max(1, samples.Count);
        return ((float)(loss / n), (float)(brier / n), (float)(right / n));
    }

    public string CSharp() => string.Join(", ", Weights.Select(v => v.ToString("0.000", CultureInfo.InvariantCulture) + "f"));

    public string Describe()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Weights.Length; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{(i < Names.Length ? Names[i] : "切片")}={Weights[i]:0.000} ");
        }

        return sb.ToString();
    }
}
