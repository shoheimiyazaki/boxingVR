using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>再現可能な測定用。通常テストからは名前の Probe で除外する。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StaminaLeagueProbe
{
    [TestMethod]
    public void EmptyAgainstFull_SameStyleFourBoutsSixtySeconds_Probe()
    {
        var styles = new[] { "swarmer", "outboxer", "slugger", "champion" };
        var results = new (string Style, float Points, float Damage, float Against, float Stamina, float FullStamina, int Hits, int FullHits)[16];
        Parallel.For(0, results.Length, index =>
        {
            var name = styles[index / 4];
            var seed = 41 + (index % 4 / 2);
            var emptyRed = index % 2 == 0;
            var style = BotCatalog.Style(name);
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners(name,
                s => new IgnoreFatigue(new BoxerBot(style, s)), s => new IgnoreFatigue(new BoxerBot(style, s))));
            var round = 0;
            var damage = 0f;
            var against = 0f;
            var stamina = 0f;
            var fullStamina = 0f;
            var hits = 0;
            var fullHits = 0;
            for (var step = 0; step < 3600; step++)
            {
                if (round != game.Round)
                {
                    round = game.Round;
                    (emptyRed ? game.Player : game.Cpu).Stamina = 0f;
                }

                game.Step(default, 1f / 60f);
                foreach (var hit in game.RecentHits)
                {
                    if ((hit.Attacker == game.Player) == emptyRed)
                    {
                        damage += hit.Damage;
                        hits++;
                    }
                    else
                    {
                        against += hit.Damage;
                        fullHits++;
                    }
                }

                stamina += (emptyRed ? game.Player : game.Cpu).Stamina / 3600f;
                fullStamina += (emptyRed ? game.Cpu : game.Player).Stamina / 3600f;
            }

            var wins = emptyRed ? game.PlayerWins : game.CpuWins;
            var losses = emptyRed ? game.CpuWins : game.PlayerWins;
            var margin = MathF.Max(1f, 0.025f * (damage + against));
            var points = wins != losses ? (wins > losses ? 1f : 0f) : MathF.Abs(damage - against) <= margin ? 0.5f : damage > against ? 1f : 0f;
            results[index] = (name, points, damage, against, stamina, fullStamina, hits, fullHits);
        });

        var text = new StringBuilder("# スタミナ枯渇の対照試合\n\n");
        text.AppendLine("PhysicsVersion 29 / LogicVersion 26。各スタイル同士 4 試合 × 60 秒、種 41/42、赤青交替。両者のボットには SelfStamina=1 を見せ、休息判断を同条件で止める。体の疲れは両者とも有効。片側だけラウンド開始時の蓄えを 0 にする（維持はしない）。もう片側は通常の満タン開始。勝ち点は league と同じくラウンド勝利数、同数ならダメージ差で決める。\n");
        text.AppendLine("| スタイル | 枯渇開始の勝ち点 | 満タン開始の勝ち点 | 枯渇側ダメージ | 満タン側ダメージ | 枯渇側当たり | 満タン側当たり | 枯渇側平均蓄え | 満タン側平均蓄え |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var style in styles)
        {
            var rows = results.Where(r => r.Style == style).ToArray();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {style} | {rows.Average(r => r.Points):F2} | {1f - rows.Average(r => r.Points):F2} | {rows.Average(r => r.Damage):F1} | {rows.Average(r => r.Against):F1} | {rows.Average(r => r.Hits):F1} | {rows.Average(r => r.FullHits):F1} | {rows.Average(r => r.Stamina):F3} | {rows.Average(r => r.FullStamina):F3} |"));
        }

        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CLAUDE.md")))
        {
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }

        File.WriteAllText(Path.Combine(root, "docs/arena/gen13_stamina_empty_vs_full.md"), text.ToString());
        Console.WriteLine(text);
    }

    private sealed class IgnoreFatigue(BoxerBot bot) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => bot.Read(sense with { SelfStamina = 1f }, dt);
        public void OnPunchLanded(Hand hand, float damage) => bot.OnPunchLanded(hand, damage);
        public void OnPunchTaken(in HitEvent hit) => bot.OnPunchTaken(hit);
    }
}
