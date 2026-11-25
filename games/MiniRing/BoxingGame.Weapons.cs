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
    private const float ContactFullSpeed = 6.714f;

    /// <summary>これより遅く近づいた触れ合い [m/s] は鳴らさない（押し当てているだけ・構えた拳が触れ合っているだけ）。</summary>
    private const float ContactSoundMinSpeed = 0.8392f;
    private readonly Dictionary<Weapon, Fighter> _wielders = [];
    private readonly Dictionary<Weapon, Hand> _wieldHand = [];
    private readonly Dictionary<Weapon, float> _weaponCooldown = [];
    private readonly Dictionary<Weapon, float> _selfCooldown = [];

    /// <summary>体の重さを運ぶ打撃と数える、当たる所の速さの下限 [m/s]（押し当てているだけの接触は数えない）。</summary>
    private const float MinimumBlowSpeed = 1.343f;

    /// <summary>
    /// 1 本の武器が体の部位に触れて押し戻され、別の部位の中へ入ったとき、同じ物理の 1 歩のうちに掃き直す回数の上限。
    /// 肩・腰など部位が接する所で、押し戻された刃が隣の部位に入っても、2〜3 回で落ち着く（調べた: <c>docs/arena/tunnel_*.md</c>）。
    /// </summary>
    public int MaxContactPasses { get; set; } = DefaultMaxContactPasses;

    /// <summary>
    /// 関節のつなぎ（<see cref="JointBridge"/>）を刃の接触に使うか。既定は使う。比べものの調べ（necklab・テスト）のために切れる（切っても体の運動は変わらない）。
    /// </summary>
    public bool JointBridges { get; set; } = DefaultJointBridges;

    /// <summary><see cref="JointBridges"/> の既定（true。簡素化の測定で切れる。非同期の流れごと: <see cref="DefaultSubstepRate"/> と同じ）。</summary>
    public static bool DefaultJointBridges
    {
        get => _defaultJointBridges.Value ?? true;
        set => _defaultJointBridges.Value = value;
    }

    private static readonly System.Threading.AsyncLocal<bool?> _defaultJointBridges = new();

    /// <summary><see cref="MaxContactPasses"/> の既定（3。比べものの調べ（掃き直し 1 回 = 以前）のために変えられる）。
    /// 非同期の流れごとに持つ（<see cref="DefaultSubstepRate"/> と同じ）。</summary>
    public static int DefaultMaxContactPasses
    {
        get => _defaultMaxContactPasses.Value ?? 3;
        set => _defaultMaxContactPasses.Value = value;
    }

    private static readonly System.Threading.AsyncLocal<int?> _defaultMaxContactPasses = new();

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

        if (_spawnGrace.Count > 0)
        {
            foreach (var weapon in _spawnGrace.Keys.ToList())
            {
                var left = _spawnGrace[weapon] - dt;
                if (left > 0f)
                {
                    _spawnGrace[weapon] = left;
                    continue;
                }

                // 構えに追いついた: まだ体に重なっている（近い）組だけを、一度離れるまで当てない。
                _spawnGrace.Remove(weapon);
                if (HolderOf(weapon) is { } holder)
                {
                    _ = MarkSpawnOverlaps(weapon, holder);
                }
            }
        }
    }

    /// <summary>武器を握る手（利き手）。</summary>
    public const Hand WeaponHand = Hand.Right;

    /// <summary><paramref name="weapon"/> を持っている（はめている）選手（誰も持っていなければ null）。描画が手の肌の色を持ち主から引くのに使う。</summary>
    public Fighter? WielderOf(Weapon weapon) => _wielders.TryGetValue(weapon, out var holder) ? holder : null;

    /// <summary><paramref name="fighter"/> が握っている武器（無ければ null）。</summary>
    public Weapon? WeaponOf(Fighter fighter)
    {
        // 毎歩何度も呼ばれるので、LINQ（クロージャと列挙子の割り当て）を使わず、辞書の順に最初の 1 つを返す（FirstOrDefault と同じ答え）。
        foreach (var (weapon, holder) in _wielders)
        {
            if (holder == fighter && !Weapon.IsWorn(weapon.Kind))
            {
                return weapon;
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="fighter"/> が手 <paramref name="hand"/> にはめている物（グローブ・素手の拳・メリケンサック・カタール・籠手。
    /// <see cref="Weapon.IsWorn"/>。はめていなければ null）。
    /// </summary>
    public Weapon? GloveOf(Fighter fighter, Hand hand)
    {
        foreach (var (weapon, holder) in _wielders)
        {
            if (holder == fighter && Weapon.IsWorn(weapon.Kind) && _wieldHand[weapon] == hand)
            {
                return weapon;
            }
        }

        return null;
    }

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

            var leather = _corners.RedKit(SeatOf(fighter)) ? BoxerAnatomy.RedGlove : BoxerAnatomy.BlueGlove; // 赤・青（追加の席は席かチームの偶奇）
            var mirrored = Weapon.MirroredOn(kind, hand);
            var worn = Weapon.CreateWorn(_world, kind, leather, fighter.GloveCenter(hand), Quaternion.Identity, mirrored);
            Track(worn);
            fighter.Wield(hand, worn.Body.BodyId, Weapon.GripOf(kind, mirrored), glove: true);
            fighter.SetStrikeReach(hand, worn.HandState is null ? null : worn.StrikeOffset);
            fighter.SetBladeChord(hand, kind == WeaponKind.SickleClaw ? SickleClawShape.Edge(hand) : null);
            _wielders[worn] = fighter;
            _wieldHand[worn] = hand;
        }
    }

    /// <summary>両手持ちで、利き手に添えて下を握る手。</summary>
    public const Hand SupportHand = Hand.Left;

    /// <summary>直近の <see cref="Step"/> で、休みに武器を支給された選手と武器の種類（中継の記録 <c>weapon_supplied</c>）。</summary>
    public List<WeaponSupplyEvent> RecentSupplies { get; } = [];

    /// <summary>選手ごとの、最後に武器を支給した休み（その休みの前のラウンドの番号）。1 回の休みに 1 本まで。</summary>
    private readonly Dictionary<Fighter, int> _suppliedAfterRound = [];

    /// <summary>選手ごとの、この試合で武器を支給した回数（<see cref="FighterSense.SelfSupplies"/>）。</summary>
    private readonly Dictionary<Fighter, int> _supplies = [];

    /// <summary>武器の種類ごとの質量 [kg]（支給する前に、手が保持できるかを見るため。作る物と同じ格子・同じ式）。</summary>
    private readonly Dictionary<WeaponKind, float> _weaponMass = [];

    /// <summary>手から外した素手の拳を置いておく所（リング・部屋から遠い下。動かない物にして、何にも触れない）。</summary>
    private static readonly Vector3 RetiredAt = new(0f, -100f, 0f);

    /// <summary>
    /// 休みの間の武器の支給（<see cref="MatchRules.ResupplyWeapons"/>）: 試合の始めに武器を持っていた選手が、今その武器を握っていない（落とした・
    /// 折れて捨てた・手を失った）とき、その手が武器を保持できれば（<see cref="Fighter.CanHoldWeapon"/>: 力の抜けていく手が武器を落とすのと同じ式）、
    /// 同じ種類の新しい武器を、試合の始めと同じ手順（<see cref="Arm"/>）で手に持たせる。休みの間は何度でも調べ、1 回の休みに 1 本まで。
    /// 床の古い武器・折れた武器はそのまま残す（握っていた人の物ではなくなる）。
    /// </summary>
    private void ResupplyWeapons()
    {
        Resupply(Player, _corners.PlayerWeapon, _corners.PlayerTwoHanded);
        Resupply(Cpu, _corners.CpuWeapon, _corners.CpuTwoHanded);
        for (var k = 0; k < _others.Length; k++)
        {
            Resupply(_others[k], _corners.Others![k].Weapon, _corners.Others[k].TwoHanded);
        }
    }

    private void Resupply(Fighter fighter, WeaponKind? kind, bool twoHanded)
    {
        if (kind is not { } weaponKind || fighter.IsDead || fighter.Wields(WeaponHand) || HasFloatingHomeWeapon(fighter)
            || (_suppliedAfterRound.TryGetValue(fighter, out var round) && round == MatchRound))
        {
            return;
        }

        if (!_weaponMass.TryGetValue(weaponKind, out var mass))
        {
            mass = Weapon.MassOf(weaponKind);
            _weaponMass[weaponKind] = mass;
        }

        if (!fighter.CanHoldWeapon(WeaponHand, mass) || !BareHandFree(fighter, WeaponHand))
        {
            return;
        }

        // 両手持ちの武器は、主の手が保持できれば支給する。添える手は、使えて空いていれば今までどおり添えに行く。
        var both = twoHanded && !fighter.ArmSevered(SupportHand) && !fighter.Wields(SupportHand) && BareHandFree(fighter, SupportHand);
        TakeOffBareFist(fighter, WeaponHand);
        if (both)
        {
            TakeOffBareFist(fighter, SupportHand);
        }

        // 床に落ちた古い武器は、もうこの選手の物ではない（WeaponOf が古い武器を返さない）。
        foreach (var old in _wielders.Where(w => w.Value == fighter && !Weapon.IsWorn(w.Key.Kind)).Select(w => w.Key).ToList())
        {
            _wielders.Remove(old);
            _wieldHand.Remove(old);
        }

        if (FloatsFor(fighter, weaponKind))
        {
            FloatWeapon(fighter, weaponKind);
        }
        else
        {
            Arm(fighter, weaponKind, both);
        }

        _suppliedAfterRound[fighter] = MatchRound;
        _supplies[fighter] = _supplies.GetValueOrDefault(fighter) + 1;
        RecentSupplies.Add(new WeaponSupplyEvent(fighter, weaponKind));
    }

    /// <summary>手 <paramref name="hand"/> が、何もはめていないか、素手の拳（<see cref="WeaponKind.Fist"/>）だけをはめている（外して武器を握れる）。</summary>
    private bool BareHandFree(Fighter fighter, Hand hand) => !fighter.WearsGlove(hand) || WornNow(fighter, hand)?.Kind == WeaponKind.Fist;

    /// <summary>
    /// 手 <paramref name="hand"/> が今はめている物（手の剛体が握っている物）。腕が切れる前にはめていた物は、力の抜けた古い手に付いたまま
    /// 同じ手の名前で残っているので、<see cref="GloveOf"/> の最初の 1 つではなく、今握っている剛体で選ぶ。
    /// </summary>
    private Weapon? WornNow(Fighter fighter, Hand hand)
    {
        var held = fighter.WieldedBody(hand);
        foreach (var (weapon, holder) in _wielders)
        {
            if (holder == fighter && Weapon.IsWorn(weapon.Kind) && weapon.Body.BodyId == held)
            {
                return weapon;
            }
        }

        return null;
    }

    /// <summary>
    /// 手にはめた素手の拳を外す（武器を握る前）。素手の拳はボクセルの手の形で、手から外れたら体の一部ではないので、世界から片付ける:
    /// 剛体を消す口が無いので、動かない物にして遠い下へ置き、武器の一覧から外す（近道: docs/DEBT.md「VFC の再生と武器の支給」）。
    /// </summary>
    private void TakeOffBareFist(Fighter fighter, Hand hand)
    {
        if (!fighter.WearsGlove(hand) || WornNow(fighter, hand) is not { } worn || fighter.DeadHandOn(worn.Body.BodyId) is not null)
        {
            return;
        }

        fighter.Unwield(hand);
        _weapons.Remove(worn);
        _wielders.Remove(worn);
        _wieldHand.Remove(worn);
        _reach.Remove(worn);
        _weaponCooldown.Remove(worn);
        _selfCooldown.Remove(worn);
        _world.MakeKinematic(worn.Body.BodyId);
        _world.SetKinematicPose(worn.Body.BodyId, RetiredAt);
    }

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
            fighter.Wield(WeaponHand, weapon.Body.BodyId, top, slideSpan: Weapon.SlideSpanOf(kind));
            fighter.Support(SupportHand, WeaponHand, bottom);
        }
        else
        {
            fighter.Wield(WeaponHand, weapon.Body.BodyId, Weapon.GripOf(kind), slideSpan: Weapon.SlideSpanOf(kind));
        }

        _wielders[weapon] = fighter;
        _wieldHand[weapon] = WeaponHand;
        if (MarkSpawnOverlaps(weapon, fighter) > 0)
        {
            // 体に重ねて置いた武器だけ: 構えへ追いつくまで本人に当てない（重ならずに置いた片手の棍棒・剣は今まで通り）。組は時間が来たら数え直す。
            _spawnOverlaps.RemoveWhere(pair => pair.Weapon == weapon);
            _spawnGrace[weapon] = _tuning.SpawnWeaponSettleSeconds;
        }

        return weapon;
    }

    /// <summary>
    /// 生成した武器の、握った本人の体に当たらない残りの時間 [s]（<see cref="FighterTuning.SpawnWeaponSettleSeconds"/> から数える）。
    /// 武器は生成で T 字の腕の先に前腕の向きで置かれ（両手で握る六尺棒は両手の握りの間が胸・首・上腕を横切る）、腕が構えへ下りる間に
    /// 刃・棒が自分の胸を掃く。この重なりと掃きは生成の書き込みが作ったもので、近づいて打った打撃ではない。最初の歩で自分の胸と頭を削り、
    /// 首を折って開始 0 秒で死ぬことがあった（2026-11-20）。時間が来たら、まだ重なっている組だけを <see cref="_spawnOverlaps"/> へ移す。
    /// </summary>
    private readonly Dictionary<Weapon, float> _spawnGrace = [];

    /// <summary>
    /// 構えに追いついた時点で、まだ握った本人の部位に重なっている（近い）（武器 × 部位の格子）の組。一度離れるまで当たらない（<see cref="SpawnOverlapping"/>）。
    /// 離れた後は今まで通り自分の体にも当たる。
    /// </summary>
    private readonly HashSet<(Weapon Weapon, VoxelBody Part)> _spawnOverlaps = [];

    /// <summary>生成の免除がまだ残っている数（構えへ移る途中の武器 ＋ まだ離れていない組。テスト用: 構えに入って離れれば 0 になり、自分の体にも当たるようになる）。</summary>
    internal int SpawnOverlapCount => _spawnGrace.Count + _spawnOverlaps.Count;

    /// <summary>生成した武器 <paramref name="weapon"/> と、握った本人 <paramref name="holder"/> の部位のうち、格子を囲む箱が重なっている組を覚える。覚えた組の数を返す。</summary>
    private int MarkSpawnOverlaps(Weapon weapon, Fighter holder)
    {
        var marked = 0;
        var parts = new List<VoxelPart>();
        holder.Voxels?.AddContactParts(parts);
        foreach (var part in parts)
        {
            if (!IsGripping(holder, part.Body.BodyId) && GridBoxesOverlap(weapon, part.Body) && _spawnOverlaps.Add((weapon, part.Body)))
            {
                marked++;
            }
        }

        return marked;
    }

    /// <summary>
    /// 武器 <paramref name="weapon"/> が、生成で重ねて置かれた部位 <paramref name="part"/> にまだ重なっているか。離れたら組を忘れる
    /// （離れた歩も当てない: 接触は歩の前から後へ掃くので、歩の前の重なった姿勢から掃くと重なりを打撃に数える。次の歩から当たる）。
    /// </summary>
    private bool SpawnOverlapping(Weapon weapon, VoxelBody part)
    {
        if (!_spawnOverlaps.Contains((weapon, part)))
        {
            return false;
        }

        if (!GridBoxesOverlap(weapon, part))
        {
            _spawnOverlaps.Remove((weapon, part));
        }

        return true;
    }

    /// <summary>
    /// 武器のどれかの格子を囲む箱と、部位の格子を囲む箱が、今の姿勢で重なっているか、<see cref="FighterTuning.SpawnWeaponClearance"/> より近いか
    /// （部位の箱をその分だけ広げて <see cref="OrientedBox"/> で調べる）。
    /// </summary>
    private bool GridBoxesOverlap(Weapon weapon, VoxelBody part)
    {
        var p = _world.GetState(part.BodyId);
        var partCenter = p.Position + Vector3.Transform(part.GridCenterLocal, p.Orientation);
        var partHalf = part.Grid.HalfExtents + new Vector3(_tuning.SpawnWeaponClearance);
        foreach (var grid in weapon.Grids)
        {
            var w = _world.GetState(grid.BodyId);
            var gridCenter = w.Position + Vector3.Transform(grid.GridCenterLocal, w.Orientation);
            if (OrientedBox.Overlaps(gridCenter, w.Orientation, grid.Grid.HalfExtents, partCenter, p.Orientation, partHalf))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 握っていない手 <paramref name="hand"/> が <see cref="HandCommand.Grip"/> を押しているとき、手が柄の近く（<see cref="FighterTuning.WeaponGraspReach"/>）に
    /// あれば握る（グリップで持つ。VR の手。ボットと従来の操作は Grip を出さないので、ここへ来ない）:
    /// <list type="bullet">
    /// <item>自分がもう一方の手で握っている武器があれば、その武器に添える（両手持ち）。握る所は、先に握っている手から柄頭の側へ
    /// 両手の通常の間隔（<see cref="Bots.FormSwordsman.Spacing"/> × 腕の長さ。柄の端まで）。</item>
    /// <item>なければ、床に落ちている（誰も握っていない）武器を、鍔に近い握り（<see cref="Weapon.GripOf"/>）で拾う。左手だけでも持てる。</item>
    /// </list>
    /// 握る前に手にはめていた素手の拳は外す（<see cref="RetireWorn"/>）。
    /// </summary>
    private void TryGraspWeapon(Fighter self, Hand hand)
    {
        // 素手の拳（放した後の空いた手）は外して握れる。グローブ・はめ物（拳の試合）をはめた手は握れない。
        if (_weapons.Count == 0 || self.Wields(hand) || self.ArmSevered(hand) || self.IsHolding(hand)
            || (GloveOf(self, hand) is { } worn && worn.Kind != WeaponKind.Fist))
        {
            return;
        }

        var fist = self.GloveCenter(hand);
        var reach = _tuning.WeaponGraspReach;
        Weapon? own = null;
        foreach (var (weapon, holder) in _wielders)
        {
            if (holder == self && !Weapon.IsWorn(weapon.Kind) && HolderOf(weapon) == self)
            {
                own = weapon;
                break;
            }
        }

        if (own is not null)
        {
            TryAddSupport(self, hand, own, fist, reach);
            return;
        }

        Weapon? best = null;
        var bestDistance = reach;
        var bestGrip = Vector3.Zero;
        foreach (var weapon in _weapons)
        {
            if (Weapon.IsWorn(weapon.Kind) || OwnerOf(weapon) is not null)
            {
                continue; // はめ物・誰かが（切れた手でも）握っている物は拾えない
            }

            var (grip, distance) = GraspPoint(weapon, fist);
            if (distance <= bestDistance)
            {
                best = weapon;
                bestDistance = distance;
                bestGrip = grip;
            }
        }

        if (best is null)
        {
            return;
        }

        RetireWorn(self, hand);
        Unfloat(best);
        var kind = best.Kind;
        self.Grasp(hand, best.Body.BodyId, bestGrip, Weapon.CanTwoHand(kind), Weapon.SlideSpanOf(kind));
        _wielders[best] = self;
        _wieldHand[best] = hand;
    }

    /// <summary>自分が握っている武器 <paramref name="weapon"/> に、空いた手 <paramref name="hand"/> を添える（<see cref="TryGraspWeapon"/>）。</summary>
    private void TryAddSupport(Fighter self, Hand hand, Weapon weapon, Vector3 fist, float reach)
    {
        var kind = weapon.Kind;
        var main = HandOf(weapon);
        if (hand == main || !Weapon.CanTwoHand(kind) || self.WieldedGrip(main) is not { } mainGrip)
        {
            return;
        }

        var (top, bottom) = Weapon.TwoHandGripsOf(kind);
        var low = MathF.Min(top.X, bottom.X);
        var high = MathF.Max(top.X, bottom.X);
        var spacing = Bots.FormSwordsman.Handles(kind) ? Bots.FormSwordsman.Spacing(kind) * self.ArmReach : Weapon.HandSpacing;
        var toPommel = MathF.Sign(bottom.X - top.X);
        var x = Math.Clamp(mainGrip.X + (toPommel * spacing), low, high);
        if (MathF.Abs(x - mainGrip.X) < 1e-3f)
        {
            x = Math.Clamp(mainGrip.X - (toPommel * spacing), low, high); // 先に握った手が柄頭の端にある（棍棒の片手の握り）: 反対の側へ添える
        }

        // 手が柄（握れる区間）のどこかの近くにあるか。
        var s = _world.GetState(weapon.Body.BodyId);
        var axis = Vector3.Transform(Vector3.UnitX, s.Orientation);
        var along = Math.Clamp(Vector3.Dot(fist - s.Position, axis), low, high);
        if (Vector3.Distance(fist, s.Position + (axis * along)) > reach)
        {
            return;
        }

        RetireWorn(self, hand);
        self.Support(hand, main, new Vector3(x, 0f, 0f));
    }

    /// <summary>
    /// 手 <paramref name="hand"/> にはめている素手の拳（<see cref="BareFreeHands"/> がはめた物）を外す。物理の世界から剛体は消せないので、
    /// 関節を外し、手の管理（武器の一覧・握っている手の表）から外して、遠く（床の下）に動かない物として置く。
    /// </summary>
    private void RetireWorn(Fighter self, Hand hand)
    {
        if (GloveOf(self, hand) is not { } worn)
        {
            return;
        }

        self.Unwield(hand);
        self.SetStrikeReach(hand, null);
        var id = worn.Body.BodyId;
        _world.MakeKinematic(id);
        _world.SetKinematicPose(id, new Vector3(0f, -RetiredWornDepth, 0f), Quaternion.Identity);
        _wielders.Remove(worn);
        _wieldHand.Remove(worn);
        _weapons.Remove(worn);
        _fullReach.Remove(worn);
        _weaponCooldown.Remove(worn);
        _selfCooldown.Remove(worn);
    }

    /// <summary>武器を握る手から外した素手の拳を置く深さ [m]（床の下。物理の世界から剛体を消せないので、遠くへ動かない物として置く）。</summary>
    private const float RetiredWornDepth = 1000f;

    /// <summary>
    /// <see cref="Sense"/> に、握った武器の見え方を足したもの: 自分が武器を握っているか、相手の武器の握り・先・先の速度。
    /// </summary>
    private FighterSense ArmedSense(Fighter self, Fighter opponent)
        => (self.Voxels?.Vision ?? VisionState.Healthy) != VisionState.Healthy && _perceived.TryGetValue(self, out var sense) ? sense : RawArmedSense(self, opponent); // 両目が無事なら遅れもぶれも無い: 今の状態をそのまま渡す（見えが傷んだときだけ、1 歩ごとに更新した遅れ・ぶれの写しを使う）

    private readonly Dictionary<Fighter, VisionPerception> _perception = [];
    private readonly Dictionary<Fighter, FighterSense> _perceived = [];

    private void UpdatePerception(float dt)
    {
        foreach (var self in Fighters)
        {
            if (!_perception.TryGetValue(self, out var filter))
            {
                filter = new VisionPerception(_seed ^ (_round * 397) ^ (0x1357 + (SeatOf(self) * 0x1111))); // 赤 0x1357・青 0x2468・追加の席はその先
                _perception.Add(self, filter);
            }
            var raw = RawArmedSense(self, TargetOf(self));
            _perceived[self] = filter.Update(raw, self.Voxels?.Vision ?? VisionState.Healthy, dt);
        }
    }

    private FighterSense RawArmedSense(Fighter self, Fighter opponent)
    {
        var sense = Sense(self, opponent) with { Ring = Layout, MayStrikeDowned = !_corners.Referee, Match = MatchSenseOf(self, opponent) };
        if (_wielders.Count == 0)
        {
            return sense;
        }

        var blade = default(BladeSense);
        if (WeaponOf(opponent) is { } weapon && HolderOf(weapon) == opponent)
        {
            var s = _world.GetState(weapon.Body.BodyId);
            // 打つ端が 2 つある武器（棒の両端、刀・ロングソード・バットの切っ先と柄頭）は、いま速く動いている端を相手の「切っ先」と読む（相手の見える動きから。種類での分岐ではない）。
            var tip = Vector3.Zero;
            var tipVelocity = Vector3.Zero;
            var fastest = -1f;
            foreach (var endX in Weapon.StrikeEndsX(weapon.Kind))
            {
                var end = s.Position + Vector3.Transform(Weapon.TipOf(weapon.Kind) with { X = endX }, s.Orientation);
                var velocity = s.LinearVelocity + Vector3.Cross(s.AngularVelocity, end - s.Position);
                if (velocity.LengthSquared() > fastest)
                {
                    (tip, tipVelocity, fastest) = (end, velocity, velocity.LengthSquared());
                }
            }

            blade = new BladeSense(true, opponent.GloveCenter(HandOf(weapon)), tip, tipVelocity);
        }

        var held = WeaponOf(self) is { } mine && HolderOf(mine) == self ? mine : null;
        var own = held is null ? default : OwnWeaponSense(held, self);
        return sense with { OpponentBlade = blade, SelfArmed = held is not null, SelfWeapon = own, SelfSupplies = _supplies.GetValueOrDefault(self) };
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
        // 既定値の FarEdge は格子の全セルを歩くので、無いときだけ求める（引数に書くと毎回歩く）。
        MathF.Max(0f, (_fullReach.TryGetValue(weapon, out var full) ? full : FarEdge(weapon)) - (holder.WieldedGrip(HandOf(weapon)) ?? Weapon.GripOf(weapon.Kind)).X);

    /// <summary>武器の今のいちばん先のセルの端（武器のローカル X）。</summary>
    private static float FarEdge(Weapon weapon)
    {
        var grid = weapon.Body.Grid;
        // 端の値はセルの x だけで決まり（x が大きいほど小さくならない）、いちばん大きい x の埋まったスライスの値が最大になる: 先のスライスから見て、
        // 最初に埋まったセルがあるスライスで止める（全セルを割り算で座標に戻して歩くのと同じビット列の答え）。
        var (nx, ny, nz) = grid.Dimensions;
        for (var x = nx - 1; x >= 0; x--)
        {
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    if (grid.At(x, y, z) != 0)
                    {
                        return weapon.Body.GridCenterLocal.X + grid.CellCenter(x, y, z).X + (0.5f * grid.CellSize.X);
                    }
                }
            }
        }

        return float.MinValue;
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
        if (!Weapon.HasRigidFace(weapon.Kind))
        {
            return Vector3.Zero;
        }

        if (HolderOf(weapon) is not { } holder)
        {
            return FreeDrive?.Invoke(weapon) ?? Vector3.Zero;
        }

        var hand = HandOf(weapon);
        var force = holder.HandPush(hand);
        return holder.SupportOf(hand) is { } support ? force + holder.HandPush(support) : force;
    }

    /// <summary>
    /// 計測台（<c>miniring-arena bladecut</c>）だけが使う入口: 誰も握っていない武器に、握った手の代わりに押し込む力 [N]（ワールド）を与える。
    /// null（既定）なら握られていない武器は押し込まない（試合はこれを使わない）。
    /// </summary>
    public Func<Weapon, Vector3>? FreeDrive { get; set; }

    private static bool UseLegacyWornImpact(Fighter fighter, WeaponKind kind)
        => !fighter.UsesJointDrive || (kind == WeaponKind.Glove && fighter.UsesLegacyGloveImpact);

    /// <summary>
    /// 手にはめた物の後ろで一緒に打ち込む質量 [kg]（<see cref="VoxelContact.Resolve"/> の massBehind）: 拳の有効質量
    /// （<see cref="Fighter.ImpactMass(Hand, Vector3)"/>: 腕と、拳の向きに乗る体の分）。ボクセルの接触は関節を見ないので、渡さないと
    /// 45 g のグローブそのものの運動エネルギー（2 m/s で 0.1 J、押す力は数 N）でしか打たない（測った: グローブの拳は 1〜7 N で、何も壊さなかった）。
    /// 握る武器は渡さない（腕は押し込む力 <see cref="Drive(Weapon)"/> で効いている。武器の調整はそれで合わせてある。CLAUDE.md §8）。
    /// </summary>
    private float MassBehind(Weapon weapon)
    {
        if (!Weapon.IsWorn(weapon.Kind) || HolderOf(weapon) is not { } holder || !UseLegacyWornImpact(holder, weapon.Kind))
        {
            return 0f;
        }

        var hand = HandOf(weapon);
        if (!_preStepGloveVelocity.TryGetValue((holder, hand), out var velocity))
        {
            return 0f;
        }

        if (holder.UsesArticulatedImpact)
        {
            // 関節で連なった体の、拳の向きの有効質量から、はめた物そのものを除いた分（打たれる点はまだ決まっていないので拳の中心で）。
            return MathF.Max(0f, holder.ArticulatedMass(weapon.Body.BodyId, holder.GloveCenter(hand), velocity, velocity.Length()) - _world.GetMass(weapon.Body.BodyId));
        }

        // 体の質量は、拳の打撃と同じく当たりへ運べる分だけ（BodyFactor）。打たれる部位はまだ決まっていないので、相手の体の重心の速度で見る。
        var factor = BodyFactor(holder, velocity, holder.GloveCenter(hand), holder.ShoulderPosition(hand), holder.Parts.Chest, PreStepBodyVelocity(OpponentOf(holder)), velocity);
        return holder.ImpactMass(hand, velocity, out _, factor);
    }

    /// <summary>打たれる人がまだ決まっていない見積もりに使う相手: 今狙っている相手（2 人の試合ではもう一人。3 人以上では近道: docs/DEBT.md）。</summary>
    private Fighter OpponentOf(Fighter fighter) => TargetOf(fighter);

    /// <summary>握った腕が武器の後ろで運ぶ運動量 [kg·m/s]: 腕の質量 × 握った拳が打たれた点へ近づく速さ（歩の前）。</summary>
    private float ArmBehind(Fighter holder, Hand hand, Vector3 struckVelocity, Vector3 direction)
    {
        var fist = _preStepGloveVelocity.TryGetValue((holder, hand), out var before) ? before : holder.GloveVelocity(hand);
        return holder.ArmMass(hand) * PunchMechanics.ClosingSpeed(fist, struckVelocity, direction);
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
            var supported = _wielders.Keys.Any(w => w.Carries(body) && OwnerOf(w) is not null);
            return ContactResonance.Of(dimensions[0], dimensions[1], dimensions[2], MathF.Max(1e-6f, _world.GetMass(body.BodyId)), supported);
        }
        return new ContactResonance(MathF.Max(Shape(hit.A).DecayScale, Shape(hit.B).DecayScale));
    }

    /// <summary>同じ 2 つの物の当たった音を、続けては鳴らさない間 [s]（1 回の当たりの中で跳ね返って触れ直す時間より長く、続けて打つ間より短く）。</summary>
    private const float ContactQuietSeconds = 0.2518f;

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
        var profileStart = StepProfile.Begin();
        var result = MayTouchCore(a, aBefore, b, bBefore);
        StepProfile.End(StepProfile.Slot.MayTouch, profileStart);
        return result;
    }

    private bool MayTouchCore(VoxelBody a, BodyState aBefore, VoxelBody b, BodyState bBefore)
    {
        var aNow = _world.GetState(a.BodyId);
        var bNow = _world.GetState(b.BodyId);
        var reach = a.Grid.HalfExtents.Length() + b.Grid.HalfExtents.Length() + a.GridCenterLocal.Length() + b.GridCenterLocal.Length();
        var moved = Vector3.Distance(aBefore.Position, aNow.Position) + Vector3.Distance(bBefore.Position, bNow.Position) + 0.02817f;
        var gap = MathF.Min(Vector3.Distance(aBefore.Position, bBefore.Position), Vector3.Distance(aNow.Position, bNow.Position));
        return gap - moved <= reach;
    }

    /// <summary>接触を調べる全ての格子（部位と、その剛体に重ねた層 = 顔の層、と破片）。</summary>
    private static IEnumerable<VoxelPart> VoxelPartsOf(IEnumerable<Fighter> fighters) =>
        fighters.SelectMany(f => f.Voxels is { } body ? body.Layers.Concat(body.Fragments) : []);

    /// <summary>物理の 1 歩の前: 武器と体の部位の姿勢を覚える（接触は歩の前後の動きを掃いて調べる）。</summary>
    private void RememberBeforeStep()
    {
        var profileStart = StepProfile.Begin();
        RememberBeforeStepCore();
        StepProfile.End(StepProfile.Slot.Remember, profileStart);
    }

    private void RememberBeforeStepCore()
    {
        _beforeStep.Clear();
        if (_weapons.Count == 0 && !AnyoneKicking() && !AnyoneElbowing())
        {
            return;
        }

        foreach (var weapon in _weapons)
        {
            _beforeStep[weapon.Body.BodyId] = _world.GetState(weapon.Body.BodyId);
        }

        foreach (var fighter in Fighters)
        {
            if (fighter.Voxels is not { } body)
            {
                continue;
            }

            foreach (var part in body.Parts)
            {
                _beforeStep[part.Body.BodyId] = _world.GetState(part.Body.BodyId);
            }

            foreach (var part in body.Fragments)
            {
                _beforeStep[part.Body.BodyId] = _world.GetState(part.Body.BodyId);
            }
        }
    }

    /// <summary>誰かが脚を上げて蹴っているか（毎歩の問い合わせなので、LINQ を使わない）。</summary>
    private bool AnyoneKicking()
    {
        foreach (var fighter in Fighters)
        {
            foreach (var hand in Hands)
            {
                if (fighter.IsKicking(hand))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>誰かが肘を打ちに出しているか（毎歩の問い合わせなので、LINQ を使わない）。</summary>
    private bool AnyoneElbowing()
    {
        foreach (var fighter in Fighters)
        {
            foreach (var hand in Hands)
            {
                if (fighter.IsElbowing(hand))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>毎歩の掃きで使い回す一時置き場（武器の一覧・体の部位の一覧・触れた部位。掃きは再入しない）。</summary>
    private readonly List<Weapon> _weaponScratch = [];
    private readonly List<VoxelPart> _partScratch = [];
    private readonly HashSet<VoxelPart> _touchedScratch = [];
    /// <summary>物理の 1 歩の後: 武器 × 武器、武器 × 体の部位の接触を解く。</summary>
    private void ResolveWeapons()
    {
        var profileStart = StepProfile.Begin();
        ResolveWeaponsCore();
        StepProfile.End(StepProfile.Slot.Weapons, profileStart);
    }

    private void ResolveWeaponsCore()
    {
        if (_weapons.Count == 0)
        {
            return;
        }

        // 接触で折れた破片は、この歩の後で加わる（今の歩の前の姿勢を持たない）。
        var weapons = _weaponScratch;
        weapons.Clear();
        weapons.AddRange(_weapons);
        for (var i = 0; i < weapons.Count; i++)
        {
            var weapon = weapons[i];
            if (!_beforeStep.TryGetValue(weapon.Body.BodyId, out var before) || _floating.ContainsKey(weapon))
            {
                continue; // 浮いている武器は誰にも当たらない
            }

            for (var j = i + 1; j < weapons.Count; j++)
            {
                var other = weapons[j];
                if (_floating.ContainsKey(other))
                {
                    continue;
                }

                // 同じ人の両手にはめた物は打ち合わない（構えでいつも触れ合っている）。
                if (Weapon.IsWorn(weapon.Kind) && Weapon.IsWorn(other.Kind) && OwnerOf(weapon) is { } h && OwnerOf(other) == h)
                {
                    continue;
                }

                if (_beforeStep.TryGetValue(other.Body.BodyId, out var otherBefore))
                {
                    var clashStart = StepProfile.Begin();
                    // 剛体に乗った全ての格子（物 → 手の固定の順）の組を調べる。腕の押す力は、最初に触れた組だけが受ける（格子の数だけ払わない）。
                    var pushed = false;
                    // （IReadOnlyList を foreach すると毎歩、列挙子が箱に入って割り当てが出るので、添字で回す。）
                    for (var ia = 0; ia < weapon.Grids.Count; ia++)
                    {
                        var gridA = weapon.Grids[ia];
                        for (var ib = 0; ib < other.Grids.Count; ib++)
                        {
                            var gridB = other.Grids[ib];
                            if (!MayTouch(gridA, before, gridB, otherBefore))
                            {
                                continue;
                            }

                            // 物どうし（武器・はめる物）は面の下を押さない: どちらが「場」になるかは格子の粗さだけで決まり、詰め物や刃の内側は固い物として扱う。
                            var pushStart = StepProfile.Begin();
                            var (driveA, driveB, behindA, behindB) = pushed ? default : (Drive(weapon), Drive(other), MassBehind(weapon), MassBehind(other));
                            StepProfile.End(StepProfile.Slot.ClashPush, pushStart);
                            var clash = VoxelContact.Resolve(gridA, before, gridB, otherBefore, driveA, driveB, behindA, behindB, pressBeneath: false);
                            var clashAfterStart = StepProfile.Begin();
                            Clash(weapon, other, gridA, gridB, clash);
                            StepProfile.End(StepProfile.Slot.ClashAfter, clashAfterStart);
                            pushed |= clash.Touched;
                        }
                    }

                    StepProfile.End(StepProfile.Slot.WeaponClash, clashStart);
                }
            }

            var drive = Drive(weapon);
            var behind = MassBehind(weapon);
            // 部位は 1 つずつ解くので、ある部位の接触が刃を押し戻すと、その先で別の部位（すでに解いた部位）の中へ入ることがある（肩の上腕と胸のつなぎ目など、
            // 部位が接している所）。そのまま歩を終えると、刃が体の中にあるのに何も起きない 1 歩になる。刃が動いたら、まだ触れていない相手の部位だけを、
            // 握りの押す力（drive・massBehind）を数えずに掃き直す（同じ部位を 2 度打たない・腕の力を 2 度払わない。<see cref="MaxContactPasses"/> まで。握った本人の体は 1 回だけ）。
            // 手にはめた物（グローブなど）は 1 回だけ（拳はいつも相手の体に押し当てて動くので、掃き直すと拳の打撃の手触りまで変わる。調べたのは刃・棍棒）。
            var passes = UsesBladeSweep(weapon.Kind) ? MaxContactPasses : 1;
            var touched = _touchedScratch;
            touched.Clear();
            _touchedBridgeScratch.Clear();
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

            // 部位の一覧の写し（接触で破片が増えても、この歩の分だけを回す）。
            var parts = _partScratch;
            parts.Clear();
            fighter.Voxels?.AddContactParts(parts); // 顔の層は頭の格子の直前（同じ剛体: 外の層から先に当たる）

            var pushedBody = -1;
            foreach (var part in parts)
            {
                if ((holds && IsGripping(fighter, part.Body.BodyId)) || deadHand?.Hand == part.Body.BodyId || (!includeHolder && touched.Contains(part)))
                {
                    continue; // 握りはいつも手の剛体に触れている
                }

                if (holds && (_spawnGrace.ContainsKey(weapon) || SpawnOverlapping(weapon, part.Body)))
                {
                    continue; // 生成から構えへ移る間と、そのとき重なっていた所は、一度離れるまで当たらない
                }

                if (_beforeStep.TryGetValue(part.Body.BodyId, out var partBefore))
                {
                    // 剛体に乗った全ての格子（物 → 手の固定の順）で、同じ部位を調べる。腕の押す力は、最初に触れた格子だけが受ける
                    // （部位の側も同じ: 同じ剛体の顔の層に触れたら、その下の頭蓋骨には払わない）。
                    var pushed = part.Body.BodyId == pushedBody;
                    for (var gi = 0; gi < weapon.Grids.Count; gi++)
                    {
                        var grid = weapon.Grids[gi];
                        if (!MayTouch(grid, before, part.Body, partBefore))
                        {
                            continue;
                        }

                        var hit = VoxelContact.Resolve(grid, before, part.Body, partBefore, pushed ? default : drive, massBehindA: pushed ? 0f : behind);
                        if (hit.Touched)
                        {
                            touched.Add(part);
                            pushed = true;
                            pushedBody = hit.Stopped ? part.Body.BodyId : pushedBody; // 顔の層で止まらず潰し抜けた打撃は、下の頭蓋骨にも腕の押す力を運ぶ
                        }

                        Cut(weapon, grid, fighter, part, hit);
                    }
                }
            }

            // 関節のつなぎ: 部位の間の隙間を通る刃も、関節の肉・骨に当たる。握った本人の体のつなぎは数えない（自分の体の中で押し合うだけ）。
            if (JointBridges && !holds && deadHand is null && UsesBladeSweep(weapon.Kind))
            {
                var bridgesStart = StepProfile.Begin();
                ResolveBridges(weapon, before, drive, behind, fighter, includeHolder);
                StepProfile.End(StepProfile.Slot.Bridges, bridgesStart);
            }
        }
    }

    private static bool UsesBladeSweep(WeaponKind kind) => !Weapon.IsWorn(kind) || kind == WeaponKind.SickleClaw;

    private readonly HashSet<JointBridge> _touchedBridgeScratch = [];

    /// <summary>
    /// 武器 <paramref name="weapon"/> × 相手の関節のつなぎ（<see cref="JointBridge"/>）。つながった 2 つの部位の隙間にある組織を、部位と同じ接触（圧力と材質）で
    /// 解き、断たれたら、そのつなぎがつないでいた両側の部位のセルを壊す（骨が断たれたら落ちる、は部位の接触と同じ後始末）。
    /// 掃き直し（<paramref name="firstPass"/> でない）では、まだ触れていないつなぎだけ。
    /// </summary>
    private void ResolveBridges(Weapon weapon, BodyState before, Vector3 drive, float behind, Fighter victim, bool firstPass)
    {
        if (victim.Voxels is not { } body)
        {
            return;
        }

        foreach (var bridge in body.Bridges)
        {
            if (!firstPass && _touchedBridgeScratch.Contains(bridge))
            {
                continue;
            }

            var (parent, child) = (bridge.Parent.Body.BodyId, bridge.Child.Body.BodyId);
            if (!victim.JointIntact(parent, child) || !_beforeStep.TryGetValue(parent, out var parentBefore) || !MayTouch(weapon.Body, before, bridge.Body, parentBefore))
            {
                continue;
            }

            var refreshStart = StepProfile.Begin();
            var near = bridge.RefreshNear(weapon.Body, before);
            StepProfile.End(StepProfile.Slot.BridgeRefresh, refreshStart);
            if (!near)
            {
                continue;
            }

            var hit = VoxelContact.Resolve(weapon.Body, before, bridge.Body, parentBefore, drive, massBehindA: behind, pressBeneath: false);
            if (hit.Touched)
            {
                _touchedBridgeScratch.Add(bridge);
            }

            CutBridge(weapon, victim, bridge, hit);
        }
    }

    /// <summary>
    /// 武器 × 関節のつなぎの接触の後始末: つなぎのセルが断たれたら、その両側の部位のセルを壊し（<see cref="JointBridge.Sever"/>）、部位ごとに
    /// 普通の接触と同じ後始末（<see cref="Wound(Weapon, VoxelBody, Fighter, VoxelPart, VoxelContactResult)"/>: 骨が断たれたら落とし、臓器が壊れたら死なせ、散らす）をする。
    /// 何も断たなかった接触は、親の部位への当たりとして残す（刃を止めた・かすめた）。
    /// </summary>
    private void CutBridge(Weapon weapon, Fighter victim, JointBridge bridge, VoxelContactResult hit)
    {
        if (!hit.Touched)
        {
            return;
        }

        Snapped(weapon, hit.SnappedA);
        var severed = hit.RemovedB.Count > 0 ? bridge.Sever(hit) : [];
        if (severed.Count == 0)
        {
            var bump = hit with { B = bridge.Parent.Body };
            _recentCuts.Add(bump);
            NoteContact(bump);
            return;
        }

        var first = true;
        foreach (var (part, cells) in severed)
        {
            var partHit = hit with { B = part.Body, RemovedA = first ? hit.RemovedA : [], RemovedB = cells, CrushedA = null, CrushedB = null, BentA = null, BentB = null };
            first = false;
            part.Body.UpdateMass(); // 部位のセルが減ったぶん、質量と剛体の形を合わせる（普通の接触は VoxelContact が呼ぶ）
            _recentCuts.Add(partHit);
            Wound(weapon, weapon.Body, victim, part, partHit);
        }
    }

    /// <summary>武器同士: 壊れた側の破片を散らし、折れた先を新しい武器の破片にする。</summary>
    private void Clash(Weapon a, Weapon b, VoxelBody gridA, VoxelBody gridB, VoxelContactResult hit)
    {
        if (!hit.Touched)
        {
            return;
        }

        _recentCuts.Add(hit);
        NoteContact(hit);
        Shatter(hit, gridA, -hit.Normal, gridB, hit.Normal, fatal: false);
        // 金属と金属がぶつかった所: 火花（ぶつかった所の両方の材質が鉄のとき、近づく速さとともに）。
        if (MaterialNear(gridA, hit.Point) == BoxerAnatomy.Iron && MaterialNear(gridB, hit.Point) == BoxerAnatomy.Iron)
        {
            var sa = _world.GetState(gridA.BodyId);
            var sb = _world.GetState(gridB.BodyId);
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
    private void Cut(Weapon weapon, VoxelBody grid, Fighter victim, VoxelPart part, VoxelContactResult hit)
    {
        var profileStart = StepProfile.Begin();
        CutCore(weapon, grid, victim, part, hit);
        StepProfile.End(StepProfile.Slot.Cut, profileStart);
    }

    private void CutCore(Weapon weapon, VoxelBody grid, Fighter victim, VoxelPart part, VoxelContactResult hit)
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
            var pointVelocity = weapon.Kind == WeaponKind.SickleClaw ? PreStepVelocityAt(weapon.Body.BodyId, hit.Point) : (Vector3?)null;
            if (!(part.FragmentOf is null && HolderOf(weapon) is { } puncher && puncher != victim
                && Punch(puncher, HandOf(weapon), victim, part, hit, contactTwist, UseLegacyWornImpact(puncher, weapon.Kind), pointVelocity)))
            {
                if (contactTwist != Vector3.Zero && MayStrike(victim))
                {
                    victim.TwistHead(contactTwist);
                }

                NoteContact(hit); // 拳の打撃として数えた当たりは打撃音が鳴る。数えない触れ合い（軽い当たり）だけ当たった音
            }

            Wound(weapon, grid, victim, part, hit);
            return;
        }

        // 握った武器の打撃は、拳の打撃と同じく、拳の後ろの体の重さも運ぶ（武器は梃子で速さを、体は重さを足す）。1 振りに 1 度。
        var carried = false;
        if (!RoundOver && !Holding && part.FragmentOf is null && HolderOf(weapon) is { } swinger && swinger != victim && MayStrike(victim)
            && _weaponCooldown.GetValueOrDefault(weapon) <= 0f && _beforeStep.TryGetValue(weapon.Body.BodyId, out var weaponBefore))
        {
            // 当たる速さは、打たれた点に対する相対速度（拳・蹴り・肘と同じ。PunchMechanics.ClosingSpeed）。
            var struckVelocity = PreStepVelocityAt(part.Body.BodyId, hit.Point);
            var contactVelocity = weaponBefore.LinearVelocity + Vector3.Cross(weaponBefore.AngularVelocity, hit.Point - weaponBefore.Position);
            var contactSpeed = PunchMechanics.ClosingSpeed(contactVelocity, struckVelocity, hit.Direction);
            var swingHand = HandOf(weapon);
            // 武器の後ろの運動量（物理の接触が運ぶのは武器そのものだけ）: 握った腕（両手なら添えた腕も）は自分の質量 × 握った拳が近づく速さ（拳の打撃で物理の接触が運ぶ分と同じ）、
            // 体の分は拳の打撃と同じ式（BodyFactor）を武器の当たる点で見る: 押し出し = 当たる点が体に対して当たりへ動く割合、体の運び = 体自身（肩の突っ張りと、
            // 腕と武器を胴に固めて当たる点が胴と一緒に動く梃子）の近づく速さ ÷ 当たる点の速さ。体の分の運動量 = 連結度 × 体の有効質量 × 体自身の速さ。
            // 腕の運動量は物理の接触（VoxelContact は歩の後にこの式で解くので、握りの関節の力は入らない）にも、腕の押す力 Drive（1 歩の力 × 時間で、
            // 腕の運動量の 1 割ほど）にも含まれていない（こんぼうが 8 m/s で当たっても接触の力積は 0.25 kg·m/s 未満だった。docs/arena/rush_physics.md §5-2）。
            var bodyFactor = BodyFactor(swinger, contactVelocity, hit.Point, swinger.ShoulderPosition(swingHand), swinger.Parts.Chest, struckVelocity, hit.Direction,
                limbEnd: swinger.GloveCenter(swingHand));
            var bodyMomentum = (swinger.ImpactMass(swingHand, hit.Direction, out _, bodyFactor) - swinger.ArmMass(swingHand)) * contactSpeed;
            var armMomentum = ArmBehind(swinger, swingHand, struckVelocity, hit.Direction)
                + (swinger.SupportOf(swingHand) is { } support ? ArmBehind(swinger, support, struckVelocity, hit.Direction) : 0f);
            if (contactSpeed >= MinimumBlowSpeed)
            {
                var momentum = hit.Direction * ((bodyMomentum + armMomentum) * KnockbackTransfer);
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
                NoteHit(swinger, victim, damage);

                // 打撲: 刃の無い硬い武器が運んだ運動エネルギーで、当たった所を拳と同じく潰して掘る（グローブの詰め物が無い分、深く）。
                // 刃のある武器は、刃が当たった所をボクセル同士の接触そのもので斬る（潰しはしない）。
                var crushed = 0;
                if (Weapon.IsBlunt(weapon.Kind))
                {
                    Strike(victim, part.Body.BodyId, hit.Point, hit.Direction, strength, swinger, push, BluntCarve(delivered, contactSpeed));
                    crushed = _chips.Count + _chunks.Sum(c => c.Cells.Count); // 潰れて飛んだセル（切り落とされた先は数えない）
                }

                // 見た目のあざ（皮膚の色だけ。臓器の打撲・スタミナには触れない）: 運んだ運動量 × 当たった速さ ÷ 2 を当たりのエネルギーの見積もりにする。
                victim.Voxels?.BruiseSkin(hit.Point, 0.5f * delivered * contactSpeed);
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

        Wound(weapon, grid, victim, part, hit);
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
            BrokenBones.Add(new BoneBreakEvent(victim, broken.Part.Kind, broken.Point, broken.Bent, broken.Part.Body.BodyId));
            ShowMessage(arm ? "ARM BROKEN!" : broken.Part.Kind == BodyPartKind.Head ? "NECK BROKEN!" : "LEG BROKEN!", 1.2f);
            return;
        }

        _recentSevers.Add(new SeverEvent(broken.Point, strength, victim, broken.Part.Body.BodyId));
        ShowMessage(arm ? "ARM OFF!" : broken.Part.Kind == BodyPartKind.Head ? "HEAD OFF!" : "LEG OFF!", 1.2f);
    }

    /// <summary>直近の <see cref="Step"/> で骨が折れて、肉だけでつながった所（切り落とされたものは <see cref="RecentSevers"/>）。</summary>
    public List<BoneBreakEvent> BrokenBones { get; } = [];

    /// <summary>
    /// 接触が体を壊した後: 骨が断たれたらその先を落とし、臓器が壊れたら死なせ、壊れたセルを散らす（刃のある物は振り抜けた向きへ、
    /// 刃の無い物は拳と同じ散り方）。握った武器も手にはめた物も同じ。
    /// </summary>
    private void Wound(Weapon weapon, VoxelBody grid, Fighter victim, VoxelPart part, VoxelContactResult hit)
    {
        if (hit.RemovedB.Count > 0 && OwnerOf(weapon) is { } holder)
        {
            NoteCut(holder, victim); // バトルロイヤルのとどめの記録（2 人の試合では何もしない）
        }

        Wound(grid, Weapon.IsBlunt(weapon.Kind) ? null : weapon, victim, part, hit);
    }

    /// <summary>
    /// 打った物（武器・蹴った脚の骨）<paramref name="striker"/> が相手の部位を壊した後始末。<paramref name="blade"/> は刃のある武器のとき
    /// （斬った体の破片を振り抜けた向きへ飛ばす）。蹴りと刃の無い武器は拳と同じ散り方なので null。
    /// </summary>
    private void Wound(VoxelBody striker, Weapon? blade, Fighter victim, VoxelPart part, VoxelContactResult hit)
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
        Shatter(hit, striker, -hit.Normal, part.Body, hit.Normal, fatal, blade);
    }

    /// <summary>
    /// 蹴り: 蹴っている脚の骨（すね・足・つま先、膝蹴りなら腿）のボクセルが、相手の部位のボクセルと拳と同じ接触
    /// （<see cref="VoxelContact.Resolve"/>）で当たる。蹴り用の式・倍率は無い。脚の後ろの体の質量（<see cref="Fighter.KickImpactMass"/>）が
    /// 面の下へ伝わる力に効く（拳の <see cref="MassBehind"/> と同じ役割）。打った側の脚の骨も曲げられ、折れることがある。
    /// </summary>
    private void ResolveKicks()
    {
        var profileStart = StepProfile.Begin();
        ResolveKicksCore();
        StepProfile.End(StepProfile.Slot.Kicks, profileStart);
    }

    private void ResolveKicksCore()
    {
        // （箱どうしの衝突は FilterKickPairs が歩の前に切ってある。）
        foreach (var attacker in Fighters)
        {
            if (attacker.Voxels is not { } own)
            {
                continue;
            }

            foreach (var side in Hands)
            {
                if (!attacker.IsKicking(side))
                {
                    continue;
                }

                foreach (var kicker in own.Parts.Where(p => attacker.IsKickingBody(side, p.Body.BodyId)).ToList())
                {
                    if (!_beforeStep.TryGetValue(kicker.Body.BodyId, out var kickerBefore))
                    {
                        continue;
                    }

                    var behind = KickMassBehind(attacker, side, kicker, kickerBefore);
                    foreach (var victim in Fighters)
                    {
                        if (victim == attacker)
                        {
                            continue; // 自分以外の全員（2 人なら相手 1 人）
                        }

                        var pushedBody = -1;
                        foreach (var part in VoxelPartsOf([victim]).ToList())
                        {
                            if (_beforeStep.TryGetValue(part.Body.BodyId, out var partBefore) && MayTouch(kicker.Body, kickerBefore, part.Body, partBefore))
                            {
                                // 脚の後ろの体の質量は、同じ剛体の格子のうち打撃を止めた層（顔の層 → 頭蓋骨）までが受ける（顔の肉を潰し抜けた打撃は頭蓋骨にも運ぶ）。
                                var drive = attacker.DistalKickDrive(side, kicker.Body.BodyId);
                                var hit = VoxelContact.Resolve(kicker.Body, kickerBefore, part.Body, partBefore,
                                    driveA: drive, massBehindA: part.Body.BodyId == pushedBody ? 0f : behind);
                                pushedBody = hit.Touched && hit.Stopped ? part.Body.BodyId : pushedBody;
                                Kick(attacker, side, victim, kicker, part, hit);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>いま剛体の箱の衝突を切っている（蹴る脚の骨, 相手の部位）の組。</summary>
    private readonly HashSet<(int Kicker, int Part)> _kickFiltered = [];

    /// <summary>
    /// 物理の 1 歩の前: 蹴っている脚の骨と相手の部位は、箱どうしでは当たらないようにする（武器・はめる物と同じ。箱は格子を囲む
    /// 四角で、ボクセルの丸みより大きい。箱が先に止めると、ボクセルが重ならず接触（圧力のモデル）が起きない）。脚が蹴り終えたら戻す。
    /// 当たった力は <see cref="VoxelContact.Resolve"/> の力積と <see cref="ApplyHit"/> の運動量の受け渡しが運ぶ。
    /// </summary>
    private void FilterKickPairs()
    {
        if (_kickFiltered.Count == 0 && !Fighters.Any(f => Hands.Any(f.IsKicking)))
        {
            return; // 蹴っている脚が無く、切った組も無い（毎歩の割り当てを避ける）
        }

        var wanted = new HashSet<(int, int)>();
        foreach (var attacker in Fighters)
        {
            if (attacker.Voxels is not { } own)
            {
                continue;
            }

            foreach (var side in Hands.Where(attacker.IsKicking))
            {
                foreach (var kicker in own.Parts.Where(p => attacker.IsKickingBody(side, p.Body.BodyId)))
                {
                    foreach (var part in VoxelPartsOf(Fighters.Where(f => f != attacker)))
                    {
                        wanted.Add((kicker.Body.BodyId, part.Body.BodyId));
                    }
                }
            }
        }

        foreach (var pair in _kickFiltered.Where(p => !wanted.Contains(p)).ToList())
        {
            _world.SetPairCollisionEnabled(pair.Kicker, pair.Part, true);
            _kickFiltered.Remove(pair);
        }

        foreach (var pair in wanted)
        {
            if (_kickFiltered.Add(pair))
            {
                _world.SetPairCollisionEnabled(pair.Item1, pair.Item2, false);
            }
        }
    }

    /// <summary>蹴る骨の後ろで一緒に打ち込む質量 [kg]: 脚と体の有効質量（歩の前の速さの向きで）から、その骨自身を除いたもの。</summary>
    private float KickMassBehind(Fighter attacker, Hand side, VoxelPart kicker, BodyState before)
    {
        var speed = before.LinearVelocity.Length();
        if (speed < 1e-3f)
        {
            return 0f;
        }

        var factor = BodyFactor(attacker, before.LinearVelocity, before.Position, attacker.HipPosition(side), attacker.Parts.Pelvis, PreStepBodyVelocity(OpponentOf(attacker)), before.LinearVelocity);
        var total = attacker.KickImpactMass(side, before.Position, before.LinearVelocity / speed, out _, factor);
        return MathF.Max(0f, total - _world.GetMass(kicker.Body.BodyId));
    }

    private void Kick(Fighter attacker, Hand side, Fighter victim, VoxelPart kicker, VoxelPart part, VoxelContactResult hit)
    {
        if (!hit.Touched)
        {
            return;
        }

        _recentCuts.Add(hit);
        var contactTwist = Vector3.Zero;
        if (part.Kind == BodyPartKind.Head && part.FragmentOf is null && hit.ImpulseOnB.LengthSquared() > 0f)
        {
            var head = _world.GetState(part.Body.BodyId);
            contactTwist = Vector3.Cross(hit.Point - head.Position, hit.ImpulseOnB);
        }

        if (!(part.FragmentOf is null && ScoreKick(attacker, side, victim, kicker, part, hit, contactTwist)))
        {
            if (contactTwist != Vector3.Zero && MayStrike(victim))
            {
                victim.TwistHead(contactTwist);
            }

            NoteContact(hit);
        }

        Wound(kicker.Body, null, victim, part, hit);
        // 蹴った脚の骨も、相手の硬い所に当たれば曲げられる・削られる（折れた脚は蹴れなくなる）。
        if (attacker.Voxels is { } own)
        {
            if (hit.BentFor(kicker.Body) is { } bend && own.BreakBent(kicker.Body.BodyId, bend) is { } bent)
            {
                BreakBone(attacker, bent, 1f);
            }

            if (hit.RemovedA.Count > 0 && own.BreakIfSevered(kicker.Body.BodyId) is { } severed)
            {
                BreakBone(attacker, severed, 1f);
            }
        }
    }

    /// <summary>蹴りを打撃（<see cref="HitEvent"/>）として数える: 拳と同じ物差し（速さ・有効質量・部位の倍率）。数えたら true。</summary>
    private bool ScoreKick(Fighter attacker, Hand side, Fighter victim, VoxelPart kicker, VoxelPart part, VoxelContactResult hit, Vector3 contactTwist)
    {
        if (RoundOver || Holding || !MayStrike(victim) || _kickCooldown.GetValueOrDefault((attacker, side)) > 0f
            || !_beforeStep.TryGetValue(kicker.Body.BodyId, out var before))
        {
            return false;
        }

        // 当たる速さは打つ点と打たれた点の相対速度（歩の前。PunchMechanics.ClosingSpeed）。向き（velocity）は打つ側の速度のまま。
        var velocity = before.LinearVelocity + Vector3.Cross(before.AngularVelocity, hit.Point - before.Position);
        var struck = PreStepVelocityAt(part.Body.BodyId, hit.Point);
        var speed = PunchMechanics.ClosingSpeed(velocity, struck, hit.Direction);
        if (speed <= 0f)
        {
            return false;
        }

        var drive = BodyFactor(attacker, velocity, hit.Point, attacker.HipPosition(side), attacker.Parts.Pelvis, struck, hit.Direction);
        var impactMass = attacker.KickImpactMass(side, hit.Point, hit.Direction, out var bodyShare, drive);
        var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
        var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
        var damage = PunchMechanics.Damage(impactMass * speed) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
        if (damage < MinimumDamage)
        {
            return false;
        }

        _kickCooldown[(attacker, side)] = RehitCooldown;
        var face = hit.A == part.Body ? hit.FaceB : hit.FaceA;
        ApplyHit(new HitEvent(attacker, side, victim, zone, hit.Point, speed, impactMass, bodyShare, damage, face, Kick: true, KickerPart: attacker.KickPartOf(side, kicker.Body.BodyId)), part.Body.BodyId, velocity, carve: false, alsoTwist: contactTwist);
        return true;
    }

    /// <summary>
    /// 肘打ち: 肘を出している腕の上腕・前腕の骨のセルが、相手の部位のセルと拳・蹴りと同じ接触（<see cref="VoxelContact.Resolve"/>）で当たる。
    /// 肘用の式・倍率は無い。肩・体の質量（<see cref="Fighter.ElbowImpactMass"/>）が面の下へ伝わる力に効き、肘の先の狭い面（骨・肘頭の小さな接触面積）に
    /// 力が集まる（圧力）ぶん、同じ速さの拳（グローブの広い面）より深く削れる。
    /// </summary>
    private void ResolveElbows()
    {
        foreach (var attacker in Fighters)
        {
            if (attacker.Voxels is not { } own)
            {
                continue;
            }

            foreach (var side in Hands)
            {
                if (!attacker.IsElbowing(side))
                {
                    continue;
                }

                foreach (var striker in own.Parts.Where(p => attacker.IsElbowingBody(side, p.Body.BodyId)).ToList())
                {
                    if (!_beforeStep.TryGetValue(striker.Body.BodyId, out var strikerBefore))
                    {
                        continue;
                    }

                    var behind = ElbowMassBehind(attacker, side, striker, strikerBefore);
                    foreach (var victim in Fighters)
                    {
                        if (victim == attacker)
                        {
                            continue; // 自分以外の全員（2 人なら相手 1 人）
                        }

                        var pushedBody = -1;
                        foreach (var part in VoxelPartsOf([victim]).ToList())
                        {
                            if (_beforeStep.TryGetValue(part.Body.BodyId, out var partBefore) && MayTouch(striker.Body, strikerBefore, part.Body, partBefore))
                            {
                                // 腕の後ろの体の質量は、同じ剛体の格子のうち打撃を止めた層（顔の層 → 頭蓋骨）までが受ける（顔の肉を潰し抜けた打撃は頭蓋骨にも運ぶ）。
                                var hit = VoxelContact.Resolve(striker.Body, strikerBefore, part.Body, partBefore, massBehindA: part.Body.BodyId == pushedBody ? 0f : behind);
                                pushedBody = hit.Touched && hit.Stopped ? part.Body.BodyId : pushedBody;
                                Elbow(attacker, side, victim, striker, part, hit);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>いま剛体の箱の衝突を切っている（肘を出している腕の骨, 相手の部位）の組。</summary>
    private readonly HashSet<(int Striker, int Part)> _elbowFiltered = [];

    /// <summary>物理の 1 歩の前: 肘を出している腕の骨と相手の部位は、箱どうしでは当たらないようにする（蹴りと同じ理由: 箱が先に止めるとボクセルの接触が起きない）。</summary>
    private void FilterElbowPairs()
    {
        if (_elbowFiltered.Count == 0 && !AnyoneElbowing())
        {
            return;
        }

        var wanted = new HashSet<(int, int)>();
        foreach (var attacker in Fighters)
        {
            if (attacker.Voxels is not { } own)
            {
                continue;
            }

            foreach (var side in Hands.Where(attacker.IsElbowing))
            {
                foreach (var striker in own.Parts.Where(p => attacker.IsElbowingBody(side, p.Body.BodyId)))
                {
                    foreach (var part in VoxelPartsOf(Fighters.Where(f => f != attacker)))
                    {
                        wanted.Add((striker.Body.BodyId, part.Body.BodyId));
                    }
                }
            }
        }

        foreach (var pair in _elbowFiltered.Where(p => !wanted.Contains(p)).ToList())
        {
            _world.SetPairCollisionEnabled(pair.Striker, pair.Part, true);
            _elbowFiltered.Remove(pair);
        }

        foreach (var pair in wanted)
        {
            if (_elbowFiltered.Add(pair))
            {
                _world.SetPairCollisionEnabled(pair.Item1, pair.Item2, false);
            }
        }
    }

    /// <summary>肘の骨の後ろで一緒に打ち込む質量 [kg]: 腕と体の有効質量（歩の前の速さの向きで）から、その骨自身を除いたもの。</summary>
    private float ElbowMassBehind(Fighter attacker, Hand side, VoxelPart striker, BodyState before)
    {
        var speed = before.LinearVelocity.Length();
        if (speed < 1e-3f)
        {
            return 0f;
        }

        var factor = BodyFactor(attacker, before.LinearVelocity, before.Position, attacker.ShoulderPosition(side), attacker.Parts.Chest, PreStepBodyVelocity(OpponentOf(attacker)), before.LinearVelocity);
        var total = attacker.ElbowImpactMass(side, before.Position, before.LinearVelocity / speed, out _, factor);
        return MathF.Max(0f, total - _world.GetMass(striker.Body.BodyId));
    }

    private void Elbow(Fighter attacker, Hand side, Fighter victim, VoxelPart striker, VoxelPart part, VoxelContactResult hit)
    {
        if (!hit.Touched)
        {
            return;
        }

        _recentCuts.Add(hit);
        var contactTwist = Vector3.Zero;
        if (part.Kind == BodyPartKind.Head && part.FragmentOf is null && hit.ImpulseOnB.LengthSquared() > 0f)
        {
            var head = _world.GetState(part.Body.BodyId);
            contactTwist = Vector3.Cross(hit.Point - head.Position, hit.ImpulseOnB);
        }

        if (!(part.FragmentOf is null && ScoreElbow(attacker, side, victim, striker, part, hit, contactTwist)))
        {
            if (contactTwist != Vector3.Zero && MayStrike(victim))
            {
                victim.TwistHead(contactTwist);
            }

            NoteContact(hit);
        }

        Wound(striker.Body, null, victim, part, hit);
        // 打った側の腕の骨も、相手の硬い所に当たれば曲げられる・削られる（折れた腕は肘を出せない）。
        if (attacker.Voxels is { } own)
        {
            if (hit.BentFor(striker.Body) is { } bend && own.BreakBent(striker.Body.BodyId, bend) is { } bent)
            {
                BreakBone(attacker, bent, 1f);
            }

            if (hit.RemovedA.Count > 0 && own.BreakIfSevered(striker.Body.BodyId) is { } severed)
            {
                BreakBone(attacker, severed, 1f);
            }
        }
    }

    /// <summary>肘打ちを打撃（<see cref="HitEvent"/>）として数える: 拳・蹴りと同じ物差し（速さ・有効質量・部位の倍率）。数えたら true。</summary>
    private bool ScoreElbow(Fighter attacker, Hand side, Fighter victim, VoxelPart striker, VoxelPart part, VoxelContactResult hit, Vector3 contactTwist)
    {
        if (RoundOver || Holding || !MayStrike(victim) || _elbowCooldown.GetValueOrDefault((attacker, side)) > 0f
            || !_beforeStep.TryGetValue(striker.Body.BodyId, out var before))
        {
            return false;
        }

        // 当たる速さは打つ点と打たれた点の相対速度（歩の前。PunchMechanics.ClosingSpeed）。向き（velocity）は打つ側の速度のまま。
        var velocity = before.LinearVelocity + Vector3.Cross(before.AngularVelocity, hit.Point - before.Position);
        var struck = PreStepVelocityAt(part.Body.BodyId, hit.Point);
        var speed = PunchMechanics.ClosingSpeed(velocity, struck, hit.Direction);
        if (speed <= 0f)
        {
            return false;
        }

        var drive = BodyFactor(attacker, velocity, hit.Point, attacker.ShoulderPosition(side), attacker.Parts.Chest, struck, hit.Direction);
        var impactMass = attacker.ElbowImpactMass(side, hit.Point, hit.Direction, out var bodyShare, drive);
        var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
        var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
        var damage = PunchMechanics.Damage(impactMass * speed) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
        if (damage < MinimumDamage)
        {
            return false;
        }

        _elbowCooldown[(attacker, side)] = RehitCooldown;
        var face = hit.A == part.Body ? hit.FaceB : hit.FaceA;
        ApplyHit(new HitEvent(attacker, side, victim, zone, hit.Point, speed, impactMass, bodyShare, damage, face, Elbow: true), part.Body.BodyId, velocity, carve: false, alsoTwist: contactTwist);
        return true;
    }

    /// <summary>
    /// グローブが相手の体に当たった: 拳の打撃（<see cref="HitEvent"/>）として数える。拳の速さ（歩の前）の、当たる向きの分が打撃の速さ、
    /// 乗る質量は拳の有効質量（<see cref="Fighter.ImpactMass(Hand, Vector3)"/>）。ダメージが小さすぎる触れ合い・押し合いは数えない（拳の打撃と同じ）。
    /// 体を削るのはボクセル同士の接触が壊した分だけ（エネルギーで掘る近道は使わない）。数えたら true。
    /// </summary>
    private bool Punch(Fighter attacker, Hand hand, Fighter victim, VoxelPart part, VoxelContactResult hit, Vector3 contactTwist, bool legacyImpact, Vector3? pointVelocity = null)
    {
        if (RoundOver || Holding || !MayStrike(victim) || _hitCooldown[(attacker, hand)] > 0f)
        {
            return false; // ダウンから立ち直った直後の一呼吸も、拳の打撃は数えない
        }

        // 当たる速さは拳と打たれた点の相対速度（歩の前。PunchMechanics.ClosingSpeed）: 下がる相手に付いたまま押す拳は打撃にならない。
        // 向き（velocity: へこみ・押し込み・体の有効質量の向き）は拳の速度のまま。
        var velocity = pointVelocity ?? _preStepGloveVelocity[(attacker, hand)];
        var struck = PreStepVelocityAt(part.Body.BodyId, hit.Point);
        var speed = PunchMechanics.ClosingSpeed(velocity, struck, hit.Direction);
        if (speed <= 0f)
        {
            return false;
        }

        if (attacker.UsesArticulatedImpact)
        {
            return ArticulatedPunch(attacker, hand, victim, part, hit, contactTwist, velocity, speed);
        }

        float impactMass;
        var bodyShare = 0f;
        if (!legacyImpact)
        {
            // 接触ソルバーが実際に渡した法線方向の力積だけを得点の運動量に使う。
            impactMass = MathF.Abs(Vector3.Dot(hit.ImpulseOnB, hit.Normal)) / speed;
        }
        else
        {
            var drive = BodyFactor(attacker, velocity, hit.Point, attacker.ShoulderPosition(hand), attacker.Parts.Chest, struck, hit.Direction);
            impactMass = attacker.ImpactMass(hand, velocity, out bodyShare, drive);
        }
        var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
        var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
        var damage = PunchMechanics.Damage(impactMass * speed) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
        if (damage < MinimumDamage)
        {
            return false;
        }

        _hitCooldown[(attacker, hand)] = RehitCooldown;
        if (!legacyImpact && contactTwist != Vector3.Zero)
        {
            victim.TwistHead(contactTwist);
        }

        // 打った面の材質（はめた物の側）: 打撃音を、グローブ・素手・鉄で変える。
        var face = hit.A == part.Body ? hit.FaceB : hit.FaceA;
        ApplyHit(new HitEvent(attacker, hand, victim, zone, hit.Point, speed, impactMass, bodyShare, damage, face),
            part.Body.BodyId, velocity, carve: false, alsoTwist: contactTwist, legacyWornImpact: legacyImpact);
        return true;
    }

    /// <summary>
    /// グローブの打撃を、関節で連なった両者の体の衝突として解く（<see cref="FighterTuning.ArticulatedImpact"/>）。
    /// 力積 J = (1 + 反発) × 近づく速さ × 換算質量（1/m打つ側 + 1/m打たれる側 の逆数）。m は当たった点・当たる向きの <see cref="Fighter.ArticulatedMass"/>。
    /// ボクセルの接触が 2 つの剛体（グローブと打たれた部位）だけで渡した法線の力積との差を、当たった点に作用・反作用で加える（体への伝わりは関節の拘束が解く）。
    /// 頭なら、点のずれが回した分を接触の回しに足して脳しんとう。得点の運動量は J（乗った質量は J ÷ 速さ）。
    /// </summary>
    private bool ArticulatedPunch(Fighter attacker, Hand hand, Fighter victim, VoxelPart part, VoxelContactResult hit, Vector3 contactTwist, Vector3 velocity, float speed)
    {
        var glove = hit.A == part.Body ? hit.B : hit.A;
        var direction = hit.Direction.LengthSquared() > 1e-12f ? Vector3.Normalize(hit.Direction) : Vector3.Normalize(velocity);
        var striker = attacker.ArticulatedMass(glove.BodyId, hit.Point, direction, speed);
        var struckMass = victim.ArticulatedMass(part.Body.BodyId, hit.Point, direction, speed);
        var reduced = 1f / ((1f / MathF.Max(1e-3f, striker)) + (1f / MathF.Max(1e-3f, struckMass)));
        var impulse = (1f + VoxelContact.Restitution) * speed * reduced;
        var impactMass = impulse / speed;
        var (kind, normalized, armHand) = victim.Locate(part.Body.BodyId, hit.Point);
        var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
        var damage = PunchMechanics.Damage(impulse) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
        if (damage < MinimumDamage)
        {
            return false;
        }

        _hitCooldown[(attacker, hand)] = RehitCooldown;
        // 接触が 2 つの剛体だけで渡した分との差を、当たった点に作用・反作用で加える。
        var given = MathF.Abs(Vector3.Dot(hit.ImpulseOnB, hit.Normal));
        var extra = direction * MathF.Max(0f, impulse - given);
        var struckState = _world.GetState(part.Body.BodyId);
        var gloveState = _world.GetState(glove.BodyId);
        var twist = Vector3.Cross(hit.Point - struckState.Position, extra);
        _world.ApplyLinearImpulse(part.Body.BodyId, extra);
        _world.ApplyAngularImpulse(part.Body.BodyId, twist);
        _world.ApplyLinearImpulse(glove.BodyId, -extra);
        _world.ApplyAngularImpulse(glove.BodyId, Vector3.Cross(hit.Point - gloveState.Position, -extra));
        if (part.Body.BodyId == victim.Parts.Head)
        {
            victim.TwistHead(contactTwist + twist); // 頭がねじられた分だけ脳しんとう（接触の回しと、足した力積の回し）
        }

        var bodyShare = striker > 1e-3f ? Math.Clamp((striker - attacker.ArmMass(hand)) / striker, 0f, 1f) : 0f;
        var face = hit.A == part.Body ? hit.FaceB : hit.FaceA;
        ApplyHit(new HitEvent(attacker, hand, victim, zone, hit.Point, speed, impactMass, bodyShare, damage, face),
            part.Body.BodyId, velocity, carve: false, alsoTwist: contactTwist, carryBody: false);
        return true;
    }

    /// <summary>
    /// 刃が振り抜けた所の、斬った側の動き:当たった所の刃の速さ（打たれた部位に対して、歩の前の姿勢から）と、刃の向き（ワールド）。
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

    /// <summary>武器ごとの、つながりを最後に調べたときの格子の形の版（削れていなければ調べ直さない）。</summary>
    private readonly Dictionary<Weapon, int> _connectedAt = [];
    private readonly List<Weapon> _disconnectScratch = [];

    /// <summary>
    /// 削れて途中のセルが無くなり、つながっていない部分が出た武器を分ける（物理: つながっていない物は別の物）。握っている手に近い部分
    /// （握られていなければ最も大きい部分）が元の剛体に残り、ほかの部分は折れて分かれたとき（<see cref="Snapped"/>）と同じ道で、元と同じ材料の
    /// 別の剛体として落ちる。格子が削れた歩（形の版が変わった武器）だけ調べる。手にはめる物は握り方が別（手に付く）なので対象外（CLAUDE.md §8）。
    /// </summary>
    private void SplitDisconnectedWeapons()
    {
        if (_weapons.Count == 0)
        {
            return;
        }

        var profileStart = StepProfile.Begin();
        _disconnectScratch.Clear();
        foreach (var weapon in _weapons)
        {
            if ((!Weapon.IsWorn(weapon.Kind) || weapon.Kind == WeaponKind.SickleClaw)
                && (!_connectedAt.TryGetValue(weapon, out var version) || version != weapon.Body.Grid.GeometryVersion))
            {
                _disconnectScratch.Add(weapon);
            }
        }

        foreach (var weapon in _disconnectScratch)
        {
            var body = weapon.Body;
            Vector3? keepNear = HolderOf(weapon) is { } holder
                ? (holder.WieldedGrip(HandOf(weapon)) ?? Weapon.GripOf(weapon.Kind)) - body.GridCenterLocal
                : null;
            foreach (var piece in body.SeverDisconnected(keepNear))
            {
                Snapped(weapon, piece);
            }

            _connectedAt[weapon] = body.Grid.GeometryVersion;
        }

        StepProfile.End(StepProfile.Slot.Disconnect, profileStart);
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
        var profileStart = StepProfile.Begin();
        ShatterCore(hit, a, outOfA, b, outOfB, fatal, blade);
        StepProfile.End(StepProfile.Slot.Debris, profileStart);
    }

    private void ShatterCore(VoxelContactResult hit, VoxelBody a, Vector3 outOfA, VoxelBody b, Vector3 outOfB, bool fatal, Weapon? blade = null)
    {
        var removed = hit.RemovedA.Count + hit.RemovedB.Count;
        if (removed == 0 && !fatal)
        {
            return;
        }

        var strength = Math.Clamp(removed / 80f, 0.15f, 1f);
        var chipSpeed = _tuning.ChipSpeed * (0.5f + (1.3f * strength));
        _chips.Clear();
        _chunks.Clear();
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

        SpawnChunks(_chunks, Vector3.Zero);
        materials.AddRange(_chips.Select(c => c.Material));
        materials.AddRange(_chunks.SelectMany(c => c.Cells.Select(cell => cell.Material)));
        var grains = new ShatterGrain[materials.Count];
        for (var i = 0; i < grains.Length; i++)
        {
            grains[i] = new ShatterGrain(materials[i], MathF.Min(0.3f, (i * 0.0035f) + (0.002f * (1f + Jitter()))));
        }

        _recentShatters.Add(new ShatterEvent(hit.Point, grains, fatal));
    }

    /// <summary>
    /// 刃のひと太刀で剥がれたボクセル: 格子の上で面でつながっている同じ組織のもの（<see cref="VoxelClumps"/>）は 1 つの塊になって剥がれ、刃が振り抜けた向き（<paramref name="swing"/>）へ、
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
        var dir = speed > 0.3357f ? swing / speed : -outward;
        var across = blade - (Vector3.Dot(blade, dir) * dir);
        across = across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : Vector3.Normalize(Vector3.Cross(dir, outward) + new Vector3(1e-4f));
        var side = Vector3.Cross(dir, across);
        var s = _world.GetState(body.BodyId);
        var toWorld = body.GridToWorld(s);
        var cell = body.Grid.CellSize;
        var fling = MathF.Max(chipSpeed, speed * SprayShare);
        Vector3 Heading(float spreadAcross, float spreadSide) => Vector3.Normalize(dir + (across * (spreadAcross * Jitter())) + (side * (spreadSide * Jitter())));
        Vector3 PointVelocity(Vector3 p) => s.LinearVelocity + Vector3.Cross(s.AngularVelocity, p - s.Position);

        var palette = body.Grid.Palette; // 体ごとの材質の表（ガルヴァの外骨格は BoxerAnatomy に無い材質）
        foreach (var members in VoxelClumps.Of(body.Grid, removed))
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
            var spin = Vector3.Cross(outward, dir) * (speed * 8.875f); // [rad/s per m/s] = 1/m
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
                    new Vector3(Jitter(), Jitter(), Jitter()) * 23.83f, BloodColor, life: 3.692f, tag: VoxelBreakSound.BloodTag);
            }
        }

        void Single(RemovedVoxel v, Vector3 at, Vector3 velocity, float size)
        {
            var material = palette[v.Material];
            var color = material.Tissue == VoxelTissue.Organ ? OrganSpark : v.WasSurface ? material.SurfaceColor : material.Color;
            var trail = material.Tissue == VoxelTissue.Bone ? BoneTrail : ChipTrail;
            _debris.Spawn(at, velocity, s.Orientation, cell * (0.9f * size), new Vector3(Jitter(), Jitter(), Jitter()) * 17.87f, color, glow: material.Tissue == VoxelTissue.Organ,
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
        var grid = body.Grid;
        var groups = VoxelClumps.Of(grid, removed);
        var clumpAt = VoxelClumps.CellsOf(grid, removed, groups);

        for (var g = 0; g < groups.Count; g++)
        {
            var members = groups[g];
            var center = members.Count == 1 ? removed[members[0]].LocalCenter : members.Aggregate(Vector3.Zero, (sum, i) => sum + removed[i].LocalCenter) / members.Count;
            var position = Vector3.Transform(center, toWorld);
            var scatter = new Vector3(Jitter(), Jitter(), Jitter());
            var heading = (outward * 0.6f) + (along * 0.5f) + (scatter * 0.35f);
            var velocity = s.LinearVelocity + Vector3.Cross(s.AngularVelocity, position - s.Position)
                + (Vector3.Normalize(heading + new Vector3(1e-4f)) * chipSpeed * (1f + (0.25f * Jitter())));
            if (members.Count == 1)
            {
                var v = removed[members[0]];
                _chips.Add(new VoxelChip(position, velocity, s.Orientation, grid.CellSize, v.Material, v.WasSurface));
                continue;
            }

            // 塊: 1 つの剛体の見た目として回りながら飛び、剥がれた断面（体に残ったセル・別の塊に面していた面）だけが一瞬光る。
            var cells = new VoxelChunkCell[members.Count];
            for (var k = 0; k < members.Count; k++)
            {
                var v = removed[members[k]];
                cells[k] = new VoxelChunkCell(v.LocalCenter - center, v.Material, v.WasSurface, VoxelClumps.TornFaces(grid, v.LocalCenter, clumpAt, g));
            }

            _chunks.Add(new VoxelChunk(position, velocity, s.Orientation, s.AngularVelocity, grid.CellSize, cells));
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
