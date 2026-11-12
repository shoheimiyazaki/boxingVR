using System.Globalization;
using System.Text;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 武器ごとの練習試合: 拳（ボクセルのグローブ）・棍棒（片手）・バット（両手の棍棒）・日本刀・ロングソードの戦い手を総当たりで戦わせ、
/// 組ごとの勝ち負け・ダウン・ダメージを並べる。武器の戦い手は、武器を落としたら拳で戦う（<see cref="DisarmedFallback"/>）。
/// </summary>
internal static class VersusLab
{
    /// <summary>練習試合の体の調整: 既定の体に、両手で柄を締めた剣の固い手首（30 Hz）だけを足す（足の運びはボクサーのまま）。</summary>
    public static FighterTuning Tuning { get; } = FighterTuning.Default with { WristServoHz = SwordStance.Tuning.WristServoHz };

    /// <summary>戦い手（名前で選ぶ）。<paramref name="hitAndAway"/> を渡すと、剣士（日本刀・ロングソード）は間合いを読んで打って離れる。</summary>
    public static IReadOnlyList<Contender> Contenders(string swordLibrary, string clubSwings, string batSwings, SwordDefense? defense = null, HitAndAway? hitAndAway = null, AimTuning? aim = null)
    {
        var library = SwordLibraryLab.Load(swordLibrary);
        var club = SwingLab.Load(clubSwings);
        var bat = SwingLab.Load(batSwings);
        return
        [
            Contender.Named("champion") with { Name = "拳" },
            new("棍棒", s => new ClubBot([.. club.Select(c => c.Swing)], s, club.Average(c => c.Range), rest: 0.6f), WeaponKind.WoodenClub),
            new("バット", s => new ClubBot([.. bat.Select(c => c.Swing)], s, bat.Average(c => c.Range), rest: 0.6f), WeaponKind.WoodenClub, TwoHanded: true),
            new("日本刀", s => SwordLibraryLab.Bot(library, WeaponKind.Katana, s, defense, hitAndAway, aim).Armed, WeaponKind.Katana, TwoHanded: true),
            new("ロングソード", s => SwordLibraryLab.Bot(library, WeaponKind.Longsword, s, defense, hitAndAway, aim).Armed, WeaponKind.Longsword, TwoHanded: true),
        ];
    }

    /// <summary>総当たりを回し、Markdown の表を返す。</summary>
    public static string Run(IReadOnlyList<Contender> contenders, int bouts, float seconds, int seed, TextWriter log)
    {
        var specs = BotArena.RoundRobin(contenders, bouts, seconds, seed);
        log.WriteLine($"versus: {specs.Count} bouts");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var results = BotArena.RunAll(specs, Tuning);
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"各組 {bouts} 試合 × {seconds:0} 秒（コーナーを半分ずつ入れ替え）。勝ち = KO で取ったラウンド、同じならダメージ。"));
        text.AppendLine();
        text.AppendLine("| 組（A 対 B） | A の勝ち | 負け | 分け | A が取ったダウン | B が取ったダウン | A のダメージ | B のダメージ |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        var standings = contenders.ToDictionary(c => c.Name, _ => (Win: 0, Loss: 0, Draw: 0));
        var order = contenders.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i);
        foreach (var pair in results.GroupBy(r => order[r.Spec.Red.Name] < order[r.Spec.Blue.Name] ? (A: r.Spec.Red.Name, B: r.Spec.Blue.Name) : (A: r.Spec.Blue.Name, B: r.Spec.Red.Name)))
        {
            int win = 0, loss = 0, draw = 0, downsA = 0, downsB = 0;
            float damageA = 0f, damageB = 0f;
            foreach (var r in pair)
            {
                var aRed = r.Spec.Red.Name == pair.Key.A;
                var sign = aRed ? r.Winner : -r.Winner;
                win += sign > 0 ? 1 : 0;
                loss += sign < 0 ? 1 : 0;
                draw += sign == 0 ? 1 : 0;
                var (a, b) = aRed ? (r.Red, r.Blue) : (r.Blue, r.Red);
                downsA += a.Knockdowns;
                downsB += b.Knockdowns;
                damageA += a.DamageDealt;
                damageB += b.DamageDealt;
            }

            var sa = standings[pair.Key.A];
            standings[pair.Key.A] = (sa.Win + win, sa.Loss + loss, sa.Draw + draw);
            var sb = standings[pair.Key.B];
            standings[pair.Key.B] = (sb.Win + loss, sb.Loss + win, sb.Draw + draw);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {pair.Key.A} 対 {pair.Key.B} | {win} | {loss} | {draw} | {downsA} | {downsB} | {damageA:0} | {damageB:0} |"));
        }

        text.AppendLine();
        text.AppendLine("| 戦い手 | 勝ち | 負け | 分け |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (var (name, s) in standings.OrderByDescending(kv => kv.Value.Win - kv.Value.Loss))
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {name} | {s.Win} | {s.Loss} | {s.Draw} |"));
        }

        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（{watch.Elapsed.TotalMinutes:0.0} 分）"));
        return text.ToString();
    }
}
