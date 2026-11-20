using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>お手本づくりの稽古の 1 つの型: 名前と、その型で切り込み始める間（両者の足と足の距離 [m]）。</summary>
/// <param name="Name">型の名前（<see cref="FormSwordsman.Forms"/>）。</param>
/// <param name="Gap">始めの間 [m]: 型の動き（踏み込みを含む）で物打ちが相手の体の面にちょうど届く距離（<see cref="StrikeDrill.ReachOf"/>）。</param>
public sealed record TrainingForm(string Name, float Gap);

/// <summary>
/// VR でお手本の動きを作る稽古（docs/VR_MOTION.md §5）の、ホストに依らない部分: 型の並びと始めの間、構えて動かない相手の試合の設定。
/// 人はスティックで型を選び、選ぶたびにワールドを作り直して（§1.1: 位置は書き込まず、作り直す）その型の間で向き合い、
/// 正眼（中段）に構えて動かない相手へ切り込む動きを記録する。記録は <c>.vrmotion</c>（§2）。
/// </summary>
public static class SwordTraining
{
    /// <summary>稽古ができる武器か（型で動かす武器と、型の無いこんぼう）。</summary>
    public static bool Supports(WeaponKind? weapon) => FormSwordsman.HasForms(weapon) || weapon == WeaponKind.WoodenClub;

    /// <summary>
    /// 型の一覧・構えを取る武器。こんぼう（片手で振る木の棒）には専用の型が無いので、バットの型を借りる（近道: docs/DEBT.md「VR の稽古のこんぼう」。
    /// 型の間合いはバットの寸法から出るので、こんぼうでは目安）。それ以外は武器そのもの。
    /// </summary>
    public static WeaponKind FormsWeapon(WeaponKind weapon) => weapon == WeaponKind.WoodenClub ? WeaponKind.Bat : weapon;

    /// <summary>稽古のリング（武器のドリルの広さ。間が長い型でも縁から離れる）。</summary>
    public static RingLayout Ring => RingLayout.DrillWeapons;

    /// <summary>始めの間の下限 [m]（柄頭打ち・押し斬りのように内側で使う型でも、始めは体が重ならない）。</summary>
    public const float MinGap = 0.563f;

    /// <summary>
    /// 武器 <paramref name="weapon"/> の型の全部（間の短い型も含む。稽古なので試合の技の下限 <see cref="FormSwordsman.MinReach"/> では外さない）と、
    /// 腕の長さ <paramref name="armReach"/> の体で切り込み始める間。
    /// </summary>
    public static IReadOnlyList<TrainingForm> Forms(WeaponKind weapon, float armReach)
    {
        var maxGap = Ring.Size - 1.127f; // リングの縁から 56 cm ずつ内（RingLayout の決まりより広め）
        return
        [
            .. FormSwordsman.Techniques(FormsWeapon(weapon), armReach, float.NegativeInfinity)
                .Select(t => new TrainingForm(t.Name, Math.Clamp(t.Range, MinGap, maxGap))),
        ];
    }

    /// <summary>
    /// 稽古の試合の設定: 赤（プレイヤー = VR の人）も青（相手）も <paramref name="weapon"/> を握り、青は構えて動かない（<see cref="GuardHold"/>）。
    /// 2 人は中央で <paramref name="gap"/> だけ離れて向き合う。審判・休みの動作は無し。
    /// </summary>
    public static BoxingCorners Corners(WeaponKind weapon, float gap, HandWear wear = HandWear.Glove) => new(
        "seigan",
        _ => new GuardHold(weapon),
        PlayerWear: wear,
        CpuWear: wear,
        PlayerWeapon: weapon,
        CpuWeapon: weapon,
        PlayerTwoHanded: FormSwordsman.Handles(weapon),
        CpuTwoHanded: FormSwordsman.Handles(weapon),
        Ring: Ring with { StartGap = gap },
        Referee: false,
        BreakActions: false);
}

/// <summary>
/// 構えたまま動かない相手: 武器の型の構え（刀は中段 = 正眼。<see cref="FormSwordsman.Guard"/>）を、相手へ向いてとり続ける。歩かない・打たない・受けない。
/// 体はバネで構えを追うので、打たれれば持っていかれ、また構えへ戻る。
/// </summary>
public sealed class GuardHold(WeaponKind weapon, FighterTuning? tuning = null) : IFighterControl
{
    private readonly FighterTuning _tuning = tuning ?? FighterTuning.Default;

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var face = sense.OpponentChest - sense.Self.Origin;
        face.Y = 0f;
        face = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
        return FormSwordsman.GuardCommand(SwordTraining.FormsWeapon(weapon), sense, face, _tuning.WalkSpeed, _tuning.MaxTorsoTwistRadians);
    }
}

/// <summary>
/// スティックの 1 つの軸を、倒した瞬間だけの −1 / +1 にする（倒しっぱなしで続けて変わらない）。<see cref="Press"/> を越えたら 1 回、<see cref="Release"/> より戻るまで次は無い。
/// </summary>
public sealed class StickFlick
{
    /// <summary>倒したとみなす量（スティックの値の絶対値）。</summary>
    public const float Press = 0.7f;

    /// <summary>戻したとみなす量。</summary>
    public const float Release = 0.3f;

    private bool _held;

    /// <summary>この刻みのスティックの値 <paramref name="value"/> から、倒した瞬間なら向き（−1・+1）、それ以外は 0。</summary>
    public int Step(float value)
    {
        var size = MathF.Abs(value);
        if (_held)
        {
            _held = size > Release;
            return 0;
        }

        if (size < Press)
        {
            return 0;
        }

        _held = true;
        return MathF.Sign(value);
    }
}
