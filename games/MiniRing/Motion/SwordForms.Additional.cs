using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Motion;

public static partial class SwordForms
{
    // 寸法は腕の長さ = 1、時間は実寸の人の秒。刃を体の外へ置き、戻しを打ちより遅くして追従の遅れを減らす。
    private static IReadOnlyList<SwordMotion> KatanaAdditional() =>
    [
        // 真っ向: 中段から頭上へ上げ、中心線に沿って頭頂へ斬り下ろし、中段へ。
        FromKeys("shomen", [Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.65f, new(-0.2f, 0.6f, 0.55f), 0f, 1.5f),
            new(1.05f, new(-0.2f, -0.05f, 0.9f), 0f, -0.1f, Step: 1.2f, Lean: 0.08f),
            Hold(1.9f, Chudan)], KatanaSpacing),
        // 右薙ぎ・左薙ぎ: 中段から胴の高さで水平に横切り、中段へ。+X は自分の左。
        Horizontal("migi-nagi", Chudan, KatanaSpacing, -1f, -0.5f),
        Horizontal("hidari-nagi", Chudan, KatanaSpacing, 1f, -0.5f),
        // 左斬り上げ: 中段から左下を通り、右肩へ斜めに上げて中段へ。
        Rising("gyaku-kiriage", Chudan, KatanaSpacing, 1f),
        // 逆風: 中段から前の低い所へ剣を下げ、真下から顔へ上げて中段へ。手元は脚より前。
        FromKeys("gyakufu", [Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.6f, new(-0.2f, -0.65f, 0.95f), 0f, -0.8f),
            new(1f, new(-0.2f, 0.1f, 0.95f), 0f, 0.8f, Step: 1.1f),
            Hold(1.9f, Chudan)], KatanaSpacing),
        // 小手: 中段から小さく剣先を上げ、前にある手首へ短く落とし、中段へ。
        FromKeys("kote", [Hold(0f, Chudan), Hold(0.15f, Chudan),
            new(0.4f, new(-0.2f, -0.2f, 0.75f), 0f, 0.65f),
            new(0.65f, new(-0.2f, -0.4f, 0.95f), 0f, -0.1f, Step: 0.8f),
            Hold(1.35f, Chudan)], KatanaSpacing),
        // 小手面: 小手を落とした後、同じ型の中で頭上へ返して面へ打ち、中段へ。
        FromKeys("kote-men", [Hold(0f, Chudan), Hold(0.15f, Chudan),
            new(0.4f, new(-0.2f, -0.2f, 0.75f), 0f, 0.65f),
            new(0.65f, new(-0.2f, -0.4f, 0.95f), 0f, -0.1f, Step: 0.5f),
            new(1.1f, new(-0.2f, 0.55f, 0.55f), 0f, 1.45f),
            new(1.5f, new(-0.2f, 0f, 0.95f), 0f, -0.05f, Step: 1f),
            Hold(2.4f, Chudan)], KatanaSpacing),
        // 胴突き: 中段からみぞおちへ水平に突き、中段へ。顔突きとは通り道の高さだけが違う。
        Thrust("do-tsuki", Chudan, KatanaSpacing, -0.5f, 1.1f),
        // 片手突きの代用: 剣の添え手は放すと再把持できないため両手を保ち、長く踏み込んで遠くを突く。
        Thrust("katate-tsuki", Chudan, KatanaSpacing, 0.1f, 1.3f),
        // 柄頭打ち: 中段から右脇で剣先を後ろへ向け、柄頭を顔・みぞおちへ短く出し、中段へ。
        Pommel("tsuka-ate", Chudan, KatanaSpacing),
        // 押し斬り: 中段から前の刃を胴に沿わせ、短く体ごと押して中段へ。
        Slice("oshi-giri", Chudan, KatanaSpacing, 0.6f),
        // 引き胴: 中段から胴へ刃を横たえ、下がりながら引き斬って中段へ。
        Slice("hiki-do", Chudan, KatanaSpacing, -1f),
        // 前蹴り: 中段の両手を保ち、空手と同じ前蹴りを出して戻す。押す力は既存の脚の接触から。
        FrontKick("mae-geri", Chudan, KatanaSpacing),
    ];

    private static IReadOnlyList<SwordMotion> LongswordAdditional() =>
    [
        // 頭頂打ち: 屋根から頭上を通り、垂直に頭頂を打って屋根へ戻る。
        FromKeys("scheitelhau", [Hold(0f, Tag), Hold(0.3f, Tag),
            new(0.75f, new(-0.25f, 0.65f, 0.5f), 0f, 1.5f),
            new(1.15f, new(-0.25f, 0.05f, 0.9f), 0f, -0.05f, Step: 1.2f),
            Hold(2.2f, Tag)], LongswordSpacing),
        // 睨み打ち: 屋根から相手の剣の上を斜めに越え、肩へ落として屋根へ。
        FromKeys("schielhau", [Hold(0f, Tag), Hold(0.3f, Tag),
            new(0.75f, new(-0.45f, 0.5f, 0.5f), -0.7f, 1.1f, Twist: -0.2f),
            new(1.15f, new(-0.1f, 0f, 0.9f), 0.4f, -0.1f, Step: 1f, Twist: 0.2f),
            Hold(2.2f, Tag)], LongswordSpacing),
        // 曲がった打ち: 雄牛から前で交差させ、手元を体の外に保って相手の手へ小さく斬り、雄牛へ。
        FromKeys("krumphau", [Hold(0f, Ochs), Hold(0.25f, Ochs),
            new(0.65f, new(-0.5f, 0.15f, 0.65f), -0.85f, 0.5f),
            new(1f, new(-0.3f, -0.35f, 0.9f), 0.65f, -0.15f, Step: 0.7f),
            Hold(2f, Ochs)], LongswordSpacing),
        // 左横打ち: 左の屋根から頭の高さを横切り、左の屋根へ戻る。
        Horizontal("zwerchhau-left", (new(0.4f, 0.4f, 0.45f), 1.3f, 0.35f), LongswordSpacing, 1f, 0.25f),
        // 左下からの打ち: 鋤から左下を通り、右肩へ斬り上げて鋤へ戻る。
        Rising("unterhau-left", Pflug, LongswordSpacing, 1f),
        // 低い突き: 鋤からみぞおちへ突き、鋤へ戻る。
        Thrust("stich-low", Pflug, LongswordSpacing, -0.5f, 1.1f),
        // 柄頭打ち（殺しの打ちの代用）: 刃の再把持が無いため握りを保ち、屋根から柄頭を出して屋根へ。
        Pommel("knaufschlag", Tag, LongswordSpacing),
        // 切り: 鋤から刃を胴へ置き、引きながら斬って鋤へ戻る。
        Slice("schnitt", Pflug, LongswordSpacing, -0.8f),
        // 蹴り: 屋根の両手を保ったまま前蹴りを出し、足を戻す。
        FrontKick("tritt", Tag, LongswordSpacing),
    ];

    /// <summary>ドリル用: 日本刀の水平の薙ぎを、手の高さ <paramref name="height"/>（腕の長さ = 1、胸の中心から）で。首・肩の高さを狙って測る（necklab）。</summary>
    public static SwordMotion KatanaHorizontalAt(string name, float side, float height) => Horizontal(name, Chudan, KatanaSpacing, side, height);

    private static SwordMotion Horizontal(string name, (Vector3 R, float Az, float El) guard, float spacing, float side, float height) =>
        FromKeys(name, [Hold(0f, guard), Hold(0.2f, guard),
            new(0.6f, new(side * 0.45f, height, 0.8f), side * 1.25f, 0f, Twist: side * 0.25f),
            new(1.1f, new(-side * 0.25f, height, 0.85f), -side * 1.1f, 0f, Step: 1f, Twist: -side * 0.25f),
            Hold(2.2f, guard)], spacing);

    private static SwordMotion Rising(string name, (Vector3 R, float Az, float El) guard, float spacing, float side) =>
        FromKeys(name, [Hold(0f, guard), Hold(0.2f, guard),
            new(0.6f, new(side * 0.5f, -0.65f, 0.85f), side * 1.1f, -0.65f, Twist: side * 0.2f),
            new(1.1f, new(-side * 0.2f, 0.25f, 0.85f), -side * 0.6f, 0.8f, Step: 1f, Twist: -side * 0.2f),
            Hold(2.2f, guard)], spacing);

    private static SwordMotion Thrust(string name, (Vector3 R, float Az, float El) guard, float spacing, float height, float forward) =>
        FromKeys(name, [Hold(0f, guard), Hold(0.2f, guard),
            new(0.45f, new(-0.15f, height, forward), 0f, 0f, Step: 1.6f, Lean: 0.1f),
            new(0.65f, new(-0.15f, height, forward), 0f, 0f), Hold(1.5f, guard)], spacing);

    private static SwordMotion Pommel(string name, (Vector3 R, float Az, float El) guard, float spacing) =>
        FromKeys(name, [Hold(0f, guard), Hold(0.2f, guard),
            new(1f, new(-0.38f, -0.1f, 0.08f), -MathF.PI, 0.1f),
            new(1.25f, new(-0.38f, -0.1f, 0.28f), -MathF.PI, 0.1f, Step: 0.5f),
            new(1.6f, new(-0.38f, -0.1f, 0.18f), -MathF.PI, 0.1f),
            new(2.3f, new(-0.38f, 0.65f, 0.4f), -MathF.PI, 1.5f),
            new(3.3f, new(-0.3f, 0.6f, 0.7f), 0f, 1.45f), Hold(4.3f, guard)], spacing) with { StrikeUntil = 1.6f };

    private static SwordMotion Slice(string name, (Vector3 R, float Az, float El) guard, float spacing, float step) =>
        FromKeys(name, [Hold(0f, guard), Hold(0.2f, guard),
            new(0.65f, new(-0.3f, -0.45f, 0.55f), -1.2f, 0f),
            new(1.2f, new(-0.1f, -0.45f, 0.75f), -1.25f, 0f, Step: step, Twist: 0.2f),
            Hold(2.1f, guard)], spacing) with { StrikeUntil = 1.2f };

    private static SwordMotion FrontKick(string name, (Vector3 R, float Az, float El) guard, float spacing)
    {
        var kick = KickSpec.Of(KickTechnique.Front);
        // 蹴る間、剣は蹴り足の通り道（体の前）から外へ上げる（Tag: 右肩の上で立てる）。構えのままだと剣が蹴り足を斬る（自傷）。両手の目標の通り道だけで、足の動きは KickSpec のまま。
        var d = kick.Duration * SwordStance.Rate;
        return FromKeys(name, [Hold(0f, guard), Hold(0.25f * d, Tag), Hold(0.6f * d, Tag), Hold(0.85f * d, guard), Hold(d, guard)], spacing) with { Kick = kick };
    }
}
