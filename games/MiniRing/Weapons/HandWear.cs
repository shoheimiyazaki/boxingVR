namespace MiniRing.Weapons;

/// <summary>
/// 両手にはめる物（グローブの種類）。どれもボクセルでできていて、拳（前腕の先）に付き、拳から離れない。拳の打撃は、はめた物と相手の体の
/// ボクセル同士の接触（<see cref="Aigis.Physics.Voxels.VoxelContact"/>、圧力のモデル）で起きる。何を壊すかは、打つ面の形（力がどれだけ
/// 狭い所に集まるか）と材質の強さ・たわみだけで決まる（種類ごとの威力の倍率は無い）。重さはボクセルの密度 × 体積で、腕はその重さも
/// 引くので、重い物ほど拳は遅れる。
/// </summary>
public enum HandWear
{
    /// <summary>ボクシンググローブ（革・詰め物・拳）。既定。<see cref="WeaponKind.Glove"/>。</summary>
    Glove,

    /// <summary>素手（皮膚と拳の骨）。<see cref="WeaponKind.Fist"/>。</summary>
    None,

    /// <summary>メリケンサック（拳頭の前の鉄の棒と突起）。<see cref="WeaponKind.KnuckleDuster"/>。</summary>
    KnuckleDuster,

    /// <summary>カタール（拳で握る H 字の枠から前へ伸びる鉄の刃）。<see cref="WeaponKind.Katar"/>。</summary>
    Katar,

    /// <summary>籠手（拳の前を覆う鉄の板・詰め物・革）。<see cref="WeaponKind.Gauntlet"/>。</summary>
    Gauntlet,
}

/// <summary><see cref="HandWear"/> の変換（ボクセルの物の種類・コマンドラインの名前）。</summary>
public static class HandWears
{
    /// <summary>コマンドラインで使える名前（<see cref="Parse"/> が受け付ける）。</summary>
    public const string Names = "glove|none|knuckles|katar|gauntlet";

    /// <summary>はめる物を作る種類。</summary>
    public static WeaponKind Kind(this HandWear wear) => wear switch
    {
        HandWear.Glove => WeaponKind.Glove,
        HandWear.None => WeaponKind.Fist,
        HandWear.KnuckleDuster => WeaponKind.KnuckleDuster,
        HandWear.Katar => WeaponKind.Katar,
        HandWear.Gauntlet => WeaponKind.Gauntlet,
        _ => throw new ArgumentOutOfRangeException(nameof(wear), wear, "知らないはめる物です。"),
    };

    /// <summary>
    /// コマンドラインの名前（<see cref="Names"/>。大文字・小文字は問わない。<c>bare</c>・<c>knuckleduster</c>・<c>knuckle-duster</c> も可）から。
    /// </summary>
    /// <exception cref="ArgumentException">知らない名前。</exception>
    public static HandWear Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToLowerInvariant() switch
        {
            "glove" or "gloves" => HandWear.Glove,
            "none" or "bare" or "fist" => HandWear.None,
            "knuckles" or "knuckleduster" or "knuckle-duster" => HandWear.KnuckleDuster,
            "katar" => HandWear.Katar,
            "gauntlet" or "gauntlets" => HandWear.Gauntlet,
            _ => throw new ArgumentException($"はめる物 '{name}' は知りません（{Names}）。", nameof(name)),
        };
    }
}
