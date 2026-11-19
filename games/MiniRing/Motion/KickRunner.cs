using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Motion;

/// <summary>
/// 蹴りを出す入力元（VR のボタン・今後の意図操作）が使う、蹴りの経路の実行（ボットの蹴り <c>BoxerBot</c> と同じ <see cref="KickSpec"/> の経路・同じ組み立て）。
/// 脚は追跡しないので、蹴りは「脚の目標の経路」を体がバネで追った結果になる（§1.1・§1.2: 姿勢は書かない）。手の指令には触れない（手はコントローラを追い続ける）。
/// <para>
/// <b>蹴りの選び方</b>（<see cref="Choose"/>）: 前蹴り（<see cref="KickTechnique.Front"/>）と膝蹴り（<see cref="KickTechnique.Knee"/>）のうち、今の間合い（腕の長さの倍数）が
/// その蹴りの届く間合い（<see cref="KickSpec.StandoffFor"/>）にいちばん近い方。遠ければ前蹴り、近ければ膝蹴り（届く間合いの比べだけ。閾値は持たない）。
/// </para>
/// </summary>
public sealed class KickRunner
{
    private KickSpec? _spec;
    private Hand _leg;
    private float _time;
    private Vector3 _aimWorld;
    private Vector3 _aimLocal;

    /// <summary>蹴っている最中か（蹴りの長さが過ぎるまで次の蹴りは始めない）。</summary>
    public bool Active => _spec is not null;

    /// <summary>今の蹴りの種類（蹴っていなければ null）。</summary>
    public KickTechnique? Technique => _spec?.Technique;

    /// <summary>今蹴っている脚（蹴っていなければ null）。</summary>
    public Hand? Leg => _spec is null ? null : _leg;

    /// <summary>ボットの蹴りと同じ、蹴る脚の差分の基準（構えの前の手 = 蹴る脚の反対側）。</summary>
    private static Hand LeadFor(Hand leg) => leg == Hand.Left ? Hand.Right : Hand.Left;

    /// <summary>
    /// 相手との間合い（胸と胸、腕の長さの倍数）に合う蹴り: 前蹴りと膝蹴りのうち、届く間合い（<see cref="KickSpec.StandoffFor"/>）との差が小さい方。
    /// 差が同じなら前蹴り。
    /// </summary>
    public static KickTechnique Choose(Hand leg, in FighterSense sense)
    {
        var distance = (sense.OpponentChest - sense.Self.Origin).Length() / MathF.Max(1e-3f, sense.ArmReach);
        float Miss(KickTechnique technique) => MathF.Abs(distance - KickSpec.Of(technique).StandoffFor(leg, LeadFor(leg)));
        return Miss(KickTechnique.Knee) < Miss(KickTechnique.Front) ? KickTechnique.Knee : KickTechnique.Front;
    }

    /// <summary>
    /// <paramref name="leg"/> で蹴り始める（蹴っている最中なら何もせず false）。蹴りの種類は <see cref="Choose"/>。狙い（相手の体の点）はこの時点のものを使う。
    /// </summary>
    public bool Start(Hand leg, in FighterSense sense)
    {
        if (_spec is not null)
        {
            return false;
        }

        var spec = KickSpec.Of(Choose(leg, sense));
        _spec = spec;
        _leg = leg;
        _time = 0f;
        _aimWorld = spec.AimWorld(sense);
        _aimLocal = sense.Self.WorldToLocal(_aimWorld);
        return true;
    }

    /// <summary>蹴りを止める（ワールドを作り直したとき）。</summary>
    public void Cancel() => _spec = null;

    /// <summary>
    /// 1 歩進めた蹴りの指令を、<paramref name="command"/> へ重ねて返す（脚の目標・回す蹴りなら体の向きと胴のひねり。手・移動は変えない）。蹴っていなければそのまま。
    /// </summary>
    public FighterCommand Apply(in FighterCommand command, in FighterSense sense, float dt)
    {
        if (_spec is not { } spec)
        {
            return command;
        }

        var lead = LeadFor(_leg);
        var length = spec.SecondsFor(_leg, lead) ?? spec.Duration;
        var reach = sense.ArmReach;
        var twist = 0f;
        var face = command.FaceDirection;
        if (spec.Turns)
        {
            twist = spec.TorsoTwist(_leg, _time, length);
            if (spec.Rotates)
            {
                face = Vector3.Transform(face, Quaternion.CreateFromAxisAngle(Vector3.UnitY, spec.FaceYaw(_leg, _time, length)));
                _aimLocal = sense.Self.WorldToLocal(_aimWorld); // 体が回るにつれて、狙いの体フレームの位置は動く
            }
        }

        var rest = new Vector3(_leg.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var leg = spec.Evaluate(_leg, _time, length, rest, _aimLocal, reach, sense.SelfLeg(_leg), lead);
        _time += dt;
        if (_time >= length)
        {
            _spec = null;
        }

        var result = command with { FaceDirection = face, TwistBias = command.TwistBias + twist };
        return _leg == Hand.Left ? result with { LeftLeg = leg } : result with { RightLeg = leg };
    }
}

/// <summary>
/// フェイスボタンの押し下げ（<see cref="VrFaceButtons"/>）から稽古の操作の合図を取り出す（押している状態の変わり目から）:
/// <list type="bullet">
/// <item>A 単独（身長の合わせ）: <b>A を離したとき</b>に、A を押している間に 4 つ同時（作り直し）が無かった場合だけ。作り直しで合わせ直さない。</item>
/// <item>4 つ同時（作り直し）: 4 つ全部が押された瞬間。</item>
/// <item>X・Y（蹴り）: 押した瞬間。ただし A か B を押している間は出さない（4 つ同時を押す途中で蹴らない）。</item>
/// </list>
/// </summary>
public sealed class FaceButtonTracker
{
    private VrFaceButtons _last;
    private bool _chordWhileA;

    /// <summary>この歩の合図。</summary>
    public readonly record struct Events(bool Calibrate, bool Reset, bool KickLeft, bool KickRight);

    public Events Step(in VrFaceButtons now)
    {
        var reset = now.All && !_last.All;
        if (now.A && !_last.A)
        {
            _chordWhileA = false;
        }

        if (now.All)
        {
            _chordWhileA = true;
        }

        var calibrate = _last.A && !now.A && !_chordWhileA;
        var modifier = now.A || now.B;
        var kickLeft = now.X && !_last.X && !modifier;
        var kickRight = now.Y && !_last.Y && !modifier;
        _last = now;
        return new Events(calibrate, reset, kickLeft, kickRight);
    }
}
