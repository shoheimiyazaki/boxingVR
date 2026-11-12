using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing;

/// <summary>
/// 試合の世界に置いたボクセルの武器（<see cref="Weapon"/>）。武器は体・他の武器とボクセル同士で当たり（<see cref="VoxelContact"/>）、
/// 壊れるかどうかは触れた形と材質の強さだけで決まる。切られた体は、拳で削られたときと同じく破片を散らし、骨が断たれれば落ち、
/// 臓器が壊れれば死ぬ。物理の箱の当たり判定は、リングとだけぶつかる。
/// </summary>
public sealed partial class BoxingGame
{
    private readonly List<Weapon> _weapons = [];
    private readonly Dictionary<int, BodyState> _beforeStep = [];
    private readonly List<VoxelContactResult> _recentCuts = [];
    private readonly Dictionary<Weapon, Fighter> _wielders = [];
    private readonly Dictionary<Weapon, Hand> _wieldHand = [];
    private readonly Dictionary<Weapon, float> _weaponCooldown = [];
    private readonly Dictionary<Weapon, float> _selfCooldown = [];

    /// <summary>体の重さを運ぶ打撃と数える、当たる所の速さの下限 [m/s]（押し当てているだけの接触は数えない）。</summary>
    private const float MinimumBlowSpeed = 0.8f;

    /// <summary>武器の打撃の、次の打撃までの間を数える（物理の 1 歩ごと）。</summary>
    private void CoolWeapons(float dt)
    {
        foreach (var weapon in _weaponCooldown.Keys.ToList())
        {
            _weaponCooldown[weapon] = MathF.Max(0f, _weaponCooldown[weapon] - dt);
        }

        foreach (var weapon in _selfCooldown.Keys.ToList())
        {
            _selfCooldown[weapon] = MathF.Max(0f, _selfCooldown[weapon] - dt);
        }
    }

    /// <summary>武器を握る手（利き手）。</summary>
    public const Hand WeaponHand = Hand.Right;

    /// <summary><paramref name="fighter"/> が握っている武器（無ければ null）。</summary>
    public Weapon? WeaponOf(Fighter fighter) => _wielders.FirstOrDefault(kv => kv.Value == fighter && kv.Key.Kind != WeaponKind.Glove).Key;

    /// <summary><paramref name="fighter"/> が手 <paramref name="hand"/> にはめているグローブ（はめていなければ null）。</summary>
    public Weapon? GloveOf(Fighter fighter, Hand hand) =>
        _wielders.FirstOrDefault(kv => kv.Value == fighter && kv.Key.Kind == WeaponKind.Glove && _wieldHand[kv.Key] == hand).Key;

    /// <summary>
    /// <paramref name="fighter"/> の、武器を握っていない手に、ボクセルのグローブ（<see cref="WeaponKind.Glove"/>、コーナーの色）をはめる。
    /// 拳の打撃は、このグローブと相手の体のボクセル同士の接触（圧力のモデル）で起きる。
    /// </summary>
    public void Glove(Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        foreach (var hand in Hands)
        {
            if (fighter.ArmSevered(hand) || fighter.Wields(hand) || fighter.WearsGlove(hand))
            {
                continue;
            }

            var glove = Weapon.CreateGlove(_world, fighter == Player ? BoxerAnatomy.RedGlove : BoxerAnatomy.BlueGlove, fighter.GloveCenter(hand), Quaternion.Identity);
            Track(glove);
            fighter.Wield(hand, glove.Body.BodyId, Weapon.GripOf(WeaponKind.Glove), glove: true);
            _wielders[glove] = fighter;
            _wieldHand[glove] = hand;
        }
    }

    /// <summary>両手持ちで、利き手に添えて下を握る手。</summary>
    public const Hand SupportHand = Hand.Left;

    /// <summary>
    /// <paramref name="fighter"/> の右手のグローブを外し、<paramref name="kind"/> の武器を握らせる（<see cref="Fighter.Wield"/>）。
    /// <paramref name="twoHanded"/> なら左手のグローブも外し、右手の下（握りの端の側）に添えさせる（<see cref="Fighter.Support"/>。
    /// 左手は握りに届いたときに握る）。握った本人の体とは、ボクセルでも当たらない。
    /// </summary>
    public Weapon Arm(Fighter fighter, WeaponKind kind, bool twoHanded = false)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        if (fighter.ArmSevered(WeaponHand) || fighter.Wields(WeaponHand) || (twoHanded && (fighter.ArmSevered(SupportHand) || fighter.Wields(SupportHand))))
        {
            throw new InvalidOperationException("武器を握る手が切り落とされているか、もう何かを握っています。");
        }

        if (twoHanded && !Weapon.CanTwoHand(kind))
        {
            throw new ArgumentException($"{kind} は両手で握れません（片手剣の握りは片手ぶんの長さ）。", nameof(kind));
        }

        var weapon = AddWeapon(kind, fighter.GloveCenter(WeaponHand), Quaternion.Identity);
        if (twoHanded)
        {
            var (top, bottom) = Weapon.TwoHandGripsOf(kind);
            fighter.Wield(WeaponHand, weapon.Body.BodyId, top);
            fighter.Support(SupportHand, WeaponHand, bottom);
        }
        else
        {
            fighter.Wield(WeaponHand, weapon.Body.BodyId, Weapon.GripOf(kind));
        }

        _wielders[weapon] = fighter;
        _wieldHand[weapon] = WeaponHand;
        return weapon;
    }

    /// <summary>
    /// <see cref="Sense"/> に、握った武器の見え方を足したもの: 自分が武器を握っているか、相手の武器の握り・先・先の速度。
    /// </summary>
    private FighterSense ArmedSense(Fighter self, Fighter opponent)
    {
        var sense = Sense(self, opponent) with { Ring = Layout };
        if (_wielders.Count == 0)
        {
            return sense;
        }

        var blade = default(BladeSense);
        if (WeaponOf(opponent) is { } weapon && HolderOf(weapon) == opponent)
        {
            var s = _world.GetState(weapon.Body.BodyId);
            var tip = s.Position + Vector3.Transform(Weapon.TipOf(weapon.Kind), s.Orientation);
            blade = new BladeSense(true, opponent.GloveCenter(HandOf(weapon)), tip, s.LinearVelocity + Vector3.Cross(s.AngularVelocity, tip - s.Position));
        }

        return sense with { OpponentBlade = blade, SelfArmed = WeaponOf(self) is { } mine && HolderOf(mine) == self };
    }

    /// <summary>今その武器を手に握っているファイター（腕ごと落ちた・放した武器なら null）。</summary>
    private Fighter? HolderOf(Weapon weapon) =>
        _wielders.TryGetValue(weapon, out var holder) && holder.WieldedBody(HandOf(weapon)) == weapon.Body.BodyId ? holder : null;

    /// <summary>その武器を握る（グローブをはめる）手。</summary>
    private Hand HandOf(Weapon weapon) => _wieldHand.TryGetValue(weapon, out var hand) ? hand : WeaponHand;

    /// <summary>
    /// 握った手が武器を押し込む力 [N]（ワールド。握られていなければ 0）: この歩で腕が拳を目標へ押した力（両手なら 2 本の腕の和）。
    /// 武器の当たりは、武器そのものの運動エネルギーに加えて、この力のうち当たる向きの分が押し込む仕事でも斬り進む
    /// （<see cref="VoxelContact.Resolve"/> の drive）。引いている手・力の抜けた手は押し込まない。
    /// </summary>
    private Vector3 Drive(Weapon weapon)
    {
        // グローブは押し込まない: 腕が押し付ける力は詰め物が広い面で受けて散らす（圧力のモデルは表の革の面だけを見るので、詰め物の
        // 下で押す腕の力を数えると、構えたグローブを押し付けているだけで肌が削れてしまう）。
        if (HolderOf(weapon) is not { } holder || weapon.Kind == WeaponKind.Glove)
        {
            return Vector3.Zero;
        }

        var hand = HandOf(weapon);
        var force = holder.HandPush(hand);
        return holder.SupportOf(hand) is { } support ? force + holder.HandPush(support) : force;
    }

    /// <summary>部位 <paramref name="bodyId"/> が、<paramref name="fighter"/> の武器を握っている（添えに行っている）手の前腕か。</summary>
    private static bool IsGripping(Fighter fighter, int bodyId) =>
        Hands.Any(h => (fighter.Wields(h) || fighter.WearsGlove(h)) && fighter.ForearmId(h) == bodyId);

    /// <summary>直近の <see cref="Step"/> で、握った武器が自分の体を打った打撃（得点にはならない。<see cref="WeaponHits"/> には入れない）。</summary>
    public List<WeaponHitEvent> SelfWeaponHits { get; } = [];

    /// <summary>握った武器が握った本人の体にも当たるか（既定 true。調べもので切って、自分の体が剣の道を塞いでいるかを見る）。</summary>
    public bool WeaponsHitHolder { get; set; } = true;

    /// <summary>直近の <see cref="Step"/> で、握られた武器が相手の体を壊した打撃。</summary>
    public List<WeaponHitEvent> WeaponHits { get; } = [];

    /// <summary>世界に置いた武器（折れて飛んだ破片も 1 本として加わる）。</summary>
    public IReadOnlyList<Weapon> Weapons => _weapons;

    /// <summary>直近の <see cref="Step"/> で武器が触れた接触（切った・潰した・弾いた）。</summary>
    public IReadOnlyList<VoxelContactResult> RecentCuts => _recentCuts;

    /// <summary>
    /// 武器を置く（<paramref name="position"/> に、<paramref name="orientation"/> の向きで、止まった状態で）。
    /// 動かすには <see cref="PhysicsWorld.SetLinearVelocity"/> などで速度を与える。ラウンドが変わると世界ごと消える。
    /// </summary>
    public Weapon AddWeapon(WeaponKind kind, Vector3 position, Quaternion orientation)
    {
        var weapon = Weapon.Create(_world, kind, position, orientation);
        Track(weapon);
        return weapon;
    }

    private void Track(Weapon weapon)
    {
        var id = weapon.Body.BodyId;
        foreach (var other in _weapons)
        {
            _world.SetPairCollisionEnabled(id, other.Body.BodyId, false);
        }

        foreach (var part in VoxelPartsOf(Fighters))
        {
            _world.SetPairCollisionEnabled(id, part.Body.BodyId, false);
        }

        foreach (var fighter in Fighters)
        {
            foreach (var part in fighter.AllParts)
            {
                _world.SetPairCollisionEnabled(id, part, false);
            }
        }

        _weapons.Add(weapon);
    }

    /// <summary>切り落とされた体の破片も、武器とは箱ではなくボクセルで当たるようにする。</summary>
    private void UntouchedByWeaponBoxes(int bodyId)
    {
        foreach (var weapon in _weapons)
        {
            _world.SetPairCollisionEnabled(weapon.Body.BodyId, bodyId, false);
        }
    }

    /// <summary>
    /// 2 つのボクセルの体が、この歩の間に近づけるか（格子を囲む球同士の、歩の前と後の近い方の距離から、動いた分を引いて）。遠ければ接触を
    /// 調べない（<see cref="VoxelContact.Resolve"/> の手前の安い振り分け。結果は変わらない: 球が離れていれば格子は触れない）。
    /// </summary>
    private bool MayTouch(VoxelBody a, BodyState aBefore, VoxelBody b, BodyState bBefore)
    {
        var aNow = _world.GetState(a.BodyId);
        var bNow = _world.GetState(b.BodyId);
        var reach = a.Grid.HalfExtents.Length() + b.Grid.HalfExtents.Length() + a.GridCenterLocal.Length() + b.GridCenterLocal.Length();
        var moved = Vector3.Distance(aBefore.Position, aNow.Position) + Vector3.Distance(bBefore.Position, bNow.Position) + 0.01f;
        var gap = MathF.Min(Vector3.Distance(aBefore.Position, bBefore.Position), Vector3.Distance(aNow.Position, bNow.Position));
        return gap - moved <= reach;
    }

    private static IEnumerable<VoxelPart> VoxelPartsOf(IEnumerable<Fighter> fighters) =>
        fighters.SelectMany(f => f.Voxels is { } body ? body.Parts.Concat(body.Fragments) : []);

    /// <summary>物理の 1 歩の前: 武器と体の部位の姿勢を覚える（接触は歩の前後の動きを掃いて調べる）。</summary>
    private void RememberBeforeStep()
    {
        _beforeStep.Clear();
        if (_weapons.Count == 0)
        {
            return;
        }

        foreach (var weapon in _weapons)
        {
            _beforeStep[weapon.Body.BodyId] = _world.GetState(weapon.Body.BodyId);
        }

        foreach (var part in VoxelPartsOf(Fighters))
        {
            _beforeStep[part.Body.BodyId] = _world.GetState(part.Body.BodyId);
        }
    }

    /// <summary>物理の 1 歩の後: 武器 × 武器、武器 × 体の部位の接触を解く。</summary>
    private void ResolveWeapons()
    {
        if (_weapons.Count == 0)
        {
            return;
        }

        // 接触で折れた破片は、この歩の後で加わる（今の歩の前の姿勢を持たない）。
        var weapons = _weapons.ToList();
        for (var i = 0; i < weapons.Count; i++)
        {
            var weapon = weapons[i];
            if (!_beforeStep.TryGetValue(weapon.Body.BodyId, out var before))
            {
                continue;
            }

            for (var j = i + 1; j < weapons.Count; j++)
            {
                var other = weapons[j];
                // 同じ人の両手のグローブは打ち合わない（構えでいつも触れ合っている）。
                if (weapon.Kind == WeaponKind.Glove && other.Kind == WeaponKind.Glove && HolderOf(weapon) is { } h && HolderOf(other) == h)
                {
                    continue;
                }

                if (_beforeStep.TryGetValue(other.Body.BodyId, out var otherBefore) && MayTouch(weapon.Body, before, other.Body, otherBefore))
                {
                    Clash(weapon, other, VoxelContact.Resolve(weapon.Body, before, other.Body, otherBefore, Drive(weapon), Drive(other)));
                }
            }

            var drive = Drive(weapon);
            foreach (var fighter in Fighters)
            {
                // 握った本人の体にも当たる（振りかぶった棍棒が自分の頭を打つことも）。握っている手の前腕だけは除く: 拳と武器は
                // 玉継手でつながっていて、握りはいつも前腕の先に触れている。
                var holds = HolderOf(weapon) == fighter;
                // グローブは自分の体を打たない（構えたグローブはいつも自分の顔に触れている。自分を殴る力は乗らない）。
                if (holds && (!WeaponsHitHolder || weapon.Kind == WeaponKind.Glove))
                {
                    continue;
                }

                foreach (var part in VoxelPartsOf([fighter]).ToList())
                {
                    if (holds && IsGripping(fighter, part.Body.BodyId))
                    {
                        continue;
                    }

                    if (_beforeStep.TryGetValue(part.Body.BodyId, out var partBefore) && MayTouch(weapon.Body, before, part.Body, partBefore))
                    {
                        Cut(weapon, fighter, part, VoxelContact.Resolve(weapon.Body, before, part.Body, partBefore, drive));
                    }
                }
            }
        }
    }

    /// <summary>武器同士: 壊れた側の破片を散らし、折れた先を新しい武器の破片にする。</summary>
    private void Clash(Weapon a, Weapon b, VoxelContactResult hit)
    {
        if (!hit.Touched)
        {
            return;
        }

        _recentCuts.Add(hit);
        Shatter(hit, a.Body, -hit.Normal, b.Body, hit.Normal, fatal: false);
        // 金属と金属がぶつかった所: 火花（ぶつかった所の両方の材質が鉄のとき、近づく速さとともに）。
        if (MaterialNear(a.Body, hit.Point) == BoxerAnatomy.Iron && MaterialNear(b.Body, hit.Point) == BoxerAnatomy.Iron)
        {
            var sa = _world.GetState(a.Body.BodyId);
            var sb = _world.GetState(b.Body.BodyId);
            var relative = (sa.LinearVelocity + Vector3.Cross(sa.AngularVelocity, hit.Point - sa.Position))
                - (sb.LinearVelocity + Vector3.Cross(sb.AngularVelocity, hit.Point - sb.Position));
            var speed = MathF.Max(MathF.Abs(Vector3.Dot(relative, hit.Normal)), relative.Length() * 0.5f);
            var along = relative.LengthSquared() > 1e-8f ? Vector3.Normalize(relative) : Vector3.Zero;
            MetalSparks(hit.Point, hit.Normal, along, speed);
        }

        Snapped(a, hit.SnappedA);
        Snapped(b, hit.SnappedB);
    }

    /// <summary><paramref name="body"/> の、ワールドの点 <paramref name="point"/> のいちばん近くの（1.5 セル以内の）埋まったセルの材質（無ければ 0）。</summary>
    private static byte MaterialNear(VoxelBody body, Vector3 point)
    {
        var grid = body.Grid;
        var local = body.WorldToGrid(point);
        var step = grid.CellSize;
        byte found = 0;
        var best = float.MaxValue;
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    var offset = new Vector3(x, y, z) * step;
                    if (grid.FilledCellAt(local + offset) is var i and >= 0 && offset.LengthSquared() < best)
                    {
                        best = offset.LengthSquared();
                        found = grid.MaterialAt(i);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>武器 × 体の部位: 切られた肉と骨を散らし、骨が断たれたら落とし、臓器が壊れたら死なせる。</summary>
    private void Cut(Weapon weapon, Fighter victim, VoxelPart part, VoxelContactResult hit)
    {
        if (!hit.Touched)
        {
            return;
        }

        _recentCuts.Add(hit);
        Snapped(weapon, hit.SnappedA);
        // 物理の接触が頭を回した分（棍棒そのものの運動量）。
        var contactTwist = Vector3.Zero;
        if (part.Kind == BodyPartKind.Head && part.FragmentOf is null && hit.ImpulseOnB.LengthSquared() > 0f)
        {
            var head = _world.GetState(part.Body.BodyId);
            contactTwist = Vector3.Cross(hit.Point - head.Position, hit.ImpulseOnB);
        }

        // グローブの打撃は拳の打撃（パンチ）: 拳と同じ物差しで数え、体の重さを運ぶ。壊れるのはボクセル同士の接触が壊した分だけ。
        if (weapon.Kind == WeaponKind.Glove)
        {
            if (!(part.FragmentOf is null && HolderOf(weapon) is { } puncher && puncher != victim && Punch(puncher, HandOf(weapon), victim, part, hit, contactTwist)))
            {
                if (contactTwist != Vector3.Zero && victim.State == FighterState.Standing)
                {
                    victim.TwistHead(contactTwist);
                }
            }

            Shatter(hit, weapon.Body, -hit.Normal, part.Body, hit.Normal, fatal: false);
            return;
        }

        // 握った武器の打撃は、拳の打撃と同じく、拳の後ろの体の重さも運ぶ（武器は梃子で速さを、体は重さを足す）。1 振りに 1 度。
        var carried = false;
        if (!RoundOver && part.FragmentOf is null && HolderOf(weapon) is { } swinger && swinger != victim && victim.State == FighterState.Standing
            && _weaponCooldown.GetValueOrDefault(weapon) <= 0f && _beforeStep.TryGetValue(weapon.Body.BodyId, out var weaponBefore))
        {
            var contactVelocity = weaponBefore.LinearVelocity + Vector3.Cross(weaponBefore.AngularVelocity, hit.Point - weaponBefore.Position);
            var contactSpeed = MathF.Max(0f, Vector3.Dot(contactVelocity, hit.Direction));
            var swingHand = HandOf(weapon);
            var fistVelocity = swinger.GloveVelocity(swingHand);
            var fistSpeed = MathF.Max(0f, Vector3.Dot(fistVelocity, hit.Direction));
            // 両手で握っていれば、添えた腕の重さも武器の後ろに乗る。
            var extraMass = swinger.ImpactMass(swingHand, hit.Direction * fistSpeed, out _) - swinger.ArmMass(swingHand)
                + (swinger.SupportOf(swingHand) is { } support ? swinger.ArmMass(support) : 0f);
            if (contactSpeed >= MinimumBlowSpeed && extraMass > 0f)
            {
                var momentum = hit.Direction * (extraMass * fistSpeed * KnockbackTransfer);
                var push = CarryBody(swinger, victim, part.Body.BodyId, hit.Point, momentum, contactSpeed, contactTwist);
                _weaponCooldown[weapon] = RehitCooldown;
                carried = true;

                // 得点も拳と同じ物差し: 運んだ運動量（体の分 + 棍棒そのものの分）と当たる速さの鋭さ、急所の倍率から。
                var delivered = momentum.Length() + hit.ImpulseOnB.Length();
                var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
                var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
                var strength = PunchMechanics.Damage(delivered) * PunchMechanics.Sharpness(contactSpeed);
                var damage = strength * ZoneDamage.Multiplier(zone);
                victim.Condition.ApplyHit(zone, damage);

                // 打撲: 刃の無い硬い武器が運んだ運動エネルギーで、当たった所を拳と同じく潰して掘る（グローブの詰め物が無い分、深く）。
                // 刃のある武器は、刃が当たった所をボクセル同士の接触そのもので斬る（潰しはしない）。
                var crushed = 0;
                if (Weapon.IsBlunt(weapon.Kind))
                {
                    Strike(victim, part.Body.BodyId, hit.Point, hit.Direction, strength, swinger, push, BluntCarve(delivered, contactSpeed));
                    crushed = _chips.Count + _chunks.Sum(c => c.Cells.Count); // 潰れて飛んだセル（切り落とされた先は数えない）
                }

                WeaponHits.Add(new WeaponHitEvent(swinger, victim, part.Kind, zone, hit.Point, hit.RemovedB.Count + crushed, contactSpeed,
                    delivered, part.Kind == BodyPartKind.Head ? victim.LastHeadTwist : 0f, damage));
            }
        }

        WeaponHitEvent? selfHit = null;
        // 自分の体を打った: 体の重さは乗らない（自分の体の中で押し合うだけ）。武器そのものの運動量で、相手を打つときと同じく潰して掘る。
        if (!RoundOver && part.FragmentOf is null && HolderOf(weapon) == victim && _selfCooldown.GetValueOrDefault(weapon) <= 0f
            && _beforeStep.TryGetValue(weapon.Body.BodyId, out var selfBefore))
        {
            var contactVelocity = selfBefore.LinearVelocity + Vector3.Cross(selfBefore.AngularVelocity, hit.Point - selfBefore.Position)
                - _world.GetState(part.Body.BodyId).LinearVelocity;
            var contactSpeed = MathF.Max(0f, Vector3.Dot(contactVelocity, hit.Direction));
            var delivered = hit.ImpulseOnB.Length();
            if (contactSpeed >= MinimumBlowSpeed && delivered > 0f)
            {
                _selfCooldown[weapon] = RehitCooldown; // 相手への 1 打の間（_weaponCooldown）とは別に数える
                var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
                var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
                var strength = PunchMechanics.Damage(delivered) * PunchMechanics.Sharpness(contactSpeed);
                var damage = strength * ZoneDamage.Multiplier(zone);
                victim.Condition.ApplyHit(zone, damage);
                var crushed = 0;
                if (Weapon.IsBlunt(weapon.Kind))
                {
                    Strike(victim, part.Body.BodyId, hit.Point, hit.Direction, strength, victim, Vector3.Zero, BluntCarve(delivered, contactSpeed));
                    crushed = _chips.Count + _chunks.Sum(c => c.Cells.Count);
                }

                selfHit = new WeaponHitEvent(victim, victim, part.Kind, zone, hit.Point, hit.RemovedB.Count + crushed, contactSpeed, delivered, 0f, damage);
            }
        }

        if (!carried && contactTwist != Vector3.Zero)
        {
            victim.TwistHead(contactTwist); // 頭を打たれたら、拳と同じく、頭がねじられた速さで意識が飛ぶ（自分の棍棒でも）
        }

        if (selfHit is { } own)
        {
            SelfWeaponHits.Add(own with { HeadTwist = part.Kind == BodyPartKind.Head ? victim.LastHeadTwist : 0f });
        }

        var fatal = false;
        if (hit.RemovedB.Count > 0 && victim.Voxels is { } body)
        {
            if (part.FragmentOf is null && body.SeverIfBroken(part.Body.BodyId) is { } fragment)
            {
                victim.Sever(part.Body.BodyId, fragment.Body);
                UntouchedByWeaponBoxes(fragment.Body.BodyId);
                ShowMessage(fragment.Kind is BodyPartKind.UpperArm or BodyPartKind.Forearm ? "ARM OFF!" : "LEG OFF!", 1.2f);
            }

            if (!victim.IsDead && body.DeathCause() is { } cause)
            {
                victim.Die(cause);
                fatal = true;
                _impacts.Spawn(hit.Point, hit.Direction, 1f, FatalFlash, Jitter);
            }
        }

        // 刃で斬った肉と骨は、刃が振り抜けた向きへ、刃に沿って扇に広がって飛ぶ（血しぶきのように）。刃の無い武器は拳と同じ散り方。
        Shatter(hit, weapon.Body, -hit.Normal, part.Body, hit.Normal, fatal, Weapon.IsBlunt(weapon.Kind) ? null : weapon);
    }

    /// <summary>
    /// グローブが相手の体に当たった: 拳の打撃（<see cref="HitEvent"/>）として数える。拳の速さ（歩の前）の、当たる向きの分が打撃の速さ、
    /// 乗る質量は拳の有効質量（<see cref="Fighter.ImpactMass(Hand, Vector3)"/>）。ダメージが小さすぎる触れ合い・押し合いは数えない（拳の打撃と同じ）。
    /// 体を削るのはボクセル同士の接触が壊した分だけ（エネルギーで掘る近道は使わない）。数えたら true。
    /// </summary>
    private bool Punch(Fighter attacker, Hand hand, Fighter victim, VoxelPart part, VoxelContactResult hit, Vector3 contactTwist)
    {
        if (RoundOver || victim.State != FighterState.Standing || _hitCooldown[(attacker, hand)] > 0f)
        {
            return false;
        }

        var velocity = _preStepGloveVelocity[(attacker, hand)];
        var speed = MathF.Max(0f, Vector3.Dot(velocity, hit.Direction));
        if (speed <= 0f)
        {
            return false;
        }

        var impactMass = attacker.ImpactMass(hand, velocity, out var bodyShare);
        var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
        var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
        var damage = PunchMechanics.Damage(impactMass * speed) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
        if (damage < MinimumDamage)
        {
            return false;
        }

        _hitCooldown[(attacker, hand)] = RehitCooldown;
        ApplyHit(new HitEvent(attacker, hand, victim, zone, hit.Point, speed, impactMass, bodyShare, damage), part.Body.BodyId, velocity, carve: false, alsoTwist: contactTwist);
        return true;
    }

    /// <summary>
    /// 刃が振り抜けた所の、斬った側の動き: 当たった所の刃の速さ（打たれた部位に対して、歩の前の姿勢から）と、刃の向き（ワールド）。
    /// </summary>
    private (Vector3 Swing, Vector3 Blade) SwingAt(Weapon weapon, VoxelBody struck, Vector3 point)
    {
        var w = _beforeStep.TryGetValue(weapon.Body.BodyId, out var before) ? before : _world.GetState(weapon.Body.BodyId);
        var p = _world.GetState(struck.BodyId);
        var swing = (w.LinearVelocity + Vector3.Cross(w.AngularVelocity, point - w.Position)) - (p.LinearVelocity + Vector3.Cross(p.AngularVelocity, point - p.Position));
        return (swing, Vector3.Transform(Vector3.UnitX, w.Orientation));
    }

    /// <summary>
    /// 武器の打撃で潰して掘る量（肉 1 ボクセル = 1）。拳と同じく運動エネルギーに比例するが、エネルギーは運んだ運動量 p と当たる速さ v から
    /// ½ p v（梃子で速い武器の先ほど大きい）。グローブの詰め物は当たる面を広げて時間を延ばすが、素の木や鉄は狭い面へ一気に
    /// 掛けるので、同じエネルギーで <see cref="FighterTuning.WeaponCarveScale"/> 倍深く掘る（上限も同じ倍率）。
    /// </summary>
    private float BluntCarve(float momentum, float contactSpeed)
    {
        var energy = 0.5f * momentum * contactSpeed * PunchMechanics.Sharpness(contactSpeed);
        return MathF.Min(_tuning.MaxCarve, energy * _tuning.CarvePerJoule) * _tuning.WeaponCarveScale;
    }

    private void Snapped(Weapon from, VoxelBody? piece)
    {
        if (piece is null)
        {
            return;
        }

        Track(Weapon.PieceOf(from.Kind, piece));
        ShowMessage("SNAP!", 1f);
    }

    /// <summary>
    /// 接触で壊れたセルを、その材質の色の破片にして飛ばす（体の削れと同じ <see cref="SpawnDebris"/>）。破片は壊れた側の面から外へ、
    /// 切る向きに流れて飛ぶ。砕ける音の粒も 1 セル 1 粒。
    /// 刃のある武器（<c>blade</c>）が体を斬ったときは、体の破片を振り抜けた向きへ飛ばす（<see cref="SliceOff"/>）。
    /// </summary>
    private void Shatter(VoxelContactResult hit, VoxelBody a, Vector3 outOfA, VoxelBody b, Vector3 outOfB, bool fatal, Weapon? blade = null)
    {
        var removed = hit.RemovedA.Count + hit.RemovedB.Count;
        if (removed == 0 && !fatal)
        {
            return;
        }

        var strength = Math.Clamp(removed / 80f, 0.15f, 1f);
        var chipSpeed = _tuning.ChipSpeed * (0.5f + (1.3f * strength));
        _chips.Clear();
        AddChips(a, hit.RemovedA, outOfA, hit.Direction, chipSpeed);
        var materials = new List<byte>();
        if (blade is not null)
        {
            // 刃で斬った体: つながって剥がれた所は塊で、振り抜けた向きへ（SliceOff）。武器の側の欠片だけ、ふつうに散らす。
            var (swing, axis) = SwingAt(blade, b, hit.Point);
            SliceOff(b, hit.RemovedB, swing, axis, outOfB, chipSpeed);
            materials.AddRange(hit.RemovedB.Select(v => v.Material));
            if (_chips.Count > 0)
            {
                SpawnDebris(_chips, hit.Point, hit.Direction, strength, attacker: null, recoil: Vector3.Zero);
            }
        }
        else
        {
            AddChips(b, hit.RemovedB, outOfB, hit.Direction, chipSpeed);
            SpawnDebris(_chips, hit.Point, hit.Direction, strength, attacker: null, recoil: Vector3.Zero);
        }

        materials.AddRange(_chips.Select(c => c.Material));
        var grains = new ShatterGrain[materials.Count];
        for (var i = 0; i < grains.Length; i++)
        {
            grains[i] = new ShatterGrain(materials[i], MathF.Min(0.3f, (i * 0.0035f) + (0.002f * (1f + Jitter()))));
        }

        _recentShatters.Add(new ShatterEvent(hit.Point, grains, fatal));
    }

    /// <summary>
    /// 刃のひと太刀で剥がれたボクセル: 格子の上で面でつながっているものは 1 つの塊になって剥がれ、刃が振り抜けた向き（<paramref name="swing"/>）へ、
    /// 刃の速さの一部をもらって飛ぶ（向きのばらつきは小さく: 刃の面に沿って ±10°、面の外へは ±4°）。塊は刃の進む向きへ前転するように回る。
    /// 1 つだけで剥がれたボクセルと、塊の縁から少しだけ欠ける細かい欠片は、同じ向きへ、少し速く。血は塊から振り抜けた向きへ垂れる。
    /// 拳の削れのような放射状の散り方・当たった所の光の輪はない。
    /// </summary>
    private void SliceOff(VoxelBody body, IReadOnlyList<RemovedVoxel> removed, Vector3 swing, Vector3 blade, Vector3 outward, float chipSpeed)
    {
        if (removed.Count == 0)
        {
            return;
        }

        var speed = swing.Length();
        var dir = speed > 0.2f ? swing / speed : -outward;
        var across = blade - (Vector3.Dot(blade, dir) * dir);
        across = across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : Vector3.Normalize(Vector3.Cross(dir, outward) + new Vector3(1e-4f));
        var side = Vector3.Cross(dir, across);
        var s = _world.GetState(body.BodyId);
        var toWorld = body.GridToWorld(s);
        var cell = body.Grid.CellSize;
        var fling = MathF.Max(chipSpeed, speed * SprayShare);
        Vector3 Heading(float spreadAcross, float spreadSide) => Vector3.Normalize(dir + (across * (spreadAcross * Jitter())) + (side * (spreadSide * Jitter())));
        Vector3 PointVelocity(Vector3 p) => s.LinearVelocity + Vector3.Cross(s.AngularVelocity, p - s.Position);

        // 面でつながったものを 1 つにまとめる（格子の上の整数の座標で、6 近傍）。
        var keys = removed.Select(v => (X: (int)MathF.Round(v.LocalCenter.X / cell.X), Y: (int)MathF.Round(v.LocalCenter.Y / cell.Y), Z: (int)MathF.Round(v.LocalCenter.Z / cell.Z))).ToArray();
        var index = new Dictionary<(int X, int Y, int Z), int>();
        for (var i = 0; i < keys.Length; i++)
        {
            index.TryAdd(keys[i], i);
        }

        var group = new int[keys.Length];
        Array.Fill(group, -1);
        var groups = new List<List<int>>();
        for (var i = 0; i < keys.Length; i++)
        {
            if (group[i] >= 0)
            {
                continue;
            }

            var members = new List<int> { i };
            group[i] = groups.Count;
            for (var m = 0; m < members.Count; m++)
            {
                var (x, y, z) = keys[members[m]];
                foreach (var n in new[] { (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1) })
                {
                    if (index.TryGetValue(n, out var j) && group[j] < 0)
                    {
                        group[j] = groups.Count;
                        members.Add(j);
                    }
                }
            }

            groups.Add(members);
        }

        var palette = BoxerAnatomy.Palette;
        foreach (var members in groups)
        {
            var center = members.Aggregate(Vector3.Zero, (sum, i) => sum + removed[i].LocalCenter) / members.Count;
            var position = Vector3.Transform(center, toWorld);
            var velocity = PointVelocity(position) + (Heading(0.18f, 0.07f) * fling * (0.8f + (0.3f * Jitter())));
            if (members.Count == 1)
            {
                Single(removed[members[0]], position, velocity, 1f);
                continue;
            }

            var cells = members.Select(i =>
            {
                var v = removed[i];
                var material = palette[v.Material];
                return new DebrisCell(v.LocalCenter - center, v.WasSurface ? material.SurfaceColor : material.Color, 0);
            }).ToArray();
            // 刃の進む向きへ前転する（刃は塊の、刃に近い側を押し出す）。
            var spin = Vector3.Cross(outward, dir) * (speed * 25f);
            _debris.SpawnChunk(position, velocity, s.Orientation, cell, cells, spin, tag: removed[members[0]].Material);
            // 塊の縁から欠ける細かい欠片（塊 4 つに 1 つ、多くて 3 つ）と、肉からの血。
            for (var k = 0; k < Math.Min(3, members.Count / 4); k++)
            {
                Single(removed[members[k * members.Count / 3 % members.Count]], position, PointVelocity(position) + (Heading(0.25f, 0.1f) * fling * 1.15f), 0.5f);
            }

            var flesh = members.Count(i => palette[removed[i].Material].Tissue is VoxelTissue.Flesh or VoxelTissue.Organ);
            for (var k = 0; k < Math.Min(4, flesh / 3); k++)
            {
                _debris.Spawn(position, PointVelocity(position) + (Heading(0.2f, 0.1f) * fling * (0.9f + (0.2f * Jitter()))), s.Orientation, cell * 0.35f,
                    new Vector3(Jitter(), Jitter(), Jitter()) * 40f, BloodColor, life: 2.2f, tag: VoxelBreakSound.BloodTag);
            }
        }

        void Single(RemovedVoxel v, Vector3 at, Vector3 velocity, float size)
        {
            var material = palette[v.Material];
            var color = material.Tissue == VoxelTissue.Organ ? OrganSpark : v.WasSurface ? material.SurfaceColor : material.Color;
            var trail = material.Tissue == VoxelTissue.Bone ? BoneTrail : ChipTrail;
            _debris.Spawn(at, velocity, s.Orientation, cell * (0.9f * size), new Vector3(Jitter(), Jitter(), Jitter()) * 30f, color, glow: material.Tissue == VoxelTissue.Organ,
                tag: v.Material, heat: ChipHeat, trail: trail, ownColor: true);
        }
    }

    /// <summary>刃で斬られた欠片が、刃の速さのどれだけをもらって飛ぶか（欠片の飛ぶ速さは、描くときにさらに 1.5〜2 倍される）。</summary>
    private const float SprayShare = 0.35f;

    private void AddChips(VoxelBody body, IReadOnlyList<RemovedVoxel> removed, Vector3 outward, Vector3 along, float chipSpeed)
    {
        if (removed.Count == 0)
        {
            return;
        }

        var s = _world.GetState(body.BodyId);
        var toWorld = body.GridToWorld(s);
        foreach (var v in removed)
        {
            var position = Vector3.Transform(v.LocalCenter, toWorld);
            var scatter = new Vector3(Jitter(), Jitter(), Jitter());
            var heading = (outward * 0.6f) + (along * 0.5f) + (scatter * 0.35f);
            var velocity = s.LinearVelocity + Vector3.Cross(s.AngularVelocity, position - s.Position)
                + (Vector3.Normalize(heading + new Vector3(1e-4f)) * chipSpeed * (1f + (0.25f * Jitter())));
            _chips.Add(new VoxelChip(position, velocity, s.Orientation, body.Grid.CellSize, v.Material, v.WasSurface));
        }
    }
}

/// <summary>握られた武器が相手に当てた 1 打（押し当てているだけの接触は含まない。得点・研究用）。</summary>
/// <param name="Attacker">振った側。</param>
/// <param name="Victim">打たれた側。</param>
/// <param name="Part">打たれた部位。</param>
/// <param name="Zone">急所の分類（ガードの上か、あご・こめかみか、など）。</param>
/// <param name="Point">当たった所。</param>
/// <param name="Cells">壊したセルの数。</param>
/// <param name="Speed">当たった所の武器の速さ [m/s]（打つ向き）。</param>
/// <param name="Momentum">相手へ運んだ運動量 [N·s]（体の重さの分 + 武器そのものの分）。</param>
/// <param name="HeadTwist">頭を打ったなら、頭がねじられた速さ [rad/s]（意識の飛び方。拳と同じ）。</param>
/// <param name="Damage">得点（拳と同じ物差し）。</param>
public readonly record struct WeaponHitEvent(Fighter Attacker, Fighter Victim, BodyPartKind Part, HitZone Zone, Vector3 Point, int Cells, float Speed, float Momentum, float HeadTwist, float Damage);
