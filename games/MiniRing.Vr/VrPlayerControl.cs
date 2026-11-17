using MiniRing.Motion;

namespace MiniRing.Vr;

/// <summary>
/// VR の姿勢・ボタン → プレイヤーのファイター指令。OpenXR の入力（<see cref="XrInputFrame"/>）を、ホスト非依存の写像
/// （<see cref="VrPlayerMapper"/>）へ渡すだけの薄い層。写像の説明（腕・グリップ・トリガーと武器の滑り・向き）は <see cref="VrPlayerMapper"/> を見る。
/// A・X: 身長キャリブレーション（まっすぐ立って押す。初回は自動）。向きは常に相手へロックオン。
/// </summary>
internal sealed class VrPlayerControl
{
    public VrPlayerControl(bool weaponMode = false)
    {
        Mapper = new VrPlayerMapper(weaponMode)
        {
            OnCalibrated = (eye, height) => Console.WriteLine($"[vr] calibrated: eye {eye:0.00} m -> height {height:0.00} m"),
        };
    }

    public VrPlayerMapper Mapper { get; }

    public float PlayerHeight => Mapper.PlayerHeight;

    public BoxingInput Read(in XrInputFrame frame, in FighterSense sense, float dt) => Mapper.Read(frame.ToRaw(), sense, dt);
}
