using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Vr;

/// <summary>
/// VR ホストの試合の設定（コマンドライン）: <c>--weapon</c>（自分の武器）・<c>--cpu-weapon</c>（相手の武器。既定は自分と同じ、<c>none</c> で素手）・
/// <c>--cpu</c>（相手のスタイル）。<c>--weapon</c> が無ければ今まで通りの素手の試合（<see cref="BoxingCorners.Default"/>）。
/// 武器の試合は既存の設定（<see cref="BoxingCorners.PlayerWeapon"/> など）をそのまま使う。棒・刀・ロングソードは両手持ち。
/// </summary>
internal sealed record VrMatchOptions(WeaponKind? PlayerWeapon, WeaponKind? CpuWeapon, string CpuStyle)
{
    /// <summary>お手本づくりの稽古（<c>--training katana</c>）の武器。null なら試合（<see cref="VrTrainingSession"/>）。</summary>
    public WeaponKind? Training { get; init; }

    /// <summary>① <c>--two-hand right</c>（既定）: 両手持ちの武器は右のコントローラが両手を動かす（左はグリップを押しているだけ）。<c>both</c>: 左右のコントローラがそれぞれの手を動かす。</summary>
    public VrTwoHand TwoHand { get; init; } = VrTwoHand.Right;

    /// <summary>② <c>--first-person</c>: カメラは HMD の姿勢のまま。体の頭を HMD の位置へ持っていく。自分の頭は目の描画に入れない。</summary>
    public bool FirstPerson { get; init; }

    public static bool TryParse(IReadOnlyList<string> args, out VrMatchOptions options, out string error)
    {
        options = new VrMatchOptions(null, null, BotCatalog.DefaultCpu);
        error = "";
        var weaponText = Value(args, "--weapon");
        var cpuWeaponText = Value(args, "--cpu-weapon");
        var cpu = Value(args, "--cpu") ?? BotCatalog.DefaultCpu;
        if (weaponText is null && cpuWeaponText is not null)
        {
            error = "--cpu-weapon needs --weapon (a weapon match needs both sides armed).";
            return false;
        }

        if (!BotCatalog.Names.Contains(cpu))
        {
            error = $"Unknown --cpu '{cpu}'. Known: {string.Join(", ", BotCatalog.Names)}.";
            return false;
        }

        WeaponKind? player = null;
        if (weaponText is not null)
        {
            if (!Enum.TryParse<WeaponKind>(weaponText, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            {
                error = $"Unknown --weapon '{weaponText}'. Known: {string.Join(", ", Enum.GetNames<WeaponKind>().Select(n => n.ToLowerInvariant()))}.";
                return false;
            }

            player = kind;
        }

        WeaponKind? cpuWeapon = player;
        if (cpuWeaponText is not null)
        {
            if (cpuWeaponText.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                cpuWeapon = null;
            }
            else if (Enum.TryParse<WeaponKind>(cpuWeaponText, ignoreCase: true, out var kind) && Enum.IsDefined(kind))
            {
                cpuWeapon = kind;
            }
            else
            {
                error = $"Unknown --cpu-weapon '{cpuWeaponText}'.";
                return false;
            }
        }

        WeaponKind? training = null;
        if (Value(args, "--training") is { } trainingText)
        {
            if (!Enum.TryParse<WeaponKind>(trainingText, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || !FormSwordsman.HasForms(kind))
            {
                error = $"Unknown --training '{trainingText}'. Weapons with forms: katana, longsword, staff, jo, ironsword, bat.";
                return false;
            }

            training = kind;
        }

        var twoHand = VrTwoHand.Right;
        if (Value(args, "--two-hand") is { } twoHandText)
        {
            if (!Enum.TryParse(twoHandText, ignoreCase: true, out twoHand) || !Enum.IsDefined(twoHand))
            {
                error = $"Unknown --two-hand '{twoHandText}'. Known: right, both.";
                return false;
            }
        }

        options = new VrMatchOptions(training ?? player, training ?? cpuWeapon, cpu)
        {
            Training = training,
            TwoHand = twoHand,
            FirstPerson = args.Contains("--first-person"),
        };
        return true;
    }

    /// <summary>この設定の両コーナー（武器の無い設定は今まで通り）。</summary>
    public BoxingCorners Corners => PlayerWeapon is null
        ? BotCatalog.Corners(CpuStyle)
        : BotCatalog.Corners(CpuStyle) with
        {
            PlayerWeapon = PlayerWeapon,
            CpuWeapon = CpuWeapon,
            PlayerTwoHanded = FormSwordsman.Handles(PlayerWeapon),
            CpuTwoHanded = FormSwordsman.Handles(CpuWeapon),
            UseWeaponBots = true,
        };

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
