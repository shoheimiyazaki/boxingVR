using System.Globalization;
using System.Text;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 名簿のキャラクターの総当たり（docs/BOT_PDCA.md「体格に応じた戦い方」の物差し）。キャラクターの値（スタイル・武器・はめる物・体格・顔）は
/// <c>video --red-char</c> と同じ決め方（<see cref="VideoMaker.Corners"/> と <see cref="VideoMaker.WithCharacters"/>）。
/// <para>
/// 戦い方の量: 2 人の胸と胸の水平距離 d を、自分の腕の長さ L<sub>s</sub> と相手の腕の長さ L<sub>o</sub> で割って、
/// どの帯にいた時間が長いかを数える（両方が立っている間だけ）。届く長さは間合いの層の既定（<see cref="BotStyle.RangeThreat"/> = 1.2 倍）。
/// 自分だけ届く帯 = k·L<sub>o</sub> &lt; d ≤ k·L<sub>s</sub>、両方 = d ≤ k·min、相手だけ = k·L<sub>s</sub> &lt; d ≤ k·L<sub>o</sub>、どちらも届かない = d &gt; k·max。
/// 内側 = d ≤ 0.8·L<sub>o</sub>（相手の腕が窮屈な所）。
/// </para>
/// </summary>
internal static class RosterLeague
{
    /// <summary>
    /// 戦略の層（<see cref="BotStyle.GamePlan"/>）の使い方: 教育のまま（On）、全員から外す（Off）、A/B の層を入れた側だけに残す（Layered: <c>--ab on</c> の a の側）。
    /// </summary>
    internal enum GamePlanUse { On, Off, Layered }

    /// <summary>
    /// 重みつきの点（docs/BOTS.md「戦略の層」の物差し）: 1 試合の点 = 与えた側の点 − <see cref="TakenShare"/> × 受けた側の点。
    /// 側の点 = ダメージ + <see cref="KoPoints"/> × K.O.（取ったラウンド）+ <see cref="BreakPoints"/> × 骨折 + <see cref="SeverPoints"/> × 切断。
    /// 重みの根拠: 勝ちを決めているのはダウン（勝ち点率との相関 0.92、与えたダメージは 0.23。docs/ROSTER_REPORT.md の初回の考察）なので、
    /// K.O. は 1 試合の与えたダメージと同じ桁に重く置き、骨折・切断はその次（切断は取り返せないので骨折の倍）。
    /// 優先は 1) 相手に多く与える、2) 自分が受けるのを減らす、なので受けた側は半分の重み。
    /// </summary>
    internal const float KoPoints = 40f;
    internal const float BreakPoints = 15f;
    internal const float SeverPoints = 30f;
    internal const float TakenShare = 0.5f;

    private const float StepDt = 1f / 60f;

    /// <summary>内側とみなす距離（相手の腕の長さ倍）。長い腕が窮屈になる所（打ち始めの AttackRange 0.95 より内）。</summary>
    internal const float InsideFactor = 0.8f;

    /// <summary>「相手が弱った」とみなす相手の意識の薄れ（Fighter.Faintness）。測るためだけの本当の値（ボットはこれを読まない）。HurtCueProbe で構えが落ち始める所。</summary>
    internal const float ChanceFaint = 0.2f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>1 人ぶんの集計（全試合・両コーナーの合計）。</summary>
    internal sealed class Tally
    {
        public int Bouts, Wins, Draws, Losses, RoundsWon, Knockdowns, KnockdownsTaken;
        public int HitsDealt, HitsTaken;
        public int RoundsLost; // 相手が K.O. で取ったラウンド
        public int BonesBroken, BonesLost; // 相手の骨を折った（肉でつながったまま）・自分の骨を折られた（BoneBreakEvent）
        public int Severs, SeversTaken; // 相手の部位を落とした・自分の部位を落とされた（SeverEvent）

        /// <summary>与えた側の点（<see cref="KoPoints"/> などの重みつき）。</summary>
        public double GivenPoints => DamageDealt + (KoPoints * RoundsWon) + (BreakPoints * BonesBroken) + (SeverPoints * Severs);

        /// <summary>受けた側の点。</summary>
        public double TakenPoints => DamageTaken + (KoPoints * RoundsLost) + (BreakPoints * BonesLost) + (SeverPoints * SeversTaken);

        /// <summary>重みつきの点（与えた − <see cref="TakenShare"/> × 受けた）。</summary>
        public double WeightedScore => GivenPoints - (TakenShare * TakenPoints);
        public float DamageDealt, DamageTaken;
        public int Thrown, Landed;
        public bool Counted; // 手数を数えられる（拳のボット）
        public double StaminaSum;
        public long Samples, StaminaSamples;
        public double ArmRatioSum; // 試合ごとの 自分の腕 / 相手の腕 の総和
        public double RatioOwn, RatioOpp; // d / Ls, d / Lo の総和
        public long Neither, MineOnly, Both, OppOnly, Inside;
        public double ChanceSeconds, ChanceDamage, ChanceDistance; // 相手が弱った間（相手の意識が薄れて Faintness > ChanceFaint で、両方立っている）の時間・与えた量・d / 自分の腕 の総和（StepDt ごと）
        public long ChanceSamples;
        public double ChanceOpening; // 弱った間のボットの読み（BoxerBot.Opening）の総和（ChanceSamples ごと）

        public double Share(long n) => Samples > 0 ? (double)n / Samples : 0;
    }

    internal sealed record BoutOutcome(string Red, string Blue, int Winner, Tally RedTally, Tally BlueTally, double Seconds, string? Layered = null);

    public static BoutOutcome RunBout(FighterCharacter red, FighterCharacter blue, int seed, float seconds, bool referee, IReadOnlyDictionary<string, float>? set = null, bool redLayered = true, bool blueLayered = true, GamePlanUse gamePlan = GamePlanUse.On)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var redContender = ContenderOf(red, blue.Id, redLayered ? set : null, KeepsGamePlan(gamePlan, redLayered));
        var blueContender = ContenderOf(blue, red.Id, blueLayered ? set : null, KeepsGamePlan(gamePlan, blueLayered));
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var corners = VideoMaker.Corners(redContender, blueContender, seed, red.Wear, blue.Wear, red.Weapon, blue.Weapon,
            s => Keep(blueBots, blueContender.Create(s)), s => Keep(redBots, redContender.Create(s)), referee);
        corners = VideoMaker.WithCharacters(corners, red, blue);
        using var game = new BoxingGame(seed: seed, corners: corners);
        var redT = new Tally();
        var blueT = new Tally();
        var round = game.Round;
        var redDown = false;
        var blueDown = false;
        var threat = new BotStyle().RangeThreat;
        var steps = (int)MathF.Ceiling(seconds / StepDt);
        for (var i = 0; i < steps; i++)
        {
            game.Step(default, StepDt);
            var bothUp = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            var redChance = bothUp && game.Cpu.Faintness > ChanceFaint;
            var blueChance = bothUp && game.Player.Faintness > ChanceFaint;
            if (redChance || blueChance)
            {
                var dNow = Flat(game.Player.ChestPosition - game.Cpu.ChestPosition);
                Chance(redT, redChance, dNow / game.Player.ArmReach, OpeningOf(redBots));
                Chance(blueT, blueChance, dNow / game.Cpu.ArmReach, OpeningOf(blueBots));
            }

            foreach (var hit in game.RecentHits)
            {
                Add(hit.Attacker == game.Player ? redT : blueT, hit.Attacker == game.Player ? blueT : redT, hit.Damage);
                if (hit.Attacker == game.Player ? redChance : blueChance)
                {
                    (hit.Attacker == game.Player ? redT : blueT).ChanceDamage += hit.Damage;
                }
            }

            foreach (var hit in game.WeaponHits)
            {
                Add(hit.Attacker == game.Player ? redT : blueT, hit.Attacker == game.Player ? blueT : redT, hit.Damage);
                if (hit.Attacker == game.Player ? redChance : blueChance)
                {
                    (hit.Attacker == game.Player ? redT : blueT).ChanceDamage += hit.Damage;
                }
            }

            foreach (var broken in game.BrokenBones)
            {
                var (breaker, victim) = broken.Victim == game.Player ? (blueT, redT) : (redT, blueT);
                breaker.BonesBroken++;
                victim.BonesLost++;
            }

            foreach (var sever in game.RecentSevers)
            {
                if (sever.Victim is null)
                {
                    continue;
                }

                var (cutter, victim) = sever.Victim == game.Player ? (blueT, redT) : (redT, blueT);
                cutter.Severs++;
                victim.SeversTaken++;
            }

            if (game.Round != round)
            {
                round = game.Round;
                redDown = blueDown = false;
            }

            var blueNow = game.Cpu.IsKnockedDown;
            var redNow = game.Player.IsKnockedDown;
            redT.Knockdowns += !blueDown && blueNow ? 1 : 0;
            blueT.Knockdowns += !redDown && redNow ? 1 : 0;
            redT.KnockdownsTaken += !redDown && redNow ? 1 : 0;
            blueT.KnockdownsTaken += !blueDown && blueNow ? 1 : 0;
            redDown = redNow;
            blueDown = blueNow;

            if (game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing)
            {
                var d = Flat(game.Player.ChestPosition - game.Cpu.ChestPosition);
                Sample(redT, d, game.Player.ArmReach, game.Cpu.ArmReach, threat);
                Sample(blueT, d, game.Cpu.ArmReach, game.Player.ArmReach, threat);
            }

            if (i % 6 == 0)
            {
                redT.StaminaSum += game.Player.Stamina;
                blueT.StaminaSum += game.Cpu.Stamina;
                redT.StaminaSamples++;
                blueT.StaminaSamples++;
            }
        }

        redT.ArmRatioSum = game.Player.ArmReach / game.Cpu.ArmReach;
        blueT.ArmRatioSum = game.Cpu.ArmReach / game.Player.ArmReach;
        redT.RoundsWon = game.PlayerWins;
        blueT.RoundsWon = game.CpuWins;
        redT.RoundsLost = game.CpuWins;
        blueT.RoundsLost = game.PlayerWins;
        FillStats(redT, redBots);
        FillStats(blueT, blueBots);
        var result = new BoutResult(new BoutSpec(redContender, blueContender, seed, seconds), new CornerResult(game.PlayerWins, redT.DamageDealt, redT.HitsDealt, 0, redT.Knockdowns, new BotStats()),
            new CornerResult(game.CpuWins, blueT.DamageDealt, blueT.HitsDealt, 0, blueT.Knockdowns, new BotStats()));
        return new BoutOutcome(red.Id, blue.Id, result.Winner, redT, blueT, watch.Elapsed.TotalSeconds, redLayered == blueLayered ? null : redLayered ? red.Id : blue.Id);
    }

    /// <summary>
    /// キャラのボット: 教科書に教育を重ねた解決済みのスタイル（<see cref="FighterCharacter.Resolve(string)"/>。<paramref name="opponentId"/> はこの試合の相手のキャラ id、無ければ null）。
    /// <paramref name="set"/>（スタイルの値の上書き。BotStyle の名前 = 数値）があればその上にさらに重ねる（係数を探すとき）。
    /// 教育が空で <paramref name="set"/> も無ければ教科書そのもの（名前で引いたボットと同じ物）。
    /// <paramref name="keepGamePlan"/> が false なら、戦略の層（教育の <c>GamePlan</c>）を外す（層なしの比べもの: <c>--gameplan off|ab</c>）。
    /// </summary>
    internal static Contender ContenderOf(FighterCharacter character, string? opponentId, IReadOnlyDictionary<string, float>? set, bool keepGamePlan = true)
    {
        var resolved = character.Resolve(opponentId);
        var style = keepGamePlan || resolved.GamePlan is null ? resolved : resolved with { GamePlan = null };
        if (set is null || set.Count == 0)
        {
            return new Contender(style.Name, seed => new BoxerBot(style, seed));
        }

        return Contender.Of(StyleSet.Apply(style, set));
    }

    private static bool KeepsGamePlan(GamePlanUse use, bool layered) => use == GamePlanUse.On || (use == GamePlanUse.Layered && layered);

    private static IFighterControl Keep(List<IFighterControl> list, IFighterControl control)
    {
        list.Add(control);
        return control;
    }

    private static void Add(Tally attacker, Tally victim, float damage)
    {
        attacker.HitsDealt++;
        attacker.DamageDealt += damage;
        victim.HitsTaken++;
        victim.DamageTaken += damage;
    }

    private static float Flat(System.Numerics.Vector3 v) => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));

    private static float OpeningOf(List<IFighterControl> bots) => bots.Count > 0 && bots[^1] is BoxerBot b ? b.Opening : 0f;

    private static void Chance(Tally t, bool chance, float distanceOverReach, float opening)
    {
        if (!chance)
        {
            return;
        }

        t.ChanceSeconds += StepDt;
        t.ChanceDistance += distanceOverReach;
        t.ChanceSamples++;
        t.ChanceOpening += opening;
    }

    internal static void Sample(Tally t, float d, float own, float opp, float threat)
    {
        t.Samples++;
        t.RatioOwn += d / own;
        t.RatioOpp += d / opp;
        var mine = d <= threat * own;
        var theirs = d <= threat * opp;
        if (mine && theirs)
        {
            t.Both++;
        }
        else if (mine)
        {
            t.MineOnly++;
        }
        else if (theirs)
        {
            t.OppOnly++;
        }
        else
        {
            t.Neither++;
        }

        t.Inside += d <= InsideFactor * opp ? 1 : 0;
    }

    private static void FillStats(Tally t, List<IFighterControl> bots)
    {
        foreach (var bot in bots.OfType<BoxerBot>())
        {
            t.Counted = true;
            foreach (var c in bot.Stats.Techniques.Values)
            {
                t.Thrown += c.Thrown;
                t.Landed += c.Landed;
            }
        }
    }

    private static void Merge(Tally into, Tally from, int outcome)
    {
        into.Bouts++;
        into.Wins += outcome > 0 ? 1 : 0;
        into.Draws += outcome == 0 ? 1 : 0;
        into.Losses += outcome < 0 ? 1 : 0;
        into.RoundsWon += from.RoundsWon;
        into.Knockdowns += from.Knockdowns;
        into.KnockdownsTaken += from.KnockdownsTaken;
        into.HitsDealt += from.HitsDealt;
        into.HitsTaken += from.HitsTaken;
        into.RoundsLost += from.RoundsLost;
        into.BonesBroken += from.BonesBroken;
        into.BonesLost += from.BonesLost;
        into.Severs += from.Severs;
        into.SeversTaken += from.SeversTaken;
        into.DamageDealt += from.DamageDealt;
        into.DamageTaken += from.DamageTaken;
        into.Thrown += from.Thrown;
        into.Landed += from.Landed;
        into.Counted |= from.Counted;
        into.StaminaSum += from.StaminaSum;
        into.RatioOwn += from.RatioOwn;
        into.RatioOpp += from.RatioOpp;
        into.Samples += from.Samples;
        into.Neither += from.Neither;
        into.MineOnly += from.MineOnly;
        into.Both += from.Both;
        into.OppOnly += from.OppOnly;
        into.Inside += from.Inside;
        into.ChanceSeconds += from.ChanceSeconds;
        into.ChanceDamage += from.ChanceDamage;
        into.ChanceDistance += from.ChanceDistance;
        into.ChanceSamples += from.ChanceSamples;
        into.ChanceOpening += from.ChanceOpening;
        into.StaminaSamples += from.StaminaSamples;
        into.ArmRatioSum += from.ArmRatioSum;
    }

    public static string Run(IReadOnlyList<FighterCharacter> roster, int bouts, float seconds, int seed, bool referee, int parallel, TextWriter progress, IReadOnlyDictionary<string, float>? set = null, bool ab = false, string? onlyLayered = null, string? onlyPlain = null, string? matrixPath = null, GamePlanUse gamePlan = GamePlanUse.On)
    {
        var jobs = new List<(FighterCharacter Red, FighterCharacter Blue, int Seed, bool RedLayered, bool BlueLayered)>();
        for (var a = 0; a < roster.Count; a++)
        {
            for (var b = ab ? 0 : a + 1; b < roster.Count; b++)
            {
                if ((ab && a == b) || (ab && onlyLayered is not null && roster[a].Id != onlyLayered) || (ab && onlyPlain is not null && roster[b].Id != onlyPlain))
                {
                    continue;
                }

                // ab: 層を入れた a が、層を入れない b と戦う（組の順序つき。a の側がどのコーナーかは試合ごとに入れ替える）。
                for (var i = 0; i < bouts; i++)
                {
                    var s = seed + (1000 * a) + (37 * b) + (i / 2);
                    var aRed = ab ? (i + a) % 2 == 0 : i % 2 == 0;
                    jobs.Add(aRed ? (roster[a], roster[b], s, true, !ab) : (roster[b], roster[a], s, !ab, true));
                }
            }
        }

        var outcomes = new BoutOutcome[jobs.Count];
        var done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            outcomes[i] = RunBout(jobs[i].Red, jobs[i].Blue, jobs[i].Seed, seconds, referee, set, jobs[i].RedLayered, jobs[i].BlueLayered, gamePlan);
            var n = Interlocked.Increment(ref done);
            lock (progress)
            {
                progress.WriteLine(string.Create(Inv, $"[{n}/{jobs.Count}] {jobs[i].Red.Id} vs {jobs[i].Blue.Id}: {outcomes[i].Seconds:0.0}s (total {watch.Elapsed.TotalSeconds:0}s)"));
            }
        });
        if (matrixPath is not null)
        {
            File.WriteAllText(matrixPath, OutcomeCsv(jobs.Select(j => j.Seed).ToList(), outcomes));
        }

        return Report(roster, outcomes, bouts, seconds, seed, referee, logicVersion: BoxerBot.LogicVersion, set, gamePlan);
    }

    /// <summary>1 試合ずつの結果（組ごとの勝率を出すため）。winner は +1 = 赤、−1 = 青、0 = 引き分け。</summary>
    internal static string OutcomeCsv(IReadOnlyList<int> seeds, IReadOnlyList<BoutOutcome> outcomes)
    {
        var sb = new StringBuilder("red,blue,seed,winner,red_rounds,blue_rounds,red_damage,blue_damage,red_knockdowns,blue_knockdowns,red_hits,blue_hits,red_breaks,blue_breaks,red_severs,blue_severs,red_score,blue_score,layered\n"); // layered: A/B の組み方で層を入れた側（a）の id（同じ条件どうしなら空）
        for (var i = 0; i < outcomes.Count; i++)
        {
            var o = outcomes[i];
            sb.AppendLine(string.Create(Inv,
                $"{o.Red},{o.Blue},{seeds[i]},{o.Winner},{o.RedTally.RoundsWon},{o.BlueTally.RoundsWon},{o.RedTally.DamageDealt:0.0},{o.BlueTally.DamageDealt:0.0},{o.RedTally.Knockdowns},{o.BlueTally.Knockdowns},{o.RedTally.HitsDealt},{o.BlueTally.HitsDealt}," +
                $"{o.RedTally.BonesBroken},{o.BlueTally.BonesBroken},{o.RedTally.Severs},{o.BlueTally.Severs},{o.RedTally.WeightedScore:0.0},{o.BlueTally.WeightedScore:0.0},{o.Layered}"));
        }

        return sb.ToString();
    }

    internal static Dictionary<string, Tally> Totals(IReadOnlyList<FighterCharacter> roster, IEnumerable<BoutOutcome> outcomes)
    {
        var totals = roster.ToDictionary(c => c.Id, _ => new Tally());
        foreach (var o in outcomes)
        {
            if (o.Layered is null || o.Layered == o.Red)
            {
                Merge(totals[o.Red], o.RedTally, o.Winner);
            }

            if (o.Layered is null || o.Layered == o.Blue)
            {
                Merge(totals[o.Blue], o.BlueTally, -o.Winner);
            }
        }

        return totals;
    }

    internal static string Report(IReadOnlyList<FighterCharacter> roster, IReadOnlyList<BoutOutcome> outcomes, int bouts, float seconds, int seed, bool referee, int logicVersion, IReadOnlyDictionary<string, float>? set = null, GamePlanUse gamePlan = GamePlanUse.On)
    {
        var totals = Totals(roster, outcomes);
        var sb = new StringBuilder();
        var planLabel = gamePlan switch { GamePlanUse.On => "教育のまま", GamePlanUse.Off => "全員外す", _ => "A/B の層の側だけ" };
        sb.AppendLine(string.Create(Inv, $"### キャラクターの総当たり（各組 {bouts} 試合 × {seconds:0} 秒、種 {seed}、レフェリー {(referee ? "あり" : "なし")}、LogicVersion {logicVersion}、戦略の層 {planLabel}{(set is { Count: > 0 } ? "、スタイルの上書き " + string.Join(", ", set.Select(p => $"{p.Key}={p.Value}")) : "")}）"));
        if (outcomes.Any(o => o.Layered is not null))
        {
            sb.AppendLine();
            sb.AppendLine("A/B: 行のキャラだけに上書きを入れ、上書きを入れない全員（その相手）と戦った結果。勝敗・打撃・距離は上書きを入れた側だけの集計。");
        }

        sb.AppendLine();
        sb.AppendLine("体格は身長 / 腕 / 筋肉、重さは H³M²（`BodyBuild.MassRatio`）。勝率は勝ち 1・引き分け 0.5。");
        sb.AppendLine();
        sb.AppendLine("#### 成績");
        sb.AppendLine();
        sb.AppendLine("| # | キャラ | スタイル・装備 | 体格（身長/腕/筋肉）重さ | 勝-分-敗 | 勝率 | KO/RO ラウンド | ダウン 奪/被 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        var order = roster.OrderByDescending(c => Score(totals[c.Id])).ThenByDescending(c => totals[c.Id].DamageDealt - totals[c.Id].DamageTaken).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var c = order[i];
            var t = totals[c.Id];
            sb.AppendLine(string.Create(Inv,
                $"| {i + 1} | {c.Id} | {c.StyleName}・{c.EquipmentName} | {c.Build.Height:0.00}/{c.Build.Arms:0.00}/{c.Build.Muscle:0.00} {c.Build.MassRatio:0.00} | {t.Wins}-{t.Draws}-{t.Losses} | {Score(t):P0} | {t.RoundsWon} | {t.Knockdowns}/{t.KnockdownsTaken} |"));
        }

        sb.AppendLine();
        sb.AppendLine(Inv, $"#### 重みつきの点（1 試合あたり。点 = 与えた − {TakenShare:0.0} × 受けた、各側 = ダメージ + {KoPoints:0} × K.O. + {BreakPoints:0} × 骨折 + {SeverPoints:0} × 切断）");
        sb.AppendLine();
        sb.AppendLine("| キャラ | 点 | 与えた側 | 受けた側 | 与 ダメージ | 受 ダメージ | K.O. 取/被 | 骨折 取/被 | 切断 取/被 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var c in roster.OrderByDescending(c => totals[c.Id].WeightedScore / Math.Max(1, totals[c.Id].Bouts)))
        {
            var t = totals[c.Id];
            var n = (double)Math.Max(1, t.Bouts);
            sb.AppendLine(string.Create(Inv,
                $"| {c.Id} | {t.WeightedScore / n:0.0} | {t.GivenPoints / n:0.0} | {t.TakenPoints / n:0.0} | {t.DamageDealt / n:0.0} | {t.DamageTaken / n:0.0} | {t.RoundsWon}/{t.RoundsLost} | {t.BonesBroken}/{t.BonesLost} | {t.Severs}/{t.SeversTaken} |"));
        }

        sb.AppendLine();
        sb.AppendLine("#### 打撃（1 試合あたり。ApplyHit の数と量）と手数・スタミナ");
        sb.AppendLine();
        sb.AppendLine("| キャラ | 与 数 | 受 数 | 与 量 | 受 量 | 量の比 | 手数 | 当たった | 命中率 | スタミナ平均 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var c in order)
        {
            var t = totals[c.Id];
            var n = Math.Max(1, t.Bouts);
            var samples = (double)t.StaminaSamples;
            sb.AppendLine(string.Create(Inv,
                $"| {c.Id} | {(float)t.HitsDealt / n:0.0} | {(float)t.HitsTaken / n:0.0} | {t.DamageDealt / n:0} | {t.DamageTaken / n:0} | {t.DamageDealt / Math.Max(1f, t.DamageTaken):0.00} | " +
                $"{(t.Counted ? ((float)t.Thrown / n).ToString("0.0", Inv) : "-")} | {(t.Counted ? ((float)t.Landed / n).ToString("0.0", Inv) : "-")} | " +
                $"{(t.Counted && t.Thrown > 0 ? ((float)t.Landed / t.Thrown).ToString("P0", Inv) : "-")} | {(t.StaminaSum / Math.Max(1.0, samples)):0.00} |"));
        }

        sb.AppendLine();
        sb.AppendLine(Inv, $"#### 相手が弱った間（相手の意識が薄れて Faintness > {ChanceFaint:0.0}、両方立っている間。本当の値で測る: ボットはこれを読まない）");
        sb.AppendLine();
        sb.AppendLine("| キャラ | 弱った間 秒/試合 | 与えた量/秒（その間） | 平均 d/自分の腕（その間） | ボットの読んだ開き（その間） | 普段の与えた量/秒 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in order)
        {
            var t = totals[c.Id];
            var n = Math.Max(1, t.Bouts);
            sb.AppendLine(string.Create(Inv,
                $"| {c.Id} | {t.ChanceSeconds / n:0.0} | {(t.ChanceSeconds > 0 ? (t.ChanceDamage / t.ChanceSeconds).ToString("0.0", Inv) : "-")} | {(t.ChanceSamples > 0 ? (t.ChanceDistance / t.ChanceSamples).ToString("0.00", Inv) : "-")} | {(t.ChanceSamples > 0 ? (t.ChanceOpening / t.ChanceSamples).ToString("0.00", Inv) : "-")} | {t.DamageDealt / Math.Max(1.0, n * seconds):0.0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("#### 相手との距離（胸と胸の水平距離 d。両方が立っている間だけ）");
        sb.AppendLine();
        sb.AppendLine(Inv, $"届く長さ = 腕の長さ × {new BotStyle().RangeThreat:0.0}（間合いの層の既定）。帯は、自分だけ届く（相手の外）／両方届く／相手だけ届く／どちらも届かない。内側 = d ≤ {InsideFactor:0.0} × 相手の腕。");
        sb.AppendLine();
        sb.AppendLine("| キャラ | 腕の長さ比（自分/相手の平均） | 平均 d/自分の腕 | 平均 d/相手の腕 | 自分だけ届く | 両方届く | 相手だけ届く | どちらも届かない | 内側 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var c in order)
        {
            var t = totals[c.Id];
            sb.AppendLine(string.Create(Inv,
                $"| {c.Id} | {t.ArmRatioSum / Math.Max(1, t.Bouts):0.00} | {t.RatioOwn / Math.Max(1L, t.Samples):0.00} | {t.RatioOpp / Math.Max(1L, t.Samples):0.00} | " +
                $"{t.Share(t.MineOnly):P0} | {t.Share(t.Both):P0} | {t.Share(t.OppOnly):P0} | {t.Share(t.Neither):P0} | {t.Share(t.Inside):P0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("#### 組ごとの勝ち（行のキャラが列のキャラに取った勝ち点 / 試合数。勝ち 1・引き分け 0.5）");
        sb.AppendLine();
        sb.AppendLine("| | " + string.Join(" | ", order.Select(c => c.Id)) + " |");
        sb.AppendLine("|---|" + string.Concat(order.Select(_ => "---|")));
        foreach (var a in order)
        {
            sb.Append(Inv, $"| {a.Id} |");
            foreach (var b in order)
            {
                if (a.Id == b.Id)
                {
                    sb.Append(" - |");
                    continue;
                }

                var games = outcomes.Where(o => ((o.Red == a.Id && o.Blue == b.Id) || (o.Red == b.Id && o.Blue == a.Id)) && (o.Layered is null || o.Layered == a.Id)).ToList();
                var points = games.Sum(o => (o.Red == a.Id ? o.Winner : -o.Winner) switch { > 0 => 1.0, 0 => 0.5, _ => 0.0 });
                sb.Append(string.Create(Inv, $" {points:0.#}/{games.Count} |"));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static double Score(Tally t) => t.Bouts > 0 ? (t.Wins + (0.5 * t.Draws)) / t.Bouts : 0;
}

/// <summary>BotStyle の数値の上書き（名前 = 値）。係数を探すときに、スタイルの JSON を経由して作り直す。</summary>
internal static class StyleSet
{
    public static BotStyle Apply(BotStyle style, IReadOnlyDictionary<string, float> set)
    {
        if (set.Count == 0)
        {
            return style;
        }

        var node = System.Text.Json.Nodes.JsonNode.Parse(style.ToJson())!.AsObject();
        foreach (var (key, value) in set)
        {
            if (!node.ContainsKey(key))
            {
                throw new ArgumentException($"BotStyle に '{key}' はありません。");
            }

            node[key] = value;
        }

        return BotStyle.FromJson(node.ToJsonString());
    }
}
