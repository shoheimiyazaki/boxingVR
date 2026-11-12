using System.Numerics;

namespace MiniRing;

/// <summary>
/// 的の構え: 頭のずれ（屈む・反る）と、両手（拳）の位置（体フレーム、腕の長さ単位。X は左手の側を正、右手は左右を返す）。
/// 体はこれをバネで追うので、屈めば頭も胸も下がり、反れば頭が後ろへ逃げる。
/// </summary>
/// <param name="Name">名前。</param>
/// <param name="Head">頭のずれ [腕の長さ]（<see cref="FighterCommand.HeadOffset"/>）。</param>
/// <param name="Hands">左手の位置 [腕の長さ]（右手は X を返した所）。</param>
public sealed record GuardPosture(string Name, Vector3 Head, Vector3 Hands)
{
    /// <summary>ふつうの構え: 両手を顔の前に上げて立つ。</summary>
    public static GuardPosture Upright { get; } = new("ふつう", Vector3.Zero, new Vector3(0.15f, 0.1f, 0.45f));

    /// <summary>屈む: 頭を下げて前へ沈む（頭も胸も低くなる）。</summary>
    public static GuardPosture Crouched { get; } = new("屈む", new Vector3(0f, -0.35f, 0.1f), new Vector3(0.15f, 0f, 0.45f));

    /// <summary>反る: 頭を後ろへ引く（頭が遠くなる）。</summary>
    public static GuardPosture Leaning { get; } = new("反る", new Vector3(0f, 0f, -0.3f), new Vector3(0.15f, 0.1f, 0.4f));

    /// <summary>構えが高い: 両手を額の上へ上げる（頭を拳で守り、胴が空く）。</summary>
    public static GuardPosture HighGuard { get; } = new("高い構え", Vector3.Zero, new Vector3(0.12f, 0.45f, 0.35f));

    /// <summary>構えが低く前: 両手を腰の高さで前へ出す（小手が前に出て、頭が空く）。</summary>
    public static GuardPosture LowGuard { get; } = new("低い構え", Vector3.Zero, new Vector3(0.12f, -0.35f, 0.6f));

    /// <summary>ドリルで使う構えの全部。</summary>
    public static IReadOnlyList<GuardPosture> All { get; } = [Upright, Crouched, Leaning, HighGuard, LowGuard];
}

/// <summary>
/// その場で構えて立っているだけの操作元: 相手を向き、構え（<see cref="GuardPosture"/>。既定は両手を顔の前に上げる）をとり、動かない。
/// 武器の実験場やテスト・ドリルの「的」に使う。
/// </summary>
public sealed class StandGuard(GuardPosture? posture = null) : IFighterControl
{
    private readonly GuardPosture _posture = posture ?? GuardPosture.Upright;

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var face = sense.OpponentChest - sense.Self.Origin;
        face.Y = 0f;
        var reach = sense.ArmReach;
        var hands = _posture.Hands;
        HandCommand Guard(Hand hand) => new(new Vector3(hand.LocalSide() * hands.X, hands.Y, hands.Z) * reach, false);
        return new FighterCommand(Vector3.Zero, face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ, Guard(Hand.Left), Guard(Hand.Right),
            HeadOffset: _posture.Head * reach);
    }
}
