using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>テレビ中継の 1 人の紹介（リングネーム・流派の見出し・その下の説明）。</summary>
/// <param name="Name">リングネーム。</param>
/// <param name="Discipline">流派・武器の見出し（「ボクシング」「空手」「日本刀」）。</param>
/// <param name="Detail">見出しの下の説明（スタイル・手にはめる物）。</param>
/// <param name="FullName">紹介の板の上の行に出す本名（名簿のキャラクター）。空なら出さない。</param>
/// <param name="Profile">プロフィール（名簿のキャラクター。比べる表に年齢・身長・体重・戦績を出す）。無ければ null。</param>
/// <param name="Seat">団体の中の階級と席（「ミドル級 王者」「ヘビー級 2位」）。比べる表に出す。無ければ null（行を出さない）。</param>
/// <param name="Character">名簿のキャラクター（全身の絵を試合の体のボクセルから写す番組の型が読む）。名簿に無い選手は null。</param>
internal sealed record TvCorner(string Name, string Discipline, string Detail, string FullName = "", MiniRing.Characters.CharacterProfile? Profile = null, MiniRing.Characters.FighterCharacter? Character = null, string? Seat = null)
{
    /// <summary>名簿のキャラクターの紹介: 試合の名前・武器（はめる物）・肩書きと出身・本名。</summary>
    public static TvCorner Of(MiniRing.Characters.FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        // 流派の見出しはスタイル・はめる物・武器から（「グローブ」でなく「ボクシング」）。下の行は肩書きと出身。
        var discipline = Of(character.HudName, character.StyleName, character.Wear, character.Weapon).Discipline;
        return new TvCorner(character.HudName, discipline, $"{character.Tagline}・{character.Origin}", character.Name, character.Profile, character);
    }

    /// <summary>手にはめる物・握る武器・スタイルから、紹介の見出しを作る。</summary>
    public static TvCorner Of(string ringName, string styleName, HandWear wear, WeaponKind? weapon)
    {
        var (discipline, detail) = weapon switch
        {
            WeaponKind.Katana => (TvLocale.T("日本刀", "Katana"), TvLocale.T("両手持ち・刀の型", "Two-handed · katana forms")),
            WeaponKind.Longsword => (TvLocale.T("ロングソード", "Longsword"), TvLocale.T("両手持ち・西洋剣の型", "Two-handed · Western sword forms")),
            WeaponKind.Staff => (TvLocale.T("棒術", "Bojutsu"), TvLocale.T("両手持ち・六尺棒の型", "Two-handed · six-foot staff forms")),
            WeaponKind.Jo => (TvLocale.T("杖術", "Jojutsu"), TvLocale.T("両手持ち・杖（4 尺 3 寸）", "Two-handed · jo staff (128 cm)")),
            WeaponKind.WoodenClub => (TvLocale.T("こんぼう", "Wooden Club"), TvLocale.T("片手持ち・", "One-handed · ") + StyleLabel(styleName)),
            WeaponKind.Bat => (TvLocale.T("バット", "Baseball Bat"), TvLocale.T("両手持ち・バットの型", "Two-handed · bat forms")),
            WeaponKind.IronSword when wear == HandWear.Shield => (TvLocale.T("剣と盾", "Sword & Shield"), TvLocale.T("右手に剣・左手に盾", "Sword in right hand · shield in left")),
            WeaponKind.IronSword => (TvLocale.T("鉄の剣", "Iron Sword"), TvLocale.T("片手持ち・", "One-handed · ") + StyleLabel(styleName)),
            _ => wear switch
            {
                HandWear.None when styleName == "karate" => (TvLocale.T("空手", "Karate"), TvLocale.T("素手・正拳と蹴り", "Bare hands · punches and kicks")),
                HandWear.None => (TvLocale.T("ベアナックル", "Bare Knuckle"), StyleLabel(styleName)),
                HandWear.KnuckleDuster => (TvLocale.T("メリケンサック", "Brass Knuckles"), StyleLabel(styleName)),
                HandWear.Katar => (TvLocale.T("カタール", "Katar"), StyleLabel(styleName)),
                HandWear.Gauntlet => (TvLocale.T("籠手", "Gauntlets"), StyleLabel(styleName)),
                HandWear.Tonfa => (TvLocale.T("トンファー", "Tonfa"), TvLocale.T("両手に木の棒・前腕で受ける", "Wooden batons in both hands · forearm blocks")),
                HandWear.Shield => (TvLocale.T("盾", "Shield"), TvLocale.T("盾で受ける", "Blocks with the shield")),
                _ => (TvLocale.T("ボクシング", "Boxing"), StyleLabel(styleName)),
            },
        };
        return new TvCorner(ringName, discipline, detail);
    }

    private static string StyleLabel(string styleName) => styleName switch
    {
        "champion" => TvLocale.T("王者型・読みとカウンター", "Champion · reads and counters"),
        "swarmer" => TvLocale.T("インファイター", "In-fighter"),
        "slugger" => TvLocale.T("強打者", "Slugger"),
        "outboxer" => TvLocale.T("アウトボクサー", "Out-boxer"),
        "counter" => TvLocale.T("カウンター使い", "Counter-puncher"),
        "jabber" => TvLocale.T("ジャブ使い", "Jabber"),
        "karate" => TvLocale.T("空手家", "Karateka"),
        _ => styleName,
    };
}

/// <summary>テレビ中継の試合の札（大会名・何試合目か・決まり）。</summary>
/// <param name="EventTitle">大会名。</param>
/// <param name="Bout">何試合目か。</param>
/// <param name="Red">赤コーナーの紹介。</param>
/// <param name="Blue">青コーナーの紹介。</param>
/// <param name="Rules">試合の決まり。</param>
/// <param name="Belt">王座のベルトが懸かった試合か（札の <c>belt</c>）。勝者が出たとき WVC の勝敗の画面が勝者の名前の上にベルトを出す。引き分けでは出さない。</param>
internal sealed record TvCard(string EventTitle, string Bout, TvCorner Red, TvCorner Blue, MatchRules Rules, bool Belt = false);

/// <summary>試合の結果（勝敗画面に出すもの）。</summary>
/// <param name="Winner">1 = 赤、−1 = 青、0 = 引き分け。</param>
/// <param name="Finish">ゲームの決着の文（"K.O. — BRAIN DESTROYED"・"10 COUNT K.O."・"... WIN BY DECISION"・"DRAW"）。</param>
/// <param name="Round">決着したラウンド。</param>
/// <param name="RoundTime">決着したラウンドの経過時間 [s]。</param>
/// <param name="RedScores">赤がラウンドごとに与えたダメージ（判定の得点）。</param>
/// <param name="BlueScores">青がラウンドごとに与えたダメージ。</param>
internal sealed record TvResult(int Winner, string? Finish, int Round, float RoundTime, IReadOnlyList<float> RedScores, IReadOnlyList<float> BlueScores)
{
    /// <summary>判定で決まった（K.O. でない）。</summary>
    public bool ByDecision => Finish is null || Finish.Contains("DECISION", StringComparison.Ordinal) || Finish == "DRAW";

    /// <summary>決まり手の大きな文字（K.O. / 判定 / 引き分け）。</summary>
    public string Method => Winner == 0 ? "引き分け" : ByDecision ? "判定" : Finish?.StartsWith("RING OUT", StringComparison.Ordinal) == true ? "リングアウト"
        : Finish?.StartsWith("TKO", StringComparison.Ordinal) == true ? "T.K.O." : "K.O.";

    /// <summary>決まり手の説明（「脳の破壊」「失神」「10 カウント」）。</summary>
    public string Cause => Finish?.Split("  ")[0] switch // ゲームの文の後ろの "  BLUE WINS" は落とす
    {
        null => "",
        "RING OUT" => "リングの外へ",
        "10 COUNT K.O." => "10 カウント",
        "K.O. — OUT COLD" => "失神",
        { } tko when tko.StartsWith("TKO — ", StringComparison.Ordinal) => $"1 ラウンドに {tko["TKO — ".Length..].Split(' ')[0]} 度のダウン",
        { } ko when ko.StartsWith("K.O. — ", StringComparison.Ordinal) => Organ(ko["K.O. — ".Length..].Replace(" DESTROYED", "", StringComparison.Ordinal)) + "の破壊",
        _ => "",
    };

    /// <summary>死んだ臓器・部位の名前（「脳」「心臓」「首」。K.O. の死でなければ空）。</summary>
    public string DeathOrgan => Finish?.Split("  ")[0] is { } ko && ko.StartsWith("K.O. — ", StringComparison.Ordinal) && !ko.Contains("OUT COLD", StringComparison.Ordinal)
        ? Organ(ko["K.O. — ".Length..].Replace(" DESTROYED", "", StringComparison.Ordinal))
        : "";

    /// <summary>死因を言葉に（「首を断たれて」「脳を壊されて」）。死でなければ空。</summary>
    public string DeathPhrase => DeathOrgan switch
    {
        "" => "",
        "首" => "首を断たれて",
        var organ => $"{organ}を壊されて",
    };

    private static string Organ(string cause) => cause switch
    {
        "BRAIN" => "脳",
        "HEART" => "心臓",
        "LUNG" => "肺",
        "LIVER" => "肝臓",
        "GUT" => "内臓",
        "NECK" => "首", // 首の骨折・切断で頭が離れた死（Fighter.Die("neck")）
        _ => cause,
    };
}

/// <summary>
/// テレビ中継の冒頭のタイトルと最後の勝敗の画面（描画だけ。試合には響かない）。背景はリングの 3D の絵のまま、
/// 帯と文字を時間でスライド・拡大させて重ねる。文字は影を付けて背景の上でも読めるようにする。
/// 団体の名前・紋章・色・決まりの言い方は <paramref name="org"/>（団体のファイル。null なら WVC＝今の見た目）から。コーナーの赤・青は団体によらず同じ。
/// </summary>
internal sealed class TvShowGraphics(int width, int height, Organization? org = null)
{
    private readonly Organization _org = org ?? Organization.Default;

    /// <summary>冒頭のタイトルの長さ [s]。</summary>
    public const float OpeningSeconds = 7f;

    /// <summary>勝敗の画面の長さ [s]。</summary>
    public const float ResultSeconds = 9f;

    // 団体の色（団体のファイルの colors。WVC は金・白・灰・濃紺）。
    private Vector4 White => _org.Colors.Text;
    private Vector4 Gold => _org.Colors.Accent;
    private Vector4 Grey => _org.Colors.Muted;
    private Vector4 Ink => _org.Colors.Ink;

    private static readonly Vector4 RedDeep = new(0.62f, 0.06f, 0.07f, 0.94f);
    private static readonly Vector4 RedBright = new(0.92f, 0.2f, 0.16f, 0.94f);
    private static readonly Vector4 BlueDeep = new(0.05f, 0.14f, 0.52f, 0.94f);
    private static readonly Vector4 BlueBright = new(0.2f, 0.42f, 0.95f, 0.94f);

    /// <summary>冒頭のタイトル: 大会名 → 両コーナーの紹介が左右から → VS → 決まりの帯。最後の 0.5 秒で消える。</summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) Opening(TvCard card, float t)
    {
        ArgumentNullException.ThrowIfNull(card);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var fade = 1f - Clamp01((t - (OpeningSeconds - 0.5f)) / 0.5f);
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.5f), new Vector2(w, h), new Vector4(0f, 0f, 0f, 0.5f * Clamp01(t / 0.4f) * fade)));

        // 大会名: 中央に出て、紹介が入る前に上へ上がる。
        var logoIn = EaseOut(Clamp01(t / 0.7f));
        var logoUp = EaseInOut(Clamp01((t - 1.1f) / 0.6f));
        var logoY = Lerp(h * 0.45f, h * 0.155f, logoUp);
        var logoSize = Lerp(1.25f, 0.8f, logoUp);
        var bandWidth = w * logoIn;
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, logoY - (10f * logoSize)), new Vector2(bandWidth, 270f * logoSize), Ink with { W = 0.88f * fade },
            FillColorEnd: Ink with { W = 0.15f * fade }, GradientAxis: Vector2.UnitY));
        // 団体の紋章（光の帯が文字を斜めに横切る: 0.5〜1.4 秒）、その下に字間を空けた団体の名前と、細い罫、大会名。
        var emblemCenter = new Vector2(w * 0.5f, logoY - (56f * logoSize));
        var emblemUnit = 7.2f * logoSize * (0.85f + (0.15f * logoIn));
        var sweepFrom = emblemCenter.X - (16f * emblemUnit);
        var shine = Lerp(sweepFrom, (2f * emblemCenter.X) - sweepFrom, Clamp01((t - 0.5f) / 0.9f));
        Emblems.Draw(_org, text, shapes, emblemCenter, emblemUnit, logoIn * fade, shine);
        text.Add(new ScreenTextDraw(Spaced(_org.Name.ToUpperInvariant()), new Vector2(w * 0.5f, logoY + (22f * logoSize)), 17f * logoSize, White with { W = 0.9f * logoIn * fade }, TextAnchor.Center));
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, logoY + (40f * logoSize)), new Vector2(bandWidth * 0.36f, 1.5f), Gold with { W = fade }));
        RuleGems(shapes, new Vector2(w * 0.5f, logoY + (40f * logoSize)), bandWidth * 0.18f, 2.6f * logoSize, logoIn * fade);
        Shadowed(text, card.EventTitle, new Vector2(w * 0.5f, logoY + (48f * logoSize)), 30f * logoSize, Gold with { W = logoIn * fade }, TextAnchor.Center);

        // 何試合目か。
        var boutIn = Clamp01((t - 1.5f) / 0.4f);
        Shadowed(text, card.Bout, new Vector2(w * 0.5f, h * 0.28f), 30f, White with { W = boutIn * fade }, TextAnchor.Center);

        // 両コーナーの紹介の板: 左右の画面の外から滑り込む。
        var slide = EaseOut(Clamp01((t - 1.6f) / 0.7f));
        var panelSize = new Vector2(w * 0.4f, 210f);
        var panelY = h * 0.53f;
        var redX = Lerp(-panelSize.X * 0.5f, w * 0.25f, slide);
        var blueX = Lerp(w + (panelSize.X * 0.5f), w * 0.75f, slide);
        CornerPanel(text, shapes, card.Red, new Vector2(redX, panelY), panelSize, RedBright, RedDeep, TvLocale.T("赤コーナー", "RED CORNER"), fade);
        CornerPanel(text, shapes, card.Blue, new Vector2(blueX, panelY), panelSize, BlueBright, BlueDeep, TvLocale.T("青コーナー", "BLUE CORNER"), fade);

        // VS: 少し大きく出て落ち着く。
        var vs = Clamp01((t - 2.3f) / 0.45f);
        if (vs > 0f)
        {
            var pop = BackOut(vs);
            shapes.Add(UiShapeDraw.Rounded(new Vector2(w * 0.5f, panelY), new Vector2(124f, 124f) * pop, 62f * pop, Ink with { W = 0.92f * fade }) with
            {
                BorderColor = Gold with { W = fade },
                BorderPixels = 3f,
            });
            Shadowed(text, "VS", new Vector2(w * 0.5f, panelY - (34f * pop)), 64f * pop, Gold with { W = fade }, TextAnchor.Center);
        }

        // 決まりの帯（下）。
        var rules = Clamp01((t - 3f) / 0.5f);
        var ribbonY = h * 0.85f;
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, ribbonY + 14f), new Vector2(w * 0.86f * EaseOut(rules), 46f), Ink with { W = 0.85f * fade }, UiShapeDraw.Uniform(6f)));
        var rule = TvLocale.Singular(_org.Text.OpeningRule
            .Replace("{seconds}", card.Rules.RoundSeconds.ToString("0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{rounds}", card.Rules.Rounds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Shadowed(text, rule, new Vector2(w * 0.5f, ribbonY), 24f, Grey with { W = rules * fade }, TextAnchor.Center);
        return (text, shapes);
    }

    /// <summary>選手の比較の表（テール・オブ・ザ・テープ）の長さ [s]。実況・解説が両者を経歴から紹介する時間（冒頭のタイトルの後）。</summary>
    public const float TapeSeconds = 14f;

    /// <summary>
    /// 選手の比較の表: 真ん中に項目（年齢・身長・体重・戦績・出身・拠点・スタンス）、左に赤、右に青の値。行が上から順に現れ、最後の 0.5 秒で消える。
    /// 両者にプロフィールが無ければ何も出さない。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) Tape(TvCard card, float t)
    {
        ArgumentNullException.ThrowIfNull(card);
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        if (card.Red.Profile is not { } red || card.Blue.Profile is not { } blue)
        {
            return (text, shapes);
        }

        var w = (float)width;
        var h = (float)height;
        var fade = Clamp01(t / 0.4f) * (1f - Clamp01((t - (TapeSeconds - 0.5f)) / 0.5f));
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.5f), new Vector2(w, h), new Vector4(0f, 0f, 0f, 0.6f * fade)));
        Logo(text, shapes, new Vector2(74f, 34f), 1f, fade);
        Shadowed(text, TvLocale.T("選手の比較", "TALE OF THE TAPE"), new Vector2(w * 0.5f, h * 0.06f), 40f, Gold with { W = fade }, TextAnchor.Center);

        // 名前の帯（赤は左、青は右）。
        var headY = h * 0.17f;
        foreach (var (corner, x, bright, deep) in new[] { (card.Red, w * 0.27f, RedBright, RedDeep), (card.Blue, w * 0.73f, BlueBright, BlueDeep) })
        {
            shapes.Add(new UiShapeDraw(new Vector2(x, headY + 22f), new Vector2(w * 0.36f, 58f), bright with { W = bright.W * fade }, UiShapeDraw.Uniform(8f),
                FillColorEnd: deep with { W = deep.W * fade }, GradientAxis: Vector2.UnitY));
            Shadowed(text, corner.Name, new Vector2(x, headY + 4f), 36f, White with { W = fade }, TextAnchor.Center);
        }

        static string Record(Characters.FightRecord r) => TvLocale.Record(r);
        var rows = new (string Label, string Red, string Blue)[]
        {
            (TvLocale.T("年齢", "AGE"), TvLocale.T($"{red.Age}歳", $"{red.Age}"), TvLocale.T($"{blue.Age}歳", $"{blue.Age}")),
            (TvLocale.T("身長", "HEIGHT"), $"{red.HeightCm} cm", $"{blue.HeightCm} cm"),
            (TvLocale.T("体重", "WEIGHT"), $"{red.WeightKg} kg", $"{blue.WeightKg} kg"),
            (TvLocale.T("戦績", "RECORD"), Record(red.FightRecord), Record(blue.FightRecord)),
            (TvLocale.T("出身", "BIRTHPLACE"), red.Birthplace, blue.Birthplace),
            (TvLocale.T("拠点", "HOMETOWN"), red.Hometown, blue.Hometown),
            (TvLocale.T("構え", "STANCE"), red.Stance, blue.Stance),
        };
        if (card.Red.Seat is not null || card.Blue.Seat is not null)
        {
            // 団体の席（階級制の団体の計量と王座・ランキング）は体重のすぐ下に。
            rows = [.. rows[..3], (TvLocale.T("階級・席", "DIVISION / RANK"), card.Red.Seat ?? "—", card.Blue.Seat ?? "—"), .. rows[3..]];
        }

        var FitChars = TvLocale.IsEnglish ? 34 : 18; // 表の 1 マスの字数（日本語は約 18 字、英語は字幅が狭いので約 34 字）
        var rowHeight = (h * 0.66f) / rows.Length;
        var top = h * 0.29f;
        for (var i = 0; i < rows.Length; i++)
        {
            var a = Clamp01((t - 0.6f - (i * 0.35f)) / 0.4f) * fade;
            if (a <= 0f)
            {
                continue;
            }

            var y = top + (i * rowHeight);
            shapes.Add(UiShapeDraw.Rounded(new Vector2(w * 0.5f, y + (rowHeight * 0.4f)), new Vector2(w * 0.9f, rowHeight - 8f), 6f, Ink with { W = 0.85f * a }));
            text.Add(new ScreenTextDraw(rows[i].Label, new Vector2(w * 0.5f, y + (rowHeight * 0.4f) - 14f), 24f, Gold with { W = a }, TextAnchor.Center));
            text.Add(new ScreenTextDraw(Fit(rows[i].Red), new Vector2(w * 0.27f, y + (rowHeight * 0.4f) - 15f), 26f, White with { W = a }, TextAnchor.Center));
            text.Add(new ScreenTextDraw(Fit(rows[i].Blue), new Vector2(w * 0.73f, y + (rowHeight * 0.4f) - 15f), 26f, White with { W = a }, TextAnchor.Center));
        }

        return (text, shapes);

        // 出身・拠点の長い値は表の幅に収める。
        string Fit(string value) => value.Length <= FitChars ? value : value[..(FitChars - 1)] + "…";
    }

    /// <summary>
    /// 本日のプログラム:見出し → 試合の行が上から順に滑り込む。済んだ試合は勝者と決まり手、この試合は金の縁と「この試合」の札、
    /// まだの試合は空欄。この試合に懸かるもの（「勝者は決勝へ」）を下の帯に。最後の 0.5 秒で消える。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) Program(TvProgram program, float t)
    {
        ArgumentNullException.ThrowIfNull(program);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var fade = 1f - Clamp01((t - (program.Seconds - 0.5f)) / 0.5f);
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.5f), new Vector2(w, h), new Vector4(0f, 0f, 0f, 0.62f * Clamp01(t / 0.4f) * fade)));
        Logo(text, shapes, new Vector2(74f, 34f), 1f, fade);

        var headIn = EaseOut(Clamp01(t / 0.6f));
        Shadowed(text, TvLocale.T("本日のプログラム", "TONIGHT'S CARD"), new Vector2(w * 0.5f, (h * 0.08f) - ((1f - headIn) * 20f)), 46f, Gold with { W = headIn * fade }, TextAnchor.Center);
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.08f + 58f), new Vector2(w * 0.4f * headIn, 1.5f), Gold with { W = fade }));
        Shadowed(text, program.Title, new Vector2(w * 0.5f, h * 0.08f + 66f), 26f, White with { W = headIn * fade }, TextAnchor.Center);

        var count = program.Bouts.Count;
        var rowHeight = MathF.Min(78f, (h * 0.56f) / count);
        var rowWidth = w * 0.84f;
        var top = (h * 0.3f) + (rowHeight * 0.5f);
        for (var i = 0; i < count; i++)
        {
            var bout = program.Bouts[i];
            var slide = EaseOut(Clamp01((t - 0.6f - (i * 0.3f)) / 0.5f));
            if (slide <= 0f)
            {
                continue;
            }

            var a = slide * fade;
            var center = new Vector2((w * 0.5f) - ((1f - slide) * w * 0.3f), top + (i * rowHeight));
            var size = new Vector2(rowWidth, rowHeight - 10f);
            var band = UiShapeDraw.Rounded(center, size, 8f, Ink with { W = 0.9f * a });
            shapes.Add(bout.Current ? band with { BorderColor = Gold with { W = a }, BorderPixels = 3f } : band);
            var left = center.X - (size.X * 0.5f);
            var textY = center.Y - 15f;
            text.Add(new ScreenTextDraw(bout.Bout, new Vector2(left + 24f, textY), 24f, (bout.Current ? Gold : Grey) with { W = a }));

            // 対戦: 赤の名前（右寄せ）・vs・青の名前（左寄せ）。勝った側を白く、負けた側を灰に。
            var vsX = left + (size.X * 0.47f);
            var redColor = bout.Winner == "blue" ? Grey : White;
            var blueColor = bout.Winner == "red" ? Grey : White;
            shapes.Add(UiShapeDraw.Rounded(new Vector2(vsX - 238f, center.Y), new Vector2(6f, size.Y * 0.56f), 2f, RedBright with { W = a }));
            shapes.Add(UiShapeDraw.Rounded(new Vector2(vsX + 238f, center.Y), new Vector2(6f, size.Y * 0.56f), 2f, BlueBright with { W = a }));
            // 名前は vs と色の棒の間（約 190 px）に収める（「準決勝第1試合の勝者」のような長い札は小さく）。
            static float Fit(string name) => MathF.Min(28f, 190f / MathF.Max(1, name.Length));
            text.Add(new ScreenTextDraw(bout.Red, new Vector2(vsX - 32f, textY - 2f + ((28f - Fit(bout.Red)) * 0.5f)), Fit(bout.Red), redColor with { W = a }, TextAnchor.Right));
            text.Add(new ScreenTextDraw("vs", new Vector2(vsX, textY + 2f), 22f, Gold with { W = a }, TextAnchor.Center));
            text.Add(new ScreenTextDraw(bout.Blue, new Vector2(vsX + 32f, textY - 2f + ((28f - Fit(bout.Blue)) * 0.5f)), Fit(bout.Blue), blueColor with { W = a }));

            // 右端: 結果・この試合・まだ。
            var right = center.X + (size.X * 0.5f) - 24f;
            if (bout.WinnerName is { } winnerName)
            {
                var method = bout.Method.Length > 0 ? TvLocale.T($"（{bout.Method}）", $" ({TvLocale.Method(bout.Method)})") : "";
                text.Add(new ScreenTextDraw(TvLocale.T($"{winnerName} 勝ち{method}", $"{winnerName} wins{method}"), new Vector2(right, textY), 24f, Gold with { W = a }, TextAnchor.Right));
            }
            else if (bout.Current)
            {
                shapes.Add(UiShapeDraw.Rounded(new Vector2(right - 62f, center.Y), new Vector2(124f, 34f), 6f, Gold with { W = 0.95f * a }));
                text.Add(new ScreenTextDraw(TvLocale.T("この試合", "UP NEXT"), new Vector2(right - 62f, center.Y - 13f), 22f, Ink with { W = a }, TextAnchor.Center));
            }
            else
            {
                text.Add(new ScreenTextDraw(TvLocale.T("このあと", "LATER"), new Vector2(right, textY), 22f, Grey with { W = 0.8f * a }, TextAnchor.Right));
            }
        }

        // この試合に懸かるもの。
        if (program.Current is { Stake.Length: > 0 } current)
        {
            var stake = Clamp01((t - 0.9f - (count * 0.3f)) / 0.5f);
            var ribbonY = h * 0.9f;
            shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, ribbonY + 16f), new Vector2(w * 0.6f * EaseOut(stake), 46f), Ink with { W = 0.85f * fade }, UiShapeDraw.Uniform(6f)));
            Shadowed(text, $"{current.Bout}　・　{current.Stake}", new Vector2(w * 0.5f, ribbonY), 26f, Gold with { W = stake * fade }, TextAnchor.Center);
        }

        return (text, shapes);
    }

    /// <summary>
    /// 勝敗の画面: 「試合終了」→ 決まり手（K.O. / 判定）が弾む → 勝者の帯が勝った側から → 決着の時刻 → ラウンドごとの採点表。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) Result(TvCard card, TvResult result, float t)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(result);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.5f), new Vector2(w, h), new Vector4(0f, 0f, 0f, 0.55f * Clamp01(t / 0.5f))));
        Logo(text, shapes, new Vector2(74f, 34f), 1f);

        // 試合終了の札。
        var tagIn = Clamp01(t / 0.4f);
        shapes.Add(UiShapeDraw.Rounded(new Vector2(w * 0.5f, h * 0.09f + 16f), new Vector2(200f * EaseOut(tagIn), 40f), 6f, Gold with { W = 0.95f * tagIn }));
        text.Add(new ScreenTextDraw(TvLocale.T("試合終了", "FINAL"), new Vector2(w * 0.5f, h * 0.09f), 26f, Ink with { W = tagIn }, TextAnchor.Center));

        // 決まり手（K.O. にあたる決着の言い方は団体ごと）。
        var methodText = result.Method == "K.O." ? _org.Text.FinishWord : TvLocale.Method(result.Method);
        var method = Clamp01((t - 0.4f) / 0.5f);
        // 王座のベルトを出すときは、決まり手の字を小さく上げて、勝者の帯の上にベルトの場所をあける（字と重ならないように）。
        var showBelt = card.Belt && result.Winner != 0;
        if (method > 0f)
        {
            var pop = BackOut(method);
            var size = (methodText.Length > 3 ? 84f : 120f) * pop * (showBelt ? 0.7f : 1f);
            Shadowed(text, methodText, new Vector2(w * 0.5f, (showBelt ? h * 0.145f : h * 0.17f) + ((1f - pop) * 20f)), size, Gold with { W = method }, TextAnchor.Center, 4f);
        }

        // 勝者の帯: 勝った側から全幅へ伸びる。
        var band = EaseOut(Clamp01((t - 1.2f) / 0.6f));
        var bandY = h * 0.5f;
        var (bright, deep) = result.Winner > 0 ? (RedBright, RedDeep) : result.Winner < 0 ? (BlueBright, BlueDeep) : (Gold with { W = 0.9f }, _org.Colors.AccentDeep);
        var bandWidth = w * band;
        var bandX = result.Winner < 0 ? w - (bandWidth * 0.5f) : result.Winner > 0 ? bandWidth * 0.5f : w * 0.5f;
        shapes.Add(new UiShapeDraw(new Vector2(bandX, bandY), new Vector2(bandWidth, 128f), bright, FillColorEnd: deep, GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(new Vector2(bandX, bandY - 64f), new Vector2(bandWidth, 3f), Gold));
        shapes.Add(new UiShapeDraw(new Vector2(bandX, bandY + 64f), new Vector2(bandWidth, 3f), Gold));
        var nameIn = Clamp01((t - 1.6f) / 0.4f);
        if (showBelt)
        {
            // ベルトは帯の上、勝者の名前の真上（帯の上の縁から 10 px 空ける）。帯が伸びきるころに降りてくる。
            var k = h / 720f;
            var cell = 4f * k;
            var (beltText, beltShapes) = Shows.Wvc.WvcShowParts.ChampionBelt(true, t - 1.7f, new Vector2(w * 0.5f, bandY - (64f * k) - (10f * k) - (Shows.Wvc.WvcShowParts.BeltRows * cell * 0.5f)), cell);
            text.AddRange(beltText);
            shapes.AddRange(beltShapes);
        }

        if (result.Winner == 0)
        {
            Shadowed(text, $"{card.Red.Name}  ×  {card.Blue.Name}", new Vector2(w * 0.5f, bandY - 34f), 58f, White with { W = nameIn }, TextAnchor.Center);
        }
        else
        {
            var winner = result.Winner > 0 ? card.Red : card.Blue;
            var corner = result.Winner > 0 ? TvLocale.T("赤コーナー", "RED CORNER") : TvLocale.T("青コーナー", "BLUE CORNER");
            Shadowed(text, TvLocale.T("勝者", "WINNER"), new Vector2(w * 0.5f - 250f, bandY - 22f), 38f, Gold with { W = nameIn }, TextAnchor.Right);
            Shadowed(text, winner.Name, new Vector2(w * 0.5f - 220f, bandY - 54f), 64f, White with { W = nameIn });
            Shadowed(text, $"{corner}　{winner.Discipline}", new Vector2(w * 0.5f - 216f, bandY + 20f), 26f, Gold with { W = nameIn });
        }

        // 決着の時刻と決まり手の説明。
        var detail = Clamp01((t - 2.1f) / 0.4f);
        var timeText = TvLocale.IsEnglish
            ? string.Create(CultureInfo.InvariantCulture, $"ROUND {result.Round} {(int)result.RoundTime / 60}:{(int)result.RoundTime % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"第{result.Round}ラウンド {(int)result.RoundTime / 60}:{(int)result.RoundTime % 60:00}");
        var detailText = result.ByDecision
            ? TvLocale.Singular(_org.Text.DecisionLine.Replace("{rounds}", card.Rules.Rounds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            : TvLocale.T($"{timeText}　・　{methodText}（{result.Cause}）", $"{timeText}  ·  {methodText} ({TvLocale.Cause(result.Cause)})");
        Shadowed(text, detailText, new Vector2(w * 0.5f, bandY + 82f), 28f, White with { W = detail }, TextAnchor.Center);

        // 採点表（ラウンドごとに与えたダメージ）。
        var table = Clamp01((t - 2.7f) / 0.5f);
        if (table > 0f)
        {
            ScoreTable(text, shapes, card, result, new Vector2(w * 0.5f, h * 0.83f), table);
        }

        return (text, shapes);
    }

    private void ScoreTable(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, TvCard card, TvResult result, Vector2 center, float alpha)
    {
        var rounds = card.Rules.Rounds;
        const float NameColumn = 230f;
        const float Column = 82f;
        const float Row = 34f;
        var tableWidth = NameColumn + (Column * (rounds + 1)) + 24f;
        var left = center.X - (tableWidth * 0.5f);
        var top = center.Y - (Row * 1.5f);
        shapes.Add(UiShapeDraw.Rounded(center, new Vector2(tableWidth, Row * 3f + 14f), 8f, Ink with { W = 0.88f * alpha }));
        float ColumnX(int i) => left + NameColumn + (Column * (i + 0.5f));
        text.Add(new ScreenTextDraw(TvLocale.T("与えたダメージ", "DAMAGE DEALT"), new Vector2(left + 16f, top + 6f), 20f, Grey with { W = alpha }));
        for (var r = 0; r <= rounds; r++)
        {
            text.Add(new ScreenTextDraw(r < rounds ? $"R{r + 1}" : TvLocale.T("計", "TOTAL"), new Vector2(ColumnX(r), top + 6f), 20f, (r < rounds ? Grey : Gold) with { W = alpha }, TextAnchor.Center));
        }

        foreach (var (row, corner, scores, color) in new[] { (1, card.Red, result.RedScores, RedBright), (2, card.Blue, result.BlueScores, BlueBright) })
        {
            var y = top + (Row * row) + 4f;
            shapes.Add(UiShapeDraw.Rounded(new Vector2(left + 12f, y + 13f), new Vector2(8f, 24f), 2f, color with { W = alpha }));
            text.Add(new ScreenTextDraw(corner.Name, new Vector2(left + 24f, y), 24f, White with { W = alpha }));
            var total = 0f;
            for (var r = 0; r < rounds; r++)
            {
                var played = r < scores.Count;
                var value = played ? scores[r] : 0f;
                total += value;
                text.Add(new ScreenTextDraw(played ? string.Create(CultureInfo.InvariantCulture, $"{value:0}") : "—", new Vector2(ColumnX(r), y), 24f, (played ? White : Grey) with { W = alpha }, TextAnchor.Center));
            }

            text.Add(new ScreenTextDraw(string.Create(CultureInfo.InvariantCulture, $"{total:0}"), new Vector2(ColumnX(rounds), y), 24f, Gold with { W = alpha }, TextAnchor.Center));
        }
    }

    private void CornerPanel(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, TvCorner corner, Vector2 center, Vector2 size, Vector4 bright, Vector4 deep, string label, float fade)
    {
        shapes.Add(new UiShapeDraw(center, size, bright with { W = bright.W * fade }, UiShapeDraw.Uniform(10f), Gold with { W = 0.8f * fade }, 2f,
            FillColorEnd: deep with { W = deep.W * fade }, GradientAxis: Vector2.UnitY));
        var top = center.Y - (size.Y * 0.5f);
        text.Add(new ScreenTextDraw(corner.FullName.Length > 0 ? $"{label}　{corner.FullName}" : label, new Vector2(center.X, top + 14f), 20f, White with { W = 0.85f * fade }, TextAnchor.Center));
        Shadowed(text, corner.Name, new Vector2(center.X, top + 46f), 62f, White with { W = fade }, TextAnchor.Center);
        shapes.Add(new UiShapeDraw(new Vector2(center.X, top + 132f), new Vector2(size.X * 0.7f, 2f), Gold with { W = 0.7f * fade }));
        Shadowed(text, corner.Discipline, new Vector2(center.X, top + 142f), 34f, Gold with { W = fade }, TextAnchor.Center);
        text.Add(new ScreenTextDraw(corner.Detail, new Vector2(center.X, top + 182f), 20f, White with { W = 0.85f * fade }, TextAnchor.Center));
    }

    /// <summary>
    /// 試合中の表示（顔の窓を左下・右下に置くとき）: 上の真ん中にラウンドと時計、右上に LIVE、各コーナーの顔の窓の下に
    /// コーナーの色の名札とスタミナのメーター。顔の窓そのものと縁は <see cref="FaceCam"/> が描く（窓の中に文字を置かない。動画では窓で上書きされる）。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) MatchHud(BoxingGame game, RenderViewport redPanel, RenderViewport bluePanel, string red, string blue, float? practiceClock = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        var w = (float)width;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();

        // ラウンドと時計（ラウンド中は残り、休みは BREAK と残り、試合の後は FINAL）。
        var rounds = game.Rules?.Rounds ?? 0;
        var (label, clock) = practiceClock is { } elapsed
            ? ("SPARRING", Clock(elapsed))
            : game.MatchOver
                ? ("FINAL", rounds > 0 ? $"R{game.MatchRound}" : "")
                : game.InBreak ? ("BREAK", Clock(game.BreakLeft)) : ($"ROUND {game.MatchRound}/{rounds}", Clock(game.RoundLeft));
        var clockCenter = new Vector2(w * 0.5f, 46f);
        shapes.Add(UiShapeDraw.Rounded(clockCenter, new Vector2(150f, 66f), 8f, new Vector4(0.93f, 0.93f, 0.95f, 0.95f)));
        shapes.Add(UiShapeDraw.Rounded(clockCenter - new Vector2(0f, 23f), new Vector2(150f, 20f), 8f, Ink with { W = 0.92f }));
        text.Add(new ScreenTextDraw(label, clockCenter - new Vector2(0f, 33f), 17f, Gold, TextAnchor.Center));
        text.Add(new ScreenTextDraw(clock, clockCenter - new Vector2(0f, 12f), 36f, new Vector4(0.06f, 0.06f, 0.08f, 1f), TextAnchor.Center));
        shapes.Add(UiShapeDraw.Rounded(new Vector2(w - 62f, 30f), new Vector2(72f, 28f), 5f, new Vector4(0.85f, 0.1f, 0.1f, 0.95f)));
        text.Add(new ScreenTextDraw("LIVE", new Vector2(w - 62f, 20f), 20f, White, TextAnchor.Center));
        Logo(text, shapes, new Vector2(74f, 34f), 1f);

        foreach (var (panel, name, fighter, bright, deep) in new[] { (redPanel, red, game.Player, RedBright, RedDeep), (bluePanel, blue, game.Cpu, BlueBright, BlueDeep) })
        {
            var centerX = panel.XPx + (panel.WidthPx * 0.5f);
            var plateWidth = panel.WidthPx + 6f;
            var plateTop = panel.YPx + panel.HeightPx + 3f;
            shapes.Add(new UiShapeDraw(new Vector2(centerX, plateTop + 15f), new Vector2(plateWidth, 30f), bright, FillColorEnd: deep, GradientAxis: Vector2.UnitY));
            Shadowed(text, name, new Vector2(centerX, plateTop + 3f), 23f, White, TextAnchor.Center);
            var barTop = plateTop + 30f;
            shapes.Add(new UiShapeDraw(new Vector2(centerX, barTop + 8f), new Vector2(plateWidth, 16f), Ink));
            shapes.AddRange(TvHud.StaminaBar(new Vector2(centerX - (plateWidth * 0.5f) + 6f, barTop + 8f), plateWidth - 12f, 8f, fighter));
            if (practiceClock is not null)
            {
                // 練習試合: 窓の内側（画面の中央寄り）に、部位ごとの酸素の蓄えの体の図。
                var figureWidth = StaminaFigureWidth(panel.HeightPx);
                var left = panel.XPx < w * 0.5f ? panel.XPx + panel.WidthPx + 14f : panel.XPx - 14f - figureWidth;
                StaminaFigure(text, shapes, fighter, new Vector2(left, panel.YPx), panel.HeightPx / FigureHeight);
            }
        }

        return (text, shapes);
    }

    private const float FigureWidth = 14f;
    private const float FigureHeight = 31f;

    /// <summary>酸素の体の図の幅 [px]（顔の窓の高さ <paramref name="panelHeight"/> に図の高さを合わせたとき）。字幕を図の内側へずらすのに使う。</summary>
    public static float StaminaFigureWidth(float panelHeight) => panelHeight / FigureHeight * FigureWidth;

    /// <summary>
    /// 酸素の体の図（描画だけ）。左腕・右腕・脚・胴の 4 つの部位ごとに、筋肉の酸素の蓄え（<see cref="Fighter.OxygenOf"/>、0..1）を、
    /// 部位の枠の中を下から満たす色で表す。蓄えが減ると色が上から抜けていき、空なら枠だけになる。色は蓄えの多さ（緑・黄・赤。
    /// 区切りは <see cref="TvHud.StaminaBar(Vector2, float, float, Fighter)"/> と同じ）。画面の左の腕が本人の右（<see cref="VfcShow"/> の体の図と同じ向き）。
    /// </summary>
    public void StaminaFigure(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Fighter fighter, Vector2 topLeft, float unit)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        var line = MathF.Max(1.5f, unit * 0.22f);
        var frame = White with { W = 0.75f };
        var danger = TiredLook.DangerBelow;
        var sluggish = MathF.Max(danger, fighter.SluggishBelow);

        // 部位 = 縦につながった枠の組。満ちた高さは、組の全体（いちばん上から下まで）に対する割合で、下から数える。
        void Region(MuscleRegion region, (float X, float Y, float W, float H)[] parts)
        {
            var level = Math.Clamp(fighter.OxygenOf(region), 0f, 1f);
            var top = parts.Min(p => p.Y);
            var bottom = parts.Max(p => p.Y + p.H);
            var fillTop = bottom - ((bottom - top) * level);
            var color = level < danger ? new Vector4(0.95f, 0.2f, 0.18f, 1f) : level < sluggish ? new Vector4(0.98f, 0.8f, 0.15f, 1f) : new Vector4(0.3f, 0.9f, 0.4f, 1f);
            foreach (var (x, y, w, h) in parts)
            {
                var size = new Vector2(w * unit, h * unit);
                var center = topLeft + new Vector2((x + (w * 0.5f)) * unit, (y + (h * 0.5f)) * unit);
                shapes.Add(new UiShapeDraw(center, size, new Vector4(0f, 0f, 0f, 0.55f)));
                var from = MathF.Max(y, fillTop);
                var to = y + h;
                if (to - from > 1e-3f && level > 0.003f)
                {
                    var fillSize = new Vector2(size.X, (to - from) * unit);
                    shapes.Add(new UiShapeDraw(topLeft + new Vector2((x + (w * 0.5f)) * unit, (from + to) * 0.5f * unit), fillSize, color));
                }

                shapes.Add(UiShapeDraw.Outline(center, size, 0f, frame, line));
            }
        }

        shapes.Add(UiShapeDraw.Outline(topLeft + new Vector2(7f * unit, 2.1f * unit), new Vector2(4f * unit, 4.2f * unit), 0f, frame with { W = 0.4f }, line)); // 頭（酸素の対象ではない）
        Region(MuscleRegion.RightArm, [(0.4f, 5f, 2.4f, 6f), (0.4f, 11.2f, 2.4f, 5.8f)]);
        Region(MuscleRegion.LeftArm, [(11.2f, 5f, 2.4f, 6f), (11.2f, 11.2f, 2.4f, 5.8f)]);
        Region(MuscleRegion.Trunk, [(3f, 5f, 8f, 8f), (4f, 13.4f, 6f, 3.6f)]);
        Region(MuscleRegion.Legs, [(4f, 17.4f, 2.8f, 7f), (7.2f, 17.4f, 2.8f, 7f), (4f, 24.6f, 2.8f, 6.4f), (7.2f, 24.6f, 2.8f, 6.4f)]);
        text.Add(new ScreenTextDraw("O2", topLeft + new Vector2(0.4f * unit, 0.2f * unit), MathF.Max(12f, unit * 2.2f), White with { W = 0.85f }));
    }

    /// <summary>
    /// リプレイ中の表示: 上下の黒い帯、左上の REPLAY の札とラウンド、下の帯に場面の説明（誰の何がどこへ・どれだけ・ダウン/骨折/切断）。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) ReplayOverlay(int round, string description, float videoTime)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>
        {
            new(new Vector2(w * 0.5f, 26f), new Vector2(w, 52f), new Vector4(0f, 0f, 0f, 0.9f)),
            new(new Vector2(w * 0.5f, h - 34f), new Vector2(w, 68f), new Vector4(0f, 0f, 0f, 0.9f)),
            UiShapeDraw.Rounded(new Vector2(92f, 26f), new Vector2(124f, 32f), 6f, new Vector4(0.85f, 0.1f, 0.1f, 0.95f)),
        };
        // 録画中の印のように点滅する丸（見た目だけ）。
        var blink = 0.5f + (0.5f * MathF.Sin(videoTime * 6f));
        shapes.Add(UiShapeDraw.Rounded(new Vector2(44f, 26f), new Vector2(12f, 12f), 6f, White with { W = 0.4f + (0.6f * blink) }));
        text.Add(new ScreenTextDraw("REPLAY", new Vector2(100f, 13f), 24f, White, TextAnchor.Center));
        text.Add(new ScreenTextDraw(TvLocale.T($"第{round}ラウンド　スロー", $"ROUND {round}  SLOW MOTION REPLAY"), new Vector2(170f, 14f), 22f, Gold));
        Shadowed(text, description, new Vector2(w * 0.5f, h - 50f), 24f, White, TextAnchor.Center);
        Logo(text, shapes, new Vector2(w - 74f, 26f), 0.8f);
        return (text, shapes);
    }

    /// <summary>
    /// 中継の主催団体（団体のファイル、2026-11-16 ユーザーの指定・2026-11-18 に団体ごとのファイルへ）。中継は作中の世界のテレビ番組なので、ゲームの名前（VGV）は出さない。
    /// </summary>
    public Organization Org => _org;

    /// <summary>
    /// 中継のロゴの札: 団体の紋章（<see cref="WvcEmblem"/>・<see cref="VfcEmblem"/>）を小さく描く。
    /// <paramref name="center"/> は札の中心、<paramref name="scale"/> は大きさ。
    /// </summary>
    public void Logo(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float scale, float alpha = 1f)
    {
        Emblems.Draw(_org, text, shapes, center, 3.1f * scale, alpha);
    }

    /// <summary>ロゴの文字列の横幅 [px]（<see cref="VoxelWord"/> と同じ組み方）。</summary>
    private static float VoxelWordWidth(string word, float cell) => ((word.Length * 5) + ((word.Length - 1) * 1.5f)) * cell;

    /// <summary>罫の飾り: 中央の大きな菱形と、罫の両端の小さな菱形（ボクセルを積んだ金）。<paramref name="halfWidth"/> は罫の半分の長さ [px]。</summary>
    public void RuleGems(List<UiShapeDraw> shapes, Vector2 center, float halfWidth, float cell, float alpha = 1f)
    {
        var cells = new List<Vector2>();
        void Lozenge(Vector2 at, int radius)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var span = radius - Math.Abs(dy);
                for (var dx = -span; dx <= span; dx++)
                {
                    cells.Add(at + (new Vector2(dx, dy) * cell));
                }
            }
        }

        Lozenge(center, 2);
        Lozenge(center + new Vector2(halfWidth, 0f), 1);
        Lozenge(center - new Vector2(halfWidth, 0f), 1);
        VoxelCells(shapes, cells, cell, alpha, float.NaN);
    }

    /// <summary>文字の間に細い空白を入れる（高級感のある字間の広い組み方）。</summary>
    private static string Spaced(string value) => string.Join('\u2009', value.ToCharArray());

    /// <summary>
    /// 5 × 7 のマスの字形（ロゴに使う文字だけ）。'#' が積むボクセル。
    /// </summary>
    private static readonly Dictionary<char, string[]> VoxelGlyphs = new()
    {
        ['V'] = ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
    };

    /// <summary>
    /// ボクセルを積んだ金の文字。<paramref name="center"/> は文字列の中心、<paramref name="cell"/> は 1 マスの大きさ [px]、
    /// <paramref name="shine"/> は光の帯が通る X [px]（NaN なら光らない）。
    /// </summary>
    public void VoxelWord(List<UiShapeDraw> shapes, string word, Vector2 center, float cell, float alpha = 1f, float shine = float.NaN)
    {
        const int GlyphWidth = 5;
        const int GlyphHeight = 7;
        const float Gap = 1.5f; // 文字の間（マス）
        var columns = (word.Length * GlyphWidth) + ((word.Length - 1) * Gap);
        var origin = center - new Vector2(columns * cell * 0.5f, GlyphHeight * cell * 0.5f);
        var cells = new List<Vector2>();
        for (var i = 0; i < word.Length; i++)
        {
            if (!VoxelGlyphs.TryGetValue(word[i], out var glyph))
            {
                continue;
            }

            var left = origin.X + (i * (GlyphWidth + Gap) * cell);
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var col = 0; col < GlyphWidth; col++)
                {
                    if (glyph[row][col] == '#')
                    {
                        cells.Add(new Vector2(left + ((col + 0.5f) * cell), origin.Y + ((row + 0.5f) * cell)));
                    }
                }
            }
        }

        VoxelCells(shapes, cells, cell, alpha, shine);
    }

    /// <summary>
    /// 金のボクセルを並べる: マスごとに、右下へずらした暗い銅色の側面（奥行き）を置き、その上に上が明るく下が深い金の面を置く（立方体の斜め見え）。
    /// 光の帯（<paramref name="shine"/> の X の前後 2.5 マス）にかかるマスは白へ寄せる。<paramref name="tone"/> は金の明るさ（翼は 0.8 で文字より沈める）。
    /// </summary>
    private void VoxelCells(List<UiShapeDraw> shapes, List<Vector2> cells, float cell, float alpha, float shine, float tone = 1f)
    {
        var depth = new Vector2(cell * 0.4f);
        var face = new Vector2(cell * 0.9f);
        var side = new Vector4(_org.Colors.VoxelSide, alpha);
        var top = new Vector4(_org.Colors.VoxelTop * tone, alpha);
        var bottom = new Vector4(_org.Colors.VoxelBottom * tone, alpha);
        var white = new Vector4(1f, 1f, 1f, alpha);

        // 側面を先に全部、その上に面（隣のマスの面が側面を隠す）。
        foreach (var c in cells)
        {
            shapes.Add(new UiShapeDraw(c + depth, face, side));
        }

        foreach (var c in cells)
        {
            var glow = float.IsNaN(shine) ? 0f : MathF.Exp(-MathF.Pow((c.X - shine - ((c.Y - cells[0].Y) * 0.5f)) / (2.5f * cell), 2f));
            shapes.Add(new UiShapeDraw(c, face, Vector4.Lerp(top, white, 0.85f * glow), FillColorEnd: Vector4.Lerp(bottom, white, 0.7f * glow), GradientAxis: Vector2.UnitY));
            shapes.Add(new UiShapeDraw(c - new Vector2(0f, face.Y * 0.4f), new Vector2(face.X, face.Y * 0.12f), new Vector4(1f, 1f, 0.9f, 0.35f * alpha)));
        }
    }

    /// <summary>リプレイへ入る・出るときの、金の帯が横切るワイプ（<paramref name="progress"/> 0..1）。</summary>
    public IReadOnlyList<UiShapeDraw> Wipe(float progress)
    {
        var w = (float)width;
        var h = (float)height;
        var x = (-0.3f * w) + (1.6f * w * Math.Clamp(progress, 0f, 1f));
        return
        [
            new(new Vector2(x - (0.16f * w), h * 0.5f), new Vector2(0.2f * w, h), Ink with { W = 0.95f }),
            new(new Vector2(x, h * 0.5f), new Vector2(0.12f * w, h), Gold with { W = 0.95f }, FillColorEnd: _org.Colors.AccentLight with { W = 0.95f }, GradientAxis: Vector2.UnitX),
        ];
    }

    /// <summary>顔の窓の下に名札とメーターを置くための、窓の下の余白 [px]（縁 3 + 名札 30 + メーター 16 + すき間）。</summary>
    public const float CornerHudBelowPanel = 58f;

    private static string Clock(float seconds)
    {
        var whole = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }

    /// <summary>影を付けた文字（背景の 3D の絵の上でも読める）。</summary>
    private static void Shadowed(List<ScreenTextDraw> text, string value, Vector2 at, float size, Vector4 color, TextAnchor anchor = TextAnchor.Left, float offset = 2f)
    {
        text.Add(new ScreenTextDraw(value, at + new Vector2(offset, offset), size, new Vector4(0f, 0f, 0f, 0.7f * color.W), anchor));
        text.Add(new ScreenTextDraw(value, at, size, color, anchor));
    }

    private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);

    private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f);

    /// <summary>少し行き過ぎて戻る（弾む）。</summary>
    private static float BackOut(float t)
    {
        const float C = 1.70158f;
        var u = t - 1f;
        return 1f + ((C + 1f) * u * u * u) + (C * u * u);
    }
}
