using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 詰めのドリル（2026-11-17）: 意識の薄れた相手（脳しんとうを決まった深さで止め、打たれても深くも浅くもならない）へ、名簿のボットが詰めて打つ。
/// 相手は自分のボットのまま（体の張りが落ちて構えが下がる・ふらつく）。拳 1 本ごとに、どこに当たったか（顔・胴・ガード・空振り）と、
/// 空振りなら拳がいちばん頭に近づいた所が頭からどちらへ外れたか（上・下・横・手前・奥）を数える。
/// ユーザーの指摘「フィニッシュに向けたコンボが下手で、ガラ空きの顔にほぼ当たっていない」を、試合でなく同じ状況の繰り返しで測る。
/// </summary>
internal static class FinishLab
{
    private const float Dt = 1f / 240f;

    /// <summary>拳を出したとみなす、胸に対する拳の外向きの速さ [腕の長さ/秒]。</summary>
    private const float LaunchSpeed = 6f;

    /// <summary>1 本の拳を見る長さの上限 [s]（引き戻しで終わらなければここで切る）。</summary>
    private const float MaxPunchSeconds = 1f;

    /// <summary>前の拳を出してからこれより短い間に出た拳を「続けて出した拳」（コンビネーションの 2 発目以降）とみなす [s]。</summary>
    private const float FollowSeconds = 0.45f;

    internal sealed class Tally
    {
        public int Thrown, Face, Body, Guard, Miss, Over, Under, Side, Short, Long, Follow, FollowFace, FollowShort;
        public double ClosestHead, OpeningSum, Seconds, FaceDamage, LaunchFace, LaunchShort, HeadBack, ShortOut, FaceOut, Wind;
        public long OpeningSamples;

        /// <summary>技ごと: 出した数・狙った所（頭打ちは顔、胴打ちは胴）に当たった数・手前で外れた数・拳の前への最大の和・打ち出した時の狙いのずれの和。</summary>
        public Dictionary<string, (int Thrown, int OnTarget, int Short, double MaxOut, double AimError)> ByTechnique { get; } = [];

        /// <summary>拳がいちばん近づいた時の受ける側の守りごと: 拳の数・狙った所に当たった数。</summary>
        public Dictionary<Defense, (int Thrown, int OnTarget)> ByDefense { get; } = [];

        public void Add(Tally o)
        {
            Thrown += o.Thrown; Face += o.Face; Body += o.Body; Guard += o.Guard; Miss += o.Miss;
            Over += o.Over; Under += o.Under; Side += o.Side; Short += o.Short; Long += o.Long; Follow += o.Follow; FollowFace += o.FollowFace; FollowShort += o.FollowShort;
            HeadBack += o.HeadBack;
            ClosestHead += o.ClosestHead; OpeningSum += o.OpeningSum; OpeningSamples += o.OpeningSamples;
            foreach (var (defense, v) in o.ByDefense)
            {
                var w = ByDefense.GetValueOrDefault(defense);
                ByDefense[defense] = (w.Thrown + v.Thrown, w.OnTarget + v.OnTarget);
            }

            foreach (var (name, v) in o.ByTechnique)
            {
                var w = ByTechnique.GetValueOrDefault(name);
                ByTechnique[name] = (w.Thrown + v.Thrown, w.OnTarget + v.OnTarget, w.Short + v.Short, w.MaxOut + v.MaxOut, w.AimError + v.AimError);
            }

            Seconds += o.Seconds; FaceDamage += o.FaceDamage; Wind += o.Wind; LaunchFace += o.LaunchFace; LaunchShort += o.LaunchShort; ShortOut += o.ShortOut; FaceOut += o.FaceOut;
        }
    }

    private sealed class Punch
    {
        public float Time, ClosestHead = float.MaxValue;
        public Vector3 Offset; // 頭から拳への、いちばん近づいた所のずれ（打つ側の向き: X 横・Y 上・Z 奥）
        public HitZone? Zone;
        public float StartReach, Launch, HeadBack, MaxOut, AimError;
        public string Name = "";
        public bool ToBody;
        public bool Follow;
        public Defense Defense;
        public Vector3 HeadAtLaunch;
    }

    public static Tally Run(FighterCharacter attacker, FighterCharacter victim, float consciousness, int seed, float seconds,
        IReadOnlyDictionary<string, float>? set, AimGoal? aim = null, IReadOnlyDictionary<string, float>? victimSet = null)
    {
        // 狙いを固定するとき（胴打ちの守りを測る）は、打つ側のボットへ狙いだけを渡す（体・威力は変えない）。
        var attackStyle = aim is { } forced
            ? new Contender(attacker.Id, s => new BoxerBot(StyleSet.Apply(attacker.Resolve(victim.Id), set ?? new Dictionary<string, float>()), s, forcedAim: forced))
            : RosterLeague.ContenderOf(attacker, victim.Id, set);
        var victimStyle = victimSet is { Count: > 0 } ? StyleSet.Apply(victim.Resolve(attacker.Id), victimSet) : victim.Resolve(attacker.Id);
        BoxerBot? bot = null;
        BoxerBot? victimBot = null;
        var corners = new BoxingCorners(victimStyle.Name, s => victimBot = new BoxerBot(victimStyle, s), s =>
            {
                var control = attackStyle.Create(s);
                bot = control as BoxerBot;
                return control;
            },
            PlayerWear: attacker.Wear, CpuWear: victim.Wear, PlayerBody: attacker.Build, CpuBody: victim.Build);
        // 脳しんとうの深さを止める（打たれて深まらず、時間で戻らない）。同じ弱り方の相手へ繰り返し打つため。
        var tuning = FighterTuning.Default with { ConcussionGain = 0f, ConcussionRecoveryRate = 0f };
        using var game = new BoxingGame(tuning, seed: seed, corners: corners);
        var tally = new Tally();
        var punches = new Punch?[2];
        var sinceLaunch = 10f;
        var victimFighter = game.Cpu;
        var self = game.Player;
        var windStart = victimFighter.WindLost;
        for (var step = 0; step < (int)(seconds / Dt); step++)
        {
            victimFighter = game.Cpu;
            self = game.Player;
            victimFighter.Concussion = Math.Clamp(1f - consciousness, 0f, 1f);
            game.Step(default, Dt);
            if (!ReferenceEquals(self, game.Player))
            {
                // 倒れて次のラウンドが始まると体を作り直す（前の体の拳は数えない）。
                punches[0] = punches[1] = null;
                tally.Wind += victimFighter.WindLost - windStart;
                self = game.Player;
                victimFighter = game.Cpu;
                windStart = victimFighter.WindLost;
                continue;
            }

            var bothUp = self.State == FighterState.Standing && victimFighter.State == FighterState.Standing;
            if (bot is null)
            {
                continue;
            }

            if (bothUp)
            {
                tally.Seconds += Dt;
                tally.OpeningSum += bot.Opening;
                tally.OpeningSamples++;
            }

            var reach = self.ArmReach;
            var forward = Vector3.Normalize((victimFighter.ChestPosition - self.ChestPosition) with { Y = 0f });
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == self && !hit.Kick && punches[(int)hit.Hand] is { Zone: null } open)
                {
                    open.Zone = hit.Zone;
                    if (IsFace(hit.Zone))
                    {
                        tally.FaceDamage += hit.Damage;
                    }
                }
            }

            sinceLaunch += Dt;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var glove = self.GloveCenter(hand);
                var out_ = glove - self.ChestPosition;
                var (technique, toBody, aimWorld) = bot.PunchOf(hand);
                var punch = punches[(int)hand];
                if (punch is null)
                {
                    // 拳はボットが技を出した時から（守りでグローブを上げる動きを拳と数えない）。
                    if (bothUp && technique is not null)
                    {
                        var follow = sinceLaunch < FollowSeconds;
                        sinceLaunch = 0f;
                        punches[(int)hand] = new Punch
                        {
                            Follow = follow, HeadAtLaunch = victimFighter.HeadPosition, StartReach = out_.Length() / reach,
                            Launch = ((victimFighter.HeadPosition - self.ChestPosition) with { Y = 0f }).Length() / reach,
                            Name = $"{technique}{(toBody ? "（胴）" : "")}", ToBody = toBody,
                            AimError = Vector3.Distance(aimWorld, toBody ? victimFighter.ChestPosition : victimFighter.HeadPosition) / reach,
                        };
                    }

                    continue;
                }

                punch.Time += Dt;
                punch.MaxOut = MathF.Max(punch.MaxOut, Vector3.Dot(out_, forward) / reach);
                var toGlove = glove - victimFighter.HeadPosition;
                var distance = toGlove.Length() / reach;
                if (distance < punch.ClosestHead)
                {
                    punch.ClosestHead = distance;
                    punch.Offset = new Vector3(Vector3.Dot(toGlove, side), toGlove.Y, Vector3.Dot(toGlove, forward)) / reach;
                    punch.Defense = victimBot?.CurrentDefense ?? Defense.None;
                    punch.HeadBack = Vector3.Dot(victimFighter.HeadPosition - punch.HeadAtLaunch, forward) / reach;
                }

                if (technique is null || punch.Time > MaxPunchSeconds || !bothUp)
                {
                    if (bothUp || punch.Zone is not null)
                    {
                        Count(tally, punch);
                    }

                    punches[(int)hand] = null;
                }
            }
        }

        tally.Wind += game.Cpu.WindLost - windStart;
        return tally;
    }

    private static bool IsFace(HitZone zone) => zone is HitZone.Head or HitZone.Chin or HitZone.Temple or HitZone.Neck;

    private static void Count(Tally tally, Punch punch)
    {
        tally.Thrown++;
        tally.ClosestHead += punch.ClosestHead;
        var onTarget = punch.Zone is { } hitZone && (punch.ToBody ? !IsFace(hitZone) && hitZone is not (HitZone.Guard or HitZone.Shoulder or HitZone.Elbow) : IsFace(hitZone));
        var isShort = punch.Zone is null && punch.Offset.Z < 0f && -punch.Offset.Z >= MathF.Max(MathF.Abs(punch.Offset.X), MathF.Abs(punch.Offset.Y));
        var before = tally.ByTechnique.GetValueOrDefault(punch.Name);
        tally.ByTechnique[punch.Name] = (before.Thrown + 1, before.OnTarget + (onTarget ? 1 : 0), before.Short + (isShort ? 1 : 0), before.MaxOut + punch.MaxOut, before.AimError + punch.AimError);
        var byDefense = tally.ByDefense.GetValueOrDefault(punch.Defense);
        tally.ByDefense[punch.Defense] = (byDefense.Thrown + 1, byDefense.OnTarget + (onTarget ? 1 : 0));
        if (punch.Follow)
        {
            tally.Follow++;
            tally.FollowFace += punch.Zone is { } z && IsFace(z) ? 1 : 0;
            tally.FollowShort += punch.Zone is null && punch.Offset.Z < 0f && -punch.Offset.Z >= MathF.Max(MathF.Abs(punch.Offset.X), MathF.Abs(punch.Offset.Y)) ? 1 : 0;
        }
        switch (punch.Zone)
        {
            case { } zone when IsFace(zone):
                tally.Face++;
                tally.LaunchFace += punch.Launch;
                tally.FaceOut += punch.MaxOut;
                break;
            case HitZone.Guard or HitZone.Shoulder or HitZone.Elbow:
                tally.Guard++;
                break;
            case not null:
                tally.Body++;
                break;
            default:
                tally.Miss++;
                var o = punch.Offset;
                var most = MathF.Max(MathF.Abs(o.X), MathF.Max(MathF.Abs(o.Y), MathF.Abs(o.Z)));
                if (MathF.Abs(o.Y) >= most)
                {
                    if (o.Y > 0f) { tally.Over++; } else { tally.Under++; }
                }
                else if (MathF.Abs(o.X) >= most)
                {
                    tally.Side++;
                }
                else if (o.Z < 0f)
                {
                    tally.Short++;
                    tally.LaunchShort += punch.Launch;
                    tally.HeadBack += punch.HeadBack;
                    tally.ShortOut += punch.MaxOut;
                }
                else
                {
                    tally.Long++;
                }

                break;
        }
    }

    public static void Report(IReadOnlyList<string> attackers, IReadOnlyList<string> victims, IReadOnlyList<float> levels, int bouts, float seconds,
        IReadOnlyDictionary<string, float>? set, Action<string> log, AimGoal? aim = null, IReadOnlyDictionary<string, float>? victimSet = null)
    {
        var jobs = attackers.SelectMany(a => victims.Where(v => v != a).SelectMany(v => levels.SelectMany(l => Enumerable.Range(1, bouts).Select(s => (a, v, l, s))))).ToArray();
        var results = new Tally[jobs.Length];
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            i => results[i] = Run(CharacterRoster.Get(jobs[i].a), CharacterRoster.Get(jobs[i].v), jobs[i].l, jobs[i].s, seconds, set, aim, victimSet));
        log($"詰めのドリル: 試合 {bouts} 本 × {seconds:0} 秒（意識を止めた相手へ）{(set is { Count: > 0 } ? " 変更: " + string.Join(", ", set.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")) : "")}{(aim is { } a ? $" 狙い固定: {a}" : "")}{(victimSet is { Count: > 0 } ? " 受ける側の変更: " + string.Join(", ", victimSet.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")) : "")}\n");
        log("| 打つ側 | 受ける側 | 意識 | 拳 / 秒 | 顔 | 胴 | ガード | 空振り | 空振りの外れ（上・下・横・手前・奥） | 頭への最接近 [腕] | 読んだ開き | 顔のダメージ / 秒 | 打ち出した間合い（顔に当たった・手前で外れた）[腕] | 手前で外れた間に頭が下がった [腕] | 拳の前への最大（顔・手前）[腕] | 続けて出した拳の割合・その顔・その手前 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var all = new Tally();
        foreach (var group in jobs.Select((j, i) => (j, r: results[i])).GroupBy(x => (x.j.a, x.j.v, x.j.l)))
        {
            var t = new Tally();
            foreach (var (_, r) in group)
            {
                t.Add(r);
            }

            all.Add(t);
            log(Row($"{group.Key.a} | {group.Key.v} | {group.Key.l:0.00}", t));
        }

        log(Row("全体 | | ", all));
        log(string.Create(CultureInfo.InvariantCulture, $"\n受ける側が打撲で失ったスタミナ: {all.Wind:0} J（拳 1 本あたり {all.Wind / Math.Max(1, all.Thrown):0.0} J）"));
        log("");
        log("| 拳が来た時の受ける側の守り | 拳の数 | 狙った所に当たった |");
        log("|---|---|---|");
        foreach (var (defense, v) in all.ByDefense.OrderByDescending(kv => kv.Value.Thrown))
        {
            log(string.Create(CultureInfo.InvariantCulture, $"| {defense} | {v.Thrown} | {(double)v.OnTarget / v.Thrown:P0} |"));
        }

        log("");
        log("| 技 | 出した数 | 狙った所に当たった | 手前で外れた | 拳の前への最大 [腕] | 打ち出しの狙いのずれ [腕] |");
        log("|---|---|---|---|---|---|");
        foreach (var (name, v) in all.ByTechnique.OrderByDescending(kv => kv.Value.Thrown))
        {
            log(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {v.Thrown} | {(double)v.OnTarget / v.Thrown:P0} | {(double)v.Short / v.Thrown:P0} | {v.MaxOut / v.Thrown:0.00} | {v.AimError / v.Thrown:0.00} |"));
        }

        log("");
    }

    private static string Row(string head, Tally t)
    {
        string P(int n) => t.Thrown > 0 ? ((double)n / t.Thrown).ToString("P0", CultureInfo.InvariantCulture) : "-";
        string M(int n) => t.Miss > 0 ? ((double)n / t.Miss).ToString("P0", CultureInfo.InvariantCulture) : "-";
        return string.Create(CultureInfo.InvariantCulture,
            $"| {head} | {t.Thrown / Math.Max(1e-6, t.Seconds):0.00} | {P(t.Face)} | {P(t.Body)} | {P(t.Guard)} | {P(t.Miss)} | {M(t.Over)}・{M(t.Under)}・{M(t.Side)}・{M(t.Short)}・{M(t.Long)} | {t.ClosestHead / Math.Max(1, t.Thrown):0.00} | {t.OpeningSum / Math.Max(1, t.OpeningSamples):0.00} | {t.FaceDamage / Math.Max(1e-6, t.Seconds):0.0} | {t.LaunchFace / Math.Max(1, t.Face):0.00}・{t.LaunchShort / Math.Max(1, t.Short):0.00} | {t.HeadBack / Math.Max(1, t.Short):0.00} | {t.FaceOut / Math.Max(1, t.Face):0.00}・{t.ShortOut / Math.Max(1, t.Short):0.00} | {P(t.Follow)}・{(t.Follow > 0 ? ((double)t.FollowFace / t.Follow).ToString("P0", CultureInfo.InvariantCulture) : "-")}・{(t.Follow > 0 ? ((double)t.FollowShort / t.Follow).ToString("P0", CultureInfo.InvariantCulture) : "-")} |");
    }
}
