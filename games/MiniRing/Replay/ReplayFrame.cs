using System.Numerics;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Replay;

/// <summary>
/// リプレイの走査（<see cref="ReplayScanner"/>）が試合時間の一定間隔で写した 1 枚の場面。再生・巻き戻し・シークはこれを引いて描く
/// （試合をもう一度進めない）。中身は描画の材料だけで、試合の計算には戻らない。
/// </summary>
/// <remarks>
/// <c>Time</c>: 記録の時計 [s]（そのステップまでの dt の和）。<br/>
/// <c>Step</c>: 写した時点で再生し終えたステップの数（0 は試合の開始前）。<br/>
/// <c>Checksum</c>: 写した時点の <see cref="BoxingGame.StateChecksum"/>。先頭から再生した試合と同じかを確かめる鍵。<br/>
/// <c>HudSize</c>: 試合の HUD（<see cref="Text"/>・<see cref="Shapes"/>）を組んだ画面の大きさ [px]。描くときは今の画面へ伸ばす。<br/>
/// <c>Focus</c>: カメラが追う点と、10 カウントの表示に要る物（表示の空間）。<br/>
/// <c>Sound</c>: 前の場面からこの場面までに鳴った音。前へ再生したときだけ鳴らす。
/// </remarks>
public sealed record ReplayFrame(
    float Time,
    int Step,
    ulong Checksum,
    PrimitiveInstance[] Instances,
    DebugLine[] Lines,
    ParticleInstance[] Particles,
    VoxelSnapshot Voxels,
    SceneLighting Lighting,
    ShadowMapConfig? Shadow,
    PostProcessSettings? PostProcess,
    Vector2 HudSize,
    ScreenTextDraw[] Text,
    UiShapeDraw[] Shapes,
    (Matrix4x4 Matrix, Vector3 Eye)? RedFace,
    (Matrix4x4 Matrix, Vector3 Eye)? BlueFace,
    ReplayFocus Focus,
    ReplayFrameSound Sound);

/// <summary>カメラの追う点と倒れた選手の数え（すべて表示の空間）。</summary>
/// <param name="RedChest">赤（プレイヤー側）の胸。</param>
/// <param name="BlueChest">青（CPU 側）の胸。</param>
/// <param name="RingCenter">リングの中央の床の少し上。</param>
/// <param name="RedCount">赤が数えられている数（0 なら数えていない）。</param>
/// <param name="BlueCount">青が数えられている数。</param>
public readonly record struct ReplayFocus(Vector3 RedChest, Vector3 BlueChest, Vector3 RingCenter, int RedCount, int BlueCount);

/// <summary>前の場面からこの場面までに鳴った音（選手の参照は持たない: 古いラウンドの世界を抱え込まないため）。</summary>
public sealed record ReplayFrameSound(
    float Dt,
    ImpactCue[] Cues,
    ShatterEvent[] Shatters,
    DebrisLanding[] Landings,
    SwingEvent[] Swings,
    RoundBell Bell,
    SeverEvent[] Severs)
{
    public static ReplayFrameSound Silent { get; } = new(0f, [], [], [], [], RoundBell.None, []);

    public bool IsSilent => Cues.Length == 0 && Shatters.Length == 0 && Landings.Length == 0 && Swings.Length == 0
        && Bell == RoundBell.None && Severs.Length == 0;

    /// <summary>この音の後に <paramref name="later"/> の音が続いたものとして 1 つにまとめる（場面を間引くとき）。</summary>
    public ReplayFrameSound Then(ReplayFrameSound later)
    {
        ArgumentNullException.ThrowIfNull(later);
        return new(Dt + later.Dt, [.. Cues, .. later.Cues], [.. Shatters, .. later.Shatters], [.. Landings, .. later.Landings],
            [.. Swings, .. later.Swings], later.Bell != RoundBell.None ? later.Bell : Bell, [.. Severs, .. later.Severs]);
    }
}
