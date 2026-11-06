using System.Numerics;

namespace MiniRing;

public enum Hand
{
    Left = 0,
    Right = 1,
}

public static class HandExtensions
{
    /// <summary>
    /// 体フレームのローカル X でその手がある側（左 +1 / 右 −1）。
    /// Aigis は右手系・Y 上なので、+Z を向いた人の右手側は −X になる（右 = 前 × 上 = Z × Y = −X）。
    /// </summary>
    public static float LocalSide(this Hand hand) => hand == Hand.Left ? 1f : -1f;
}

/// <summary>片手ぶんの指令。目標はファイターの体フレーム（肩中心・向きヨー）のローカル座標 [m]。</summary>
public readonly record struct HandCommand(Vector3 TargetLocal, bool Grip);

/// <summary>
/// 1 ステップ分の操作指令 — 入力元（VR / キーボード / AI）とラグドールの唯一の契約。
/// ラグドールはこれを位置として書き込まず、バネ・ダンパの目標として追う。
/// </summary>
public readonly record struct FighterCommand(Vector3 MoveWorld, Vector3 FaceDirection, HandCommand Left, HandCommand Right)
{
    public HandCommand For(Hand hand) => hand == Hand.Left ? Left : Right;

    /// <summary>水平・長さ 1 以下に正規化した移動指令。</summary>
    public Vector3 ClampedMove
    {
        get
        {
            var flat = new Vector3(MoveWorld.X, 0f, MoveWorld.Z);
            var length = flat.Length();
            return length > 1f ? flat / length : flat;
        }
    }
}

/// <summary>入力元がファイターの状況を知るための読み取り専用情報。</summary>
public readonly record struct FighterSense(BodyFrame Self, float ArmReach, Vector3 OpponentHead, Vector3 OpponentChest, bool OpponentAlive);

/// <summary>ファイターの操作元。VR・キーボード・AI がこれを実装する。</summary>
public interface IFighterControl
{
    public FighterCommand Read(in FighterSense sense, float dt);

    /// <summary>自分のパンチが当たった（ハプティクス等のフィードバック用）。</summary>
    public void OnPunchLanded(Hand hand, float damage)
    {
    }
}

/// <summary>外部（ホスト）が毎フレーム書き込む指令をそのまま返す操作元。デスクトップ/VR ホストが使う。</summary>
public sealed class ExternalControl : IFighterControl
{
    public FighterCommand Command { get; set; }

    public Action<Hand, float>? PunchLanded { get; set; }

    public FighterCommand Read(in FighterSense sense, float dt) => Command;

    public void OnPunchLanded(Hand hand, float damage) => PunchLanded?.Invoke(hand, damage);
}
