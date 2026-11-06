using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;

namespace MiniRing;

/// <summary>ホストから毎ステップ渡される入力: プレイヤーの指令と、ラウンドのやり直し要求。</summary>
public readonly record struct BoxingInput(FighterCommand Player, bool ResetRequested = false);

/// <summary>
/// 試合本体（ホスト非依存）: リング・2 体のファイター・CPU・判定・ラウンド進行・描画データ。
/// デスクトップは Aigis の <see cref="GameHost"/> から、VR は OpenXR ホストから同じ <see cref="Step"/> を呼ぶ。
/// <para>物理は内部で約 240Hz にサブステップする（60cm の小さな体を 60Hz・1 サブステップで回すと
/// サーボが発散しやすい／Aigis 調査メモ）。ホストの dt は 60Hz でも 90Hz でもよい。</para>
/// </summary>
public sealed class BoxingGame : IGame<BoxingInput>
{
    public const float MaxHp = 100f;
    private const float SubstepRate = 240f;
    private const float RoundResetDelay = 3.5f;
    private const float RehitCooldown = 0.18f;

    private readonly FighterTuning _tuning;
    private readonly int _seed;
    private readonly List<HitEvent> _recentHits = [];
    private readonly Dictionary<(Fighter, Hand), Vector3> _preStepGloveVelocity = [];
    private readonly Dictionary<(Fighter, Hand), float> _hitCooldown = [];
    private PhysicsWorld _world = null!;
    private Ring _ring = null!;
    private AiControl _ai = null!;
    private float _roundOverTimer;
    private string _roundMessage = "";
    private float _messageTimer;
    private int _round;

    public BoxingGame(FighterTuning? tuning = null, int seed = 1)
    {
        _tuning = tuning ?? FighterTuning.Default;
        _seed = seed;
        StartRound();
    }

    public PhysicsWorld World => _world;
    public Ring Ring => _ring;
    public Fighter Player { get; private set; } = null!;
    public Fighter Cpu { get; private set; } = null!;
    public float PlayerHp { get; private set; }
    public float CpuHp { get; private set; }
    public int PlayerWins { get; private set; }
    public int CpuWins { get; private set; }
    public bool RoundOver => _roundOverTimer > 0f;
    public string Message => _messageTimer > 0f ? _roundMessage : "";

    /// <summary>直近の <see cref="Step"/> で発生したヒット（ホストの効果音・ハプティクス用）。</summary>
    public IReadOnlyList<HitEvent> RecentHits => _recentHits;

    public string StatusLine => $"Round {_round}  You {PlayerHp:0} - CPU {CpuHp:0}  ({PlayerWins}-{CpuWins})";

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
        var cpuCommand = _ai.Read(Sense(Cpu, Player, PlayerHp), dt);
        Drive(Player, Cpu, playerCommand, dt);
        Drive(Cpu, Player, cpuCommand, dt);

        foreach (var fighter in new[] { Player, Cpu })
        {
            foreach (var hand in new[] { Hand.Left, Hand.Right })
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
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            if (self.UpdateGrab(hand, command.For(hand).Grip, opponent) is not null)
            {
                ShowMessage(self == Player ? "THROW!" : "THROWN!", 1f);
            }
        }

        self.ApplyControl(command, dt);
    }

    /// <summary>
    /// 物理の接触一覧からグローブ（前腕）× 相手の部位を拾い、衝突直前のグローブ速度のうち
    /// 相手へ踏み込む成分だけでダメージを計算する（相手が歩いてガードに当たっただけでは減らない）。
    /// </summary>
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

    private void TryHit(Fighter attacker, Fighter victim, int attackerBody, int victimBody)
    {
        // 相手の前腕（＝グローブ）との接触はガード／打ち合いとして数えない（両者にダメージが出るのを防ぐ）。
        if (!victim.Owns(victimBody) || victim.IsForearm(victimBody))
        {
            return;
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            if (attacker.ForearmId(hand) != attackerBody || _hitCooldown[(attacker, hand)] > 0f)
            {
                continue;
            }

            // 前腕のどこが当たっても接触は出るので、拳（グローブ球）の近くで当たったものだけをパンチとする（肘打ちを除外）。
            var glove = attacker.GloveCenter(hand);
            if (victim.DistanceToPart(victimBody, glove) > FighterTuning.Default.GloveRadius * 1.6f)
            {
                continue;
            }

            var into = _world.GetState(victimBody).Position - glove;
            var speed = into.LengthSquared() > 1e-8f ? MathF.Max(0f, Vector3.Dot(_preStepGloveVelocity[(attacker, hand)], Vector3.Normalize(into))) : 0f;
            var zone = victimBody == victim.Parts.Head ? HitZone.Head
                : victimBody == victim.Parts.Chest || victimBody == victim.Parts.Pelvis ? HitZone.Body
                : HitZone.Limb;
            var damage = PunchMath.Damage(speed, zone);
            if (damage <= 0f)
            {
                continue;
            }

            _hitCooldown[(attacker, hand)] = RehitCooldown;
            ApplyDamage(new HitEvent(attacker, hand, victim, zone, glove, speed, damage), _preStepGloveVelocity[(attacker, hand)]);
        }
    }

    private void ApplyDamage(HitEvent hit, Vector3 direction)
    {
        if (RoundOver)
        {
            return;
        }

        var victimIsPlayer = hit.Victim == Player;
        var hp = MathF.Max(0f, (victimIsPlayer ? PlayerHp : CpuHp) - hit.Damage);
        if (victimIsPlayer)
        {
            PlayerHp = hp;
        }
        else
        {
            CpuHp = hp;
        }

        hit.Victim.AddStun(PunchMath.Stun(hit.Damage), Vector3.Zero);
        _recentHits.Add(hit);
        if (hp <= 0f)
        {
            hit.Victim.Knockdown(direction, permanent: true);
            EndRound(cpuWon: victimIsPlayer, "K.O.");
        }
        else if (PunchMath.IsKnockdown(hit.Damage))
        {
            hit.Victim.Knockdown(direction, permanent: false);
            ShowMessage("DOWN!", 1.2f);
        }
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
    /// ワールドごと作り直す。
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
        _ai = new AiControl(_seed + _round, 0.5f);
        PlayerHp = MaxHp;
        CpuHp = MaxHp;
        _roundOverTimer = 0f;
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
    public static FighterSense Sense(Fighter self, Fighter opponent, float opponentHp)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(opponent);
        return new FighterSense(self.Frame, self.ArmReach, opponent.HeadPosition, opponent.ChestPosition, opponentHp > 0f);
    }

    public FighterSense PlayerSense => Sense(Player, Cpu, CpuHp);

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
        => BoxingRenderer.Build(this, showTargetGhosts: true);

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        var white = new Vector4(1f, 1f, 1f, 1f);
        var list = new List<ScreenTextDraw>
        {
            new($"YOU {PlayerHp:0}", new Vector2(24f, 20f), 28f, new Vector4(1f, 0.45f, 0.4f, 1f)),
            new($"CPU {CpuHp:0}", new Vector2(screenSizePixels.X - 24f, 20f), 28f, new Vector4(0.5f, 0.65f, 1f, 1f), TextAnchor.Right),
            new($"{PlayerWins} - {CpuWins}", new Vector2(screenSizePixels.X * 0.5f, 20f), 28f, white, TextAnchor.Center),
        };
        if (Message.Length > 0)
        {
            list.Add(new ScreenTextDraw(Message, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y * 0.3f), 56f, white, TextAnchor.Center));
        }

        return list;
    }

    public void Dispose() => _world.Dispose();
}
