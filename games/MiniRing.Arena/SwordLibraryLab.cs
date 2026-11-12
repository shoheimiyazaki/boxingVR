using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 両手の剣の技の一覧を作って、戦わせる:
/// <list type="number">
/// <item>技を集める: 日本刀は剣道のモーションキャプチャの中の強い打ち（剣先が腕の長さの 15 倍 / s より速い）を、打つ前に止まっていた所から
/// 打ち終えて止まった所まで切り出す。ロングソードは型の打ち（<see cref="SwordForms.Longsword"/>）。</item>
/// <item>間合いを決める: 技の動きの中で剣先がいちばん速い時に物打ちが届く距離（一足一刀、<see cref="StrikeDrill.ReachOf"/>）。そこから構えて立つ相手へ出し、何を斬ったかを測る。
/// 名前は、いちばん斬った部位（頭なら面、前腕なら小手、胸・腹なら胴か突き）。</item>
/// <item>選ぶ: 剣ごとに、部位ごとにいちばん効いた技を残す（間合いが <see cref="MinReach"/> より短い技は除く）。</item>
/// <item>戦わせる: 日本刀同士・ロングソード同士・日本刀とロングソード、と拳の最強ボット。保つ間合いと、斬った量を数える。</item>
/// </list>
/// </summary>
internal sealed class SwordLibraryLab(int seed)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, IncludeFields = true };
    private static readonly float[] Ranges = [0.3f, 0.35f, 0.4f, 0.45f, 0.5f, 0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 0.8f];
    private const string Kendo = "data/mocap/sfu_kendo/";

    /// <summary>剣ごと・部位ごとに残す技の数（いちばん効いた順に。同じ部位でも違う形・間合いの技を持たせる）。</summary>
    private const int PerTarget = 3;

    /// <summary>残す技の、いちばん短い間合い [m]（胸と胸。人の大きさでおよそ 1 m）。</summary>
    private const float MinReach = 0.33f;

    /// <summary>保存する一覧: 剣ごとの構えのフレームと技。</summary>
    internal sealed record Library(Dictionary<WeaponKind, MotionFrame> Guards, List<MotionTechnique> Techniques);

    public string Run(string outPath, TextWriter log, int bouts, float seconds)
    {
        var watch = Stopwatch.StartNew();
        var rate = SwordStance.Rate;
        var tuning = SwordStance.Tuning;
        var report = new StringBuilder();
        report.AppendLine("# 両手の剣の技（swordlib）");
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"体の材質の強さは人形の相似則で人の {MiniRing.Voxels.BoxerAnatomy.Similitude:0.00} 倍（BoxerAnatomy.Doll）。間合いは、物打ち（鍔から切っ先までの先の 4 割）で斬ったボクセル / 60 + 脳しんとう − 自分を斬った分 / 60（決め手）で決める（鍔元や手で押し付けて削った分・押し付けで落とした腕は数えない）。"));
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"日本刀の技は SFU の剣道のモーションキャプチャ（data/mocap/sfu_kendo）から切り出した打ち、ロングソードの技は型の打ち。どれも両手持ちで、人の動きを腕の長さの比で縮め {rate:0.00} 倍の速さでなぞる。技の間合いは、技の動きの中で剣先がいちばん速い時に物打ちが届く距離（一足一刀: 人の剣士の打ち込む距離をそのまま縮めたもの）。構えて立つ相手へその間合い（と前後 5 cm）から 1 回ずつ出して、何を斬ったかを測った。効き = 失わせたボクセル / 60 + 切り落とし × 2 + 死 × 3 + 脳しんとう − 自分を斬った分 / 60。"));
        report.AppendLine();

        var guards = new Dictionary<WeaponKind, MotionFrame>();
        var candidates = new List<MotionTechnique>();
        var basic = BvhClip.Load(Kendo + "0015_BasicKendo001.bvh");
        guards[WeaponKind.Katana] = MotionRetarget.FromBvh(basic, "chudan", 6.0f, 6.1f).Frames[0];
        foreach (var file in new[] { "0015_BasicKendo001.bvh", "0015_Kirikaeshi001.bvh", "0015_KendoKata001.bvh" })
        {
            var clip = file == "0015_BasicKendo001.bvh" ? basic : BvhClip.Load(Kendo + file);
            var whole = MotionRetarget.FromBvh(clip, file, 0f, clip.Duration);
            foreach (var s in StrikeFinder.Find(whole).Where(s => s.PeakSpeed >= 15f))
            {
                var (from, to) = (MathF.Max(0f, s.Start - 0.3f), MathF.Min(clip.Duration, s.End + 0.35f));
                var name = string.Create(CultureInfo.InvariantCulture, $"{file[5..^7]}@{s.Start:0.00}");
                candidates.Add(new MotionTechnique(name, WeaponKind.Katana, MotionRetarget.FromBvh(clip, name, from, to), 0.5f));
            }
        }

        // 日本刀の型（突き・袈裟・逆袈裟・抜き胴・斬り上げ・払い面・引き面）も、モーションキャプチャの打ちと並べて試す。
        candidates.AddRange(SwordForms.Katana().Select(f => new MotionTechnique(f.Name, WeaponKind.Katana, f, 0.5f)));
        var forms = SwordForms.Longsword();
        guards[WeaponKind.Longsword] = forms[0].Frames[0];
        candidates.AddRange(forms.Skip(1).Select(f => new MotionTechnique(f.Name, WeaponKind.Longsword, f, 0.5f)));
        log.WriteLine($"candidates: {candidates.Count}");

        // 間合いを決める: 技の動きの物打ちが届く距離（一足一刀）。そこから出して、何を斬ったかを測る（前後 5 cm でも測って並べる）。
        float armReach;
        using (var probe = new BoxingGame(tuning))
        {
            armReach = probe.Player.ArmReach;
        }

        var tried = new List<(MotionTechnique Best, IReadOnlyList<StrikeResult> Results)>();
        foreach (var c in candidates)
        {
            var reach = StrikeDrill.ReachOf(c, armReach);
            var ranges = new[] { reach - 0.05f, reach, reach + 0.05f };
            var results = new StrikeResult[ranges.Length];
            Parallel.For(0, ranges.Length, i => results[i] = StrikeDrill.Run(c with { Range = ranges[i] }, guards[c.Weapon], rate, tuning));
            var at = results[1];
            var found = (Best: c with { Range = reach, Target = at.MostCut }, Results: (IReadOnlyList<StrikeResult>)results);
            tried.Add(found);
            var best = at;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Name}: best range {found.Best.Range:0.00} score {best.MaaiScore:0.00} monouchi {best.MonouchiCells} cells {best.Cells} part {best.MostCut} severed {best.Severed.Count} killed {best.Killed} ({watch.Elapsed.TotalSeconds:0}s)"));
        }

        report.AppendLine("## 技ごとの、間合いと効き");
        report.AppendLine();
        report.AppendLine("| 剣 | 技（切り出した所） | 一足一刀の間合い [cm] | 決め手 | 物打ちで斬った | 失わせたボクセル | いちばん斬った部位 | 切り落とし | 死 | 自分を斬った | 決め手（間合い −5 / ±0 / +5 cm） |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---|---|---|---:|---|");
        foreach (var (best, results) in tried)
        {
            var top = results[1];
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {Name(best.Weapon)} | {best.Name} | {best.Range * 100f:0} | {top.MaaiScore:0.00} | {top.MonouchiCells} | {top.Cells} | {top.MostCut?.ToString() ?? "—"} | {string.Join(",", top.Severed)} | {(top.Killed ? "○" : "")} | {top.SelfCells} | {string.Join(" ", results.Select(r => r.MaaiScore.ToString("0.0", CultureInfo.InvariantCulture)))} |"));
        }

        report.AppendLine();

        // 選ぶ: 剣ごと・部位ごとに、いちばん効いた技（効き 0.3 以上）。
        // 剣の間合いより内側（体が触れ合う近さ）から出す技は残さない: 人の剣士の打ち込みを縮めた間合いが MinReach より短い技は、
        // 打ち込まずにその場で振り下ろす素振りで、剣士同士の間合いの技ではない。
        var chosen = tried.Where(t => t.Best.Target is not null && t.Results[1].MaaiScore >= 0.3f && t.Best.Range >= MinReach)
            .GroupBy(t => (t.Best.Weapon, t.Best.Target))
            .SelectMany(g => g.OrderByDescending(t => t.Results[1].MaaiScore).Take(PerTarget).Select(t => t.Best))
            .Select(t => t with { Name = Title(t) + " / " + t.Name })
            .ToList();
        report.AppendLine("## 残した技");
        report.AppendLine();
        report.AppendLine("| 剣 | 技 | 狙う部位 | 間合い [cm] | 長さ [s]（なぞる速さで） |");
        report.AppendLine("|---|---|---|---:|---:|");
        foreach (var t in chosen)
        {
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {Name(t.Weapon)} | {t.Name} | {t.Target} | {t.Range * 100f:0} | {t.Motion.Duration / rate:0.00} |"));
        }

        report.AppendLine();
        var library = new Library(guards, chosen);
        File.WriteAllText(outPath, JsonSerializer.Serialize(library, Json));
        report.Append(Bouts(library, bouts, seconds, log));
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（かかった時間 {watch.Elapsed.TotalMinutes:0.0} 分）"));
        return report.ToString();
    }

    /// <summary>保存した一覧を読む。</summary>
    public static Library Load(string path) => JsonSerializer.Deserialize<Library>(File.ReadAllText(path), Json) ?? throw new JsonException("空の一覧です。");

    /// <summary>一覧を書き出す（<see cref="Load"/> で読める形）。</summary>
    public static void Save(string path, Library library) => File.WriteAllText(path, JsonSerializer.Serialize(library, Json));

    /// <summary>保存した一覧で、試合だけを回す。</summary>
    public string FightOnly(string path, TextWriter log, int bouts, float seconds) => Bouts(Load(path), bouts, seconds, log);

    private static string Name(WeaponKind kind) => kind == WeaponKind.Katana ? "日本刀" : kind == WeaponKind.Longsword ? "ロングソード" : kind.ToString();

    /// <summary>技の日本語の名前（狙う部位から）。</summary>
    private static string Title(MotionTechnique t) => t.Target switch
    {
        BodyPartKind.Head => "面",
        BodyPartKind.Forearm or BodyPartKind.UpperArm => "小手",
        BodyPartKind.Chest => t.Motion.Name.Contains("stich", StringComparison.Ordinal) ? "突き" : "胴（胸）",
        BodyPartKind.Pelvis => "胴（腹）",
        BodyPartKind.Thigh or BodyPartKind.Shin => "脚",
        _ => t.Target?.ToString() ?? "?",
    };

    /// <summary>剣の握り（右手）から先までの長さ [腕の長さ]（受けるとき、剣のどこで受けるか）。</summary>
    internal static float BladeLength(WeaponKind weapon) => (Weapon.TipOf(weapon).X - Weapon.TwoHandGripsOf(weapon).Top.X) / ArmReach.Value;

    private static readonly Lazy<float> ArmReach = new(() =>
    {
        using var game = new BoxingGame(SwordStance.Tuning);
        return game.Player.ArmReach;
    });

    /// <summary>剣士のボット（試合用）。武器を落としたら拳で戦う（<see cref="DisarmedFallback"/>）。</summary>
    internal static DisarmedFallback Bot(Library library, WeaponKind weapon, int seed, SwordDefense? defense = null, HitAndAway? hitAndAway = null, AimTuning? aim = null) =>
        DisarmedFallback.WithFists(new SwordsmanBot([.. library.Techniques.Where(t => t.Weapon == weapon)], library.Guards[weapon], seed, SwordStance.Rate,
            tuning: SwordStance.Tuning, defense: defense ?? SwordDefense.Adopted, bladeLength: BladeLength(weapon),
            spacing: weapon == WeaponKind.Longsword ? SwordForms.LongswordSpacing : SwordForms.KatanaSpacing, opponentReach: 3f, hitAndAway: hitAndAway, aim: aim)
        {
            // 狙いのドリル（aimlab）で技ごとの打てる部位を測ってあれば、相手の構えの空いた所を狙う。
            AutoAim = library.Techniques.Any(t => t.Weapon == weapon && t.Skill is not null),
        }, seed); // 腕 1 + 剣 2 弱

    /// <summary>1 試合の測ったもの。</summary>
    internal sealed record BoutMeasure(float MeanDistance, float CloseShare, int RedLost, int BlueLost, bool RedDied, bool BlueDied, List<string> RedUsed, List<float> RedUsedAt, List<string> BlueUsed, List<float> BlueUsedAt,
        int RedBlocks = 0, int RedEvasions = 0, int BlueBlocks = 0, int BlueEvasions = 0, bool RedDisarmed = false, bool BlueDisarmed = false);

    /// <summary>
    /// 剣士同士の 1 試合: 赤 <paramref name="red"/> の剣と青 <paramref name="blue"/> の剣（守り方はそれぞれ）。間合い（胸と胸）を 1 歩ごとに測る。
    /// </summary>
    internal static BoutMeasure Bout(Library library, WeaponKind red, WeaponKind blue, int seed, float seconds, Action<BoxingGame, float, SwordsmanBot?, SwordsmanBot?>? afterStep = null,
        SwordDefense? redDefense = null, SwordDefense? blueDefense = null)
    {
        DisarmedFallback? redBot = null, blueBot = null;
        var reds = new List<DisarmedFallback>();
        var blues = new List<DisarmedFallback>();
        DisarmedFallback Keep(List<DisarmedFallback> into, DisarmedFallback bot)
        {
            into.Add(bot);
            return bot;
        }

        using var game = new BoxingGame(SwordStance.Tuning, seed, new BoxingCorners("swordsman",
            s => blueBot = Keep(blues, Bot(library, blue, s, blueDefense)), s => redBot = Keep(reds, Bot(library, red, s, redDefense)),
            CpuWeapon: blue, PlayerWeapon: red, CpuTwoHanded: true, PlayerTwoHanded: true));
        var redStart = game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var blueStart = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        float sum = 0f, close = 0f;
        var count = 0;
        const float dt = 1f / 60f;
        static SwordsmanBot? Swordsman(DisarmedFallback? f) => f is { Disarmed: false } ? f.Armed as SwordsmanBot : null;
        for (var t = 0f; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += dt)
        {
            game.Step(default, dt);
            var d = game.Cpu.ChestPosition - game.Player.ChestPosition;
            d.Y = 0f;
            sum += d.Length();
            close += d.Length() < 0.3f ? 1f : 0f;
            count++;
            afterStep?.Invoke(game, t, Swordsman(redBot), Swordsman(blueBot));
        }

        // ラウンドごとに作り直したボットを合わせて数える。
        static List<SwordsmanBot> Swords(List<DisarmedFallback> bots) => [.. bots.Select(f => f.Armed).OfType<SwordsmanBot>()];
        var (rs, bs) = (Swords(reds), Swords(blues));
        return new BoutMeasure(sum / Math.Max(1, count), close / Math.Max(1, count),
            redStart - game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), blueStart - game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount),
            game.Player.IsDead, game.Cpu.IsDead, [.. rs.SelectMany(x => x.Used)], [.. rs.SelectMany(x => x.UsedAt)], [.. bs.SelectMany(x => x.Used)], [.. bs.SelectMany(x => x.UsedAt)],
            rs.Sum(x => x.Blocks), rs.Sum(x => x.Evasions), bs.Sum(x => x.Blocks), bs.Sum(x => x.Evasions), reds.Any(x => x.Disarmed), blues.Any(x => x.Disarmed));
    }

    private string Bouts(Library library, int bouts, float seconds, TextWriter log)
    {
        var text = new StringBuilder();
        text.AppendLine("## 戦わせると");
        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"各 {bouts} 試合 × {seconds:0} 秒（死んだら終わり）。間合いは胸と胸の水平の距離。"));
        text.AppendLine();
        text.AppendLine("| 組（赤 対 青） | 平均の間合い [cm] | 30 cm より近かった時間 | 赤が失ったボクセル（平均） | 青が失ったボクセル（平均） | 赤が死んだ | 青が死んだ | 受け（赤/青、平均） | 外し（赤/青、平均） | 武器を落とした（赤/青） |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|---|---|");
        var usage = new Dictionary<string, (int Count, float Distance)>();
        foreach (var (red, blue) in new[] { (WeaponKind.Katana, WeaponKind.Katana), (WeaponKind.Longsword, WeaponKind.Longsword), (WeaponKind.Katana, WeaponKind.Longsword) })
        {
            if (!library.Techniques.Any(t => t.Weapon == red) || !library.Techniques.Any(t => t.Weapon == blue))
            {
                text.AppendLine("| " + Name(red) + " 対 " + Name(blue) + " | （技が残らなかった剣があるので戦わせない） | | | | | |");
                log.WriteLine($"bouts {red} vs {blue} skipped: no techniques");
                continue;
            }

            var results = new BoutMeasure[bouts];
            Parallel.For(0, bouts, b => results[b] = Bout(library, red, blue, seed + b, seconds));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {Name(red)} 対 {Name(blue)} | {results.Average(r => r.MeanDistance) * 100f:0} | {results.Average(r => r.CloseShare) * 100f:0}% | {results.Average(r => r.RedLost):0} | {results.Average(r => r.BlueLost):0} | {results.Count(r => r.RedDied)} | {results.Count(r => r.BlueDied)} | {results.Average(r => r.RedBlocks):0.0} / {results.Average(r => r.BlueBlocks):0.0} | {results.Average(r => r.RedEvasions):0.0} / {results.Average(r => r.BlueEvasions):0.0} | {results.Count(r => r.RedDisarmed)} / {results.Count(r => r.BlueDisarmed)} |"));
            foreach (var r in results)
            {
                foreach (var (name, at) in r.RedUsed.Zip(r.RedUsedAt).Concat(r.BlueUsed.Zip(r.BlueUsedAt)))
                {
                    var u = usage.GetValueOrDefault(name);
                    usage[name] = (u.Count + 1, u.Distance + at);
                }
            }

            log.WriteLine($"bouts {red} vs {blue} done");
        }

        // 守りの効き: 日本刀同士で、片方だけ守る（コーナーを半分ずつ入れ替え）。
        var defended = new BoutMeasure[bouts];
        Parallel.For(0, bouts, k => defended[k] = k % 2 == 0
            ? Bout(library, WeaponKind.Katana, WeaponKind.Katana, seed + 500 + k, seconds, redDefense: SwordDefense.Adopted, blueDefense: SwordDefense.None)
            : Bout(library, WeaponKind.Katana, WeaponKind.Katana, seed + 500 + k, seconds, redDefense: SwordDefense.None, blueDefense: SwordDefense.Adopted));
        float mineLost = 0f, theirsLost = 0f;
        for (var k = 0; k < bouts; k++)
        {
            var r = defended[k];
            mineLost += k % 2 == 0 ? r.RedLost : r.BlueLost;
            theirsLost += k % 2 == 0 ? r.BlueLost : r.RedLost;
        }

        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"守りの効き（日本刀同士、片方だけ守る、{bouts} 試合）: 守る側が失ったボクセル 平均 {mineLost / bouts:0}、守らない側 {theirsLost / bouts:0}。"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"defense check: {mineLost / bouts:0} vs {theirsLost / bouts:0}"));
        text.AppendLine();
        text.AppendLine("出した技と、出した時の間合い（平均）:");
        text.AppendLine();
        text.AppendLine("| 技 | 出した回数 | 出した時の間合い [cm] |");
        text.AppendLine("|---|---:|---:|");
        foreach (var (name, (n, d)) in usage.OrderByDescending(kv => kv.Value.Count))
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {name} | {n} | {d / n * 100f:0} |"));
        }

        return text.ToString();
    }
}
