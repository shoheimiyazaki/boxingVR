using System.Diagnostics;

namespace Aigis.Foundation.Voxels;

/// <summary>
/// 物理の 1 歩の時間の内訳を取る計測の道具（調査用: 既定では切ってあり、切っている間は <see cref="Begin"/> が 0 を返すだけで
/// 時計を読まない。試合の計算には一切響かない: 時間を数えるだけで、結果（チェックサム）は変わらない。単スレッド前提）。
/// 入れ子の枠は、外側の枠に内側の時間も含まれる（表に「含む」と書く）。
/// </summary>
public static class StepProfile
{
    /// <summary>計測の枠。</summary>
    public enum Slot
    {
        /// <summary>Bepu の 1 歩（<c>_world.Step</c>）。</summary>
        Bepu,
        /// <summary>入力元（ボット・操作）の判断と <c>Drive</c>（体への目標の指示）。</summary>
        Control,
        /// <summary>歩の前の姿勢を覚える。</summary>
        Remember,
        /// <summary>拳の当たり判定（<c>DetectHits</c>）。</summary>
        FistHits,
        /// <summary>武器の接触の全体（<c>ResolveWeapons</c>。以下の Sweep 各枠・Snap・Bend・MayTouch・Cut を含む）。</summary>
        Weapons,
        /// <summary>Sweep の組み立て（相対姿勢・候補セルの絞り込み）。</summary>
        SweepSetup,
        /// <summary>Sweep の走り（圧力・削り）。</summary>
        SweepRun,
        /// <summary>武器の折れの判定（<c>Snap</c>）。</summary>
        Snap,
        /// <summary>手足の骨の曲げの判定（<c>Bend</c>）。</summary>
        Bend,
        /// <summary>接触を調べる前の安い振り分け。</summary>
        MayTouch,
        /// <summary>接触の後始末（<c>Cut</c>: 傷・骨折・破片・音のイベント。Debris を含む）。</summary>
        Cut,
        /// <summary>破片の生成（<c>Shatter</c>・<c>SliceOff</c>）。</summary>
        Debris,
        /// <summary>蹴りの接触。</summary>
        Kicks,
        /// <summary>歩の後（グレイズ・回復の監視）。</summary>
        After,
        /// <summary>1 回の更新の、歩のあとの処理（破片・傷・ラウンド）。</summary>
        PostStep,
        /// <summary>SweepSetup のうち、相対姿勢（行列の逆・分解）。</summary>
        SetupRelative,
        /// <summary>SweepSetup のうち、遠いかの判定。</summary>
        SetupFar,
        /// <summary>SweepSetup のうち、候補セルの絞り込み。</summary>
        SetupCandidates,
        /// <summary>SweepRun のうち、最初に触れる標本を探す走査。</summary>
        RunScan,
        /// <summary>Snap のうち、梁の剛性。</summary>
        SnapStiffness,
        /// <summary>Snap のうち、折れ目の探索（FindBreak）。</summary>
        SnapFind,
        /// <summary>Snap のうち、体を割る（SplitAt）。</summary>
        SnapSplit,
        /// <summary>SweepRun のうち、相手の面の高さ場（FieldFront）。</summary>
        RunFront,
        /// <summary>SweepRun のうち、掃く間の Overlaps（最初の標本より後）。</summary>
        RunOverlaps,
        /// <summary>SweepRun のうち、面の下の圧力（PatchBins）。</summary>
        RunPatch,
        /// <summary>SweepRun のうち、押す力・耐える力（Give・Hold・Resistance）。</summary>
        RunHold,
        /// <summary>SweepRun のうち、面の下を潰す（CrushBeneath）。</summary>
        RunCrush,
        /// <summary>Control のうち、知覚の更新（<c>UpdatePerception</c>: 視覚の遅れ・ぶれ）。</summary>
        Perception,
        /// <summary>Control のうち、入力元の判断（<c>IFighterControl.Read</c>。以下の Bot 各枠を含む）。</summary>
        BotRead,
        /// <summary>Control のうち、体への指示（<c>Drive(Fighter, …)</c>。<c>ApplyControl</c> を含む）。</summary>
        DriveFighter,
        /// <summary><c>Fighter.ApplyControl</c> の全体。</summary>
        ApplyControl,
        /// <summary>ApplyControl のうち、スタミナ（<c>UpdateStamina</c>）。</summary>
        FighterStamina,
        /// <summary>ApplyControl のうち、立っている間の更新（<c>TickStanding</c>: 足の運び・サーボ・PD）。</summary>
        FighterStanding,
        /// <summary>ApplyControl のうち、倒れている間の更新（<c>TickDown</c>）。</summary>
        FighterDown,
        /// <summary>BotRead のうち（ボクサー）、相手の拳の知覚（<c>AttackPerception.Update</c>）。</summary>
        BotPerceive,
        /// <summary>BotRead のうち（ボクサー）、相手の読み・予測・脅威・間合いの層。</summary>
        BotRead2,
        /// <summary>BotRead のうち（ボクサー）、守り・攻めの選択。</summary>
        BotChoose,
        /// <summary>BotRead のうち（ボクサー）、技の目標の経路（<c>TechniqueExecution</c>・足の運び）。</summary>
        BotExecute,
        /// <summary>Control のうち、歩の前の拳の速さの記録・冷却・蹴りの振り分け。</summary>
        ControlTail,
        /// <summary>武器の格子が削れた歩の、つながっていない部分の切り離し（連結成分の探索と <c>SeverDisconnected</c>）。</summary>
        Disconnect,
        /// <summary>Bepu の歩の前の準備（浮いた武器・歩の前の姿勢・武器の冷却・キャンバスの速さ・運ぶ荷）。</summary>
        PreBepu,
        /// <summary>キャンバス（床）への衝突の検出（<c>DetectCanvasImpacts</c>）。</summary>
        CanvasImpacts,
        /// <summary>肘の接触（<c>ResolveElbows</c>）。</summary>
        Elbows,
        /// <summary>体のつながりで分ける（全員の <c>Fighter.SplitDisconnected</c>。剛体の作り直しを含む）。</summary>
        FighterSplit,
        /// <summary>PostStep のうち、破片の更新（<c>Debris.Step</c>）。</summary>
        PostDebris,
        /// <summary>PostStep のうち、傷の更新（<c>Wounds.Step</c>）。</summary>
        PostWounds,
        /// <summary>PostStep のうち、振りの音の検出（<c>DetectSwings</c>）。</summary>
        PostSwings,
        /// <summary>PostStep のうち、ラウンドの更新（<c>UpdateRound</c>）。</summary>
        PostRound,
    }

    /// <summary>この歩のこのスレッドの割り当て [byte]（計測を取る間だけ、<see cref="Reset"/> で基準を取る）。</summary>
    public static long AllocatedSinceReset => Enabled ? GC.GetAllocatedBytesForCurrentThread() - _allocBase : 0;

    private static long _allocBase;

    /// <summary>計測を取るか。</summary>
    public static bool Enabled { get; set; }

    /// <summary>この歩で <c>VoxelContact.Resolve</c> が呼ばれた回数。</summary>
    public static int ResolveCalls { get; private set; }

    /// <summary>この歩で、実際に触れた（<c>Touched</c>）接触の数。</summary>
    public static int Touches { get; private set; }

    private static readonly long[] Ticks = new long[Enum.GetValues<Slot>().Length];

    /// <summary>枠の開始。切っていれば 0。</summary>
    public static long Begin()
    {
        if (!Enabled)
        {
            return 0;
        }

        if (TrackAllocations)
        {
            AllocStack[_depth++] = GC.GetAllocatedBytesForCurrentThread();
        }

        return Stopwatch.GetTimestamp();
    }

    /// <summary>枠ごとの割り当て [byte] も数えるか（枠の入れ子ごとに GC の読みを足すので、時間の測りを少し重くする。時間の表とは別の走りで使う）。</summary>
    public static bool TrackAllocations { get; set; }

    private static readonly long[] AllocStack = new long[64];
    private static int _depth;
    private static readonly long[] AllocBytes = new long[Enum.GetValues<Slot>().Length];

    /// <summary>枠ごとの割り当て [byte]（入れ子の内側も含む）。<see cref="TrackAllocations"/> のときだけ。</summary>
    public static long Allocated(Slot slot) => AllocBytes[(int)slot];

    /// <summary>枠の終わり。</summary>
    public static void End(Slot slot, long start)
    {
        if (start != 0)
        {
            Ticks[(int)slot] += Stopwatch.GetTimestamp() - start;
            if (TrackAllocations)
            {
                AllocBytes[(int)slot] += GC.GetAllocatedBytesForCurrentThread() - AllocStack[--_depth];
            }
        }
    }

    /// <summary><c>Resolve</c> の呼び出しを数える。</summary>
    public static void CountResolve(bool touched)
    {
        if (Enabled)
        {
            ResolveCalls++;
            if (touched)
            {
                Touches++;
            }
        }
    }

    /// <summary>次の歩のために 0 に戻す。</summary>
    public static void Reset()
    {
        Array.Clear(Ticks);
        Array.Clear(AllocBytes);
        _depth = 0;
        _allocBase = Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
        ResolveCalls = 0;
        Touches = 0;
    }

    /// <summary>枠ごとの時間 [ms]。</summary>
    public static double Milliseconds(Slot slot) => Ticks[(int)slot] * 1000.0 / Stopwatch.Frequency;
}
