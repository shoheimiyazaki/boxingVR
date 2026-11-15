using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Desktop;

/// <summary>既存の意図操作に、装備と空手に応じた攻撃の経路を重ねる。</summary>
internal sealed class DesktopFighterControl(WeaponKind? weapon = null, bool karate = false)
{
    private readonly IntentControl _intent = new();
    private static readonly WeaponSwing SwordLight = SwordTechnique.Basics.First(t => t.Name == "kote").Swing;
    private static readonly WeaponSwing SwordHeavy = SwordTechnique.Basics.First(t => t.Name == "men").Swing;
    private static readonly WeaponSwing ClubLight = WeaponSwing.Default(SwingFamily.Thrust);
    private static readonly WeaponSwing ClubHeavy = WeaponSwing.Default(SwingFamily.Chop);
    private WeaponSwing? _swing;
    private SwordMotion? _form;
    private float _formTime;
    private static readonly FighterTuning Body = FighterTuning.Default;
    private KickSpec? _kick;
    private Vector3 _kickAim;
    private float _time;
    private bool _light, _heavy;

    public FighterCommand Read(FighterIntent input, in FighterSense sense, float dt)
    {
        var light = input.Light && !_light;
        var heavy = input.Heavy && !_heavy;
        (_light, _heavy) = (input.Light, input.Heavy);
        _intent.Input = input with { Light = input.Light && !sense.SelfWeapon.Held, Heavy = input.Heavy && !sense.SelfWeapon.Held && !karate };
        var command = _intent.Read(sense, dt);

        if (FormSwordsman.Handles(weapon) && sense.SelfWeapon.Held)
        {
            // 刀・ロングソード: 弱・強は、その武器の型（FormSwordsman。ボットと同じ型・同じ ToCommand）。両手の構えから型を目標の経路として出す。
            var kind = weapon!.Value;
            if (_form is { } running && _formTime >= running.Duration)
            {
                _form = null;
            }

            if (_form is null && !input.Evade && (light || heavy))
            {
                var (lightForm, heavyForm) = FormSwordsman.PlayerForms(kind);
                _form = heavy ? heavyForm : lightForm;
                _formTime = 0f;
            }

            var held = _form is { } form
                ? FormSwordsman.Command(form, _formTime, sense, command.FaceDirection, Body.WalkSpeed, Body.MaxTorsoTwistRadians, command.MoveWorld)
                : FormSwordsman.GuardCommand(kind, sense, command.FaceDirection, Body.WalkSpeed, Body.MaxTorsoTwistRadians);
            if (_form is null)
            {
                // 構え: 手の目標と胴のひねりだけ構えにして、歩き・回避の頭の動きは意図操作のまま。
                return command with { Left = held.Left, Right = held.Right, TwistBias = held.TwistBias };
            }

            _formTime += dt * SwordStance.Rate;
            return command with
            {
                Left = held.Left,
                Right = held.Right,
                HeadOffset = held.HeadOffset,
                TwistBias = held.TwistBias,
                MoveWorld = held.MoveWorld.LengthSquared() > 1f ? Vector3.Normalize(held.MoveWorld) : held.MoveWorld,
            };
        }

        _form = null;
        if (weapon.HasValue && sense.SelfWeapon.Held)
        {
            var club = weapon == WeaponKind.WoodenClub;
            if (_swing is { } previous && _time >= previous.Duration)
            {
                _swing = null;
            }
            if (_swing is null && !input.Evade && (light || heavy))
            {
                _swing = club ? (heavy ? ClubHeavy : ClubLight) : (heavy ? SwordHeavy : SwordLight);
                _time = 0f;
            }

            if (_swing is { } swing)
            {
                var (hand, twist, step) = swing.At(_time, sense.ArmReach);
                _time += dt;
                var move = command.MoveWorld + command.FaceDirection * step;
                return command with
                {
                    Right = hand,
                    TwistBias = twist,
                    MoveWorld = move.LengthSquared() > 1f ? Vector3.Normalize(move) : move,
                };
            }
            return command with { Right = WeaponSwing.GuardCommand(sense.ArmReach, club ? SwingFamily.Chop : SwingFamily.Sword) };
        }

        _swing = null;
        if (!karate)
        {
            return command;
        }
        if (_kick is { } previousKick && _time >= previousKick.Duration)
        {
            _kick = null;
        }
        if (heavy && !input.Evade && _kick is null)
        {
            _kick = KickSpec.Of(KickTechnique.Front);
            _kickAim = sense.Self.WorldToLocal(_kick.Value.AimWorld(sense));
            _time = 0f;
        }
        if (_kick is not { } kick)
        {
            return command;
        }

        var rest = new Vector3(0.2f, -1.4f, 0f) * sense.ArmReach;
        var leg = kick.Evaluate(Hand.Right, _time, kick.Duration, rest, _kickAim, sense.ArmReach);
        _time += dt;
        return command with { RightLeg = leg };
    }
}
