using System.Numerics;

namespace MiniRing.Weapons;

/// <summary>
/// 構えから助走を始め、切断面の中を一定方向へ振り抜く目標経路。<see cref="Draw"/> なら引き斬り（2026-11-25）: 刃を寝かせて狙いの点の向こう側へ掛け、
/// そこから手前へ引いて、刃の手前の縁を狙いの点へ通す（鎌で刈る形。刃の押し当てと引く速さで斬る）。
/// </summary>
public readonly record struct ClawStroke(Hand Hand, float Reach, Vector3 Aim, bool Sweep, float Windup, float DriveTime, float FollowThrough, bool Draw = false,
    ClawCylinder? Cylinder = null)
{
    /// <summary>円柱を断つ経路で、斬る点が中心の手前から動き始める距離 [腕の長さ]（溜め <see cref="Windup"/> = 1 のとき）。</summary>
    private const float CutBefore = 0.45f;

    /// <summary>引き斬りで、刃を掛ける所の、狙いの点より向こうの距離 [腕の長さ]（溜め <see cref="Windup"/> = 1 のとき）。</summary>
    private const float DrawBehind = 0.16f;

    /// <summary>引き斬りで、狙いの点より手前へ引き抜く距離 [腕の長さ]。</summary>
    private const float DrawPull = 0.42f;

    /// <summary>引き斬りで、刃を掛けに行く間に狙いの点より上へ上げる高さ [腕の長さ]（頭の上を越えて向こうへ回す）。</summary>
    private const float DrawLift = 0.30f;

    /// <summary>引き斬りの駆動のうち、刃を掛けて下ろす区間の割合（残りが引く区間）。</summary>
    private const float DrawHook = 0.30f;

    private const float LoadTime = 0.27f;

    /// <summary>切断面の高さの上限 [腕の長さ、肩から上]: 腕が届く高さ。</summary>
    private const float MaxHeight = 0.85f;
    public float CutStart => LoadTime + DriveTime * 0.30f;
    public float CutEnd => LoadTime + DriveTime * 0.82f;
    public float Duration => LoadTime + DriveTime + FollowThrough;
    private static Vector3 Forward => Vector3.UnitZ;
    private Vector3 Across
    {
        get
        {
            var slope = Math.Clamp(0.12f - (0.25f * Aim.Y / MathF.Max(Reach, 0.01f)), 0.03f, 0.28f);
            return Vector3.Normalize(new Vector3(-Hand.LocalSide(), -slope, 0f));
        }
    }
    public Vector3 Normal => Cylinder is { } c ? c.Axis : Sweep && !Draw ? Vector3.Cross(Forward, Across) : Vector3.UnitY;

    public static ClawStroke Create(Hand hand, float reach, Vector3 aim, bool sweep, float drive = 0.31f, float windup = 1f, bool draw = false, ClawCylinder? cylinder = null)
        => new(hand, reach, aim, sweep, windup, drive, 0.32f, draw, cylinder);

    public static ClawSegment Guard(Hand hand, float reach)
    {
        var root = new Vector3(0.57f * hand.LocalSide(), 0.10f, 0.34f) * reach;
        var direction = Vector3.Normalize(new Vector3(0.24f * hand.LocalSide(), -0.84f, 0.62f));
        return new ClawSegment(root, root + direction * ClawSegment.Length, new Vector3(-0.45f * hand.LocalSide(), 0.20f, 0.76f));
    }

    public ClawSegment Sample(float time, ClawSegment? recoveryGuard = null)
    {
        var guard = Guard(Hand, Reach);
        if (time < LoadTime)
        {
            return Blend(guard, Drive(0f), Smooth(time / LoadTime));
        }

        if (time < LoadTime + DriveTime)
        {
            return Drive(Smooth((time - LoadTime) / DriveTime));
        }

        return Blend(Drive(1f), recoveryGuard ?? guard, Smooth((time - LoadTime - DriveTime) / FollowThrough));
    }

    private ClawSegment Drive(float phase)
    {
        if (Cylinder is { } cylinder)
        {
            return CylinderCut(cylinder, phase);
        }

        if (Draw)
        {
            return DrawCut(phase);
        }

        Vector3 root;
        Vector3 axis;
        var z = Math.Clamp(Aim.Z - ClawSegment.Length * 0.56f, -Reach * 0.12f, Reach * 0.86f);
        // 切断面の高さは、腕が届く高さ（肩から腕の長さの 0.85 倍上）まで狙いに合わせる。2026-11-25 までは 0.48 倍で頭打ちにしていて、
        // 背の高い相手の首（ガルヴァの肩から 0.45〜0.77 倍上）を狙っても刃が肩・胸を通った（huntlab の neckAim）。
        var center = new Vector3(Aim.X * 0.80f, Math.Clamp(Aim.Y, -Reach * 0.66f, Reach * MaxHeight), z);
        if (Sweep)
        {
            var across = Across;
            var angle = (-0.98f * Windup) + (phase * (0.98f * Windup + 1.17f));
            root = center + (across * ((phase - 0.54f) * Reach * (0.90f + 0.30f * Windup))) + (Forward * ((MathF.Cos(angle) - 1f) * Reach * 0.30f));
            axis = Forward * MathF.Cos(angle) + across * MathF.Sin(angle);
        }
        else
        {
            var atContact = Math.Clamp(Aim.Z - ClawSegment.Length, -Reach * 0.42f, Reach * 0.86f);
            root = center with { Z = atContact + ((phase - 0.40f) * Reach * 0.92f * Windup) };
            axis = Forward;
        }

        return new ClawSegment(root, root + axis * ClawSegment.Length, Normal);
    }

    /// <summary>
    /// 引き斬りの刃: 刃は水平に寝かせ、刃元を手の側、切っ先を体の中心の側（少し向こうへ曲がる）へ向ける。刃の真ん中が狙いの点の上を通る横の位置に置き、
    /// 掛ける区間（<see cref="DrawHook"/>）で狙いの点より上・向こうから狙いの高さへ下ろし、残りで向こうから手前へ引き抜く。
    /// </summary>
    private ClawSegment DrawCut(float phase)
    {
        var side = Hand.LocalSide();
        var axis = Vector3.Normalize(new Vector3(-side, 0f, 0.35f));
        var behind = Aim.Z + (DrawBehind * Reach * Windup);
        var level = Math.Clamp(Aim.Y, -Reach * 0.66f, Reach * MaxHeight);
        Vector3 middle;
        if (phase < DrawHook)
        {
            var down = Smooth(phase / DrawHook);
            middle = new Vector3(Aim.X, level + ((1f - down) * DrawLift * Reach), behind);
        }
        else
        {
            var pull = Smooth((phase - DrawHook) / (1f - DrawHook));
            middle = new Vector3(Aim.X, level, behind - (pull * ((DrawBehind * Reach * Windup) + (DrawPull * Reach))));
        }

        var root = middle - (axis * (ClawSegment.Length * 0.5f));
        return new ClawSegment(root, root + axis * ClawSegment.Length, Vector3.UnitY);
    }

    /// <summary>
    /// 円柱を軸に垂直に断つ経路（2026-11-25、ユーザーの指定「目標を竹のような円柱と考え、なるべく垂直に切る軌道面を定義し、その面を刃の向きで高速で振り抜く」）:
    /// 切断面 = 狙いの点（円柱の中心）を通り、軸（<see cref="ClawCylinder.Axis"/>）に垂直な面。刃はその面に寝かせ（面の法線 = 軸）、刃の弦は面の中で前（自分から遠い向き）へ向ける。
    /// 刃の上の斬る点（刃元から弦の <see cref="ClawCylinder.Point"/> の割合）が、中心の手前（手の側へ <see cref="CutBefore"/> × 溜め）から、中心を通って
    /// 向こう側（<see cref="ClawCylinder.After"/>）まで、面の中をまっすぐ進む（開始点 → 中心 → 終着点）。刃は進む向きに垂直なので、刃筋が通る。
    /// 狙う部位では分けない（首・腕・胴のどれでも、狙いの点と軸から同じ式）。
    /// </summary>
    private ClawSegment CylinderCut(ClawCylinder cylinder, float phase)
    {
        var axis = cylinder.Axis.LengthSquared() > 1e-8f ? Vector3.Normalize(cylinder.Axis) : Vector3.UnitY;
        var forward = Forward - (axis * Vector3.Dot(Forward, axis));
        forward = forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
        var across = Vector3.Normalize(Vector3.Cross(axis, forward));
        if (Vector3.Dot(across, new Vector3(-Hand.LocalSide(), 0f, 0f)) < 0f)
        {
            across = -across; // 手の側から体の中心の側へ振り抜く
        }

        var before = CutBefore * Reach * Windup;
        var after = cylinder.After * Reach;
        var cutPoint = Aim + (across * (-before + (phase * (before + after))));
        var root = cutPoint - (forward * (ClawSegment.Length * cylinder.Point));
        return new ClawSegment(root, root + (forward * ClawSegment.Length), axis);
    }

    private static ClawSegment Blend(ClawSegment from, ClawSegment to, float blend)
    {
        var root = Vector3.Lerp(from.Root, to.Root, blend);
        var axis = Vector3.Normalize(Vector3.Lerp(from.Tip - from.Root, to.Tip - to.Root, blend));
        return new ClawSegment(root, root + axis * ClawSegment.Length, Vector3.Lerp(from.Normal, to.Normal, blend));
    }

    private static float Smooth(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// 円柱を断つ経路の的（体フレーム）: 円柱の軸 <paramref name="Axis"/>（向きだけ）、刃の上の斬る点 <paramref name="Point"/>（刃元から弦の割合 0..1）、
/// 中心から向こうへ振り抜く距離 <paramref name="After"/> [腕の長さ]。<see cref="ClawStroke.Cylinder"/>。
/// </summary>
public readonly record struct ClawCylinder(Vector3 Axis, float Point, float After);
