using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 合成の VR の動き（VR の実機なしで、記録 → なぞりの道を通して確かめる手本）。人の体の寸法のコントローラ・HMD の軌跡を作り、
/// 実機と同じ <see cref="VrPlayerMapper"/> と <see cref="VrMotionRecorder"/> に通して <see cref="VrMotionClip"/> にする。
/// </summary>
public static class VrMotionSynth
{
    private const float Step = 1f / 90f; // 実機の表示の刻み（90 Hz）に合わせる

    /// <summary>
    /// 棒の突き: 身長 1.7 m の人が棒を両手で持ち（右手が前・左手が後ろの 0.25 m）、少し引いてから前へ 0.14 m 突き（写像で 2 倍になる）、構えへ戻る。
    /// 突きの間（0.30〜0.62 s）は左手のトリガーを離し（棒を滑らせる）、右手は握ったまま。
    /// </summary>
    public static VrMotionClip StaffThrust(float puppetArmReach = 0.530f)
    {
        var mapper = new VrPlayerMapper(weaponMode: true);
        var recorder = new VrMotionRecorder("Staff", "synthetic staff thrust");
        var sense = new FighterSense(new BodyFrame(new Vector3(0f, 0f, -1f), 0f), puppetArmReach, default, new Vector3(0f, 1f, 1f), true);
        var head = new Vector3(0f, 1.58f, 0f);
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI); // コントローラ・HMD の前（ローカル −Z）が +Z（リングの方）を向く
        var time = 0f;
        for (var k = 0; k < 190; k++)
        {
            // 0..0.3 s: 引く（−0.06 m）、0.3..0.62 s: 突く（+0.14 m）、0.62..0.95 s: 戻す。
            var u = time;
            float reach;
            if (u < 0.3f)
            {
                reach = -0.06f * Smooth(u / 0.3f);
            }
            else if (u < 0.62f)
            {
                reach = Lerp(-0.06f, 0.14f, Smooth((u - 0.3f) / 0.32f));
            }
            else
            {
                reach = Lerp(0.14f, 0f, Smooth(MathF.Min(1f, (u - 0.62f) / 0.33f)));
            }

            var right = new Vector3(-0.25f, 1.30f, 0.25f + reach);
            var left = new Vector3(-0.12f, 1.28f, 0.00f + reach);
            var sliding = u is > 0.30f and < 0.62f;
            var raw = new VrRawFrame(head, facing, left, right, true, true, Vector2.Zero, Vector2.Zero, 0f, 0f, false, true,
                facing, facing, LeftTrigger: sliding ? 0f : 1f, RightTrigger: 1f);
            var command = mapper.Read(raw, sense, Step).Player;
            recorder.Add(time, raw, command, mapper, puppetArmReach);
            time += Step;
        }

        return recorder.Build() ?? throw new InvalidOperationException("The synthetic motion has no frames.");
    }

    private static float Smooth(float x) => x * x * (3f - (2f * x));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
