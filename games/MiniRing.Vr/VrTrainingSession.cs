using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Vr;

/// <summary>
/// VR でお手本の動きを作る稽古（<c>--training katana</c>、docs/VR_MOTION.md §5）: 正眼に構えて動かない相手へ切り込む動きを、型ごとに記録する。
/// <list type="bullet">
/// <item>右スティック 左・右: 前・次の型。変えるたびにワールドを作り直し、その型の間（<see cref="TrainingForm.Gap"/>）で向き合う（記録中なら保存して止める）。</item>
/// <item>右スティック 上・下: 前・次の相手（<see cref="SwordTraining.Opponents"/>: 構えて動かない・無防備・同じ武器で戦ってくる・素手の各スタイル）。変えるたびに作り直す。</item>
/// <item>自分の武器は手に持たずに始まり、目の前に浮いている（グリップで取る。放して床で止まると、また目の前に浮く: <see cref="BoxingCorners.PlayerWeaponFloats"/>）。</item>
/// <item>A + B + X + Y を同時に: 同じ型のまま作り直す（相手を斬り崩したあと・立ち位置がずれたとき）。A 単独（離したとき）は身長の合わせ。X = 左脚・Y = 右脚の蹴り（間合いで前蹴り・膝蹴り: <see cref="KickRunner"/>）。</item>
/// <item>記録は常に取る: 稽古を始めたときから続け、型を変える・作り直すたびに新しい 1 ファイル（<c>motions/&lt;日時&gt;_&lt;武器&gt;_&lt;型&gt;.vrmotion</c>。注に型と間）。終了時に今の分を保存する。</item>
/// <item>ヘッドセットの中ではコンソールが見えないので、合図は振動: 型を変えた = 右手に短く 1 回、作り直した = 両手に短く。</item>
/// </list>
/// 試合のスレッドから <see cref="Step"/> を呼ぶ。今の試合は <see cref="Game"/>（作り直すと別の物になる）。試合の記録（replays）は取らない。
/// </summary>
internal sealed class VrTrainingSession : IDisposable
{
    private readonly WeaponKind _weapon;
    private readonly FighterTuning _tuning;
    private readonly bool _firstPerson;
    private readonly HandWear _wear;
    private readonly StickFlick _choose = new();
    private readonly StickFlick _pickOpponent = new();
    private readonly VrMotionSession _motion;
    private readonly List<VrHaptic> _haptics = [];
    private int _index;
    private int _opponent;

    public VrTrainingSession(WeaponKind weapon, FighterTuning tuning, string motionsDirectory, bool firstPerson = false, HandWear wear = HandWear.Glove)
    {
        _weapon = weapon;
        _tuning = tuning;
        _firstPerson = firstPerson;
        _wear = wear;
        // 型の間は体の腕の長さで決まるので、一度作った試合の体から腕の長さを読む。
        using (var probe = new BoxingGame(tuning, corners: SwordTraining.Corners(weapon, 2.817f, wear)))
        {
            Forms = SwordTraining.Forms(weapon, probe.PlayerSense.ArmReach);
        }

        Opponents = SwordTraining.Opponents(weapon);

        if (Forms.Count == 0)
        {
            throw new ArgumentException($"{weapon} には型がありません。", nameof(weapon));
        }

        _motion = new VrMotionSession(weapon, motionsDirectory, startRecording: true,
            label: () => $"{weapon.ToString().ToLowerInvariant()}_{Current.Name}",
            note: () => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"training {Current.Name} gap {Current.Gap:0.000} m (seigan target)"));
        Game = Create();
        Console.WriteLine($"[vr] training: {weapon}, {Forms.Count} forms: {string.Join(", ", Forms.Select(f => f.Name))}");
        Console.WriteLine("[vr] recording always (one file per form / reset). right stick left/right = previous/next form, right stick up/down = previous/next opponent, A+B+X+Y together = reset, X = left kick, Y = right kick, A alone = height calibration");
        Announce();
    }

    /// <summary>型の並び（右スティックで選ぶ順）。</summary>
    public IReadOnlyList<TrainingForm> Forms { get; }

    public TrainingForm Current => Forms[_index];

    /// <summary>相手の並び（右スティックの上下で選ぶ順）。</summary>
    public IReadOnlyList<string> Opponents { get; }

    /// <summary>今の相手。</summary>
    public string Opponent => Opponents[_opponent];

    /// <summary>今の試合（型を変える・作り直すと別の物になる）。</summary>
    public BoxingGame Game { get; private set; }

    /// <summary>
    /// 1 歩: スティックで型を変える・作り直す → 記録の開始・停止と 1 コマの記録 → 試合を 1 歩進める。<paramref name="input"/> は今の試合の体の感覚から作った指令。
    /// </summary>
    public void Step(in XrInputFrame frame, BoxingInput input, VrPlayerMapper mapper, float dt)
    {
        var choose = _choose.Step(frame.RightStick.X);
        var pick = _pickOpponent.Step(-frame.RightStick.Y); // 上へ倒す（+Y）= 前の相手
        var reset = mapper.ResetRequested; // A + B + X + Y を同時に
        if (pick != 0)
        {
            _opponent = (_opponent + pick + Opponents.Count) % Opponents.Count;
            Rebuild();
            _motion.Restart();
            mapper.ResetPlacement(); // first person: re-align the transform to the rebuilt puppet
            Announce();
            _haptics.Add(new VrHaptic(Hand.Right, 0.4f, 0.05f));
        }
        else if (choose != 0)
        {
            _index = (_index + choose + Forms.Count) % Forms.Count;
            Rebuild();
            _motion.Restart(); // 型ごとに 1 ファイル（直前の記録を保存して、この型の記録を始める）
            mapper.ResetPlacement(); // first person: re-align the transform to the rebuilt puppet
            Announce();
            _haptics.Add(new VrHaptic(Hand.Right, 0.4f, 0.05f));
        }
        else if (reset)
        {
            Rebuild();
            _motion.Restart(); // 作り直すたびに新しいファイル（1 ファイル = 作り直しから次の作り直しまで）
            mapper.ResetPlacement(); // first person: re-align the transform to the rebuilt puppet
            Console.WriteLine($"[vr] reset: {Current.Name}");
            _haptics.Add(new VrHaptic(Hand.Left, 0.4f, 0.05f));
            _haptics.Add(new VrHaptic(Hand.Right, 0.4f, 0.05f));
        }

        _motion.Step(frame, input.Player, mapper, Game.PlayerSense.ArmReach, dt);

        Game.Step(input, dt);
    }

    /// <summary>この歩までにたまった振動の合図（取り出すと空になる）。</summary>
    public VrHaptic[] DrainHaptics()
    {
        var haptics = _haptics.ToArray();
        _haptics.Clear();
        return haptics;
    }

    public void Dispose()
    {
        _motion.Dispose();
        Game.Dispose();
    }

    private BoxingGame Create() => new(_tuning, corners: SwordTraining.Corners(_weapon, Current.Gap, _wear, Opponent, floats: true)) { HidePlayerHead = _firstPerson, PlayerBodyOpacity = _firstPerson ? BoxingRenderer.FirstPersonBodyOpacity : 1f };

    private void Rebuild()
    {
        var old = Game;
        Game = Create();
        old.Dispose();
    }

    private void Announce()
        => Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[vr] opponent {_opponent + 1}/{Opponents.Count}: {Opponent}, form {_index + 1}/{Forms.Count}: {Current.Name} (start gap {Current.Gap:0.00} m in the game = {Current.Gap / _tuning.Scale:0.00} m at human size)"));
}
