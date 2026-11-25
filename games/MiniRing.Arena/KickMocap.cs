using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;

namespace MiniRing.Arena;

/// <summary>人の回し蹴りのモーションキャプチャ（data/mocap/kyokushin）の読み替えの表と、手本（Bots/kicks/*.json）の書き出し（kickmocap）。</summary>
internal static class KickMocap
{
    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    private static float Deg(float rad) => rad * 180f / MathF.PI;

    /// <summary>人形の 1 本の蹴り（技の値 <paramref name="spec"/> を試合と同じ道で。間合い <paramref name="standoff"/> の目標まで歩いて止まり、体重を軸足へ移してから。体重の移し方 weight は教科書の値で、null ならテコンドーの教科書）。</summary>
        internal static KneePractice.Outcome Doll(BodyBuild body, KickSpec spec, Hand side, float standoff, float duration, int seed, Action<BoxingGame, Vector3?, float>? onStep = null, Action<BoxingGame, LegCommand?, float>? onCommand = null, BodyBuild? victimBody = null, Bots.BotStyle? weight = null, FighterTuning? tuning = null)
    {
        using var game = new BoxingGame(tuning, seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body, CpuBody: victimBody));
        var attacker = game.Player;
        var victim = game.Cpu;
        var reach = attacker.ArmReach;
        const float dt = 1f / 240f;
        FighterCommand Command(Vector3 move, LegCommand? leg, float yaw, float twist, Vector3 head = default)
        {
            var face = Vector3.Transform(victim.ChestPosition - attacker.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
            return new FighterCommand(move, face, new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
                HeadOffset: head, TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
        }

        // 蹴る前に体重を軸足へ移す（教科書の KickShift・KickLeanForward、試合のボットと同じ: BoxerBot.KickLean）。頭を軸足の側へ寄せる指令だけで、
        // 体がそれを追って重心が軸足に乗ってから脚を上げる。軸足は蹴らない側の足: 後ろ（右）で蹴れば前足（前へ寄せる）、前（左）で蹴れば後ろ足（後ろへ）。
        var teach = weight ?? Bots.BotCatalog.Style("taekwondo");
        var toStance = side == Hand.Right ? 1f : -1f;
        var lean = new Vector3(0f, 0f, toStance * teach.KickLeanForward * reach);

        float Distance() => Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) / reach;

        var stable = 0;
        for (var i = 0; i < 4028 && stable < 151; i++)
        {
            var error = Distance() - standoff;
            if (MathF.Abs(error) < 0.03f)
            {
                stable++;
                game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), dt);
                continue;
            }

            stable = 0;
            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(Command(toward * MathF.Sign(error) * Math.Clamp(MathF.Abs(error) * 4f, 0.25f, 1f), null, 0f, 0f)), dt);
        }

        for (var i = 0; i < 604; i++)
        {
            game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), dt);
        }

        var shiftSteps = (int)(teach.KickShift / dt);
        for (var i = 0; i < shiftSteps; i++)
        {
            game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f, lean * ((i + 1f) / shiftSteps))), dt);
        }

        var aimWorld = spec.AimWorld(game.PlayerSense);
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        HitEvent? first = null;
        var fraction = 0f;
        var bendAtHit = 0f;
        var hitTime = 0f;
        for (var i = 0; i < (int)((duration + 0.5035f) / dt); i++)
        {
            var t = i * dt;
            var sense = game.PlayerSense;
            LegCommand? leg = t <= duration ? spec.Evaluate(side, t, duration, rest, sense.Self.WorldToLocal(aimWorld), reach, sense.SelfLeg(side), Hand.Left) : null;
            game.Step(new BoxingInput(Command(Vector3.Zero, leg, t <= duration ? spec.FaceYaw(side, t, duration) : 0f, t <= duration ? spec.TorsoTwist(side, t, duration) : 0f, t <= duration ? lean : Vector3.Zero)), dt);
            onCommand?.Invoke(game, leg, t);
            onStep?.Invoke(game, leg?.KneeLocal is { } k ? attacker.Frame.LocalToWorld(k) : null, t);
            if (first is null)
            {
                foreach (var hit in game.RecentHits)
                {
                    if (hit.Kick && hit.Attacker == attacker)
                    {
                        first = hit;
                        var knee = attacker.KneePosition(side);
                        var axis = attacker.AnklePosition(side) - knee;
                        fraction = Vector3.Dot(hit.Point - knee, axis) / MathF.Max(1e-6f, axis.LengthSquared());
                        bendAtHit = attacker.KneeBend(side);
                        hitTime = t;
                        break;
                    }
                }
            }
        }

        var fell = attacker.State != FighterState.Standing;
        return first is { } h
            ? new KneePractice.Outcome(h.Momentum, h.Damage, h.Energy, fraction, h.Speed, bendAtHit, hitTime, true, fell, h.KickerPart, h.Zone)
            : new KneePractice.Outcome(0f, 0f, 0f, 0f, 0f, 0f, 0f, false, fell, KickPart.None, default);
    }

    /// <summary>人の蹴り（棒人間）と、人形が手本でなぞった蹴りを 1 本の mp4 に（真横・斜め前、0.25 倍スロー）。</summary>
    public static void Video(string dir, string outPath, Action<string> log)
    {
        const int width = 1280, height = 720, fps = 30;
        const float slow = 0.25f;
        var body = Characters.CharacterRoster.Get("taeyun").Build;
        using var renderer = new Aigis.Render.HeadlessRenderer(width, height);
        using var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        { RedirectStandardInput = true, UseShellExecute = false }) ?? throw new IOException("ffmpeg を起動できません。");
        var white = new Vector4(1f);
        var gold = new Vector4(1f, 0.85f, 0.3f, 1f);

        void Stick(string file, string title)
        {
            var track = KickCapture.Load(Path.Combine(dir, file));
            var analysis = track.Analyze();
            var origin = (track.Point("RHJC", 0)!.Value + track.Point("LHJC", 0)!.Value) * 0.5f;
            var forward = Vector3.Normalize(new Vector3(analysis.Mitt.X - origin.X, 0f, analysis.Mitt.Z - origin.Z));
            var outward = -Vector3.Cross(Vector3.UnitY, forward); // 蹴る脚（右）の側から撮る
            var center = origin + (forward * 0.45f) + new Vector3(0f, -0.05f, 0f);
            var eye = center + (outward * 3.0f) + new Vector3(0f, 0.15f, 0f);
            var vp = Aigis.Render.Camera.ViewProjection(eye, center, Vector3.UnitY, 40f * MathF.PI / 180f, (float)width / height, 0.05f, 30f);
            var step = 200f * slow / fps;
            for (var f = 0f; f < track.FrameCount; f += step)
            {
                var i = (int)f;
                var lines = new List<Aigis.Render.DebugLine>();
                void Seg(string a, string b, Vector4 color)
                {
                    if (track.Point(a, i) is { } p && track.Point(b, i) is { } q)
                    {
                        lines.Add(new Aigis.Render.DebugLine(p, q, color));
                    }
                }

                var green = new Vector4(0.5f, 1f, 0.6f, 1f);
                var blue = new Vector4(0.6f, 0.8f, 1f, 1f);
                Seg("RHJC", "RKJC", gold);
                Seg("RKJC", "RAJC", gold);
                Seg("RAJC", "RTOE", gold);
                Seg("RAJC", "RHEE", gold);
                Seg("LHJC", "LKJC", blue);
                Seg("LKJC", "LAJC", blue);
                Seg("LAJC", "LTOE", blue);
                Seg("LAJC", "LHEE", blue);
                Seg("LHJC", "RHJC", green);
                Seg("LSJC", "RSJC", green);
                Seg("LHJC", "LSJC", green);
                Seg("RHJC", "RSJC", green);
                Seg("C7", "CLAV", green);
                for (var m = 1; m <= 6; m++)
                {
                    if (track.Point($"Tarcza{m}", i) is { } mp)
                    {
                        lines.Add(new Aigis.Render.DebugLine(mp - new Vector3(0.03f, 0f, 0f), mp + new Vector3(0.03f, 0f, 0f), white));
                        lines.Add(new Aigis.Render.DebugLine(mp - new Vector3(0f, 0.03f, 0f), mp + new Vector3(0f, 0.03f, 0f), white));
                    }
                }

                lines.Add(new Aigis.Render.DebugLine(origin with { Y = 0f } - (forward * 1f), origin with { Y = 0f } + (forward * 2f), new Vector4(0.4f, 0.4f, 0.45f, 1f)));
                var text = new Aigis.Render.ScreenTextDraw[]
                {
                    new($"人の蹴り（棒人間）: {title}", new Vector2(40f, 40f), 28f, white, Aigis.Render.TextAnchor.Left),
                    new($"{i / 200f:0.00} s  金 = 蹴る右脚  {slow:0.##} 倍スロー  白の十字 = ミット  出典 Szczęsna ら 2021 (CC0)", new Vector2(40f, 76f), 22f, gold, Aigis.Render.TextAnchor.Left),
                };
                ffmpeg.StandardInput.BaseStream.Write(renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.09f, 0.12f, 1f), vp, [], lines, text, cameraPosition: eye));
            }
        }

        void DollShot(KickSpec spec, Hand side, float standoff, string title)
        {
            foreach (var oblique in new[] { false, true })
            {
                var next = 0f;
                var duration = spec.Seconds!.Value;
                var o = KickMocap.Doll(body, spec, side, standoff, duration, 1, (game, goal, t) =>
                {
                    if (t < next)
                    {
                        return;
                    }

                    next = t + (slow / fps);
                    var lines = new List<Aigis.Render.DebugLine>();
                    if (goal is { } gp)
                    {
                        lines.Add(new Aigis.Render.DebugLine(game.Player.HipPosition(side), gp, gold));
                    }

                    KickVideo.Emit(renderer, ffmpeg, width, height, game, [
                        new($"人形の蹴り（人の動きから作った手本）: {title}（{(oblique ? "斜め前" : "真横")}）", new Vector2(40f, 40f), 26f, white, Aigis.Render.TextAnchor.Left),
                        new($"膝の曲げ {game.Player.KneeBend(side):0.00} rad  {t:0.00} s  {slow:0.##} 倍スロー  金の線 = 膝の制御点", new Vector2(40f, 76f), 22f, gold, Aigis.Render.TextAnchor.Left),
                    ], lines, 0f, oblique);
                });
                log($"doll {title} {(oblique ? "oblique" : "side")}: momentum {F(o.Momentum)} damage {F(o.Damage)} zone {o.Zone} part {o.Part} shin {F(o.Fraction)} bend {F(o.BendAtHit)}");
            }
        }

        Stick("kick_S03-T01_01.csv", "廻し蹴り下段（膝〜腰）");
        DollShot(KickSpec.Of(KickTechnique.Roundhouse), Hand.Right, 1.0f, "下段・右脚");
        DollShot(KickSpec.Of(KickTechnique.Roundhouse), Hand.Left, 1.0f, "下段・左脚");
        Stick("kick_S04-T01_01.csv", "廻し蹴り上段（肩〜頭）");
        DollShot(KickSpec.Of(KickTechnique.RoundhouseHigh), Hand.Left, 1.2f, "上段・左脚");
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
    }

    /// <summary>人形で蹴りの長さ・間合いを振って、手作り（段 2）と人の動きの手本を並べる。</summary>
    public static void Compare(string bodyId, IReadOnlyList<float> durations, IReadOnlyList<float> standoffs, Action<string> log)
    {
        var body = bodyId == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(bodyId).Build;
        log($"# kick_mocap: 人形の蹴り（体 {bodyId}、PhysicsVersion {Fighter.PhysicsVersion}）");
        log("");
        log("相手は構えて立つだけ。運動量・ダメージ・エネルギーは最初に当たった打撃（`HitEvent`）。すねの点 = 当たった瞬間の膝 → 足首の割合（1 超 = 足の甲・先）。曲げ = 当たった瞬間の実際の膝の曲げ。");
        log("");
        log("| 蹴り | 脚 | 長さ [s] | 間合い目標 | 当たった | 運動量 | ダメージ | エネルギー [J] | すねの点 | 曲げ [rad] | 当たった時刻 [s] | 足の部位 | 相手の所 | 倒れた |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var variants = new (string Name, KickSpec Spec)[]
        {
            ("手作り（段 2）", KickSpec.HandmadeRoundhouse),
            ("人・下段", KickSpec.Of(KickTechnique.Roundhouse)),
            ("人・上段", KickSpec.Of(KickTechnique.RoundhouseHigh)),
        };
        foreach (var (name, spec) in variants)
        {
            foreach (var side in new[] { Hand.Left, Hand.Right })
            {
                foreach (var duration in name.StartsWith("手作り", StringComparison.Ordinal) ? new[] { spec.Seconds!.Value } : durations)
                {
                    foreach (var standoff in standoffs)
                    {
                        var o = Doll(body, spec, side, standoff, duration, 1);
                        log($"| {name} | {side} | {F(duration)} | {F(standoff)} | {(o.Landed ? "当たった" : "外れ")} | {F(o.Momentum)} | {F(o.Damage)} | {F(o.Energy)} | {F(o.Fraction)} | {F(o.BendAtHit)} | {F(o.HitTime)} | {o.Part} | {o.Zone} | {(o.Fell ? "倒れた" : "")} |");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 中段の手本（roundhouse_mid.json）を、下段と上段の手本から高さの割合で混ぜて書き出す。割合 = (狙いの高さ / 脚の長さ − 下段のミット / 脚の長さ) /
    /// (上段のミット / 脚の長さ − 下段のミット / 脚の長さ)。人は 0.63・1.37 m のミットを自分の脚（腿 + すね）の長さで割り、人形は中段の狙い（胸と腰の中点）の高さを
    /// 人形の脚の長さで割る。
    /// </summary>
    public static void ExportMid(string dir, string exportDir, string bodyId, Action<string> log)
    {
        float Ratio(string tag)
        {
            var tracks = Directory.GetFiles(dir, $"kick_{tag}-*.csv").Select(f => KickCapture.Load(f).Analyze()).ToList();
            var mitt = tracks.Average(t => t.Mitt.Y - t.Samples[0].Ankle.Y); // 構えの足首からの高さ（人形も足首から測る）
            var leg = tracks.Average(t => t.Samples[t.ImpactIndex].ThighLength + t.Samples[t.ImpactIndex].ShinLength);
            log($"人 {tag}: ミットの高さ {F(mitt)} m / 脚 {F(leg)} m = {F(mitt / leg)}（{tracks.Count} 本）");
            return mitt / leg;
        }

        var low = Ratio("S03");
        var high = Ratio("S04");
        var body = bodyId == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(bodyId).Build;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
        var sense = game.PlayerSense;
        var pelvis = sense.OpponentBody == default ? sense.OpponentChest - new Vector3(0f, 0.5f * sense.ArmReach, 0f) : sense.OpponentBody.Pelvis;
        var aim = (sense.OpponentChest + pelvis) * 0.5f;
        var legBody = game.Player.LegBodyOf(Hand.Right);
        var floor = game.Player.AnklePosition(Hand.Right).Y;
        var height = aim.Y - floor;
        var legLength = legBody.Thigh + legBody.Shin;
        var doll = height / legLength;
        var share = Math.Clamp((doll - low) / (high - low), 0f, 1f);
        log($"人形（{bodyId}）: 中段の狙いの高さ {F(height)} m（足首から。胸と腰の中点）/ 脚 {F(legLength)} m = {F(doll)}  → 上段の手本の割合 {F(share)}");
        var mid = CapturedKick.Blend("roundhouse_mid", CapturedKick.Load("roundhouse_low"), CapturedKick.Load("roundhouse_high"), share);
        Directory.CreateDirectory(exportDir);
        File.WriteAllText(Path.Combine(exportDir, "roundhouse_mid.json"), mid.ToJson());
        log($"roundhouse_mid.json を書いた（当たる瞬間 {F(mid.ImpactShare)}、区間 {F(mid.HumanSeconds)} s）");
    }

    public static void Report(string dir, string? exportDir, Action<string> log)
    {
        log("# kickmocap: 極真空手の廻し蹴り（ミットを蹴る E02、右脚 12 本）の読み替え");
        log("");
        log("出典: Szczęsna, Błaszczyszyn, Pawlyta (2021) Sci Data 8:13、CC0。200 Hz。角は膝の制御の定義（`LegKnee`）: 方位 = 腿の軸が前（ミットの方向）から脚の外へ回った角、仰角 = 水平から上、ねじり・曲げ = `LegKnee.ForShin`（すねの向きから）。骨盤の回り = ミットの方向に対する骨盤の前の向き（左回りが正）。胸のひねり = 肩の回り − 骨盤の回り。");
        log("");
        log("| 蹴り | フレーム | 当たる時刻 [s] | 区間の長さ [s] | 当たる瞬間の 足首からミット [m] | 腿 [m] | すね [m] | 方位 [°] | 仰角 [°] | ねじり [°] | 曲げ [°] | 骨盤の回り [°] | 胸のひねり [°] | 膝の高さ / 腿（股から） |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var groups = new Dictionary<string, List<KickCapture.Track>>();
        foreach (var file in Directory.GetFiles(dir, "kick_*.csv").OrderBy(f => f, StringComparer.Ordinal))
        {
            var track = KickCapture.Load(file).Analyze();
            var s = track.Samples[track.ImpactIndex];
            var name = Path.GetFileName(file);
            var key = name.Contains("S02", StringComparison.Ordinal) ? "front_mid" : name.Contains("S03", StringComparison.Ordinal) ? "roundhouse_low" : "roundhouse_high";
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(track);
            log($"| {Path.GetFileNameWithoutExtension(file)} | {track.Samples.Length} | {F(track.ImpactTime)} | {F(track.Samples.Length / track.Rate)} | {F(track.AnkleToMitt)} | {F(s.ThighLength)} | {F(s.ShinLength)} | {F(Deg(s.Azimuth), "0")} | {F(Deg(s.Elevation), "0")} | {F(Deg(s.Twist), "0")} | {F(Deg(s.Bend), "0")} | {F(Deg(s.PelvisYaw), "0")} | {F(Deg(s.ShoulderTwist), "0")} | {F((s.Knee.Y - s.Hip.Y) / s.ThighLength)} |");
        }

        foreach (var (key, tracks) in groups)
        {
            var model = CapturedKick.FromTracks(key, tracks);
            log("");
            log($"## {key}（{tracks.Count} 本を当たる瞬間で時刻をそろえて平均。当たる瞬間 = 時間の {F(model.ImpactShare)}、区間の長さ平均 {F(model.HumanSeconds)} s）");
            log("| 時間 | 方位 [°] | 仰角 [°] | ねじり [°] | 曲げ [°] | 骨盤の回り [°] | 胸のひねり [°] |");
            log("|---|---|---|---|---|---|---|");
            for (var k = 0; k <= 10; k++)
            {
                var i = k * (model.Azimuth.Length - 1) / 10;
                log($"| {F(k / 10f, "0.0")} | {F(Deg(model.Azimuth[i]), "0")} | {F(Deg(model.Elevation[i]), "0")} | {F(Deg(model.Twist[i]), "0")} | {F(Deg(model.Bend[i]), "0")} | {F(Deg(model.Yaw[i]), "0")} | {F(Deg(model.Torso[i]), "0")} |");
            }

            if (exportDir is not null)
            {
                Directory.CreateDirectory(exportDir);
                File.WriteAllText(Path.Combine(exportDir, key + ".json"), model.ToJson());
            }
        }
    }
}
