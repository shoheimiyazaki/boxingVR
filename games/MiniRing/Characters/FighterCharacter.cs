using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Characters;

/// <summary>国旗の図形の種類（回転できない図形で近いものだけ: 丸と四角）。</summary>
public enum FlagShape { Disc, Rect }

/// <summary>国旗の上に重ねる 1 つの図形（旗の中の正規化座標 0..1。中心と大きさ）。</summary>
public sealed record FlagMark(FlagShape Shape, Vector2 Center, Vector2 Size, Vector3 Color);

/// <summary>
/// 簡略の国旗: 色の帯（縦または横）に、丸と四角を重ねたもの。メニューが図形で描く（外部の画像を持ち込まない）。
/// 本物の旗の細部（紋章・星・斜めの十字）は描かず、国を見分けられる程度に近づける。
/// </summary>
public sealed record FlagSpec(bool VerticalStripes, IReadOnlyList<Vector3> Stripes, IReadOnlyList<FlagMark> Marks);

/// <summary>国（表示名と旗）。名簿の <c>countries</c> 表の 1 行。</summary>
public sealed record CountryInfo(string Id, string Name, FlagSpec Flag);

/// <summary>
/// 名簿の 1 人（キャラクター）。国籍・武器・肌の色・身長と体格・名前を 1 か所にまとめる。全員が架空の人物で、
/// 実在の人はモデルにしない（docs/CHARACTERS.md）。強さは見えている体（<see cref="Build"/>）から物理で出る（CLAUDE.md §1.7）ので、
/// ここに能力値は持たない。ボットの性格は教科書（<see cref="StyleName"/>、<see cref="BotCatalog"/> のスタイル）に教育（<see cref="Education"/>、差分だけ）を重ねた<see cref="Resolve(string)"/>で、入力元の違いにすぎない。
/// </summary>
/// <param name="Id">短い識別子（小文字・数字・-・_）。</param>
/// <param name="Name">表示名（フルネーム）。</param>
/// <param name="Reading">読み（カタカナ）。</param>
/// <param name="HudName">試合の画面（得点・状態）に出る短い名前。</param>
/// <param name="Origin">出身の表示（例: 日本（沖縄））。</param>
/// <param name="Country">国（旗）。</param>
/// <param name="Wear">武器を握っていない手にはめる物（素手・グローブなど）。</param>
/// <param name="Weapon">握る武器。null なら持たない。</param>
/// <param name="StyleName">教科書: ボットのスタイルの名前（<see cref="BotCatalog.Styles"/>）。ファイルでは <c>textbook</c>。</param>
/// <param name="Build">体格（身長・腕・脚・筋肉の基準に対する比）。</param>
/// <param name="Look">顔（肌・髪・瞳・髪型・眉・ひげ・顔の部位）。</param>
/// <param name="Tagline">一言。</param>
/// <param name="Education">教育（教科書からの差分。空なら教科書そのもの）。</param>
/// <param name="Record">育ちの記録（教育を変えた履歴。E2 以降が書き足す。中身は読み込みでは解釈しない）。</param>
public sealed record FighterCharacter(string Id, string Name, string Reading, string HudName, string Origin, CountryInfo Country,
    HandWear Wear, WeaponKind? Weapon, string StyleName, BodyBuild Build, FaceLook Look, string Tagline,
    CharacterEducation Education, IReadOnlyList<System.Text.Json.Nodes.JsonObject> Record)
{
    /// <summary>この人のボットのスタイル（相手を決めない教育まで。<see cref="Resolve(string)"/> の <c>null</c>）。</summary>
    /// <summary>プロフィール（経歴・人柄。実況・解説の資料。<see cref="CharacterProfile"/>）。ファイルに無ければ null。</summary>
    public CharacterProfile? Profile { get; init; }

    /// <summary>顔の定義（部位ごとの部品の重みと値。ファイルの <c>face</c>。無ければ名前と <see cref="Look"/> の髪型・眉・ひげから決める: <see cref="FaceSpec.FromLook"/>）。見た目と当たりの形だけ（強さは変えない）。</summary>
    public FaceSpec Face { get => _face ??= FaceSpec.FromLook(Look, Id); init => _face = value; }

    private FaceSpec? _face;

    /// <summary>所属の団体の id（<c>wvc</c>・<c>vfc</c>。ファイルの <c>org</c>、無ければ <see cref="DefaultOrg"/>）。中継の団体のファイル（Arena の <c>Orgs/&lt;id&gt;.json</c>）の id。強さは体から物理で出るので、所属は強さに響かない（中継の見せ方と対戦の組み合わせの印）。</summary>
    public string Org { get; init; } = DefaultOrg;

    /// <summary>所属が書かれていないときの団体の id。</summary>
    public const string DefaultOrg = "wvc";

    public BotStyle Style => Resolve((string?)null);

    /// <summary>
    /// この相手（<paramref name="opponentId"/>。試合の相手が決まっていなければ null）と戦うときのスタイル: 教科書 → 教育の <c>style</c> → <c>vs[相手]</c> の順に重ねた結果。
    /// 教育が空なら教科書そのもの。名前は教科書の名前のまま（<see cref="EducationResolver"/>）。
    /// <para>戦略の層（<see cref="BotStyle.GamePlan"/>）があり、相手の表（<see cref="GamePlanStyle.Foe"/>）が教育に書かれていなければ、相手のキャラ（名簿の
    /// <paramref name="opponentId"/>）の自分の表（<see cref="GamePlanStyle.Own"/>）を入れる（相手の得意な間合いを情報として持つ。CLAUDE.md §1.8 の「相手のキャラの id で引く値」）。
    /// 名簿にいない相手・表の無い相手なら空のまま。</para>
    /// </summary>
    public BotStyle Resolve(string? opponentId = null)
        => WithFoe(EducationResolver.Resolve(BotCatalog.Style(StyleName), Education, opponentId), opponentId is null ? null : CharacterRoster.Find(opponentId));

    /// <summary>この相手（名簿にいない、プレイヤーが作ったキャラでもよい）と戦うときのスタイル（<see cref="Resolve(string)"/> と同じ重ね方。相手の表はこのキャラから）。</summary>
    public BotStyle Resolve(FighterCharacter opponent)
    {
        ArgumentNullException.ThrowIfNull(opponent);
        return WithFoe(EducationResolver.Resolve(BotCatalog.Style(StyleName), Education, opponent.Id), opponent);
    }

    private static BotStyle WithFoe(BotStyle style, FighterCharacter? opponent)
        => style.GamePlan is { Foe.Count: 0 } plan && opponent?.Style.GamePlan is { Own.Count: > 0 } theirs
            ? style with { GamePlan = plan with { Foe = theirs.Own } }
            : style;

    /// <summary>
    /// 握る武器のボットの判断の値: 武器の教科書（<see cref="WeaponCatalog"/>）に教育の <c>weaponBot</c> を重ねた結果（<see cref="WeaponEducation"/>）。
    /// 刀・ロングソード・こんぼうを握っていなければ null。教育が空なら教科書そのもの。
    /// </summary>
    public WeaponStyle? WeaponStyle => WeaponEducation.Resolve(Weapon, Education.WeaponBot);

    /// <summary>空手の構え（素手で、スタイルが空手）か。デスクトップの操作の割り当て（蹴り）に使う。</summary>
    public bool IsKarate => StyleName == BotCatalog.Karate.Name && Wear == HandWear.None && Weapon is null;

    /// <summary>武器の表示名（握る物が無ければはめる物の名前）。</summary>
    public string EquipmentName => Weapon is { } w ? (w == WeaponKind.IronSword && Wear == HandWear.Shield ? "剣と盾" : WeaponLabel(w)) : WearLabel(Wear, StyleName);

    public static string WeaponLabel(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Katana => "日本刀",
        WeaponKind.Longsword => "ロングソード",
        WeaponKind.IronSword => "鉄の剣",
        WeaponKind.WoodenClub => "こんぼう",
        WeaponKind.Staff => "六尺棒",
        WeaponKind.Jo => "杖",
        WeaponKind.Bat => "バット",
        _ => weapon.ToString(),
    };

    public static string WearLabel(HandWear wear, string styleName = "") => wear switch
    {
        HandWear.Glove => "グローブ",
        HandWear.None => styleName == BotCatalog.Karate.Name ? "素手（空手）" : "素手",
        HandWear.KnuckleDuster => "メリケンサック",
        HandWear.Katar => "カタール",
        HandWear.Gauntlet => "籠手",
        HandWear.Tonfa => "トンファー",
        HandWear.Shield => "盾",
        _ => wear.ToString(),
    };
}
