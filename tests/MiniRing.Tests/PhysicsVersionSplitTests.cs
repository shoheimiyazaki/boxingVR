using System.Text;
using System.Text.Json;
using MiniRing.Materials;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// 物理の版を材料の版（L1）と体の版（L2〜L3）に分けた（2026-11-28、物理の憲法 §2.5）: 記録は両方を持ち、どちらかが違えば再生しない。
/// 材料の版を持たない古い記録（形式 14 まで）は材料の版 1（分ける前の表）として読む。
/// </summary>
[TestClass]
[TestCategory("L0")]
public sealed class PhysicsVersionSplitTests
{
    private static readonly JsonSerializerOptions SettingsJson = new() { IncludeFields = true };

    [TestMethod]
    public void ARecording_CarriesBothVersions()
    {
        var header = ReplayFile.Read(new MemoryStream(Record(MaterialTable.Version))).Header;
        Assert.AreEqual(Fighter.BodyVersion, header.Physics);
        Assert.AreEqual(MaterialTable.Version, header.Material);
        StringAssert.Contains(PhysicsVersions.Label, $"material {MaterialTable.Version}");
    }

    [TestMethod]
    public void ARecording_FromAnotherMaterialTable_IsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ReplayFile.Read(new MemoryStream(Record(MaterialTable.Version + 1))));
    }

    [TestMethod]
    public void AnOldRecording_WithoutAMaterialVersion_ReadsAsTheFirstTable()
    {
        var header = ReplayFile.Read(new MemoryStream(Record(MaterialTable.Version))).Header;
        using var v14 = new MemoryStream();
        using (var writer = new BinaryWriter(v14, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("MRRP"u8.ToArray());
            writer.Write(14);
            writer.Write(header.Seed);
            writer.Write(header.TuningJson);
            writer.Write(header.Host);
            writer.Write(header.AppVersion);
            writer.Write(header.CreatedUtc.Ticks);
            writer.Write(header.CpuName);
            writer.Write(header.CpuStyleJson);
            writer.Write(header.CpuLogic);
            writer.Write(header.Physics);
            writer.Write(JsonSerializer.Serialize(header.MatchSettings, SettingsJson));
        }

        v14.Position = 0;
        Assert.AreEqual(1, ReplayFile.Read(v14).Header.Material);
    }

    private static byte[] Record(int material)
    {
        using var game = new BoxingGame();
        var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            ReplayFile.WriteHeader(writer, new ReplayHeader(game.Seed, JsonSerializer.Serialize(game.Tuning), "test", "0", new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc),
                Material: material));
        }

        return output.ToArray();
    }
}
