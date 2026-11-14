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
    private readonly List<ContactSoundEvent> _recentContacts = [];

    /// <summary>この速さ [m/s] で近づいてぶつかったとき、当たった音がいちばん強い（拳の速い所）。</summary>
    private const float ContactFullSpeed = 4f;

    /// <summary>これより遅く近づいた触れ合い [m/s] は鳴らさない（押し当てているだけ・構えた拳が触れ合っているだけ）。</summary>
    private const float ContactSoundMinSpeed = 0.5f;
    private readonly Dictionary<Weapon, Fighter> _wielders = [];
    private readonly Dictionary<Weapon, Hand> _wieldHand = [];
    private readonly Dictionary<Weapon, float> _weaponCooldown = [];
    private readonly Dictionary<Weapon, float> _selfCooldown = [];

    /// <summary>体の重さを運ぶ打撃と数える、当たる所の速さの下限 [m/s]（押し当てているだけの接触は数えない）。</summary>
    private const float MinimumBlowSpeed = 0.8f;

    /// <summary>
    /// 1 本の武器が体の部位に触れて押し戻され、別の部位の中へ入ったとき、同じ物理の 1 歩のうちに掃き直す回数の上限。
    /// 肩・腰など部位が接する所で、押し戻された刃が隣の部位に入っても、2〜3 回で落ち着く（調べた: <c>docs/arena/tunnel_*.md</c>）。
    /// </summary>
    public int MaxContactPasses { get; set; } = DefaultMaxContactPasses;

    /// <summary><see cref="MaxContactPasses"/> の既定（3。比べものの調べ（掃き直し 1 回 = 以前）のために変えられる）。</summary>
    public static int DefaultMaxContactPasses { get; set; } = 3;

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
    public Weapon? WeaponOf(Fighter fighter) => _wielders.FirstOrDefault(kv => kv.Value == fighter && !Weapon.IsWorn(kv.Key.Kind)).Key;

    /// <summary>
    /// <paramref name="fighter"/> が手 <paramref name="hand"/> にはめている物（グローブ・素手の拳・メリケンサック・カタール・籠手。
    /// <see cref="Weapon.IsWorn"/>。はめていなければ null）。
    /// </summary>
    public Weapon? GloveOf(Fighter fighter, Hand hand) =>
        _wielders.FirstOrDefault(kv => kv.Value == fighter && Weapon.IsWorn(kv.Key.Kind) && _wieldHand[kv.Key] == hand).Key;

    /// <summary>
    /// <paramref name="fighter"/> の、武器を握っていない手に、<paramref name="wear"/>（既定はボクセルのグローブ。革はコーナーの色）をはめる。
    /// 拳の打撃は、はめた物と相手の体のボクセル同士の接触（圧力のモデル）で起きる。どれも同じ道を通り、違いはボクセルの形と材質だけ。
    /// </summary>
    public void Wear(Fighter fighter, HandWear wear = HandWear.Glove)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        var kind = wear.Kind();
        foreach (var hand in Hands)
        {
            if (fighter.ArmSevered(hand) || fighter.Wields(hand) || fighter.WearsGlove(hand))
            {
                continue;
            }

            var leather = fighter == Player ? BoxerAnatomy.RedGlove : BoxerAnatomy.BlueGlove;
            var worn = Weapon.CreateWorn(_world, kind, leather, fighter.GloveCenter(hand), Quaternion.Identity);
            Track(worn);
            fighter.Wield(hand, worn.Body.BodyId, Weapon.GripOf(kind), glove: true);
            _wielders[worn] = fighter;
            _wieldHand[worn] = hand;
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

        var held = WeaponOf(self) is { } mine && HolderOf(mine) == self ? mine : null;
        var own = held is null ? default : OwnWeaponSense(held, self);
        return sense with { OpponentBlade = blade, SelfArmed = held is not null, SelfWeapon = own };
    }

    /// <summary>握っている武器の見え方: 長さと、今実際にある握りの位置・向き（手に感じる剣）。</summary>
    private WeaponSense OwnWeaponSense(Weapon held, Fighter holder)
    {
        var state = _world.GetState(held.Body.BodyId);
        var grip = (holder.WieldedGrip(HandOf(held)) ?? Weapon.GripOf(held.Kind));
        return new WeaponSense(true, ReachOf(held, holder), FullReachOf(held, holder),
            state.Position + Vector3.Transform(grip, state.Orientation), Vector3.Transform(Vector3.UnitX, state.Orientation));
    }

    /// <summary>武器ごとの、いちばん先のセルの端（武器のローカル X。格子の版が変わったときだけ数え直す）。</summary>
    private readonly Dictionary<Weapon, (int Version, float Reach)> _reach = [];

    /// <summary>武器ごとの、作ったときのいちばん先のセルの端（武器のローカル X）。尖った先の細る所はセルにならないので、形の先ではなくセルで測る。</summary>
    private readonly Dictionary<Weapon, float> _fullReach = [];

    /// <summary><paramref name="holder"/> が握っている武器の、作ったときの握る所から先の端までの長さ [m]（<see cref="Track"/> で測った）。</summary>
    private float FullReachOf(Weapon weapon, Fighter holder) =>
        MathF.Max(0f, _fullReach.GetValueOrDefault(weapon, FarEdge(weapon)) - (holder.WieldedGrip(HandOf(weapon)) ?? Weapon.GripOf(weapon.Kind)).X);

    /// <summary>武器の今のいちばん先のセルの端（武器のローカル X）。</summary>
    private static float FarEdge(Weapon weapon)
    {
        var grid = weapon.Body.Grid;
        var far = float.MinValue;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                var (x, y, z) = grid.Coordinates(i);
                far = MathF.Max(far, weapon.Body.GridCenterLocal.X + grid.CellCenter(x, y, z).X + (0.5f * grid.CellSize.X));
            }
        }

        return far;
    }

    /// <summary>
    /// <paramref name="holder"/> が握っている武器 <paramref name="weapon"/> の、握る所から先の端（武器のローカル +X: 打つ側の先）までの今の長さ [m]。
    /// 折れて先が飛んだ武器は、残ったセルのうちいちばん先のものまで。
    /// </summary>
    private float ReachOf(Weapon weapon, Fighter holder)
    {
        var grid = weapon.Body.Grid;
        var grip = (holder.WieldedGrip(HandOf(weapon)) ?? Weapon.GripOf(weapon.Kind)).X;
        if (!_reach.TryGetValue(weapon, out var known) || known.Version != grid.Version)
        {
            known = (grid.Version, FarEdge(weapon));
            _reach[weapon] = known;
        }

        return MathF.Max(0f, known.Reach - grip);
    }

    /// <summary>その物が付いているファイター: 握っている人か、力の抜けた手（<see cref="Fighter.DeadHandOn"/>）がまだ持っている人（無ければ null）。</summary>
    private Fighter? OwnerOf(Weapon weapon) => HolderOf(weapon) ?? (_wielders.TryGetValue(weapon, out var f) && f.DeadHandOn(weapon.Body.BodyId) is not null ? f : null);

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
        // 打つ面が柔らかい物（グローブの革・素手の皮膚）は押し込まない: 腕が押し付ける力は詰め物や肉が広い面で受けて散らす（圧力のモデルは
        // 表の面だけを見るので、その下で押す腕の力を数えると、構えた拳を押し付けているだけで肌が削れてしまう）。硬い面（鉄・木）は伝える。
        if (HolderOf(weapon) is not { } holder || !Weapon.HasRigidFace(weapon.Kind))
        {
            return Vector3.Zero;
        }

        var hand = HandOf(weapon);
        var force = holder.HandPush(hand);
        return holder.SupportOf(hand) is { } support ? force + holder.HandPush(support) : force;
    }

    /// <summary>
    /// 手にはめた物の後ろで一緒に打ち込む質量 [kg]（<see cref="VoxelContact.Resolve"/> の massBehind）: 拳の有効質量
    /// （<see cref="Fighter.ImpactMass(Hand, Vector3)"/>: 腕と、拳の向きに乗る体の分）。ボクセルの接触は関節を見ないので、渡さないと
    /// 45 g のグローブそのものの運動エネルギー（2 m/s で 0.1 J、押す力は数 N）でしか打たない（測った: グローブの拳は 1〜7 N で、何も壊さなかった）。
    /// 握る武器は渡さない（腕は押し込む力 <see cref="Drive(Weapon)"/> で効いている。武器の調整はそれで合わせてある。CLAUDE.md §8）。
    /// </summary>
    private float MassBehind(Weapon weapon)
    {
        if (!Weapon.IsWorn(weapon.Kind) || HolderOf(weapon) is not { } holder)
        {
            return 0f;
        }

        var hand = HandOf(weapon);
        return _preStepGloveVelocity.TryGetValue((holder, hand), out var velocity) ? holder.ImpactMass(hand, velocity) : 0f;
    }

    /// <summary>部位 <paramref name="bodyId"/> が、<paramref name="fighter"/> の武器を握っている（添えに行っている）手の前腕か。</summary>
    private static bool IsGripping(Fighter fighter, int bodyId) =>
        Hands.Any(h => (fighter.Wields(h) || fighter.WearsGlove(h)) && fighter.ForearmId(h) == bodyId);

    /// <summary>直近の <see cref="Step"/> で、握った武器が自分の体を打った打撃（得点にはならない。<see cref="WeaponHits"/> には入れない）。</summary>
    public List<WeaponHitEvent> SelfWeaponHits { get; } = [];

    /// <summary>握った武器が握った本人の体にも当たるか（既定 true。調べもので切って、自分の体が剣の道を塞いでいるかを見る）。</summary>
    public bool WeaponsHitHolder { get; set; } = true;

    /// <summary>直近の <see cref="Step"/> で、握った武器が握った本人の体の部位に触れた接触の全て（調べもの用。打撃の速さの下限・間の取り方に関わらず、削れたセルの数を添える）。</summary>
    public List<SelfTouchEvent> SelfTouches { get; } = [];

    /// <summary>直近の <see cref="Step"/> で、握られた武器が相手の体を壊した打撃。</summary>
    public List<WeaponHitEvent> WeaponHits { get; } = [];

    /// <summary>世界に置いた武器（折れて飛んだ破片も 1 本として加わる）。</summary>
    public IReadOnlyList<Weapon> Weapons => _weapons;

    /// <summary>直近の <see cref="Step"/> で武器が触れた接触（切った・潰した・弾いた）。</summary>
    public IReadOnlyList<VoxelContactResult> RecentCuts => _recentCuts;

    /// <summary>直近の <see cref="Step"/> で、ボクセルの物どうしが何も壊さずにぶつかった瞬間（当たった音。物の組ごとに 1 つ、いちばん強いもの）。</summary>
    public IReadOnlyList<ContactSoundEvent> RecentContacts => _recentContacts;

    /// <summary>
    /// 何も壊さなかった接触を、当たった音として残す（物の組の材質から音の種類、近づく速さから強さ）。同じ 2 つの物の音は 1 回の
    /// <see cref="Step"/> に 1 つ（細かい歩ごとに触れ直しても重ねない）。壊れた接触は砕ける音・斬る音が鳴るので、ここでは鳴らさない。
    /// </summary>
    private void NoteContact(VoxelContactResult hit)
    {
        if (!hit.Touched || !hit.BrokeNothing || hit.ApproachSpeed < ContactSoundMinSpeed || ContactPairs.Of(hit.FaceA, hit.FaceB) is not { } pair)
        {
            return;
        }

        var bodies = (hit.A.BodyId, hit.B.BodyId);
        if (_contactQuiet.GetValueOrDefault(bodies) > 0f)
        {
            return; // 同じ 2 つの物が、鳴ったばかり（押し当てたまま・跳ね返って当たり直す）
        }

        var strength = Math.Clamp(hit.ApproachSpeed / ContactFullSpeed, 0f, 1f);
        for (var i = 0; i < _recentContacts.Count; i++)
        {
            if (_recentContactBodies[i] == (hit.A.BodyId, hit.B.BodyId))
            {
                if (strength > _recentContacts[i].Strength)
                {
                    _recentContacts[i] = ContactAudioOf(hit, pair, strength);
                }

                return;
            }
        }

        _recentContacts.Add(ContactAudioOf(hit, pair, strength));
        _recentContactBodies.Add(bodies);
        _contactQuiet[bodies] = ContactQuietSeconds;
    }

    private ContactSoundEvent ContactAudioOf(VoxelContactResult hit, ContactPair pair, float strength) =>
        new(hit.Point, pair, strength, ResonanceOf(hit), 0.5f * MathF.Abs(Vector3.Dot(hit.ImpulseOnB, hit.Normal)) * hit.ApproachSpeed);

    // 音のメタデータだけを読む。剛体・材質・乱数・チェックサムへの書き込みはない。
    private ContactResonance ResonanceOf(VoxelContactResult hit)
    {
        ContactResonance Shape(VoxelBody body)
        {
            var half = body.Grid.OccupiedBounds()?.HalfExtents ?? body.Grid.HalfExtents;
            var dimensions = new[] { half.X * 2, half.Y * 2, half.Z * 2 }.OrderDescending().ToArray();
            var supported = _wielders.Keys.Any(w => w.Body == body && OwnerOf(w) is not null);
            return ContactResonance.Of(dimensions[0], dimensions[1], dimensions[2], MathF.Max(1e-6f, _world.GetMass(body.BodyId)), supported);
        }
        return new ContactResonance(MathF.Max(Shape(hit.A).DecayScale, Shape(hit.B).DecayScale));
    }

    /// <summary>同じ 2 つの物の当たった音を、続けては鳴らさない間 [s]（1 回の当たりの中で跳ね返って触れ直す時間より長く、続けて打つ間より短く）。</summary>
    private const float ContactQuietSeconds = 0.15f;

    /// <summary>物の組（剛体 id の組）ごとの、当たった音を鳴らさない残りの時間 [s]。</summary>
    private readonly Dictionary<(int, int), float> _contactQuiet = [];

    /// <summary>当たった音を鳴らさない時間を <paramref name="dt"/> だけ進める（<see cref="Step"/> の初めに 1 度）。</summary>
    private void QuietContacts(float dt)
    {
        foreach (var key in _contactQuiet.Keys.ToList())
        {
            var left = _contactQuiet[key] - dt;
            if (left > 0f)
            {
                _contactQuiet[key] = left;
            }
            else
            {
                _contactQuiet.Remove(key);
            }
        }
    }

    /// <summary><see cref="_recentContacts"/> と同じ並びの、ぶつかった 2 つの物の剛体 id。</summary>
    private readonly List<(int, int)> _recentContactBodies = [];

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
        if (!Weapon.IsWorn(weapon.Kind))
        {
            _fullReach[weapon] = FarEdge(weapon); // 作ったときの長さ（折れた分を測る物差し）
        }

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
                // 同じ人の両手にはめた物は打ち合わない（構えでいつも触れ合っている）。
                if (Weapon.IsWorn(weapon.Kind) && Weapon.IsWorn(other.Kind) && OwnerOf(weapon) is { } h && OwnerOf(other) == h)
                {
                    continue;
                }

                if (_beforeStep.TryGetValue(other.Body.BodyId, out var otherBefore) && MayTouch(weapon.Body, before, other.Body, otherBefore))
                {
                    // 物どうし（武器・はめる物）は面の下を押さない: どちらが「場」になるかは格子の粗さだけで決まり、詰め物や刃の内側は固い物として扱う。
                    Clash(weapon, other, VoxelContact.Resolve(weapon.Body, before, other.Body, otherBefore, Drive(weapon), Drive(other), MassBehind(weapon), MassBehind(other), pressBeneath: false));
                }
            }

            var drive = Drive(weapon);
            var behind = MassBehind(weapon);
            // 部位は 1 つずつ解くので、ある部位の接触が刃を押し戻すと、その先で別の部位（すでに解いた部位）の中へ入ることがある（肩の上腕と胸のつなぎ目など、
            // 部位が接している所）。そのまま歩を終えると、刃が体の中にあるのに何も起きない 1 歩になる。刃が動いたら、まだ触れていない相手の部位だけを、
            // 握りの押す力（drive・massBehind）を数えずに掃き直す（同じ部位を 2 度打たない・腕の力を 2 度払わない。<see cref="MaxContactPasses"/> まで。握った本人の体は 1 回だけ）。
            // 手にはめた物（グローブなど）は 1 回だけ（拳はいつも相手の体に押し当てて動くので、掃き直すと拳の打撃の手触りまで変わる。調べたのは刃・棍棒）。
            var passes = Weapon.IsWorn(weapon.Kind) ? 1 : MaxContactPasses;
            var touched = new HashSet<VoxelPart>();
            for (var pass = 0; pass < passes; pass++)
            {
                var poseBefore = _world.GetState(weapon.Body.BodyId);
                ResolveBodies(weapon, before, pass == 0 ? drive : default, pass == 0 ? behind : 0f, includeHolder: pass == 0, touched);
                var poseNow = _world.GetState(weapon.Body.BodyId);
                if (poseNow.Position == poseBefore.Position && poseNow.Orientation == poseBefore.Orientation)
                {
                    break;
                }
            }
        }
    }

    /// <summary>武器 <paramref name="weapon"/> を、全ての体の部位に対して歩の前の姿勢から今の姿勢まで掃く（1 回）。</summary>
    private void ResolveBodies(Weapon weapon, BodyState before, Vector3 drive, float behind, bool includeHolder, HashSet<VoxelPart> touched)
    {
        foreach (var fighter in Fighters)
        {
            // 握った本人の体にも当たる（振りかぶった棍棒が自分の頭を打つことも）。握っている手の前腕だけは除く: 拳と武器は
            // 玉継手でつながっていて、握りはいつも前腕の先に触れている。
            var holds = HolderOf(weapon) == fighter;
            if (holds && !includeHolder)
            {
                continue; // 掃き直しは相手の体だけ（握った本人の体は、握りに近く、毎歩どこかが触れて押し戻すので、掃き直すと握った刃が自分の体に押し返され続ける）
            }

            // 力の抜けた手（腕が切れた・折れた）がまだ握っている・はめている物も、その人の手に付いている。
            var deadHand = fighter.DeadHandOn(weapon.Body.BodyId);
            // 手にはめた物は自分の体を打たない（構えた拳はいつも自分の顔に触れている。自分を殴る力は乗らない）。
            if ((holds || deadHand is not null) && (!WeaponsHitHolder || Weapon.IsWorn(weapon.Kind)))
            {
                continue;
            }

            foreach (var part in VoxelPartsOf([fighter]).ToList())
            {
                if ((holds && IsGripping(fighter, part.Body.BodyId)) || deadHand?.Hand == part.Body.BodyId || (!includeHolder && touched.Contains(part)))
                {
                    continue; // 握りはいつも手の剛体に触れている
                }

                if (_beforeStep.TryGetValue(part.Body.BodyId, out var partBefore) && MayTouch(weapon.Body, before, part.Body, partBefore))
                {
                    var hit = VoxelContact.Resolve(weapon.Body, before, part.Body, partBefore, drive, massBehindA: behind);
                    if (hit.Touched)
                    {
                        touched.Add(part);
                    }

                    Cut(weapon, fighter, part, hit);
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
        NoteContact(hit);
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
        if (HolderOf(weapon) == victim && part.FragmentOf is null)
        {
            SelfTouches.Add(new SelfTouchEvent(victim, weapon, part.Kind, hit.RemovedB.Count, hit.Point));
        }

        // 物理の接触が頭を回した分（棍棒そのものの運動量）。
        var contactTwist = Vector3.Zero;
        if (part.Kind == BodyPartKind.Head && part.FragmentOf is null && hit.ImpulseOnB.LengthSquared() > 0f)
        {
            var head = _world.GetState(part.Body.BodyId);
            contactTwist = Vector3.Cross(hit.Point - head.Position, hit.ImpulseOnB);
        }

        // 手にはめた物の打撃は拳の打撃（パンチ）: 拳と同じ物差しで数え、体の重さを運ぶ。壊れるのはボクセル同士の接触が壊した分だけ
        // （カタールの刃が斬った所も、メリケンサックの突起が潰した所も、同じ接触が壊した分）。
        if (Weapon.IsWorn(weapon.Kind))
        {
            if (!(part.FragmentOf is null && HolderOf(weapon) is { } puncher && puncher != victim && Punch(puncher, HandOf(weapon), victim, part, hit, contactTwist)))
            {
                if (contactTwist != Vector3.Zero && victim.State == FighterState.Standing)
                {
                    victim.TwistHead(contactTwist);
                }

                NoteContact(hit); // 拳の打撃として数えた当たりは打撃音が鳴る。数えない触れ合い（軽い当たり）だけ当たった音
            }

            Wound(weapon, victim, part, hit);
            return;
        }

        // 握った武器の打撃は、拳の打撃と同じく、拳の後ろの体の重さも運ぶ（武器は梃子で速さを、体は重さを足す）。1 振りに 1 度。
        var carried = false;
        if (!RoundOver && !Holding && part.FragmentOf is null && HolderOf(weapon) is { } swinger && swinger != victim && victim.State == FighterState.Standing
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
                    delivered, part.Kind == BodyPartKind.Head ? victim.LastHeadTwist : 0f, damage, hit.FaceA,
                    ContactPairs.Of(hit.FaceA, hit.FaceB) is { } pair ? ContactAudioOf(hit, pair, Math.Clamp(contactSpeed / ContactFullSpeed, 0f, 1f)) : null));
            }
        }

        WeaponHitEvent? selfHit = null;
        // 自分の体を打った: 体の重さは乗らない（自分の体の中で押し合うだけ）。武器そのものの運動量で、相手を打つときと同じく潰して掘る。
        if (!RoundOver && !Holding && part.FragmentOf is null && HolderOf(weapon) == victim && _selfCooldown.GetValueOrDefault(weapon) <= 0f
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

                selfHit = new WeaponHitEvent(victim, victim, part.Kind, zone, hit.Point, hit.RemovedB.Count + crushed, contactSpeed, delivered, 0f, damage, hit.FaceA,
                    ContactPairs.Of(hit.FaceA, hit.FaceB) is { } pair ? ContactAudioOf(hit, pair, Math.Clamp(contactSpeed / ContactFullSpeed, 0f, 1f)) : null);
            }
        }

        if (!carried && selfHit is null)
        {
            NoteContact(hit); // 握った武器が体に当たって何も壊さなかった（刃の腹・止められた棍棒）
        }

        if (!carried && contactTwist != Vector3.Zero)
        {
            victim.TwistHead(contactTwist); // 頭を打たれたら、拳と同じく、頭がねじられた速さで意識が飛ぶ（自分の棍棒でも）
        }

        if (selfHit is { } own)
        {
            SelfWeaponHits.Add(own with { HeadTwist = part.Kind == BodyPartKind.Head ? victim.LastHeadTwist : 0f });
        }

        Wound(weapon, victim, part, hit);
    }

    /// <summary>
    /// 骨が断たれた・折れた（<see cref="BoneBreak"/>）: 体の関節を付け替え（<see cref="Fighter.Break"/>）、落ちた先は武器と箱で当たらないようにし、
    /// 知らせる。肉がまだつないでいれば「折れた」、断たれていれば「落ちた」。
    /// </summary>
    internal void BreakBone(Fighter victim, BoneBreak broken, float strength)
    {
        victim.Break(broken);
        var arm = broken.Part.Kind is BodyPartKind.UpperArm or BodyPartKind.Forearm;
        if (broken.Fragment is { } fragment)
        {
            UntouchedByWeaponBoxes(fragment.Body.BodyId);
        }

        if (broken.Hangs)
        {
            BrokenBones.Add(new BoneBreakEvent(victim, broken.Part.Kind, broken.Point, broken.Bent));
            ShowMessage(arm ? "ARM BROKEN!" : "LEG BROKEN!", 1.2f);
            return;
        }

        _recentSevers.Add(new SeverEvent(broken.Point, strength));
        ShowMessage(arm ? "ARM OFF!" : "LEG OFF!", 1.2f);
    }

    /// <summary>直近の <see cref="Step"/> で骨が折れて、肉だけでつながった所（切り落とされたものは <see cref="RecentSevers"/>）。</summary>
    public List<BoneBreakEvent> BrokenBones { get; } = [];

    /// <summary>
    /// 接触が体を壊した後: 骨が断たれたらその先を落とし、臓器が壊れたら死なせ、壊れたセルを散らす（刃のある物は振り抜けた向きへ、
    /// 刃の無い物は拳と同じ散り方）。握った武器も手にはめた物も同じ。
    /// </summary>
    private void Wound(Weapon weapon, Fighter victim, VoxelPart part, VoxelContactResult hit)
    {
        var fatal = false;
        // 打撃の曲げで骨が折れた（肉は断たれていないので、ふつうはぶら下がる）。削られて断たれたのと同じ道を通る。
        if (part.FragmentOf is null && hit.BentFor(part.Body) is { } bend && victim.Voxels is { } bentBody && bentBody.BreakBent(part.Body.BodyId, bend) is { } bent)
        {
            BreakBone(victim, bent, 1f);
        }

        if ((hit.RemovedB.Count > 0 || hit.CrushedFor(part.Body).Count > 0) && victim.Voxels is { } body) // 皮膚の下で潰れた分も（臓器が潰れれば死ぬことがある）
        {
            if (part.FragmentOf is null && body.BreakIfSevered(part.Body.BodyId) is { } broken)
            {
                BreakBone(victim, broken, 1f);
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
        if (RoundOver || Holding || victim.State != FighterState.Standing || _hitCooldown[(attacker, hand)] > 0f)
        {
            return false; // ダウンから立ち直った直後の一呼吸も、拳の打撃は数えない
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
        // 打った面の材質（はめた物の側）: 打撃音を、グローブ・素手・鉄で変える。
        var face = hit.A == part.Body ? hit.FaceB : hit.FaceA;
        ApplyHit(new HitEvent(attacker, hand, victim, zone, hit.Point, speed, impactMass, bodyShare, damage, face), part.Body.BodyId, velocity, carve: false, alsoTwist: contactTwist);
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
/// <param name="Face">打面の材質。表示・音専用。</param>
/// <param name="Sound">形状・支持・接触力積から読む音専用メタデータ。</param>
public readonly record struct WeaponHitEvent(Fighter Attacker, Fighter Victim, BodyPartKind Part, HitZone Zone, Vector3 Point, int Cells, float Speed, float Momentum, float HeadTwist, float Damage,
    byte Face = 0, ContactSoundEvent? Sound = null);

/// <summary>握った武器が握った本人の体に触れた 1 回（<see cref="BoxingGame.SelfTouches"/>）。<paramref name="Removed"/> は削れたセルの数。</summary>
public readonly record struct SelfTouchEvent(Fighter Fighter, Weapon Weapon, BodyPartKind Part, int Removed, Vector3 Point);
