using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 息が上がった見た目（描画だけ。試合には響かない）。チアノーゼの肌の色だけでは遠目に分かりにくかった（ユーザーの指摘）ので、
/// ゲームとしてひと目で読める合図を 3 つ重ねる。どれも <see cref="Fighter.Winded"/>（スタミナと息の働き）から連続に決まる。
/// <list type="bullet">
/// <item>吐く息: 口から白い息がふっと出て、前へ流れながら広がって消える。疲れるほど速く（肩で息をする拍子）、大きく濃い。</item>
/// <item>肌: 青紫へ寄せる（<see cref="Voxels.VoxelPart.Draw(float)"/>）。</item>
/// <item>スタミナのメーター: 残りが少ないと、息の拍子で白く点滅する（<see cref="MeterFlash"/>）。</item>
/// </list>
/// </summary>
public static class TiredLook
{
    /// <summary>落ち着いているときの息の速さ [回/s]（安静時の呼吸 ≈ 15 回/分）。</summary>
    private const float RestBreaths = 0.25f;

    /// <summary>息が上がりきったときの速さ [回/s]（激しい運動の直後の、あえぐ呼吸 ≈ 100 回/分）。</summary>
    private const float PantBreaths = 1.7f;

    /// <summary>吐く息が見え始める <see cref="Fighter.Winded"/>（ここより下では息を見せない: 試合の始めから白い息が出ると合図にならない）。</summary>
    private const float ShowFrom = 0.15f;

    /// <summary>吐いた息が消えるまで [s]。</summary>
    private const float PuffLife = 0.75f;

    /// <summary>吐いた息が口から離れていく速さ [m/s]（前へ）と、昇る速さ [m/s]。</summary>
    private const float PuffSpeed = 0.45f;
    private const float PuffRise = 0.12f;

    /// <summary>吐いた直後の玉の半径 [m] と、1 秒あたりに広がる半径 [m/s]。</summary>
    private const float PuffRadius = 0.016f;
    private const float PuffGrow = 0.06f;

    /// <summary>
    /// いちばん息が上がったときの濃さ（白い玉の被覆率。半端な値は網点で抜ける）。加算の光（AdditiveGlow）で描くと自己発光の
    /// ブルームで頭が白い光の輪に包まれ（顔の窓は白く飛んだ）、息に見えなかったので、照明を受ける白い玉の網点にした。
    /// </summary>
    private const float PuffStrength = 0.7f;

    /// <summary>吐く向きの下への傾き（前 1 に対する下の割合。0.7 ≈ 35°）。</summary>
    private const float PuffDown = 0.7f;

    /// <summary>吐き始めて濃さが立ち上がるまで [s]（いきなり玉が出ない）。</summary>
    private const float PuffOnset = 0.08f;

    /// <summary>吐いた息が空気に止められる割合 [1/s]（前へ進む速さが時間とともに落ちる）。</summary>
    private const float PuffDrag = 0.4f;

    /// <summary>息が上がりかけ（濃さ 0）のとき、雲がどれだけ小さいか（1 − この値の大きさ）。</summary>
    private const float SmallPuffShare = 0.3f;

    /// <summary>
    /// メーターの「ヤバい」区域の上端で、点滅し始めるスタミナ。鈍り始め（<see cref="FighterTuning.TiredKnee"/> 0.6）の半分:
    /// ここでは拳を運ぶ力がすでに 0.7 倍ほどへ落ちていて、あと数発で尽きる。
    /// </summary>
    public const float DangerBelow = 0.3f;

    private static readonly Vector4 BreathColor = new(0.85f, 0.92f, 1f, 1f);

    /// <summary>1 つの息の雲を作る玉（口から見た向きのずれ: 横・上・前、と半径の倍率）。ばらつきが無いと 1 つの玉に見える。</summary>
    private static readonly (Vector3 Offset, float Scale)[] Cloud =
    [
        (new Vector3(0f, 0f, 0f), 1f),
        (new Vector3(0.6f, 0.35f, 0.4f), 0.75f),
        (new Vector3(-0.55f, 0.15f, 0.7f), 0.7f),
        (new Vector3(0.1f, -0.3f, 1.1f), 0.6f),
    ];

    /// <summary>息の速さ [回/s]: 息の上がり方 <paramref name="winded"/>（0..1）で安静からあえぎまで連続に。</summary>
    public static float BreathsPerSecond(float winded) => RestBreaths + ((PantBreaths - RestBreaths) * Math.Clamp(winded, 0f, 1f));

    /// <summary>吐く息の濃さ（0..1）: <see cref="ShowFrom"/> から滑らかに立ち上がる。</summary>
    public static float Visibility(Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        var t = Math.Clamp((fighter.Winded - ShowFrom) / (1f - ShowFrom), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    /// スタミナのメーターの白い点滅（0..1）: 残りが <see cref="DangerBelow"/> を下回るほど強く、息を吐く瞬間に明るい
    /// （速く息をするほど速く点滅する）。
    /// </summary>
    public static float MeterFlash(Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        if (fighter.IsDead)
        {
            return 0f; // 息をしていない体は点滅させない（スタミナは戻らないまま残る）
        }

        var low = Math.Clamp((DangerBelow - fighter.Stamina) / DangerBelow, 0f, 1f);
        var pulse = 0.5f + (0.5f * MathF.Cos(2f * MathF.PI * fighter.BreathPhase));
        return MathF.Sqrt(low) * pulse;
    }

    /// <summary>
    /// 両者の吐く息（白い玉の雲）を詰める。前の息の位置は今の息の速さで逆算する近道なので、打たれて息の速さが急に変わると
    /// 漂っている息が少し跳ぶ（0.75 秒で消えるので目立たない）。
    /// </summary>
    public static void WriteInstances(BoxingGame game, ICollection<PrimitiveInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(instances);
        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            var strength = Visibility(fighter);
            if (strength <= 0f)
            {
                continue;
            }

            var head = game.World.GetState(fighter.Parts.Head);
            var half = fighter.HalfExtentsOf(fighter.Parts.Head);
            // 口: 頭の箱の中心から、下へ半分弱・前（頭のローカル +Z が顔の向き。FaceCam と同じ）へ箱の表面まで。
            var mouth = head.Position + Vector3.Transform(new Vector3(0f, -0.45f * half.Y, 0.95f * half.Z), head.Orientation);
            // 息は顔の前へ真っすぐでなく、斜め下へ吐く（あえぐとき顎が落ちる）。真っすぐだと顔の窓のカメラ（顔の前 0.3 m）を雲が覆った。
            var forward = Vector3.Normalize(Vector3.Transform(new Vector3(0f, -PuffDown, 1f), head.Orientation));
            var right = Vector3.Transform(Vector3.UnitX, head.Orientation);
            var period = 1f / BreathsPerSecond(fighter.Winded);
            var since = (fighter.BreathPhase - MathF.Floor(fighter.BreathPhase)) * period;
            // 前の息がまだ消えていなければ一緒に描く（あえぐと息が重なる）。
            for (var age = since; age < PuffLife; age += period)
            {
                var fade = 1f - (age / PuffLife);
                var weight = PuffStrength * strength * fade * fade * MathF.Min(1f, age / PuffOnset);
                var center = mouth + (forward * (PuffSpeed * age * (1f - (PuffDrag * age)))) + (Vector3.UnitY * (PuffRise * age));
                var radius = (PuffRadius + (PuffGrow * age)) * (1f - SmallPuffShare + (SmallPuffShare * strength));
                foreach (var (offset, scale) in Cloud)
                {
                    var at = center + (((right * offset.X) + (Vector3.UnitY * offset.Y) + (forward * offset.Z)) * radius);
                    instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(at),
                        new Vector3(radius * scale, 0f, 0f), BreathColor with { W = weight }, MaterialKind.Standard));
                }
            }
        }
    }
}
