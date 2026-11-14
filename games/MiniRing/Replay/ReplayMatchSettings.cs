using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Replay;

/// <summary>操作元以外の、試合開始時の条件。</summary>
public sealed record ReplayMatchSettings(
    WeaponKind? CpuWeapon, WeaponKind? PlayerWeapon, bool CpuTwoHanded, bool PlayerTwoHanded,
    bool VoxelGloves, HandWear PlayerWear, HandWear CpuWear, string? PlayerName,
    RingLayout? Ring, BodyBuild? PlayerBody, BodyBuild? CpuBody, FaceParts? PlayerFace, FaceParts? CpuFace,
    MatchRules? Rules)
{
    public static ReplayMatchSettings From(BoxingCorners corners, MatchRules? rules) => new(
        corners.CpuWeapon, corners.PlayerWeapon, corners.CpuTwoHanded, corners.PlayerTwoHanded,
        corners.VoxelGloves, corners.PlayerWear, corners.CpuWear, corners.PlayerName, corners.Ring,
        corners.PlayerBody, corners.CpuBody, corners.PlayerFace, corners.CpuFace, rules);

    public BoxingCorners Apply(BoxingCorners corners)
    {
        if (!Enum.IsDefined(PlayerWear) || !Enum.IsDefined(CpuWear)
            || (CpuWeapon is { } cpu && !Enum.IsDefined(cpu))
            || (PlayerWeapon is { } player && !Enum.IsDefined(player)))
        {
            throw new ArgumentException("Invalid equipment in replay settings.");
        }
        _ = Ring?.Validated();
        _ = PlayerBody?.Validate();
        _ = CpuBody?.Validate();
        _ = PlayerFace?.Validate();
        _ = CpuFace?.Validate();
        return corners with { CpuWeapon = CpuWeapon, PlayerWeapon = PlayerWeapon, CpuTwoHanded = CpuTwoHanded,
            PlayerTwoHanded = PlayerTwoHanded, VoxelGloves = VoxelGloves, PlayerWear = PlayerWear, CpuWear = CpuWear,
            PlayerName = PlayerName, Ring = Ring, PlayerBody = PlayerBody, CpuBody = CpuBody,
            PlayerFace = PlayerFace, CpuFace = CpuFace };
    }
}
