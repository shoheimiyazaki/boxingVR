using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 蹴りの間合いのドリル（kicklab、2026-11-17）: 蹴る人（名簿のテコンドー家の体）が、立って構えるだけの相手の胸へ、間合い（胸と胸の距離、腕の長さの倍数）を
/// 刻んで回し蹴りを 1 本ずつ蹴る。蹴りごとに「当たったか・当たったすねの点（膝から足首までの割合。0 = 膝、1 = 足首、1 を超えれば足の甲・先）・
/// 相手へ渡したエネルギー（<see cref="HitEvent.Energy"/>。<c>ApplyHit</c> が組織へ渡す値）・足の速さ・当たった足の部位」を表にする。
/// <para>
/// 足の運び（歩いて止まる位置）は半歩ごとにしか決まらず、決めた間合いにぴったり止まれない。なので表は<b>蹴り始めの実測の間合い</b>で 0.1 刻みに束ねる。
/// 間合いは、威力（渡したエネルギー）が最大で、かつ当たったすねの点が足首のそば（0.5〜1.2）に入る割合が高い所を採る。調整に使った種とは別の種
/// （<c>--confirm-seeds</c>）で、採った間合いと今の間合いを比べ直し、良いときだけ教科書へ書き込む（CLAUDE.md §1.8）。
/// 蹴りは試合と同じ道（<see cref="KickSpec.Evaluate"/> → <see cref="FighterCommand"/> → 体）を通る。
/// </para>
/// </summary>
internal static class KickLab
{
    private const float Dt = 1f / 240f;

    /// <summary>「足首のそば」とみなす、当たったすねの点の範囲（膝からの割合）。足首の先の足の甲は 1.1 前後に出る。</summary>
    internal const float BandLow = 0.5f;
    internal const float BandHigh = 1.2f;

    /// <summary>間合いの束の幅（腕の長さの倍数）。</summary>
    private const float BinWidth = 0.1f;

    internal sealed record Hit(float Fraction, float Energy, float Speed, KickPart Part, HitZone Zone);

    internal sealed record Trial(float Distance, IReadOnlyList<Hit> Hits);

    internal sealed record Row(float Distance, int Kicks, int Landed, int Hits, float MeanFraction, float InBand, float EnergyPerHit, float EnergyPerKick, float Speed, string Parts, string Zones);

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    public static void Report(string bodyId, KickTechnique kind, IReadOnlyList<float> distances, IReadOnlyList<int> seeds, IReadOnlyList<int> confirmSeeds,
        float? shinPoint, float? overshoot, Hand? onlySide, Action<string> log)
    {
        var body = bodyId == "standard" ? BodyBuild.Standard : CharacterRoster.Get(bodyId).Build;
        var spec = KickSpec.Of(kind);
        if (shinPoint is { } f)
        {
            spec = spec with { ShinPoint = f };
        }

        if (overshoot is { } o)
        {
            spec = spec with { Overshoot = o };
        }

        log($"# kicklab: {kind}（蹴る体 {bodyId}、すねの当てる点 ShinPoint {F(spec.ShinPoint)}、Overshoot {F(spec.Overshoot)}、現在の Standoff {F(spec.Standoff)}、PhysicsVersion {Fighter.PhysicsVersion}）");
        log("");
        log($"蹴る人が目標の間合いまで歩いて止まり、0.5 秒構えてから 1 本蹴る（目標 {string.Join(", ", distances.Select(d => F(d)))}。止まる位置は足の運びで決まるので、表は蹴り始めの実測の間合いを {F(BinWidth)} 刻みに束ねる）。相手は構えて立つだけ。種ごとに左右の脚を替える{(onlySide is { } side ? $"（今回は {side} だけ）" : "")}。");
        log($"`当たったすねの点` = 当たった瞬間の、膝 → 足首の線へ落とした割合（0 = 膝、1 = 足首、1 超 = 足の甲・先）。`帯内` = その点が {F(BandLow)}〜{F(BandHigh)}（下すね〜足首のそば〜足の甲）に入った当たりが渡したエネルギーの割合（腿・膝の軽い当たりを数えないため、エネルギーで重みを付ける）。");
        log("`エネルギー` = `HitEvent.Energy`（組織へ渡った J）。`/蹴り` は 1 本の蹴りで渡した合計。");
        log("");

        var tuned = Trials(body, spec, distances, seeds, onlySide);
        var tunedRows = Bins(tuned);
        log($"## 調整に使った種 {string.Join(",", seeds)}（蹴った数 {tuned.Count}）");
        Print(tunedRows, log);

        // 採る間合い: 蹴りが 5 本以上あり、帯内の割合が半分以上の束の中で、1 本あたりのエネルギーが最大の所（無ければ帯内の割合が最大の所）。
        var eligible = tunedRows.Where(r => r.Kicks >= 5).ToList();
        var candidates = eligible.Where(r => r.InBand >= 0.5f && r.Landed > 0).ToList();
        var best = (candidates.Count > 0 ? candidates.OrderByDescending(r => r.EnergyPerKick) : eligible.OrderByDescending(r => r.InBand)).First();
        log("");
        log($"採る間合い（調整の種）: **{F(best.Distance)}**（エネルギー/蹴り {F(best.EnergyPerKick)} J、帯内 {F(best.InBand * 100f, "0")}%）。現在の `Standoff` は {F(spec.Standoff)}。");

        if (confirmSeeds.Count > 0)
        {
            var confirmTrials = Trials(body, spec, distances, confirmSeeds, onlySide);
            var confirmRows = Bins(confirmTrials);
            log("");
            log($"## 使っていない種 {string.Join(",", confirmSeeds)} で比べ直し（蹴った数 {confirmTrials.Count}）");
            Print(confirmRows, log);
            Row? Near(float d) => confirmRows.Where(r => r.Kicks >= 2).OrderBy(r => MathF.Abs(r.Distance - d)).FirstOrDefault();
            var chosen = Near(best.Distance);
            var current = Near(spec.Standoff);
            log("");
            if (chosen is null || current is null)
            {
                log("比べ直しの標本が足りない（束に蹴りが 2 本以上ない）。");
            }
            else if (MathF.Abs(chosen.Distance - current.Distance) < 1e-4f)
            {
                log("採った間合いは今の間合いの束と同じ。");
            }
            else
            {
                // 採る条件: 使っていない種でも、(1) 当たったエネルギーの半分以上が帯内、(2) 9 割以上の蹴りが当たる、(3) エネルギー/蹴りが、蹴りが 5 本以上ある束の最大の半分以上。
                // 足の先（帯の外）で強く当たる束は、すねの中ほどで当てるという狙いから外れるので、エネルギーの最大だけでは選ばない。
                var bestAny = confirmRows.Where(r => r.Kicks >= 5).Select(r => r.EnergyPerKick).DefaultIfEmpty(0f).Max();
                var accept = chosen.InBand >= 0.5f && chosen.Landed >= 0.9f * chosen.Kicks && chosen.EnergyPerKick >= 0.5f * bestAny;
                log(accept
                    ? $"採る: 比べ直しでも、間合い {F(chosen.Distance)} は帯内 {F(chosen.InBand * 100f, "0")}%・当たった蹴り {chosen.Landed}/{chosen.Kicks}・エネルギー/蹴り {F(chosen.EnergyPerKick)} J（束の最大 {F(bestAny)} J の半分以上）。今の間合い {F(current.Distance)} は帯内 {F(current.InBand * 100f, "0")}%・{F(current.EnergyPerKick)} J。"
                    : $"採らない: 比べ直しでは、間合い {F(chosen.Distance)} は帯内 {F(chosen.InBand * 100f, "0")}%・当たった蹴り {chosen.Landed}/{chosen.Kicks}・{F(chosen.EnergyPerKick)} J（束の最大 {F(bestAny)} J）で条件に届かなかった。");
            }
        }
    }

    private static List<Trial> Trials(BodyBuild body, KickSpec spec, IReadOnlyList<float> distances, IReadOnlyList<int> seeds, Hand? onlySide)
    {
        var trials = new List<Trial>();
        foreach (var distance in distances)
        {
            for (var i = 0; i < seeds.Count; i++)
            {
                trials.Add(Run(body, spec, distance, seeds[i], onlySide ?? (i % 2 == 0 ? Hand.Right : Hand.Left)));
            }
        }

        return trials;
    }

    private static List<Row> Bins(List<Trial> trials) => trials
        .GroupBy(t => MathF.Round(t.Distance / BinWidth))
        .OrderBy(g => g.Key)
        .Select(g => Summarize(g.ToList()))
        .ToList();

    private static Row Summarize(List<Trial> trials)
    {
        var hits = trials.SelectMany(t => t.Hits).ToList();
        var parts = string.Join(" ", hits.GroupBy(h => h.Part).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}:{g.Count()}"));
        return new Row(trials.Average(t => t.Distance), trials.Count, trials.Count(t => t.Hits.Count > 0), hits.Count,
            hits.Count > 0 ? hits.Average(h => h.Fraction) : 0f,
            hits.Sum(h => h.Energy) > 0f ? hits.Where(h => h.Fraction is >= BandLow and <= BandHigh).Sum(h => h.Energy) / hits.Sum(h => h.Energy) : 0f,
            hits.Count > 0 ? hits.Average(h => h.Energy) : 0f,
            trials.Average(t => t.Hits.Sum(h => h.Energy)),
            hits.Count > 0 ? hits.Average(h => h.Speed) : 0f, parts, string.Join(" ", hits.GroupBy(h => h.Zone).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key}:{g.Count()}")));
    }

    private static void Print(IReadOnlyList<Row> rows, Action<string> log)
    {
        log("| 実測の間合い（腕の長さ倍） | 蹴った数 | 当たった蹴り | 当たり数 | 当たったすねの点（平均） | 帯内 | エネルギー/当たり [J] | エネルギー/蹴り [J] | 足の速さ [m/s] | 当たった足の部位 | 相手の当たった所 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
        {
            log($"| {F(r.Distance)} | {r.Kicks} | {r.Landed} | {r.Hits} | {F(r.MeanFraction)} | {F(r.InBand * 100f, "0")}% | {F(r.EnergyPerHit)} | {F(r.EnergyPerKick)} | {F(r.Speed)} | {r.Parts} | {r.Zones} |");
        }
    }

    /// <summary>1 本の蹴りの試験（目標の間合い <paramref name="standoff"/>、<paramref name="side"/> の脚）。</summary>
    internal static Trial Run(BodyBuild body, KickSpec spec, float standoff, int seed, Hand side)
    {
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
        var attacker = game.Player;
        var victim = game.Cpu;
        var reach = attacker.ArmReach;
        FighterCommand Command(Vector3 move, LegCommand? leg, float yaw, float twist)
        {
            var face = Vector3.Transform(victim.ChestPosition - attacker.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
            return new FighterCommand(move, face, new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
                TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
        }

        float Distance() => Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) / reach;

        // 目標の間合いまで歩く（歩幅の刻みで止まるので、実測の間合いは目標から外れる。表は実測で束ねる）。
        var target = standoff * (1f + (((seed % 3) - 1) * 0.01f));
        var stable = 0;
        for (var i = 0; i < 240 * 10 && stable < 90; i++)
        {
            var error = Distance() - target;
            if (MathF.Abs(error) < 0.03f)
            {
                stable++;
                game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), Dt);
                continue;
            }

            stable = 0;
            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            var gain = Math.Clamp(MathF.Abs(error) * 4f, 0.25f, 1f);
            game.Step(new BoxingInput(Command(toward * MathF.Sign(error) * gain, null, 0f, 0f)), Dt);
        }

        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), Dt);
        }

        var actual = Distance();
        var sense = game.PlayerSense;
        var aimWorld = spec.AimWorld(sense);
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var duration = BotCatalog.Taekwondo.KickDuration;
        var hits = new List<Hit>();
        var seen = new HashSet<HitEvent>();
        var steps = (int)((duration + 0.4f) / Dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var yaw = t <= duration ? spec.FaceYaw(side, t, duration) : 0f;
            var twist = t <= duration ? spec.TorsoTwist(side, t, duration) : 0f;
            var current = game.PlayerSense;
            var aim = current.Self.WorldToLocal(aimWorld);
            var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, aim, reach, current.SelfLeg(side)) : (LegCommand?)null;
            game.Step(new BoxingInput(Command(Vector3.Zero, leg, yaw, twist)), Dt);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Kick && hit.Attacker == attacker && seen.Add(hit))
                {
                    var knee = attacker.KneePosition(side);
                    var axis = attacker.AnklePosition(side) - knee;
                    var fraction = Vector3.Dot(hit.Point - knee, axis) / MathF.Max(1e-6f, axis.LengthSquared());
                    if (Environment.GetEnvironmentVariable("KICKLAB_VERBOSE") is not null)
                    {
                        Console.Error.WriteLine($"  hit d {actual:F2} seed {seed} {side} t {t:F2} {hit.KickerPart} fraction {fraction:F2} E {hit.Energy:F2} v {hit.Speed:F2} bend {attacker.KneeBend(side):F2}");
                    }

                    hits.Add(new Hit(fraction, hit.Energy, hit.Speed, hit.KickerPart, hit.Zone));
                }
            }
        }

        return new Trial(actual, hits);
    }
}
