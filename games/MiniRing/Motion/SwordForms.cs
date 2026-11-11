using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 型のキー 1 つ（体フレーム、長さは腕の長さ = 1）: 時刻、右手（上の手）の位置、剣の向き（方位角: 0 = 前・正 = 左、仰角: 正 = 上）、
/// 腰の前への速さ（腕の長さ / s、踏み込みは正・下がるは負）、前傾（頭の前へのずれ）、胴のひねり [rad]（正で右肩が前）。
/// </summary>
public readonly record struct SwordKey(float Time, Vector3 Right, float Azimuth, float Elevation, float Step = 0f, float Lean = 0f, float Twist = 0f);

/// <summary>
/// 手で作る型（キーを滑らかにつないだ <see cref="SwordMotion"/>）。ロングソードの型は、リヒテナウアーの流れ（ドイツ式）の 4 つの構え
/// （Vom Tag・Ochs・Pflug・Alber）と、5 つの打ち（Oberhau・Zornhau・Zwerchhau・Unterhau・Stich）を、右利き・両手持ちで。
/// 時間は実寸の人の速さ。左手は剣の握りに沿って右手の下（柄頭の側）。刃筋は剣の先の動きの面へ合わせる（<see cref="SwordMotion.FollowCut"/>）。
/// </summary>
public static class SwordForms
{
    /// <summary>ロングソードの両手の間隔（腕の長さ = 1。実寸およそ 17 cm / 腕 53 cm）。</summary>
    public const float LongswordSpacing = 0.32f;

    /// <summary>剣の先を測る長さ（腕の長さ = 1。刀身 90 cm）。</summary>
    private const float Blade = 1.7f;

    /// <summary>キーの列を、<paramref name="rate"/> フレーム / s の動きにする（キーの間は滑らかに、両端は止まる）。</summary>
    public static SwordMotion FromKeys(string name, IReadOnlyList<SwordKey> keys, float spacing, float rate = 60f)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count < 2 || keys.Zip(keys.Skip(1)).Any(p => !(p.Second.Time > p.First.Time)))
        {
            throw new ArgumentException("キーは 2 つ以上、時刻の順に。", nameof(keys));
        }

        var dt = 1f / rate;
        var n = (int)MathF.Floor((keys[^1].Time - keys[0].Time) / dt) + 1;
        var rights = new Vector3[n];
        var blades = new Vector3[n];
        var heads = new Vector3[n];
        var twists = new float[n];
        var steps = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = keys[0].Time + (i * dt);
            var k = 0;
            while (k < keys.Count - 2 && t > keys[k + 1].Time)
            {
                k++;
            }

            var (a, b) = (keys[k], keys[k + 1]);
            var u = Math.Clamp((t - a.Time) / (b.Time - a.Time), 0f, 1f);
            var s = u * u * (3f - (2f * u));
            rights[i] = Vector3.Lerp(a.Right, b.Right, s);
            // 剣の向きは球の上の最短の道で（方位角と仰角を別々に補うと、後ろ上から前への斬り下ろしが横へ回ってしまう）。
            blades[i] = Slerp(Direction(a.Azimuth, a.Elevation), Direction(b.Azimuth, b.Elevation), s);
            heads[i] = new Vector3(0f, 0f, Lerp(a.Lean, b.Lean, s));
            twists[i] = Lerp(a.Twist, b.Twist, s);
            steps[i] = Lerp(a.Step, b.Step, s);
        }

        var tips = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var (p, q) = (Math.Max(0, i - 1), Math.Min(n - 1, i + 1));
            var tp = rights[p] + (blades[p] * Blade);
            var tq = rights[q] + (blades[q] * Blade);
            tips[i] = q > p ? (tq - tp) / ((q - p) * dt) : Vector3.Zero;
        }

        var edges = new Vector3[n];
        edges[0] = SwordMotion.Perpendicular(-Vector3.UnitY, blades[0]);
        // 腕の長さ / s: 剣の先が腕の長さの 2 倍 / s より速く動く間だけ、刃筋を動きの面へ合わせる。
        SwordMotion.FollowCut(blades, tips, edges, moving: 2f, follow: 0.3f);
        var frames = new MotionFrame[n];
        for (var i = 0; i < n; i++)
        {
            frames[i] = new MotionFrame(rights[i], rights[i] - (blades[i] * spacing), blades[i], edges[i], heads[i], twists[i], new Vector3(0f, 0f, steps[i]));
        }

        return new SwordMotion(name, dt, frames);
    }

    /// <summary>体フレームでの向き（方位角: 0 = 前、正 = 左 / 仰角: 正 = 上）。</summary>
    public static Vector3 Direction(float azimuth, float elevation) =>
        new(MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(azimuth) * MathF.Cos(elevation));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>単位ベクトルの間を、球の上の最短の道（大円）で補う。</summary>
    public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        var angle = MathF.Acos(dot);
        if (angle < 1e-4f)
        {
            return Vector3.Normalize(Vector3.Lerp(a, b, t));
        }

        var sin = MathF.Sin(angle);
        return Vector3.Normalize(((MathF.Sin((1f - t) * angle) / sin) * a) + ((MathF.Sin(t * angle) / sin) * b));
    }

    // ── 構え（右手の位置・剣の方位角・仰角）。
    /// <summary>Vom Tag（屋根から）: 右肩の上で剣を立て、少し後ろへ傾ける。</summary>
    private static readonly (Vector3 R, float Az, float El) Tag = (new Vector3(-0.25f, 0.15f, 0.2f), MathF.PI, 1.2f);

    /// <summary>Ochs（雄牛）: 頭の右で両手を高く、剣先を相手の顔へ（やや下）。</summary>
    private static readonly (Vector3 R, float Az, float El) Ochs = (new Vector3(-0.3f, 0.3f, 0.35f), 0.15f, -0.1f);

    /// <summary>Pflug（鋤）: 右の腰の前で両手、剣先を相手の顔へ斜め上。</summary>
    private static readonly (Vector3 R, float Az, float El) Pflug = (new Vector3(-0.2f, -0.55f, 0.4f), 0.1f, 0.45f);

    /// <summary>Alber（愚者）: 両手を体の前の低い所、剣先を前下の床へ。</summary>
    private static readonly (Vector3 R, float Az, float El) Alber = (new Vector3(-0.05f, -0.65f, 0.45f), 0f, -0.6f);

    private static SwordKey Hold(float time, (Vector3 R, float Az, float El) g, float twist = 0f) => new(time, g.R, g.Az, g.El, Twist: twist);

    /// <summary>
    /// ロングソードの基本: 4 つの構えを順に取る型と、5 つの打ち（それぞれ構え → 打ち → 構えへ戻す、打ちで右足から踏み込む）。
    /// </summary>
    public static IReadOnlyList<SwordMotion> Longsword() =>
    [
        FromKeys("longsword-guards", [
            Hold(0f, Tag, -0.2f), Hold(0.8f, Tag, -0.2f),
            Hold(1.5f, Ochs, -0.15f), Hold(2.3f, Ochs, -0.15f),
            Hold(3.0f, Pflug, -0.1f), Hold(3.8f, Pflug, -0.1f),
            Hold(4.5f, Alber), Hold(5.3f, Alber),
            Hold(6.0f, Tag, -0.2f),
        ], LongswordSpacing),

        // Oberhau: 屋根から、頭の上を通して真上から斬り下ろす（剣先が相手の頭の高さで止まる所まで）。
        FromKeys("oberhau", [
            Hold(0f, Tag, -0.2f), Hold(0.4f, Tag, -0.2f),
            new(0.85f, new Vector3(-0.05f, 0.55f, 0.25f), MathF.PI, 1.45f, Twist: -0.1f),
            new(1.15f, new Vector3(-0.05f, -0.05f, 0.85f), 0f, -0.15f, Step: 1.6f, Lean: 0.12f, Twist: 0.15f),
            new(1.45f, new Vector3(-0.05f, -0.2f, 0.75f), 0f, -0.3f, Step: 0f, Lean: 0.1f, Twist: 0.1f),
            Hold(2.4f, Tag, -0.2f),
        ], LongswordSpacing),

        // Zornhau（怒りの打ち）: 右肩の屋根から、相手の左肩から右へ斜めに強く斬り下ろす。
        FromKeys("zornhau", [
            Hold(0f, Tag, -0.2f), Hold(0.4f, Tag, -0.25f),
            new(0.85f, new Vector3(-0.3f, 0.3f, 0.2f), -2.4f, 0.9f, Twist: -0.3f),
            new(1.15f, new Vector3(0.1f, -0.15f, 0.8f), 0.5f, -0.35f, Step: 1.6f, Lean: 0.12f, Twist: 0.35f),
            new(1.45f, new Vector3(0.2f, -0.35f, 0.65f), 0.8f, -0.6f, Lean: 0.1f, Twist: 0.3f),
            Hold(2.4f, Tag, -0.2f),
        ], LongswordSpacing),

        // Zwerchhau（横の打ち）: 屋根から、両手を頭の高さに保ったまま、右から相手の頭の左へ横に斬る。
        FromKeys("zwerchhau", [
            Hold(0f, Tag, -0.2f), Hold(0.4f, Tag, -0.3f),
            new(0.85f, new Vector3(-0.35f, 0.35f, 0.25f), -2.0f, 0.3f, Twist: -0.35f),
            new(1.15f, new Vector3(-0.05f, 0.35f, 0.75f), 1.2f, 0.05f, Step: 1.4f, Lean: 0.08f, Twist: 0.35f),
            new(1.45f, new Vector3(0.1f, 0.3f, 0.6f), 1.6f, 0.05f, Twist: 0.3f),
            Hold(2.4f, Tag, -0.2f),
        ], LongswordSpacing),

        // Unterhau（下からの打ち）: 鋤から右下へ引き、相手の左の腕・胴へ斜めに斬り上げる。
        FromKeys("unterhau", [
            Hold(0f, Pflug, -0.1f), Hold(0.4f, Pflug, -0.1f),
            new(0.85f, new Vector3(-0.4f, -0.55f, 0.15f), -2.3f, -0.5f, Twist: -0.35f),
            new(1.15f, new Vector3(0.1f, 0.15f, 0.75f), 0.7f, 0.45f, Step: 1.4f, Lean: 0.08f, Twist: 0.3f),
            new(1.45f, new Vector3(0.15f, 0.3f, 0.6f), 0.9f, 0.7f, Twist: 0.25f),
            Hold(2.4f, Pflug, -0.1f),
        ], LongswordSpacing),

        // Stich（突き）: 雄牛から、腕を伸ばしながら踏み込んで、剣先を相手の顔へまっすぐ突く。
        FromKeys("stich", [
            Hold(0f, Ochs, -0.15f), Hold(0.4f, Ochs, -0.15f),
            new(0.7f, new Vector3(-0.1f, 0.15f, 1.0f), 0.05f, -0.05f, Step: 1.8f, Lean: 0.15f, Twist: 0.2f),
            new(0.95f, new Vector3(-0.1f, 0.15f, 1.0f), 0.05f, -0.05f, Lean: 0.12f, Twist: 0.2f),
            Hold(1.7f, Ochs, -0.15f),
        ], LongswordSpacing),
    ];

    // ── 日本刀の型（両手、右手が鍔元・左手が柄頭）。構えは剣道の中段（剣道のモーションキャプチャの中段から取った値）。
    /// <summary>日本刀の両手の間隔（腕の長さ = 1。柄の両端）。</summary>
    public const float KatanaSpacing = 0.46f;

    /// <summary>中段: 拳をへその前、剣先を相手の喉へ（仰角 38°）。</summary>
    private static readonly (Vector3 R, float Az, float El) Chudan = (new Vector3(-0.2f, -0.57f, 0.78f), 0f, 0.66f);

    /// <summary>
    /// 日本刀の技（剣道・剣術から）。どれも中段から始まり中段へ戻る。打ちで右足から踏み込む（<c>Step</c> 正）、引き技は下がる。
    /// <list type="bullet">
    /// <item>突き（剣道の諸手突き）: 両腕を伸ばして剣先を喉へ。</item>
    /// <item>袈裟斬り: 右肩の上へ振りかぶり、相手の左肩から右脇へ斜めに斬り下ろす。</item>
    /// <item>逆袈裟: 左肩の上へ振りかぶり、相手の右肩から左脇へ。</item>
    /// <item>抜き胴: 振りかぶって、手首を返して相手の右の胴を横に斬り抜け、そのまま前へ出る。</item>
    /// <item>斬り上げ: 右下へ下ろし、相手の左脇から右肩へ斜めに斬り上げる。</item>
    /// <item>払い面: 相手の剣を左から右へ払ってから、そのまま頭の真上から斬り下ろす。</item>
    /// <item>引き面: 近間から、振りかぶって下がりながら頭を打つ。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<SwordMotion> Katana() =>
    [
        FromKeys("tsuki", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.45f, new Vector3(-0.12f, -0.2f, 1.15f), 0f, 0.15f, Step: 1.8f, Lean: 0.15f),
            new(0.65f, new Vector3(-0.12f, -0.2f, 1.1f), 0f, 0.15f, Lean: 0.12f),
            Hold(1.2f, Chudan),
        ], KatanaSpacing),
        FromKeys("kesa", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.6f, new Vector3(-0.35f, 0.35f, 0.3f), -2.3f, 1.1f, Twist: -0.3f),
            new(0.85f, new Vector3(0.1f, -0.35f, 0.85f), 0.6f, -0.45f, Step: 1.6f, Lean: 0.12f, Twist: 0.3f),
            new(1.05f, new Vector3(0.2f, -0.55f, 0.7f), 0.9f, -0.75f, Lean: 0.1f, Twist: 0.3f),
            Hold(1.7f, Chudan),
        ], KatanaSpacing),
        FromKeys("gyaku-kesa", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.6f, new Vector3(0.15f, 0.35f, 0.3f), 2.3f, 1.1f, Twist: 0.25f),
            new(0.85f, new Vector3(-0.3f, -0.35f, 0.85f), -0.6f, -0.45f, Step: 1.6f, Lean: 0.12f, Twist: -0.25f),
            new(1.05f, new Vector3(-0.4f, -0.55f, 0.7f), -0.9f, -0.75f, Lean: 0.1f, Twist: -0.25f),
            Hold(1.7f, Chudan),
        ], KatanaSpacing),
        FromKeys("nuki-do", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.55f, new Vector3(-0.1f, 0.45f, 0.4f), 0.4f, 1.4f, Twist: 0.1f),
            new(0.8f, new Vector3(0.15f, -0.4f, 0.8f), 1.2f, -0.15f, Step: 1.8f, Lean: 0.1f, Twist: 0.35f),
            new(1.05f, new Vector3(0.3f, -0.45f, 0.45f), 1.9f, -0.1f, Step: 1.0f, Twist: 0.35f),
            Hold(1.8f, Chudan),
        ], KatanaSpacing),
        FromKeys("kiriage", [
            Hold(0f, Chudan), Hold(0.2f, Chudan),
            new(0.55f, new Vector3(-0.35f, -0.75f, 0.45f), -2.0f, -0.6f, Twist: -0.3f),
            new(0.8f, new Vector3(0.05f, 0.05f, 0.85f), 0.6f, 0.55f, Step: 1.5f, Lean: 0.1f, Twist: 0.3f),
            new(1.0f, new Vector3(0.15f, 0.25f, 0.7f), 0.8f, 0.9f, Twist: 0.25f),
            Hold(1.6f, Chudan),
        ], KatanaSpacing),
        FromKeys("harai-men", [
            Hold(0f, Chudan), Hold(0.15f, Chudan),
            new(0.35f, new Vector3(-0.3f, -0.5f, 0.85f), -0.5f, 0.45f, Step: 0.6f),
            new(0.65f, new Vector3(-0.1f, 0.45f, 0.45f), 0f, 1.5f, Lean: 0.05f),
            new(0.9f, new Vector3(-0.1f, -0.15f, 0.95f), 0f, 0f, Step: 1.6f, Lean: 0.15f),
            new(1.1f, new Vector3(-0.1f, -0.3f, 0.85f), 0f, -0.15f, Lean: 0.1f),
            Hold(1.7f, Chudan),
        ], KatanaSpacing),
        FromKeys("hiki-men", [
            Hold(0f, Chudan), Hold(0.15f, Chudan),
            new(0.45f, new Vector3(-0.1f, 0.4f, 0.35f), 0f, 1.45f),
            new(0.7f, new Vector3(-0.1f, -0.15f, 0.8f), 0f, 0f, Step: -1.6f),
            new(0.9f, new Vector3(-0.1f, -0.35f, 0.75f), 0f, 0.3f, Step: -1.0f),
            Hold(1.5f, Chudan),
        ], KatanaSpacing),
    ];

    /// <summary>
    /// 刀で受ける構えの 1 フレーム: 右手の位置（体フレーム、腕の長さ = 1）に柄を置き、剣を <paramref name="blade"/> の向きにして、刃を
    /// <paramref name="edge"/> の向きへ（相手の打ちを鎬で受ける）。左手は柄の上の位置。
    /// </summary>
    public static MotionFrame Block(Vector3 right, Vector3 blade, Vector3 edge, float spacing) =>
        new(right, right - (blade * spacing), blade, SwordMotion.Perpendicular(edge, blade), Vector3.Zero, 0f, Vector3.Zero);
}
