using System.Globalization;
using System.Text.Json;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 守りを練習試合で育てる（guardspar）: 剣士の守り（<see cref="SwordDefense"/>: 読む時間・近さ・読む速さ・続ける時間・足と頭の遅れの読み・
/// 受け流すか・受け流しの値・打ち返すか）を、打って離れる剣士として練習試合で育てる（<see cref="MatchTrainer{T}"/>）。守りのドリル（guardlab）は
/// その場で受けるだけだが、試合では間合いの取り合い・自分の打ち・相手の連打の中で守る。
/// </summary>
internal static class GuardSpar
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>守りを少し変える（値は範囲の中へ、受け流すか・打ち返すかは 15% で入れ替える）。</summary>
    public static SwordDefense Mutate(SwordDefense p, Random random)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(random);
        float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        float Scale(float v, float sigma, float min, float max) => Math.Clamp(v * MathF.Exp(sigma * Gauss()), min, max);
        float Shift(float v, float sigma, float min, float max) => Math.Clamp(v + (sigma * Gauss()), min, max);
        // 足と頭で外す・受け流しながら外すの 2 つの間だけを入れ替える（ほかの組み立て方で始めたら、そのまま）。
        var mode = p.Mode is DefenseMode.Step or DefenseMode.ParryAndStep && random.NextDouble() < 0.15
            ? (p.Mode == DefenseMode.Step ? DefenseMode.ParryAndStep : DefenseMode.Step)
            : p.Mode;
        var child = p with
        {
            Mode = mode,
            Horizon = Scale(p.Horizon, 0.25f, 0.1f, 0.6f),
            Margin = Scale(p.Margin, 0.25f, 0.2f, 1.2f),
            ThreatSpeed = Scale(p.ThreatSpeed, 0.25f, 3f, 14f),
            HoldTime = Scale(p.HoldTime, 0.3f, 0.1f, 1f),
            StepDelay = Shift(p.StepDelay, 0.03f, 0f, 0.2f),
            HeadTime = Scale(p.HeadTime, 0.25f, 0.05f, 0.4f),
        };

        // 受け流しの値と打ち返すかは、受け流すときだけ効く（足と頭だけのときに変えても成績は変わらず、候補が無駄になる）。
        return mode is DefenseMode.Parry or DefenseMode.ParryAndStep
            ? child with
            {
                Counter = random.NextDouble() < 0.15 ? !p.Counter : p.Counter,
                Deflect = Shift(p.Deflect, 0.15f, 0f, 1.2f),
                Bind = Shift(p.Bind, 0.12f, 0f, 1f),
                Drive = Shift(p.Drive, 0.12f, 0f, 1f),
                Lead = Shift(p.Lead, 0.1f, 0f, 1f),
            }
            : child;
    }

    public static string Describe(SwordDefense d) => string.Create(CultureInfo.InvariantCulture,
        $"{(d.Mode == DefenseMode.ParryAndStep ? $"受け流し（傾き {d.Deflect:0.00}・握りの側 {d.Bind:0.00}・払い {d.Drive:0.00}・迎え {d.Lead:0.00}）＋ " : "")}足と頭"
        + $"・読む {d.Horizon:0.00} s・近さ {d.Margin:0.00}・速さ {d.ThreatSpeed:0.0}・続ける {d.HoldTime:0.00} s・足の遅れ {d.StepDelay:0.00} s・頭 {d.HeadTime:0.00} s"
        + $"・返し {(d.Counter ? "あり" : "なし")}");

    public static SwordDefense Load(string path) => JsonSerializer.Deserialize<SwordDefense>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"{path} を読めません。");

    public static void Save(string path, SwordDefense value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
