using System.Globalization;
using System.Text;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 武器の教科書を練習試合で育てる（handlespar、<see cref="MatchTrainer{T}"/> と同じ考え方をゲームと同じ道で）: 育てる武器（刀・ロングソード・こんぼう）を握るキャラ
/// （<c>trainee</c>）の <see cref="WeaponStyle"/> を少しずつ変えた候補を、決まった相手たちと戦わせ、勝ち越しの多い方を残す（(1+λ)）。
/// <list type="bullet">
/// <item>世代ごとに試合の種を変え、親もその世代の種で戦い直す。同じ世代の候補は同じ種で比べる。</item>
/// <item>最後に、育てるのに使わなかった種（同じ相手）と、使わなかった相手（別の拳・はめる物の相手）の両方で、始めの値と比べ直し、<b>どちらも</b>始めの値以上で、
/// 合計が正のときだけ採る。</item>
/// </list>
/// 育てるのは入力の側の値だけ（間合いの取り合い・守り・技の間・こんぼうの間合いなど。§1.7: 筋力・質量・材料・威力の倍率は無い）。
/// </summary>
internal static class HandleSpar
{
    public static string Describe(WeaponStyle s, WeaponKind weapon) => weapon == WeaponKind.WoodenClub
        ? string.Create(CultureInfo.InvariantCulture, $"間合い {s.ClubRange:0.000}・休み {s.ClubRest:0.00}・待つ距離 {s.ClubStandOff:0.000}・厳密 {(s.ClubExact ? "○" : "×")}・振りの重み {Weights(s.ClubSwingWeights)}")
        : string.Create(CultureInfo.InvariantCulture,
            $"休み {s.Rest:0.00}・間合いの下限 {s.MinReach:0.00}・技の重み {Weights(s.TechniqueWeights)}・{GuardSpar.Describe(s.Defense)}・取り合い {(s.HitAndAway is { } h ? $"余裕 {h.Margin:0.00}/踏み込み {h.Lunge:0.00}/隙 {h.OpeningTime:0.00}/待ち {h.Patience:0.00}/回る {h.Circle:0.00}/出端 {(h.Debana ? "○" : "×")}" : "なし")}"
            + $"・自分の体を避ける {(s.Clearance is { } c ? $"余裕 {c.Margin:0.00}/握りから {c.FromGrip:0.00}/傾き {c.MaxTilt:0.00}" : "なし")}"
            + $"・技の時計 {(s.Pacing is { } p ? $"握り {p.GripSlack:0.00}/角 {p.AngleSlack:0.00}/下限 {p.Floor:0.00}" : "なし")}");

    private static string Weights(IReadOnlyDictionary<string, float>? w) => w is null ? "同じ" : string.Join("/", w.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key} {p.Value:0.00}")));

    /// <summary>候補を作る: 武器のボットが読む側の値だけを少し変える（剣士は守り・取り合い・休み・自分の体を避ける通り道・技の時計、こんぼうは間合いと休み）。</summary>
    public static WeaponStyle Mutate(WeaponStyle p, WeaponKind weapon, Random random)
    {
        float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        float Scale(float v, float sigma, float min, float max) => Math.Clamp(v * MathF.Exp(sigma * Gauss()), min, max);
        float Shift(float v, float sigma, float min, float max) => Math.Clamp(v + (sigma * Gauss()), min, max);
        // 技の重み: 型ごとに少し変える（時々 0 = 出さない、時々 1 へ戻す）。名前は武器の型・振りの型。
        IReadOnlyDictionary<string, float> MutateWeights(IReadOnlyDictionary<string, float>? old, IEnumerable<string> names) =>
            names.ToDictionary(n => n, n =>
            {
                var current = old?.GetValueOrDefault(n, 1f) ?? 1f;
                if (random.NextDouble() > 0.35)
                {
                    return current; // 変える型は一部だけ
                }

                var r = random.NextDouble();
                return r < 0.08 ? 0f : r < 0.16 ? 1f : Scale(MathF.Max(current, 0.05f), 0.5f, 0.05f, 4f);
            });
        if (weapon == WeaponKind.WoodenClub)
        {
            var range = Shift(p.ClubRange, 0.03f, 0.2f, 0.6f);
            var swingWeights = MutateWeights(p.ClubSwingWeights, ClubBot.OneHanded.Select(f => f.ToString()));
            return (p with
            {
                ClubSwingWeights = swingWeights.Values.Sum() > 0f ? swingWeights : p.ClubSwingWeights,
                ClubRange = range,
                ClubRest = Scale(p.ClubRest, 0.3f, 0.1f, 2f),
                ClubStandOff = MathF.Max(range, Shift(p.ClubStandOff, 0.05f, 0.25f, 1f)),
                ClubExact = random.NextDouble() < 0.15 ? !p.ClubExact : p.ClubExact,
            }).Validated();
        }

        var hit = p.HitAndAway;
        if (random.NextDouble() < 0.12)
        {
            hit = hit is null ? new HitAndAway() : null;
        }
        else if (hit is not null)
        {
            hit = AwayLab.Mutate(hit, random);
        }

        var clearance = p.Clearance;
        if (random.NextDouble() < 0.1)
        {
            clearance = clearance is null ? SelfClearance.Adopted : null;
        }
        else if (clearance is not null)
        {
            clearance = clearance with { Margin = Shift(clearance.Margin, 0.03f, 0.02f, 0.3f), MaxTilt = Shift(clearance.MaxTilt, 0.1f, 0.2f, 1.2f) };
        }

        var pacing = p.Pacing;
        if (random.NextDouble() < 0.1)
        {
            pacing = pacing is null ? new SwingPacing() : null;
        }
        else if (pacing is not null)
        {
            pacing = pacing with { GripSlack = Scale(pacing.GripSlack, 0.25f, 0.08f, 1f), AngleSlack = Scale(pacing.AngleSlack, 0.25f, 0.2f, 1.5f), Floor = Shift(pacing.Floor, 0.05f, 0.05f, 0.5f) };
        }

        return (p with
        {
            Defense = GuardSpar.Mutate(p.Defense, random),
            HitAndAway = hit,
            Clearance = clearance,
            Pacing = pacing,
            Rest = Scale(p.Rest, 0.25f, 0.2f, 2f),
            MinReach = Shift(p.MinReach, 0.02f, 0.26f, 0.42f),
            TechniqueWeights = MutateWeights(p.TechniqueWeights, FormSwordsman.Forms(weapon).Select(f => f.Name)) is { } tw && tw.Values.Sum() > 0f ? tw : p.TechniqueWeights,
        }).Validated();
    }

    internal sealed record Eval(float Value, int Wins, int Losses, int Draws, float Dealt, float Taken, float CellDiff, string Detail);

    private static HandleLab.Overrides OverridesFor(WeaponKind weapon, WeaponStyle style, string trainee) => weapon switch
    {
        WeaponKind.Katana => new HandleLab.Overrides(Katana: style, OnlyFor: trainee),
        WeaponKind.Longsword => new HandleLab.Overrides(Longsword: style, OnlyFor: trainee),
        _ => new HandleLab.Overrides(Club: style, OnlyFor: trainee),
    };

    /// <summary>値 <paramref name="style"/> のトレーニーを、相手たちと各 <paramref name="bouts"/> 試合（コーナーを半分ずつ入れ替え）戦わせた成績。</summary>
    public static Eval Evaluate(string trainee, WeaponKind weapon, WeaponStyle style, IReadOnlyList<string> opponents, int bouts, float seconds, int seed, bool referee, int parallel)
    {
        var matchups = opponents.Select(o => new HandleLab.Matchup(trainee, o)).ToList();
        var results = HandleLab.Run(matchups, bouts, seconds, seed, referee, parallel, OverridesFor(weapon, style, trainee), TextWriter.Null);
        var score = HandleLab.Score(results, weapon);
        var detail = new StringBuilder();
        foreach (var group in results.GroupBy(b => b.Red.Id == trainee ? b.Blue.Id : b.Red.Id))
        {
            var mine = group.Select(b => b.Red.Id == trainee ? b.Red : b.Blue).ToList();
            detail.Append(CultureInfo.InvariantCulture, $"{group.Key} {mine.Count(s => s.Result > 0)}-{mine.Count(s => s.Result < 0)} ");
        }

        return new Eval(score.Value, score.Wins, score.Losses, score.Draws, score.Dealt, score.Taken, score.CellDiff, detail.ToString().Trim());
    }

    public static (WeaponStyle Best, string Report) Run(string trainee, WeaponStyle start, IReadOnlyList<string> opponents, IReadOnlyList<string> heldOutOpponents,
        int gens, int lambda, int bouts, float seconds, int seed, bool referee, int parallel, TextWriter log)
    {
        var character = CharacterRoster.Get(trainee);
        var weapon = character.Weapon ?? throw new ArgumentException($"{trainee} は武器を握っていません。");
        if (!WeaponCatalog.HasBot(weapon))
        {
            throw new ArgumentException($"{trainee} の武器にはボットの値がありません。");
        }

        var random = new Random(seed);
        var report = new StringBuilder();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"# 武器の教科書を育てる（{trainee} / {CharacterLabel(weapon)}、相手: {string.Join("・", opponents)}、各相手 {bouts} 試合 × {seconds:0} 秒、{gens} 世代 × {lambda} 候補、世代ごとに種を変える、レフェリー {(referee ? "あり" : "なし")}、LogicVersion {BoxerBot.LogicVersion}）"));
        report.AppendLine();
        report.AppendLine("点 = 勝ち − 負け + 0.45 tanh((与えた − 受けた) / 400) + 0.45 tanh(1 試合あたりの（相手が失ったセル − 自分が失ったセル）/ 300)（`HandleLab.Score`）。");
        report.AppendLine();
        report.AppendLine("| 世代 | 値 | 点（その世代の種） | 勝ち | 負け | 分け | 与えた | 受けた | セル差/試合 | 相手ごと（勝ち-負け） |");
        report.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---|");
        var parent = start;
        for (var g = 1; g <= gens; g++)
        {
            var genSeed = seed + (1000 * g);
            var parentScore = Evaluate(trainee, weapon, parent, opponents, bouts, seconds, genSeed, referee, parallel);
            var scored = new List<(WeaponStyle Child, Eval Score)>();
            for (var k = 0; k < lambda; k++)
            {
                var child = Mutate(parent, weapon, random);
                scored.Add((child, Evaluate(trainee, weapon, child, opponents, bouts, seconds, genSeed, referee, parallel)));
                log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"gen {g} cand {k + 1}: {scored[^1].Score.Value:0.00} (parent {parentScore.Value:0.00}) {Describe(child, weapon)}"));
            }

            var best = scored.MaxBy(x => x.Score.Value);
            if (best.Score.Value > parentScore.Value)
            {
                parent = best.Child;
                report.AppendLine(Row(g, "替えた", parent, weapon, best.Score));
            }
            else
            {
                report.AppendLine(Row(g, "親のまま", parent, weapon, parentScore));
            }
        }

        var heldSeed = seed + 7777;
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"育てるのに使わなかった種（{heldSeed}、各相手 {bouts * 2} 試合）と、使わなかった相手（{string.Join("・", heldOutOpponents)}）で、始めの値と比べ直す:"));
        report.AppendLine();
        report.AppendLine("| 比べ | 値 | 点 | 勝ち | 負け | 分け | 与えた | 受けた | セル差/試合 | 相手ごと |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        var gain = 0f;
        var gains = new List<float>();
        foreach (var (label, set) in new[] { ("使わなかった種・同じ相手", opponents), ("使わなかった相手", heldOutOpponents) })
        {
            if (set.Count == 0)
            {
                continue;
            }

            var before = Evaluate(trainee, weapon, start, set, bouts * 2, seconds, heldSeed, referee, parallel);
            var after = Evaluate(trainee, weapon, parent, set, bouts * 2, seconds, heldSeed, referee, parallel);
            report.AppendLine(Line(label, "始め", start, weapon, before));
            report.AppendLine(Line(label, "育てた", parent, weapon, after));
            gains.Add(after.Value - before.Value);
            gain += after.Value - before.Value;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"check {label}: start {before.Value:0.00}, trained {after.Value:0.00}"));
        }

        var adopt = !ReferenceEquals(parent, start) && gains.All(x => x >= 0f) && gain > 0f;
        var gainText = string.Join(" / ", gains.Select(x => x.ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture)));
        report.AppendLine();
        report.AppendLine(ReferenceEquals(parent, start)
            ? "どの世代も親を超える候補が無く、始めの値のまま。"
            : adopt
            ? string.Create(CultureInfo.InvariantCulture, $"比べ直しの両方で始めの値以上（{gainText}、合計 {gain:+0.00;-0.00;0}）なので、育てた値を採る。")
            : $"比べ直し（{gainText}）のどれかが始めの値より悪い、または合計が正でないので、育てた値は採らず、始めの値のままにする。");
        return (adopt ? parent : start, report.ToString());
    }

    private static string CharacterLabel(WeaponKind weapon) => weapon switch { WeaponKind.Katana => "日本刀", WeaponKind.Longsword => "ロングソード", _ => "こんぼう" };

    private static string Row(int gen, string what, WeaponStyle value, WeaponKind weapon, Eval s) => string.Create(CultureInfo.InvariantCulture,
        $"| {gen}（{what}） | {Describe(value, weapon)} | {s.Value:0.00} | {s.Wins} | {s.Losses} | {s.Draws} | {s.Dealt:0} | {s.Taken:0} | {s.CellDiff:0} | {s.Detail} |");

    private static string Line(string name, string label, WeaponStyle value, WeaponKind weapon, Eval s) => string.Create(CultureInfo.InvariantCulture,
        $"| {name} | {label}: {Describe(value, weapon)} | {s.Value:0.00} | {s.Wins} | {s.Losses} | {s.Draws} | {s.Dealt:0} | {s.Taken:0} | {s.CellDiff:0} | {s.Detail} |");
}
