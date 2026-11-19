using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 軸足に乗って蹴る練習（kickbalance、docs/TAEKWONDO.md「軸足に乗って蹴る」）: 1 種の蹴りだけを出し続ける素手の蹴り手と、構えて立つだけの相手。
/// 蹴り 1 本ごとに、蹴り始め（体重を移し始め）から蹴り終わりの 1 秒後までに蹴った側が立っていられなくなったか（倒れた）、当たったか、
/// 当たった蹴りが渡したエネルギーを数える。教育の値（<see cref="BotStyle.KickShift"/>・<see cref="BotStyle.KickLeanForward"/>・
/// <see cref="BotStyle.KickLeanSide"/>）の組を並べて比べる。結果はスタイル JSON の値として出すだけ（使わなかった種で比べ直してから採る）。
/// </summary>
internal sealed class KickBalanceLab(int bouts, float seconds, int seed, Action<string> log)
{
    private const float StepDt = 1f / 60f;

    /// <summary>蹴り終わりから、倒れたかを見続ける時間 [s]。</summary>
    private const float AfterKick = 1f;

    internal readonly record struct Teaching(float Shift, float Forward, float Side)
    {
        public override string ToString() => $"間 {Shift:0.00} s・前 {Forward:0.00}・横 {Side:0.00}";
    }

    internal readonly record struct Result(int Kicks, int Falls, int Hits, float Energy)
    {
        public float FallRate => Kicks > 0 ? Falls / (float)Kicks : 0f;

        public float HitRate => Kicks > 0 ? Hits / (float)Kicks : 0f;

        public float EnergyPerKick => Kicks > 0 ? Energy / Kicks : 0f;

        public static Result operator +(Result a, Result b) => new(a.Kicks + b.Kicks, a.Falls + b.Falls, a.Hits + b.Hits, a.Energy + b.Energy);

        public override string ToString() => $"{Kicks} | {Falls}（{FallRate:P0}） | {HitRate:P0} | {EnergyPerKick:0.00}";
    }

    /// <summary>1 つの蹴り・1 つの教育の値で、種 <paramref name="firstSeed"/> から bouts 本。</summary>
    internal Result Measure(string styleName, KickTechnique kind, Teaching teaching, int firstSeed)
    {
        var style = (BotCatalog.Style(styleName) with
        {
            Name = $"kick-{kind}",
            KickRate = 1f,
            KickWeights = new Dictionary<KickTechnique, float> { [kind] = 1f },
            Aggression = 1f,
            KickShift = teaching.Shift,
            KickLeanForward = teaching.Forward,
            KickLeanSide = teaching.Side,
        }).Validated();
        var total = default(Result);
        for (var b = 0; b < bouts; b++)
        {
            BoxerBot? bot = null;
            var corners = new BoxingCorners(style.Name, s => bot = new BoxerBot(style, s), _ => new StandGuard(), PlayerWear: HandWear.None, CpuWear: HandWear.None);
            using var game = new BoxingGame(seed: firstSeed + b, corners: corners);
            var (kicks, falls, hits, energy) = (0, 0, 0, 0f);
            var watch = -1f; // 今の蹴りを見ている残り時間（蹴りの間は ∞）
            var fell = false;
            var seen = new HashSet<HitEvent>();
            for (var t = 0f; t < seconds && !game.RoundOver; t += StepDt)
            {
                game.Step(default, StepDt);
                var kicking = bot?.CurrentKick is not null;
                if (kicking && watch < 0f)
                {
                    kicks++;
                    fell = false;
                }

                if (kicking)
                {
                    watch = AfterKick;
                }
                else if (watch >= 0f)
                {
                    watch -= StepDt;
                    if (watch < 0f && fell)
                    {
                        falls++;
                    }
                }

                if (watch >= 0f && game.Cpu.State != FighterState.Standing)
                {
                    fell = true;
                }

                foreach (var hit in game.RecentHits)
                {
                    if (hit.Kick && hit.Attacker == game.Cpu && seen.Add(hit))
                    {
                        hits++;
                        energy += hit.Energy;
                    }
                }
            }

            falls += watch >= 0f && fell ? 1 : 0;
            total += new Result(kicks, falls, hits, energy);
        }

        return total;
    }

    /// <summary>動画: 1 種の蹴りだけを出す蹴り手（青）と、構えて立つ相手（赤）。</summary>
    public static void Video(string styleName, KickTechnique kind, Teaching teaching, int seed, float seconds, string output, TextWriter console)
    {
        var style = (BotCatalog.Style(styleName) with
        {
            Name = styleName,
            KickRate = 1f,
            KickWeights = new Dictionary<KickTechnique, float> { [kind] = 1f },
            Aggression = 1f,
            KickShift = teaching.Shift,
            KickLeanForward = teaching.Forward,
            KickLeanSide = teaching.Side,
        }).Validated();
        new VideoMaker(1280, 720, 30, speed: 0.5f).Make(new Contender("guard", _ => new StandGuard()), new Contender(styleName, s => new BoxerBot(style, s)),
            seed, seconds, output, console, redWear: HandWear.None, blueWear: HandWear.None);
    }

    public void Run(string styleName, IReadOnlyList<KickTechnique> kinds, IReadOnlyList<Teaching> teachings, int confirmSeed)
    {
        log($"## 軸足に乗って蹴る（{styleName}、{bouts} 本 × {seconds:0} 秒、相手は構えて立つだけ、PhysicsVersion {Fighter.PhysicsVersion}）");
        log("");
        log("`倒れた` = 蹴り始めから蹴り終わりの 1 秒後までに蹴った側が立っていられなくなった蹴りの数。`当たり` = 当たった打撃の数 / 蹴った数。`J/蹴り` = 蹴り 1 本あたり相手へ渡したエネルギー。");
        log("");
        log("| 蹴り | 教育の値 | 種 | 蹴った | 倒れた | 当たり | J/蹴り |");
        log("|---|---|---|---|---|---|---|");
        foreach (var kind in kinds)
        {
            foreach (var teaching in teachings)
            {
                log($"| {kind} | {teaching} | {seed}〜 | {Measure(styleName, kind, teaching, seed)} |");
            }

            if (confirmSeed > 0)
            {
                foreach (var teaching in teachings)
                {
                    log($"| {kind} | {teaching} | {confirmSeed}〜（比べ直し） | {Measure(styleName, kind, teaching, confirmSeed)} |");
                }
            }
        }

        log("");
    }
}
