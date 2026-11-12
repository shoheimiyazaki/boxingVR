using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 調査: 武器が相手の体に「刺さったまま何も起きない」場面があるか。練習試合を物理の 1 歩ごとに進め、握られた武器の埋まったセルの中心が、
/// 相手（と自分）の体の部位の<b>埋まったセル</b>の中にある数（めり込み）と、<b>始めは埋まっていて今は空いたセル</b>の中にある数
/// （斬った傷の中にある: 本当に刺さっている）を数える。めり込みが続いた区間ごとに、長さ・深さ・その間に壊したセル・接触が止まったかを並べる。
/// </summary>
internal static class BladeProbe
{
    private const float Dt = 1f / 240f;

    private sealed class Episode
    {
        public required string Pair;
        public required string Weapon;
        public required string Victim;
        public required BodyPartKind Part;
        public float Start;
        public float End;
        public int MaxCells;
        public int Removed;
        public int Touched;
        public int Stopped;
        public int Untested;
        public string Materials = "";
    }

    public static string Run(IReadOnlyList<Contender> contenders, string[] pairs, int bouts, float seconds, TextWriter log)
    {
        var report = new StringBuilder();
        var episodes = new List<Episode>();
        var totals = new Dictionary<string, (int Steps, int Embedded, int InWound)>();
        foreach (var pair in pairs)
        {
            var names = pair.Split('-');
            var red = contenders.Single(c => c.Name == names[0]);
            var blue = contenders.Single(c => c.Name == names[1]);
            for (var b = 0; b < bouts; b++)
            {
                var (e, steps, embedded, wound) = Bout(red, blue, 1 + b, seconds, pair);
                episodes.AddRange(e);
                var t = totals.GetValueOrDefault(pair);
                totals[pair] = (t.Steps + steps, t.Embedded + embedded, t.InWound + wound);
                log.WriteLine($"{pair} bout {b + 1}: episodes {e.Count}, embedded steps {embedded}/{steps}, in-wound steps {wound}");
            }
        }

        report.AppendLine("| 組 | 物理の歩 | 刃が体の埋まったセルにめり込んでいた歩 | 刃が傷（斬って空いたセル）の中にあった歩 |");
        report.AppendLine("|---|---:|---:|---:|");
        foreach (var (pair, t) in totals)
        {
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {pair} | {t.Steps} | {t.Embedded} ({100f * t.Embedded / t.Steps:0.0}%) | {t.InWound} ({100f * t.InWound / t.Steps:0.0}%) |"));
        }

        foreach (var (label, group) in new[]
                 {
                     ("相手の体", episodes.Where(e => e.Victim == "相手").ToList()),
                     ("自分の体（握った前腕を除く）", episodes.Where(e => e.Victim == "自分" && e.Part != BodyPartKind.Forearm).ToList()),
                 })
        {
            var longOnes = group.Where(e => e.End - e.Start >= 0.1f).ToList();
            report.AppendLine();
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{label}: めり込んだ区間 {group.Count}（0.1 秒以上 {longOnes.Count}、0.25 秒以上 {group.Count(e => e.End - e.Start >= 0.25f)}、0.1 秒以上で何も壊さなかった {longOnes.Count(e => e.Removed == 0)}）。長い順に 25:"));
            report.AppendLine();
            report.AppendLine("| 組 | 武器 | 部位 | 始め [s] | 長さ [s] | 最大のめり込み [セル] | 壊したセル | 接触あり | 止まった | 調べず | めり込んだ材質 |");
            report.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var e in group.OrderByDescending(e => e.End - e.Start).Take(25))
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {e.Pair} | {e.Weapon} | {e.Part} | {e.Start:0.00} | {e.End - e.Start:0.00} | {e.MaxCells} | {e.Removed} | {e.Touched} | {e.Stopped} | {e.Untested} | {e.Materials} |"));
            }
        }

        return report.ToString();
    }

    private static (List<Episode> Episodes, int Steps, int Embedded, int InWound) Bout(Contender red, Contender blue, int seed, float seconds, string pair)
    {
        IFighterControl Make(Contender c, int s) => c.Weapon is null ? c.Create(s) : DisarmedFallback.WithFists(c.Create(s), s);
        using var game = new BoxingGame(VersusLab.Tuning, seed, new BoxingCorners(blue.Name, s => Make(blue, s), s => Make(red, s),
            CpuWeapon: blue.Weapon, PlayerWeapon: red.Weapon, CpuTwoHanded: blue.TwoHanded, PlayerTwoHanded: red.TwoHanded));
        var original = new Dictionary<VoxelGrid, byte[]>();
        var open = new Dictionary<(Weapon, VoxelPart), Episode>();
        var done = new List<Episode>();
        int steps = 0, embeddedSteps = 0, woundSteps = 0;
        var round = game.Round;
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Step(default, Dt);
            steps++;
            if (game.Round != round)
            {
                round = game.Round;
                original.Clear();
                done.AddRange(open.Values);
                open.Clear();
            }

            var fighters = new[] { game.Player, game.Cpu };
            foreach (var f in fighters)
            {
                foreach (var part in f.Voxels!.Parts)
                {
                    if (!original.ContainsKey(part.Body.Grid))
                    {
                        original[part.Body.Grid] = Snapshot(part.Body.Grid);
                    }
                }
            }

            var anyEmbedded = false;
            var anyWound = false;
            var seen = new HashSet<(Weapon, VoxelPart)>();
            foreach (var weapon in game.Weapons.Where(w => w.Kind != WeaponKind.Glove))
            {
                var holder = fighters.FirstOrDefault(f => game.WeaponOf(f) == weapon);
                if (holder is null)
                {
                    continue;
                }

                var cells = WorldCells(weapon.Body);
                foreach (var victim in fighters)
                {
                    foreach (var part in victim.Voxels!.Parts)
                    {
                        var (inside, wound, materials) = Overlap(cells, part.Body, original[part.Body.Grid]);
                        anyWound |= wound > 0 && victim != holder;
                        if (inside == 0)
                        {
                            continue;
                        }

                        anyEmbedded |= victim != holder;
                        var key = (weapon, part);
                        seen.Add(key);
                        if (!open.TryGetValue(key, out var e))
                        {
                            e = new Episode { Pair = pair, Weapon = $"{(holder == game.Player ? "赤" : "青")}{weapon.Kind}", Victim = victim == holder ? "自分" : "相手", Part = part.Kind, Start = t };
                            open[key] = e;
                        }

                        e.End = t;
                        e.MaxCells = Math.Max(e.MaxCells, inside);
                        var cut = game.RecentCuts.FirstOrDefault(c => (c.A == weapon.Body && c.B == part.Body) || (c.B == weapon.Body && c.A == part.Body));
                        if (cut is null || !cut.Touched)
                        {
                            e.Untested++;
                        }
                        else
                        {
                            e.Touched++;
                            e.Stopped += cut.Stopped ? 1 : 0;
                            e.Removed += cut.RemovedFor(part.Body).Count;
                        }

                        if (!e.Materials.Contains(materials, StringComparison.Ordinal))
                        {
                            e.Materials = e.Materials.Length == 0 ? materials : $"{e.Materials},{materials}";
                        }
                    }
                }
            }

            foreach (var key in open.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                done.Add(open[key]);
                open.Remove(key);
            }

            embeddedSteps += anyEmbedded ? 1 : 0;
            woundSteps += anyWound ? 1 : 0;
        }

        done.AddRange(open.Values);
        return (done, steps, embeddedSteps, woundSteps);
    }

    private static byte[] Snapshot(VoxelGrid grid)
    {
        var (nx, ny, nz) = grid.Dimensions;
        var cells = new byte[nx * ny * nz];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = grid.MaterialAt(i);
        }

        return cells;
    }

    /// <summary>武器の埋まったセルの中心（ワールド）。</summary>
    private static List<Vector3> WorldCells(VoxelBody body)
    {
        var grid = body.Grid;
        var toWorld = body.GridToWorld();
        var (nx, ny, nz) = grid.Dimensions;
        var list = new List<Vector3>();
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (grid.At(x, y, z) != 0)
                    {
                        list.Add(Vector3.Transform(grid.CellCenter(x, y, z), toWorld));
                    }
                }
            }
        }

        return list;
    }

    private static (int Inside, int Wound, string Materials) Overlap(List<Vector3> cells, VoxelBody part, byte[] original)
    {
        var grid = part.Grid;
        int inside = 0, wound = 0;
        var materials = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var p in cells)
        {
            var i = grid.CellAt(part.WorldToGrid(p));
            if (i < 0)
            {
                continue;
            }

            var m = grid.MaterialAt(i);
            if (m != 0)
            {
                inside++;
                materials.Add(grid.Palette[m].Name);
            }
            else if (original[i] != 0)
            {
                wound++;
            }
        }

        return (inside, wound, string.Join("/", materials));
    }
}
