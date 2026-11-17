using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MiniRing.Characters;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Bots;

/// <summary>
/// 武器のボット（刀・ロングソードの剣士 <see cref="SwordsmanBot"/>、こんぼうの <see cref="ClubBot"/>）の判断の値の全部（CLAUDE.md §1.8: 武器の教科書）。
/// <see cref="BotStyle"/> と同じく不変で、変えるときは <c>with</c>。JSON に読み書きでき（<see cref="ToJson"/>・<see cref="FromJson"/>）、
/// <see cref="Validated"/> が範囲を検査する。武器ごとの教科書は組み込みの JSON（<c>Bots/weapons/katana.json</c>・<c>longsword.json</c>・<c>club.json</c>、
/// <see cref="WeaponCatalog"/>）で、キャラクターの教育（<c>education.weaponBot</c>）が差分で重なる（<see cref="WeaponEducation"/>）。
/// <para>
/// ここにある値は、判断・技の経路・間合いの取り方だけ（入力の側）。筋力・質量・材料・威力の倍率は無い（強さは体から。§1.7）。
/// 武器の種類で判断を分けず（§1.3）、同じ値の組を 2 つのボットが読む: 剣士は <see cref="Defense"/> から <see cref="KeepReach"/> まで、
/// こんぼうは <c>Club*</c>。使わない側の値は無視される（武器の長さ・重さ・握りは、技の間合いなど見えている寸法から出る）。
/// </para>
/// </summary>
public sealed record WeaponStyle
{
    public string Name { get; init; } = "weapon";

    // ── 剣士（刀・ロングソード）──

    /// <summary>守り方（<see cref="SwordDefense"/>）。既定は練習試合で選んだ守り（足と頭で外す）。</summary>
    public SwordDefense Defense { get; init; } = SwordDefense.Adopted;

    /// <summary>間合いの取り合い（打って離れる）。null なら、遠間で構えて技を 1 つずつ狙って詰める（今の試合の既定）。</summary>
    public HitAndAway? HitAndAway { get; init; }

    /// <summary>狙いの付け方（通り道のずらし方）。</summary>
    public AimTuning Aim { get; init; } = new();

    /// <summary>狙う部位を自分で選ぶか（相手の構えでいちばん空いている部位）。</summary>
    public bool AutoAim { get; init; }

    /// <summary>自分の体を避ける刃の通り道（<see cref="SelfClearance"/>）。null なら技のまま。</summary>
    public SelfClearance? Clearance { get; init; }

    /// <summary>技の時計の進め方（<see cref="SwingPacing"/>）。null なら録った速さのまま。</summary>
    public SwingPacing? Pacing { get; init; }

    /// <summary>技を出し終えてから次を狙うまでの間 [s]。</summary>
    public float Rest { get; init; } = 0.6f;

    /// <summary>
    /// 技の選ばれやすさ（型の名前 → 重み。書いていない技は 1、0 の技は出さない）。null なら全部の技を同じ割合で。型の名前は武器の型（刀: tsuki・kesa・gyaku-kesa・nuki-do・kiriage・harai-men・hiki-men、
    /// ロングソード: oberhau・zornhau・zwerchhau・unterhau・stich）。知らない名前は教育の解決で断る（<see cref="WeaponEducation"/>）。
    /// </summary>
    public IReadOnlyDictionary<string, float>? TechniqueWeights { get; init; }

    /// <summary>これより間合いが短い型は技に入れない [m]（打ち込まずその場で振る型は、剣士同士の間合いの技ではない）。</summary>
    public float MinReach { get; init; } = FormSwordsman.MinReach;

    /// <summary>折れた武器を使い続ける、残った長さの下限 [腕の長さ]（<see cref="DisarmedFallback"/>）。</summary>
    public float KeepReach { get; init; } = DisarmedFallback.DefaultKeepReach;

    // ── こんぼう ──

    /// <summary>振り始める間合い [m]（胸と胸の水平の距離）。</summary>
    public float ClubRange { get; init; } = 0.36f;

    /// <summary>振り終えてから次を探すまでの間 [s]。</summary>
    public float ClubRest { get; init; } = 0.6f;

    /// <summary>打たない間に構えて待つ距離 [m]（相手の拳の届かない所）。</summary>
    public float ClubStandOff { get; init; } = 0.48f;

    /// <summary>振るのを間合いが <see cref="ClubRange"/> ± 3 cm に入ったときだけにするか。</summary>
    public bool ClubExact { get; init; }

    /// <summary>こんぼうの振りの選ばれやすさ（振りの型 Chop・Side・Thrust → 重み。書いていない型は 1）。null なら同じ割合で。</summary>
    public IReadOnlyDictionary<string, float>? ClubSwingWeights { get; init; }

    // この record の既定値（コード側）は、教科書を JSON へ移した時点（LogicVersion 30）までの値のまま。今の教科書の値（Pacing を入れる・こんぼうの間合い 0.30 m など）は
    // Bots/weapons/*.json が持つ（WeaponCatalog.Textbook）。new WeaponStyle() は「何も鍛えていない昔の値」。

    /// <summary>範囲の検査をして、検査済みの自分を返す。範囲の外・有限でない数は <see cref="ArgumentOutOfRangeException"/>（どの値かを書く）。</summary>
    public WeaponStyle Validated()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentOutOfRangeException(nameof(Name), "名前が空です。");
        }

        if (Defense is null || Aim is null)
        {
            throw new ArgumentOutOfRangeException(nameof(Defense), "Defense・Aim は null にできません。");
        }

        _ = Defense.Validated();
        _ = HitAndAway?.Validated();
        _ = Aim.Validated();
        if (Clearance is { } clearance)
        {
            Check(clearance);
        }

        if (Pacing is { } pacing)
        {
            Check(pacing);
        }

        InRange(Rest, 0f, 5f, nameof(Rest));
        InRange(MinReach, 0f, 1.5f, nameof(MinReach));
        InRange(KeepReach, 0f, 3f, nameof(KeepReach));
        InRange(ClubRange, 0.1f, 1.2f, nameof(ClubRange));
        InRange(ClubRest, 0f, 5f, nameof(ClubRest));
        InRange(ClubStandOff, 0.1f, 2f, nameof(ClubStandOff));
        CheckWeights(TechniqueWeights, nameof(TechniqueWeights), null);
        CheckWeights(ClubSwingWeights, nameof(ClubSwingWeights), [.. ClubBot.OneHanded.Select(f => f.ToString())]);
        return this;
    }

    /// <summary>重みの検査: 0〜100 の有限な数。<paramref name="names"/> があれば、キーはその中の名前だけ（書いた分の合計が全部 0 にならないことも）。</summary>
    private static void CheckWeights(IReadOnlyDictionary<string, float>? weights, string field, IReadOnlyList<string>? names)
    {
        if (weights is null)
        {
            return;
        }

        foreach (var (key, value) in weights)
        {
            if (names is not null && !names.Contains(key))
            {
                throw new ArgumentOutOfRangeException(field, key, $"{field} の鍵 '{key}' を知りません（{string.Join(", ", names)}）。");
            }

            InRange(value, 0f, 100f, $"{field}.{key}");
        }

        if (names is not null && names.Sum(n => weights.GetValueOrDefault(n, 1f)) <= 0f)
        {
            throw new ArgumentOutOfRangeException(field, "全部の重みが 0 では何も振れません。");
        }
    }

    private static void Check(SelfClearance c)
    {
        InRange(c.Margin, 0f, 0.5f, "Clearance.Margin");
        InRange(c.FromGrip, 0f, 1.5f, "Clearance.FromGrip");
        InRange(c.MaxTilt, 0f, 1.6f, "Clearance.MaxTilt");
    }

    private static void Check(SwingPacing p)
    {
        InRange(p.GripSlack, 0.02f, 2f, "Pacing.GripSlack");
        InRange(p.AngleSlack, 0.05f, 3f, "Pacing.AngleSlack");
        InRange(p.Floor, 0.01f, 1f, "Pacing.Floor");
    }

    internal static void InRange(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} の数にしてください。");
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>JSON から読む。知らない鍵は <see cref="JsonException"/>、範囲の外は <see cref="ArgumentOutOfRangeException"/>。</summary>
    public static WeaponStyle FromJson(string json)
        => (JsonSerializer.Deserialize<WeaponStyle>(json, ReadOptions) ?? throw new JsonException("空の武器のスタイルです。")).Validated();

    internal static readonly JsonSerializerOptions ReadOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
}

/// <summary>
/// 武器ごとの教科書（組み込みの JSON を読んだ <see cref="WeaponStyle"/>）。ボットを使う武器だけ（刀・ロングソード・こんぼう・棒。鉄の剣・はめる物は無い）。
/// </summary>
public static class WeaponCatalog
{
    private static readonly Lazy<WeaponStyle> Katana = new(() => Load("katana"));
    private static readonly Lazy<WeaponStyle> Longsword = new(() => Load("longsword"));
    private static readonly Lazy<WeaponStyle> Club = new(() => Load("club"));
    private static readonly Lazy<WeaponStyle> Staff = new(() => Load("staff"));
    private static readonly Lazy<WeaponStyle> Jo = new(() => Load("jo"));
    private static readonly Lazy<WeaponStyle> SwordShield = new(() => Load("swordshield"));
    private static readonly Lazy<WeaponStyle> Bat = new(() => Load("bat"));

    /// <summary>この武器のボットを教科書の値で動かすか（<see cref="Textbook"/> がある武器）。</summary>
    public static bool HasBot(WeaponKind? weapon) => weapon is WeaponKind.Katana or WeaponKind.Longsword or WeaponKind.WoodenClub or WeaponKind.Staff or WeaponKind.Jo or WeaponKind.IronSword or WeaponKind.Bat;

    /// <summary>この武器の教科書。ボットの無い武器は <see cref="ArgumentOutOfRangeException"/>。</summary>
    public static WeaponStyle Textbook(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Katana => Katana.Value,
        WeaponKind.Longsword => Longsword.Value,
        WeaponKind.WoodenClub => Club.Value,
        WeaponKind.Staff => Staff.Value,
        WeaponKind.Jo => Jo.Value,
        WeaponKind.IronSword => SwordShield.Value,
        WeaponKind.Bat => Bat.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(weapon), weapon, "刀・ロングソード・こんぼう・棒・片手剣（剣盾）だけに教科書があります。"),
    };

    private static WeaponStyle Load(string name)
    {
        var resource = $"MiniRing.Bots.weapons.{name}.json";
        using var stream = typeof(WeaponCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"組み込みの武器の教科書 {resource} が見つかりません（MiniRing.csproj の EmbeddedResource）。");
        using var reader = new StreamReader(stream);
        return WeaponStyle.FromJson(reader.ReadToEnd());
    }
}

/// <summary>
/// 武器の教育の解決（<see cref="EducationResolver"/> と同じ重ね方）: 武器の教科書の JSON に <c>education.weaponBot</c> を鍵ごとに（入れ子は再帰で）重ね、
/// <see cref="WeaponStyle"/> に読み直して検査する。名前（<c>Name</c>）は教育で変えられない。最上位の鍵が教科書に無ければ（綴りの誤り）止める。
/// 重なる層が無ければ教科書そのもの（同じ物）を返す。
/// </summary>
public static class WeaponEducation
{
    /// <summary>
    /// <paramref name="weapon"/> の教科書に <paramref name="education"/> を重ねた武器のスタイル。武器にボットが無ければ（拳・鉄の剣・はめる物）null。
    /// 壊れた教育は <see cref="InvalidDataException"/>（どの鍵かを書く）。
    /// </summary>
    public static WeaponStyle? Resolve(WeaponKind? weapon, JsonObject? education)
    {
        if (!WeaponCatalog.HasBot(weapon))
        {
            if (education is { Count: > 0 })
            {
                throw new InvalidDataException("education.weaponBot は、刀・ロングソード・こんぼう・棒・片手剣を握るキャラだけが持てます。");
            }

            return null;
        }

        var textbook = WeaponCatalog.Textbook(weapon!.Value);
        if (education is not { Count: > 0 })
        {
            return textbook;
        }

        var node = JsonNode.Parse(textbook.ToJson())!.AsObject();
        foreach (var key in education.Select(p => p.Key))
        {
            if (key == nameof(WeaponStyle.Name))
            {
                throw new InvalidDataException("education.weaponBot.Name は教育で変えられません。");
            }

            if (!node.ContainsKey(key))
            {
                throw new InvalidDataException($"education.weaponBot.{key}: 武器の教科書にこの鍵はありません（綴りの誤りかもしれません）。");
            }
        }

        EducationResolver.Merge(node, education);
        try
        {
            var parsed = JsonSerializer.Deserialize<WeaponStyle>(node.ToJsonString(), WeaponStyle.ReadOptions) ?? throw new InvalidDataException("空の武器のスタイルになりました。");
            var resolved = (parsed with { Name = textbook.Name }).Validated();
            FormSwordsman.CheckTechniqueNames(weapon, resolved.TechniqueWeights);
            return resolved;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidDataException($"education.weaponBot を武器の教科書 '{textbook.Name}' に重ねられません: {ex.Message}", ex);
        }
    }
}
