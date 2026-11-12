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
    internal sealed record Scene(string Title, Func<BoxingGame> Create, float At, Action<BoxingGame> Act, Func<BoxingGame, bool, string> Describe);

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

    public static IReadOnlyList<Scene> Scenes() =>
    [
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
    ];
}
