using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 目のドリル（visionlab）: 目が潰れると何が起きるか、目を狙うことが点になるかを、試合と同じ物理・同じボットで測る。
/// 守りが間に合うかは、同じ拳が来るのを目の状態ごとに見せて測る単体テスト（<c>VisionTests.OneDestroyedEye_DelaysTheDefenseAgainstPunchesFromThatSide</c>）に置いた
/// （守る受け手が打たれる前に守りを出せるドリルでは、健康な目でも守りが 0 回だった）。
/// <list type="number">
/// <item>目を潰して始める: 片目・両目を潰されたチャンピオンが、無事なチャンピオンに何点取れるか（目の損傷の価値）。</item>
/// <item>目を狙う: 目・見えない側を狙う重みを足したスタイルが、足さない同じスタイルに勝つか、相手の目をどれだけ潰せたか。</item>
/// </list>
/// </summary>
internal sealed class VisionLab(int bouts, float seconds, int seed, Action<string> log)
{
    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    public void Run(bool start, bool aim)
    {
        if (start)
        {
            BlindStart();
        }

        if (aim)
        {
            EyeAim();
        }
    }

    private void BlindStart()
    {
        log("## 目を潰して始める");
        log("");
        log($"チャンピオン同士。片方が始めから目を潰されている。{bouts} 本（赤青を入れ替え）× {seconds:0} 秒、種 {seed}〜。点は 勝ち 1 / 引き分け 0.5。");
        log("");
        log("| 潰した目 | 潰された側の点 | 潰された側が与えたダメージ | 相手が与えたダメージ |");
        log("|---|---|---|---|");
        var champion = Contender.Named("champion");
        var blinded = new Contender("champion-blinded", champion.Create);
        foreach (var (label, left, right) in new[] { ("無事（対照）", false, false), ("片目（左）", true, false), ("両目", true, true) })
        {
            var specs = BotArena.Pair(blinded, champion, bouts, seconds, seed).Select(spec =>
            {
                var blindIsRed = spec.Red.Name == blinded.Name;
                return spec with
                {
                    Setup = g =>
                    {
                        var victim = blindIsRed ? g.Player : g.Cpu;
                        if (left)
                        {
                            victim.Voxels!.DestroyEye(left: true);
                        }

                        if (right)
                        {
                            victim.Voxels!.DestroyEye(left: false);
                        }
                    },
                };
            }).ToList();
            var results = BotArena.RunAll(specs);
            var rows = Standing.From(results);
            var own = rows.First(r => r.Name == blinded.Name);
            log($"| {label} | {F(own.Score)} | {F(own.DamageFor)} | {F(own.DamageAgainst)} |");
        }

        log("");
    }

    private void EyeAim()
    {
        log("## 目を狙う");
        log("");
        log($"同じスタイルどうしの対戦。片方だけ、狙いの重みに `Eye = 3`・`BlindSide = 2` を足す（`Targeting.Weight`: 目は相手の見えが悪いほど、見えない側は片側が悪いほど重みが増える）。{bouts} 本 × {seconds:0} 秒、種 {seed}〜。");
        log("`相手の目の損失` は、その試合で相手が失った目の割合の最大（0 = 両目無事 … 1 = 両目）の平均。");
        log("");
        log("| スタイル | 点（目を狙う側） | 勝 | 分 | 敗 | 与ダメージ | 受ダメージ | 相手の目の損失 | 自分の目の損失 |");
        log("|---|---|---|---|---|---|---|---|---|");
        foreach (var baseName in new[] { "champion", "karate", "swarmer" })
        {
            var style = BotCatalog.Style(baseName);
            var eyes = style with { Name = baseName + "+eye", Aim = (style.Aim ?? AimWeights.None) with { Eye = 3f, BlindSide = 2f } };
            var results = BotArena.RunAll([.. BotArena.Pair(Contender.Of(eyes), Contender.Of(style), bouts, seconds, seed)]);
            var own = Standing.From(results).First(r => r.Name == eyes.Name);
            var theirLoss = results.Average(r => r.Of(baseName).EyeLoss);
            var ownLoss = results.Average(r => r.Of(eyes.Name).EyeLoss);
            log($"| {baseName} | {F(own.Score)} | {own.Wins} | {own.Draws} | {own.Losses} | {F(own.DamageFor, "0")} | {F(own.DamageAgainst, "0")} | {F(theirLoss)} | {F(ownLoss)} |");
        }

        log("");
    }
}
