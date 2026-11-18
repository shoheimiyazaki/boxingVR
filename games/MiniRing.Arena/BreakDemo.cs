using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 台本つきの場面（breakdemo）: 骨が断たれる・折れる・武器が折れるときの体と武器の振る舞いを、決まった瞬間に起こして見る。
/// 起こすのは「切れた・折れた」という結果だけで、その後の動き（落ちる・ぶら下がる・握りが抜ける・捨てて素手で打つ）はすべて物理とボット。
/// </summary>
internal static class BreakDemo
{
    /// <summary>1 つの場面: 名前、試合の作り方、起こす瞬間 [s]、起こすこと、画面の説明。</summary>
    internal sealed record Scene(string Title, Func<BoxingGame> Create, float At, Action<BoxingGame> Act, Func<BoxingGame, bool, string> Describe, Func<BoxingGame, Vector3>? CloseUp = null);

    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    private static FighterCommand Guard(in FighterSense s, SwingFamily? weapon)
    {
        var face = s.OpponentChest - s.Self.Origin;
        face.Y = 0f;
        var reach = s.ArmReach;
        var left = new HandCommand(new Vector3(0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        var right = weapon is { } family ? WeaponSwing.GuardCommand(reach, family) : new HandCommand(new Vector3(-0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), left, right);
    }

    private static BoxingGame TwoHandedBat() =>
        new(seed: 1, corners: new BoxingCorners("bat", _ => new Scripted(s => Guard(s, SwingFamily.Bat)), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub, CpuTwoHanded: true));

    /// <summary>部位 <paramref name="part"/> の格子のスライスを消し、骨が切れたら試合に伝える。</summary>
    private static void Cut(BoxingGame game, Fighter fighter, int part, IEnumerable<int> slices)
    {
        var body = fighter.Voxels!;
        var grid = body.PartOf(part)!.Body.Grid;
        var removed = new List<RemovedVoxel>();
        foreach (var slice in slices)
        {
            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid.Coordinates(i).X == slice && grid.MaterialAt(i) != 0)
                {
                    grid.Break(i, removed);
                }
            }
        }

        if (body.BreakIfSevered(part) is { } broken)
        {
            game.BreakBone(fighter, broken, 1f);
        }
    }

    /// <summary>脚の部位 <paramref name="part"/> を、体に近い端から長さの <paramref name="fromNear"/> の所で、骨の軸に沿った 1 枚のスライスごと断つ。</summary>
    private static void CutLimb(BoxingGame game, Fighter fighter, int part, float fromNear)
    {
        var voxel = fighter.Voxels!.PartOf(part)!;
        var grid = voxel.Body.Grid;
        var axis = voxel.Body.BeamAxis ?? 1;
        var n = axis == 0 ? grid.Dimensions.X : axis == 1 ? grid.Dimensions.Y : grid.Dimensions.Z;
        var k = Math.Clamp((int)MathF.Round(fromNear * (n - 1)), 1, n - 2);
        var slice = voxel.DistalHigh ? k : n - 1 - k;
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            if ((axis == 0 ? x : axis == 1 ? y : z) == slice && grid.MaterialAt(i) != 0)
            {
                grid.Break(i, removed);
            }
        }

        if (fighter.Voxels.BreakIfSevered(part) is { } broken)
        {
            game.BreakBone(fighter, broken, 1f);
        }
    }

    /// <summary>ボット同士の打ち合い。<paramref name="shortcut"/> が false なら、脚を失った体も支えのモデルを通る（<see cref="FighterTuning.LegLossSupportShortcut"/>）。</summary>
    private static BoxingGame Bout(bool shortcut) =>
        new(tuning: FighterTuning.Default with { LegLossSupportShortcut = shortcut }, seed: 3,
            corners: new BoxingCorners("bout", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s)));

    /// <summary>脚の部位 <paramref name="part"/> の骨を真ん中で折る（肉でつながったまま）。</summary>
    private static void BreakLimb(BoxingGame game, Fighter fighter, int part)
    {
        var voxel = fighter.Voxels!.PartOf(part)!;
        var axis = voxel.Body.BeamAxis ?? 1;
        var dims = voxel.Body.Grid.Dimensions;
        var n = axis == 0 ? dims.X : axis == 1 ? dims.Y : dims.Z;
        if (fighter.Voxels.BreakBent(part, new Aigis.Physics.Voxels.VoxelBreak(axis, n / 2, true, 2f)) is { } broken)
        {
            game.BreakBone(fighter, broken, 1f);
        }
    }

    /// <summary>脚を失う場面を、近道あり（今の既定）と近道なし（支えのモデルをそのまま通す）の 2 本にする。</summary>
    private static IEnumerable<Scene> LegScenes(string title, Action<BoxingGame> act, string after)
    {
        foreach (var shortcut in new[] { true, false })
        {
            var label = shortcut ? "近道あり" : "近道なし";
            yield return new Scene($"{title}（{label}）", () => Bout(shortcut), 2f, act,
                (game, done) => !done ? $"打ち合う（{label}）" : $"{after}（{label}: {Stance(game.Cpu)}）");
        }
    }

    private static string Stance(Fighter f) =>
        $"{f.State}  腰 {f.HoverHeight * 100f:F0} cm  失った脚 {f.LegShortfall * 100f:F0} cm";

    public static IReadOnlyList<Scene> Scenes() =>
    [
        .. LegScenes("すねを断つ", game => CutLimb(game, game.Cpu, game.Cpu.Parts.LeftShin, 0.4f), "すねの切り株"),
        .. LegScenes("片方の腿を断つ", game => CutLimb(game, game.Cpu, game.Cpu.Parts.LeftThigh, 0.5f), "腿の切り株"),
        .. LegScenes("両脚を腿の付け根近くで断つ", game =>
            {
                CutLimb(game, game.Cpu, game.Cpu.Parts.LeftThigh, 0.25f);
                CutLimb(game, game.Cpu, game.Cpu.Parts.RightThigh, 0.25f);
            }, "腰で座る"),
        .. LegScenes("すねの骨が折れる", game => BreakLimb(game, game.Cpu, game.Cpu.Parts.LeftShin), "折れたすねが肉でぶら下がる"),
        new("両手の棍棒: 添えた左腕を切り落とす", TwoHandedBat, 1.5f,
            game =>
            {
                var upper = game.Cpu.UpperArmId(BoxingGame.SupportHand);
                Cut(game, game.Cpu, upper, [game.Cpu.Voxels!.PartOf(upper)!.Body.Grid.Dimensions.X / 2]);
            },
            (game, after) => !after ? "両手で握る" : game.Cpu.DeadGrips > 0 ? "切られた左手は、力が抜けるまで握ったまま" : "左手が抜けた（右手の片手持ち）"),
        new("右肘の肉を斬り取る（骨の端より先）", TwoHandedBat, 1.5f,
            game =>
            {
                var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
                var part = game.Cpu.Voxels!.PartOf(upper)!;
                var grid = part.Body.Grid;
                var n = grid.Dimensions.X;
                bool HasBone(int s) => Enumerable.Range(0, grid.CellCount).Any(i => grid.Coordinates(i).X == s && grid.MaterialAt(i) != 0 && grid.Palette[grid.MaterialAt(i)].Tissue == VoxelTissue.Bone);
                var beyond = part.DistalHigh ? Enumerable.Range(0, n).Last(HasBone) + 1 : Enumerable.Range(0, n).First(HasBone) - 1;
                Cut(game, game.Cpu, upper, [beyond]);
            },
            (game, after) => !after ? "両手で握る" : game.Cpu.DeadGrips > 0 ? "肘から先が外れた（右手はまだ握っている）" : "肘から先が落ちた"),
        new("棍棒が握りのすぐ先で折れる: 捨てて素手で戦う", () => new BoxingGame(seed: 1, corners: new BoxingCorners("club",
                s => new DisarmedFallback(new Scripted(x => Guard(x, SwingFamily.Chop)), BotCatalog.Create(DisarmedFallback.FistStyle, s)), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub)), 1.5f,
            game =>
            {
                var club = game.WeaponOf(game.Cpu)!;
                var grid = club.Body.Grid;
                var grip = game.Cpu.WieldedGrip(BoxingGame.WeaponHand)!.Value.X;
                var slice = (int)MathF.Floor((grip + 0.01f - club.Body.GridCenterLocal.X + grid.HalfExtents.X) / grid.CellSize.X);
                if (club.Body.SplitAt(0, slice, takeHigh: true) is { } piece)
                {
                    game.World.SetLinearVelocity(piece.BodyId, new Vector3(0f, 0.5f, 0f));
                }
            },
            (game, after) => !after ? "棍棒で構える" : game.Cpu.Wields(BoxingGame.WeaponHand) ? "柄だけになった" : "柄を捨てて、素手の拳で打つ"),
        new("左の前腕の骨が折れる（肉でぶら下がる）", () => new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard())), 1.5f,
            game =>
            {
                var forearm = game.Cpu.ForearmId(Hand.Left);
                var n = game.Cpu.Voxels!.PartOf(forearm)!.Body.Grid.Dimensions.X;
                if (game.Cpu.Voxels!.BreakBent(forearm, new Aigis.Physics.Voxels.VoxelBreak(0, n / 2, true, 2f)) is { } broken)
                {
                    game.BreakBone(game.Cpu, broken, 1f);
                }
            },
            (game, after) => !after ? "構える" : game.Cpu.Fractures > 0 ? "折れた先が肉でぶら下がる（駆動の無い弱いバネ）" : "肉が千切れて落ちた"),
        new("首の骨が折れる（頭が肉でぶら下がる）", () => new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard())), 1.5f,
            game =>
            {
                // 折れた瞬間に、頭を前へ倒す打撃（頭への回転の力積）が入ったことにする。折れたあとの動き（垂れる・揺れる）は肉のバネと重さだけ。
                var head = game.Cpu.Parts.Head;
                if (game.Cpu.Voxels!.BreakBent(head, new Aigis.Physics.Voxels.VoxelBreak(1, MiniRing.Voxels.BoxerSpine.NeckSlices.High, false, 2f)) is { } broken)
                {
                    game.BreakBone(game.Cpu, broken, 1f);
                    game.World.ApplyAngularImpulse(head, Vector3.Transform(Vector3.UnitX, game.World.GetState(game.Cpu.Parts.Chest).Orientation) * 0.1f);
                }
            },
            (game, after) => !after ? "構える" : game.Cpu.IsDead ? "頭が離れて死んだ" : game.Cpu.HangsByFlesh(game.Cpu.Parts.Head) ? "首が折れた: 頭が肉でぶら下がる（首の筋は外れ、肉の断面に比例する弱いバネ）" : "肉が千切れた",
            game => game.World.GetState(game.Cpu.Parts.Chest).Position + new Vector3(0f, 0.1f, 0f)),
        new("首を斬る（頭が離れて落ち、その体は死ぬ）", () => new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard())), 1.5f,
            game =>
            {
                // 首の段（皮膚・筋肉・骨・脊髄のすべて）を斬り取った結果だけを起こす。頭が離れて落ちる・死ぬ・試合が終わるのは物理とゲームの決まり。
                var head = game.Cpu.Parts.Head;
                var body = game.Cpu.Voxels!;
                var grid = body.PartOf(head)!.Body.Grid;
                var removed = new List<RemovedVoxel>();
                for (var i = 0; i < grid.CellCount; i++)
                {
                    if (grid.Coordinates(i).Y == MiniRing.Voxels.BoxerSpine.NeckSlices.High && grid.MaterialAt(i) != 0)
                    {
                        grid.Break(i, removed);
                    }
                }

                if (body.BreakIfSevered(head) is { } broken)
                {
                    game.BreakBone(game.Cpu, broken, 1f);
                }
            },
            (game, after) => !after ? "構える" : game.Cpu.IsDead ? $"首が断たれた: 頭が離れて落ちる、体は死ぬ（{game.Cpu.DeathCause}）" : "首は繋がっている",
            game => game.World.GetState(game.Cpu.Parts.Chest).Position + new Vector3(0f, 0.1f, 0f)),
    ];
}
