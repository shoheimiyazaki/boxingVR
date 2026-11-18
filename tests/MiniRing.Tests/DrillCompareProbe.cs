using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>守りのドリルの内訳（打った数・頭・ガード・そのほか・守りを出した数）。版 43 の写しと並べるための Probe（通常の実行から外す）。</summary>
[TestClass]
public sealed class DrillCompareProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void EarlySlipDrill()
    {
        var inside = new DefenseMove { HeadX = 0.5f, HeadY = -0.5f, HeadZ = -0.2f, Step = 1f, Duration = 0.3f, Rate = 60f, HandsUp = 0.3f, VsStraight = 1f };
        var book = DefenseBook.Default.With(Defense.Duck, inside);
        static void KeepFresh(BoxingGame game, BoxerBot _)
        {
            game.Player.Stamina = 1f;
            game.Cpu.Stamina = 1f;
        }

        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var open = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: false, counters: false), seed: seed, seconds: 8f, telegraph: 0.08f, observe: KeepFresh);
            Console.WriteLine($"EARLY seed {seed} open: {open}");
        }
    }

    /// <summary>始めの 8 秒の開いた受け手: 攻め手の拳のすべての接触（部位・速さ・質量・ダメージ）と、頭と攻め手の拳の最短の距離。</summary>
    [TestMethod]
    [TestCategory("Probe")]
    public void EarlySlipHits()
    {
        var inside = new DefenseMove { HeadX = 0.5f, HeadY = -0.5f, HeadZ = -0.2f, Step = 1f, Duration = 0.3f, Rate = 60f, HandsUp = 0.3f, VsStraight = 1f };
        var book = DefenseBook.Default.With(Defense.Duck, inside);
        var t = 0f;
        foreach (var seed in new[] { 2, 5, 6 })
        {
            t = 0f;
            var result = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: false, counters: false), seed: seed, seconds: 8f, telegraph: 0.08f,
                observe: (g, _) =>
                {
                    t += 1f / 60f;
                    g.Player.Stamina = 1f;
                    g.Cpu.Stamina = 1f;
                    foreach (var h in g.RecentHits.Where(h => h.Attacker == g.Cpu))
                    {
                        Console.WriteLine($"EHIT seed {seed} t {t:0.00} {h.Zone} {h.Hand} speed {h.Speed:0.00} mass {h.ImpactMass:0.00} share {h.BodyShare:0.00} dmg {h.Damage:0.0}");
                    }
                });
            Console.WriteLine($"EHIT seed {seed} punches {result.Punches} open head {result.OpenHeadHits}");
        }
    }

    /// <summary>予測する受け手の読み（OpponentRead）を、攻め手が拳を打つたびに見る: 直前 0.6 秒の確からしさと体の手がかりの最大、覚えた拳の数。</summary>
    [TestMethod]
    [TestCategory("Probe")]
    public void PredictedSlipForecasts()
    {
        foreach (var seed in new[] { 2, 3 })
        {
            var maxLike = 0f;
            var maxBody = -99f;
            var t = 0f;
            var window = new Queue<(float T, float Like, float Body)>();
            DefenseDrill.Punch("2", Defense.Slip, PredictionTests.SlipDefender(1f), seed: seed, seconds: 30f, telegraph: 0.08f,
                observe: (g, bot) =>
                {
                    t += 1f / 60f;
                    g.Player.Stamina = 1f;
                    g.Cpu.Stamina = 1f;
                    var read = (OpponentRead)typeof(BoxerBot).GetField("_read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(bot)!;
                    var f = read.Predict();
                    window.Enqueue((t, f.Likelihood, f.BodyEvidence));
                    while (window.Count > 0 && window.Peek().T < t - 0.6f)
                    {
                        window.Dequeue();
                    }

                    foreach (var hit in g.RecentHits.Where(h => h.Attacker == g.Cpu))
                    {
                        Console.WriteLine($"PF seed {seed} t {t:0.0} HIT {hit.Zone} speed {hit.Speed:0.00} mass {hit.ImpactMass:0.00} dmg {hit.Damage:0.0}");
                    }

                    var gv = g.Cpu.GloveVelocity(Hand.Right).Length();
                    if (gv > 1f && (int)(t * 60) % 6 == 0 && window.Count > 0)
                    {
                        maxLike = window.Max(w => w.Like);
                        maxBody = window.Max(w => w.Body);
                        Console.WriteLine($"PF seed {seed} t {t:0.0} glove {gv:0.0} onsets {read.Onsets} like(max0.6s) {maxLike:0.00} bodyEv {maxBody:0.00} cue {bot.Style.BodyCue:0.00} defense {bot.CurrentDefense}");
                    }
                });
        }
    }

    /// <summary>TechniqueWeights_DecideWhichFormsTheSwordsmanThrows の写し: 1 秒ごとの状態（受け手・振り手の意識、ダウンの状態、使った型）。</summary>
    [TestMethod]
    [TestCategory("Probe")]
    public void TsukiOnlySwordsman()
    {
        var only = WeaponCatalog.Textbook(MiniRing.Weapons.WeaponKind.Katana) with
        {
            TechniqueWeights = new Dictionary<string, float> { ["tsuki"] = 1f, ["kesa"] = 0f, ["gyaku-kesa"] = 0f, ["nuki-do"] = 0f, ["kiriage"] = 0f, ["harai-men"] = 0f, ["hiki-men"] = 0f },
        };
        var corners = BoxingCorners.Default with
        {
            RedBotStyle = BotCatalog.Style("champion"),
            PlayerWeapon = MiniRing.Weapons.WeaponKind.Katana,
            PlayerTwoHanded = true,
            UseWeaponBots = true,
            PlayerWeaponStyle = only,
        };
        using var game = new BoxingGame(seed: 3, corners: corners);
        var used = new HashSet<string>();
        for (var i = 0; i < 120 * 60 && !game.Player.IsDead && !game.Cpu.IsDead; i++)
        {
            game.Step(default, 1f / 60f);
            if (((DisarmedFallback)game.PlayerControl!).Armed is SwordsmanBot bot)
            {
                used.UnionWith(bot.Used);
            }

            if (i % 300 == 0)
            {
                var d = game.Cpu.ChestPosition - game.Player.ChestPosition;
                Console.WriteLine($"TSUKI t {i / 60f:0} player {game.Player.State} cons {game.Player.Consciousness:0.00} cpu {game.Cpu.State} cons {game.Cpu.Consciousness:0.00} gap {new System.Numerics.Vector2(d.X, d.Z).Length():0.00} used [{string.Join(",", used)}] armed {((DisarmedFallback)game.PlayerControl!).Armed?.GetType().Name} wields {game.Player.Wields(BoxingGame.WeaponHand)} dead {game.Player.IsDead}/{game.Cpu.IsDead}");
            }
        }
    }

    /// <summary>棍棒の 1 振り（ClubSwingTests）: 運んだ運動量・削ったセル・当たった所の速さ。</summary>
    [TestMethod]
    [TestCategory("Probe")]
    public void ClubChop()
    {
        foreach (var family in new[] { MiniRing.Weapons.SwingFamily.Chop })
        {
            var r = MiniRing.Weapons.SwingTest.Run(MiniRing.Weapons.WeaponSwing.Default(family), 0.38f, (game, _, _) =>
            {
                game.Player.Stamina = 1f;
                game.Cpu.Stamina = 1f;
            }, gloves: false);
            Console.WriteLine($"CLUB {family}: landed {r.Landed} part {r.Part} cells {r.Cells} impulse {r.Impulse:0.00} hitTipSpeed {r.HitTipSpeed:0.00} tipSpeed {r.TipSpeed:0.00} twist {r.HeadTwist:0.0} concussion {r.Concussion:0.00}");
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void PredictedSlipDrill()
    {
        static void KeepFresh(BoxingGame game, BoxerBot _)
        {
            game.Player.Stamina = 1f;
            game.Cpu.Stamina = 1f;
        }

        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            var r = DefenseDrill.Punch("2", Defense.Slip, PredictionTests.SlipDefender(0f), seed: seed, seconds: 30f, telegraph: 0.08f, observe: KeepFresh);
            var p = DefenseDrill.Punch("2", Defense.Slip, PredictionTests.SlipDefender(1f), seed: seed, seconds: 30f, telegraph: 0.08f, observe: KeepFresh);
            Console.WriteLine($"PRED seed {seed} react: {r}");
            Console.WriteLine($"PRED seed {seed} predict: {p}");
        }
    }
}
