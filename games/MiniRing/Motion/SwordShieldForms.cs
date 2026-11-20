using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 片手剣と盾の型。<see cref="SwordForms"/> と同じ作り方（キーを滑らかにつないだ <see cref="SwordMotion"/>）で、右手の剣の経路と、左手（盾をはめた手）の経路を
/// 目標として書く（CLAUDE.md §1.2: 姿勢を書き込まない。体がバネで追った結果が見た目になる）。長さは腕の長さ = 1（人の大きさの体で 0.53 m）。
/// <para>
/// 剣は片手（<see cref="MiniRing.Weapons.WeaponKind.IronSword"/>: 握りの真ん中を握る。握りから切っ先まで 1.58 腕の長さ）。両手の間隔は無く、左手は盾の位置として
/// キーに書く（<see cref="SwordKey.Left"/>）。盾は拳にはめた丸い板で、表（椀の側）は <see cref="ShieldFacing"/> の向き。
/// 型の中でも盾は相手と体の間にあり、剣が振られて自分の盾を斬らないよう、剣の線は盾の右を通す。
/// </para>
/// <para>
/// 左手は盾を持たない（グローブ）ときも同じ経路を描く。盾が無ければ、左の拳が顔の前の同じ所に構えるだけ（盾の大きさは守りの式に見える寸法として入る）。
/// </para>
/// </summary>
public static class SwordShieldForms
{
    /// <summary>構えの右手（剣）: 体の右の前、剣は少し左へ向けて上へ（切っ先は相手の顔の上）。</summary>
    public static readonly (Vector3 R, float Az, float El) Chudan = (new Vector3(-0.30f, 0.05f, 0.75f), 0.25f, 0.55f);

    /// <summary>構えの左手（盾）: 体の左の前、肩の高さ。盾の面が相手の胸・顔を覆う。</summary>
    public static readonly Vector3 ShieldGuard = new(0.32f, 0.05f, 0.75f);

    /// <summary>盾の表が向く向き（体フレーム。前、わずかに上）。</summary>
    public static readonly Vector3 ShieldFacing = Vector3.Normalize(new Vector3(0f, 0.1f, 1f));

    private static SwordKey Hold(float time, float twist = 0f) => new(time, Chudan.R, Chudan.Az, Chudan.El, Twist: twist, Left: ShieldGuard);

    /// <summary>構えの 1 フレーム。</summary>
    public static MotionFrame Guard() => Forms()[0].Frames[0];

    /// <summary>
    /// 技の一覧（どれも構えから始まり構えへ戻る）:
    /// <list type="bullet">
    /// <item>tsuki（突き）: 剣を引いてから、踏み込んで腕を伸ばし、切っ先をまっすぐ顔へ突く。</item>
    /// <item>kiri-oroshi（斬り下ろし）: 剣を右肩の上へ立て、前へ踏み込んで真上から斬り下ろす。</item>
    /// <item>yoko-barai（横の払い）: 右へ引いた剣を、左へ水平に払う（盾は剣の通り道から後ろへ引く）。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<SwordMotion> Forms() => FormList.Value;

    private static readonly Lazy<IReadOnlyList<SwordMotion>> FormList = new(Make);

    private static IReadOnlyList<SwordMotion> Make() =>
        [.. new[]
        {
            SwordForms.FromKeys("tsuki", [
                Hold(0f), Hold(0.2f),
                new(0.5f, new Vector3(-0.35f, -0.05f, 0.50f), 0.20f, 0.30f, Twist: -0.2f, Left: ShieldGuard + new Vector3(0f, 0f, 0.05f)),
                new(0.72f, new Vector3(-0.22f, 0.00f, 0.95f), 0.15f, 0.15f, Step: 1.8f, Lean: 0.15f, Twist: 0.2f, Left: ShieldGuard + new Vector3(0f, 0f, 0.1f)),
                new(0.92f, new Vector3(-0.22f, 0.00f, 0.95f), 0.15f, 0.15f, Lean: 0.12f, Twist: 0.2f, Left: ShieldGuard + new Vector3(0f, 0f, 0.1f)),
                Hold(1.5f),
            ], 0f),
            SwordForms.FromKeys("kiri-oroshi", [
                Hold(0f), Hold(0.2f),
                new(0.8f, new Vector3(-0.40f, 0.60f, 0.35f), 0.30f, 1.35f, Twist: -0.2f, Left: ShieldGuard),
                new(1.3f, new Vector3(-0.25f, 0.05f, 0.90f), 0.20f, -0.35f, Step: 1.6f, Lean: 0.12f, Twist: 0.2f, Left: ShieldGuard + new Vector3(0f, 0f, 0.1f)),
                new(1.55f, new Vector3(-0.25f, -0.25f, 0.85f), 0.20f, -0.60f, Lean: 0.1f, Twist: 0.15f, Left: ShieldGuard + new Vector3(0f, 0f, 0.1f)),
                Hold(2.3f),
            ], 0f),
            SwordForms.FromKeys("yoko-barai", [
                Hold(0f), Hold(0.2f),
                new(0.6f, new Vector3(-0.70f, 0.10f, 0.35f), -0.50f, 0.15f, Twist: -0.35f, Left: ShieldGuard + new Vector3(0.1f, 0f, -0.1f)),
                new(0.88f, new Vector3(-0.25f, 0.15f, 0.85f), 0.65f, 0.10f, Step: 1.4f, Lean: 0.08f, Twist: 0.35f, Left: ShieldGuard + new Vector3(0.15f, 0f, -0.15f)),
                new(1.1f, new Vector3(-0.12f, 0.10f, 0.80f), 0.95f, 0.10f, Twist: 0.3f, Left: ShieldGuard + new Vector3(0.15f, 0f, -0.15f)),
                Hold(1.8f),
            ], 0f),
        }.Select(m => m with { Frames = [.. m.Frames.Select(f => f with { LeftFacing = ShieldFacing })] })];
}
