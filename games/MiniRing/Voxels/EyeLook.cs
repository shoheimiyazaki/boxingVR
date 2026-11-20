using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 描くときの目の指示（見た目だけ）: まぶたの閉じ具合と、注目している点。試合の計算・視覚・チェックサムには使わない。
/// </summary>
/// <param name="Closure">まぶたの閉じ具合（0 = 開いている、1 = 閉じている）。</param>
/// <param name="Target">注目している点（ワールド）。null なら正面を向く。</param>
public readonly record struct EyePose(float Closure, Vector3? Target);

/// <summary>
/// 頭の格子のどのセルを黒目の色で塗るか・どの段までまぶたの色にするか（<see cref="EyeGaze.Scan"/> の結果）。
/// </summary>
/// <param name="Pupil0">+X 側の目の黒目のセルの番号（その目の白目が無ければ -1）。</param>
/// <param name="Pupil1">−X 側の目の黒目のセルの番号。</param>
/// <param name="ClosedAbove0">+X 側の目で、この段（Y）より上のセルがまぶたに覆われている。覆われていなければ int.MaxValue。</param>
/// <param name="ClosedAbove1">−X 側の目の同じ値。</param>
public readonly record struct EyeScan(int Pupil0, int Pupil1, int ClosedAbove0, int ClosedAbove1)
{
    /// <summary>目が 1 つも無い（描き替える物が無い）ときの値。</summary>
    public static EyeScan None { get; } = new(-1, -1, int.MaxValue, int.MaxValue);
}

/// <summary>
/// 黒目の向き: 描くときに、見ている点にいちばん近い白目のセルを黒目の色で塗る（材質の <see cref="BoxerAnatomy.Pupil"/> は動かさない。
/// 打撃・視覚の計算は材質のセルを使い続けるので、チェックサムは変わらない）。
/// <para>眼球は、目の表のセルの重心から奥へ <see cref="BallRadiusCells"/> 個ぶん下がった所を中心とする球と見なし、黒目は
/// 「球の中心から見て見ている向きにいちばん近い向きにある、表に出ている白目のセル」。削れて残った白目の中だけで動く。</para>
/// <para>まぶた: 目のセル（白目・黒目）を上の段から順に、顔の皮膚の色へ塗り替える（<see cref="EyeBlink"/> の閉じ具合）。</para>
/// </summary>
public static class EyeGaze
{
    /// <summary>眼球の半径 [セル]。目は 3 セルほどの平たい面なので、見る向きに対して黒目が端のセルまで動くのに十分な小ささ。</summary>
    public const float BallRadiusCells = 2.5f;

    /// <summary>目が向けられる向きの限界: 正面(+Z)への成分が、横・縦の成分の長さのこの割合より小さくならない（真後ろは見られない）。</summary>
    public const float MinForward = 0.35f;

    [ThreadStatic]
    private static List<(int X, int Y, int Z)>? _cells0;

    [ThreadStatic]
    private static List<(int X, int Y, int Z)>? _cells1;

    /// <summary>目の描き方を決める。<paramref name="worldToGrid"/> は頭の格子のワールド→格子の局所（メートル）の変換。</summary>
    public static EyeScan Scan(VoxelGrid grid, in Matrix4x4 worldToGrid, Vector3? targetWorld, float closure)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (dx, dy, dz) = grid.Dimensions;
        var side0 = _cells0 ??= [];
        var side1 = _cells1 ??= [];
        side0.Clear();
        side1.Clear();
        var any0 = new List<(int X, int Y, int Z)>();
        var any1 = new List<(int X, int Y, int Z)>();
        for (var z = 0; z < dz; z++)
        {
            for (var y = 0; y < dy; y++)
            {
                for (var x = 0; x < dx; x++)
                {
                    var m = grid.At(x, y, z);
                    if (m is not (BoxerAnatomy.Eye or BoxerAnatomy.Pupil))
                    {
                        continue;
                    }

                    var first = x >= dx / 2;
                    (first ? any0 : any1).Add((x, y, z));
                    // 表に出ている（前の面が空いている）セルだけが見える白目。
                    if (z + 1 >= dz || grid.At(x, y, z + 1) == 0)
                    {
                        (first ? side0 : side1).Add((x, y, z));
                    }
                }
            }
        }

        var (p0, c0) = ScanSide(grid, side0.Count > 0 ? side0 : any0, worldToGrid, targetWorld, closure);
        var (p1, c1) = ScanSide(grid, side1.Count > 0 ? side1 : any1, worldToGrid, targetWorld, closure);
        return new EyeScan(p0, p1, c0, c1);
    }

    private static (int Pupil, int ClosedAbove) ScanSide(VoxelGrid grid, List<(int X, int Y, int Z)> cells, in Matrix4x4 worldToGrid, Vector3? targetWorld, float closure)
    {
        if (cells.Count == 0)
        {
            return (-1, int.MaxValue);
        }

        float sx = 0f, sy = 0f, sz = 0f;
        var yMin = int.MaxValue;
        var yMax = int.MinValue;
        var zFront = int.MinValue;
        foreach (var (x, y, z) in cells)
        {
            sx += x;
            sy += y;
            sz += z;
            yMin = Math.Min(yMin, y);
            yMax = Math.Max(yMax, y);
            zFront = Math.Max(zFront, z);
        }

        var n = cells.Count;
        var cx = sx / n;
        var cy = sy / n;

        // 見ている向き（頭の格子の局所。軸はセルの軸と同じ）。
        var dir = Vector3.UnitZ;
        if (targetWorld is { } target)
        {
            var eyeLocal = grid.CellCenter((int)MathF.Round(cx), (int)MathF.Round(cy), zFront);
            var toTarget = Vector3.Transform(target, worldToGrid) - eyeLocal;
            if (toTarget.LengthSquared() > 1e-8f)
            {
                var d = Vector3.Normalize(toTarget);
                var lateral = MathF.Sqrt((d.X * d.X) + (d.Y * d.Y));
                dir = new Vector3(d.X, d.Y, MathF.Max(d.Z, MinForward * lateral));
                dir = Vector3.Normalize(dir);
            }
        }

        // 眼球の中心（セル座標）: 表のセルの重心から奥へ半径ぶん。
        var centerZ = zFront - BallRadiusCells;
        var best = float.MinValue;
        var pick = -1;
        foreach (var (x, y, z) in cells)
        {
            var v = new Vector3(x - cx, y - cy, z - centerZ);
            var score = Vector3.Dot(Vector3.Normalize(v), dir);
            // 同点は番号の小さい方（走査の順で決まっている）。
            if (score > best + 1e-6f)
            {
                best = score;
                pick = grid.Index(x, y, z);
            }
        }

        var height = yMax - yMin + 1;
        var rows = Math.Clamp((int)MathF.Round(Math.Clamp(closure, 0f, 1f) * height), 0, height);
        return (pick, yMax - rows);
    }

    /// <summary>
    /// <paramref name="self"/> が見ている点（ワールド）。相手の頭を見る。相手の拳がこちらへ速く来ているときは、その拳の方へ視線が寄る
    /// （寄る割合は拳が近づく速さに応じて連続。8.4 m/s で 0.8）。意識が薄れる（<see cref="Fighter.Awareness"/> が <see cref="FocusAwareness"/> 未満）か死んでいれば
    /// 焦点が合わず null（正面を向く）。見た目だけ。
    /// </summary>
    public static Vector3? TargetOf(Fighter self, Fighter opponent)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(opponent);
        if (self.IsDead || self.Awareness < FocusAwareness)
        {
            return null;
        }

        var head = opponent.HeadPosition;
        var mine = self.HeadPosition;
        var target = head;
        var bestWeight = 0f;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var pos = opponent.GloveCenter(hand);
            var toMe = mine - pos;
            if (toMe.LengthSquared() < 1e-6f)
            {
                continue;
            }

            var approach = Vector3.Dot(opponent.GloveVelocity(hand), Vector3.Normalize(toMe));
            var weight = Math.Clamp(approach / GloveApproachSpeed, 0f, 1f) * GloveGazeShare;
            if (weight > bestWeight)
            {
                bestWeight = weight;
                target = Vector3.Lerp(head, pos, weight);
            }
        }

        return target;
    }

    /// <summary>焦点が合う意識の下限（<see cref="Fighter.Awareness"/>）。これを下回ると視線は定まらず正面を向く。見た目の値。</summary>
    public const float FocusAwareness = 0.35f;

    /// <summary>視線が拳へ寄り切る拳の近づく速さ [m/s]。見た目の値。</summary>
    public const float GloveApproachSpeed = 8.392f;

    /// <summary>拳が最も速く近づくときに、頭から拳へ寄る視線の割合の上限。</summary>
    public const float GloveGazeShare = 0.8f;
}

/// <summary>
/// まばたきとまぶたの状態（見た目だけ。試合の計算・チェックサムには使わない。決定的: 間隔の揺らぎはファイターごとの決まった数列）。
/// <list type="bullet">
/// <item>ふつうのまばたき: 間隔 <see cref="CalmInterval"/>（7.0 秒）から、疲れ・痛みが増すほど <see cref="StressedInterval"/>（2.35 秒）まで連続に詰まる。
/// 1 回 0.27 秒（疲れるほど 0.37 秒まで遅い）。閉じる 35 %・開く 65 %。</item>
/// <item>意識が薄れるほどまぶたが落ちる（<see cref="EyeGaze.FocusAwareness"/> 未満から半開き、意識が無ければほぼ閉じる）。死ねば閉じる。</item>
/// <item>打たれた瞬間は反射で目をつぶる（<see cref="Flinch"/>: 強いほど長く閉じ、痛みの記憶が残って目を細め、まばたきが増える）。</item>
/// </list>
/// </summary>
public sealed class EyeBlink
{
    /// <summary>落ち着いているときのまばたきの間隔 [s]（人形の単位の 4.2 秒を相似で換算。人は 2〜6 秒に 1 回ほど）。</summary>
    public const float CalmInterval = 7.049f;

    /// <summary>疲れ・痛みが最大のときの間隔 [s]。</summary>
    public const float StressedInterval = 2.350f;

    /// <summary>1 回のまばたきの長さ [s]（落ち着いているとき。疲れると <see cref="TiredExtraSeconds"/> だけ長く）。</summary>
    public const float BlinkSeconds = 0.2685f;

    /// <summary>疲れ切ったときに、1 回のまばたきが長くなる分 [s]。</summary>
    public const float TiredExtraSeconds = 0.1007f;

    /// <summary>まばたきのうち、閉じる側が占める割合（残りが開く側）。</summary>
    public const float ClosingShare = 0.35f;

    /// <summary>反射で閉じたあと、開いていく時間 [s]。</summary>
    public const float FlinchOpenSeconds = 0.1343f;

    /// <summary>打撃の強さ 0 のときに目をつぶる時間 [s]。強さ 1 でさらに <see cref="FlinchExtraSeconds"/> 足す。</summary>
    public const float FlinchBaseSeconds = 0.2014f;

    /// <summary>強さ 1 の打撃で足す、目をつぶる時間 [s]。</summary>
    public const float FlinchExtraSeconds = 0.9231f;

    /// <summary>反射の強さの基準のエネルギー [J]。強さ = 1 − exp(−E / この値) × 目の近さ。</summary>
    public const float FlinchEnergy = 422.5f;

    /// <summary>打たれた点が目から離れるほど反射が弱まる距離 [m]（exp(−d / この値)）。</summary>
    public const float FlinchReach = 1.127f;

    /// <summary>痛みの記憶が抜ける速さ [/s]。</summary>
    public const float PainRecoveryRate = 0.0715f;

    /// <summary>痛みで目を細める最大の閉じ具合。</summary>
    public const float SquintMax = 0.3f;

    /// <summary>意識が無いときの閉じ具合の上限（死ねば 1）。</summary>
    public const float DroopMax = 0.95f;

    private uint _seed;
    private bool _seeded;
    private int _count;
    private float _untilBlink;
    private float _blinkTime = -1f;
    private float _blinkLength;
    private float _flinchTimer;
    private float _pain;
    private float _droop;
    private bool _dead;

    /// <summary>いまのまぶたの閉じ具合（0 = 開く、1 = 閉じる）。</summary>
    public float Closure { get; private set; }

    /// <summary>痛みの記憶（0..1）。</summary>
    public float Pain => _pain;

    /// <summary>
    /// 時間を <paramref name="dt"/> 進める。<paramref name="seedHint"/> は初回だけ使う、ファイターごとに違う整数（まばたきの数列の種）。
    /// </summary>
    /// <param name="dt">時間 [s]。</param>
    /// <param name="seedHint">種。</param>
    /// <param name="winded">息の上がり方（0..1）。</param>
    /// <param name="awareness">意識の薄れ方（<see cref="Fighter.Awareness"/>、0..1）。</param>
    /// <param name="dead">死んでいるか。</param>
    public void Advance(float dt, int seedHint, float winded, float awareness, bool dead)
    {
        if (!_seeded)
        {
            _seed = unchecked(((uint)seedHint * 2654435761u) + 0x9E3779B9u);
            _seeded = true;
            _untilBlink = Lerp(0.8392f, 4.196f, Hash01(_seed, -1));
        }

        _dead = dead;
        _pain = MathF.Max(0f, _pain - (dt * PainRecoveryRate));
        _flinchTimer = MathF.Max(0f, _flinchTimer - dt);
        var stress = MathF.Max(winded * 0.7f, _pain);
        if (_blinkTime >= 0f)
        {
            _blinkTime += dt;
            if (_blinkTime >= _blinkLength)
            {
                _blinkTime = -1f;
            }
        }
        else
        {
            _untilBlink -= dt;
            if (_untilBlink <= 0f)
            {
                _blinkTime = 0f;
                _blinkLength = BlinkSeconds + (TiredExtraSeconds * Math.Clamp(winded, 0f, 1f));
                _count++;
                _untilBlink = Lerp(CalmInterval, StressedInterval, Math.Clamp(stress, 0f, 1f)) * Lerp(0.55f, 1.45f, Hash01(_seed, _count));
            }
        }

        var blink = 0f;
        if (_blinkTime >= 0f)
        {
            var u = _blinkTime / MathF.Max(1e-3f, _blinkLength);
            blink = u < ClosingShare ? Smooth(u / ClosingShare) : 1f - Smooth((u - ClosingShare) / (1f - ClosingShare));
        }

        var flinch = Math.Clamp(_flinchTimer / FlinchOpenSeconds, 0f, 1f);
        // 意識が薄れるほどまぶたが落ち（awareness 1 で 0）、痛みの間は目を細める。
        _droop = _dead ? 1f : DroopMax * MathF.Pow(1f - Math.Clamp(awareness, 0f, 1f), 1.5f);
        var baseline = MathF.Max(_droop, SquintMax * _pain);
        Closure = baseline + ((1f - baseline) * MathF.Max(blink, Smooth(flinch)));
    }

    /// <summary>
    /// 打撃を受けた: 反射で目をつぶる。<paramref name="energy"/> は打撃のエネルギー [J]、<paramref name="distanceToHead"/> は当たった点と頭の距離 [m]
    /// （目の近くほど強く反射する）。強さは連続（1 − exp(−E / <see cref="FlinchEnergy"/>) × exp(−d / <see cref="FlinchReach"/>)）。
    /// </summary>
    public void Flinch(float energy, float distanceToHead)
    {
        var strength = (1f - MathF.Exp(-MathF.Max(0f, energy) / FlinchEnergy)) * MathF.Exp(-MathF.Max(0f, distanceToHead) / FlinchReach);
        _flinchTimer = MathF.Max(_flinchTimer, FlinchBaseSeconds + (FlinchExtraSeconds * strength));
        _pain = MathF.Min(1f, _pain + (0.5f * strength));
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Hash01(uint seed, int n)
    {
        unchecked
        {
            var h = seed ^ ((uint)n * 0x85EBCA6Bu);
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
