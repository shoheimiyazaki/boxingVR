using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;
using MiniRing.LowPoly;

namespace MiniRing;

/// <summary>ホストから毎ステップ渡される入力: プレイヤーの指令と、ラウンドのやり直し要求。</summary>
public readonly record struct BoxingInput(FighterCommand Player, bool ResetRequested = false);

/// <summary>
/// 試合本体（ホスト非依存）: リング・2 体のファイター・CPU・判定・ラウンド進行・描画データ。
/// デスクトップは Aigis の <see cref="GameHost"/> から、VR は OpenXR ホストから同じ <see cref="Step"/> を呼ぶ。
/// <para>物理は内部で約 240Hz にサブステップする（60cm の小さな体を 60Hz・1 サブステップで回すと
/// サーボが発散しやすい／Aigis 調査メモ）。ホストの dt は 60Hz でも 90Hz でもよい。</para>
/// <para>勝敗: 意識が尽きたら KO、倒れて 10 秒立てなければ KO（脚を壊されて立てない場合も）、リングから落ちたら負け。</para>
/// </summary>
public sealed class BoxingGame : IGame<BoxingInput>
{
    public const float CountSeconds = 10f;
    private const float SubstepRate = 240f;
    private const float RoundResetDelay = 3.5f;
    private const float RehitCooldown = 0.18f;
    private const float MinimumDamage = 1f;

    /// <summary>拳に乗った質量のうち、物理の接触では伝わらない体の分を相手へ押し込む割合（反発を含めた運動量の受け渡し）。</summary>
    private const float KnockbackTransfer = 0.6f;

    /// <summary>1 発で相手の全身に与える速度変化の上限 [m/s]（吹き飛ばしすぎない）。</summary>
    private const float MaxKnockbackSpeed = 0.6f;

    private static readonly Hand[] Hands = [Hand.Left, Hand.Right];

    private readonly FighterTuning _tuning;
    private readonly BoxerModel _model;
    private readonly int _seed;
    private readonly List<HitEvent> _recentHits = [];
    private readonly Dictionary<(Fighter, Hand), Vector3> _preStepGloveVelocity = [];
    private readonly Dictionary<(Fighter, Hand), float> _hitCooldown = [];
    private readonly List<PrimitiveInstance> _renderInstances = [];
    private readonly List<DebugLine> _renderLines = [];
    private PhysicsWorld _world = null!;
    private Fighter[] _fighters = [];
    private Ring _ring = null!;
    private AiControl _ai = null!;
    private float _roundOverTimer;
    private string _roundMessage = "";
    private float _messageTimer;
    private int _round;

    public BoxingGame(FighterTuning? tuning = null, int seed = 1)
    {
        _tuning = tuning ?? FighterTuning.Default;
        _model = BoxerModel.Build(_tuning.HalfExtents, _tuning.ModelSubdivisions);
        _seed = seed;
        StartRound();
    }

    public PhysicsWorld World => _world;

    /// <summary>CPU の乱数の種（リプレイで同じ試合を作り直すため）。</summary>
    public int Seed => _seed;

    /// <summary>ファイターの調整値（リプレイで同じ試合を作り直すため）。</summary>
    public FighterTuning Tuning => _tuning;
    public Ring Ring => _ring;
    public Fighter Player { get; private set; } = null!;
    public Fighter Cpu { get; private set; } = null!;

    /// <summary>見た目の体（打撃でへこむローポリ）。ファイターと同じくラウンドごとに新品。</summary>
    public BoxerSkin PlayerSkin { get; private set; } = null!;
    public BoxerSkin CpuSkin { get; private set; } = null!;

    /// <summary>この試合で使っている体のメッシュ（部位ごとの変形前の形）。</summary>
    public BoxerModel Model => _model;

    public BoxerSkin SkinOf(Fighter fighter) => fighter == Player ? PlayerSkin
        : fighter == Cpu ? CpuSkin
        : throw new ArgumentException($"この試合のファイターではありません: {fighter?.Name}", nameof(fighter));
    public int PlayerWins { get; private set; }
    public int CpuWins { get; private set; }
    public bool RoundOver => _roundOverTimer > 0f;
    public string Message => _messageTimer > 0f ? _roundMessage : "";

    /// <summary>直近のヒット（HUD 表示用。次のヒットまで残る）。</summary>
    public HitEvent? LastHit { get; private set; }

    /// <summary>直近の <see cref="Step"/> で発生したヒット（ホストの効果音・ハプティクス用）。</summary>
    public IReadOnlyList<HitEvent> RecentHits => _recentHits;

    public string StatusLine => string.Create(CultureInfo.InvariantCulture,
        $"Round {_round}  You {Cpu.Condition.TotalDamage:0} - CPU {Player.Condition.TotalDamage:0} damage  ({PlayerWins}-{CpuWins})");

    public bool IsOver => false;

    public static Vector3 RingCenter { get; } = new(0f, 0f, 0.95f);
    public const float RingSize = 1.2f;
    public const float RingSurfaceHeight = 0.75f;

    public void Step(BoxingInput input, float dt)
    {
        _recentHits.Clear();
        if (input.ResetRequested)
        {
            StartRound();
            return;
        }

        var substeps = Math.Max(1, (int)MathF.Ceiling(dt * SubstepRate));
        var sub = dt / substeps;
        for (var i = 0; i < substeps; i++)
        {
            Substep(input.Player, sub);
        }

        _messageTimer = MathF.Max(0f, _messageTimer - dt);
        UpdateRound(dt);
    }

    private void Substep(in FighterCommand playerCommand, float dt)
    {
        var cpuCommand = _ai.Read(Sense(Cpu, Player), dt);
        Drive(Player, Cpu, playerCommand, dt);
        Drive(Cpu, Player, cpuCommand, dt);

        foreach (var fighter in Fighters)
        {
            foreach (var hand in Hands)
            {
                _preStepGloveVelocity[(fighter, hand)] = fighter.GloveVelocity(hand);
                _hitCooldown[(fighter, hand)] = MathF.Max(0f, _hitCooldown.GetValueOrDefault((fighter, hand)) - dt);
            }
        }

        _world.Step(dt);
        DetectHits();
    }

    private void Drive(Fighter self, Fighter opponent, in FighterCommand command, float dt)
    {
        foreach (var hand in Hands)
        {
            if (self.UpdateGrab(hand, command.For(hand).Grip, opponent) is not null)
            {
                ShowMessage(self == Player ? "THROW!" : "THROWN!", 1f);
            }
        }

        self.ApplyControl(command, dt);
    }

    /// <summary>物理の接触一覧から「拳（前腕の先）× 相手の部位」を拾う。</summary>
    private void DetectHits()
    {
        foreach (var contact in _world.Contacts())
        {
            if (contact.StaticA || contact.StaticB)
            {
                continue;
            }

            TryHit(Player, Cpu, contact.A, contact.B);
            TryHit(Player, Cpu, contact.B, contact.A);
            TryHit(Cpu, Player, contact.A, contact.B);
            TryHit(Cpu, Player, contact.B, contact.A);
        }
    }

    /// <summary>
    /// 1 発の判定。衝撃 = 拳に乗った質量（<see cref="Fighter.ImpactMass(Hand, Vector3)"/>: 姿勢と向きだけで決まる）
    /// × 衝突直前に相手へ踏み込む拳の速さ。急所の判定は当たった点の部位ローカル位置から（<see cref="HitZones"/>）。
    /// </summary>
    private void TryHit(Fighter attacker, Fighter victim, int attackerBody, int victimBody)
    {
        // 倒れている相手（起き上がり中を含む）への打撃は数えない: ダウンはカウントで決着させる。
        if (!victim.Owns(victimBody) || victim.State != FighterState.Standing)
        {
            return;
        }

        foreach (var hand in Hands)
        {
            if (attacker.ForearmId(hand) != attackerBody || _hitCooldown[(attacker, hand)] > 0f)
            {
                continue;
            }

            // 前腕のどこが当たっても接触は出るので、拳（グローブ球）の近くで当たったものだけをパンチとする（肘打ちを除外）。
            var glove = attacker.GloveCenter(hand);
            if (victim.DistanceToPart(victimBody, glove) > _tuning.GloveRadius * 1.6f)
            {
                continue;
            }

            var velocity = _preStepGloveVelocity[(attacker, hand)];
            var into = _world.GetState(victimBody).Position - glove;
            var speed = into.LengthSquared() > 1e-8f ? MathF.Max(0f, Vector3.Dot(velocity, Vector3.Normalize(into))) : 0f;
            if (speed <= 0f)
            {
                continue;
            }

            var impactMass = attacker.ImpactMass(hand, velocity, out var bodyShare);
            var (kind, normalized, armHand) = victim.Locate(victimBody, glove);
            var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
            var damage = PunchMechanics.Damage(impactMass * speed) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
            if (damage < MinimumDamage)
            {
                continue; // 触れただけ・押し合い
            }

            _hitCooldown[(attacker, hand)] = RehitCooldown;
            ApplyHit(new HitEvent(attacker, hand, victim, zone, glove, speed, impactMass, bodyShare, damage), victimBody, velocity);
        }
    }

    private void ApplyHit(HitEvent hit, int victimBody, Vector3 gloveVelocity)
    {
        if (RoundOver)
        {
            return;
        }

        var victim = hit.Victim;
        // ダメージは記録だけ（見た目と得点）。動きは落とさない。
        victim.Condition.ApplyHit(hit.Zone, hit.Damage);

        // 当たった部位の、拳に近いノードを拳の進む向きへへこませる（見た目だけ。当たり判定の箱は変わらない）。
        SkinOf(victim).Dent(victimBody, _world.GetState(victimBody), hit.Point, gloveVelocity, hit.Damage);

        // 体重の乗った拳は相手を押し込む: 物理の接触が運ぶのは腕の質量だけなので、体から来た分の運動量を足す。
        // 1 部位に入れると千切れるほど速くなるので、相手の全身へ一様な速度変化として配り（上限付き）、
        // 同じ運動量の反作用を打った側の全身へ返す（運動量は湧かない）。
        var extraMass = hit.ImpactMass - hit.Attacker.ArmMass(hit.Hand);
        if (extraMass > 0f && gloveVelocity.LengthSquared() > 1e-8f)
        {
            var momentum = extraMass * hit.Speed * KnockbackTransfer;
            var push = Vector3.Normalize(gloveVelocity) * MathF.Min(MaxKnockbackSpeed, momentum / victim.TotalMass);
            victim.Push(push);
            hit.Attacker.Push(-push * (victim.TotalMass / hit.Attacker.TotalMass));
        }

        _recentHits.Add(hit);
        LastHit = hit;
    }

    private void UpdateRound(float dt)
    {
        if (RoundOver)
        {
            _roundOverTimer -= dt;
            if (_roundOverTimer <= 0f)
            {
                StartRound();
            }

            return;
        }

        if (_ring.IsOut(Player.PelvisPosition))
        {
            EndRound(cpuWon: true, "RING OUT");
        }
        else if (_ring.IsOut(Cpu.PelvisPosition))
        {
            EndRound(cpuWon: false, "RING OUT");
        }
        else if (Player.DownTime > CountSeconds)
        {
            EndRound(cpuWon: true, "10 COUNT K.O.");
        }
        else if (Cpu.DownTime > CountSeconds)
        {
            EndRound(cpuWon: false, "10 COUNT K.O.");
        }
    }

    private void EndRound(bool cpuWon, string reason)
    {
        if (RoundOver)
        {
            return;
        }

        if (cpuWon)
        {
            CpuWins++;
        }
        else
        {
            PlayerWins++;
        }

        _roundOverTimer = RoundResetDelay;
        ShowMessage($"{reason}  {(cpuWon ? "CPU WINS" : "YOU WIN")}", RoundResetDelay);
    }

    /// <summary>
    /// ラウンドを作り直す。Aigis では剛体の削除がハンドル再利用で危険（architecture.md risk 4）なので、
    /// ワールドごと作り直す（体の状態もラウンドごとに新品）。
    /// </summary>
    private void StartRound()
    {
        _world?.Dispose();
        _round++;
        _world = new PhysicsWorld();
        _ring = new Ring(_world, RingCenter, RingSize, RingSurfaceHeight);
        var surface = _ring.SurfaceCenter;
        Player = Fighter.Spawn(_world, _tuning, "player", surface - new Vector3(0f, 0f, 0.22f), 0f);
        Cpu = Fighter.Spawn(_world, _tuning, "cpu", surface + new Vector3(0f, 0f, 0.22f), MathF.PI);
        PlayerSkin = new BoxerSkin(_model, Player, _tuning);
        CpuSkin = new BoxerSkin(_model, Cpu, _tuning);
        _fighters = [Player, Cpu];
        _ai = new AiControl(_seed + _round, 0.5f);
        _roundOverTimer = 0f;
        LastHit = null;
        _preStepGloveVelocity.Clear();
        _hitCooldown.Clear();
        ShowMessage("FIGHT!", 1.5f);
    }

    private void ShowMessage(string text, float seconds)
    {
        _roundMessage = text;
        _messageTimer = seconds;
    }

    /// <summary>入力元に渡す、<paramref name="self"/> から見た状況。</summary>
    public static FighterSense Sense(Fighter self, Fighter opponent)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(opponent);
        return new FighterSense(self.Frame, self.ArmReach, opponent.HeadPosition, opponent.ChestPosition,
            opponent.State == FighterState.Standing);
    }

    public FighterSense PlayerSense => Sense(Player, Cpu);

    private Fighter[] Fighters => _fighters;

    /// <summary>
    /// このフレームの描画データ。返すリストは使い回しで、次の呼び出しで中身が入れ替わる
    /// （Triage の <c>BuildRenderables</c> と同じ。受け取ったらそのフレームのうちに描くこと）。
    /// </summary>
    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
    {
        BoxingRenderer.Fill(this, showTargetGhosts: true, _renderInstances, _renderLines);
        return (_renderInstances, _renderLines);
    }

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        var white = new Vector4(1f, 1f, 1f, 1f);
        var list = new List<ScreenTextDraw>
        {
            new($"{PlayerWins} - {CpuWins}", new Vector2(screenSizePixels.X * 0.5f, 20f), 28f, white, TextAnchor.Center),
        };
        AddCondition(list, "YOU", Cpu, new Vector2(24f, 20f), TextAnchor.Left, new Vector4(1f, 0.45f, 0.4f, 1f));
        AddCondition(list, "CPU", Player, new Vector2(screenSizePixels.X - 24f, 20f), TextAnchor.Right, new Vector4(0.5f, 0.65f, 1f, 1f));
        if (LastHit is { } hit)
        {
            var who = hit.Attacker == Player ? "YOU" : "CPU";
            var text = $"{who} {(hit.Hand == Hand.Left ? "L" : "R")} > {HitZones.Label(hit.Zone)}  {hit.Damage:0}  " +
                $"{hit.ImpactMass:0.0}kg x {hit.Speed:0.0}m/s  BODY {Percent(hit.BodyShare)}";
            list.Add(new ScreenTextDraw(text, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y - 44f), 22f, white, TextAnchor.Center));
        }

        foreach (var fighter in Fighters)
        {
            if (fighter.DownTime > 0.5f && !RoundOver)
            {
                list.Add(new ScreenTextDraw($"{(fighter == Player ? "YOU" : "CPU")} DOWN  {MathF.Min(CountSeconds, MathF.Floor(fighter.DownTime) + 1f):0}",
                    new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y * 0.42f), 40f, new Vector4(1f, 0.85f, 0.3f, 1f), TextAnchor.Center));
            }
        }

        if (Message.Length > 0)
        {
            list.Add(new ScreenTextDraw(Message, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y * 0.3f), 56f, white, TextAnchor.Center));
        }

        return list;
    }

    private static void AddCondition(List<ScreenTextDraw> list, string name, Fighter opponent, Vector2 at, TextAnchor anchor, Vector4 color)
    {
        // 得点 = 相手に与えたダメージとクリーンヒット数（ダメージは動きに影響しない）。
        var given = opponent.Condition;
        string[] lines =
        [
            name,
            string.Create(CultureInfo.InvariantCulture, $"DAMAGE {given.TotalDamage:0}"),
            string.Create(CultureInfo.InvariantCulture, $"CLEAN HITS {given.CleanHitsTaken}"),
        ];
        for (var i = 0; i < lines.Length; i++)
        {
            var y = i == 0 ? 0f : 12f + (i * 22f);
            list.Add(new ScreenTextDraw(lines[i], at + new Vector2(0f, y), i == 0 ? 26f : 18f, color, anchor));
        }
    }

    private static string Percent(float value) => (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 試合の状態のチェックサム（FNV-1a 64）: 全剛体の位置・姿勢・速度、両者の受けたダメージ、勝敗。
    /// リプレイが記録と同じ動きをしているかを、ビット単位で照合するのに使う。
    /// </summary>
    public ulong StateChecksum()
    {
        var hash = 14695981039346656037UL;
        void Add(float value)
        {
            var bits = (uint)BitConverter.SingleToInt32Bits(value);
            for (var i = 0; i < 4; i++)
            {
                hash = (hash ^ ((bits >> (8 * i)) & 0xFF)) * 1099511628211UL;
            }
        }

        foreach (var body in _world.SnapshotDynamics())
        {
            Add(body.Position.X);
            Add(body.Position.Y);
            Add(body.Position.Z);
            Add(body.Orientation.X);
            Add(body.Orientation.Y);
            Add(body.Orientation.Z);
            Add(body.Orientation.W);
            Add(body.LinearVelocity.X);
            Add(body.LinearVelocity.Y);
            Add(body.LinearVelocity.Z);
        }

        foreach (var fighter in _fighters)
        {
            Add(fighter.Condition.TotalDamage);
            Add(fighter.Condition.CleanHitsTaken);
            Add((float)fighter.State);
        }

        Add(PlayerWins);
        Add(CpuWins);
        Add(_round);
        return hash;
    }

    public void Dispose() => _world.Dispose();
}
