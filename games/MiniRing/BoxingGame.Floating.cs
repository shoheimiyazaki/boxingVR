using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing;

/// <summary>
/// VR のプレイヤーの武器を、手に持たせて始めるのでなく、目の前に浮かせて置く（<see cref="BoxingCorners.PlayerWeaponFloats"/>、2026-11-25、ユーザーの要望
/// 「開始時に落としてしまうので、VR プレイヤーに対してのみ、目の前に浮いている剣などを取るようにして」「落とした剣を拾えない」）。
/// <para>浮いている武器は運動学の物体（重さ・当たりの無い置物）で、持ち主の体のフレーム（肩の中心・向き）に対して決まった所に付いてくる。
/// 誰の体・武器とも当たらない（腕が通り抜けても切れない）。グリップで握ると（<see cref="TryGraspWeapon"/>）ふつうの物体に戻る。
/// 放した武器が床などで止まって <see cref="FloatReturnSeconds"/> 経つと、また持ち主の目の前に浮く。ゲームの近道（docs/DEBT.md「VR の浮いた武器」）。</para>
/// </summary>
public sealed partial class BoxingGame
{
    /// <summary>放した武器が止まってから（速さ <see cref="FloatRestSpeed"/> 未満が続いて）持ち主の前へ浮いて戻るまでの時間 [s]。</summary>
    public const float FloatReturnSeconds = 1.5f;

    /// <summary>止まっているとみなす武器の速さ [m/s]（転がり終えて床に横たわった剣）。</summary>
    private const float FloatRestSpeed = 0.3f;

    /// <summary>浮いている武器と、その持ち主。</summary>
    private readonly Dictionary<Weapon, Fighter> _floating = [];

    /// <summary>浮かせて渡した武器（放したら浮いて戻る）と、その持ち主。</summary>
    private readonly Dictionary<Weapon, Fighter> _floatHome = [];

    /// <summary>放した武器が止まっている時間 [s]。</summary>
    private readonly Dictionary<Weapon, float> _floatRest = [];

    /// <summary>その武器が今、目の前に浮いているか。</summary>
    public bool IsFloating(Weapon weapon) => _floating.ContainsKey(weapon);

    private void ClearFloating()
    {
        _floating.Clear();
        _floatHome.Clear();
        _floatRest.Clear();
    }

    /// <summary>手にはめる物（グローブ・トンファー・盾…）は浮かせない（握るのでなく、はめて始める）。</summary>
    private bool FloatsFor(Fighter fighter, WeaponKind kind) => fighter == Player && _corners.PlayerWeaponFloats && !Weapon.IsWorn(kind);

    /// <summary><paramref name="fighter"/> の目の前に <paramref name="kind"/> の武器を浮かせて置く。</summary>
    private Weapon FloatWeapon(Fighter fighter, WeaponKind kind)
    {
        var (position, orientation) = FloatPose(fighter, kind);
        var weapon = AddWeapon(kind, position, orientation);
        _world.MakeKinematic(weapon.Body.BodyId);
        _world.SetKinematicPose(weapon.Body.BodyId, position, orientation);
        _floating[weapon] = fighter;
        _floatHome[weapon] = fighter;
        return weapon;
    }

    /// <summary>
    /// 浮いている所: 握り（<see cref="Weapon.GripOf"/>）が利き手の構えの少し前・下（肩の中心から右へ 0.2、下へ 0.15、前へ 0.55 × 腕の長さ）、
    /// 打つ側の先が前へ 45° 上がる向き。手を前へ出せば握りに届き、切っ先は相手の方を向く。
    /// </summary>
    private static (Vector3 Position, Quaternion Orientation) FloatPose(Fighter fighter, WeaponKind kind)
    {
        var frame = fighter.Frame;
        var reach = fighter.ArmReach;
        var grip = frame.LocalToWorld(new Vector3(WeaponHand.LocalSide() * 0.2f * reach, -0.15f * reach, 0.55f * reach));
        var x = Vector3.Normalize(frame.Rotate(new Vector3(0f, 1f, 1f)));
        var y = Vector3.Normalize(Vector3.UnitY - (x * Vector3.Dot(Vector3.UnitY, x)));
        var z = Vector3.Cross(x, y);
        var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f)));
        return (grip - Vector3.Transform(Weapon.GripOf(kind), orientation), orientation);
    }

    /// <summary>
    /// 毎歩（物理の前に）: 浮いている武器を持ち主の前へ置き直し、放して止まった武器を浮かせて戻す。
    /// </summary>
    private void UpdateFloatingWeapons(float dt)
    {
        if (_floatHome.Count == 0)
        {
            return;
        }

        foreach (var (weapon, fighter) in _floatHome)
        {
            if (!_weapons.Contains(weapon))
            {
                continue; // 折れて消えた・外した
            }

            var id = weapon.Body.BodyId;
            if (_floating.ContainsKey(weapon))
            {
                var (position, orientation) = FloatPose(fighter, weapon.Kind);
                _world.SetKinematicPose(id, position, orientation);
                continue;
            }

            // 誰かが握っている・持ち主がもう別の武器を握っている間は戻さない。
            if (OwnerOf(weapon) is not null || HoldsAnyWeapon(fighter))
            {
                _floatRest[weapon] = 0f;
                continue;
            }

            var resting = _world.GetState(id).LinearVelocity.Length() < FloatRestSpeed;
            var rest = resting ? _floatRest.GetValueOrDefault(weapon) + dt : 0f;
            _floatRest[weapon] = rest;
            if (rest >= FloatReturnSeconds && !fighter.IsDead)
            {
                _world.MakeKinematic(id);
                var (position, orientation) = FloatPose(fighter, weapon.Kind);
                _world.SetKinematicPose(id, position, orientation);
                _floating[weapon] = fighter;
                _floatRest[weapon] = 0f;
            }
        }
    }

    /// <summary>
    /// 手に握っている武器（はめる物を除く）があるか。<see cref="WeaponOf"/> は放した古い武器を返すことがある（放しても一覧に残る）ので、
    /// 今握っているかで見る。
    /// </summary>
    private bool HoldsAnyWeapon(Fighter fighter)
    {
        foreach (var (weapon, holder) in _wielders)
        {
            if (holder == fighter && !Weapon.IsWorn(weapon.Kind) && HolderOf(weapon) == fighter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>補給の前: この選手へ浮かせて渡した武器がまだあれば補給しない（浮いている・持っている・床にある。床の物はやがて浮いて戻る）。</summary>
    private bool HasFloatingHomeWeapon(Fighter fighter)
    {
        foreach (var (weapon, home) in _floatHome)
        {
            if (home == fighter && _weapons.Contains(weapon))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>浮いている武器を握った: ふつうの物体に戻す（止まった状態から）。</summary>
    private void Unfloat(Weapon weapon)
    {
        if (!_floating.Remove(weapon))
        {
            return;
        }

        var id = weapon.Body.BodyId;
        _world.MakeDynamic(id);
        _world.SetLinearVelocity(id, Vector3.Zero);
        _world.SetAngularVelocity(id, Vector3.Zero);
    }

    /// <summary>
    /// 拳に一番近い握りの点（武器のローカル）と、その距離: 両手で握れる武器は柄のどこでも（両手の握りの点を結ぶ線分）、片手の武器は握りの点。
    /// 床に横たわった刀の柄の端に手が届けば、その所を握って拾う（2026-11-25 までは鍔元の 1 点だけで、柄の端では届かなかった。握りの点は
    /// 掴んだ所なので、拾った刀が鍔元まで手へ引き寄せられて跳ねない。添える手は先に握った手の位置から決まる: <see cref="TryAddSupport"/>）。
    /// </summary>
    private (Vector3 Local, float Distance) GraspPoint(Weapon weapon, Vector3 fist)
    {
        var s = _world.GetState(weapon.Body.BodyId);
        if (!Weapon.CanTwoHand(weapon.Kind))
        {
            var grip = Weapon.GripOf(weapon.Kind);
            return (grip, Vector3.Distance(fist, s.Position + Vector3.Transform(grip, s.Orientation)));
        }

        var (top, bottom) = Weapon.TwoHandGripsOf(weapon.Kind);
        var a = s.Position + Vector3.Transform(top, s.Orientation);
        var ab = Vector3.Transform(bottom - top, s.Orientation);
        var t = ab.LengthSquared() > 1e-10f ? Math.Clamp(Vector3.Dot(fist - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return (Vector3.Lerp(top, bottom, t), Vector3.Distance(fist, a + (ab * t)));
    }
}
