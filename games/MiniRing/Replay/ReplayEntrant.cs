using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Replay;

/// <summary>
/// バトルロイヤルの追加の席（<see cref="RoyaleEntrant"/>）の記録の形: スタイルは解決済みの JSON のまま持つ（名簿のファイルが後で変わっても同じ試合を再生するため）。
/// </summary>
public sealed record ReplayEntrant(string Name, string Label, string StyleJson, WeaponKind? Weapon = null, bool TwoHanded = false,
    HandWear Wear = HandWear.Glove, string? WeaponStyleJson = null, BodyBuild? Body = null, FaceLook? Look = null, FaceSpec? Face = null)
{
    public static ReplayEntrant From(RoyaleEntrant entrant)
    {
        ArgumentNullException.ThrowIfNull(entrant);
        return new ReplayEntrant(entrant.Name, entrant.Label, entrant.Style.ToJson(), entrant.Weapon, entrant.TwoHanded, entrant.Wear,
            entrant.WeaponStyle?.ToJson(), entrant.Body, entrant.Look, entrant.Face);
    }

    /// <summary>記録の値を検査して席の中身へ戻す（壊れた記録は例外）。</summary>
    public RoyaleEntrant ToEntrant()
    {
        if (!Enum.IsDefined(Wear) || (Weapon is { } weapon && !Enum.IsDefined(weapon)))
        {
            throw new ArgumentException("Invalid equipment in replay entrant.");
        }

        if (string.IsNullOrEmpty(Name) || Name.Length > 64 || Name.Any(char.IsControl) || Label is null || Label.Length is 0 or > 32 || Label.Any(char.IsControl))
        {
            throw new ArgumentException("記録の席の名前は 1〜32 文字（ボットの名前は 64 文字まで）の制御文字なしで指定してください。");
        }

        _ = Body?.Validate();
        _ = Look?.Validate();
        _ = Face?.Validate();
        return new RoyaleEntrant(Name, Label, Bots.BotStyle.FromJson(StyleJson), Weapon, TwoHanded, Wear,
            WeaponStyleJson is { } json ? Bots.WeaponStyle.FromJson(json) : null, Body, Look, Face);
    }
}
