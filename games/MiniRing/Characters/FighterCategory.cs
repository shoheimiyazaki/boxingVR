using System.Globalization;
using MiniRing.Weapons;

namespace MiniRing.Characters;

/// <summary>
/// 選ぶ画面の分け方（2026-11-26）。名簿のファイルの値（体の形・持ち物・教科書のスタイルの値）だけから決め、キャラ名・教科書の名前では分けない（CLAUDE.md §1.8）。
/// 並びは画面の並び（素手の打撃 → 蹴り → 組み → 武器 → 人間でない体）。
/// </summary>
public enum FighterCategory
{
    /// <summary>拳で打つ（グローブか素手で、蹴らない・組まない教科書）。</summary>
    Boxer,

    /// <summary>蹴りも使う打撃（教科書のスタイルが蹴る: <see cref="Bots.BotStyle.KickRate"/> &gt; 0）。空手・テコンドー・ムエタイなど。</summary>
    Kicker,

    /// <summary>組み技（教科書のスタイルが組みの技表を持つ: <see cref="Bots.BotStyle.Grapple"/>）。</summary>
    Grappler,

    /// <summary>武器を握るか、グローブ・素手でない物（鉄・刃・木）を手にはめる。</summary>
    Armed,

    /// <summary>人間の体の形でない（四つ足・鎌の形の骨の手）。</summary>
    Creature,
}

/// <summary>分け方の決め方と表示の名前。</summary>
public static class FighterCategories
{
    /// <summary>画面の並び。</summary>
    public static IReadOnlyList<FighterCategory> Order { get; } = Enum.GetValues<FighterCategory>();

    /// <summary>
    /// このキャラの分け方: 体の形 → 手の物 → 教科書のスタイルの値（組みの技表・蹴る割合）の順に見る。
    /// 先の方が見た目で分かる違い（四つ足・刃）なので先に見る。
    /// </summary>
    public static FighterCategory Of(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.Build.FourLegged || character.Wear == HandWear.SickleClaw)
        {
            return FighterCategory.Creature;
        }

        if (character.Weapon is not null || character.Wear is not (HandWear.Glove or HandWear.None))
        {
            return FighterCategory.Armed;
        }

        var style = character.Style;
        return style.Grapple is not null ? FighterCategory.Grappler
            : style.KickRate > 0f ? FighterCategory.Kicker
            : FighterCategory.Boxer;
    }

    /// <summary>見出しの名前。</summary>
    public static string Label(this FighterCategory category) => category switch
    {
        FighterCategory.Boxer => "ボクサー",
        FighterCategory.Kicker => "蹴りも使う打撃（空手・キック）",
        FighterCategory.Grappler => "組み技",
        FighterCategory.Armed => "武器を持つ",
        FighterCategory.Creature => "人間でない体",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "知らない分け方です。"),
    };

    /// <summary>札に収まる短い名前。</summary>
    public static string ShortLabel(this FighterCategory category) => category switch
    {
        FighterCategory.Boxer => "ボクサー",
        FighterCategory.Kicker => "打撃（蹴り）",
        FighterCategory.Grappler => "組み技",
        FighterCategory.Armed => "武器",
        FighterCategory.Creature => "人間以外",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "知らない分け方です。"),
    };
}

/// <summary>
/// ボクシングの慣習の単位（身長はフィートとインチ・センチの併記、体重はポンド）。値は見えている体から（<see cref="BodySizing"/>、試合の体と同じ）。
/// </summary>
public readonly record struct FighterTape(int HeightCm, int Feet, int Inches, int WeightKg, int WeightLb)
{
    /// <summary>1 インチ [cm]。</summary>
    public const float CmPerInch = 2.54f;

    /// <summary>1 ポンド [kg]（国際ポンド）。</summary>
    public const float KgPerPound = 0.45359237f;

    public static FighterTape Of(BodyBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var meters = BodySizing.PhysicalHeightMeters(build);
        var kg = BodySizing.PhysicalMassKg(build);
        var totalInches = (int)MathF.Round(meters * 100f / CmPerInch, MidpointRounding.AwayFromZero);
        return new FighterTape(CharacterProfile.HeightOf(build), totalInches / 12, totalInches % 12, CharacterProfile.WeightOf(build),
            (int)MathF.Round(kg / KgPerPound, MidpointRounding.AwayFromZero));
    }

    /// <summary>「5'11"（180 cm）」。</summary>
    public string HeightText => string.Create(CultureInfo.InvariantCulture, $"{Feet}'{Inches}\"（{HeightCm} cm）");

    /// <summary>「176 lb（80 kg）」。</summary>
    public string WeightText => string.Create(CultureInfo.InvariantCulture, $"{WeightLb} lb（{WeightKg} kg）");
}
