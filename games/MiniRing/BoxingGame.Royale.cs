using MiniRing.Bots;
using MiniRing.LowPoly;

namespace MiniRing;

/// <summary>バトルロイヤルで 1 人が脱落した（死んだ・場から落ちた）。<paramref name="By"/> は最後に打撃を当てた人（とどめ。いなければ null）。</summary>
public sealed record Elimination(Fighter Fighter, Fighter? By, float Time, int Round, string Cause);

/// <summary>
/// 3 人以上の試合（バトルロイヤル、docs/plans/VFC_ROYALE_PLAN.md）。体・物理・当たりは 2 人の試合と同じ道を通り、ここには席・狙い・脱落だけを置く。
/// 2 人の試合では、狙いはいつももう一人で、ここの状態は何も使わない（計算の順も結果も今までと同じ）。
/// </summary>
public sealed partial class BoxingGame
{
    /// <summary>追加の席の選手（赤・青の後、席 2..）。</summary>
    private Fighter[] _others = [];

    /// <summary>追加の席の操作元（ボット）。</summary>
    private IFighterControl?[] _otherControls = [];

    /// <summary>追加の席の、この歩の指令（使い回し）。</summary>
    private FighterCommand[] _otherCommands = [];

    /// <summary>追加の席の皮膚（描画）。</summary>
    private BoxerSkin[] _otherSkins = [];

    /// <summary>席ごとの解決済みのスタイル（狙いの重み・休みの動作・倒れた相手への好み）。</summary>
    private BotStyle?[] _seatStyles = [];

    /// <summary>席ごとの狙いの重み。</summary>
    private RivalWeights[] _rivalWeights = [];

    /// <summary>席ごとの、立ったときの頭の高さ [m]（リングの面から。生成した直後に測る）。</summary>
    private float[] _standHead = [];

    /// <summary>席ごとの今の狙い（席の番号）。</summary>
    private int[] _target = [];

    /// <summary>恨み [受けた側の席, 打った側の席]（受けた打撃の強さの和 0..1。受けた側の <see cref="RivalWeights.RevengeHalfLife"/> で減る）。</summary>
    private float[,] _grudge = new float[0, 0];

    /// <summary>席ごとの、最後に打撃を当てた人の席（とどめの記録。−1 なら無し）。</summary>
    private int[] _lastHitBy = [];

    /// <summary>試合を通して取られたダウンの数（席ごと。ラウンドで体が作り直されても残す）。</summary>
    private int[] _downs = [0, 0];

    private readonly List<Elimination> _eliminations = [];
    private readonly List<Elimination> _recentEliminations = [];
    private readonly HashSet<Fighter> _eliminated = [];
    private RivalView[] _views = [];
    private readonly List<int> _candidates = [];

    /// <summary>3 人以上の試合か。</summary>
    public bool IsRoyale => _fighters.Length > 2;

    /// <summary>全員（席の順: 赤・青・追加の席）。</summary>
    public IReadOnlyList<Fighter> AllFighters => _fighters;

    /// <summary>選手の席の番号（赤 0・青 1・追加 2..。この試合の選手でなければ −1）。</summary>
    public int SeatOf(Fighter fighter) => Array.IndexOf(_fighters, fighter);

    /// <summary>バトルロイヤルの勝者（最後の 1 人）。決着前・時間切れ・2 人の試合は null。</summary>
    public Fighter? Winner { get; private set; }

    /// <summary>脱落の順（バトルロイヤル）。</summary>
    public IReadOnlyList<Elimination> Eliminations => _eliminations;

    /// <summary>直近の <see cref="Step"/> で脱落した人。</summary>
    public IReadOnlyList<Elimination> RecentEliminations => _recentEliminations;

    /// <summary>まだ戦っている（脱落していない）か。</summary>
    public bool InPlay(Fighter fighter) => !_eliminated.Contains(fighter);

    /// <summary><paramref name="fighter"/> が今狙っている相手（2 人の試合ではもう一人）。</summary>
    public Fighter TargetOf(Fighter fighter)
    {
        if (!IsRoyale)
        {
            return fighter == Player ? Cpu : Player;
        }

        var seat = SeatOf(fighter);
        return seat >= 0 && _target[seat] >= 0 ? _fighters[_target[seat]] : fighter == Player ? Cpu : Player;
    }

    /// <summary>画面に出す名前（赤・青・追加の席）。</summary>
    public string LabelOf(Fighter fighter)
    {
        var seat = SeatOf(fighter);
        return seat switch
        {
            0 => RedLabel,
            1 => BlueLabel,
            > 1 => _corners.Others![seat - 2].Label,
            _ => "",
        };
    }

    /// <summary>選手の操作元（調べもの・下見の道具用）。</summary>
    internal IFighterControl? ControlFor(Fighter fighter) => ControlOf(fighter);

    /// <summary>選手が今見ている状況（調べもの・下見の道具用）。</summary>
    internal FighterSense SenseFor(Fighter fighter) => ArmedSense(fighter, TargetOf(fighter));

    /// <summary>席 <paramref name="seat"/> の解決済みのスタイル（無ければ null）。</summary>
    private BotStyle? StyleOfSeat(int seat) => seat >= 0 && seat < _seatStyles.Length ? _seatStyles[seat] : null;

    /// <summary>席の状態を作り直す（<see cref="StartRound"/> で、全員を生成した後）。</summary>
    private void ResetSeats(float surfaceY)
    {
        var n = _fighters.Length;
        _seatStyles = new BotStyle?[n];
        _seatStyles[0] = _corners.RedBotStyle ?? _corners.PlayerStyle;
        _seatStyles[1] = n > 1 ? _corners.CpuStyle : null;
        for (var k = 2; k < n; k++)
        {
            _seatStyles[k] = _corners.Others![k - 2].Style;
        }

        _rivalWeights = [.. _seatStyles.Select(s => s?.Rival ?? RivalWeights.Default)];
        _standHead = [.. _fighters.Select(f => MathF.Max(1e-3f, f.HeadPosition.Y - surfaceY))];
        _target = [.. Enumerable.Range(0, n).Select(i => -1)];
        _lastHitBy = [.. Enumerable.Range(0, n).Select(i => -1)];
        _grudge = new float[n, n];
        _unattended = new float[n];
        _views = new RivalView[n];
        if (_downs.Length != n)
        {
            _downs = new int[n];
        }

        _eliminations.Clear();
        _recentEliminations.Clear();
        _eliminated.Clear();
        Winner = null;
        WinningTeam = null;
    }

    /// <summary>
    /// 打撃を覚える（バトルロイヤルの恨み・とどめ）: <paramref name="attacker"/> が <paramref name="victim"/> に <paramref name="damage"/> を当てた。
    /// 2 人の試合では何もしない（狙いは選ばない）。
    /// </summary>
    private void NoteHit(Fighter attacker, Fighter victim, float damage)
    {
        if (!IsRoyale || attacker == victim)
        {
            return;
        }

        var a = SeatOf(attacker);
        var v = SeatOf(victim);
        if (a < 0 || v < 0)
        {
            return;
        }

        _grudge[v, a] = MathF.Min(1f, _grudge[v, a] + HitLook.Strength(damage));
        _lastHitBy[v] = a;
    }

    /// <summary>
    /// 毎サブステップ、生きている全員の狙いを選び直す（<see cref="RivalChoice"/>）。候補の見える特徴は 1 人 1 回だけ測る。恨みはここで減らす。
    /// 狙いの選び方は入力元の判断だが、見え方（<see cref="FighterSense"/>）が相手 1 人の契約なので、席ごとのスタイルの値でここで選ぶ（docs/DEBT.md）。
    /// </summary>
    private void ChooseRivals(float dt)
    {
        if (!IsRoyale)
        {
            return;
        }

        var n = _fighters.Length;
        var surfaceY = _ring.SurfaceCenter.Y;
        for (var v = 0; v < n; v++)
        {
            var keep = MathF.Pow(2f, -dt / _rivalWeights[v].RevengeHalfLife);
            for (var a = 0; a < n; a++)
            {
                _grudge[v, a] *= keep;
            }
        }

        for (var j = 0; j < n; j++)
        {
            var f = _fighters[j];
            var lowness = Math.Clamp(1f - ((f.HeadPosition.Y - surfaceY) / _standHead[j]), 0f, 1f);
            _views[j] = new RivalView(f.ChestPosition, f.Frame.Forward, InPlay(f) ? f.Injury(felt: false) : 0f, lowness, 0f);
        }

        Span<RivalView> rivals = stackalloc RivalView[n];
        for (var i = 0; i < n; i++)
        {
            var self = _fighters[i];
            if (!InPlay(self))
            {
                continue;
            }

            _candidates.Clear();
            var current = -1;
            for (var j = 0; j < n; j++)
            {
                if (j == i || !InPlay(_fighters[j]) || _corners.TeamOf(j) == _corners.TeamOf(i))
                {
                    continue;
                }

                if (j == _target[i])
                {
                    current = _candidates.Count;
                }

                rivals[_candidates.Count] = _views[j] with { Grudge = _grudge[i, j] };
                _candidates.Add(j);
            }

            var pick = RivalChoice.Choose(self.ChestPosition, self.ArmReach, rivals[.._candidates.Count], current, _rivalWeights[i]);
            if (pick >= 0)
            {
                _target[i] = _candidates[pick];
            }
        }
    }

    /// <summary>
    /// バトルロイヤルの決着: 死んだ・場から落ちた人を脱落にし（とどめは最後に当てた人）、生き残りが 1 人以下なら終わる。
    /// ダウンの数え・判定は 2 人の決まりなので使わない（バトルロイヤルはレフェリーなし・ダウンを取らない VFC で回す）。
    /// </summary>
    private void CheckRoyaleFinish()
    {
        foreach (var fighter in _fighters)
        {
            if (_eliminated.Contains(fighter))
            {
                continue;
            }

            var cause = fighter.IsDead ? $"{fighter.DeathCause!.ToUpperInvariant()} DESTROYED" : _ring.IsOut(fighter.PelvisPosition) ? "OUT OF THE CAGE" : null;
            if (cause is null)
            {
                continue;
            }

            _eliminated.Add(fighter);
            var by = _lastHitBy[SeatOf(fighter)] is var seat and >= 0 ? _fighters[seat] : null;
            var elimination = new Elimination(fighter, by, _time, MatchRound, cause);
            _eliminations.Add(elimination);
            _recentEliminations.Add(elimination);
            ShowMessage(by is null ? $"{LabelOf(fighter)} — {cause}" : $"{LabelOf(by)} FINISHES {LabelOf(fighter)}", 2.5f);
        }

        // 残っているチームが 1 つ以下なら終わり（チームが無ければ席ごとに別のチームなので、最後の 1 人）。
        var survivors = Enumerable.Range(0, _fighters.Length).Where(s => InPlay(_fighters[s])).ToList();
        if (survivors.Select(_corners.TeamOf).Distinct().Count() > 1)
        {
            return;
        }

        WinningTeam = survivors.Count > 0 ? _corners.TeamOf(survivors[0]) : null;
        Winner = survivors.Count == 1 ? _fighters[survivors[0]] : null;
        if (Winner == Player)
        {
            PlayerWins++;
        }
        else if (Winner == Cpu)
        {
            CpuWins++;
        }

        _roundOverTimer = RoundResetDelay;
        RecentBell = RoundBell.End;
        var text = survivors.Count == 0 ? "NO ONE SURVIVES"
            : _corners.Teams is null ? $"LAST ONE STANDING — {LabelOf(Winner!)}"
            : $"TEAM WINS — {string.Join(", ", survivors.Select(s => LabelOf(_fighters[s])))}";
        ShowMessage(text, Rules is null ? RoundResetDelay : 1e6f);
    }

    /// <summary>勝ったチームの番号（<see cref="BoxingCorners.Teams"/> のある試合。チームの無いバトルロイヤルでは勝者の席）。決着前・時間切れは null。</summary>
    public int? WinningTeam { get; private set; }

    /// <summary>席ごとの、倒れて立てないまま誰にも攻められずにいる時間 [s]（再生の倍率 2^(t / StuckDoublingSeconds)）。</summary>
    private float[] _unattended = [];

    /// <summary>
    /// 3 人以上のラウンド中の再生（2 人の「両者ダウンの再生」を 1 人ずつへ広げた決まり）: 倒れていて意識だけでは立てない人は、自分を狙っている人が誰も
    /// 立っていない（か、誰にも狙われていない）間だけ、倍率 2^(t / <see cref="MatchRules.StuckDoublingSeconds"/>) で再生する。立って攻めに来られる人がいれば
    /// 再生しない（攻める場面を消さない）。2 人の決まりは「相手も立てない」まで求めるが、N 人では起き上がりの途中で止まった相手（立てる体なのに立ちきれない）を
    /// 待つと誰も動かなくなったので、狙う人は「立っているか」だけで見る（docs/DEBT.md「バトルロイヤルの近道」）。
    /// </summary>
    private void RegrowUnattended(MatchRules rules, float dt)
    {
        var n = _fighters.Length;
        if (_unattended.Length != n)
        {
            _unattended = new float[n];
        }

        var any = false;
        var rate = 0f;
        for (var i = 0; i < n; i++)
        {
            var stuck = rules.StuckDoublingSeconds > 0f && rules.RegrowthSpeed > 0f && rules.RegrowthTickSeconds > 0f && Unattended(i);
            _unattended[i] = stuck ? _unattended[i] + dt : 0f;
            any |= stuck;
            rate = stuck ? MathF.Max(rate, MathF.Pow(2f, _unattended[i] / rules.StuckDoublingSeconds)) : rate;
        }

        if (!any)
        {
            if (RegrowingWhileStuck)
            {
                _regrowClock = 0f; // 次に止まったときは、抽選の間隔を 0 から数える
            }

            RegrowingWhileStuck = false;
            RegrowthRate = 0f;
            return;
        }

        RegrowingWhileStuck = true;
        RegrowthRate = rate;
        _regrowRandom ??= new Random(_seed ^ 0x2e9e0);
        _regrowClock += dt;
        while (_regrowClock >= rules.RegrowthTickSeconds)
        {
            _regrowClock -= rules.RegrowthTickSeconds;
            for (var i = 0; i < n; i++)
            {
                if (_unattended[i] > 0f)
                {
                    RegrowOne(_fighters[i], rules.RegrowthSpeed * rules.RegrowthTickSeconds * MathF.Pow(2f, _unattended[i] / rules.StuckDoublingSeconds));
                }
            }
        }
    }

    /// <summary>席 <paramref name="seat"/> の人が倒れて立てず、狙っている人が誰も立っていないか。</summary>
    private bool Unattended(int seat)
    {
        var self = _fighters[seat];
        if (!InPlay(self) || self.State == FighterState.Standing || self.BodyLetsStand)
        {
            return false;
        }

        for (var j = 0; j < _fighters.Length; j++)
        {
            var hunter = _fighters[j];
            if (j != seat && _target[j] == seat && InPlay(hunter) && hunter.State == FighterState.Standing)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>1 人の体の前線を <paramref name="advance"/> [m] だけ再生させる（床へ写した古い中身・生え直しの記録も）。</summary>
    private void RegrowOne(Fighter fighter, float advance)
    {
        var report = fighter.Regrow(_regrowRandom!, advance);
        foreach (var piece in report.NewPieces)
        {
            UntouchedByWeaponBoxes(piece);
        }

        foreach (var part in report.Rebudded)
        {
            RecentRegrowths.Add(new RegrowthEvent(fighter, part));
        }
    }

    /// <summary>刃・武器がセルを削った（数える打撃でなくても）: とどめの記録だけ更新する。</summary>
    private void NoteCut(Fighter holder, Fighter victim)
    {
        if (IsRoyale && holder != victim && SeatOf(holder) is var a and >= 0 && SeatOf(victim) is var v and >= 0)
        {
            _lastHitBy[v] = a;
        }
    }

    /// <summary>生きている全員が倒れていて、誰も意識だけでは立てないか（両者ダウンの再生の条件を N 人へ。2 人なら今までと同じ）。</summary>
    private bool AllStuck()
    {
        foreach (var fighter in _fighters)
        {
            if (InPlay(fighter) && fighter.State == FighterState.Standing)
            {
                return false;
            }
        }

        foreach (var fighter in _fighters)
        {
            if (InPlay(fighter) && fighter.BodyLetsStand)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>誰かがダウン（審判が数える）か。</summary>
    private bool AnyKnockedDown()
    {
        foreach (var fighter in _fighters)
        {
            if (fighter.IsKnockedDown && InPlay(fighter))
            {
                return true; // 脱落した体（死んだ体）は数えない
            }
        }

        return false;
    }

    /// <summary>試合を通して受けたダメージの合計（得点が動いたかを見る）。</summary>
    private float ScoredDamage()
    {
        var scored = 0f;
        foreach (var fighter in _fighters)
        {
            scored += fighter.Condition.TotalDamage;
        }

        return scored;
    }
}
