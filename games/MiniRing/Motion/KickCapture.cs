using System.Globalization;
using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 人の回し蹴りのモーションキャプチャ（極真空手、ミットを蹴る。Szczęsna ら 2021、CC0。<c>data/mocap/kyokushin/</c> の CSV。
/// 切り出し方は <c>tools/mocap/extract_kicks.py</c>）の読み込みと、膝の制御の言葉への読み替え。
/// <para>
/// 読み込み: 長さ [mm]・Z 上のデータを、ゲームの座標（Y 上・+Z 前・+X 左、メートル）へ回す（(x, y, z) → (x, z, −y)。右手系のまま）。
/// 読み替え（<see cref="Analyze"/>）: 蹴る脚（右脚）について、ミットの方向を基準にした体フレーム（前 = 股からミットへの水平の向き、左、上）で、
/// 腿の軸（股 → 膝）の方位（前から脚の外へ）・仰角、すねの曲げの面のねじりと膝の曲げ（<see cref="LegKnee.ForShin"/>。ゲームの膝の制御と同じ幾何なので、
/// ゲームの <c>KneeTwist</c>・<c>KneeBend</c> の定義そのもの）、骨盤の回り（ミットの方向に対する向き）、肩の回りと骨盤の回りの差（胸のひねり）を出す。
/// 当たる瞬間は、足首の速さの山の前後 0.3 秒で、足首がミットにいちばん近い時刻。
/// </para>
/// </summary>
public sealed class KickCapture
{
    private static readonly string[] Labels =
    [
        "RHJC", "RKJC", "RAJC", "LHJC", "LKJC", "LAJC", "RTOE", "RHEE", "LTOE", "LHEE", "LASI", "RASI", "LPSI", "RPSI", "LSJC", "RSJC", "CLAV", "C7",
        "Tarcza1", "Tarcza2", "Tarcza3", "Tarcza4", "Tarcza5", "Tarcza6",
    ];

    private static readonly string[] MittLabels = ["Tarcza1", "Tarcza2", "Tarcza3", "Tarcza4", "Tarcza5", "Tarcza6"];

    private readonly Dictionary<string, Vector3?[]> _points;

    private KickCapture(string name, float rate, int peakFrame, Dictionary<string, Vector3?[]> points, int frames)
    {
        Name = name;
        Rate = rate;
        PeakFrame = peakFrame;
        _points = points;
        FrameCount = frames;
    }

    public string Name { get; }

    /// <summary>撮影の速さ [Hz]（200）。</summary>
    public float Rate { get; }

    /// <summary>足首の速さの山のフレーム（切り出した区間の中）。</summary>
    public int PeakFrame { get; }

    public int FrameCount { get; }

    public IReadOnlyCollection<string> LabelNames => _points.Keys;

    /// <summary>ラベルの位置（ゲームの座標 [m]）。欠損は null。</summary>
    public Vector3? Point(string label, int frame) => _points[label][frame];

    public static KickCapture Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 3 || !lines[0].StartsWith('#'))
        {
            throw new InvalidDataException($"{path}: header line is missing.");
        }

        var meta = lines[0][1..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        var rate = float.Parse(meta["rate"], CultureInfo.InvariantCulture);
        var header = lines[1].Split(',');
        var count = lines.Length - 2;
        var points = new Dictionary<string, Vector3?[]>();
        foreach (var label in Labels)
        {
            points[label] = new Vector3?[count];
        }

        for (var i = 0; i < count; i++)
        {
            var cells = lines[i + 2].Split(',');
            if (cells.Length != header.Length)
            {
                throw new InvalidDataException($"{path}: row {i} has {cells.Length} cells, expected {header.Length}.");
            }

            for (var c = 1; c + 2 < header.Length; c += 3)
            {
                if (cells[c].Length == 0 || cells[c + 1].Length == 0 || cells[c + 2].Length == 0)
                {
                    continue;
                }

                var x = float.Parse(cells[c], CultureInfo.InvariantCulture) / 1000f;
                var y = float.Parse(cells[c + 1], CultureInfo.InvariantCulture) / 1000f;
                var z = float.Parse(cells[c + 2], CultureInfo.InvariantCulture) / 1000f;
                points[header[c][..header[c].LastIndexOf('_')]][i] = new Vector3(x, z, -y);
            }
        }

        return new KickCapture(meta["source"], rate, int.Parse(meta["peak_frame"], CultureInfo.InvariantCulture), points, count);
    }

    /// <summary>1 フレームの読み替え。角は rad、長さは m。</summary>
    public readonly record struct Sample(float Time, Vector3 Hip, Vector3 Knee, Vector3 Ankle, float ThighLength, float ShinLength,
        float Azimuth, float Elevation, float Twist, float Bend, float PelvisYaw, float ShoulderTwist);

    /// <summary>読み替えた蹴り 1 本。<see cref="ImpactIndex"/> は足首がミットにいちばん近いフレーム。</summary>
    public sealed record Track(string Name, float Rate, Sample[] Samples, int ImpactIndex, Vector3 Mitt, float AnkleToMitt)
    {
        public float ImpactTime => Samples[ImpactIndex].Time;
    }

    /// <summary>
    /// 右脚の蹴りを読み替える。欠損は前後のフレームから線形に埋める。構え（最初の 0.1 秒）のミットの位置の平均を、蹴る目標とする
    /// （ミットは蹴られると動くので、当たる前の位置）。
    /// </summary>
    public Track Analyze()
    {
        var hip = Series("RHJC");
        var knee = Series("RKJC");
        var ankle = Series("RAJC");
        var otherHip = Series("LHJC");
        var leftShoulder = Series("LSJC");
        var rightShoulder = Series("RSJC");
        var mittPoints = MittLabels.Select(Series).ToArray();
        var lead = Math.Max(1, (int)(0.1f * Rate));
        var mitt = Average(mittPoints.Select(s => (Vector3?)Average(s.Take(lead).Select(p => (Vector3?)p).ToArray())).ToArray());
        var samples = new Sample[FrameCount];
        for (var f = 0; f < FrameCount; f++)
        {
            var forward = new Vector3(mitt.X - hip[f].X, 0f, mitt.Z - hip[f].Z);
            forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : Vector3.UnitZ;
            var left = Vector3.Cross(Vector3.UnitY, forward);
            var thigh = knee[f] - hip[f];
            var shin = ankle[f] - knee[f];
            var axis = Vector3.Normalize(thigh);
            var (twist, bend) = LegKnee.ForShin(axis, forward, Vector3.Normalize(shin));
            // 蹴る脚は右（外 = 右 = −left）。
            var azimuth = MathF.Atan2(-Vector3.Dot(thigh, left), Vector3.Dot(thigh, forward));
            var elevation = MathF.Asin(Math.Clamp(axis.Y, -1f, 1f));
            var pelvis = YawRelative(hip[f] - otherHip[f], forward);
            var shoulders = YawRelative(rightShoulder[f] - leftShoulder[f], forward);
            samples[f] = new Sample(f / Rate, hip[f], knee[f], ankle[f], thigh.Length(), shin.Length(), azimuth, elevation, twist, bend, pelvis, Wrap(shoulders - pelvis));
        }

        // 当たる瞬間: 足首の速さの山の前後 0.3 秒で、足首がミットにいちばん近いフレーム。
        var window = (int)(0.3f * Rate);
        var impact = Enumerable.Range(Math.Max(0, PeakFrame - window), Math.Min(FrameCount, PeakFrame + window) - Math.Max(0, PeakFrame - window))
            .MinBy(f => Vector3.Distance(ankle[f], mitt));
        return new Track(Name, Rate, samples, impact, mitt, Vector3.Distance(ankle[impact], mitt));
    }

    /// <summary>右腰 − 左腰（右 → 左の逆。左右の軸）の向きから、体の前（骨盤・肩の前 = 左右の軸と上の外積）の、ミットの方向に対する回り [rad]（左回りが正）。</summary>
    private static float YawRelative(Vector3 rightToLeftNegative, Vector3 mittDirection)
    {
        // rightToLeftNegative = 右の点 − 左の点。左向きの軸は −rightToLeftNegative。体の前 = 左 × 上。
        var left = -new Vector3(rightToLeftNegative.X, 0f, rightToLeftNegative.Z);
        if (left.LengthSquared() < 1e-8f)
        {
            return 0f;
        }

        var facing = Vector3.Cross(Vector3.Normalize(left), Vector3.UnitY);
        return SignedYaw(mittDirection, facing);
    }

    /// <summary>基準の向き <paramref name="from"/> から <paramref name="to"/> への水平の回り [rad]（Y まわりの右手系。左回りが正）。</summary>
    private static float SignedYaw(Vector3 from, Vector3 to)
        => MathF.Atan2((from.Z * to.X) - (from.X * to.Z), (from.X * to.X) + (from.Z * to.Z));

    private static float Wrap(float a) => LegKnee.Wrap(a);

    private Vector3[] Series(string label)
    {
        var raw = _points[label];
        var result = new Vector3[raw.Length];
        var known = Enumerable.Range(0, raw.Length).Where(i => raw[i].HasValue).ToArray();
        if (known.Length == 0)
        {
            throw new InvalidDataException($"{Name}: label {label} has no data.");
        }

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] is { } p)
            {
                result[i] = p;
                continue;
            }

            var before = known.Where(k => k < i).DefaultIfEmpty(-1).Max();
            var after = known.Where(k => k > i).DefaultIfEmpty(-1).Min();
            result[i] = before < 0 ? raw[after]!.Value : after < 0 ? raw[before]!.Value
                : Vector3.Lerp(raw[before]!.Value, raw[after]!.Value, (i - before) / (float)(after - before));
        }

        return result;
    }

    private static Vector3 Average(IReadOnlyList<Vector3?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return present.Length == 0 ? Vector3.Zero : present.Aggregate(Vector3.Zero, (a, b) => a + b) / present.Length;
    }

    private static Vector3 Average(IEnumerable<Vector3> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? Vector3.Zero : array.Aggregate(Vector3.Zero, (a, b) => a + b) / array.Length;
    }
}
