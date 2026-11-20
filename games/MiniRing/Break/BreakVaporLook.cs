using System.Numerics;
using Aigis.Render;

namespace MiniRing.Break;

/// <summary>
/// 口から出る煙（たばこ）・飛沫（口に含んで吐いた水）の描画（見た目だけ。試合の計算・記録に響かない）。
/// 吐く息の雲（<see cref="TiredLook"/>）と同じく、頭の向きと口の位置から、経過時間で決まる位置へ玉を置く。乱数は使わない（玉ごとの決まったずれの表）。
/// </summary>
public static class BreakVaporLook
{
    private static readonly Vector4 SmokeColor = new(0.72f, 0.72f, 0.74f, 1f);
    private static readonly Vector4 SpitColor = new(0.75f, 0.9f, 1f, 1f);

    /// <summary>煙の 1 つの玉のずれ（横・上・前の向きの倍率）と大きさの倍率。</summary>
    private static readonly (Vector3 Offset, float Scale)[] Wisps =
    [
        (new Vector3(0f, 0f, 0f), 1f),
        (new Vector3(0.5f, 0.3f, 0.4f), 0.8f),
        (new Vector3(-0.45f, 0.5f, 0.9f), 0.75f),
        (new Vector3(0.2f, 0.9f, 1.4f), 0.9f),
        (new Vector3(-0.3f, 1.3f, 1.9f), 0.7f),
        (new Vector3(0.45f, 1.7f, 2.3f), 0.8f),
    ];

    /// <summary>飛沫の 1 粒ごとの、前へ飛ぶ速さの倍率・横へ散る向き。</summary>
    private static readonly (float Speed, float Spread)[] Drops =
    [
        (1.0f, 0f), (0.8f, 0.5f), (0.9f, -0.6f), (0.65f, 0.25f), (0.7f, -0.3f), (1.1f, 0.1f), (0.55f, -0.8f), (0.75f, 0.8f),
    ];

    /// <summary>煙を出している選手の口から、煙・飛沫の玉を詰める。</summary>
    public static void WriteInstances(BoxingGame game, ICollection<PrimitiveInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(instances);
        foreach (var (fighter, vapor) in game.BreakVapors)
        {
            var head = game.World.GetState(fighter.Parts.Head);
            var half = fighter.HalfExtentsOf(fighter.Parts.Head);
            var mouth = head.Position + Vector3.Transform(new Vector3(0f, -0.45f * half.Y, 0.95f * half.Z), head.Orientation);
            var right = Vector3.Transform(Vector3.UnitX, head.Orientation);
            var forward = Vector3.Normalize(Vector3.Transform(new Vector3(0f, -0.25f, 1f), head.Orientation));
            if (vapor.Kind == VaporKind.Smoke)
            {
                WriteSmoke(instances, mouth, right, forward, vapor.Age);
            }
            else
            {
                WriteSpit(instances, mouth, right, forward, vapor.Age);
            }
        }
    }

    private static void WriteSmoke(ICollection<PrimitiveInstance> instances, Vector3 mouth, Vector3 right, Vector3 forward, float age)
    {
        // 煙: ゆっくり前へ出て、上へ昇りながら広がって薄くなる（吐く息 TiredLook より灰色で長く漂う）。
        const float life = 4.364f;
        var fade = 1f - Math.Clamp(age / life, 0f, 1f);
        var weight = 0.55f * fade * fade * MathF.Min(1f, age / 0.2518f);
        var center = mouth + (forward * (0.05874f * age * (1f - (0.149f * age)))) + (Vector3.UnitY * (0.05035f * age));
        var radius = 0.0169f + (0.01846f * age);
        foreach (var (offset, scale) in Wisps)
        {
            var at = center + (((right * offset.X) + (Vector3.UnitY * offset.Y) + (forward * offset.Z)) * radius * (0.6f + (0.2979f * age)));
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(at),
                new Vector3(radius * scale, 0f, 0f), SmokeColor with { W = weight }, MaterialKind.Standard));
        }
    }

    private static void WriteSpit(ICollection<PrimitiveInstance> instances, Vector3 mouth, Vector3 right, Vector3 forward, float age)
    {
        // 飛沫: 前へ飛びながら落ちる（重力）水色の小さな玉。0.9 秒ほどで消える。
        const float life = 0.9231f;
        if (age > life)
        {
            return;
        }

        var fade = 1f - (age / life);
        foreach (var (speed, spread) in Drops)
        {
            var at = mouth + (forward * (0.235f * speed * age)) + (right * (0.06714f * spread * age)) - (Vector3.UnitY * (0.5f * 9.8f * age * age * 0.12f));
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(at),
                new Vector3(0.00986f, 0f, 0f), SpitColor with { W = 0.85f * fade }, MaterialKind.Standard));
        }
    }
}
