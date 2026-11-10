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
/// <para><paramref name="HeadOffset"/> は頭の構えからのずれ（体フレームのローカル [m]）: 前へ出せば前傾、横へずらせば
/// スリップ（横傾き）、下げればダッキング（沈み込み）。体の軸の向きは両手の位置とこの頭の位置から決まる。</para>
/// <para><paramref name="TwistBias"/> は胴のひねりの上乗せ [rad]（正で右肩が前）。拳より先に腰と肩を回す（打つ前の溜め）のに使う。</para>
/// </summary>
public readonly record struct FighterCommand(Vector3 MoveWorld, Vector3 FaceDirection, HandCommand Left, HandCommand Right, Vector3 HeadOffset = default, float TwistBias = 0f)
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

/// <summary>拳 1 つの見え方（ワールド座標の位置と速度）。</summary>
public readonly record struct GloveSense(Vector3 Position, Vector3 Velocity);

/// <summary>
/// 相手の胴の見え方（ワールド座標の位置）: 両肩と腰。パンチは拳より先に腰と肩が動き出す（体重移動・ひねり・踏み込み）ので、
/// 上級者は拳ではなくここを見て次の拳を読む。
/// </summary>
public readonly record struct BodySense(Vector3 LeftShoulder, Vector3 RightShoulder, Vector3 Pelvis)
{
    public Vector3 Shoulder(Hand hand) => hand == Hand.Left ? LeftShoulder : RightShoulder;
}

/// <summary>入力元がファイターの状況を知るための読み取り専用情報。OpponentAlive は相手が打ってよい状態か
/// （KO されておらず、立っている）。倒れた相手には手を出さない。
/// <para>SelfHead と相手の拳（<paramref name="OpponentLeft"/> / <paramref name="OpponentRight"/>）は、ボットが飛んでくる拳を
/// 見て守るためのもの（ブロック・ダッキング・スリップ・スウェー）。<paramref name="OpponentVelocity"/> は相手の胴の速度で、
/// 拳の動きから歩く動きを差し引いて「パンチの動き出し」を見るのに使う。<paramref name="OpponentBody"/> は相手の両肩と腰
/// （打つ前の体の動きを読むため。見えないホストでは既定値）。</para></summary>
public readonly record struct FighterSense(
    BodyFrame Self, float ArmReach, Vector3 OpponentHead, Vector3 OpponentChest, bool OpponentAlive,
    Vector3 SelfHead = default, GloveSense OpponentLeft = default, GloveSense OpponentRight = default, Vector3 OpponentVelocity = default,
    BodySense OpponentBody = default)
{
    public GloveSense Opponent(Hand hand) => hand == Hand.Left ? OpponentLeft : OpponentRight;
}

/// <summary>ファイターの操作元。VR・キーボード・AI がこれを実装する。</summary>
public interface IFighterControl
{
    public FighterCommand Read(in FighterSense sense, float dt);

    /// <summary>自分のパンチが当たった（ハプティクス等のフィードバック用）。</summary>
    public void OnPunchLanded(Hand hand, float damage)
    {
    }

    /// <summary>自分が打たれた（ボットが「守れたか」を知るため）。</summary>
    public void OnPunchTaken(in HitEvent hit)
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
