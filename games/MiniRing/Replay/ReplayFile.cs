using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MiniRing.Replay;

/// <summary>リプレイの見出し: 試合を作り直すのに必要なもの（乱数の種・調整値・CPU のボット名）と、記録の素性。</summary>
public sealed record ReplayHeader(int Seed, string TuningJson, string Host, string AppVersion, DateTime CreatedUtc,
    string CpuName = Bots.BotCatalog.Legacy, string CpuStyleJson = "", int CpuLogic = 0, int Physics = Fighter.PhysicsVersion,
    ReplayMatchSettings? MatchSettings = null)
{
    /// <summary>
    /// 記録時と同じ CPU の両コーナー。名前ではなく記録したスタイルから作るので、あとで <c>champion</c> を調整し直しても再現できる。
    /// </summary>
    public BoxingCorners Corners
    {
        get
        {
            var corners = CpuStyleJson.Length == 0 ? Bots.BotCatalog.Corners(Bots.BotCatalog.Legacy)
                : Bots.BotCatalog.Corners(Bots.BotStyle.FromJson(CpuStyleJson) with { Name = CpuName });
            return MatchSettings?.Apply(corners) ?? corners;
        }
    }

    public FighterTuning Tuning => JsonSerializer.Deserialize<FighterTuning>(TuningJson) ?? FighterTuning.Default;
}

/// <summary>1 ステップ分の記録: ホストが <see cref="BoxingGame.Step"/> に渡した入力と時間刻み。</summary>
public readonly record struct ReplayStep(BoxingInput Input, float Dt);

/// <summary>読み込んだリプレイ: 見出し・全ステップ・途中のチェックサム（ステップ番号 → 値）。</summary>
public sealed record ReplayData(ReplayHeader Header, IReadOnlyList<ReplayStep> Steps, IReadOnlyDictionary<int, ulong> Checksums)
{
    public float Duration => Steps.Sum(s => s.Dt);
}

/// <summary>
/// リプレイのファイル形式（バイナリ・リトルエンディアン）。
/// <code>
/// "MRRP" int32 version | int32 seed | string tuningJson | string host | string appVersion | int64 createdUtcTicks | string cpuName | string cpuStyleJson | int32 cpuLogic | int32 physics
/// （physics は版 3 から: 体の物理の版（Fighter.PhysicsVersion）。違う版の記録は読まない。CPU の欄は版 2 から。cpuStyleJson が空なら legacy。cpuLogic はボットの判断の仕組みの版（BoxerBot.LogicVersion）。
/// 版 1 の記録は CPU が legacy だった頃のもの）
/// 以降、レコードの並び:
///   0x01 Step:     float dt | BoxingInput（目標など float × 15 + bool × 3）
///                | v4: float 胴のひねり | 左右各(float Spring, bool LetGo, bool Orientation有無, あれば float×4)
///                | v5: 左右の手形 int32 | 左右の脚指令（有無、目標、膝目標・向きの有無と値、Spring）
///   0x02 Checksum: int32 step（そのステップを終えた直後）| uint64 BoxingGame.StateChecksum()
/// </code>
/// v3 の Step は追加部分を持たず、Spring・ひねりは 0 として読む。
/// 途中で終わっていても（クラッシュ・強制終了）、最後の完全なレコードまで読める。
/// </summary>
public static class ReplayFile
{
    public const int Version = 6;
    public const string Extension = ".mrrep";
    /// <summary>1 ステップの時間刻みとして受け付ける上限 [s]（壊れたファイルの値を物理に渡さない）。</summary>
    public const float MaxDt = 0.25f;
    private const byte StepTag = 1;
    private const byte ChecksumTag = 2;
    private static readonly byte[] Magic = "MRRP"u8.ToArray();
    private static readonly JsonSerializerOptions SettingsJsonOptions = new() { IncludeFields = true };

    public static void WriteHeader(BinaryWriter writer, ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(header.Seed);
        writer.Write(header.TuningJson);
        writer.Write(header.Host);
        writer.Write(header.AppVersion);
        writer.Write(header.CreatedUtc.Ticks);
        writer.Write(header.CpuName);
        writer.Write(header.CpuStyleJson);
        writer.Write(header.CpuLogic);
        writer.Write(header.Physics);
        writer.Write(JsonSerializer.Serialize(header.MatchSettings, SettingsJsonOptions));
    }

    public static void WriteStep(BinaryWriter writer, in ReplayStep step)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(StepTag);
        writer.Write(step.Dt);
        var c = step.Input.Player;
        Write(writer, c.MoveWorld);
        Write(writer, c.FaceDirection);
        Write(writer, c.Left.TargetLocal);
        writer.Write(c.Left.Grip);
        Write(writer, c.Right.TargetLocal);
        writer.Write(c.Right.Grip);
        Write(writer, c.HeadOffset);
        writer.Write(step.Input.ResetRequested);
        writer.Write(c.TwistBias);
        WriteHandExtras(writer, c.Left);
        WriteHandExtras(writer, c.Right);
        writer.Write((int)c.Left.Shape);
        writer.Write((int)c.Right.Shape);
        WriteLeg(writer, c.LeftLeg);
        WriteLeg(writer, c.RightLeg);
    }

    public static void WriteChecksum(BinaryWriter writer, int stepIndex, ulong checksum)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ChecksumTag);
        writer.Write(stepIndex);
        writer.Write(checksum);
    }

    public static ReplayData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a Mini Ring replay (bad magic).");
        }

        var version = reader.ReadInt32();
        if (version is < 1 or > Version)
        {
            throw new InvalidDataException($"Replay format version {version} is not supported (expected 1..{Version}).");
        }

        var header = new ReplayHeader(reader.ReadInt32(), reader.ReadString(), reader.ReadString(), reader.ReadString(),
            new DateTime(reader.ReadInt64(), DateTimeKind.Utc));
        var recordedPhysics = 0;
        if (version >= 2)
        {
            header = header with { CpuName = reader.ReadString(), CpuStyleJson = reader.ReadString(), CpuLogic = reader.ReadInt32() };
            recordedPhysics = version >= 3 ? reader.ReadInt32() : 0;
            if (version >= 6)
            {
                try
                {
                    header = header with { MatchSettings = JsonSerializer.Deserialize<ReplayMatchSettings>(reader.ReadString(), SettingsJsonOptions) };
                }
                catch (JsonException e)
                {
                    throw new InvalidDataException("Replay match settings are invalid.", e);
                }
            }
            if (header.CpuStyleJson.Length == 0 && header.CpuName != Bots.BotCatalog.Legacy)
            {
                throw new InvalidDataException($"Replay CPU '{header.CpuName}' has no recorded style.");
            }

            // 仕組みの違うボットで再生すると、黙ってどこかでずれる。読む前にはっきり断る。
            if (header.CpuStyleJson.Length > 0 && header.CpuLogic != Bots.BoxerBot.LogicVersion)
            {
                throw new InvalidDataException(
                    $"Replay CPU was recorded with bot logic v{header.CpuLogic}; this build has v{Bots.BoxerBot.LogicVersion} and cannot reproduce it.");
            }

            try
            {
                _ = header.Corners; // スタイルの JSON を読む前に確かめる
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or ArgumentException)
            {
                throw new InvalidDataException($"Replay CPU style is invalid: {e.Message}", e);
            }
        }

        // 版 3 から体の物理の版を持つ。それより前の記録は、足の運び・関節の位置・非線形のダメージより前の体で記録したもので、再現できない。
        if (recordedPhysics != Fighter.PhysicsVersion)
        {
            throw new InvalidDataException(
                $"Replay was recorded with body physics v{recordedPhysics}; this build has v{Fighter.PhysicsVersion} and cannot reproduce it.");
        }

        header = header with { Physics = recordedPhysics };
        var steps = new List<ReplayStep>();
        var checksums = new Dictionary<int, ulong>();
        try
        {
            while (stream.Position < stream.Length)
            {
                switch (reader.ReadByte())
                {
                    case StepTag:
                        var dt = reader.ReadSingle();
                        if (!float.IsFinite(dt) || dt <= 0f || dt > MaxDt)
                        {
                            throw new InvalidDataException($"Invalid timestep {dt} in step {steps.Count}.");
                        }

                        var command = new FighterCommand(Read3(reader), Read3(reader),
                            new HandCommand(Read3(reader), reader.ReadBoolean()),
                            new HandCommand(Read3(reader), reader.ReadBoolean()),
                            Read3(reader));
                        var reset = reader.ReadBoolean();
                        if (version >= 4)
                        {
                            command = command with
                            {
                                TwistBias = ReadFinite(reader),
                                Left = ReadHandExtras(reader, command.Left),
                                Right = ReadHandExtras(reader, command.Right),
                            };
                        }

                        if (version >= 5)
                        {
                            command = command with
                            {
                                Left = command.Left with { Shape = ReadShape(reader) },
                                Right = command.Right with { Shape = ReadShape(reader) },
                                LeftLeg = ReadLeg(reader),
                                RightLeg = ReadLeg(reader),
                            };
                        }

                        steps.Add(new ReplayStep(new BoxingInput(command, reset), dt));
                        break;
                    case ChecksumTag:
                        var index = reader.ReadInt32();
                        checksums[index] = reader.ReadUInt64();
                        break;
                    default:
                        throw new InvalidDataException($"Unknown replay record at byte {stream.Position - 1}.");
                }
            }
        }
        catch (EndOfStreamException)
        {
            // 書き込み途中で終わった最後のレコードは捨てる（それまでは完全に再現できる）。
        }

        return new ReplayData(header, steps, checksums);
    }

    public static ReplayData Read(string path)
    {
        using var file = File.OpenRead(path);
        return Read(file);
    }

    private static void Write(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    private static void WriteHandExtras(BinaryWriter writer, HandCommand hand)
    {
        writer.Write(hand.Spring);
        writer.Write(hand.LetGo);
        writer.Write(hand.Orientation.HasValue);
        if (hand.Orientation is { } q)
        {
            writer.Write(q.X);
            writer.Write(q.Y);
            writer.Write(q.Z);
            writer.Write(q.W);
        }
    }

    private static void WriteLeg(BinaryWriter writer, LegCommand? leg)
    {
        writer.Write(leg.HasValue);
        if (leg is not { } value)
        {
            return;
        }

        Write(writer, value.TargetLocal);
        writer.Write(value.KneeTargetLocal.HasValue);
        if (value.KneeTargetLocal is { } knee)
        {
            Write(writer, knee);
        }

        writer.Write(value.Orientation.HasValue);
        if (value.Orientation is { } q)
        {
            writer.Write(q.X);
            writer.Write(q.Y);
            writer.Write(q.Z);
            writer.Write(q.W);
        }

        writer.Write(value.Spring);
    }

    private static HandShape ReadShape(BinaryReader reader)
    {
        var shape = (HandShape)reader.ReadInt32();
        return Enum.IsDefined(shape) ? shape : throw new InvalidDataException("記録の手の形が不正です。");
    }

    private static LegCommand? ReadLeg(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
        {
            return null;
        }

        var target = Read3(reader);
        Vector3? knee = reader.ReadBoolean() ? Read3(reader) : null;
        Quaternion? orientation = null;
        if (reader.ReadBoolean())
        {
            var q = new Quaternion(ReadFinite(reader), ReadFinite(reader), ReadFinite(reader), ReadFinite(reader));
            if (MathF.Abs(q.LengthSquared() - 1f) > 0.01f)
            {
                throw new InvalidDataException("脚の向きは単位クォータニオンで指定してください。");
            }

            orientation = q;
        }

        var spring = ReadFinite(reader);
        if (spring is < 0f or > 1f)
        {
            throw new InvalidDataException("脚のバネ伝達は 0..1 で指定してください。");
        }

        return new LegCommand(target, knee, orientation, spring);
    }

    private static HandCommand ReadHandExtras(BinaryReader reader, HandCommand hand)
    {
        var spring = ReadFinite(reader);
        if (spring is < 0f or > 1f)
        {
            throw new InvalidDataException("拳へのバネ伝達は 0..1 で指定してください。");
        }

        var letGo = reader.ReadBoolean();
        Quaternion? orientation = null;
        if (reader.ReadBoolean())
        {
            var q = new Quaternion(ReadFinite(reader), ReadFinite(reader), ReadFinite(reader), ReadFinite(reader));
            if (MathF.Abs(q.LengthSquared() - 1f) > 0.01f)
            {
                throw new InvalidDataException("武器の向きは単位クォータニオンで指定してください。");
            }

            orientation = q;
        }

        return hand with { Spring = spring, LetGo = letGo, Orientation = orientation };
    }

    private static float ReadFinite(BinaryReader reader)
    {
        var value = reader.ReadSingle();
        return float.IsFinite(value) ? value : throw new InvalidDataException("記録に有限でない指令が含まれています。");
    }

    private static Vector3 Read3(BinaryReader reader)
    {
        var v = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)
            ? v
            : throw new InvalidDataException($"Non-finite input value {v} at byte {reader.BaseStream.Position - 12}.");
    }
}
