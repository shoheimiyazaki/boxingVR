namespace MiniRing;

/// <summary>
/// 当たりの光（<see cref="Voxels.ImpactEffects"/>・飛び散り方）と打撃音（<see cref="Audio.HitTone"/>）の強さの尺度（見た目と音だけ。試合には響かない）。
/// どちらも打撃のダメージ（<see cref="HitEvent.Damage"/> = 運動量の平方根 − しきい値）から連続に出し、技の種類では分けない。
/// <para>
/// <b>尺度の根拠（2026-11-24 の測定、<c>fistmatch</c> champion:swarmer・counter:slugger・slugger:champion × 種 4〜9 × 40 秒、素手でなくグローブの拳）</b>:
/// 実物大への変換と物理 84〜87 のあと、拳の当たりは「数は少なく 1 発が重く、ダメージの上の側は低く」なった。
/// 1 試合あたりの当たり 145 → 12 発（物理 66 → 90）、乗る質量 p50 0.64 → 2.81 kg、エネルギー p50 0.60 → 16.4 J、
/// ダメージは p50 1.74 → 2.10、p90 5.14 → 4.12、p99 11.4 → 7.48（max 19.4 → 9.24）。ダメージの式は相似の換算（運動量 × 16.8）で
/// 合わせてあるので、尺度を変えなければ光る発数が 35 → 2.6 発 / 試合、いちばん強い光が 0.78 → 0.48 に落ち、音も 0.4 → 0.27 までしか上がらなかった。
/// そこで、今の分布の上の側が前と同じ強さの光・音になるように尺度を引き直した（下の 2 つの値）。
/// </para>
/// </summary>
internal static class HitLook
{
    /// <summary>
    /// この値のダメージより軽い当たりは光らない（飛び散り方の最小の強さ）。前は 3（物理 66 の当たりの上位 28 %）。今の当たりは
    /// 全部が「ちゃんと当たった」拳（エネルギー p10 = 7.6 J）なので、今の p50（2.1）に置いて上の半分が光るようにした。
    /// </summary>
    public const float FloorDamage = 2f;

    /// <summary>
    /// この値のダメージで光・音が最大になる。前は光 30・音 28（物理 66 の p99 = 11.4 は強さ 0.56・音 0.41）。
    /// 今の p99 = 7.48 が同じ光 0.57・音 0.39 になる 19（光の強さ = √((d − 2) / 17)、音の強さ = d / 19）。p90 は光 0.35・音 0.22（前は 0.28・0.18）。
    /// </summary>
    public const float FullDamage = 19f;

    /// <summary>光・飛び散り方の強さ 0（かすり）〜 1（とても重い 1 発）。平方根で（中くらいの差も見える）。</summary>
    public static float Strength(float damage) => MathF.Sqrt(Math.Clamp((damage - FloorDamage) / (FullDamage - FloorDamage), 0f, 1f));

    /// <summary>音の強さ 0..1（低く太く・長く・大きくなる）。</summary>
    public static float Loudness(float damage) => Math.Clamp(damage / FullDamage, 0f, 1f);
}
