using System.Numerics;

namespace MiniRing.Weapons;

/// <summary>間合い（胸と胸の水平の距離）の 3 つの区分。</summary>
public enum Maai
{
    /// <summary>近間: 拳も届く近さ。</summary>
    Close,

    /// <summary>中間: 剣の物打ち（先から 1/3）が相手の頭に届く距離。</summary>
    Mid,

    /// <summary>遠間: 踏み込んで、いちばん近い所（構えた前腕）か、剣先の突きだけが届く距離。</summary>
    Far,
}

/// <summary>
/// 剣の技（片手、右手）: 名前・狙う部位・間合い（振り始める胸と胸の距離）と、振り（<see cref="WeaponSwing"/>、型は
/// <see cref="SwingFamily.Sword"/>）。<see cref="Basics"/> が基本の形（手で決めた素直な形）で、ドリル（<see cref="SwordDrill"/>）で磨く。
/// </summary>
/// <param name="Name">技の名前（英字。保存と表示の鍵。日本語は <see cref="Title"/>、由来は <see cref="Origin"/>）。</param>
/// <param name="Target">狙う部位。</param>
/// <param name="Maai">間合いの区分。</param>
/// <param name="Range">振り始める間合い [m]（胸と胸の水平の距離）。</param>
/// <param name="Swing">振り。</param>
public sealed record SwordTechnique(string Name, BodyPartKind Target, Maai Maai, float Range, WeaponSwing Swing)
{
    /// <summary>技を出す間合いの幅 [m]: 間合いが <see cref="Range"/> ± これに入ったら振る。</summary>
    public const float Window = 0.03f;

    /// <summary>ドリルで間合いを動かしてよい幅 [m]（基本の間合い ± これ。技がほかの間合いの技にならないように）。</summary>
    public const float RangeLeeway = 0.05f;

    /// <summary>間合い <paramref name="distance"/> で出せる技か。</summary>
    public bool Fits(float distance) => MathF.Abs(distance - Range) <= Window;

    /// <summary>日本語の名前。</summary>
    public string Title => Name switch
    {
        "kote" => "飛び込み小手",
        "katate-tsuki" => "片手突き",
        "men" => "面",
        "kesa" => "袈裟斬り",
        "do" => "胴",
        "hiki-kote" => "引き小手",
        "hiki-do" => "引き胴",
        "sune" => "脛斬り",
        _ => Name,
    };

    /// <summary>どの武術の、どんな技か。</summary>
    public string Origin => Name switch
    {
        "kote" => "剣道の仕掛け技。遠間から剣先を小さく上げ、踏み込みながら相手の構えた右手首（小手）を打つ。いちばん近い所を打つので遠くから届く",
        "katate-tsuki" => "剣道の片手突き。中段から右手一本で腕を伸ばし切り、踏み込んで喉を突く。片手のぶん両手より遠くへ届く",
        "men" => "剣道の面。一足一刀の間合いから振りかぶり、踏み込んで頭の真上へ打ち下ろす",
        "kesa" => "剣術・居合の袈裟斬り。右肩の上へ振りかぶり、相手の左肩から右脇腹へ斜めに斬り下ろす",
        "do" => "剣道の胴。面のように振りかぶり、手首を返して相手の右の脇腹を斜め下へ斬り抜く",
        "hiki-kote" => "剣道の引き技。鍔迫り合いの近さから、下がりながら相手の小手を打つ",
        "hiki-do" => "剣道の引き技。鍔迫り合いの近さから、振りかぶって下がりながら相手の胴を斬る",
        "sune" => "なぎなた・柳生新陰流の脛（すね）。構えた相手の注意の外の、前の脚の脛を低く払う",
        _ => string.Empty,
    };

    /// <summary>間合いの区分の日本語。</summary>
    public static string MaaiName(Maai maai) => maai switch
    {
        Maai.Close => "近間",
        Maai.Mid => "中間",
        Maai.Far => "遠間",
        _ => maai.ToString(),
    };

    private static WeaponSwing Sword(Vector3 windupFist, float windupAz, float windupEl, Vector3 strikeFist, float strikeAz, float strikeEl,
        float windup, float strike, float step, float roll = 0f) =>
        new(SwingFamily.Sword, windupFist, windupAz, windupEl, strikeFist, strikeAz, strikeEl, windup, strike, 0.35f,
            WristDelay: 0f, Twist: 0f, Step: step, Ease: 1f, Roll: roll);

    /// <summary>
    /// 基本の 8 つの技: 剣道（仕掛け技・引き技）・剣術と居合・なぎなたから、片手の剣で出せるものを間合いごとに選んだ。どれも中段の構え
    /// （<see cref="WeaponSwing.SwordGuard"/>）から、拳を等速で、手首のタメも胴のひねりも無しに振る、その技の素直な形。
    /// </summary>
    public static IReadOnlyList<SwordTechnique> Basics { get; } =
    [
        // ── 遠間（踏み込んで届かせる）
        new("kote", BodyPartKind.Forearm, Maai.Far, 0.50f,
            Sword(new Vector3(-0.08f, 0.25f, 0.45f), 0f, 1.3f, new Vector3(-0.05f, 0.0f, 0.85f), 0.05f, -0.15f, 0.18f, 0.12f, step: 0.8f)),
        new("katate-tsuki", BodyPartKind.Chest, Maai.Far, 0.52f,
            Sword(new Vector3(-0.1f, 0.0f, 0.3f), 0.05f, 0.1f, new Vector3(-0.02f, 0.2f, 1.05f), 0f, 0.05f, 0.2f, 0.15f, step: 1f, roll: MathF.PI / 2f)),

        // ── 中間（一足一刀）
        new("men", BodyPartKind.Head, Maai.Mid, 0.42f,
            Sword(new Vector3(-0.05f, 0.65f, 0.2f), 0f, 1.9f, new Vector3(-0.05f, 0.2f, 0.75f), 0f, -0.1f, 0.3f, 0.15f, step: 0.5f)),
        new("kesa", BodyPartKind.Chest, Maai.Mid, 0.40f,
            Sword(new Vector3(-0.4f, 0.6f, 0.15f), -0.9f, 1.5f, new Vector3(0.2f, 0.0f, 0.65f), 0.5f, -0.5f, 0.3f, 0.15f, step: 0.3f)),
        new("do", BodyPartKind.Pelvis, Maai.Mid, 0.40f,
            Sword(new Vector3(-0.05f, 0.6f, 0.2f), 0f, 1.9f, new Vector3(0.25f, -0.05f, 0.6f), 1.0f, -0.3f, 0.3f, 0.15f, step: 0.5f, roll: MathF.PI / 2f)),

        // ── 近間（鍔迫り合いの近さ。引き技は下がりながら）
        new("hiki-kote", BodyPartKind.Forearm, Maai.Close, 0.29f,
            Sword(new Vector3(-0.1f, 0.3f, 0.3f), 0f, 1.3f, new Vector3(-0.05f, 0.05f, 0.55f), 0.05f, -0.2f, 0.2f, 0.12f, step: -0.8f)),
        new("hiki-do", BodyPartKind.Pelvis, Maai.Close, 0.29f,
            Sword(new Vector3(-0.05f, 0.35f, 0.25f), 0f, 1.2f, new Vector3(0.2f, -0.05f, 0.45f), 0.9f, -0.3f, 0.25f, 0.15f, step: -0.8f, roll: MathF.PI / 2f)),
        new("sune", BodyPartKind.Shin, Maai.Close, 0.31f,
            Sword(new Vector3(-0.35f, 0.2f, 0.2f), -0.8f, 0.6f, new Vector3(0.05f, -0.6f, 0.55f), 0.4f, -1.1f, 0.25f, 0.15f, step: 0.2f)),
    ];
}
