using System.Numerics;

namespace MiniRing.Arena;

/// <summary>
/// バトルロイヤルの中継のカメラの切り替え（見せ方だけ。試合の計算に入らない）: 選手どうしの組を「戦い」とみなし、
/// 戦いごとの見どころの量（当たった打撃のダメージが薄れていく和 ＋ 間合いに入って狙い合っている度合い ＋ 倒れた相手へ詰める度合い）を連続に出して、
/// いちばん面白い戦いへ寄る。切り替えは頻繁にしない: ひとつの絵は少なくとも <see cref="DirectorTuning.MinShot"/> 続け、
/// 今の戦いより十分に（<see cref="DirectorTuning.SwitchRatio"/> 倍 ＋ <see cref="DirectorTuning.SwitchMargin"/>）面白い戦いが出たときだけ移る。
/// 全体の絵（檻の全員）は、寄った絵が続いた長さに応じて見せる必要が増え、どの戦いも静かなときにも選ばれる。
/// 判断は戦いの種類で分けず、量の大小だけで決める（§1.3 を見せ方にも当てはめる）。
/// </summary>
internal sealed class RoyaleDirector(DirectorTuning tuning)
{
    private readonly Dictionary<(int A, int B), float> _heat = [];
    private Shot _shot = Shot.Wide;
    private float _shotStart;
    private float _lastWide;
    private float? _endedAt;

    /// <summary>今の絵。</summary>
    public Shot Current => _shot;

    /// <summary>今の絵が始まった時刻 [s]（切り替わった瞬間はカメラを飛ばす: 中継の「カット」）。</summary>
    public float ShotStart => _shotStart;

    /// <summary>直前の <see cref="Update"/> で絵が切り替わった。</summary>
    public bool Cut { get; private set; }

    /// <summary>組 (a, b) の見どころの量（試合の計算に入らない。調べる・テストのため）。</summary>
    public float Score(IReadOnlyList<DirectorFighter> fighters, int a, int b) => Heat(Key(a, b)) + Engagement(fighters, a, b);

    /// <summary>
    /// 時刻 <paramref name="t"/> の様子で絵を選ぶ。<paramref name="hits"/> はこの刻みに当たった打撃（打った人, 打たれた人, ダメージ）。
    /// <paramref name="wholeCage"/> の間（始まる前・ラウンドの間の休み）は全体を映す。
    /// </summary>
    public Shot Update(float t, float dt, IReadOnlyList<DirectorFighter> fighters, IEnumerable<(int Attacker, int Victim, float Damage)> hits, bool wholeCage = false)
    {
        var fade = MathF.Exp(-dt / tuning.HeatHalfLife * MathF.Log(2f));
        foreach (var key in _heat.Keys.ToList())
        {
            _heat[key] *= fade;
        }

        foreach (var (attacker, victim, damage) in hits)
        {
            if (attacker != victim && attacker >= 0 && victim >= 0 && float.IsFinite(damage))
            {
                var key = Key(attacker, victim);
                _heat[key] = Heat(key) + damage;
            }
        }

        var (best, bestScore) = BestFight(fighters);
        var current = _shot.IsWide ? WideScore(t) : Score(fighters, _shot.A, _shot.B);
        var age = t - _shotStart;
        var next = _shot;
        if (wholeCage)
        {
            next = Shot.Wide;
        }
        else if (!_shot.IsWide && !Alive(fighters, _shot))
        {
            // 戦いが終わった（どちらかが脱落）: 倒れた所を少し映してから、全体へ引く（次の戦いを探す間の「引き」）。
            _endedAt ??= t;
            next = t - _endedAt.Value >= tuning.AfterElimination ? Shot.Wide : _shot;
        }
        else if (age >= (_shot.IsWide ? tuning.MinWide : tuning.MinShot))
        {
            // 全体へ引くとき・全体から寄るときは下駄を履かせない（静かな戦いからは基準の量だけで引き、狙い合って詰めた 2 人へは打撃が無くても寄る）。
            // 戦いから別の戦いへ移るときだけ下駄を足す（似た 2 つの戦いを行き来しない）。
            var wide = WideScore(t);
            var (candidate, candidateScore, margin) = !_shot.IsWide && wide > bestScore ? (Shot.Wide, wide, 0f)
                : (best, bestScore, _shot.IsWide ? 0f : tuning.SwitchMargin);
            // どれほど激しくても、全体を見せない時間は MaxFocus まで（視聴者が全員の位置を見失わない）。
            var overdue = !_shot.IsWide && t - _lastWide >= tuning.MaxFocus;
            candidate = overdue ? Shot.Wide : candidate;
            if (candidate != _shot && (overdue || candidateScore > (current * tuning.SwitchRatio) + margin))
            {
                next = candidate;
            }
        }

        Cut = next != _shot;
        if (Cut)
        {
            _shot = next;
            _shotStart = t;
            _endedAt = null;
        }

        if (_shot.IsWide)
        {
            _lastWide = t;
        }

        return _shot;
    }

    /// <summary>
    /// 全体の絵の見どころ: 基準の量に、寄った絵が続いた長さ（<see cref="DirectorTuning.WideDue"/> を超えた分）で増える「引いて全体を見せる必要」を足す。
    /// どの戦いも静かなら基準の量で全体が勝つ。
    /// </summary>
    private float WideScore(float t) => tuning.WideBase + (MathF.Max(0f, t - _lastWide - tuning.WideDue) * tuning.WideNeedPerSecond);

    private (Shot Shot, float Score) BestFight(IReadOnlyList<DirectorFighter> fighters)
    {
        var best = (Shot: Shot.Wide, Score: 0f);
        for (var a = 0; a < fighters.Count; a++)
        {
            for (var b = a + 1; b < fighters.Count; b++)
            {
                if (!fighters[a].InPlay || !fighters[b].InPlay)
                {
                    continue;
                }

                var score = Score(fighters, a, b);
                if (score > best.Score)
                {
                    best = (new Shot(a, b), score);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 打たれていなくても見せたい度合い: 狙い合っている（片方でも相手を狙っている）2 人が間合いに入るほど大きい（<see cref="DirectorTuning.EngageRange"/> で 0、
    /// 触れる距離で <see cref="DirectorTuning.Engage"/>）。片方が倒れていて、もう片方が詰めているなら、とどめの見込みの分（<see cref="DirectorTuning.Finish"/>）を足す。
    /// </summary>
    private float Engagement(IReadOnlyList<DirectorFighter> fighters, int a, int b)
    {
        var fa = fighters[a];
        var fb = fighters[b];
        if (!fa.InPlay || !fb.InPlay)
        {
            return 0f;
        }

        var aims = (fa.Target == b ? 1f : 0f) + (fb.Target == a ? 1f : 0f); // 0, 1, 2: 一方的か、狙い合いか
        var gap = Vector2.Distance(new Vector2(fa.Position.X, fa.Position.Z), new Vector2(fb.Position.X, fb.Position.Z));
        var near = MathF.Max(0f, 1f - (gap / tuning.EngageRange));
        var down = MathF.Max(fa.Down && fb.Target == a ? 1f : 0f, fb.Down && fa.Target == b ? 1f : 0f);
        return near * ((tuning.Engage * aims * 0.5f) + (tuning.Finish * down));
    }

    private static bool Alive(IReadOnlyList<DirectorFighter> fighters, Shot shot) => fighters[shot.A].InPlay && fighters[shot.B].InPlay;

    private float Heat((int, int) key) => _heat.TryGetValue(key, out var h) ? h : 0f;

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}

/// <summary>カメラの判断に渡す選手の様子（胸の位置・まだ試合にいるか・倒れているか・狙っている相手の席）。</summary>
internal readonly record struct DirectorFighter(Vector3 Position, bool InPlay, bool Down, int Target);

/// <summary>絵: 全体（<see cref="IsWide"/>）か、席 A と B の戦い。</summary>
internal readonly record struct Shot(int A, int B)
{
    public static Shot Wide { get; } = new(-1, -1);

    public bool IsWide => A < 0;
}

/// <summary>カメラの切り替えの調整値（見せ方だけ。ダメージの量は試合のダメージの値: 強い 1 発がおよそ 10〜30）。</summary>
internal sealed record DirectorTuning
{
    /// <summary>寄った絵を少なくとも続ける長さ [s]（テレビの格闘中継の 1 カットの目安 4〜8 秒の下の端）。</summary>
    public float MinShot { get; init; } = 4.5f;

    /// <summary>全体の絵を少なくとも続ける長さ [s]（全員の位置が読める長さ）。</summary>
    public float MinWide { get; init; } = 3f;

    /// <summary>今の絵より何倍面白ければ移るか（切り替えのしすぎを防ぐ履歴の幅）。</summary>
    public float SwitchRatio { get; init; } = 1.6f;

    /// <summary>移るのに足りないといけない量の下駄（静かな絵どうしの小さな差で行き来しない）。</summary>
    public float SwitchMargin { get; init; } = 4f;

    /// <summary>当たった打撃の量が半分に薄れる時間 [s]（直前の数秒の打ち合いを「今の激しさ」として覚える）。</summary>
    public float HeatHalfLife { get; init; } = 3f;

    /// <summary>この距離 [m] より離れた 2 人は戦っていない（間合いの重みが 0）。檻の対角は約 9 m、踏み込みの届く距離は 2 m ほど。</summary>
    public float EngageRange { get; init; } = 3f;

    /// <summary>狙い合って触れる距離にいる 2 人の量（打撃が無くても寄る値。全体の絵の基準 × SwitchRatio = 8 を、狙い合って 1 m ほどに詰めると越える）。</summary>
    public float Engage { get; init; } = 12f;

    /// <summary>倒れた相手へ詰めている 2 人の量（とどめの見込み）。</summary>
    public float Finish { get; init; } = 14f;

    /// <summary>全体の絵の基準の量（どの戦いもこれより静かなら全体を映す）。</summary>
    public float WideBase { get; init; } = 5f;

    /// <summary>寄った絵がこの秒数 [s] 続いたら、全体を見せる必要が増え始める。</summary>
    public float WideDue { get; init; } = 14f;

    /// <summary>全体を見せる必要の増え方 [量/s]（WideDue を 8 秒過ぎると強い打ち合いでも引く程度）。</summary>
    public float WideNeedPerSecond { get; init; } = 6f;

    /// <summary>寄った絵（いくつ続いても）の後、全体を見せるまでの最長 [s]。</summary>
    public float MaxFocus { get; init; } = 24f;

    /// <summary>脱落の後、その場を映し続ける長さ [s]（倒れた所を見せてから全体へ引く）。</summary>
    public float AfterElimination { get; init; } = 3f;
}
