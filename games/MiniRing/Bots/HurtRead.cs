namespace MiniRing.Bots;

/// <summary>
/// 相手の弱りを読む層（立ち回りの層の入力。docs/BOT_PDCA.md「弱った相手を詰める」）: 相手がどれだけ打たれて開いているか（<see cref="Opening"/>、0..1）を、
/// 見えている手がかりと自分が当てた手応えだけから連続に出す（CLAUDE.md §1.7: 見えない意識の値は読まない。§1.3: 状態の名前で分けない）。
/// <list type="bullet">
/// <item>構えの下がり: 相手の頭と両拳の高さの差（相手の腕の長さ倍）が、その相手の普段の構え（下へは速く、上へはゆっくり追う基準）より
/// どれだけ下がったか。測った物差し（2026-11-16、HurtCueProbe、ボット同士 12 試合 × 60 秒）: 意識がはっきりした体は 0.26 で、疲れても変わらない
/// （スタミナ 0.2 未満でも 0.26）。意識が薄れ始めると 0.43、深く薄れると 0.49 へ下がる（腕のサーボが意識で弱るため。体の物理から出る見た目）。
/// ふらつき（頭の横の揺れの速さ）は疲れでも同じくらい増える（0.53→0.87）ので使わない。</item>
/// <item>手応え: 自分の拳が当たった重さ（<see cref="IFighterControl.OnPunchLanded"/> のダメージ）を時定数で忘れながら足したもの。打った本人が感じる量。</item>
/// </list>
/// 2 つを「どちらかが開いていれば開いている」（1 − (1 − a)(1 − b)）で合わせ、0.42 秒でならす。
/// </summary>
internal sealed class HurtRead
{
    /// <summary>普段の構えの基準が、下がった構えへ寄る時定数 [s]（上がる側＝より構えた側へは <see cref="UsualFallSeconds"/>）。打たれて下がった構えを普段と取り違えないよう遅く。</summary>
    private const float UsualRiseSeconds = 20.14f;

    /// <summary>普段の構えの基準が、より高い構えへ寄る時定数 [s]。</summary>
    private const float UsualFallSeconds = 1.678f;

    /// <summary>見積もりのならしの時定数 [s]。</summary>
    private const float SmoothSeconds = 0.4196f;

    private float _usual = float.NaN;
    private float _felt;

    /// <summary>相手の開き（0 = 普段どおり、1 = 構えが落ち、重いのが続けて入っている）。</summary>
    public float Opening { get; private set; }

    /// <summary>構えの下がりだけの値（調べもの用）。</summary>
    public float Sag { get; private set; }

    public void Observe(in FighterSense sense, BotStyle style, float dt)
    {
        _felt *= MathF.Exp(-dt / MathF.Max(1e-3f, style.HurtMemory));
        if (!sense.OpponentAlive || sense.OpponentLeft.Position == default || sense.OpponentRight.Position == default)
        {
            Opening *= MathF.Exp(-dt / SmoothSeconds);
            return;
        }

        var reach = sense.OpponentArmReach > 0f ? sense.OpponentArmReach : sense.ArmReach;
        var drop = (sense.OpponentHead.Y - (0.5f * (sense.OpponentLeft.Position.Y + sense.OpponentRight.Position.Y))) / MathF.Max(1e-3f, reach);
        if (!float.IsFinite(drop))
        {
            return;
        }

        if (float.IsNaN(_usual))
        {
            _usual = drop;
        }
        else
        {
            var tau = drop < _usual ? UsualFallSeconds : UsualRiseSeconds;
            _usual += (drop - _usual) * (1f - MathF.Exp(-dt / tau));
        }

        Sag = Math.Clamp((drop - _usual - style.HurtSagStart) / MathF.Max(1e-3f, style.HurtSagSpan), 0f, 1f);
        var felt = 1f - MathF.Exp(-_felt / MathF.Max(1e-3f, style.HurtFeltScale));
        var target = 1f - ((1f - Sag) * (1f - felt));
        Opening += (target - Opening) * (1f - MathF.Exp(-dt / SmoothSeconds));
    }

    /// <summary>自分の拳が当たった（打った本人の手応え）。</summary>
    public void Landed(float damage)
    {
        if (float.IsFinite(damage) && damage > 0f)
        {
            _felt += damage;
        }
    }
}
