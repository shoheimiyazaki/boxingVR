using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using MiniRing.Arena;
using MiniRing.Characters;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（docs/arena/foot_snag.md「種を増やした比べ」）: 足の運びの変更（体重を見た足の選び方・宙の足の計画し直し）で落ちた既存のテストの、比べている量を
/// 多くの種で測る。直す前（<see cref="FootworkTuning.UnloadedFootShare"/> 0・<see cref="FootworkTuning.ReplanSwing"/> 偽を既定にした同じコード）と今とで回して比べる。
/// 1 行 = 「テスト名 種 量=値 …」。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FootSnagSeedProbe
{
    private const float Dt = 1f / 240f;
    private static readonly int[] Seeds = [.. Enumerable.Range(1, 12)];
    private static readonly int[] IvfSeeds = [.. Enumerable.Range(1, Environment.GetEnvironmentVariable("IVF_TRACE") == "1" ? 1 : 12)];
    private static readonly float[] Levels = [0.6f, 0.4f, 0.25f, 0.1f];

    public TestContext TestContext { get; set; } = null!;

    private void Report(string name, IEnumerable<int> seeds, Func<int, string> run)
    {
        var list = seeds.ToArray();
        var lines = new ConcurrentDictionary<int, string>();
        Parallel.For(0, list.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try
            {
                lines[i] = run(list[i]);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                lines[i] = "error " + e.GetType().Name + ": " + e.Message.Replace('\n', ' ');
            }
        });
        for (var i = 0; i < list.Length; i++)
        {
            TestContext.WriteLine($"{name} seed {list[i]} {lines[i]}");
        }
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    [TestMethod]
    public void Hybrid() => Report("hybrid", Seeds.SelectMany(s => new[] { s, -s }), seed =>
    {
        var fromCorners = seed < 0;
        var corners = new BoxingCorners("hybrid", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), VoxelGloves: true);
        corners = fromCorners ? corners.StartingAtCorners() : corners;
        using var game = new BoxingGame(FighterTuning.HybridPunch, Math.Abs(seed), corners);
        var hits = 0;
        var firstHit = float.NaN;
        var firstDown = float.NaN;
        var seconds = fromCorners ? 12 : 3;
        for (var frame = 1; frame <= seconds * 60; frame++)
        {
            game.Step(default, 1f / 60f);
            hits += game.RecentHits.Count;
            firstHit = float.IsNaN(firstHit) && hits > 0 ? frame / 60f : firstHit;
            firstDown = float.IsNaN(firstDown) && (game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing) ? frame / 60f : firstDown;
        }

        var pass = hits > 0 && (float.IsNaN(firstDown) || firstDown >= firstHit);
        return F($"corners={fromCorners} hits={hits} firstHit={firstHit:0.00} firstDown={firstDown:0.00} pass={pass}");
    });

    [TestMethod]
    public void HeelStomp() => Report("heelstomp", Seeds, seed =>
    {
        var sole = HeelStompTests.Drill(heel: false, seed);
        var heel = HeelStompTests.Drill(heel: true, seed);
        return F($"soleHits={sole.FootHits} heelHits={heel.FootHits} soleHead={sole.HeadHits} heelHead={heel.HeadHits} solePeak={sole.PeakSpeed:0.00} heelPeak={heel.PeakSpeed:0.00} heelFalls={heel.AttackerFalls} lift={heel.Lift:0.00}");
    });

    [TestMethod]
    public void HeelSwitch() => Report("heelswitch", Seeds, seed =>
    {
        var attacker = HeelStompTests.Attacker(heel: true);
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("attacker", s => new BoxerBot(attacker, s),
            s => new BoxerBot(BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f }, s),
            CpuStyleJson: attacker.ToJson(), Referee: false));
        Hand? first = null;
        Hand? after = null;
        var falls = 0;
        var standing = true;
        var hitsAfter = 0;
        for (var t = 0f; t < 20f; t += Dt)
        {
            if (t > 2.518f)
            {
                game.Player.Concussion = 0.97f;
            }

            game.Step(default, Dt);
            var ground = (game.CpuControl as GroundStriker)?.Ground;
            if (first is null && ground is { Striking: true, CurrentName: "heel-stomp" })
            {
                first = ground.CurrentFoot;
                game.Cpu.SetOxygen(first == Hand.Left ? MuscleRegion.LeftLeg : MuscleRegion.RightLeg, 0.3f);
            }

            if (first is not null && after is null && ground is { FootSwitches: > 0 })
            {
                after = ground.CurrentFoot;
            }

            if (after is not null)
            {
                hitsAfter += game.RecentHits.Count(h => h.Attacker == game.Cpu && h.Victim == game.Player && h.Kick && h.Hand == after);
            }

            var up = game.Cpu.State == FighterState.Standing;
            falls += standing && !up ? 1 : 0;
            standing = up;
        }

        return F($"first={first} after={after} switched={after is not null && after != first} hitsAfter={hitsAfter} falls={falls}");
    });

    [TestMethod]
    public void Targeting() => Report("targeting", Seeds, seed =>
    {
        using var game = new BoxingGame(seed: seed);
        for (var i = 0; i < 50; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var from = game.Player.HeadPosition;
        var chin = cpu.AsTarget.Point(AimGoal.Chin, from)!.Value;
        var margin = Vector3.Distance(from, chin) - (Vector3.Distance(from, cpu.HeadPosition) + 0.01f);
        return F($"chinBehindMargin={margin:0.0000} pass={margin < 0f}");
    });

    [TestMethod]
    public void Roundhouse() => Report("roundhouse", Seeds, seed =>
    {
        var side = seed % 2 == 1 ? Hand.Right : Hand.Left;
        var spec = KickSpec.Of(KickTechnique.Roundhouse);
        var body = MiniRing.Characters.CharacterRoster.Get("taeyun").Build;
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
        var attacker = game.Player;
        var victim = game.Cpu;
        var reach = attacker.ArmReach;
        FighterCommand Cmd(Vector3 move, LegCommand? leg, float yaw, float twist)
        {
            var face = Vector3.Transform(victim.ChestPosition - attacker.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
            return new FighterCommand(move, face, new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
                TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
        }

        for (var i = 0; i < 3222; i++)
        {
            if (Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) < spec.Standoff * reach)
            {
                break;
            }

            game.Step(new BoxingInput(Cmd(Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f }), null, 0f, 0f)), Dt);
        }

        for (var i = 0; i < 604; i++)
        {
            game.Step(new BoxingInput(Cmd(Vector3.Zero, null, 0f, 0f)), Dt);
        }

        var aimWorld = spec.AimWorld(game.PlayerSense);
        var gap = Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f });
        Vector3 Toe(int foot) => game.World.GetState(attacker.ToeId(foot)).Position;
        var feet = attacker.Frame.WorldToLocal(Toe(0)) - attacker.Frame.WorldToLocal(Toe(1));
        var stance = F($"gap={gap:0.000} feetX={feet.X:0.000} feetZ={feet.Z:0.000} steps={attacker.Footwork.Steps}");
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var duration = spec.Seconds!.Value;
        var hits = new List<(float Fraction, float Energy)>();
        var seen = new HashSet<HitEvent>();
        for (var i = 0; i < (int)((duration + 0.6714f) / Dt); i++)
        {
            var t = i * Dt;
            var sense = game.PlayerSense;
            var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, sense.Self.WorldToLocal(aimWorld), reach, sense.SelfLeg(side)) : (LegCommand?)null;
            game.Step(new BoxingInput(Cmd(Vector3.Zero, leg, t <= duration ? spec.FaceYaw(side, t, duration) : 0f, t <= duration ? spec.TorsoTwist(side, t, duration) : 0f)), Dt);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Kick && hit.Attacker == attacker && seen.Add(hit))
                {
                    var knee = attacker.KneePosition(side);
                    var axis = attacker.AnklePosition(side) - knee;
                    hits.Add((Vector3.Dot(hit.Point - knee, axis) / axis.LengthSquared(), hit.Energy));
                }
            }
        }

        var main = hits.Count > 0 ? hits.OrderByDescending(h => h.Energy).First() : (Fraction: float.NaN, Energy: 0f);
        return stance + F($" side={side} landed={hits.Count > 0} mainFraction={main.Fraction:0.00} inBand={main.Fraction is >= 0.5f and <= 1.2f} standing={attacker.State == FighterState.Standing}");
    });

    [TestMethod]
    public void Shield() => Report("shield", Seeds, seed =>
    {
        var results = SwordShieldForms.Forms().Select(form =>
        {
            var technique = new MotionTechnique(form.Name, WeaponKind.IronSword, form, 1.408f);
            technique = technique with { Range = StrikeDrill.ReachOf(technique, 0.5296f) };
            var r = StrikeDrill.Run(technique, FormSwordsman.Guard(WeaponKind.IronSword), SwordStance.Rate, FighterTuning.Default, wear: HandWear.Shield, seed: seed);
            return $"{form.Name}={r.Cells}";
        });
        return string.Join(' ', results);
    });

    private static BoxingGame StillGame(int seed, bool fromCorners)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s));
        return new BoxingGame(seed: seed, corners: fromCorners ? corners.StartingAtCorners() : corners);
    }

    [TestMethod]
    public void DeepKnockdown() => Report("deepknockdown", Seeds, seed =>
    {
        using var game = StillGame(seed, fromCorners: false);
        var f = game.Cpu;
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.02817f;
        var stand = f.PelvisPosition.Y - ground;
        f.Concussion = 0.95f;
        var lowest = 1f;
        float? standingAt = null;
        for (var t = 0f; t < 20.14f && standingAt is null; t += Dt)
        {
            game.Step(default, Dt);
            var ratio = (f.PelvisPosition.Y - ground) / stand;
            if (t > 0.8392f && f.Consciousness < FighterTuning.Default.LimpConsciousness)
            {
                lowest = MathF.Min(lowest, ratio);
            }

            standingAt ??= t > 0.8392f && f.State == FighterState.Standing ? t : null;
            if (seed == 1 && Environment.GetEnvironmentVariable("DEEP_TRACE") == "1" && (int)(t / Dt) % 48 == 0)
            {
                Console.WriteLine(F($"  t={t:0.0} state={f.State} aware={f.Consciousness:0.00} tone={f.Tone:0.00} ratio={ratio:0.00} limp={f.Consciousness < FighterTuning.Default.LimpConsciousness}"));
            }
        }

        return F($"lowestWhileLimp={lowest:0.00} standingAt={standingAt:0.00} pass={lowest < 0.3f && standingAt is not null}");
    });

    [TestMethod]
    public void PelvisHeight() => Report("pelvisheight", Seeds, seed =>
    {
        float Settled(float consciousness)
        {
            using var game = StillGame(seed, fromCorners: false);
            var f = game.Cpu;
            for (var t = 0f; t < 2.518f; t += Dt)
            {
                game.Step(default, Dt);
            }

            var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.02817f;
            var stand = f.PelvisPosition.Y - ground;
            var sum = 0f;
            var n = 0;
            for (var t = 0f; t < 6.714f; t += Dt)
            {
                f.Concussion = 1f - consciousness;
                game.Step(default, Dt);
                if (t > 4.196f)
                {
                    sum += f.PelvisPosition.Y - ground;
                    n++;
                }
            }

            return sum / n / stand;
        }

        var h = Levels.Select(Settled).ToArray();
        var pass = h[1] < h[0] - 0.03f && h[3] < h[2] - 0.03f && h[2] is > 0.35f and < 0.85f && h[3] < 0.3f;
        return F($"h0.6={h[0]:0.00} h0.4={h[1]:0.00} h0.25={h[2]:0.00} h0.1={h[3]:0.00} pass={pass}");
    });

    [TestMethod]
    public void CornerAndDown() => Report("cornerdown", Seeds, seed =>
    {
        using var game = StillGame(seed, fromCorners: true);
        for (var t = 0f; t < 10.07f; t += Dt)
        {
            game.Step(default, Dt);
        }

        game.Cpu.Concussion = 0.95f;
        Vector3? fellAt = null;
        var counted = 0f;
        var drift = 0f;
        var toward = 0f;
        Vector3? corner = null;
        var closest = float.MaxValue;
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
        for (var t = 0f; t < 33.57f; t += Dt)
        {
            game.Step(default, Dt);
            if (game.Cpu.IsKnockedDown)
            {
                counted += Dt;
                corner ??= game.Layout.NeutralCorner(game.Cpu.PelvisPosition, game.FootprintOf(game.Player));
                closest = MathF.Min(closest, Flat(game.Player.PelvisPosition, corner.Value));
                if (counted >= 1.678f)
                {
                    fellAt ??= game.Cpu.PelvisPosition;
                    var now = Flat(game.Cpu.PelvisPosition, fellAt.Value);
                    if (now > drift)
                    {
                        drift = now;
                        var move = (game.Cpu.PelvisPosition - fellAt.Value) with { Y = 0f };
                        var toOpponent = (game.Player.PelvisPosition - fellAt.Value) with { Y = 0f };
                        toward = move.LengthSquared() < 1e-8f || toOpponent.LengthSquared() < 1e-8f ? 0f : Vector3.Dot(Vector3.Normalize(move), Vector3.Normalize(toOpponent));
                    }
                }
            }
            else if (fellAt is not null)
            {
                break;
            }
        }

        return F($"counted={fellAt is not null} drift={drift:0.000} toward={toward:0.00} closest={closest:0.000} pass={fellAt is not null && closest < 0.845f && drift < 0.845f && toward < 0.5f}");
    });

    [TestMethod]
    public void PunchWeight() => Report("punchweight", Seeds, seed =>
    {
        float Cross(FighterTuning tuning, float telegraph)
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), seed, telegraph, tuning);
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var f = bout.Game.Player;
            while (bout.PunchTime is { } t && t < 0.45f)
            {
                bout.Step();
                foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                {
                    return hit.Energy;
                }
            }

            return 0f;
        }

        var weak = FighterTuning.Default with { PelvisRotatorTorque = 60f };
        return F($"quick={Cross(FighterTuning.Default, 0.06f):0} quickWeak={Cross(weak, 0.06f):0} loaded={Cross(FighterTuning.Default, 0.25f):0} loadedWeak={Cross(weak, 0.25f):0}");
    });

    [TestMethod]
    public void CrossStance() => Report("crossstance", [1], seed =>
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), seed, 0.25f, FighterTuning.Default);
        var frames = 0;
        while (bout.PunchTime is null)
        {
            bout.Step();
            frames++;
            var f0 = bout.Game.Player;
            if (frames % 12 == 0)
            {
                Console.WriteLine(F($"  f={frames} feet=({Local(bout.Game, f0, 0).X:0.00},{Local(bout.Game, f0, 0).Z:0.00})/({Local(bout.Game, f0, 1).X:0.00},{Local(bout.Game, f0, 1).Z:0.00}) sw={f0.Footwork.Swinging} steps={f0.Footwork.Steps} v=({f0.PelvisVelocity.X:0.00},{f0.PelvisVelocity.Z:0.00})"));
            }
        }

        var f = bout.Game.Player;
        var lead = Local(bout.Game, f, 0);
        var rear = Local(bout.Game, f, 1);
        var gap = Vector3.Distance(f.ChestPosition with { Y = 0f }, bout.Game.Cpu.ChestPosition with { Y = 0f });
        return F($"frames={frames} gap={gap:0.000} lead=({lead.X:0.00},{lead.Z:0.00}) rear=({rear.X:0.00},{rear.Z:0.00}) steps={f.Footwork.Steps} sw={f.Footwork.Swinging}");

        static Vector3 Local(BoxingGame game, Fighter f, int foot) => f.Frame.WorldToLocal(game.World.GetState(f.ToeId(foot)).Position);
    });

    /// <summary>寄る間合い（腕の長さの倍）を 0.97〜1.13 に振った右ストレート: 拳のエネルギー（腰の強弱・溜めの有無）と、溜めた本の腰の鉛直軸の最高角速度（腰の回旋筋の強弱）。</summary>
    [TestMethod]
    public void CrossRanges() => Report("crossrange", [.. Enumerable.Range(0, 9)], i =>
    {
        var range = 0.97f + (0.02f * i);
        float Energy(FighterTuning tuning, float telegraph)
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, telegraph, tuning);
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var f = bout.Game.Player;
            while (bout.PunchTime is { } t && t < 0.45f)
            {
                bout.Step();
                foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                {
                    return hit.Energy;
                }
            }

            return 0f;
        }

        float PelvisRate(FighterTuning tuning)
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, BotStyle.MaxTelegraph, tuning with { HipTwistShare = 0.45f, ChestTwistResponseSeconds = 0.025f }, new PunchLab.DriveKnobs());
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var rate = 0f;
            while (bout.PunchTime is { } t && t < 0.25f)
            {
                rate = t >= 0f ? MathF.Max(rate, bout.Game.World.GetState(bout.Game.Player.Parts.Pelvis).AngularVelocity.Y) : rate;
                bout.Step();
            }

            return rate;
        }

        var d = FighterTuning.Default;
        var weak = d with { PelvisRotatorTorque = 60f };
        return F($"range={range:0.00} quick={Energy(d, 0.06f):0} quickWeak={Energy(weak, 0.06f):0} loaded={Energy(d, 0.25f):0} loadedWeak={Energy(weak, 0.25f):0} pelvisRate={PelvisRate(d):0.00} pelvisRateStrong={PelvisRate(d with { PelvisRotatorTorque = d.TrunkRotatorTorque }):0.00}");
    });

    /// <summary>寄る間合いを振った、ストレート系のテストの量（TrunkTwistTests・PunchWeightTests・PunchPathTests の 1 試行の関数）。</summary>
    [TestMethod]
    public void PunchFamily() => Report("punchfamily", [.. Enumerable.Range(0, 9)], i =>
    {
        var range = 0.97f + (0.02f * i);
        var legacy = FighterTuning.Default with { TrunkRotatorTorque = 28.2f, TrunkTwistFeedForward = 0f };
        var baseline = TrunkTwistTests.Punch(legacy, range);
        var strong = TrunkTwistTests.Punch(legacy with { TrunkRotatorTorque = 120f }, range);
        var ff = TrunkTwistTests.Punch(legacy with { TrunkTwistFeedForward = 1f }, range);
        var all = TrunkTwistTests.Punch(FighterTuning.Default, range);
        float Pelvis(FighterTuning t)
        {
            try
            {
                return TrunkTwistTests.LoadedCross(t, range);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return float.NaN;
            }
        }

        var d = FighterTuning.Default;
        return F($"range={range:0.00} chestLegacy={baseline.ChestRate:0.00} chestStrong={strong.ChestRate:0.00} chestFf={ff.ChestRate:0.00} chestAll={all.ChestRate:0.00} shoulderLegacy={baseline.ShoulderForward:0.00} shoulderAll={all.ShoulderForward:0.00} ")
            + F($"pelvis={Pelvis(d):0.00} pelvisStrong={Pelvis(d with { PelvisRotatorTorque = d.TrunkRotatorTorque }):0.00} uniform={PunchPathTests.PeakFistSpeed(true, range):0.00} smooth={PunchPathTests.PeakFistSpeed(false, range):0.00} ")
            + F($"quick={PunchWeightTests.Cross(d, 0.06f, range: range):0} quickWeak={PunchWeightTests.Cross(d with { PelvisRotatorTorque = 60f }, 0.06f, range: range):0} loaded={PunchWeightTests.Cross(d, 0.25f, range: range):0} loadedWeak={PunchWeightTests.Cross(d with { PelvisRotatorTorque = 60f }, 0.25f, range: range):0}");
    });

    [TestMethod]
    public void Settle() => Report("settle", [.. Enumerable.Range(0, 9)], i =>
    {
        var range = 0.97f + (0.02f * i);
        try
        {
            return F($"range={range:0.00} settled=True smooth={PunchPathTests.PeakFistSpeed(false, range):0.00}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return F($"range={range:0.00} settled=False");
        }
    });

    [TestMethod]
    public void GroundStrikeNoReferee() => Report("groundstrike", Seeds, seed =>
    {
        var off = GroundStrikeTests.Drill(BotCatalog.Boxer, referee: false, seed: seed);
        return F($"downed={off.Downed} hits={off.Hits} started={off.Started} pass={off.Downed && off.Hits > 0}");
    });

    [TestMethod]
    public void WvcSlip() => Report("wvcslip", [.. Enumerable.Range(1, 8)], seed =>
    {
        const float dt = 1f / 60f;
        var albert = CharacterRoster.Get("albert");
        var ramon = CharacterRoster.Get("ramon");
        var red = RosterLeague.ContenderOf(albert, ramon.Id, null);
        var blue = RosterLeague.ContenderOf(ramon, albert.Id, null);
        var wvc = Organizations.Get("wvc");
        var corners = TvShowRules.Stage(VideoMaker.WithCharacters(VideoMaker.Corners(
            red, blue, seed, albert.Wear, ramon.Wear, albert.Weapon, ramon.Weapon,
            blue.Create.Invoke, red.Create.Invoke), albert, ramon), wvc);
        var rules = TvShowRules.Rules(3, 20f);
        using var game = new BoxingGame(seed: seed, corners: corners, rules: rules, mood: wvc.ArenaMood);
        var wasStanding = true;
        var falls = 0;
        var hits = 0;
        var ko = false;
        for (var step = 0; step < (rules.TotalSeconds + 9f) / dt && !game.MatchOver; step++)
        {
            game.Step(default, dt);
            if (game.Resting)
            {
                wasStanding = game.Player.State == FighterState.Standing;
                continue;
            }

            var standing = game.Player.State == FighterState.Standing;
            falls += wasStanding && !standing && game.Player.Consciousness >= 0.98f ? 1 : 0;
            wasStanding = standing;
            hits += game.RecentHits.Count;
            ko |= game.Cpu.IsKnockedDown && game.Cpu.Consciousness < 0.30f;
        }

        return F($"falls={falls} hits={hits} ko={ko} pass={falls <= 1 && hits >= 3 && ko}");
    });

    [TestMethod]
    public void WindBack() => Report("windback", [.. Enumerable.Range(1, Environment.GetEnvironmentVariable("WB_TRACE") == "1" ? 1 : 8)], seed =>
    {
        var attackers = new List<BoxerBot>();
        var attacker = DefenseDrill.PunchAttacker("2", telegraph: 0.2014f);
        var corners = new BoxingCorners("a", s => { var b = new BoxerBot(attacker, s); attackers.Add(b); return b; }, s => new BoxerBot(PredictionTests.SlipDefender(0f) with { DefenseRate = 0f }, s));
        using var game = new BoxingGame(seed: seed, corners: corners);
        float Twist()
        {
            var toward = Vector3.Normalize((game.Player.Frame.Origin - game.Cpu.Frame.Origin) with { Y = 0f });
            return Vector3.Dot(game.Cpu.ShoulderPosition(Hand.Right) - game.Cpu.ShoulderPosition(Hand.Left), toward) / game.Cpu.ArmReach;
        }

        var thrownAt = -1f;
        var twistAt = 0f;
        for (var t = 0f; t < 10.07f; t += Dt)
        {
            game.Step(default, Dt);
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            if (thrownAt < 0f && thrown > 0)
            {
                thrownAt = t;
                twistAt = Twist();
            }

            if ((thrownAt >= 0f || game.Cpu.Footwork.Steps > 0) && seed == 1 && Environment.GetEnvironmentVariable("WB_TRACE") == "1" && (int)(t / Dt) % 6 == 0)
            {
                var c = game.Cpu;
                Vector3 Toe(int foot) => c.Frame.WorldToLocal(game.World.GetState(c.ToeId(foot)).Position);
                Console.WriteLine(F($"  t={t:0.00} thrown={thrownAt >= 0f} dt={t - thrownAt:0.000} gap={new Vector2(game.Cpu.ChestPosition.X - game.Player.ChestPosition.X, game.Cpu.ChestPosition.Z - game.Player.ChestPosition.Z).Length():0.00} twist={Twist() - twistAt:0.000} sw={c.Footwork.Swinging} steps={c.Footwork.Steps} lead=({Toe(0).X:0.00},{Toe(0).Z:0.00}) rear=({Toe(1).X:0.00},{Toe(1).Z:0.00}) yaw={c.Frame.YawRadians:0.000} pelvisV=({c.PelvisVelocity.X:0.00},{c.PelvisVelocity.Z:0.00})"));
            }

            if (thrownAt >= 0f && t >= thrownAt + 0.1678f)
            {
                var d = Twist() - twistAt;
                return F($"windBack={d:0.000} pass={d < -0.01f}");
            }
        }

        return "nothrow";
    });

    /// <summary>PunchWeightTests の溜めない右ストレート（腰の強弱）を、間合い 5 通り × 種で。</summary>
    [TestMethod]
    public void HipsSample() => Report("hips", [.. Enumerable.Range(1, 4)], seed =>
    {
        float[] ranges = [0.95f, 1.0f, 1.05f, 1.1f, 1.15f];
        var d = FighterTuning.Default;
        var quick = ranges.Sum(r => PunchWeightTests.Cross(d, 0.06f, seed, r));
        var weak = ranges.Sum(r => PunchWeightTests.Cross(d with { PelvisRotatorTorque = 60f }, 0.06f, seed, r));
        return F($"quick={quick:0} quickWeak={weak:0} pass={quick > 1.15f * weak}");
    });

    /// <summary>DownRecoveryTests の深いダウン（master の形: 0.84 秒より後に意識が力の抜ける線より下だった回を数える）。</summary>
    [TestMethod]
    public void DeepLimp() => Report("deeplimp", Seeds, seed =>
    {
        using var game = StillGame(seed, fromCorners: false);
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var f = game.Cpu;
        var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.02817f;
        var stand = f.PelvisPosition.Y - ground;
        f.Concussion = 0.95f;
        var samples = 0;
        var lowest = 1f;
        var lowestKo = 1f;
        for (var t = 0f; t < 6f; t += Dt)
        {
            game.Step(default, Dt);
            var ratio = (game.Cpu.PelvisPosition.Y - ground) / stand;
            if (t > 0.8392f && game.Cpu.Consciousness < FighterTuning.Default.LimpConsciousness)
            {
                samples++;
                lowest = MathF.Min(lowest, ratio);
            }

            lowestKo = t > 0.8392f && game.Cpu.Consciousness < FighterTuning.Default.KnockoutConsciousness ? MathF.Min(lowestKo, ratio) : lowestKo;
        }

        return F($"limpSamples={samples} wentLimp={samples > 0} lowestLimp={lowest:0.00} lowestKo={lowestKo:0.00} flat={lowestKo < 0.3f}");
    });

    /// <summary>EvasionBasicsTests の早いスリップ: 3.6 秒の中で打った本数（1 本であること）と頭に当たった数。</summary>
    [TestMethod]
    public void EarlySlip() => Report("earlyslip", [.. Enumerable.Range(1, 16)], seed =>
    {
        var inside = new DefenseMove { HeadX = 0.5f, HeadY = -0.5f, HeadZ = -0.2f, Step = 1f, Duration = 0.5035f, Rate = 35.75f, HandsUp = 0.3f, VsStraight = 1f };
        var book = DefenseBook.Default.With(Defense.Duck, inside);
        static void KeepFresh(BoxingGame game, BoxerBot _)
        {
            game.Player.Stamina = 1f;
            game.Cpu.Stamina = 1f;
        }

        var open = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: false, counters: false), seed: seed, seconds: 3.6f, telegraph: 0.1343f, observe: KeepFresh);
        var slip = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: true, counters: false), seed: seed, seconds: 3.6f, cue: 0f, telegraph: 0.1343f, observe: KeepFresh);
        return F($"openPunches={open.Punches} slipPunches={slip.Punches} openHead={open.HeadHits + open.OpenHeadHits} slipHead={slip.HeadHits + slip.OpenHeadHits} onePunch={open.Punches == 1 && slip.Punches == 1}");
    });

    /// <summary>1 場面のテスト 3 つを、寄る位置・間合いを 9 通りに振って（かかと落とし・肘の速さ・胸の回転）。</summary>
    [TestMethod]
    public void Fragile() => Report("fragile", [.. Enumerable.Range(0, 9)], i =>
    {
        var closeIn = (i - 4) * 0.01f;
        var axe = KickContactTests.Run(KickTechnique.Axe, closeIn: closeIn).MaxYawDeviation;
        var side = KickContactTests.Run(KickTechnique.Side, closeIn: closeIn).MaxYawDeviation;
        var scale = 0.96f + (0.01f * i);
        var slow = ElbowContactTests.Run(ElbowTechnique.Horizontal, durationScale: 1.4f, distanceScale: scale);
        var fast = ElbowContactTests.Run(ElbowTechnique.Horizontal, durationScale: 0.8f, distanceScale: scale);
        var range = 0.97f + (0.02f * i);
        var legacy = FighterTuning.Default with { TrunkRotatorTorque = 28.2f, TrunkTwistFeedForward = 0f };
        var chestLegacy = TrunkTwistTests.Punch(legacy, range).ChestRate;
        var chestFf = TrunkTwistTests.Punch(legacy with { TrunkTwistFeedForward = 1f }, range).ChestRate;
        return F($"axe={axe:0.00} side={side:0.00} slowJ={slow.MaxSpecificEnergy:0.00} fastJ={fast.MaxSpecificEnergy:0.00} slowHits={slow.Hits} fastHits={fast.Hits} chestLegacy={chestLegacy:0.00} chestFf={chestFf:0.00}");
    });

    /// <summary>かかと落とし・横蹴りの体の回り、肘の速さの比べを、左右 × 技の長さ・間合いを振って。</summary>
    [TestMethod]
    public void Fragile2() => Report("fragile2", [.. Enumerable.Range(0, 6)], i =>
    {
        var side = i % 2 == 0 ? Hand.Right : Hand.Left;
        var duration = 0.9f + (0.1f * (i / 2));
        var axe = KickContactTests.Run(KickTechnique.Axe, side, duration).MaxYawDeviation;
        var sideKick = KickContactTests.Run(KickTechnique.Side, side, duration).MaxYawDeviation;
        var scale = 0.85f + (0.15f * (i / 2));
        var slow = ElbowContactTests.Run(ElbowTechnique.Horizontal, side, durationScale: 1.4f, distanceScale: scale);
        var fast = ElbowContactTests.Run(ElbowTechnique.Horizontal, side, durationScale: 0.8f, distanceScale: scale);
        return F($"side={side} dur={duration:0.0} scale={scale:0.00} axe={axe:0.00} sideKick={sideKick:0.00} slowJ={slow.MaxSpecificEnergy:0.00} fastJ={fast.MaxSpecificEnergy:0.00} slowHits={slow.Hits} fastHits={fast.Hits} slowSpeed={slow.MaxElbowSpeed:0.00} fastSpeed={fast.MaxElbowSpeed:0.00}");
    });

    /// <summary>IntentFeelTests のカメラ基準の移動（相手との線に直角へ 1.7 秒）を、足の動きを追いながら。</summary>
    [TestMethod]
    public void CameraSide() => Report("cameraside", [43], seed =>
    {
        const float dt = 1f / 60f;
        var game = new BoxingGame(null, seed: seed);
        var control = new IntentControl();
        for (var i = 0; i < 101; i++)
        {
            game.Step(new BoxingInput(control.Read(game.PlayerSense, dt)), dt);
        }

        using var _ = game;
        var before = game.Player.PelvisPosition;
        var yaw = (MathF.PI / 2f) + CameraFollow.BehindYaw(game.PlayerSense.Self.Origin, game.PlayerSense.OpponentChest, 0f);
        var expected = CameraFollow.ToWorld(Vector2.UnitY, yaw);
        control.Input = new FighterIntent(Vector2.Zero, World: expected);
        for (var i = 0; i < 101; i++)
        {
            game.Step(new BoxingInput(control.Read(game.PlayerSense, dt)), dt);
            var p = game.Player;
            Vector3 Toe(int foot) => p.Frame.WorldToLocal(game.World.GetState(p.ToeId(foot)).Position);
            if (i % 3 == 0)
            {
                Console.WriteLine(F($"  i={i} moved=({(p.PelvisPosition - before).X:0.00},{(p.PelvisPosition - before).Z:0.00}) v=({p.PelvisVelocity.X:0.00},{p.PelvisVelocity.Z:0.00}) sw={p.Footwork.Swinging} steps={p.Footwork.Steps} lead=({Toe(0).X:0.00},{Toe(0).Z:0.00}) rear=({Toe(1).X:0.00},{Toe(1).Z:0.00}) state={p.State} hits={game.RecentHits.Count}"));
            }
        }

        var moved = game.Player.PelvisPosition - before;
        return F($"moved=({moved.X:0.00},{moved.Z:0.00}) expected=({expected.X:0.00},{expected.Y:0.00})");
    });

    [TestMethod]
    public void CounterDamage() => Report("counter", [.. Enumerable.Range(1, 16)], seed =>
    {
        var with = DefenseDrill.Punch("2", Defense.Slip, DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, true), seed: seed, seconds: 23.5f, cue: 0.06714f);
        var without = DefenseDrill.Punch("2", Defense.Slip, DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, false), seed: seed, seconds: 23.5f, cue: 0.06714f);
        return F($"with={with.CounterDamage:0.0} without={without.CounterDamage:0.0}");
    });

    [TestMethod]
    public void CounterTrace() => Report("countertrace", [1, 2], seed =>
    {
        var gaps = new List<float>();
        BoxerBot? last = null;
        var punches = 0;
        DefenseDrill.Punch("2", Defense.Slip, DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, true), seed: seed, seconds: 23.5f, cue: 0.06714f,
            observe: (game, bot) =>
            {
                last = bot;
                gaps.Add(Vector3.Distance(game.Player.ChestPosition with { Y = 0f }, game.Cpu.ChestPosition with { Y = 0f }));
                punches = game.Player.Footwork.Steps;
                if (seed == -1 && gaps.Count <= 900 && (game.RecentHits.Count > 0 || bot.CurrentDefense != Defense.None || game.Player.State != FighterState.Standing) && gaps.Count % 2 == 0)
                {
                    var p = game.Player;
                    Vector3 Toe(int foot) => game.World.GetState(p.ToeId(foot)).Position;
                    var lead = p.Frame.WorldToLocal(Toe(0));
                    var rear = p.Frame.WorldToLocal(Toe(1));
                    var head = p.Frame.WorldToLocal(p.HeadPosition);
                    Console.WriteLine(F($"  t={gaps.Count / 60f:0.00} gap={gaps[^1]:0.00} lead=({lead.X:0.00},{lead.Z:0.00}) rear=({rear.X:0.00},{rear.Z:0.00}) head=({head.X:0.00},{head.Y:0.00},{head.Z:0.00}) sw={p.Footwork.Swinging} steps={p.Footwork.Steps} def={bot.CurrentDefense} state={p.State} hits={string.Join(",", game.RecentHits.Where(h => h.Victim == p).Select(h => h.Zone))}"));
                }
            });
        var defenses = last!.Stats.Defenses.Values.Sum(d => d.Attempts);
        return F($"gapMean={gaps.Average():0.000} gapMin={gaps.Min():0.000} gapMax={gaps.Max():0.000} countersThrown={last.Stats.CountersThrown} countersLanded={last.Stats.CountersLanded} defenses={defenses} defenderSteps={punches}");
    });

    [TestMethod]
    public void IvfThirdKnockdown() => Report("ivf3kd", IvfSeeds, seed =>
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)),
            rules: MatchRules.Tv with { RoundSeconds = 120f, Rounds = 1, KnockdownLimit = 3 });
        var downs = 0;
        var wasDown = false;
        var upFor = 0f;
        for (var t = 0f; t < 100f && !game.RoundOver && downs < 4; t += Dt)
        {
            game.Step(default, Dt);
            var down = game.Cpu.IsKnockedDown;
            downs += down && !wasDown ? 1 : 0;
            wasDown = down;
            upFor = game.Cpu.State == FighterState.Standing && !down ? upFor + Dt : 0f;
            if (upFor > 1f)
            {
                game.Cpu.Knockdown(-game.Cpu.Frame.Forward, permanent: false);
                upFor = 0f;
            }

            var c = game.Cpu;
            if (seed == 1 && Environment.GetEnvironmentVariable("IVF_TRACE") == "1" && ((int)(t / Dt) % 24 == 0 || (t > 1f && t < 2.2f && game.RecentHits.Count > 0)))
            {
                Console.WriteLine(F($"  t={t:0.0} state={c.State} down={down} conc={c.Concussion:0.00} aware={c.Consciousness:0.00} gap={new Vector2(c.ChestPosition.X - game.Player.ChestPosition.X, c.ChestPosition.Z - game.Player.ChestPosition.Z).Length():0.00} pelY={c.PelvisPosition.Y:0.000} hits={string.Join(",", game.RecentHits.Select(h => (h.Attacker == c ? "cpu" : "pl") + ">" + (h.Victim == c ? "cpu" : "pl") + ":" + h.Zone))}"));
            }
        }

        return F($"downs={downs} over={game.RoundOver} message=\"{game.Message}\" pass={game.Message.StartsWith("TKO — 3 KNOCKDOWNS", StringComparison.Ordinal) && downs == 3}");
    });

    [TestMethod]
    public void StaminaRush() => Report("rush", Seeds, seed =>
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: seed, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        var rushing = 0f;
        var clock = 0f;
        var tap = 0f;
        var second = 0f;
        var peak = 0f;
        const float rushDt = 1f / 60f; // StaminaRushTests と同じ刻み
        var windows = new (double Sum, int Count)[4];
        while (rushing < 20f - 1e-3f && clock < 60f)
        {
            var standing = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            var phase = tap / 0.2014f;
            var index = (int)MathF.Floor(phase);
            var down = standing && phase - index < 0.67f;
            control.Input = new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
            game.Step(new BoxingInput(default), rushDt);
            clock += rushDt;
            if (!standing)
            {
                continue;
            }

            tap += rushDt;
            rushing += rushDt;
            second += rushDt;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                peak = MathF.Max(peak, (game.Player.GloveVelocity(hand) - game.Player.ChestVelocity).Length());
            }

            if (second >= 0.8333f)
            {
                var w = Math.Min(3, (int)((rushing - 1e-3f) / 5f));
                windows[w] = (windows[w].Sum + peak, windows[w].Count + 1);
                second = 0f;
                peak = 0f;
            }
        }

        var m = windows.Select(w => w.Count > 0 ? w.Sum / w.Count : 0.0).ToArray();
        var pass = m[0] > 4.5 && m[1] < 0.8 * m[0] && m[1] > 0.4 * m[0] && m[3] > 0.25 * m[0] && m[3] < 0.6 * m[0] && Math.Abs(m[2] - m[3]) < 0.3 * m[3];
        return F($"w0={m[0]:0.00} w1={m[1]:0.00} w2={m[2]:0.00} w3={m[3]:0.00} pass={pass}");
    });
}
