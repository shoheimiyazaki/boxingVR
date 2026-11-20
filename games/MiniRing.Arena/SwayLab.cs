using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 揺さぶりのドリル（docs/GRAPPLING.md §9）: 袖と襟を持って、周波数 f で押し引きを n 回繰り返してから、引いて踏み出す足を塞ぐ（支え釣り込み足）。
/// 周波数ごと・釣り合いの積分の強さ（BalanceKi）ごとに、投げ・受けの崩れの最大・受けの支えの残りの最小を数え、共振（リズムで崩れやすくなる周波数）が
/// あるかを見る。技の経路は周波数から作る（教科書の値ではなく調べもの）。
/// </summary>
internal sealed class SwayLab(int bouts, float seconds, int seed, Action<string> log)
{
    private const float Dt = 1f / 60f;

    public void Run(IReadOnlyList<float> frequencies, IReadOnlyList<float> gains, int cycles, float push)
    {
        log($"| BalanceKi | 揺さぶり [Hz] | かけた | 投げ | 取り倒れ | 受けの崩れの最大 [cm] | 受けの支えの残りの最小 |");
        log("|---|---|---|---|---|---|---|");
        foreach (var ki in gains)
        {
            var tuning = FighterTuning.Default with { BalanceKi = ki };
            foreach (var hz in frequencies)
            {
                var technique = Rhythm(hz, cycles, push);
                var book = GrappleBook.Jujutsu with { Rate = 29.8f, Techniques = [technique] };
                var (attempts, throws, falls, margin, balance) = (0, 0, 0, float.NegativeInfinity, 1f);
                for (var b = 0; b < bouts; b++)
                {
                    JujutsuBot? bot = null;
                    var corners = new BoxingCorners("rhythm", s => bot = new JujutsuBot(new Closer(), book, s, only: technique.Name), _ => new StandGuard(),
                        CpuWear: MiniRing.Weapons.HandWear.None);
                    using var game = new BoxingGame(tuning, seed + b, corners);
                    game.Thrown += (by, _) => throws += by == game.Cpu ? 1 : 0;
                    var wasUp = true;
                    for (var t = 0f; t < seconds; t += Dt)
                    {
                        game.Step(default, Dt);
                        var uke = game.Player;
                        if (uke.State == FighterState.Standing && float.IsFinite(uke.SupportMargin))
                        {
                            margin = MathF.Max(margin, uke.SupportMargin);
                            balance = MathF.Min(balance, uke.Balance);
                        }

                        var up = game.Cpu.State == FighterState.Standing;
                        falls += wasUp && !up ? 1 : 0;
                        wasUp = up;
                    }

                    attempts += bot?.Attempts.GetValueOrDefault(technique.Name) ?? 0;
                }

                log($"| {ki:0.#} | {(hz <= 0f ? "なし" : hz.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))} | {attempts} | {throws} | {falls} | {margin * 100f:0.0} | {balance:0.00} |");
            }
        }

        log("");
    }

    /// <summary>
    /// 揺さぶりの技の経路: 掴んで（0.42 秒）、周期 1/f で押す（両手を相手の側へ）・引く（手前へ）を n 回、最後に支え釣り込み足（引いて足首を塞ぐ）。
    /// f = 0 は揺さぶらずにすぐ支え釣り込み足（比べもの）。
    /// </summary>
    internal static GrappleTechnique Rhythm(float hz, int cycles, float push)
    {
        GrappleHand Sleeve(float z, bool grip = true) => new(GrappleAnchor.ElbowL, [0f, 0f, z], grip);
        GrappleHand Collar(float z, bool grip = true) => new(GrappleAnchor.ShoulderR, [0f, -0.15f, z], grip);
        var keys = new List<GrappleKey>
        {
            new(0f, Sleeve(0f, false), Collar(0f, false)),
            new(0.42f, Sleeve(0f), Collar(0f)),
        };
        var t = 0.42f;
        if (hz > 0f)
        {
            var period = 1f / hz;
            for (var i = 0; i < cycles; i++)
            {
                keys.Add(new GrappleKey(t + (0.25f * period), Sleeve(push), Collar(push), Spring: 0.3f));
                keys.Add(new GrappleKey(t + (0.75f * period), Sleeve(-push), Collar(-push), Spring: 0.3f));
                t += period;
            }
        }

        keys.Add(new GrappleKey(t + 0.336f, new GrappleHand(GrappleAnchor.Self, [0.35f, -0.15f, 0.45f], true), new GrappleHand(GrappleAnchor.Self, [0.05f, -0.1f, 0.5f], true),
            new GrappleLeg(Hand.Left, GrappleAnchor.AnkleL, [0f, 0.04f, -0.1f]), Head: [0f, -0.05f, -0.15f], Spring: 0.3f));
        keys.Add(new GrappleKey(t + 1.594f, new GrappleHand(GrappleAnchor.Self, [0.5f, -0.3f, 0.25f], true), new GrappleHand(GrappleAnchor.Self, [0.3f, -0.35f, 0.35f], true),
            new GrappleLeg(Hand.Left, GrappleAnchor.AnkleL, [0f, 0.04f, -0.05f]), Head: [0f, -0.1f, -0.25f], Spring: 0.4f, Twist: -0.3f));
        keys.Add(new GrappleKey(t + 1.930f, new GrappleHand(GrappleAnchor.Self, [0.2f, -0.1f, 0.5f]), new GrappleHand(GrappleAnchor.Self, [-0.2f, -0.1f, 0.5f])));
        return new GrappleTechnique($"rhythm-{hz:0.##}", 1f, [0.7f, -0.7f], keys);
    }
}
