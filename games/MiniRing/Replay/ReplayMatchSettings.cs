using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Replay;

/// <summary>
/// 操作元以外の、試合開始時の条件。<c>RedBotStyleJson</c>（赤がボットのとき）・<c>PlayerStyleJson</c>（人が操る赤の意図の解釈に使う）は、
/// 名簿のキャラクターなら教育を重ねた解決済みのスタイルの全体（名簿のファイルが後で変わっても同じ試合を再生するため）。青は見出しの <c>CpuStyleJson</c>。
/// <c>PlayerStyleJson</c> は版 8 から（無い記録は null: 今までの決め方）。<c>PlayerWeaponStyleJson</c>・<c>CpuWeaponStyleJson</c> は版 9 から
/// （握る武器のボットの判断の値の解決済み JSON。無い記録は null: その武器の組み込みの教科書）。
/// <c>Others</c> はバトルロイヤルの追加の席、<c>Teams</c> は席ごとのチーム（無い記録は null: 全員が敵）、<c>BreakActions</c> は休みの動作をするか（無い記録は true: 今までの既定。2026-11-25 まで書いていなかったので、休みの動作を切った試合は休みで再生がずれた）。
/// <c>PlayerFaceSpec</c>・<c>CpuFaceSpec</c> は顔の層の形（名簿のキャラの顔。顔の層は当たりの形なので、再生も同じ顔で作る）。無い記録は null: 見た目と名前から決める顔。
/// </summary>
public sealed record ReplayMatchSettings(
    WeaponKind? CpuWeapon, WeaponKind? PlayerWeapon, bool CpuTwoHanded, bool PlayerTwoHanded,
    bool VoxelGloves, HandWear PlayerWear, HandWear CpuWear, string? PlayerName,
    RingLayout? Ring, BodyBuild? PlayerBody, BodyBuild? CpuBody, FaceParts? PlayerFace, FaceParts? CpuFace,
    MatchRules? Rules, bool ExternalBlue = false, string? RedBotStyleJson = null, bool UseWeaponBots = false,
    FaceLook? PlayerLook = null, FaceLook? CpuLook = null, string? PlayerLabel = null, string? CpuLabel = null, bool Referee = true,
    string? PlayerStyleJson = null, string? PlayerWeaponStyleJson = null, string? CpuWeaponStyleJson = null, float SettleSeconds = 0f,
    FaceSpec? PlayerFaceSpec = null, FaceSpec? CpuFaceSpec = null, IReadOnlyList<ReplayEntrant>? Others = null, bool BreakActions = true, IReadOnlyList<int>? Teams = null)
{
    /// <summary>記録できるバトルロイヤルの追加の席の上限（壊れた記録で何百人も作らない）。</summary>
    public const int MaxOthers = 14;

    public static ReplayMatchSettings From(BoxingCorners corners, MatchRules? rules) => new(
        corners.CpuWeapon, corners.PlayerWeapon, corners.CpuTwoHanded, corners.PlayerTwoHanded,
        corners.VoxelGloves, corners.PlayerWear, corners.CpuWear, corners.PlayerName, corners.Ring,
        corners.PlayerBody, corners.CpuBody, corners.PlayerFace, corners.CpuFace, rules, corners.ExternalBlue, corners.RedBotStyle?.ToJson(), corners.UseWeaponBots,
        corners.PlayerLook, corners.CpuLook, corners.PlayerLabel, corners.CpuLabel, corners.Referee, corners.PlayerStyle?.ToJson(),
        corners.PlayerWeaponStyle?.ToJson(), corners.CpuWeaponStyle?.ToJson(), corners.SettleSeconds, corners.PlayerFaceSpec, corners.CpuFaceSpec,
        corners.Others is { Count: > 0 } others ? [.. others.Select(ReplayEntrant.From)] : null, corners.BreakActions, corners.Teams);

    /// <summary>画面の名前の検証: あるなら空でなく、32 文字まで、制御文字なし（壊れた記録を画面に出さない）。</summary>
    private static void ValidateLabel(string? label, string field)
    {
        if (label is not null && (label.Length is 0 or > 32 || label.Any(char.IsControl)))
        {
            throw new ArgumentException($"{field} は 1〜32 文字の制御文字なしで指定してください。", field);
        }
    }

    public BoxingCorners Apply(BoxingCorners corners)
    {
        if (!Enum.IsDefined(PlayerWear) || !Enum.IsDefined(CpuWear)
            || (CpuWeapon is { } cpu && !Enum.IsDefined(cpu))
            || (PlayerWeapon is { } player && !Enum.IsDefined(player)))
        {
            throw new ArgumentException("Invalid equipment in replay settings.");
        }
        if (!(SettleSeconds >= 0f && SettleSeconds <= 10f))
        {
            throw new ArgumentException("SettleSeconds は 0〜10 秒で指定してください。");
        }

        _ = Ring?.Validated();
        _ = PlayerBody?.Validate();
        _ = CpuBody?.Validate();
        _ = PlayerFace?.Validate();
        _ = CpuFace?.Validate();
        _ = PlayerLook?.Validate();
        _ = CpuLook?.Validate();
        _ = PlayerFaceSpec?.Validate();
        _ = CpuFaceSpec?.Validate();
        ValidateLabel(PlayerLabel, nameof(PlayerLabel));
        ValidateLabel(CpuLabel, nameof(CpuLabel));
        if (Others is { Count: > MaxOthers })
        {
            throw new ArgumentException($"バトルロイヤルの追加の席は {MaxOthers} 人まで。");
        }

        var others = Others is { Count: > 0 } list ? list.Select(e => e.ToEntrant()).ToList() : null;
        return corners with { Others = others, BreakActions = BreakActions, Teams = Teams is { Count: > 0 } t ? [.. t] : null, CpuWeapon = CpuWeapon, PlayerWeapon = PlayerWeapon, CpuTwoHanded = CpuTwoHanded,
            PlayerTwoHanded = PlayerTwoHanded, VoxelGloves = VoxelGloves, PlayerWear = PlayerWear, CpuWear = CpuWear,
            PlayerName = PlayerName, Ring = Ring, PlayerBody = PlayerBody, CpuBody = CpuBody,
            PlayerFace = PlayerFace, CpuFace = CpuFace, ExternalBlue = ExternalBlue,
            PlayerLook = PlayerLook, CpuLook = CpuLook, PlayerLabel = PlayerLabel, CpuLabel = CpuLabel, Referee = Referee,
            UseWeaponBots = UseWeaponBots, RedBotStyle = RedBotStyleJson is { } json ? Bots.BotStyle.FromJson(json) : null,
            PlayerStyle = PlayerStyleJson is { } playerJson ? Bots.BotStyle.FromJson(playerJson) : null,
            PlayerWeaponStyle = PlayerWeaponStyleJson is { } playerWeapon ? Bots.WeaponStyle.FromJson(playerWeapon) : null,
            CpuWeaponStyle = CpuWeaponStyleJson is { } cpuWeapon ? Bots.WeaponStyle.FromJson(cpuWeapon) : null,
            SettleSeconds = SettleSeconds, PlayerFaceSpec = PlayerFaceSpec, CpuFaceSpec = CpuFaceSpec };
    }
}
