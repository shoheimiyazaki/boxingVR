using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Vr;

/// <summary>
/// VR の姿勢・ボタン → プレイヤーのファイター指令。OpenXR の入力（<see cref="XrInputFrame"/>）を、ホスト非依存の写像
/// （<see cref="VrPlayerMapper"/>）へ渡すだけの薄い層。写像の説明（グリップで持つ・① 右手で両手・② 一人称）は <see cref="VrPlayerMapper"/> を見る。
/// A・X: 身長キャリブレーション（まっすぐ立って押す。初回は自動）。向きは常に相手へロックオン。
/// </summary>
internal sealed class VrPlayerControl
{
    /// <param name="weapon">自分の武器（null = 素手の試合: 今まで通りの写像）。武器の試合はグリップで持つ。</param>
    /// <param name="twoHand">① 両手持ちの左手の決め方。</param>
    /// <param name="firstPerson">② 一人称。</param>
    /// <param name="faceButtons">稽古の操作: A 単独 = 合わせ、4 つ同時 = 作り直し、X・Y = 蹴り。</param>
    public VrPlayerControl(WeaponKind? weapon = null, VrTwoHand twoHand = VrTwoHand.Right, bool firstPerson = false, bool faceButtons = false)
    {
        Mapper = new VrPlayerMapper(weaponMode: weapon is not null)
        {
            GripHolds = weapon is not null,
            TwoHand = twoHand,
            FirstPerson = firstPerson,
            FaceButtonControls = faceButtons,
            HeldWeapon = weapon,
            OnCalibrated = (eye, height) => Console.WriteLine($"[vr] calibrated: eye {eye:0.00} m -> height {height:0.00} m"),
        };
    }

    /// <summary>旧（2026-11-17）: トリガーで握り、離すと棒が滑る写像（<see cref="VrPlayerMapper.GripHolds"/> = false）。写像の単体テストと合成の記録用。</summary>
    public VrPlayerControl(bool weaponMode)
    {
        Mapper = new VrPlayerMapper(weaponMode);
    }

    public VrPlayerMapper Mapper { get; }

    public float PlayerHeight => Mapper.PlayerHeight;

    public BoxingInput Read(in XrInputFrame frame, in FighterSense sense, float dt) => Mapper.Read(frame.ToRaw(), sense, dt);
}
