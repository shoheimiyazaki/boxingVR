using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MiniRing.Motion;

/// <summary>
/// VR の動きの記録 1 フレーム（<c>.vrmotion</c>）。生の入力（人の体の寸法のまま [m]）と、<see cref="VrPlayerMapper"/> が出した写像後の値
/// （体フレーム、<b>腕の長さ = 1 に正規化</b>。人形の大きさに依らず当てはめるため。<c>StaffForms</c> と同じ単位）の両方を持つ。
/// 時刻はクリップの始まりからの秒（実時間ではなく試合の刻みの和）。
/// </summary>
public readonly record struct VrMotionFrame(
    float Time,
    // ---- 生の入力（ワールド、人の体の寸法 [m]） ----
    Vector3 HeadPosition, Quaternion HeadOrientation,
    Vector3 LeftHandPosition, Quaternion LeftHandOrientation, bool LeftTracked,
    Vector3 RightHandPosition, Quaternion RightHandOrientation, bool RightTracked,
    float LeftTrigger, float RightTrigger, float LeftGrip, float RightGrip,
    Vector2 LeftStick, Vector2 RightStick,
    // ---- 写像後（体フレーム、腕の長さ = 1） ----
    Vector3 LeftTarget, Vector3 RightTarget,
    Quaternion? LeftWeaponOrientation, Quaternion? RightWeaponOrientation,
    bool LeftSlide, bool RightSlide, bool LeftGrab, bool RightGrab,
    Vector3 HeadOffset, Vector3 MoveLocal)
{
    /// <summary>列の名前（<see cref="ToNumbers"/> の並び。ファイルの見出しにも書く）。</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "t",
        "headPos.x", "headPos.y", "headPos.z", "headQ.x", "headQ.y", "headQ.z", "headQ.w",
        "lPos.x", "lPos.y", "lPos.z", "lQ.x", "lQ.y", "lQ.z", "lQ.w", "lTracked",
        "rPos.x", "rPos.y", "rPos.z", "rQ.x", "rQ.y", "rQ.z", "rQ.w", "rTracked",
        "lTrigger", "rTrigger", "lGrip", "rGrip",
        "lStick.x", "lStick.y", "rStick.x", "rStick.y",
        "lTarget.x", "lTarget.y", "lTarget.z", "rTarget.x", "rTarget.y", "rTarget.z",
        "lWeaponQ.x", "lWeaponQ.y", "lWeaponQ.z", "lWeaponQ.w", "lHasWeaponQ",
        "rWeaponQ.x", "rWeaponQ.y", "rWeaponQ.z", "rWeaponQ.w", "rHasWeaponQ",
        "lSlide", "rSlide", "lGrab", "rGrab",
        "headOffset.x", "headOffset.y", "headOffset.z", "move.x", "move.y", "move.z",
    ];

    /// <summary>数の並び（<see cref="Columns"/> の順。真偽は 0/1）。</summary>
    public float[] ToNumbers()
    {
        var q = new List<float>(Columns.Count) { Time };
        void V(Vector3 v) { q.Add(v.X); q.Add(v.Y); q.Add(v.Z); }
        void Q(Quaternion r) { q.Add(r.X); q.Add(r.Y); q.Add(r.Z); q.Add(r.W); }
        void B(bool b) => q.Add(b ? 1f : 0f);
        void OptQ(Quaternion? r) { Q(r ?? Quaternion.Identity); B(r.HasValue); }
        V(HeadPosition); Q(HeadOrientation);
        V(LeftHandPosition); Q(LeftHandOrientation); B(LeftTracked);
        V(RightHandPosition); Q(RightHandOrientation); B(RightTracked);
        q.Add(LeftTrigger); q.Add(RightTrigger); q.Add(LeftGrip); q.Add(RightGrip);
        q.Add(LeftStick.X); q.Add(LeftStick.Y); q.Add(RightStick.X); q.Add(RightStick.Y);
        V(LeftTarget); V(RightTarget);
        OptQ(LeftWeaponOrientation); OptQ(RightWeaponOrientation);
        B(LeftSlide); B(RightSlide); B(LeftGrab); B(RightGrab);
        V(HeadOffset); V(MoveLocal);
        return [.. q];
    }

    /// <summary><see cref="ToNumbers"/> の逆。</summary>
    public static VrMotionFrame FromNumbers(IReadOnlyList<float> n)
    {
        if (n.Count != Columns.Count)
        {
            throw new FormatException($"A vrmotion frame needs {Columns.Count} numbers, got {n.Count}.");
        }

        var i = 0;
        float F() => n[i++];
        Vector3 V() => new(F(), F(), F());
        Quaternion Q() => new(F(), F(), F(), F());
        bool B() => F() > 0.5f;
        Quaternion? OptQ() { var r = Q(); return B() ? r : null; }
        var time = F();
        var headPos = V(); var headQ = Q();
        var lPos = V(); var lQ = Q(); var lTracked = B();
        var rPos = V(); var rQ = Q(); var rTracked = B();
        var lTrig = F(); var rTrig = F(); var lGrip = F(); var rGrip = F();
        var lStick = new Vector2(F(), F()); var rStick = new Vector2(F(), F());
        var lTarget = V(); var rTarget = V();
        var lw = OptQ(); var rw = OptQ();
        var lSlide = B(); var rSlide = B(); var lGrab = B(); var rGrab = B();
        var headOffset = V(); var move = V();
        return new VrMotionFrame(time, headPos, headQ, lPos, lQ, lTracked, rPos, rQ, rTracked, lTrig, rTrig, lGrip, rGrip, lStick, rStick,
            lTarget, rTarget, lw, rw, lSlide, rSlide, lGrab, rGrab, headOffset, move);
    }

    /// <summary>2 つのフレームの間（<paramref name="t"/> = 0..1）。真偽は前のフレームの値、向きは球面補間（どちらかが無ければ前の値）。</summary>
    public static VrMotionFrame Lerp(in VrMotionFrame a, in VrMotionFrame b, float t)
    {
        static Quaternion S(Quaternion x, Quaternion y, float t) => Quaternion.Slerp(VrRawFrame.OrIdentity(x), VrRawFrame.OrIdentity(y), t);
        static Quaternion? O(Quaternion? x, Quaternion? y, float t) => x is { } p && y is { } q ? S(p, q, t) : x;
        return new VrMotionFrame(
            float.Lerp(a.Time, b.Time, t),
            Vector3.Lerp(a.HeadPosition, b.HeadPosition, t), S(a.HeadOrientation, b.HeadOrientation, t),
            Vector3.Lerp(a.LeftHandPosition, b.LeftHandPosition, t), S(a.LeftHandOrientation, b.LeftHandOrientation, t), a.LeftTracked,
            Vector3.Lerp(a.RightHandPosition, b.RightHandPosition, t), S(a.RightHandOrientation, b.RightHandOrientation, t), a.RightTracked,
            float.Lerp(a.LeftTrigger, b.LeftTrigger, t), float.Lerp(a.RightTrigger, b.RightTrigger, t),
            float.Lerp(a.LeftGrip, b.LeftGrip, t), float.Lerp(a.RightGrip, b.RightGrip, t),
            Vector2.Lerp(a.LeftStick, b.LeftStick, t), Vector2.Lerp(a.RightStick, b.RightStick, t),
            Vector3.Lerp(a.LeftTarget, b.LeftTarget, t), Vector3.Lerp(a.RightTarget, b.RightTarget, t),
            O(a.LeftWeaponOrientation, b.LeftWeaponOrientation, t), O(a.RightWeaponOrientation, b.RightWeaponOrientation, t),
            a.LeftSlide, a.RightSlide, a.LeftGrab, a.RightGrab,
            Vector3.Lerp(a.HeadOffset, b.HeadOffset, t), Vector3.Lerp(a.MoveLocal, b.MoveLocal, t));
    }
}

/// <summary>
/// <c>.vrmotion</c> の見出し。長さは人の体の寸法のまま [m] と、腕の長さ（<see cref="PlayerArmReach"/>）で割った正規化の両方が取り出せる
/// （<see cref="VrMotionClip.ToHuman"/> / <see cref="VrMotionClip.ToNormalized"/>）。
/// </summary>
/// <param name="PlayerHeight">身長のキャリブレーション値 [m]。</param>
/// <param name="PlayerArmReach">プレイヤーの腕の長さの見積もり [m]（肩から拳まで）。</param>
/// <param name="PuppetArmReach">記録したときの人形の腕の長さ [m]（写像後の値を正規化する前の尺度。参考）。</param>
/// <param name="MotionGain">写像の増幅率（<see cref="VrPlayerMapper.MotionGain"/>）。</param>
/// <param name="Weapon">選んだ武器（<c>WeaponKind</c> の名前。素手なら null）。</param>
/// <param name="Note">メモ（稽古の名前など）。</param>
public sealed record VrMotionHeader(float PlayerHeight, float PlayerArmReach, float PuppetArmReach, float MotionGain, string? Weapon = null, string Note = "");

/// <summary>
/// VR の動きの記録（手本として取り込む）。1 回の稽古 = 1 ファイル（<c>motions/&lt;日時&gt;_&lt;武器&gt;.vrmotion</c>、git の外）。
/// 文字の JSON（見出し + 列の名前 + フレームの数の並び）。<see cref="VrMotionImitation"/> が人形になぞらせる。
/// </summary>
public sealed class VrMotionClip
{
    /// <summary>形式の版。列を変えたら上げる。</summary>
    public const int FormatVersion = 1;

    public const string Extension = ".vrmotion";

    public VrMotionClip(VrMotionHeader header, IReadOnlyList<VrMotionFrame> frames)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        if (frames.Count == 0)
        {
            throw new ArgumentException("A vrmotion clip needs at least one frame.", nameof(frames));
        }

        for (var i = 1; i < frames.Count; i++)
        {
            if (!(frames[i].Time >= frames[i - 1].Time))
            {
                throw new ArgumentException($"Frame times must not go backwards (frame {i}).", nameof(frames));
            }
        }
    }

    public VrMotionHeader Header { get; }

    public IReadOnlyList<VrMotionFrame> Frames { get; }

    /// <summary>長さ [s]。</summary>
    public float Duration => Frames[^1].Time - Frames[0].Time;

    /// <summary>時刻 <paramref name="time"/>（クリップの始まりから [s]）のフレーム（間は補う。範囲の外は端）。</summary>
    public VrMotionFrame Sample(float time)
    {
        var t = Frames[0].Time + Math.Clamp(time, 0f, Duration);
        var lo = 0;
        var hi = Frames.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (Frames[mid].Time <= t)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        if (Frames.Count == 1)
        {
            return Frames[0];
        }

        var span = Frames[hi].Time - Frames[lo].Time;
        return VrMotionFrame.Lerp(Frames[lo], Frames[hi], span > 1e-6f ? Math.Clamp((t - Frames[lo].Time) / span, 0f, 1f) : 0f);
    }

    /// <summary>正規化（腕の長さ = 1）の長さを、人の体の寸法 [m] にする。</summary>
    public Vector3 ToHuman(Vector3 normalized) => normalized * Header.PlayerArmReach;

    /// <summary>人の体の寸法 [m] の長さを、腕の長さ = 1 に正規化する。</summary>
    public Vector3 ToNormalized(Vector3 human) => human / Header.PlayerArmReach;

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("format", "vrmotion");
            w.WriteNumber("version", FormatVersion);
            w.WriteNumber("playerHeight", Header.PlayerHeight);
            w.WriteNumber("playerArmReach", Header.PlayerArmReach);
            w.WriteNumber("puppetArmReach", Header.PuppetArmReach);
            w.WriteNumber("motionGain", Header.MotionGain);
            if (Header.Weapon is { } weapon)
            {
                w.WriteString("weapon", weapon);
            }

            w.WriteString("note", Header.Note);
            w.WriteString("units", "raw: metres (world, player body); mapped: arm length = 1 (body frame); quaternions x,y,z,w");
            w.WriteStartArray("columns");
            foreach (var c in VrMotionFrame.Columns)
            {
                w.WriteStringValue(c);
            }

            w.WriteEndArray();
            w.WriteStartArray("frames");
            foreach (var f in Frames)
            {
                w.WriteStartArray();
                foreach (var n in f.ToNumbers())
                {
                    w.WriteNumberValue(n);
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static VrMotionClip FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != "vrmotion")
        {
            throw new FormatException("Not a vrmotion file.");
        }

        var version = root.GetProperty("version").GetInt32();
        if (version != FormatVersion)
        {
            throw new FormatException($"Unsupported vrmotion version {version} (this build reads {FormatVersion}).");
        }

        var columns = root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToArray();
        if (!columns.SequenceEqual(VrMotionFrame.Columns))
        {
            throw new FormatException("The vrmotion columns do not match this build.");
        }

        var header = new VrMotionHeader(
            root.GetProperty("playerHeight").GetSingle(), root.GetProperty("playerArmReach").GetSingle(),
            root.GetProperty("puppetArmReach").GetSingle(), root.GetProperty("motionGain").GetSingle(),
            root.TryGetProperty("weapon", out var weapon) ? weapon.GetString() : null,
            root.TryGetProperty("note", out var note) ? note.GetString() ?? "" : "");
        if (!(header.PlayerArmReach > 0.05f) || !float.IsFinite(header.PlayerArmReach))
        {
            throw new FormatException("The vrmotion player arm length must be positive.");
        }

        var frames = new List<VrMotionFrame>();
        var numbers = new float[VrMotionFrame.Columns.Count];
        foreach (var row in root.GetProperty("frames").EnumerateArray())
        {
            var k = 0;
            foreach (var e in row.EnumerateArray())
            {
                if (k >= numbers.Length)
                {
                    throw new FormatException("A vrmotion frame has too many numbers.");
                }

                var value = e.GetSingle();
                if (!float.IsFinite(value))
                {
                    throw new FormatException("A vrmotion frame has a non-finite number.");
                }

                numbers[k++] = value;
            }

            if (k != numbers.Length)
            {
                throw new FormatException($"A vrmotion frame needs {numbers.Length} numbers, got {k}.");
            }

            frames.Add(VrMotionFrame.FromNumbers(numbers));
        }

        return new VrMotionClip(header, frames);
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
    }

    public static VrMotionClip Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>保存先（<paramref name="directory"/>/&lt;日時&gt;_&lt;武器&gt;.vrmotion）。同じ秒なら _2, _3 … を付ける。</summary>
    public static string NewPath(string directory, string? weapon)
    {
        var stem = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyyMMdd_HHmmss}_{(string.IsNullOrEmpty(weapon) ? "fists" : weapon.ToLowerInvariant())}");
        var path = Path.Combine(directory, stem + Extension);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{stem}_{n}{Extension}"));
        }

        return path;
    }
}
