using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 一人称（<c>--first-person</c>）の「VR の空間 → ゲームのワールド」の変換（相似変換: 縮尺 + Y 軸まわりの回転 + 平行移動）。
/// 身長の合わせ（A/X）のときに、HMD の位置が人形の頭の位置に重なるよう決めて、その後は固定する（人形が押されてもカメラは付いていかない）。
/// 動くのは、左スティックの歩き（<see cref="Moved"/> で基準点 <see cref="WorldAnchor"/> を平行移動）だけ。カメラは HMD の姿勢のまま
/// （この変換を掛けた視点で描く）で、体は頭をその点へ持っていく（<see cref="VrPlayerMapper"/>）。
/// <para><b>目の描画</b>: 目の位置・間隔・向きにこの変換を掛ける。縮尺は「人形の腕の長さ ÷ 人の腕の長さ」（<see cref="PuppetMapping.Scale"/>）なので、
/// 目の間隔も同じ縮尺で縮む（小さな体の目から見た世界）。</para>
/// </summary>
/// <param name="VrAnchor">合わせたときの HMD の位置（VR の空間）。</param>
/// <param name="WorldAnchor">その HMD が重なる点（ワールド。合わせたときは人形の頭、歩くと動く）。</param>
/// <param name="Yaw">VR の空間のヨーに足してワールドのヨーにする角 [rad]（人形が向いているヨー − 合わせたときの HMD のヨー）。</param>
/// <param name="Scale">VR の長さ → ワールドの長さの倍率。</param>
public readonly record struct VrWorldPlacement(Vector3 VrAnchor, Vector3 WorldAnchor, float Yaw, float Scale)
{
    private Quaternion YawRotation => Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw);

    /// <summary>VR の空間の点 → ワールド。</summary>
    public Vector3 Point(Vector3 vr) => WorldAnchor + (Vector3.Transform(vr - VrAnchor, YawRotation) * Scale);

    /// <summary>VR の空間のベクトル → ワールド（長さは縮尺ぶん）。</summary>
    public Vector3 Direction(Vector3 vr) => Vector3.Transform(vr, YawRotation) * Scale;

    /// <summary>VR の空間の姿勢（回転）→ ワールド。</summary>
    public Quaternion Rotation(Quaternion vr) => Quaternion.Normalize(Quaternion.Concatenate(vr, YawRotation));

    /// <summary>
    /// VR の空間の視点（目の行列。ワールド → 目）を、ワールドの視点にする（目の描画に使う）。
    /// 目の位置は <see cref="Point"/>、向きは <see cref="Rotation"/>。行列が逆にできなければそのまま返す。
    /// </summary>
    public Matrix4x4 View(Matrix4x4 vrView)
    {
        if (!Matrix4x4.Invert(vrView, out var eyeToVr))
        {
            return vrView;
        }

        var position = Point(eyeToVr.Translation);
        var rotation = Rotation(Quaternion.CreateFromRotationMatrix(eyeToVr));
        return Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position), out var view) ? view : vrView;
    }

    /// <summary>
    /// 描画に使う「VR の空間 → 表示の空間」の変換: 物理の空間（この変換の行き先）を、さらに <paramref name="display"/> で表示の空間へ移したもの。
    /// 表示 = 物理 × 縮尺なので、基準点と縮尺に表示の縮尺を掛けるだけ（回転は同じ）。目の描画（視点・目の位置・近い面）はこちらを使う。
    /// </summary>
    public VrWorldPlacement Displayed(DisplaySpace display) => display.IsIdentity ? this : this with { WorldAnchor = display.Point(WorldAnchor), Scale = Scale * display.Scale };

    /// <summary>歩いた分だけワールドの基準点を動かす（スティックの歩き）。</summary>
    public VrWorldPlacement Moved(Vector3 worldDelta) => this with { WorldAnchor = WorldAnchor + worldDelta };

    /// <summary>
    /// 合わせる: HMD の位置 <paramref name="hmdPosition"/>・ヨー <paramref name="hmdYaw"/> が、人形の頭 <paramref name="puppetHead"/>・人形の向き
    /// <paramref name="puppetYaw"/> に重なる変換。縮尺は <paramref name="scale"/>。
    /// </summary>
    public static VrWorldPlacement Align(Vector3 hmdPosition, float hmdYaw, Vector3 puppetHead, float puppetYaw, float scale)
        => new(hmdPosition, puppetHead, BodyFrame.DeltaAngle(hmdYaw, puppetYaw), scale);
}
