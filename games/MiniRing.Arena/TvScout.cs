using System.Globalization;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// テレビ中継の動画を撮る前の下見: 動画と同じ両コーナー（<see cref="VideoMaker.Corners"/>）・同じ歩の幅で、描画せずに種ごとの試合を回し、
/// 決着（K.O. か判定・何ラウンドの何秒）・与えたダメージ・ダウンの回数を並べる。<see cref="TvShowRules.Drama"/> で見ごたえの点を付け、良い種を選ぶ。
/// 歩の幅は動画の既定（30 fps・0.75 倍の速さ → 1/80 s）にそろえる（同じ種なら動画と同じ試合になる）。
/// </summary>
internal static class TvScout
{
    /// <summary>種ごとに試合を回した結果（種の順）。</summary>
    public static IReadOnlyList<TvScoutResult> Run(Contender red, Contender blue, HandWear redWear, HandWear blueWear, WeaponKind? redWeapon, WeaponKind? blueWeapon,
        int rounds, IEnumerable<int> seeds, float speed, int fps,
        MiniRing.Characters.FighterCharacter? redCharacter = null, MiniRing.Characters.FighterCharacter? blueCharacter = null, bool referee = true, float breakSeconds = 16.78f, Organization? org = null)
    {
        org ??= Organization.Default;
        var rules = TvShowRules.Rules(rounds, breakSeconds, org);
        var frameDt = speed / fps;
        var stepsPerFrame = Math.Max(1, (int)MathF.Ceiling((frameDt / (1f / 60f)) - 1e-3f));
        var stepDt = frameDt / stepsPerFrame;
        var results = new List<TvScoutResult>();
        // 1 本ずつ順に回す。同じプロセスの中で試合を並列に回すと、同じ種でも動画と違う試合になった（2026-11-16 に確かめた。
        // 試合どうしがどこかで状態を共有している。別のプロセスなら一致する）。
        foreach (var seed in seeds.Distinct())
        {
            var corners = TvShowRules.Stage(VideoMaker.WithCharacters(
                VideoMaker.Corners(red, blue, seed, redWear, blueWear, redWeapon, blueWeapon, s => blue.Create(s), s => red.Create(s), referee), redCharacter, blueCharacter), org);
            using var game = new BoxingGame(seed: seed, corners: corners, rules: rules);
            var redDowns = 0;
            var blueDowns = 0;
            bool redWasDown = false, blueWasDown = false;
            var time = 0f;
            float bothDown = 0f, bothDownRun = 0f, longestBothDown = 0f;
            while (!game.MatchOver && time < rules.TotalSeconds + 8.392f)
            {
                game.Step(default, stepDt);
                time += stepDt;
                // 両者が同時に倒れている時間（ラウンド中だけ。休み・試合の始めは数えない）: 番組として止まって見える時間の物差し。
                var both = !game.Resting && game.Player.State != FighterState.Standing && game.Cpu.State != FighterState.Standing;
                bothDownRun = both ? bothDownRun + stepDt : 0f;
                bothDown += both ? stepDt : 0f;
                longestBothDown = MathF.Max(longestBothDown, bothDownRun);
                redDowns += !redWasDown && game.Player.IsKnockedDown ? 1 : 0;
                blueDowns += !blueWasDown && game.Cpu.IsKnockedDown ? 1 : 0;
                (redWasDown, blueWasDown) = (game.Player.IsKnockedDown, game.Cpu.IsKnockedDown);
            }

            var winner = game.PlayerWins > game.CpuWins ? 1 : game.CpuWins > game.PlayerWins ? -1 : 0;
            results.Add(new TvScoutResult(seed, game.Decision ?? game.Message, game.Decision is null, game.MatchRound, rules.RoundSeconds - game.RoundLeft,
                game.Cpu.Condition.TotalDamage, game.Player.Condition.TotalDamage, redDowns, blueDowns, winner,
                Death: game.Decision is null && TvScoutResult.IsDeath(game.Message)) { BothDownSeconds = bothDown, LongestBothDown = longestBothDown, Checksum = game.StateChecksum() });
        }
        return [.. results.OrderBy(r => r.Seed)];
    }

    /// <summary>結果を 1 行ずつ書く（見ごたえの点つき）。</summary>
    public static void Print(IReadOnlyList<TvScoutResult> results, int rounds, float roundSeconds, TextWriter log, DramaWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(log);
        foreach (var r in results)
        {
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  seed {r.Seed,3}: {r.Finish,-40} R{r.Round} {r.RoundTime,4:0.0}s  dmg red {r.RedDealt,5:0} / blue {r.BlueDealt,5:0}  downs red {r.RedDowns} / blue {r.BlueDowns}  both-down {r.BothDownSeconds,5:0.0}s (longest {r.LongestBothDown,4:0.0}s)  drama {TvShowRules.Drama(r, rounds, roundSeconds, weights),5:0.0}"));
        }
    }

}
