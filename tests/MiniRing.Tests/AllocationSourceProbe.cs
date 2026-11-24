using System.Diagnostics.Tracing;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査: 刀の試合（StepCostRegressionTests と同じ）の 1 歩の割り当てが、どの型から来ているか（GC の AllocationTick の標本）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class AllocationSourceProbe
{
    private sealed class Listener : EventListener
    {
        public readonly Dictionary<string, long> Bytes = [];

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || e.Payload is null)
            {
                return;
            }

            var name = e.PayloadNames!.IndexOf("TypeName") is var i and >= 0 ? e.Payload[i]?.ToString() ?? "?" : "?";
            var amount = e.PayloadNames.IndexOf("AllocationAmount64") is var j and >= 0 ? Convert.ToInt64(e.Payload[j], System.Globalization.CultureInfo.InvariantCulture) : 100_000;
            lock (Bytes)
            {
                Bytes[name] = Bytes.GetValueOrDefault(name) + amount;
            }
        }
    }

    [TestMethod]
    public void Probe_WhatAllocates()
    {
        var corners = BoxingCorners.Default with
        {
            Player = SwordBot.Basic, Cpu = SwordBot.Basic,
            PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true, CpuTwoHanded = true,
        };
        using var game = new BoxingGame(seed: 0, corners: corners);
        for (var i = 0; i < 400; i++)
        {
            game.Step(default, 1f / 240f);
        }

        using var listener = new Listener();
        for (var i = 0; i < 3600; i++)
        {
            game.Step(default, 1f / 240f);
        }

        foreach (var (name, bytes) in listener.Bytes.OrderByDescending(kv => kv.Value).Take(25))
        {
            Console.WriteLine($"{bytes / 1024,8} KB  {name}");
        }
    }
}
