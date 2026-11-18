using System.Numerics;
using Aigis.Platform;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;
using Silk.NET.Input;

namespace MiniRing.Desktop;

internal enum MatchMode { Single, Versus, Spectate }

/// <summary>
/// メニューで決めた試合の中身: 赤（1P）と青（2P・CPU）のキャラクターと、レフェリーの有無（設定）。
/// 武器・はめる物・体格・顔・名前はキャラクターの値で固定（選ぶ行は無い）。ボットの性格はキャラクターのスタイル
/// （教科書に教育を重ねた解決済みのもの。相手ごとの備え <c>vs</c> はこの試合の相手で引く。<see cref="FighterCharacter.Resolve"/>）。
/// 解決したスタイルは、青のボット（<see cref="BoxingCorners.CpuStyleJson"/>）・赤のボット（<see cref="BoxingCorners.RedBotStyle"/>）・
/// 人が操る側の意図の解釈（<see cref="BoxingCorners.PlayerStyle"/>・<see cref="BoxingCorners.CpuStyle"/>）・記録の見出しの全部に同じ物が通る。
/// </summary>
internal sealed record MatchSelection(MatchMode Mode, FighterCharacter Red, FighterCharacter Blue, bool Referee = true)
{
    public BoxingCorners Corners => Build(Red.Resolve(Blue.Id), Blue.Resolve(Red.Id));

    private BoxingCorners Build(BotStyle redStyle, BotStyle blueStyle) => BotCatalog.Corners(blueStyle) with
    {
        RedBotStyle = Mode == MatchMode.Spectate ? redStyle : null,
        PlayerStyle = redStyle,
        ExternalBlue = Mode == MatchMode.Versus,
        // 名前はスタイルの名前（空手の判定 LocalVersusControl.IsKarate がスタイルで見る）。顔・画面の名前はキャラクターの値。
        PlayerName = Red.StyleName,
        PlayerBody = Red.Build,
        PlayerFace = Red.Look.Parts,
        PlayerLook = Red.Look,
        PlayerLabel = Red.HudName,
        PlayerWear = Red.Wear,
        PlayerWeapon = Red.Weapon,
        PlayerWeaponStyle = Red.WeaponStyle,
        CpuBody = Blue.Build,
        CpuFace = Blue.Look.Parts,
        CpuLook = Blue.Look,
        CpuLabel = Blue.HudName,
        CpuWeapon = Blue.Weapon,
        CpuWeaponStyle = Blue.WeaponStyle,
        PlayerTwoHanded = TwoHanded(Red.Weapon),
        CpuTwoHanded = TwoHanded(Blue.Weapon),
        UseWeaponBots = true,
        CpuWear = Blue.Wear,
        Referee = Referee,
    };

    private static bool TwoHanded(WeaponKind? weapon) => FormSwordsman.Handles(weapon);
}

internal sealed class LocalVersusControl(BoxingCorners corners)
{
    private DesktopFighterControl _red = RedControl(corners);
    private DesktopFighterControl _blue = BlueControl(corners);
    private int _round;

    public BoxingInput Read(InputSnapshot input, BoxingGame game, float dt, float yaw)
        => Read(input, game.PlayerSense, game.CpuSense, game.Round, dt, yaw);

    public BoxingInput Read(InputSnapshot input, FighterSense player, FighterSense cpu, int round, float dt, float yaw)
    {
        var reset = input.WasKeyPressed(Key.R);
        if (reset || _round != round)
        {
            _round = round;
            _red = RedControl(corners);
            _blue = BlueControl(corners);
        }

        var red = ReadSide(input, 0, yaw, Key.W, Key.A, Key.S, Key.D, Key.F, Key.G, Key.H);
        var blue = ReadSide(input, 1, yaw, Key.Up, Key.Left, Key.Down, Key.Right, Key.J, Key.K, Key.L);
        return new BoxingInput(_red.Read(red, player, dt), reset, _blue.Read(blue, cpu, dt));
    }

    internal static DesktopFighterControl RedControl(BoxingCorners corners) =>
        new(corners.PlayerWeapon, IsKarate(corners.PlayerName, corners.PlayerWear), corners.PlayerStyle);

    internal static DesktopFighterControl BlueControl(BoxingCorners corners) =>
        new(corners.CpuWeapon, IsKarate(corners.CpuName, corners.CpuWear), corners.CpuStyle);

    /// <summary>空手の構えか。<paramref name="styleName"/> はボットのスタイルの名前（キャラクターの <see cref="FighterCharacter.StyleName"/>。表示名ではない）。</summary>
    internal static bool IsKarate(string? styleName, HandWear wear) => styleName == BotCatalog.Karate.Name && wear == HandWear.None;

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
            world.LengthSquared() > 1f ? Vector2.Normalize(world) : world, Step: input.IsGamepadButtonDown(pad, GamepadButton.East));
    }
}
