using System.Globalization;
using System.Text.Json;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 打って離れる剣士（<see cref="HitAndAway"/>）を、練習試合で育てる: 間合いの取り合いの値（余裕・相手の踏み込みの読み・隙と見る時間・待つ時間・
/// 回る強さ・出端を打つか）を少しずつ変えた候補を、決まった相手たち（今までの剣士・バット・棍棒・拳）と戦わせて育てる（<see cref="MatchTrainer{T}"/>:
/// 世代ごとに種を変え、最後に育てるのに使わなかった種で始めの値と比べ直す）。
/// </summary>
internal static class AwayLab
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static (HitAndAway Best, string Report) Run(Func<HitAndAway?, IReadOnlyList<Contender>> contenders, string trainee, IReadOnlyList<string> opponents,
        HitAndAway start, int gens, int lambda, int bouts, float seconds, int seed, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(contenders);
        var others = contenders(null);
        var trainer = new MatchTrainer<HitAndAway>(h => contenders(h).Single(c => c.Name == trainee), Mutate, Describe);
        return trainer.Run($"打って離れる剣士を育てる（{trainee}）", [.. opponents.Select(name => others.Single(c => c.Name == name))], start, gens, lambda, bouts, seconds, seed, log);
    }

    public static HitAndAway Load(string path) => JsonSerializer.Deserialize<HitAndAway>(File.ReadAllText(path))?.Validated()
        ?? throw new InvalidDataException($"{path} を読めません。");

    public static void Save(string path, HitAndAway value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));

    private static HitAndAway Mutate(HitAndAway p, Random random)
    {
        float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        return new HitAndAway(
            Math.Clamp(p.Margin + (0.04f * Gauss()), -0.15f, 0.4f),
            Math.Clamp(p.Lunge + (0.15f * Gauss()), 0f, 1.5f),
            Math.Clamp(p.OpeningTime + (0.12f * Gauss()), 0f, 1.5f),
            Math.Clamp(p.Patience * MathF.Exp(0.35f * Gauss()), 0.2f, 6f),
            Math.Clamp(p.Circle + (0.12f * Gauss()), 0f, 1f),
            random.NextDouble() < 0.15 ? !p.Debana : p.Debana).Validated();
    }

    private static string Describe(HitAndAway h) => string.Create(CultureInfo.InvariantCulture,
        $"余裕 {h.Margin:0.00} m・踏み込みの読み {h.Lunge:0.00}・隙 {h.OpeningTime:0.00} s・待ち {h.Patience:0.00} s・回る {h.Circle:0.00}・出端 {(h.Debana ? "打つ" : "打たない")}");
}
