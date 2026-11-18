using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Vr;

/// <summary>
/// VR でお手本の動きを作る稽古（<c>--training katana</c>、docs/VR_MOTION.md §5）: 正眼に構えて動かない相手へ切り込む動きを、型ごとに記録する。
/// <list type="bullet">
/// <item>右スティック 左・右: 前・次の型。変えるたびにワールドを作り直し、その型の間（<see cref="TrainingForm.Gap"/>）で向き合う（記録中なら保存して止める）。</item>
/// <item>右スティック 下: 同じ型のまま作り直す（相手を斬り崩したあと・立ち位置がずれたとき）。</item>
/// <item>B / Y: 記録の開始・停止（1 回 = 1 ファイル <c>motions/&lt;日時&gt;_katana_&lt;型&gt;.vrmotion</c>。注に型と間）。</item>
/// <item>ヘッドセットの中ではコンソールが見えないので、合図は振動: 型を変えた = 右手に短く 1 回、作り直した = 両手に短く、記録の開始 = 右手に強め、停止 = 両手に長め。</item>
/// </list>
/// 試合のスレッドから <see cref="Step"/> を呼ぶ。今の試合は <see cref="Game"/>（作り直すと別の物になる）。試合の記録（replays）は取らない。
/// </summary>
internal sealed class VrTrainingSession : IDisposable
{
    private readonly WeaponKind _weapon;
    private readonly FighterTuning _tuning;
    private readonly bool _firstPerson;
    private readonly StickFlick _choose = new();
    private readonly StickFlick _reset = new();
    private readonly VrMotionSession _motion;
    private readonly List<VrHaptic> _haptics = [];
    private int _index;

    public VrTrainingSession(WeaponKind weapon, FighterTuning tuning, string motionsDirectory, bool firstPerson = false)
    {
        _weapon = weapon;
        _tuning = tuning;
        _firstPerson = firstPerson;
        // 型の間は体の腕の長さで決まるので、一度作った試合の体から腕の長さを読む。
        using (var probe = new BoxingGame(tuning, corners: SwordTraining.Corners(weapon, 1f)))
        {
            Forms = SwordTraining.Forms(weapon, probe.PlayerSense.ArmReach);
        }

        if (Forms.Count == 0)
        {
            throw new ArgumentException($"{weapon} には型がありません。", nameof(weapon));
        }

        _motion = new VrMotionSession(weapon, motionsDirectory, startRecording: false,
            label: () => $"{weapon.ToString().ToLowerInvariant()}_{Current.Name}",
            note: () => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"training {Current.Name} gap {Current.Gap:0.000} m (seigan target)"));
        Game = Create();
        Console.WriteLine($"[vr] training: {weapon}, {Forms.Count} forms: {string.Join(", ", Forms.Select(f => f.Name))}");
        Console.WriteLine("[vr] right stick left/right = previous/next form (resets the stance), right stick down = reset, B/Y = start/stop recording");
        Announce();
    }

    /// <summary>型の並び（右スティックで選ぶ順）。</summary>
    public IReadOnlyList<TrainingForm> Forms { get; }

    public TrainingForm Current => Forms[_index];

    /// <summary>今の試合（型を変える・作り直すと別の物になる）。</summary>
    public BoxingGame Game { get; private set; }

    /// <summary>
    /// 1 歩: スティックで型を変える・作り直す → 記録の開始・停止と 1 コマの記録 → 試合を 1 歩進める。<paramref name="input"/> は今の試合の体の感覚から作った指令。
    /// </summary>
    public void Step(in XrInputFrame frame, BoxingInput input, VrPlayerMapper mapper, float dt)
    {
        var choose = _choose.Step(frame.RightStick.X);
        var reset = _reset.Step(frame.RightStick.Y) < 0;
        if (choose != 0)
        {
            _motion.Stop();
            _index = (_index + choose + Forms.Count) % Forms.Count;
            Rebuild();
            mapper.ResetPlacement(); // first person: re-align the transform to the rebuilt puppet
            Announce();
            _haptics.Add(new VrHaptic(Hand.Right, 0.4f, 0.05f));
        }
        else if (reset)
        {
            _motion.Stop();
            Rebuild();
            mapper.ResetPlacement(); // first person: re-align the transform to the rebuilt puppet
            Console.WriteLine($"[vr] reset: {Current.Name}");
            _haptics.Add(new VrHaptic(Hand.Left, 0.4f, 0.05f));
            _haptics.Add(new VrHaptic(Hand.Right, 0.4f, 0.05f));
        }

        var wasRecording = _motion.Recording;
        _motion.Step(frame, input.Player, mapper, Game.PlayerSense.ArmReach, dt);
        if (_motion.Recording != wasRecording)
        {
            if (_motion.Recording)
            {
                _haptics.Add(new VrHaptic(Hand.Right, 0.8f, 0.15f));
            }
            else
            {
                _haptics.Add(new VrHaptic(Hand.Left, 0.8f, 0.35f));
                _haptics.Add(new VrHaptic(Hand.Right, 0.8f, 0.35f));
            }
        }

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

    private BoxingGame Create() => new(_tuning, corners: SwordTraining.Corners(_weapon, Current.Gap)) { HidePlayerHead = _firstPerson };

    private void Rebuild()
    {
        var old = Game;
        Game = Create();
        old.Dispose();
    }

    private void Announce()
        => Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[vr] form {_index + 1}/{Forms.Count}: {Current.Name} (start gap {Current.Gap:0.00} m in the game = {Current.Gap / _tuning.Scale:0.00} m at human size)"));
}
