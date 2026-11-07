using System.Numerics;

namespace MiniRing;

/// <summary>当たった剛体の種類（Aigis HumanoidRagdoll の箱）。</summary>
public enum BodyPartKind
{
    Head,
    Chest,
    Pelvis,
    UpperArm,
    Forearm,
    Thigh,
    Shin,
    Foot,
}

/// <summary>打たれた場所。ダメージの倍率（<see cref="ZoneDamage"/>）と、打撃音・傷の見た目を決める。</summary>
public enum HitZone
{
    Chin,
    Temple,
    Head,
    Neck,
    Heart,
    Liver,
    SolarPlexus,
    Body,
    Shoulder,
    Elbow,
    Guard,
    Knee,
    Leg,
}

/// <summary>
/// 部位の箱の中で「どこに当たったか」から急所を判定する（純関数）。
/// normalized は当たった点の部位ローカル座標を半径（half extents）で割った値（各軸 −1..1）。
/// 部位ローカルは +Z が体の前、+Y が上、+X が本人の左（右手系で +Z を向いた体の左）。
/// </summary>
public static class HitZones
{
    /// <summary>
    /// <paramref name="armSide"/> は腕の部位のとき、その腕が体のどちら側か（ローカル X 符号: 左 +1 / 右 −1）。
    /// 肩寄りか肘寄りかは、腕の長さ方向（ローカル X）で体側の端に近いかで決める。
    /// </summary>
    public static HitZone Classify(BodyPartKind part, Vector3 normalized, float armSide = 0f)
    {
        var u = normalized;
        return part switch
        {
            // 顎は顔の前・下、こめかみは横。首は頭の箱の底。
            // 顎を先に判定する（下から突き上げた拳は箱の底面に当たるが、顔の前寄りなら顎）。首は頭の底面の後ろ寄り。
            BodyPartKind.Head when u.Y < -0.25f && u.Z > 0f => HitZone.Chin,
            BodyPartKind.Head when u.Y < -0.75f => HitZone.Neck,
            BodyPartKind.Head when MathF.Abs(u.X) > 0.6f => HitZone.Temple,
            BodyPartKind.Head => HitZone.Head,

            // 胸の箱: 上端中央が首（喉）、前面の左が心臓、右下が肝臓、中央下がみぞおち、それ以外は肋骨・ボディ。
            BodyPartKind.Chest when u.Y > 0.7f && MathF.Abs(u.X) < 0.45f => HitZone.Neck,
            BodyPartKind.Chest when u.Z > 0f && u.X > 0.05f && u.X < 0.75f && u.Y > -0.2f && u.Y < 0.65f => HitZone.Heart,
            BodyPartKind.Chest when u.X < -0.2f && u.Y < 0f => HitZone.Liver,
            BodyPartKind.Chest when u.Z > 0f && MathF.Abs(u.X) < 0.35f && u.Y < 0.1f => HitZone.SolarPlexus,
            BodyPartKind.Chest => HitZone.Body,
            BodyPartKind.Pelvis when u.X < -0.3f && u.Y > 0.3f => HitZone.Liver,
            BodyPartKind.Pelvis => HitZone.Body,

            // 上腕: 体側の端（ローカル X が −side 側）なら肩、反対端なら肘。前腕は体側の端なら肘、それ以外はガード（グローブ）。
            BodyPartKind.UpperArm => u.X * armSide < 0f ? HitZone.Shoulder : HitZone.Elbow,
            BodyPartKind.Forearm => u.X * armSide < -0.5f ? HitZone.Elbow : HitZone.Guard,

            BodyPartKind.Thigh when u.Y < -0.55f => HitZone.Knee,
            BodyPartKind.Shin when u.Y > 0.55f => HitZone.Knee,
            _ => HitZone.Leg,
        };
    }

    public static string Label(HitZone zone) => zone switch
    {
        HitZone.Chin => "CHIN",
        HitZone.Temple => "TEMPLE",
        HitZone.Head => "HEAD",
        HitZone.Neck => "NECK",
        HitZone.Heart => "HEART",
        HitZone.Liver => "LIVER",
        HitZone.SolarPlexus => "SOLAR PLEXUS",
        HitZone.Body => "BODY",
        HitZone.Shoulder => "SHOULDER",
        HitZone.Elbow => "ELBOW",
        HitZone.Guard => "GUARD",
        HitZone.Knee => "KNEE",
        _ => "LEG",
    };
}
