using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 剣士の型の練習（<see cref="SwordPractice"/>）を tower32 で回す入口（調査用。<c>bash tools/remote-test.sh probe:SwordPracticeProbe.&lt;名前&gt;</c>）。
/// 結果は標準出力（trx）に出す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class SwordPracticeProbe
{
    private static readonly string[] Swordsmen = ["jin", "konrad", "edgar"];
    private static readonly bool[] Flags = [false, true];
    private static readonly string[] Standard = ["standard"];
    private static readonly int[] CheckSeeds = [11, 12];

    [TestMethod]
    public void Baseline()
    {
        foreach (var id in Swordsmen)
        {
            Console.WriteLine(SwordPractice.Baseline(CharacterRoster.Get(id), [1, 2, 3, 4], ["standard"], Environment.ProcessorCount, TextWriter.Null));
        }
    }

    /// <summary>練習する型（基準の表で、頭・首へ届く打ち下ろし・払い・斬り上げと、点の高かった型）。</summary>
    private static readonly Dictionary<string, string[]> Practice = new()
    {
        ["jin"] = ["kesa", "shomen", "harai-men", "kiriage", "hiki-men"],
        ["konrad"] = ["oberhau", "zornhau", "scheitelhau", "unterhau"],
        ["edgar"] = ["kiri-oroshi", "yoko-barai", "tsuki"],
    };

    /// <summary>2 周目に採った差分（docs/arena/swordpractice_2026-11-26_round2.md）。3 周目はここから探す。</summary>
    private static readonly Dictionary<string, string> Round2 = new()
    {
        ["jin"] = """{"kesa":{"Tempo":0.6303967,"Twist":0.9745735,"Lift":-0.004811477,"Lean":0.03043313,"Reach":-0.27542216},"shomen":{"Tempo":1.7576734,"Twist":0.7710245,"Lift":0.103208706,"Lean":-0.014151556,"Reach":-0.020852275},"harai-men":{"Tempo":1.4607458,"Twist":0.64340913,"Lift":0.020486098,"Lean":0.04547536,"Reach":0.012455659},"kiriage":{"Tempo":1.2575268,"Twist":0.9802079,"Lift":0.04677126,"Lean":-0.01587192,"Reach":0.092310674},"hiki-men":{"Tempo":0.84818125,"Twist":0.6648953,"Lift":0.031758472,"Lean":-0.023804525,"Reach":-0.010962265}}""",
        ["konrad"] = """{"unterhau":{"Tempo":1.2127702,"Twist":0.94143367,"Lift":0.017749438,"Lean":-0.007598623,"Reach":-0.1753549}}""",
        ["edgar"] = """{"kiri-oroshi":{"Tempo":2.1096509,"Twist":1.0058315,"Lift":0.018611528,"Lean":0.107511446,"Reach":0.0061401967},"tsuki":{"Tempo":1.2337928,"Twist":0.70719886,"Lift":-0.008519694,"Lean":0.10842066,"Reach":-0.033439774}}""",
    };

    [TestMethod]
    [DataRow("jin")]
    [DataRow("konrad")]
    [DataRow("edgar")]
    public void Train(string id)
    {
        var who = CharacterRoster.Get(id);
        var adopted = new Dictionary<string, MiniRing.Motion.FormTune>();
        var round2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MiniRing.Motion.FormTune>>(Round2[id])!;
        foreach (var form in Practice[id])
        {
            // 3 周目（2026-11-26）: 2 周目は基準の体だけで練習し、比べ直しで大きい体（コンラートの体格）を 0/6 しか倒せなかった。練習に 2 つの体、比べ直しに練習で使わない体（ramon・dmitri）も。
            var (tune, report) = SwordPractice.Train(who, form, generations: 25, lambda: 12, sigma: 0.4f, seed: 3, seeds: [1, 2, 3], victims: ["standard", "heavy"],
                heldSeeds: [11, 12, 13], heldVictims: ["standard", "heavy", "ramon", "dmitri"], parallel: Environment.ProcessorCount, log: TextWriter.Null,
                from: round2.GetValueOrDefault(form));
            Console.WriteLine(report);
            if (tune is not null)
            {
                adopted[form] = tune;
            }
        }

        Console.WriteLine($"ADOPTED {id} " + System.Text.Json.JsonSerializer.Serialize(adopted));
    }

    /// <summary>3 周目に採った差分（docs/arena/swordpractice_2026-11-26_round3.md）。4 周目はここから（オーバーハウは教科書から）。</summary>
    private static readonly Dictionary<string, string> Round3 = new()
    {
        ["jin"] = """{"kesa":{"Tempo":0.7340395,"Twist":0.90022045,"Lift":-0.039707273,"Lean":0.0064524487,"Reach":-0.28055072},"shomen":{"Tempo":1.4569677,"Twist":1.0799829,"Lift":0.03290581,"Lean":-0.004199765,"Reach":-0.033927582},"harai-men":{"Tempo":1.286337,"Twist":0,"Lift":0.124481454,"Lean":0.022987546,"Reach":-0.05875615}}""",
        ["konrad"] = """{"unterhau":{"Tempo":1.3274145,"Twist":0.9117595,"Lift":-0.034988325,"Lean":0.00048754504,"Reach":-0.12780634}}""",
        ["edgar"] = """{"kiri-oroshi":{"Tempo":2.0052786,"Twist":0.9811229,"Lift":-0.0072263554,"Lean":0.06029655,"Reach":-0.06284144}}""",
    };

    private static readonly Dictionary<string, string[]> Practice4 = new()
    {
        ["jin"] = ["harai-men", "shomen", "kesa"],
        ["konrad"] = ["oberhau", "unterhau"],
        ["edgar"] = ["kiri-oroshi"],
    };

    /// <summary>
    /// 4 周目（2026-11-26）: 3 周目はどの体でも種による違いがほぼ無く（体ごとに 3/3 か 0/3）、いちばん大きく太い dmitri だけがどの型でも倒れなかった。
    /// 練習に dmitri を足し（standard・heavy・dmitri × 種 1,2）、比べ直しは練習で使わない 4 つの体（ramon・akira = 小さい、marcus・takumi-ishiguro = 大きい・高い）。
    /// </summary>
    [TestMethod]
    [DataRow("jin")]
    [DataRow("konrad")]
    [DataRow("edgar")]
    public void Train4(string id)
    {
        var who = CharacterRoster.Get(id);
        var adopted = new Dictionary<string, MiniRing.Motion.FormTune>();
        var round3 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MiniRing.Motion.FormTune>>(Round3[id])!;
        foreach (var form in Practice4[id])
        {
            var (tune, report) = SwordPractice.Train(who, form, generations: 30, lambda: 12, sigma: 0.4f, seed: 4, seeds: [1, 2], victims: ["standard", "heavy", "dmitri"],
                heldSeeds: [11, 12], heldVictims: ["ramon", "akira", "marcus", "takumi-ishiguro"], parallel: Environment.ProcessorCount, log: TextWriter.Null,
                from: round3.GetValueOrDefault(form));
            Console.WriteLine(report);
            if (tune is not null)
            {
                adopted[form] = tune;
            }
        }

        Console.WriteLine($"ADOPTED {id} " + System.Text.Json.JsonSerializer.Serialize(adopted));
    }

    /// <summary>4 周目に採った差分（docs/arena/swordpractice_2026-11-26_round4.md）。</summary>
    private static readonly Dictionary<string, string> Round4 = new()
    {
        ["jin"] = """{"harai-men":{"Tempo":1.2629216,"Twist":0.22269554,"Lift":0.108421944,"Lean":-0.03258914,"Reach":0.02162886},"shomen":{"Tempo":1.4428612,"Twist":1.2003361,"Lift":-0.010764726,"Lean":-0.0052448334,"Reach":-0.056772053}}""",
        ["konrad"] = """{"oberhau":{"Tempo":2.2890172,"Twist":1.084059,"Lift":-0.025004588,"Lean":-0.1161254,"Reach":-0.056888994}}""",
        ["edgar"] = """{"kiri-oroshi":{"Tempo":2.3576238,"Twist":0.9448839,"Lift":0.028237833,"Lean":0.08900997,"Reach":-0.04126966}}""",
    };

    /// <summary>
    /// 5 周目（2026-11-26）: 4 周目も、倒れるかどうかは体ごとにほぼ決まり（種で変わらない）、練習した体だけを倒す型になった。
    /// 種は 1 つにして体を 6 つ（小さい・標準・大きい・太い）で練習し、比べ直しは練習で使わない 5 つの体 × 種 2 つ。
    /// </summary>
    [TestMethod]
    [DataRow("jin")]
    [DataRow("konrad")]
    [DataRow("edgar")]
    public void Train5(string id)
    {
        var who = CharacterRoster.Get(id);
        var adopted = new Dictionary<string, MiniRing.Motion.FormTune>();
        var from = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MiniRing.Motion.FormTune>>(Round4[id])!;
        foreach (var (form, tune0) in from)
        {
            var (tune, report) = SwordPractice.Train(who, form, generations: 30, lambda: 12, sigma: 0.3f, seed: 5, seeds: [1], victims: ["standard", "heavy", "dmitri", "ramon", "marcus", "akira"],
                heldSeeds: [11, 12], heldVictims: ["takumi-ishiguro", "sean", "joao", "abel-navarrete", "tsuyoshi"], parallel: Environment.ProcessorCount, log: TextWriter.Null,
                from: tune0);
            Console.WriteLine(report);
            if (tune is not null)
            {
                adopted[form] = tune;
            }
        }

        Console.WriteLine($"ADOPTED {id} " + System.Text.Json.JsonSerializer.Serialize(adopted));
    }

    /// <summary>
    /// 狙い（<see cref="MiniRing.Bots.WeaponStyle.AutoAim"/>: 相手の空いている部位へ両手の通り道をずらす）の有無で、5 周目の型が体格の違う相手を倒せるか。
    /// 5 周目までは、どの体でも同じ通り道で振っていた（刀・剣と盾の教科書は狙わない）。
    /// </summary>
    [TestMethod]
    public void AimCheck()
    {
        var cases = new (string Id, string Form, string Tune)[]
        {
            ("jin", "harai-men", """{"Tempo":1.23004,"Twist":0.07971518,"Lift":0.064279966,"Lean":0.0033723712,"Reach":0.016796041}"""),
            ("jin", "shomen", """{"Tempo":1.4831306,"Twist":0.59811574,"Lift":-0.041464347,"Lean":0.080022454,"Reach":-0.06768147}"""),
            ("edgar", "kiri-oroshi", """{"Tempo":2.2448418,"Twist":1.0368705,"Lift":0.015612647,"Lean":0.07474075,"Reach":-0.03295328}"""),
            ("konrad", "oberhau", """{"Tempo":2.2155821,"Twist":1.0217743,"Lift":-0.006157292,"Lean":-0.11143296,"Reach":-0.062244557}"""),
        };
        string[] bodies = ["standard", "heavy", "dmitri", "ramon", "marcus", "akira", "takumi-ishiguro", "sean", "joao", "abel-navarrete", "tsuyoshi", "ivan-escobedo", "andre-bishop", "taeyun"];
        var jobs = (from c in cases from aim in Flags from tuned in Flags from b in bodies select (c, aim, tuned, b)).ToList();
        var results = new string[jobs.Count];
        var outcomes = new SwordPractice.Outcome[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var (c, aim, tuned, b) = jobs[i];
            var who = CharacterRoster.Get(c.Id);
            var style = who.WeaponStyle! with { AutoAim = aim };
            var tune = tuned ? System.Text.Json.JsonSerializer.Deserialize<MiniRing.Motion.FormTune>(c.Tune)! : MiniRing.Motion.FormTune.Identity;
            outcomes[i] = SwordPractice.Swing(who, style, SwordPractice.Technique(who, c.Form, tune), 11, SwordPractice.Victim(b)) with { Victim = b };
        });
        foreach (var g in jobs.Select((j, i) => (j, o: outcomes[i])).GroupBy(x => (x.j.c.Id, x.j.c.Form, x.j.aim, x.j.tuned)))
        {
            var list = g.ToList();
            Console.WriteLine($"{g.Key.Id} {g.Key.Form} aim={g.Key.aim} tuned={g.Key.tuned}: {list.Count(x => x.o.Incapacitated)}/{list.Count} impact {list.Average(x => x.o.ImpactSpeed):0.0} self {list.Average(x => x.o.SelfCells):0} | "
                + string.Join(" ", list.Select(x => $"{x.j.b}:{(x.o.Incapacitated ? "X" : "-")}{x.o.ConsciousnessEnd:0.0}")));
        }
    }

    /// <summary>5 周目に採った差分（docs/arena/swordpractice_2026-11-26_round5.md）。</summary>
    private static readonly Dictionary<string, string> Round5 = new()
    {
        ["jin"] = """{"harai-men":{"Tempo":1.23004,"Twist":0.07971518,"Lift":0.064279966,"Lean":0.0033723712,"Reach":0.016796041},"shomen":{"Tempo":1.4831306,"Twist":0.59811574,"Lift":-0.041464347,"Lean":0.080022454,"Reach":-0.06768147}}""",
        ["konrad"] = """{"oberhau":{"Tempo":2.2155821,"Twist":1.0217743,"Lift":-0.006157292,"Lean":-0.11143296,"Reach":-0.062244557}}""",
        ["edgar"] = """{"kiri-oroshi":{"Tempo":2.2448418,"Twist":1.0368705,"Lift":0.015612647,"Lean":0.07474075,"Reach":-0.03295328}}""",
    };

    /// <summary>
    /// 6 周目（2026-11-26）: 5 周目の比べ直しと <see cref="AimCheck"/> で、倒せなかったのは背の高い・脚の長い体（takumi-ishiguro・taeyun・ivan-escobedo・abel-navarrete）。
    /// 練習の振りは試合と同じく教科書の狙い（AutoAim: 長剣は狙う）で振る。練習に高い体を足して 8 つ、比べ直しは別の 8 つ。
    /// </summary>
    [TestMethod]
    [DataRow("jin")]
    [DataRow("konrad")]
    [DataRow("edgar")]
    public void Train6(string id)
    {
        var who = CharacterRoster.Get(id);
        var adopted = new Dictionary<string, MiniRing.Motion.FormTune>();
        var from = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MiniRing.Motion.FormTune>>(Round5[id])!;
        foreach (var (form, tune0) in from)
        {
            var (tune, report) = SwordPractice.Train(who, form, generations: 30, lambda: 12, sigma: 0.3f, seed: 6, seeds: [1],
                victims: ["standard", "heavy", "dmitri", "ramon", "takumi-ishiguro", "taeyun", "abel-navarrete", "akira"],
                heldSeeds: [11], heldVictims: ["marcus", "sean", "joao", "tsuyoshi", "ivan-escobedo", "andre-bishop", "prasert-rattanakul", "mauricio-villegas"],
                parallel: Environment.ProcessorCount, log: TextWriter.Null, from: tune0);
            Console.WriteLine(report);
            if (tune is not null)
            {
                adopted[form] = tune;
            }
        }

        Console.WriteLine($"ADOPTED {id} " + System.Text.Json.JsonSerializer.Serialize(adopted));
    }

    /// <summary>
    /// 採る候補を、名簿の全部の体（四足の galva を除く 28 体 + 基準の体）× 種 11・12 で、教科書と比べる（練習で使った体も使わなかった体も）。
    /// 長剣は狙う（教科書）・狙わない（教育で変える候補）の両方。
    /// </summary>
    [TestMethod]
    public void FinalCheck()
    {
        var cases = new (string Id, string Form, string Tune, bool? Aim)[]
        {
            ("jin", "harai-men", """{"Tempo":1.235742,"Twist":0.5021529,"Lift":0.077622086,"Lean":-0.010121513,"Reach":0.04616947}""", null),
            ("jin", "shomen", """{"Tempo":1.5067066,"Twist":0.13271883,"Lift":-0.023316912,"Lean":0.049709447,"Reach":-0.10149773}""", null),
            ("jin", "harai-men", """{"Tempo":1.23004,"Twist":0.07971518,"Lift":0.064279966,"Lean":0.0033723712,"Reach":0.016796041}""", null),
            ("edgar", "kiri-oroshi", """{"Tempo":2.1822982,"Twist":1.0218937,"Lift":0.03213026,"Lean":-0.010457604,"Reach":0.033105146}""", null),
            ("edgar", "kiri-oroshi", """{"Tempo":2.2448418,"Twist":1.0368705,"Lift":0.015612647,"Lean":0.07474075,"Reach":-0.03295328}""", null),
            ("konrad", "oberhau", """{"Tempo":2.2155821,"Twist":1.0217743,"Lift":-0.006157292,"Lean":-0.11143296,"Reach":-0.062244557}""", true),
            ("konrad", "oberhau", """{"Tempo":2.2155821,"Twist":1.0217743,"Lift":-0.006157292,"Lean":-0.11143296,"Reach":-0.062244557}""", false),
        };
        var bodies = Standard.Concat(CharacterRoster.All.Where(c => !c.Build.FourLegged).Select(c => c.Id)).ToList();
        var jobs = (from c in cases.Select((c, k) => (c, k)) from tuned in Flags from b in bodies from seed in CheckSeeds select (c.c, c.k, tuned, b, seed)).ToList();
        var outcomes = new SwordPractice.Outcome[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var (c, _, tuned, b, seed) = jobs[i];
            var who = CharacterRoster.Get(c.Id);
            var textbook = MiniRing.Bots.WeaponCatalog.Textbook(who.Weapon!.Value);
            var style = tuned && c.Aim is { } aim ? textbook with { AutoAim = aim } : textbook;
            var tune = tuned ? System.Text.Json.JsonSerializer.Deserialize<MiniRing.Motion.FormTune>(c.Tune)! : MiniRing.Motion.FormTune.Identity;
            outcomes[i] = SwordPractice.Swing(who, style, SwordPractice.Technique(who, c.Form, tune), seed, SwordPractice.Victim(b)) with { Victim = b };
        });
        foreach (var g in jobs.Select((j, i) => (j, o: outcomes[i])).GroupBy(x => (x.j.k, x.j.tuned)))
        {
            var list = g.ToList();
            var c = list[0].j.c;
            var failed = list.Where(x => !x.o.Incapacitated).Select(x => $"{x.j.b}/{x.j.seed}").ToList();
            Console.WriteLine($"[{g.Key.k}] {c.Id} {c.Form} {(g.Key.tuned ? $"tuned aim={c.Aim?.ToString() ?? "textbook"}" : "textbook")}: {list.Count(x => x.o.Incapacitated)}/{list.Count} dead {list.Count(x => x.o.Dead)} impact {list.Average(x => x.o.ImpactSpeed):0.0} m/s self {list.Average(x => x.o.SelfCells):0} selfdown {list.Count(x => x.o.SelfDown)} | failed: {string.Join(" ", failed)}");
        }
    }

    [TestMethod]
    public void TipTrace()
    {
        var who = CharacterRoster.Get("jin");
        var technique = SwordPractice.Technique(who, "shomen", MiniRing.Motion.FormTune.Identity);
        var last = -1f;
        var o = SwordPractice.Swing(who, who.WeaponStyle!, technique, 1, SwordPractice.Victim("standard"), (game, t, swinging) =>
        {
            if (t - last < 0.05f)
            {
                return;
            }

            last = t;
            var sword = game.WeaponOf(game.Player)!;
            var s = game.World.GetState(sword.Body.BodyId);
            var tip = System.Numerics.Vector3.Transform(MiniRing.Weapons.Weapon.TipOf(sword.Kind), s.Orientation);
            var v = s.LinearVelocity + System.Numerics.Vector3.Cross(s.AngularVelocity, tip);
            var chest = game.World.GetState(game.Player.Parts.Chest).LinearVelocity;
            Console.WriteLine($"{t:0.00} swing {swinging} tip {v.Length():0.0} chest {chest.Length():0.00} foe c {game.Cpu.Consciousness:0.00} conc {game.Cpu.Concussion:0.00}");
        });
        Console.WriteLine(o);
    }

    private static readonly int[] RetrainSeeds = [1, 2];
    private static readonly string[] RetrainVictims = ["standard", "heavy", "dmitri"];
    private static readonly string[] RetrainHeldVictims = ["ramon", "akira", "marcus", "takumi-ishiguro"];

    /// <summary>
    /// 体が変わった後に、名簿の教育の型を今の体に合わせて練習し直す（2026-11-26、つま先の蝶番を固くした体 = 物理 103。エドガーは ToeEdgarTrainProbe）。
    /// 名簿の今の差分から、練習 standard・heavy・dmitri × 種 1・2、比べ直しは使っていない ramon・akira・marcus・takumi-ishiguro × 種 11・12（Train4 と同じ設定）。
    /// そのあと名簿の全部の体（四足を除く 29 体）× 種 11・12 で、教科書・今の教育・練習し直した型を比べる。
    /// </summary>
    [TestMethod]
    [DataRow("jin")]
    public void RetrainForBody(string id)
    {
        var who = CharacterRoster.Get(id);
        var current = who.WeaponStyle!.Forms!;
        var adopted = new Dictionary<string, MiniRing.Motion.FormTune>();
        foreach (var form in current.Keys)
        {
            var (tune, report) = SwordPractice.Train(who, form, generations: 30, lambda: 12, sigma: 0.4f, seed: 5, seeds: RetrainSeeds, victims: RetrainVictims,
                heldSeeds: CheckSeeds, heldVictims: RetrainHeldVictims, parallel: Environment.ProcessorCount, log: TextWriter.Null);
            Console.WriteLine(report);
            if (tune is not null)
            {
                adopted[form] = tune;
            }
        }

        Console.WriteLine($"ADOPTED {id} " + System.Text.Json.JsonSerializer.Serialize(adopted));
        var bodies = Standard.Concat(CharacterRoster.All.Where(c => !c.Build.FourLegged).Select(c => c.Id)).ToList();
        var kinds = new[] { "textbook", "current", "retrained" };
        var jobs = (from form in current.Keys from kind in kinds where kind != "retrained" || adopted.ContainsKey(form) from b in bodies from seed in CheckSeeds select (form, kind, b, seed)).ToList();
        var outcomes = new SwordPractice.Outcome[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var (form, kind, b, seed) = jobs[i];
            var style = kind == "textbook" ? MiniRing.Bots.WeaponCatalog.Textbook(who.Weapon!.Value) : who.WeaponStyle!;
            var tune = kind switch { "textbook" => MiniRing.Motion.FormTune.Identity, "current" => current[form], _ => adopted[form] };
            outcomes[i] = SwordPractice.Swing(who, style, SwordPractice.Technique(who, form, tune), seed, SwordPractice.Victim(b)) with { Victim = b };
        });
        foreach (var g in jobs.Select((j, i) => (j, o: outcomes[i])).GroupBy(x => (x.j.form, x.j.kind)))
        {
            var five = g.Count(x => x.o.Incapacitated && x.j.seed == 11 && new[] { "standard", "konrad", "ramon", "joao", "dmitri" }.Contains(x.j.b));
            Console.WriteLine($"ROSTER {id} {g.Key.form} {g.Key.kind}: {g.Count(x => x.o.Incapacitated)}/{g.Count()} impact {g.Average(x => x.o.ImpactSpeed):0.0} m/s; the five test victims {five}/5");
        }
    }
}
