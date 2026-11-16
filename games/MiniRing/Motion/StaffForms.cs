using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 棒術（六尺棒）の型。<see cref="SwordForms"/> と同じ作り方（キーを滑らかにつないだ <see cref="SwordMotion"/>）で、棒の両手の動きを
/// 目標の経路として書く（CLAUDE.md §1.2: 姿勢を書き込まない。体がバネで追った結果が見た目になる）。長さは腕の長さ = 1（人形で 0.188 m）。
/// <para>
/// 棒は真ん中を肩幅（0.11 m）ほど離した両手で握る。右手（上の手）が +X の側、左手が −X の側で、左手は「右手 − 棒の向き × <see cref="Spacing"/>」の
/// 位置（<see cref="SwordForms.FromKeys"/>）。棒は対称で、右手から +X の端まで 1.36、−X の端まで 1.94（腕の長さ）。向き（Blade）は右手の側の端を
/// 指すので、前の端で打つ技は Blade が前、後ろの端で打つ技は Blade が後ろ（方位角が π 付近）を向く。どちらの端で打つかは、技の中で棒の向きが
/// どう回るかだけで決まり、判断の側に「突きなら前の端」のような場合分けは無い（CLAUDE.md §1.3）。
/// </para>
/// <para>
/// 長い棒の後ろの端は、右手より 1.9 腕の長さも後ろにあり、棒の線が体の中を通ると自分に当たる（自傷）。構えと技は、棒の線が体の右側を通るよう、
/// 手を体の右に置いて棒を少し左（方位角 +）へ向ける。
/// </para>
/// </summary>
public static class StaffForms
{
    /// <summary>両手の間隔（腕の長さ = 1。<see cref="MiniRing.Weapons.Weapon.StaffHandSpacing"/> 0.11 m ÷ 腕の長さ 0.188 m）。</summary>
    public const float Spacing = 0.585f;

    /// <summary>構え（中段）: 右手を体の右の腰の前、前の端を相手の顔へ（棒の線は体の右側を通り、後ろの端は右の腰の後ろへ抜ける）。</summary>
    public static readonly (Vector3 R, float Az, float El) Chudan = (new Vector3(-0.3f, 0.0f, 0.85f), 0.4f, 0.1f);

    private static SwordKey Hold(float time, (Vector3 R, float Az, float El) g, float twist = 0f) => new(time, g.R, g.Az, g.El, Twist: twist);

    /// <summary>構えの 1 フレーム。</summary>
    public static MotionFrame Guard() => Forms()[0].Frames[0];

    /// <summary>
    /// 技の一覧（どれも中段から始まり中段へ戻る）:
    /// <list type="bullet">
    /// <item>tsuki（突き）: 引いてから、踏み込んで前の端でまっすぐ顔へ突く。</item>
    /// <item>uchi-oroshi（打ち下ろし）: 棒を右肩の横で立て、前の端を真上から打ち下ろす（後ろの端は下から前へ回って後ろへ抜ける）。</item>
    /// <item>yoko-uchi（横打ち）: 右へ引いた前の端を、左へ払って頭を打つ。</item>
    /// <item>ashi-barai（足払い）: 低く構え、前の端で足を横に払う。</item>
    /// <item>gaeshi（返し）: 横打ちの前の端が打ち終わった勢いで棒を返し、続けて後ろの端で逆から打つ。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<SwordMotion> Forms() => FormList.Value;

    private static readonly Lazy<IReadOnlyList<SwordMotion>> FormList = new(Make);

    private static IReadOnlyList<SwordMotion> Make() =>
    [
        SwordForms.FromKeys("tsuki", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.5f, new Vector3(-0.35f, 0.00f, 0.61f), 0.35f, 0.15f, Twist: -0.1f),
            new(0.72f, new Vector3(-0.26f, 0.09f, 0.90f), 0.25f, 0.10f, Step: 1.8f, Lean: 0.15f, Twist: 0.2f),
            new(0.92f, new Vector3(-0.26f, 0.08f, 0.90f), 0.25f, 0.10f, Lean: 0.12f, Twist: 0.2f),
            Hold(1.5f, Chudan),
        ], Spacing),
        SwordForms.FromKeys("uchi-oroshi", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.8f, new Vector3(-0.47f, 0.50f, 0.40f), 0.36f, 1.42f, Twist: -0.2f),
            new(1.3f, new Vector3(-0.30f, 0.00f, 0.90f), 0.30f, -0.15f, Step: 1.6f, Lean: 0.12f, Twist: 0.2f),
            new(1.55f, new Vector3(-0.30f, -0.20f, 0.85f), 0.30f, -0.30f, Lean: 0.1f, Twist: 0.15f),
            Hold(2.3f, Chudan),
        ], Spacing),
        SwordForms.FromKeys("yoko-uchi", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.6f, new Vector3(-0.75f, 0.13f, 0.28f), -0.63f, 0.14f, Twist: -0.35f),
            new(0.88f, new Vector3(-0.11f, 0.11f, 0.78f), 0.81f, 0.12f, Step: 1.4f, Lean: 0.08f, Twist: 0.35f),
            new(1.1f, new Vector3(-0.01f, 0.07f, 0.69f), 1.07f, 0.14f, Twist: 0.3f),
            Hold(1.8f, Chudan),
        ], Spacing),
        SwordForms.FromKeys("ashi-barai", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.6f, new Vector3(-0.75f, -0.60f, 0.37f), -0.52f, -0.42f, Twist: -0.3f, Lean: 0.1f),
            new(0.9f, new Vector3(-0.27f, -0.60f, 0.71f), 0.46f, -0.53f, Step: 1.2f, Lean: 0.2f, Twist: 0.3f),
            new(1.1f, new Vector3(-0.16f, -0.57f, 0.54f), 0.83f, -0.48f, Lean: 0.15f, Twist: 0.3f),
            Hold(1.8f, Chudan),
        ], Spacing),
        SwordForms.FromKeys("gaeshi", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            // 前の端で打ち下ろし、そのまま棒を縦に回し続けて（右の脇の縦の面）、後ろの端が頭の上から前へ打ち下ろす。
            // 打ち終えたら棒を縦に立てて（ほぼ真上）、前後を持ち直さずに向きを戻す。
            new(0.7f, new Vector3(-0.49f, 0.49f, 0.40f), 0.39f, 1.45f, Twist: -0.2f),
            new(0.95f, new Vector3(-0.27f, -0.11f, 0.90f), 0.32f, -0.15f, Step: 1.4f, Lean: 0.1f, Twist: 0.2f),
            new(1.2f, new Vector3(-0.48f, -0.22f, 0.45f), 0.28f, -1.15f, Twist: 0.1f),
            new(1.4f, new Vector3(-0.50f, -0.12f, 0.06f), 3.50f, -0.59f),
            new(1.62f, new Vector3(-0.36f, 0.15f, 0.31f), 3.64f, 0.18f, Step: 1.0f, Lean: 0.1f, Twist: -0.1f),
            new(1.85f, new Vector3(-0.40f, 0.05f, 0.24f), 3.63f, 0.44f, Lean: 0.1f),
            new(2.05f, new Vector3(-0.45f, 0.38f, 0.41f), 3.43f, 1.27f),
            new(2.25f, new Vector3(-0.46f, 0.40f, 0.49f), 0.45f, 1.27f),
            Hold(2.9f, Chudan),
        ], Spacing),
    ];
}
