using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing;

/// <summary>
/// バトルロイヤルの追加の席（3 人目から）の中身: 赤・青の席と同じ項目（名前・画面の名前・解決済みのスタイル・武器・はめる物・体格・顔）。
/// 追加の席はいつもボット（武器を握るならその武器のボット、無ければ <see cref="BoxerBot"/>）。docs/plans/VFC_ROYALE_PLAN.md。
/// </summary>
/// <param name="Name">ボットの名前（顔を名前から決めるとき・記録の見出し）。</param>
/// <param name="Label">画面に出す名前。</param>
/// <param name="Style">解決済みのスタイル（名簿のキャラなら教育を重ねた全体）。狙いの選び方 <see cref="BotStyle.Rival"/> もここから。</param>
/// <param name="Weapon">握る武器（無ければ null）。</param>
/// <param name="TwoHanded">両手で握るか。</param>
/// <param name="Wear">武器を握っていない手にはめる物。</param>
/// <param name="WeaponStyle">武器のボットの判断の値（教科書に教育を重ねた物。null ならその武器の教科書）。</param>
/// <param name="Body">体格（null なら基準）。</param>
/// <param name="Look">顔の見た目（null なら名前から）。</param>
/// <param name="Face">顔の層の形（null なら見た目と名前から）。</param>
public sealed record RoyaleEntrant(string Name, string Label, BotStyle Style,
    WeaponKind? Weapon = null, bool TwoHanded = false, HandWear Wear = HandWear.Glove,
    WeaponStyle? WeaponStyle = null, BodyBuild? Body = null, FaceLook? Look = null, FaceSpec? Face = null)
{
    /// <summary>名簿のキャラクターから（相手を決めない教育まで: 相手が何人もいるので <see cref="FighterCharacter.Style"/>）。</summary>
    public static RoyaleEntrant From(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return new RoyaleEntrant(character.StyleName, character.HudName, character.Style, character.Weapon, FormSwordsman.Handles(character.Weapon),
            character.Wear, character.WeaponStyle, character.Build, character.Look, character.Face);
    }
}

/// <summary>バトルロイヤルの席の組み立て。</summary>
public static class RoyaleCorners
{
    /// <summary>
    /// 名簿のキャラクター <paramref name="characters"/>（バトルロイヤルは 3 人以上。2 人なら追加の席の無い試合）の、レフェリーなしの試合。1 人目が赤の席、2 人目が青の席、残りが追加の席。
    /// 全員ボットで、武器はその武器のボットが握る。場は <paramref name="ring"/>（null なら <see cref="RingLayout.RoyaleCage"/>）。
    /// </summary>
    public static BoxingCorners From(IReadOnlyList<FighterCharacter> characters, RingLayout? ring = null)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (characters.Count < 2)
        {
            throw new ArgumentException("2 人以上（2 人なら追加の席の無い普通の試合: 計算の重さの比べもの用）。", nameof(characters));
        }

        var seats = characters.Select(RoyaleEntrant.From).ToList();
        return From(seats, ring);
    }

    /// <summary>席の中身 <paramref name="seats"/>（2 人以上）から。1 人目が赤、2 人目が青、残りが追加の席。</summary>
    public static BoxingCorners From(IReadOnlyList<RoyaleEntrant> seats, RingLayout? ring = null)
    {
        ArgumentNullException.ThrowIfNull(seats);
        if (seats.Count < 2)
        {
            throw new ArgumentException("2 人以上（2 人なら追加の席の無い普通の試合: 計算の重さの比べもの用）。", nameof(seats));
        }

        var red = seats[0];
        var blue = seats[1];
        return BotCatalog.Corners(blue.Style) with
        {
            RedBotStyle = red.Style,
            PlayerStyle = red.Style,
            PlayerName = red.Name,
            PlayerLabel = red.Label,
            PlayerBody = red.Body,
            PlayerFace = red.Look?.Parts,
            PlayerLook = red.Look,
            PlayerFaceSpec = red.Face,
            PlayerWeapon = red.Weapon,
            PlayerTwoHanded = red.TwoHanded,
            PlayerWear = red.Wear,
            PlayerWeaponStyle = red.WeaponStyle,
            CpuLabel = blue.Label,
            CpuBody = blue.Body,
            CpuFace = blue.Look?.Parts,
            CpuLook = blue.Look,
            CpuFaceSpec = blue.Face,
            CpuWeapon = blue.Weapon,
            CpuTwoHanded = blue.TwoHanded,
            CpuWear = blue.Wear,
            CpuWeaponStyle = blue.WeaponStyle,
            UseWeaponBots = true,
            Referee = false,
            Ring = (ring ?? RingLayout.RoyaleCage).AtCorners(),
            Others = seats.Skip(2).ToList(),
        };
    }
}
