using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 移動の出だしの引っかかりの物差し（2026-11-26、ユーザーの指摘「移動時、足が引っかかる場面がある。重心がかかっている足が出ない問題かも」）。
/// 試合（<see cref="BoxingGame"/>、相手は 4 m 先で構えて立つだけ）でプレイヤーをデスクトップの意図操作（<see cref="IntentControl"/>: 方向キー・弱/強・回避・ステップ）で動かし、
/// いろいろな足と重心の状態（前置き <see cref="Setup"/>）を作ってから、8 方向のどれかへ方向キーを入れ続ける。入れた瞬間からの骨盤と足を測る。
/// <list type="bullet">
/// <item>出だしの遅れ: 方向を入れてから、骨盤が指令の向きへ歩く速さの半分に達するまでの時間。</item>
/// <item>止まっている時間: 入れて 0.25 秒より後で、骨盤の指令の向きの速さが歩く速さの 25 % 未満で続いた最長の時間。</item>
/// <item>進んだ割合: 測る間の、骨盤の指令の向きの速さの平均 ÷ 歩く速さ。</item>
/// <item>足の遅れ: 踏み出している足の目標（足の運びの母指球）と実際のつま先の、水平の距離の最大。</item>
/// <item>最初の 1 歩: 入れてから足の運びが 1 歩目を踏み出すまでの時間と、その足に入れた瞬間に乗っていた体重の割合（重心の、両足を結ぶ線への射影）。</item>
/// </list>
/// </summary>
internal static class FootSnagDrill
{
    public const float Dt = 1f / 60f;
    public const float MeasureSeconds = 1.2f;

    /// <summary>止まっていると数える、骨盤の指令の向きの速さ（歩く速さに対する割合）。BootSnagDrill と同じ。</summary>
    public const float StalledRatio = 0.25f;

    /// <summary>前置き（足と重心の状態）。</summary>
    public enum Setup
    {
        /// <summary>止まって 2.5 秒（足を留めた構え）。</summary>
        Rest,

        /// <summary>ジャブ（弱の短押し）の途中: 体重が前足へ。</summary>
        Jab,

        /// <summary>ストレート（強の短押し）の途中: ひねりで体重が前足へ、後ろ足は母指球で回る。</summary>
        Straight,

        /// <summary>スウェー（後ろ＋回避）を 0.4 秒保った直後: 体重が後ろ足へ。</summary>
        Sway,

        /// <summary>前＋回避（ダッキング・スリップ）を 0.4 秒保った直後。</summary>
        Duck,

        /// <summary>前へ 0.8 秒歩いて離し、0.1 秒後（止まりかけの勢いが前足に乗る）。</summary>
        WalkStop,

        /// <summary>逆向きへ歩いていて、片足が宙にある瞬間に切り返す。</summary>
        Reverse,

        /// <summary>90 度ずれた向きへ歩いていて、片足が宙にある瞬間に向きを変える。</summary>
        Turn,

        /// <summary>前へステップ（ステップ＋前）して着いた直後。</summary>
        Step,

        /// <summary>前足で前蹴り（足首の目標）を蹴り終えた直後（後ろ足が軸足として留められていた）。</summary>
        KickLead,

        /// <summary>後ろ足で前蹴りを蹴り終えた直後（前足が軸足）。</summary>
        KickRear,

        /// <summary>倒れて立ち上がった直後。</summary>
        Risen,
    }

    /// <summary>向き（相手基準の方向キー: X が右、Y が前）。</summary>
    public static readonly (string Name, Vector2 Direction)[] Directions =
    [
        ("F", new(0f, 1f)), ("FR", Vector2.Normalize(new(1f, 1f))), ("R", new(1f, 0f)), ("BR", Vector2.Normalize(new(1f, -1f))),
        ("B", new(0f, -1f)), ("BL", Vector2.Normalize(new(-1f, -1f))), ("L", new(-1f, 0f)), ("FL", Vector2.Normalize(new(-1f, 1f))),
    ];

    public readonly record struct Result(Setup Setup, string Direction, float Latency, float LongestStall, float Progress, float MaxFootGap, float FirstStep,
        int FirstFoot, float FirstFootLoad, float LeadLoad, int Steps, bool Standing);

    public const int Variants = 5;

    public static IEnumerable<(Setup Setup, string Name, Vector2 Direction, int Variant)> Cases()
    {
        foreach (var setup in Enum.GetValues<Setup>())
        {
            foreach (var (name, direction) in Directions)
            {
                for (var v = 0; v < Variants; v++)
                {
                    yield return (setup, name, direction, v);
                }
            }
        }
    }

    /// <summary>同じ組（前置き × 向き）の種の中の最悪: 出だしと止まりは最大、進みは最小。</summary>
    public static Result Worst(IEnumerable<Result> results)
    {
        var list = results.ToList();
        var worst = list.MaxBy(r => r.Latency + r.LongestStall - r.Progress);
        return worst with { Latency = list.Max(r => r.Latency), LongestStall = list.Max(r => r.LongestStall), Progress = list.Min(r => r.Progress), MaxFootGap = list.Max(r => r.MaxFootGap) };
    }

    private static BoxingGame NewGame(FighterTuning? tuning) => new(tuning, seed: 7, corners: BoxingCorners.Default with
    {
        ExternalBlue = true,
        Ring = new RingLayout(10f, 4f),
        Referee = false,
        BreakActions = false,
    });

    private static FighterCommand Idle(Fighter f, Fighter other) => new(Vector3.Zero, other.PelvisPosition - f.PelvisPosition,
        new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));

    /// <summary>1 つの組を回す。</summary>
    public static Result Run(Setup setup, string name, Vector2 direction, FighterTuning? tuning = null, Action<string>? trace = null, int variant = 0)
    {
        using var game = NewGame(tuning);
        var control = new IntentControl();
        var walk = (tuning ?? FighterTuning.Default).WalkSpeed;
        Fighter P() => game.Player;
        FighterCommand Frame(FighterIntent intent, Func<FighterCommand, FighterCommand>? edit = null)
        {
            control.Input = intent;
            var command = control.Read(game.PlayerSense, Dt);
            command = edit?.Invoke(command) ?? command;
            game.Step(new BoxingInput(command, Blue: Idle(game.Cpu, game.Player)), Dt);
            return command;
        }

        void Hold(FighterIntent intent, float seconds)
        {
            for (var i = 0; i < (int)MathF.Round(seconds / Dt); i++)
            {
                Frame(intent);
            }
        }

        // 前置きの前に立つ時間を種ごとに少しずらす（同じ状態でも足の位置・体の揺れが少し違う組で、たまたまの結果を避ける）。
        Hold(default, 2.5f + (0.0833f * variant));
        switch (setup)
        {
            case Setup.Rest:
                break;
            case Setup.Jab:
                Hold(new FighterIntent(Vector2.Zero, Light: true), 2 * Dt);
                Hold(default, 0.15f);
                break;
            case Setup.Straight:
                Hold(new FighterIntent(Vector2.Zero, Heavy: true), 2 * Dt);
                Hold(default, 0.2f);
                break;
            case Setup.Sway:
                Hold(new FighterIntent(new Vector2(0f, -1f), Evade: true), 0.4f);
                break;
            case Setup.Duck:
                Hold(new FighterIntent(new Vector2(0f, 1f), Evade: true), 0.4f);
                break;
            case Setup.WalkStop:
                Hold(new FighterIntent(new Vector2(0f, 1f)), 0.8f);
                Hold(default, 0.1f);
                break;
            case Setup.Reverse:
            case Setup.Turn:
            {
                var before = setup == Setup.Reverse ? -direction : new Vector2(-direction.Y, direction.X);
                Hold(new FighterIntent(before), 0.6f);
                for (var i = 0; i < 60 && P().Footwork.Swinging < 0; i++)
                {
                    Frame(new FighterIntent(before));
                }

                break;
            }
            case Setup.Step:
                Hold(new FighterIntent(new Vector2(0f, 1f), Step: true), 2 * Dt);
                Hold(default, 0.35f);
                break;
            case Setup.KickLead:
            case Setup.KickRear:
            {
                var side = setup == Setup.KickLead ? Hand.Left : Hand.Right;
                const int frames = 43; // 0.72 秒（FrontLegStrike の長さ）
                for (var i = 0; i < frames; i++)
                {
                    var leg = game.PlayerSense.SelfLeg(side);
                    var length = leg.Thigh + leg.Shin;
                    var aim = leg.Hip + new Vector3(0f, -0.3f * length, length);
                    var kick = FrontLegStrike.At(leg, aim, (float)i / frames, false, side);
                    Frame(default, c => side == Hand.Left ? c with { LeftLeg = kick } : c with { RightLeg = kick });
                }

                break;
            }
            case Setup.Risen:
            {
                P().Knockdown(-P().Frame.Forward, permanent: false);
                var standing = 0;
                for (var i = 0; i < 900 && standing < 12; i++)
                {
                    Frame(default);
                    standing = P().State == FighterState.Standing ? standing + 1 : 0;
                }

                break;
            }
        }

        // 入れた瞬間の、前足に乗っている体重（重心の両足を結ぶ線への射影: 0 = 後ろ足、1 = 前足）。
        var world = game.World;
        Vector2 Ball(int foot)
        {
            var toe = world.GetState(P().ToeId(foot)).Position;
            return new Vector2(toe.X, toe.Z);
        }

        float LeadShare()
        {
            var lead = Ball(Footwork.Lead);
            var rear = Ball(Footwork.Rear);
            var com = P().CenterOfMass;
            var line = lead - rear;
            return line.LengthSquared() < 1e-8f ? 0.5f : Math.Clamp(Vector2.Dot(new Vector2(com.X, com.Z) - rear, line) / line.LengthSquared(), 0f, 1f);
        }

        var leadLoad = LeadShare();
        var steps0 = P().Footwork.Steps;
        var swinging0 = P().Footwork.Swinging;
        var latency = float.NaN;
        var firstStep = float.NaN;
        var firstFoot = -1;
        var stallRun = 0f;
        var longest = 0f;
        var along = 0f;
        var maxGap = 0f;
        var frames0 = (int)MathF.Round(MeasureSeconds / Dt);
        for (var i = 0; i < frames0; i++)
        {
            var command = Frame(new FighterIntent(direction));
            var t = (i + 1) * Dt;
            var move = command.ClampedMove;
            var dir = move.LengthSquared() > 1e-8f ? Vector3.Normalize(move) : Vector3.Zero;
            var v = P().PelvisVelocity;
            var speed = Vector3.Dot(new Vector3(v.X, 0f, v.Z), dir);
            along += speed;
            if (float.IsNaN(latency) && speed >= 0.5f * walk)
            {
                latency = t;
            }

            if (t > 0.25f && speed < StalledRatio * walk)
            {
                stallRun += Dt;
                longest = MathF.Max(longest, stallRun);
            }
            else
            {
                stallRun = 0f;
            }

            var swinging = P().Footwork.Swinging;
            if (float.IsNaN(firstStep) && P().Footwork.Steps > steps0)
            {
                firstStep = t;
                firstFoot = swinging;
            }

            if (swinging >= 0)
            {
                var pose = P().Footwork.Pose(swinging);
                maxGap = MathF.Max(maxGap, Vector2.Distance(new Vector2(pose.Ball.X, pose.Ball.Z), Ball(swinging)));
            }

            if (trace is not null && (t < 0.6f || i % 3 == 0))
            {
                var com = P().CenterOfMass;
                trace($"t={t:0.000} v∥ {speed:0.00} sw {swinging} steps {P().Footwork.Steps - steps0} lead-load {LeadShare():0.00} pelvisY {P().PelvisPosition.Y:0.000} " +
                      $"L ({Ball(0).X:0.00},{Ball(0).Y:0.00}) R ({Ball(1).X:0.00},{Ball(1).Y:0.00}) com ({com.X:0.00},{com.Z:0.00}) to {(P().Footwork.Landing is { } l ? $"({l.Ball.X:0.00},{l.Ball.Y:0.00})" : "-")} sup ({P().Footwork.SupportCenter.X:0.00},{P().Footwork.SupportCenter.Y:0.00}) pel ({P().PelvisPosition.X:0.00},{P().PelvisPosition.Z:0.00}) v ({v.X:0.00},{v.Z:0.00}) dir ({dir.X:0.0},{dir.Z:0.0})");
            }
        }

        if (float.IsNaN(firstStep) && swinging0 >= 0)
        {
            firstStep = 0f;
            firstFoot = swinging0;
        }

        var firstLoad = firstFoot < 0 ? float.NaN : firstFoot == Footwork.Lead ? leadLoad : 1f - leadLoad;
        return new Result(setup, name, float.IsNaN(latency) ? MeasureSeconds : latency, longest, along / frames0 / walk, maxGap, float.IsNaN(firstStep) ? MeasureSeconds : firstStep,
            firstFoot, firstLoad, leadLoad, P().Footwork.Steps - steps0, P().State == FighterState.Standing);
    }

    /// <summary>引っかかりと数える組: 出だしが 0.45 秒より遅い・0.3 秒以上止まる・進みが歩く速さの 4 割未満。</summary>
    public static bool Snagged(in Result r) => r.Latency > 0.45f || r.LongestStall >= 0.3f || r.Progress < 0.4f;

    public static string Header => "| 前置き | 向き | 出だし s | 止まり s | 進み | 足の遅れ m | 1 歩目 s | 1 歩目の足 | その足の荷重 | 前足の荷重 | 歩数 | 引っかかり |\n|---|---|---|---|---|---|---|---|---|---|---|---|";

    public static string Row(in Result r) => $"| {r.Setup} | {r.Direction} | {r.Latency:0.00} | {r.LongestStall:0.00} | {r.Progress:0.00} | {r.MaxFootGap:0.000} | {r.FirstStep:0.00} | " +
        $"{(r.FirstFoot == Footwork.Lead ? "前" : r.FirstFoot == Footwork.Rear ? "後" : "-")} | {r.FirstFootLoad:0.00} | {r.LeadLoad:0.00} | {r.Steps} | {(Snagged(r) ? "**×**" : "")} |";
}
