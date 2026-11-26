using System.Numerics;
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

    /// <summary>
    /// 武器の当たりはこの重さから記録する（武器の打撃の値は 1 発ずつは拳より小さく、削った量で効く: 種 6 で 4 人を倒したこんぼうの当たりが 12 に届かず、記録に 1 つも出なかった）。
    /// </summary>
    private const float WeaponHitFloor = 3f;

    /// <summary>同じ 2 人の当たりがこの間 [s] に続いたら 1 行にまとめる（連打を 1 行ずつ並べない）。</summary>
    private const float VolleyGap = 2f;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly List<(float T, string Type, string Text)> _events = [];
    private readonly Dictionary<(Fighter, Fighter), (int Index, float Last, int Count, string Text)> _volleys = [];
    private readonly RoyaleDirector _director = new(new DirectorTuning());
    private readonly bool[] _down = new bool[game.AllFighters.Count];
    private readonly int[] _target = [.. Enumerable.Repeat(-2, game.AllFighters.Count)];
    private readonly int[,] _windowHits = new int[game.AllFighters.Count, game.AllFighters.Count]; // [打った, 打たれた]: 前の状況の要約からの当たりの数
    private readonly int[] _landed = new int[game.AllFighters.Count];
    private readonly int[] _taken = new int[game.AllFighters.Count];
    private readonly int[] _startCells = [.. game.AllFighters.Select(Cells)];
    private float _nextSituation = SituationEvery;
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

        Tally(game.RecentHits.Select(h => (h.Attacker, h.Victim)).Concat(game.WeaponHits.Select(h => (h.Attacker, h.Victim))));
        foreach (var h in game.RecentHits.Where(h => h.Damage >= HitFloor && h.Attacker != h.Victim))
        {
            var tool = h.Kick ? "蹴り" : h.Elbow ? "肘" : "拳";
            Hit(t, h.Attacker, h.Victim, $"{Name(h.Attacker)}の{tool}が{Name(h.Victim)}の{Broadcast.ZoneName(h.Zone)}に入った（{Strength(h.Damage)}）");
        }

        foreach (var h in game.WeaponHits.Where(h => (h.Damage >= WeaponHitFloor || h.Cells > 0) && h.Attacker != h.Victim))
        {
            var how = string.Join("、", new[] { Broadcast.Strength(h.Damage) is { Length: > 0 } s ? $"{s}一撃" : "", h.Cells > 0 ? "肉が削れた" : "" }.Where(x => x.Length > 0));
            Hit(t, h.Attacker, h.Victim, $"{Name(h.Attacker)}の{Gear(h.Attacker)}が{Name(h.Victim)}の{PartJa(h.Part)}を捉えた{(how.Length > 0 ? $"（{how}）" : "")}");
        }

        foreach (var b in game.BrokenBones)
        {
            Add(t, "bone", $"{Name(b.Victim)}の{(b.Body >= 0 ? Broadcast.PartName(b.Victim, b.Body) : PartJa(b.Part))}の骨が{(b.Bent ? "曲がった" : "折れた")}");
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
        Targets(t);
        if (t >= _nextSituation && !game.MatchOver)
        {
            _nextSituation = t + SituationEvery;
            if (!game.InIntro)
            {
                Add(t, "situation", Situation());
            }
        }

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

    /// <summary>状況の要約を出す間隔 [s]（書き手が「今、檻の中で誰が誰と」を俯瞰できる頻度。出来事の行の間に挟まる）。</summary>
    private const float SituationEvery = 4f;

    /// <summary>この距離 [m] より近い 2 人は「組み合っている」（踏み込みの届く距離 2 m ＋ 武器の長さの目安）。</summary>
    private const float EngagedRange = 2.5f;

    private void Tally(IEnumerable<(Fighter Attacker, Fighter Victim)> hits)
    {
        foreach (var (attacker, victim) in hits)
        {
            var a = game.SeatOf(attacker);
            var v = game.SeatOf(victim);
            if (a >= 0 && v >= 0 && a != v)
            {
                _windowHits[a, v]++;
                _landed[a]++;
                _taken[v]++;
            }
        }
    }

    /// <summary>狙いを替えた（新しく狙った・別の相手へ替えた）ときだけ 1 行（休みの間は数えない）。</summary>
    private void Targets(float t)
    {
        var all = game.AllFighters;
        for (var i = 0; i < all.Count; i++)
        {
            var target = game.InPlay(all[i]) ? game.TargetSeatOf(all[i]) : -1;
            if (target != _target[i] && _target[i] != -2 && target >= 0 && !game.Resting)
            {
                var from = _target[i] >= 0 && game.InPlay(all[_target[i]]) ? $"{game.LabelOf(all[_target[i]])}から" : "";
                Add(t, "target", $"{game.LabelOf(all[i])}が狙いを{from}{game.LabelOf(all[target])}へ替えた");
            }

            _target[i] = target;
        }
    }

    /// <summary>
    /// 今の檻の俯瞰（書き手の資料。画面に出ない）: 生き残りの数・組み合っている組と距離・誰が誰を狙っているか（複数に狙われている人）・
    /// 離れている人・檻の中の位置（中央寄り／金網際）・前の要約からの当たりの数・体の残り・立っているか・息。
    /// </summary>
    private string Situation()
    {
        var all = game.AllFighters;
        var alive = Enumerable.Range(0, all.Count).Where(i => game.InPlay(all[i])).ToList();
        string N(int i) => game.LabelOf(all[i]);
        float Gap(int a, int b) => Vector2.Distance(Flat(all[a].ChestPosition), Flat(all[b].ChestPosition));
        var sb = new StringBuilder($"状況（残り {alive.Count} 人）: ");

        var pairs = alive.SelectMany(a => alive.Where(b => b > a).Select(b => (A: a, B: b))).Where(p => Gap(p.A, p.B) < EngagedRange).OrderBy(p => Gap(p.A, p.B)).ToList();
        sb.Append(pairs.Count == 0 ? "組み合っている組は無い" : "組み合い " + string.Join("、", pairs.Select(p =>
        {
            var aims = (_target[p.A] == p.B, _target[p.B] == p.A) switch
            {
                (true, true) => "狙い合い",
                (true, false) => $"{N(p.A)}が一方的に狙う",
                (false, true) => $"{N(p.B)}が一方的に狙う",
                _ => "互いに別の相手を狙う",
            };
            var ab = _windowHits[p.A, p.B];
            var ba = _windowHits[p.B, p.A];
            var exchange = ab + ba > 0 ? $"、当たり {N(p.A)} {ab}・{N(p.B)} {ba}" : "";
            return string.Create(CultureInfo.InvariantCulture, $"{N(p.A)}×{N(p.B)}（{Gap(p.A, p.B):0.0} m、{aims}{exchange}）");
        })));

        var lonely = alive.Where(i => alive.All(j => j == i || Gap(i, j) >= EngagedRange)).ToList();
        if (lonely.Count > 0)
        {
            sb.Append("。離れている: ").Append(string.Join("、", lonely.Select(i =>
            {
                var near = alive.Where(j => j != i).OrderBy(j => Gap(i, j)).DefaultIfEmpty(-1).First();
                return near >= 0 ? string.Create(CultureInfo.InvariantCulture, $"{N(i)}（一番近い {N(near)} まで {Gap(i, near):0.0} m）") : N(i);
            })));
        }

        foreach (var i in alive)
        {
            var by = alive.Where(j => j != i && _target[j] == i).ToList();
            if (by.Count >= 2)
            {
                sb.Append(CultureInfo.InvariantCulture, $"。{N(i)}が {by.Count} 人（{string.Join("・", by.Select(N))}）に狙われている");
            }
        }

        sb.Append("。各選手: ").Append(string.Join("／", alive.Select(i =>
        {
            var f = all[i];
            var aim = _target[i] >= 0 ? $"→{N(_target[i])}" : "→狙いなし";
            var place = Flat(f.ChestPosition - game.Layout.Center).Length() > game.Layout.Circumradius * 0.6f ? "金網際" : "中央寄り";
            var stance = f.State == FighterState.Standing ? "立っている" : "倒れている";
            var body = Tenths(Cells(f) / (float)Math.Max(1, _startCells[i]));
            var breath = f.Stamina < 0.3f ? "、息が上がっている" : "";
            var dealt = Enumerable.Range(0, all.Count).Sum(v => _windowHits[i, v]);
            var got = Enumerable.Range(0, all.Count).Sum(a => _windowHits[a, i]);
            return $"{N(i)} {aim}・{place}・{stance}・体の残り{body}{breath}・この間に当てた {dealt}／受けた {got}";
        })));

        Array.Clear(_windowHits);
        return sb.ToString();
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private static int Cells(Fighter fighter) => fighter.Voxels?.Parts.Sum(p => p.Body.Grid.FilledCount) ?? 1;

    /// <summary>割合を話し言葉の「N割」で（中継では % を言わない）。</summary>
    private static string Tenths(float fraction) => fraction >= 0.95f ? "ほぼすべて" : $"{Math.Clamp((int)MathF.Round(fraction * 10f), 0, 9)}割";

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
        md.AppendLine("## 試合の通しの数（先出しに使わないこと。起きた時点の出来事で語る）");
        md.AppendLine();
        md.AppendLine("| 選手 | 当てた | 受けた | 倒した相手 | 結末 |");
        md.AppendLine("|---|---|---|---|---|");
        var fighters = game.AllFighters;
        for (var i = 0; i < fighters.Count; i++)
        {
            var kills = game.Eliminations.Where(e => e.By == fighters[i]).Select(e => game.LabelOf(e.Fighter));
            var death = game.Eliminations.FirstOrDefault(e => e.Fighter == fighters[i]);
            var end = death is null ? "生還" : string.Create(CultureInfo.InvariantCulture, $"第{death.Round}ラウンドに死亡（{RoyaleVideo.Cause(death.Cause)}）");
            md.AppendLine(CultureInfo.InvariantCulture, $"| {game.LabelOf(fighters[i])} | {_landed[i]} | {_taken[i]} | {string.Join("・", kills)} | {end} |");
        }

        md.AppendLine();
        md.AppendLine("## 出来事（動画の時刻 [s]）");
        md.AppendLine();
        md.AppendLine("[situation] は 4 秒ごとの檻の俯瞰（画面には出ない）: 組み合っている組と距離・誰が誰を狙っているか・離れている人・複数に狙われている人・各選手の位置・体の残り・その 4 秒の当たりの数。[target] は狙いを替えた瞬間。");
        md.AppendLine();
        foreach (var e in _events)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- {e.T:0.0} [{e.Type}] {e.Text}");
        }

        File.WriteAllText(Path.ChangeExtension(jsonPath, ".md"), md.ToString(), new UTF8Encoding(false));
    }

    private void Add(float t, string type, string text) => _events.Add((t, type, text));

    private static string PartJa(BodyPartKind part) => part switch
    {
        BodyPartKind.Abdomen => "腹",
        BodyPartKind.Foot or BodyPartKind.Toe => "足",
        BodyPartKind.Coxa => "脚の付け根",
        _ => AimLab.Ja(part),
    };

    /// <summary>当たりを記録する。同じ 2 人の当たりが続けば、最初の行に「続けて N 発」と数を足す。</summary>
    private void Hit(float t, Fighter attacker, Fighter victim, string text)
    {
        if (_volleys.TryGetValue((attacker, victim), out var v) && t - v.Last <= VolleyGap)
        {
            var count = v.Count + 1;
            _events[v.Index] = _events[v.Index] with { Text = $"{v.Text}。続けて計 {count} 発（{t:0.0} 秒まで）" };
            _volleys[(attacker, victim)] = v with { Last = t, Count = count };
            return;
        }

        _volleys[(attacker, victim)] = (_events.Count, t, 1, text);
        Add(t, "hit", text);
    }

    private string Gear(Fighter f) => game.SeatOf(f) is var seat and >= 0 && seat < corners.Count ? corners[seat].Discipline : "武器";

    private static string Strength(float damage) => Broadcast.Strength(damage) is { Length: > 0 } s ? $"{s}一撃" : "一撃";
}
