using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Motion;

/// <summary>
/// バット（<see cref="MiniRing.Weapons.WeaponKind.Bat"/>、木の棍棒を両手で握る）の型。<see cref="SwordForms"/> と同じ作り方（キーを滑らかにつないだ
/// <see cref="SwordMotion"/>）で、両手の目標の経路と棒の向きを書く（CLAUDE.md §1.2: 姿勢を書き込まない。体がバネで追った結果が見た目になる）。
/// どの型も同じ構え（<see cref="Guard"/>）から始まって、構えへ戻る。技の選び方・狙い・守りは剣と同じ（<c>FormSwordsman</c> / <c>SwordsmanBot</c>）で、
/// バットのための分岐は無い（CLAUDE.md §1.3）。棍棒は刃が無い（<c>Weapon.IsBlunt</c>）ので、当たりの違いは材料から出る。
/// <para>
/// 寸法は腕の長さ = 1（人形で 0.188 m）。棒は 21 cm（腕の 1.1 倍）しかなく、上の手（右手）から頭の端まで 0.84・握りの端（グリップエンド）まで 0.18。
/// 剣よりずっと短いので、どの打ちも腕を伸ばして踏み込んで届かせる（<c>Step</c>）。右手の位置は肩の中心から測る。方位角は 0 が前・正が左、仰角は正が上。
/// </para>
/// 型の一覧（どれも構え → 打ち → 構え）:
/// <list type="bullet">
/// <item><c>swing-right</c>: 右から左への水平の振り（頭〜肩の高さ）。<c>swing-left</c>: 左から右への返しの水平（バックハンド）。
/// <c>swing-mid</c>: 右から胴の高さの水平。<c>swing-low</c>: 右から脚の高さへ低く。</item>
/// <item><c>chop</c>: 真上から振り下ろす。<c>chop-right</c> / <c>chop-left</c>: 右上・左上から斜めに振り下ろす。</item>
/// <item><c>scoop-right</c> / <c>scoop-left</c>: 右下・左下から斜めにすくい上げる（あご・手）。</item>
/// <item><c>poke-body</c> / <c>poke-face</c>: 頭の端での突き（胴・顔）。</item>
/// <item><c>snap</c>: 小さく速い手打ち（相手の手・前腕へ短く落とす）。</item>
/// <item><c>butt</c>: 握りの端（グリップエンド）で殴る（棒を逆さに返し、握りの端を前へ出す）。<c>front-kick</c>: 前蹴りで離す（構えのまま脚だけ出す）。</item>
/// </list>
/// </summary>
public static class BatForms
{
    /// <summary>両手の間隔（腕の長さ = 1。<see cref="MiniRing.Weapons.Weapon.HandSpacing"/> 0.028 m ÷ 腕の長さ 0.188 m）。</summary>
    public const float Spacing = 0.149f;

    /// <summary>構え: 両手を胸の前、棒を斜め上へ立てて頭の端を相手の顔のほうへ。</summary>
    public static readonly (Vector3 R, float Az, float El) Ready = (new Vector3(-0.25f, -0.15f, 0.6f), 0.15f, 0.8f);

    private static SwordKey Hold(float time, (Vector3 R, float Az, float El) g, float twist = 0f) => new(time, g.R, g.Az, g.El, Twist: twist);

    /// <summary>構えの 1 フレーム。</summary>
    public static MotionFrame Guard() => Forms()[0].Frames[0];

    /// <summary>型の一覧（上の説明の順）。</summary>
    public static IReadOnlyList<SwordMotion> Forms() => FormList.Value;

    private static readonly Lazy<IReadOnlyList<SwordMotion>> FormList = new(Make);

    private static IReadOnlyList<SwordMotion> Make() =>
    [
        Horizontal("swing-right", -1f, height: 0.15f, drop: 0f, forward: 0.95f),
        Horizontal("swing-left", 1f, height: 0.15f, drop: 0f, forward: 0.95f),
        Horizontal("swing-mid", -1f, height: -0.35f, drop: -0.3f, forward: 0.9f),
        Horizontal("swing-low", -1f, height: -0.65f, drop: -0.8f, forward: 0.7f),

        // 真上から: 棒を頭の上で立て、前へ振り下ろす（前の面の縦の弧）。
        FromKeys("chop", [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.75f, new(-0.3f, 0.6f, 0.4f), 0.1f, 1.45f, Twist: -0.1f),
            new(1.15f, new(-0.3f, -0.1f, 0.95f), 0f, -0.3f, Step: 1.8f, Lean: 0.12f, Twist: 0.15f),
            new(1.4f, new(-0.3f, -0.2f, 0.9f), 0f, -0.5f, Lean: 0.1f, Twist: 0.1f),
            Hold(2.3f, Ready)]),
        // 右上から斜めに（相手の左肩から右へ）。
        FromKeys("chop-right", [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.75f, new(-0.4f, 0.4f, 0.2f), -2.3f, 0.9f, Twist: -0.3f),
            new(1.15f, new(0.1f, -0.2f, 0.9f), 0.5f, -0.35f, Step: 1.8f, Lean: 0.12f, Twist: 0.35f),
            new(1.4f, new(0.2f, -0.35f, 0.8f), 0.8f, -0.6f, Lean: 0.1f, Twist: 0.3f),
            Hold(2.4f, Ready)]),
        // 左上から斜めに（相手の右肩から左へ）。
        FromKeys("chop-left", [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.75f, new(0.3f, 0.35f, 0.3f), 2.3f, 0.9f, Twist: 0.25f),
            new(1.15f, new(-0.35f, -0.3f, 0.9f), -0.6f, -0.4f, Step: 1.8f, Lean: 0.12f, Twist: -0.25f),
            new(1.4f, new(-0.45f, -0.45f, 0.8f), -0.9f, -0.6f, Lean: 0.1f, Twist: -0.3f),
            Hold(2.4f, Ready)]),

        // 下からのすくい上げ（右下・左下から、あご・手へ）。
        Scoop("scoop-right", -1f),
        Scoop("scoop-left", 1f),

        // 頭の端での突き。
        Poke("poke-body", height: -0.4f, forward: 1.05f),
        Poke("poke-face", height: 0.1f, forward: 1.0f),

        // 小さく速い手打ち: 棒の先を少し上げ、前にある手・前腕へ短く落とす。
        FromKeys("snap", [Hold(0f, Ready), Hold(0.15f, Ready),
            new(0.4f, new(-0.3f, 0.15f, 0.55f), 0.1f, 1.2f),
            new(0.65f, new(-0.25f, -0.3f, 0.95f), 0.2f, -0.1f, Step: 1.2f, Lean: 0.08f),
            new(0.85f, new(-0.25f, -0.4f, 0.9f), 0.2f, -0.3f, Lean: 0.06f),
            Hold(1.5f, Ready)]),

        // グリップエンド: 棒を頭の上から後ろへ返して逆さにし、右手を前へ突き出して握りの端で殴る。
        FromKeys("butt", [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.6f, new(-0.35f, -0.1f, 0.35f), -MathF.PI, 0.15f, Twist: -0.2f),
            new(0.9f, new(-0.2f, 0f, 1.0f), -MathF.PI, 0.1f, Step: 1.8f, Lean: 0.1f, Twist: 0.25f),
            new(1.1f, new(-0.2f, 0f, 1.0f), -MathF.PI, 0.1f, Lean: 0.1f, Twist: 0.25f),
            Hold(2.0f, Ready)]) with { StrikeUntil = 1.1f },

        // 前蹴り: 構えの両手を保ち、空手と同じ前蹴りを出して戻す。押す力は既存の脚の接触から。
        FrontKick("front-kick"),
    ];

    private static SwordMotion FromKeys(string name, IReadOnlyList<SwordKey> keys) => SwordForms.FromKeys(name, keys, Spacing);

    /// <summary>
    /// 水平の振り: 構えから、棒を横へ引いて（手を引く側・棒を横へ向ける）、反対の側へ払って、構えへ戻す。<paramref name="side"/> は −1 で右から・+1 で左から
    /// （+X は自分の左）。<paramref name="drop"/> は棒の先の高さ（仰角 [rad]。負で下げる）。
    /// </summary>
    private static SwordMotion Horizontal(string name, float side, float height, float drop, float forward) =>
        FromKeys(name, [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.6f, new(side * 0.5f, height, 0.5f), side * 1.3f, drop, Twist: side * 0.3f),
            new(1.1f, new(-side * 0.2f, height, forward), -side * 1.15f, drop, Step: 2.0f, Lean: 0.08f, Twist: -side * 0.3f),
            new(1.3f, new(-side * 0.35f, height, forward - 0.1f), -side * 1.5f, drop, Lean: 0.06f, Twist: -side * 0.3f),
            Hold(2.3f, Ready)]);

    private static SwordMotion Scoop(string name, float side) =>
        FromKeys(name, [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.6f, new(side * 0.45f, -0.55f, 0.25f), side * 1.9f, -0.7f, Twist: side * 0.3f),
            new(1.1f, new(-side * 0.1f, 0.15f, 0.85f), -side * 0.7f, 0.45f, Step: 1.8f, Lean: 0.08f, Twist: -side * 0.3f),
            new(1.35f, new(-side * 0.2f, 0.25f, 0.8f), -side * 0.5f, 0.8f, Lean: 0.06f, Twist: -side * 0.3f),
            Hold(2.3f, Ready)]);

    private static SwordMotion Poke(string name, float height, float forward) =>
        FromKeys(name, [Hold(0f, Ready), Hold(0.2f, Ready),
            new(0.5f, new(-0.15f, height, forward), 0f, 0.05f, Step: 1.8f, Lean: 0.1f),
            new(0.7f, new(-0.15f, height, forward), 0f, 0.05f), Hold(1.5f, Ready)]);

    private static SwordMotion FrontKick(string name)
    {
        var kick = KickSpec.Of(KickTechnique.Front);
        return FromKeys(name, [Hold(0f, Ready), Hold(kick.Duration * SwordStance.Rate, Ready)]) with { Kick = kick };
    }
}
