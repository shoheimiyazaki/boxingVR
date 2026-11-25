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
}
