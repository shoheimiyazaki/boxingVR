using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// VR の記録から選んだ振り（<c>docs/vrmotion/katana_&lt;型&gt;_&lt;順&gt;_*.vrmotion</c>）を、刀の剣士の型として試合で使ってみる（<c>vrforms</c>）。
/// 教科書の型のままの剣士と、選んだ型を VR の振り（<see cref="VrMotionForms"/>）に差し替えた剣士を、色を入れ替えて種ごとに戦わせ、
/// 失った体のセル・死・技の使われ方を比べる。体・物理・判断は同じで、違うのは技の目標の経路だけ（§1.1・§1.2）。
/// </summary>
internal sealed class VrFormsLab(int seed, int bouts, float seconds, IReadOnlyList<string>? only = null, bool confirm = false)
{
    private const WeaponKind Katana = WeaponKind.Katana;

    private static readonly bool[] Both = [false, true];

    internal sealed record Side(string Name, IReadOnlyList<MotionTechnique> Techniques);

    internal sealed record Result(int Seed, bool VrIsRed, int VrLost, int TextbookLost, bool VrDied, bool TextbookDied, List<string> VrUsed, List<string> TextbookUsed);

    /// <summary>教科書の型の技と、VR の振りに差し替えた技（差し替えた型の名前も）。</summary>
    internal static (Side Textbook, Side Vr, IReadOnlyList<string> Replaced) Sides(string dir, int rank, FighterTuning tuning, float armReach, IReadOnlyList<string>? only = null)
    {
        var style = WeaponCatalog.Textbook(Katana);
        var minReach = style.InnerMinReach ?? style.MinReach;
        var textbook = FormSwordsman.Techniques(Katana, armReach, minReach);
        var guard = FormSwordsman.Guard(Katana);
        var replaced = new List<string>();
        var vr = new List<MotionTechnique>();
        foreach (var form in FormSwordsman.Forms(Katana))
        {
            var file = only is { Count: > 0 } && !only.Contains(form.Name) ? null
                : Directory.GetFiles(dir, $"katana_{form.Name}_{rank}_*.vrmotion").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            var motion = form;
            if (file is not null)
            {
                motion = VrMotionForms.ToSwordMotion(VrMotionClip.Load(file), form.Name, guard, tuning.WalkSpeed, armReach, SwordStance.Rate, tuning.MaxTorsoTwistRadians);
                replaced.Add($"{form.Name} ← {Path.GetFileName(file)}");
            }

            // 教科書と同じ作り方（間合いはその型の動きで物打ちが届く距離、下限より短い型は除く）。
            var technique = new MotionTechnique(form.Name, Katana, motion, 1.408f, Skill: SwordFormSkills.For(Katana, form.Name));
            technique = technique with { Range = StrikeDrill.ReachOf(technique, armReach) };
            if (technique.Range >= minReach)
            {
                vr.Add(technique);
            }
        }

        return (new Side("教科書", textbook), new Side("VR", vr), replaced);
    }

    internal static DisarmedFallback Bot(Side side, int s, FighterTuning tuning, float armReach) =>
        FormSwordsman.Bot(Katana, s, armReach, tuning, WeaponCatalog.Textbook(Katana), techniques: side.Techniques);

    internal static BoxingCorners Corners(Side red, Side blue, FighterTuning tuning, float armReach, List<DisarmedFallback> reds, List<DisarmedFallback> blues) =>
        new BoxingCorners("vrforms",
            s => { var b = Bot(blue, s, tuning, armReach); blues.Add(b); return b; },
            s => { var b = Bot(red, s, tuning, armReach); reds.Add(b); return b; },
            CpuWeapon: Katana, PlayerWeapon: Katana, CpuTwoHanded: true, PlayerTwoHanded: true).StartingAtCorners();

    private static int Cells(Fighter f) => f.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);

    private static List<string> Used(List<DisarmedFallback> bots) => [.. bots.Select(b => b.Armed).OfType<SwordsmanBot>().SelectMany(b => b.Used)];

    internal Result Bout(Side textbook, Side vr, int s, bool vrIsRed, FighterTuning tuning, float armReach)
    {
        var reds = new List<DisarmedFallback>();
        var blues = new List<DisarmedFallback>();
        var (red, blue) = vrIsRed ? (vr, textbook) : (textbook, vr);
        using var game = new BoxingGame(tuning, s, Corners(red, blue, tuning, armReach, reds, blues));
        var (redStart, blueStart) = (Cells(game.Player), Cells(game.Cpu));
        const float dt = 1f / 60f;
        for (var t = 0f; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += dt)
        {
            game.Step(default, dt);
        }

        var (redLost, blueLost) = (redStart - Cells(game.Player), blueStart - Cells(game.Cpu));
        return vrIsRed
            ? new Result(s, true, redLost, blueLost, game.Player.IsDead, game.Cpu.IsDead, Used(reds), Used(blues))
            : new Result(s, false, blueLost, redLost, game.Cpu.IsDead, game.Player.IsDead, Used(blues), Used(reds));
    }

    /// <summary>
    /// 自分を斬る量: 構えて動かない相手（<see cref="GuardHold"/>、打たない）と試合の秒数だけ戦わせ、剣士が失った体のセル。相手は打たないので、全部が自分の刃
    /// （か床・相手の刀への当たりで跳ね返った自分の刃）による。種 <paramref name="s"/>。
    /// </summary>
    internal (int SelfLost, int OpponentLost, List<string> Used) SelfCut(Side side, int s, FighterTuning tuning, float armReach)
    {
        var bots = new List<DisarmedFallback>();
        using var game = new BoxingGame(tuning, s, new BoxingCorners("vrforms-self",
            _ => new GuardHold(Katana, tuning),
            x => { var b = Bot(side, x, tuning, armReach); bots.Add(b); return b; },
            CpuWeapon: Katana, PlayerWeapon: Katana, CpuTwoHanded: true, PlayerTwoHanded: true).StartingAtCorners());
        var (start, opponentStart) = (Cells(game.Player), Cells(game.Cpu));
        const float dt = 1f / 60f;
        for (var t = 0f; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += dt)
        {
            game.Step(default, dt);
        }

        return (start - Cells(game.Player), opponentStart - Cells(game.Cpu), Used(bots));
    }

    public string Run(string dir, int rank, TextWriter log)
    {
        var tuning = FighterTuning.Default;
        float armReach;
        using (var probe = new BoxingGame(tuning))
        {
            armReach = probe.Player.ArmReach;
        }

        var (textbook, vr, replaced) = Sides(dir, rank, tuning, armReach, only);
        var jobs = Enumerable.Range(0, bouts).SelectMany(b => new[] { (Seed: seed + b, VrIsRed: true), (Seed: seed + b, VrIsRed: false) }).ToArray();
        var results = new Result[jobs.Length];
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            i => results[i] = Bout(textbook, vr, jobs[i].Seed, jobs[i].VrIsRed, tuning, armReach));
        var selfJobs = Enumerable.Range(0, bouts).SelectMany(b => new[] { (Seed: seed + b, Vr: true), (Seed: seed + b, Vr: false) }).ToArray();
        var selfResults = new (int SelfLost, int OpponentLost, List<string> Used)[selfJobs.Length];
        Parallel.For(0, selfJobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            i => selfResults[i] = SelfCut(selfJobs[i].Vr ? vr : textbook, selfJobs[i].Seed, tuning, armReach));
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"## 教科書の型 対 VR の振りの型（刀の剣士どうし、種 {seed}〜{seed + bouts - 1} × 色を入れ替えて {jobs.Length} 試合 × {seconds:0} 秒、死んだら終わり）"));
        text.AppendLine();
        text.AppendLine("差し替えた型（`docs/vrmotion/` の順 " + rank + "）: " + string.Join("・", replaced));
        text.AppendLine();
        text.AppendLine("| | VR の振り | 教科書 |");
        text.AppendLine("|---|---:|---:|");
        static string Mean(IEnumerable<int> x) => x.Average().ToString("0", CultureInfo.InvariantCulture);
        text.AppendLine(CultureInfo.InvariantCulture, $"| 失ったセル（平均） | {Mean(results.Select(r => r.VrLost))} | {Mean(results.Select(r => r.TextbookLost))} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| 死んだ | {results.Count(r => r.VrDied)} | {results.Count(r => r.TextbookDied)} |");
        var vrBetter = results.Count(r => r.TextbookLost > r.VrLost);
        text.AppendLine(CultureInfo.InvariantCulture, $"| 相手より失ったセルが少なかった試合 | {vrBetter} | {results.Count(r => r.VrLost > r.TextbookLost)} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| 技を出した数（平均） | {results.Average(r => r.VrUsed.Count):0.0} | {results.Average(r => r.TextbookUsed.Count):0.0} |");
        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"自分を斬る量（構えて動かない相手 `GuardHold` と {seconds:0} 秒。相手は打たないので、剣士が失ったセルは自分の刃による）:"));
        text.AppendLine();
        text.AppendLine("| | VR の振り | 教科書 |");
        text.AppendLine("|---|---:|---:|");
        static IEnumerable<T> Pick<T>(IEnumerable<T> all, IEnumerable<bool> mask, bool want) => all.Zip(mask).Where(p => p.Second == want).Select(p => p.First);
        var mask = selfJobs.Select(j => j.Vr).ToArray();
        text.AppendLine(CultureInfo.InvariantCulture, $"| 自分が失ったセル（平均） | {Mean(Pick(selfResults, mask, true).Select(r => r.SelfLost))} | {Mean(Pick(selfResults, mask, false).Select(r => r.SelfLost))} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| 自分を斬った試合 | {Pick(selfResults, mask, true).Count(r => r.SelfLost > 0)} / {bouts} | {Pick(selfResults, mask, false).Count(r => r.SelfLost > 0)} / {bouts} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| 動かない相手が失ったセル（平均） | {Mean(Pick(selfResults, mask, true).Select(r => r.OpponentLost))} | {Mean(Pick(selfResults, mask, false).Select(r => r.OpponentLost))} |");
        text.AppendLine();
        text.AppendLine("技ごとの出した数（全試合の合計）:");
        text.AppendLine();
        text.AppendLine("| 型 | VR の振り | 教科書 | VR の間合い [m] | 教科書の間合い [m] |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var form in FormSwordsman.Forms(Katana).Select(f => f.Name))
        {
            var v = results.Sum(r => r.VrUsed.Count(u => u == form));
            var t = results.Sum(r => r.TextbookUsed.Count(u => u == form));
            var vRange = vr.Techniques.FirstOrDefault(x => x.Name == form)?.Range;
            var tRange = textbook.Techniques.FirstOrDefault(x => x.Name == form)?.Range;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {form} | {v} | {t} | {(vRange is { } a ? a.ToString("0.00", CultureInfo.InvariantCulture) : "—")} | {(tRange is { } b ? b.ToString("0.00", CultureInfo.InvariantCulture) : "—")} |"));
        }

        text.AppendLine();
        text.AppendLine("| 種 | VR の色 | VR が失ったセル | 教科書が失ったセル | 死 |");
        text.AppendLine("|---:|---|---:|---:|---|");
        foreach (var r in results)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {r.Seed} | {(r.VrIsRed ? "赤" : "青")} | {r.VrLost} | {r.TextbookLost} | {(r.VrDied ? "VR" : r.TextbookDied ? "教科書" : "")} |");
        }

        if (confirm)
        {
            // 比べ直しの台帳（docs/arena/confirm.jsonl）: 種ごとに 2 色の試合を平均して 1 対にし（同じ種の 2 試合は独立でない）、失ったセル（少ないほど良い）を比べる。
            var bySeed = results.GroupBy(r => r.Seed).OrderBy(g => g.Key).ToArray();
            var a = bySeed.Select(g => g.Average(r => (double)r.VrLost)).ToArray();
            var b = bySeed.Select(g => g.Average(r => (double)r.TextbookLost)).ToArray();
            ConfirmLog.Append("vrforms", "失ったセル（少ないほど良い、種ごとに 2 色の平均）", "VR の振りの型（" + (only is { Count: > 0 } ? string.Join(",", only) : "全部") + "）", "教科書の型",
                [.. bySeed.Select(g => g.Key)], PairedEvaluation.Compute(a, b));
        }

        log.WriteLine($"vrforms: {jobs.Length} bouts, VR lost fewer cells in {vrBetter}");
        return text.ToString();
    }

    /// <summary>
    /// キャラで比べる（<c>--char jin --forms gyaku-kesa=katana_gyaku-kesa_1_…,…</c>）: そのキャラのまま（A = 今の教育）と、教育の <c>weaponBot</c> に
    /// <see cref="WeaponStyle.VrForms"/> を重ねた版（B）を、名簿の相手それぞれと同じ種・同じ色で戦わせる（名簿の試合と同じ道筋 <see cref="RosterLeague.RunBout"/>）。
    /// 勝ち = 1・引き分け = 0.5・負け = 0 と、名簿の点（与えた − 受けた の重み付き）を、相手 × 種ごとに 2 色を平均して対にして比べる。
    /// </summary>
    public string RunCharacter(string characterId, IReadOnlyDictionary<string, string> vrForms, IReadOnlyList<string> opponents, bool referee, TextWriter log)
    {
        var baseChar = MiniRing.Characters.CharacterRoster.Get(characterId);
        var weaponBot = baseChar.Education.WeaponBot?.DeepClone().AsObject() ?? new System.Text.Json.Nodes.JsonObject();
        var forms = new System.Text.Json.Nodes.JsonObject();
        foreach (var (form, id) in vrForms)
        {
            forms[form] = id;
        }

        weaponBot["VrForms"] = forms;
        var variant = baseChar with { Education = baseChar.Education with { WeaponBot = weaponBot } };
        _ = variant.WeaponStyle; // 教育を重ねられるか（鍵・名前・組み込みの振り）を先に確かめる
        var foes = opponents.Count > 0 ? [.. opponents.Select(MiniRing.Characters.CharacterRoster.Get)]
            : MiniRing.Characters.CharacterRoster.All.Where(c => c.Id != characterId).ToList();
        var jobs = (from foe in foes
                    from b in Enumerable.Range(0, bouts)
                    from red in Both
                    from useVr in Both
                    select (Foe: foe, Seed: seed + b, Red: red, Vr: useVr)).ToArray();
        var outcomes = new (double Points, double Score)[jobs.Length];
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, i =>
        {
            var (foe, s, red, useVr) = jobs[i];
            var me = useVr ? variant : baseChar;
            var o = red ? RosterLeague.RunBout(me, foe, s, seconds, referee) : RosterLeague.RunBout(foe, me, s, seconds, referee);
            var mine = red ? o.RedTally : o.BlueTally;
            var winner = red ? o.Winner : -o.Winner;
            outcomes[i] = (winner > 0 ? 1 : winner == 0 ? 0.5 : 0, mine.WeightedScore);
        });

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"## {baseChar.HudName}（{characterId}）: 今の教育（A）対 遠間の型を VR の振りにした教育（B）");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"相手 {foes.Count} 人 × 種 {seed}〜{seed + bouts - 1} × 色を入れ替えて、A・B それぞれ {jobs.Length / 2} 試合（{seconds:0} 秒、レフェリー {(referee ? "あり" : "なし")}）。名簿の試合と同じ道筋（`RosterLeague.RunBout`）。");
        text.AppendLine();
        text.AppendLine("差し替えた型: " + string.Join("・", vrForms.Select(p => $"{p.Key} ← {p.Value}")));
        text.AppendLine();
        text.AppendLine("| 相手 | A 勝ち/分け/負け | B 勝ち/分け/負け | A 名簿の点（平均） | B 名簿の点（平均） |");
        text.AppendLine("|---|---|---|---:|---:|");
        string Wdl(IEnumerable<int> idx)
        {
            var l = idx.Select(k => outcomes[k].Points).ToList();
            return string.Create(CultureInfo.InvariantCulture, $"{l.Count(x => x == 1)}/{l.Count(x => x == 0.5)}/{l.Count(x => x == 0)}");
        }

        foreach (var foe in foes)
        {
            var a = Enumerable.Range(0, jobs.Length).Where(k => jobs[k].Foe.Id == foe.Id && !jobs[k].Vr).ToList();
            var b = Enumerable.Range(0, jobs.Length).Where(k => jobs[k].Foe.Id == foe.Id && jobs[k].Vr).ToList();
            text.AppendLine(CultureInfo.InvariantCulture, $"| {foe.HudName}（{foe.Id}） | {Wdl(a)} | {Wdl(b)} | {a.Average(k => outcomes[k].Score):0} | {b.Average(k => outcomes[k].Score):0} |");
        }

        // 相手 × 種ごとに、2 色の平均で 1 対（A, B）。
        var pairs = jobs.Select((j, k) => (j, k)).GroupBy(x => (x.j.Foe.Id, x.j.Seed)).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.Seed)
            .Select(g => (A: g.Where(x => !x.j.Vr).Select(x => outcomes[x.k]).ToList(), B: g.Where(x => x.j.Vr).Select(x => outcomes[x.k]).ToList(), g.Key.Seed)).ToList();
        var pointsA = pairs.Select(p => p.A.Average(x => x.Points)).ToArray();
        var pointsB = pairs.Select(p => p.B.Average(x => x.Points)).ToArray();
        var scoreA = pairs.Select(p => p.A.Average(x => x.Score)).ToArray();
        var scoreB = pairs.Select(p => p.B.Average(x => x.Score)).ToArray();
        var winResult = PairedEvaluation.Compute(pointsB, pointsA);
        var scoreResult = PairedEvaluation.Compute(scoreB, scoreA);
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"勝ち点（勝ち 1・分け 0.5、相手 × 種の {pairs.Count} 対）: A {pointsA.Average():0.000}・B {pointsB.Average():0.000}、B − A {winResult.MeanDiff:+0.000;-0.000;0.000}（95 % 区間 {winResult.CiLow:+0.000;-0.000;0.000}〜{winResult.CiHigh:+0.000;-0.000;0.000}、符号 p {winResult.SignP:0.00}、{winResult.Verdict}）");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"名簿の点: A {scoreA.Average():0}・B {scoreB.Average():0}、B − A {scoreResult.MeanDiff:+0;-0;0}（95 % 区間 {scoreResult.CiLow:+0;-0}〜{scoreResult.CiHigh:+0;-0}、符号 p {scoreResult.SignP:0.00}、{scoreResult.Verdict}）");
        if (confirm)
        {
            var seeds = pairs.Select(p => p.Seed).ToArray();
            var b = "教育 + VR の振り（" + string.Join(",", vrForms.Keys) + "）";
            ConfirmLog.Append("vrforms", $"{characterId} の勝ち点（相手 × 種、2 色の平均）", b, "今の教育", seeds, winResult);
            ConfirmLog.Append("vrforms", $"{characterId} の名簿の点（相手 × 種、2 色の平均）", b, "今の教育", seeds, scoreResult);
        }

        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"vrforms --char {characterId}: points A {pointsA.Average():0.000} B {pointsB.Average():0.000} ({winResult.Verdict}), score A {scoreA.Average():0} B {scoreB.Average():0} ({scoreResult.Verdict})"));
        return text.ToString();
    }

    /// <summary>1 試合の動画（赤 = VR の振り、青 = 教科書）。両者の真ん中を横から映す。</summary>
    public void Video(string dir, int rank, int s, string outPath, int width, int height, int fps, float slow, TextWriter log)
    {
        var tuning = FighterTuning.Default;
        float armReach;
        using (var probe = new BoxingGame(tuning))
        {
            armReach = probe.Player.ArmReach;
        }

        var (textbook, vr, _) = Sides(dir, rank, tuning, armReach, only);
        var reds = new List<DisarmedFallback>();
        var blues = new List<DisarmedFallback>();
        using var game = new BoxingGame(tuning, s, Corners(vr, textbook, tuning, armReach, reds, blues));
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 23 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        const float dt = 1f / 240f;
        var perFrame = 1f / (fps * slow);
        var next = 0f;
        var (redStart, blueStart) = (Cells(game.Player), Cells(game.Cpu));
        for (var t = 0f; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += dt)
        {
            game.Step(default, dt);
            if (t < next)
            {
                continue;
            }

            next = t + perFrame;
            var redUsed = Used(reds);
            var blueUsed = Used(blues);
            ScreenTextDraw[] text =
            [
                new("赤 = VR の振りの型　青 = 教科書の型（刀）", new Vector2(40f, 40f), 30f, new Vector4(1f, 1f, 1f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"赤の技: {(redUsed.Count > 0 ? redUsed[^1] : "—")}（{redUsed.Count}）  失ったセル {redStart - Cells(game.Player)}"), new Vector2(40f, 80f), 24f, new Vector4(1f, 0.55f, 0.5f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"青の技: {(blueUsed.Count > 0 ? blueUsed[^1] : "—")}（{blueUsed.Count}）  失ったセル {blueStart - Cells(game.Cpu)}"), new Vector2(40f, 112f), 24f, new Vector4(0.55f, 0.7f, 1f, 1f), TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{t:0.0} s（{slow:0.#} 倍スロー）"), new Vector2(width - 40f, height - 40f), 24f, new Vector4(0.75f, 0.78f, 0.85f, 1f), TextAnchor.Right),
            ];
            Emit(renderer, ffmpeg, game, text);
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        log.WriteLine($"vrforms video {outPath}: red(VR) lost {redStart - Cells(game.Player)}, blue(textbook) lost {blueStart - Cells(game.Cpu)}, red used [{string.Join(",", Used(reds))}], blue used [{string.Join(",", Used(blues))}]");
    }

    private static void Emit(HeadlessRenderer renderer, Process ffmpeg, BoxingGame game, IReadOnlyList<ScreenTextDraw> text)
    {
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
        var a = game.Player.ChestPosition;
        var b = game.Cpu.ChestPosition;
        var center = ((a + b) * 0.5f) + new Vector3(0f, -0.2f, 0f);
        var along = b - a;
        along.Y = 0f;
        along = along.LengthSquared() > 1e-6f ? Vector3.Normalize(along) : Vector3.UnitZ;
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, along));
        var distance = MathF.Max(2.4f, Vector3.Distance(a, b) * 1.6f);
        var eye = center + (side * distance) + new Vector3(0f, 0.5f, 0f);
        var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, center, Vector3.UnitY, 45f * MathF.PI / 180f, (float)renderer.Width / renderer.Height, 0.02f, 30f);
        var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: displayEye, voxels: game.BuildVoxelVolumes(), postProcess: game.BuildPostProcess());
        ffmpeg.StandardInput.BaseStream.Write(rgba);
    }
}
