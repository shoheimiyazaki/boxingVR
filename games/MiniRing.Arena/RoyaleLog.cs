using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MiniRing.Audio;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// バトルロイヤルの中継の記録（実況・解説の資料。読むだけで試合に響かない）: ホーン・重い当たり・骨折・切断・倒れた／立った・脱落・カメラの寄り引き・決着を、
/// 動画の時刻（試合の時刻と同じ）で残す。1 対 1 の <see cref="Broadcast"/> の記録のロワイヤル版。
/// カメラの寄り引きは動画と同じ判断（<see cref="RoyaleVideo.DirectorInput"/> を同じ調整値の <see cref="RoyaleDirector"/> へ）で出すので、動画を撮らずに書いても絵と合う。
/// </summary>
internal sealed class RoyaleLog(BoxingGame game, IReadOnlyList<FighterCharacter> characters, IReadOnlyList<TvCorner> corners)
{
    /// <summary>この重さより軽い当たりは記録しない（Broadcast.Strength の「いい」一撃の下の端）。</summary>
    private const float HitFloor = 12f;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly List<(float T, string Type, string Text)> _events = [];
    private readonly RoyaleDirector _director = new(new DirectorTuning());
    private readonly bool[] _down = new bool[game.AllFighters.Count];
    private int _eliminationsSeen;
    private bool _ended;

    public void Observe(float t)
    {
        string Name(Fighter f) => game.LabelOf(f);
        switch (game.RecentBell)
        {
            case RoundBell.Start:
                Add(t, "horn", $"第{game.MatchRound}ラウンド開始のホーン（残り {game.AllFighters.Count(game.InPlay)} 人）");
                break;
            case RoundBell.End:
                Add(t, "horn", game.MatchOver ? "終了のホーン" : $"第{game.MatchRound}ラウンド終了のホーン。休みへ（休みの間に傷が少しずつ戻る）");
                break;
        }

        foreach (var h in game.RecentHits.Where(h => h.Damage >= HitFloor && h.Attacker != h.Victim))
        {
            var tool = h.Kick ? "蹴り" : h.Elbow ? "肘" : "拳";
            Add(t, "hit", $"{Name(h.Attacker)}の{tool}が{Name(h.Victim)}の{Broadcast.ZoneName(h.Zone)}に入った（{Strength(h.Damage)}）");
        }

        foreach (var h in game.WeaponHits.Where(h => h.Damage >= HitFloor && h.Attacker != h.Victim))
        {
            Add(t, "hit", $"{Name(h.Attacker)}の{Gear(h.Attacker)}が{Name(h.Victim)}の{AimLab.Ja(h.Part)}を捉えた（{Strength(h.Damage)}）");
        }

        foreach (var b in game.BrokenBones)
        {
            Add(t, "bone", $"{Name(b.Victim)}の{(b.Body >= 0 ? Broadcast.PartName(b.Victim, b.Body) : AimLab.Ja(b.Part))}の骨が{(b.Bent ? "曲がった" : "折れた")}");
        }

        foreach (var s in game.RecentSevers.Where(s => s.Victim is not null))
        {
            Add(t, "sever", $"{Name(s.Victim!)}の{(s.Body >= 0 ? Broadcast.PartName(s.Victim!, s.Body) : "手足")}が斬り落とされた");
        }

        var all = game.AllFighters;
        for (var i = 0; i < all.Count; i++)
        {
            var down = all[i].State != FighterState.Standing && game.InPlay(all[i]);
            if (down != _down[i] && !game.Resting && game.InPlay(all[i]))
            {
                Add(t, down ? "down" : "up", down ? $"{Name(all[i])}が倒れた（ダウンは無い。攻撃は止まらない）" : $"{Name(all[i])}が立ち上がった");
            }

            _down[i] = down;
        }

        foreach (var e in game.Eliminations.Skip(_eliminationsSeen))
        {
            var by = e.By is { } killer ? $"、倒したのは{Name(killer)}" : "";
            Add(t, "death", $"{Name(e.Fighter)}が死亡、脱落（死因: {RoyaleVideo.Cause(e.Cause)}{by}）。残り {all.Count(game.InPlay)} 人");
        }

        _eliminationsSeen = game.Eliminations.Count;
        var (view, hits) = RoyaleVideo.DirectorInput(game);
        _director.Update(t, 1f / 60f, view, hits, wholeCage: game.InIntro || game.InBreak);
        if (_director.Cut)
        {
            var shot = _director.Current;
            Add(t, "camera", shot.IsWide ? "カメラが檻の全体へ引く" : $"カメラが{Name(all[shot.A])}と{Name(all[shot.B])}の戦いに寄る");
        }

        if (game.MatchOver && !_ended)
        {
            _ended = true;
            var alive = all.Where(game.InPlay).Select(Name).ToList();
            Add(t, "result", game.Winner is { } w ? $"決着: {Name(w)}の勝利（最後の 1 人）" : $"5 ラウンドを終えて決着つかず、引き分け。生還: {string.Join("・", alive)}");
        }
    }

    /// <summary>記録（JSON）と、書き手が読む資料（Markdown: 選手の紹介と時刻の表）を書く。</summary>
    public void Write(string jsonPath)
    {
        var data = new
        {
            org = "vfc",
            format = "battle-royale",
            fighters = corners.Select((c, i) => new { seat = i, name = c.Name, discipline = c.Discipline, detail = c.Detail }),
            events = _events.Select(e => new { t = MathF.Round(e.T, 2), type = e.Type, text = e.Text }),
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(data, JsonOptions), new UTF8Encoding(false));

        var md = new StringBuilder();
        md.AppendLine("# VFC バトルロイヤル（実況・解説の資料）");
        md.AppendLine();
        md.AppendLine("決まり: 金網の六角形の檻に全員が入り、最後の 1 人まで戦う。ダウン・カウント・判定・レフェリーは無い。決着は死だけ。5 ラウンドを終えて 2 人以上生きていれば引き分け。ラウンドの合図はホーン。休みの間に傷は生きている所から少しずつ戻る（浅い削れは埋まるが、折れた骨・失った手足は戻りきらない）。");
        md.AppendLine();
        md.AppendLine("## 選手");
        for (var i = 0; i < characters.Count; i++)
        {
            var c = characters[i];
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"### {corners[i].Name}（{corners[i].Discipline}）");
            md.AppendLine(c.Profile is { } p ? p.Briefing(c.Name, c.HudName, c.Tagline) : $"{c.Name} — {c.Tagline}・{c.Origin}");
        }

        md.AppendLine();
        md.AppendLine("## 出来事（動画の時刻 [s]）");
        md.AppendLine();
        foreach (var e in _events)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- {e.T:0.0} [{e.Type}] {e.Text}");
        }

        File.WriteAllText(Path.ChangeExtension(jsonPath, ".md"), md.ToString(), new UTF8Encoding(false));
    }

    private void Add(float t, string type, string text) => _events.Add((t, type, text));

    private string Gear(Fighter f) => game.SeatOf(f) is var seat and >= 0 && seat < corners.Count ? corners[seat].Discipline : "武器";

    private static string Strength(float damage) => Broadcast.Strength(damage) is { Length: > 0 } s ? $"{s}一撃" : "一撃";
}
