using System.Numerics;
using Aigis.Platform;
using MiniRing.Bots;
using MiniRing.Weapons;
using Silk.NET.Input;

namespace MiniRing.Desktop;

internal enum MatchMode { Single, Versus, Spectate }

internal sealed record MatchSelection(MatchMode Mode, BotStyle Red, BotStyle Blue, HandWear RedWear, HandWear BlueWear,
    WeaponKind? RedWeapon = null, WeaponKind? BlueWeapon = null)
{
    public BoxingCorners Corners => BotCatalog.Corners(Blue) with
    {
        RedBotStyle = Mode == MatchMode.Spectate ? Red : null,
        ExternalBlue = Mode == MatchMode.Versus,
        PlayerName = Red.Name,
        PlayerBody = Red.Build,
        PlayerFace = Red.Face,
        PlayerWear = RedWear,
        PlayerWeapon = RedWeapon,
        CpuWeapon = BlueWeapon,
        PlayerTwoHanded = TwoHanded(RedWeapon),
        CpuTwoHanded = TwoHanded(BlueWeapon),
        UseWeaponBots = true,
        CpuWear = BlueWear,
    };

    private static bool TwoHanded(WeaponKind? weapon) => weapon is WeaponKind.Katana or WeaponKind.Longsword;
}

internal sealed class LocalVersusControl(BoxingCorners corners)
{
    private DesktopFighterControl _red = RedControl(corners);
    private DesktopFighterControl _blue = BlueControl(corners);
    private int _round;

    public BoxingInput Read(InputSnapshot input, BoxingGame game, float dt, float yaw)
    {
        var reset = input.WasKeyPressed(Key.R);
        if (reset || _round != game.Round)
        {
            _round = game.Round;
            _red = RedControl(corners);
            _blue = BlueControl(corners);
        }

        var red = ReadSide(input, 0, yaw, Key.W, Key.A, Key.S, Key.D, Key.F, Key.G, Key.H);
        var blue = ReadSide(input, 1, yaw, Key.Up, Key.Left, Key.Down, Key.Right, Key.J, Key.K, Key.L);
        return new BoxingInput(_red.Read(red, game.PlayerSense, dt), reset, _blue.Read(blue, game.CpuSense, dt));
    }

    private static DesktopFighterControl RedControl(BoxingCorners corners) =>
        new(corners.PlayerWeapon, IsKarate(corners.PlayerName, corners.PlayerWear));

    private static DesktopFighterControl BlueControl(BoxingCorners corners) =>
        new(corners.CpuWeapon, IsKarate(corners.CpuName, corners.CpuWear));

    internal static bool IsKarate(string? name, HandWear wear) => name == BotCatalog.Karate.Name && wear == HandWear.None;

    private static FighterIntent ReadSide(InputSnapshot input, int pad, float yaw,
        Key up, Key left, Key down, Key right, Key light, Key heavy, Key guard)
    {
        float Axis(Key positive, Key negative) => (input.IsKeyDown(positive) ? 1f : 0f)
            - (input.IsKeyDown(negative) ? 1f : 0f);
        var movement = new Vector2(Axis(right, left), Axis(up, down));
        var stick = GamepadIntent.Map(input.GetGamepadLeftStick(pad),
            input.IsGamepadButtonDown(pad, GamepadButton.West), input.IsGamepadButtonDown(pad, GamepadButton.North),
            input.IsGamepadButtonDown(pad, GamepadButton.South), yaw);
        var world = CameraFollow.ToWorld(movement, yaw) + stick.World.GetValueOrDefault();
        return new FighterIntent(Vector2.Zero, input.IsKeyDown(light) || stick.Light,
            input.IsKeyDown(heavy) || stick.Heavy, input.IsKeyDown(guard) || stick.Evade,
            world.LengthSquared() > 1f ? Vector2.Normalize(world) : world);
    }
}
