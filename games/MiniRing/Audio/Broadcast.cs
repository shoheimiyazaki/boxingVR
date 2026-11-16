using System.Text.Json;
using MiniRing.Voxels;

namespace MiniRing.Audio;

/// <summary>会場の反応 1 つ: 映像（または実時間）の時刻・種類・強さ・揺らぎの種。</summary>
public readonly record struct CrowdCue(float At, CrowdReaction? Reaction, float Intensity, int Seed);

/// <summary>
/// テレビ中継風のモード（動画 <c>video --tv on</c>・デスクトップ <c>--tv</c>）の、試合の外側の演出: 会場の歓声と、実況・解説を書くための出来事の記録。
/// 試合を見て反応するだけで、試合（物理・記録）には触れない。
/// <list type="bullet">
/// <item>歓声: 会場のざわめきをずっと流し、出来事に反応を重ねる。重い 1 発は歓声（重いほど大きく）、ダウンはどよめき、カウントの間は
/// 1 秒ごとの掛け声、K.O. と判定は大歓声、紙一重で外れた拳は「おぉ」、部位が飛べば悲鳴まじりのどよめき、ラウンドの始まりと終わりは拍手。
/// 歓声で何が起きたかが分かるように、反応の大きさと形を出来事ごとに変える。</item>
/// <item>出来事の記録: 映像の時刻つきで、打撃（誰が・どこへ・どれだけ）・ダウン・カウント・K.O.・判定・ラウンドの始まりと終わり・
/// スタミナ切れ・腹が効いた・部位が飛んだ、を JSON に書く。実況と解説の台本はこれを見て書く（tools/commentary）。</item>
/// </list>
/// </summary>
/// <param name="redName">赤コーナーの呼び名（リングネーム）。</param>
/// <param name="blueName">青コーナーの呼び名。</param>
/// <param name="redStyle">赤コーナーのスタイルの名前（実況・解説の資料に書く）。</param>
/// <param name="blueStyle">青コーナーのスタイルの名前。</param>
public sealed class Broadcast(string redName, string blueName, string? redStyle = null, string? blueStyle = null)
{
    /// <summary>状態の記録（スタミナ・意識・腹・息・ダメージ）を書く間隔 [映像の s]。解説が「今どうなっているか」を言えるように。</summary>
    private const float SnapshotEvery = 10f;

    private float _nextSnapshot;
    private bool _bothDown;

    /// <summary>
    /// 試合の外側から分かる出来事を記録に足す。技の狙い（technique）は選手の頭の中なので解説へ、それ以外（守り・カメラ・リプレイ）は画面で見えるので実況へ。
    /// </summary>
    public void Note(float at, string type, string who, string text) => Log(at, type, who, text, type == "technique" ? Analyst : Announcer);

    /// <summary>赤コーナーの使う物の呼び名（「日本刀」「こんぼう」。拳なら null）。武器の当たりを実況するときに使う。</summary>
    public string? RedGear { get; init; }

    /// <summary>青コーナーの使う物の呼び名。</summary>
    public string? BlueGear { get; init; }

    /// <summary>赤コーナーの紹介（本名・流派・肩書き。台本の資料）。</summary>
    public string? RedProfile { get; init; }

    /// <summary>青コーナーの紹介。</summary>
    public string? BlueProfile { get; init; }

    /// <summary>赤コーナーの流派・武器の見出し（「ボクシング」「日本刀」。台本の下書きの選手紹介）。</summary>
    public string? RedDiscipline { get; init; }

    /// <summary>青コーナーの流派・武器の見出し。</summary>
    public string? BlueDiscipline { get; init; }

    /// <summary>大会名（台本の資料）。</summary>
    public string? Event { get; init; }

    /// <summary>何試合目か（台本の資料）。</summary>
    public string? Bout { get; init; }

    /// <summary>大会のプログラム（その日の試合の順と済んだ試合の結果。台本の資料）。null なら書かない。</summary>
    public object? Program { get; init; }

    /// <summary>赤コーナーのプロフィール（経歴・人柄・戦績・エピソード。実況・解説の資料。CharacterProfile.Briefing）。無ければ書かない。</summary>
    public string? RedDossier { get; init; }

    /// <summary>青コーナーのプロフィール。</summary>
    public string? BlueDossier { get; init; }

    /// <summary>試合の結果（勝者 red / blue / draw・決まり手・ラウンド。トーナメントの勝ち上がりを決める道具が読む）。決着の後に入れる。</summary>
    public object? Outcome { get; set; }

    /// <summary>出来事を伝える相手: 実況（画面で見えること）。</summary>
    public const string Announcer = "実況";

    /// <summary>出来事を伝える相手: 解説（体の中で起きていること・作戦）。</summary>
    public const string Analyst = "解説";

    /// <summary>実況が言うほどの当たりのダメージ。</summary>
    private const float MentionDamage = 8f;

    /// <summary>解説が「深くえぐられた」と言う、武器が 1 回で削ったセルの数。</summary>
    private const int DeepCut = 30;

    /// <summary>重い 1 発とみなすダメージ（歓声が上がる）。</summary>
    private const float CheerDamage = 12f;

    /// <summary>どよめくほど重い 1 発のダメージ。</summary>
    private const float RoarDamage = 24f;

    /// <summary>同じくらいの反応を重ねない間隔 [映像の s]。</summary>
    private const float ReactionGap = 0.9f;

    private readonly List<Dictionary<string, object>> _events = [];
    private readonly List<CrowdCue> _cues = [];
    private readonly Dictionary<Fighter, (bool Down, int Count, bool Tired, bool Gut)> _seen = [];
    private float _lastReaction = -10f;
    private float _lastOoh = -10f;
    private int _round;
    private bool _inBreak;
    private bool _over;
    private readonly Dictionary<Fighter, VisionState> _vision = [];
    private readonly Dictionary<(Fighter, int), string> _limbs = [];

    /// <summary>これまでの会場の反応（時刻順）。動画は最後に <see cref="MixInto"/> でまとめて混ぜ、ホストは増えた分を鳴らす。</summary>
    public IReadOnlyList<CrowdCue> Cues => _cues;

    /// <summary>出来事の数（ホストが増えた分だけ見るため）。</summary>
    public int EventCount => _events.Count;

    /// <summary>1 歩ごとに試合を見る。<paramref name="at"/> は映像の時刻 [s]、<paramref name="step"/> は歓声の揺らぎの種。</summary>
    public void Observe(BoxingGame game, float at, int step)
    {
        if (!game.MatchOver && _over)
        {
            // 試合が作り直された（デスクトップの R など）: 新しい試合として数え直す。
            _over = false;
            _round = 0;
            _seen.Clear();
            _vision.Clear();
            _limbs.Clear();
        }

        if (game.MatchRound != _round && !game.InBreak && !game.MatchOver)
        {
            _round = game.MatchRound;
            Log(at, "round_start", null, $"第{_round}ラウンド開始", Announcer);
            React(CrowdReaction.Cheer, 0.4f, at, step, force: true);
        }

        if (game.InBreak && !_inBreak)
        {
            Log(at, "round_end", null, $"第{game.MatchRound}ラウンド終了", Announcer);
            Log(at, "score", null, Lead(game), Analyst);
            Log(at, "body", NameOf(game, game.Player), BodyReport(game, game.Player), Analyst);
            Log(at, "body", NameOf(game, game.Cpu), BodyReport(game, game.Cpu), Analyst);
            React(CrowdReaction.Cheer, 0.5f, at, step, force: true);
        }

        _inBreak = game.InBreak;
        if (game.MatchOver && !_over)
        {
            _over = true;
            Log(at, game.Decision is null ? "ko" : "decision", null, Finish(game), Announcer);
            Log(at, "score", null, Lead(game), Analyst);
            Log(at, "body", NameOf(game, game.Player), BodyReport(game, game.Player), Analyst);
            Log(at, "body", NameOf(game, game.Cpu), BodyReport(game, game.Cpu), Analyst);
            React(CrowdReaction.Eruption, 1f, at, step, force: true);
        }

        if (at >= _nextSnapshot && !game.MatchOver && !game.InBreak)
        {
            _nextSnapshot = at + SnapshotEvery;
            Log(at, "condition", null, $"{Condition(game, game.Player)}。{Condition(game, game.Cpu)}", Analyst);
        }

        foreach (var hit in game.RecentHits)
        {
            var who = NameOf(game, hit.Attacker);
            var victim = hit.Victim;
            if (hit.Damage >= MentionDamage)
            {
                var blow = hit.Kick ? "蹴り" : hit.Hand == Hand.Left ? "左" : "右";
                Log(at, "hit", who, $"{who}の{Strength(hit.Damage)}{blow}が、{NameOf(game, victim)}の{ZoneName(hit.Zone)}に入った", Announcer);
                if (Effect(victim, hit.Zone) is { } effect)
                {
                    Log(at, "effect", NameOf(game, victim), effect, Analyst);
                }
            }

            if (hit.Damage >= RoarDamage)
            {
                React(CrowdReaction.Roar, MathF.Min(1f, hit.Damage / 40f), at, step, force: true);
            }
            else if (hit.Damage >= CheerDamage)
            {
                React(CrowdReaction.Cheer, hit.Damage / RoarDamage, at, step);
            }
        }

        foreach (var hit in game.WeaponHits)
        {
            // 握った武器の当たり（刃・棍棒）。実況には何でどこを、解説にはどれだけ深く削れたか。
            var who = NameOf(game, hit.Attacker);
            var gear = GearOf(game, hit.Attacker);
            var part = PartLabel(hit.Victim, hit.Part, hit.Point);
            if (hit.Damage >= MentionDamage || hit.Cells >= DeepCut)
            {
                var verb = gear.Contains("こんぼう", StringComparison.Ordinal) ? "打った" : "斬った";
                Log(at, "weapon_hit", who, $"{who}の{gear}が、{NameOf(game, hit.Victim)}の{ZoneName(hit.Zone)}（{part}）を{verb}", Announcer);
                if (hit.Cells >= DeepCut)
                {
                    Log(at, "effect", NameOf(game, hit.Victim), $"{NameOf(game, hit.Victim)}の{part}の肉が深くえぐられた", Analyst);
                }

                if (Effect(hit.Victim, hit.Zone) is { } effect)
                {
                    Log(at, "effect", NameOf(game, hit.Victim), effect, Analyst);
                }
            }

            if (hit.Damage >= RoarDamage)
            {
                React(CrowdReaction.Roar, MathF.Min(1f, hit.Damage / 40f), at, step, force: true);
            }
            else if (hit.Damage >= CheerDamage)
            {
                React(CrowdReaction.Cheer, hit.Damage / RoarDamage, at, step);
            }
        }

        foreach (var broken in game.BrokenBones)
        {
            var name = NameOf(game, broken.Victim);
            var body = broken.Body >= 0 ? broken.Body : PartId(broken.Victim, broken.Part, broken.Position);
            var part = PartName(broken.Victim, body);
            _limbs[(broken.Victim, body)] = "骨折";
            var limb = broken.Part is BodyPartKind.UpperArm or BodyPartKind.Forearm ? "その腕では、もう強く打てません" : "その脚では、もう踏ん張れません";
            Log(at, "fracture", name, $"{name}の{part}が折れた", Announcer);
            Log(at, "fracture", name, $"{name}の{part}の骨が折れて、肉だけでつながっている。{limb}", Analyst);
            React(CrowdReaction.Groan, 0.8f, at, step, force: true);
        }

        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            if (fighter.Voxels is not { } voxels)
            {
                continue;
            }

            var vision = voxels.Vision;
            var before = _vision.GetValueOrDefault(fighter, VisionState.Healthy);
            foreach (var (label, side, now, was) in new[] { ("左目", "左", vision.Left, before.Left), ("右目", "右", vision.Right, before.Right) })
            {
                if ((now < 0.5f && was >= 0.5f) || (now <= 0.01f && was > 0.01f))
                {
                    Log(at, "eye", NameOf(game, fighter), now <= 0.01f
                        ? $"{NameOf(game, fighter)}の{label}が潰れた。{side}側から来る攻撃が見えていない"
                        : $"{NameOf(game, fighter)}の{label}が傷ついて、{side}側が見えにくくなっている", Analyst);
                }
            }

            _vision[fighter] = vision;
        }

        foreach (var _ in game.RecentGrazes)
        {
            if (at - _lastOoh > 2.5f)
            {
                _lastOoh = at;
                React(CrowdReaction.Ooh, 0.5f, at, step);
            }
        }

        foreach (var sever in game.RecentSevers)
        {
            // 切り落とされた部位（ぶら下がっていた骨折の先が千切れた場合も同じ出来事で来る）。
            if (sever.Victim is { } cut && sever.Body >= 0)
            {
                var key = (cut, sever.Body);
                var torn = _limbs.GetValueOrDefault(key) == "骨折";
                _limbs[key] = "切断";
                var part = PartName(cut, sever.Body);
                Log(at, "severed", NameOf(game, cut), $"{NameOf(game, cut)}の{part}が飛んだ", Announcer);
                Log(at, "severed", NameOf(game, cut), $"{NameOf(game, cut)}は{part}から先を失った{(torn ? "（折れてぶら下がっていたのが千切れた）" : "")}", Analyst);
            }

            React(CrowdReaction.Roar, 1f, at, step, force: true);
            React(CrowdReaction.Groan, 0.8f, at + 0.8f, step + 1, force: true);
        }

        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            var name = NameOf(game, fighter);
            var before = _seen.GetValueOrDefault(fighter);
            var down = fighter.IsKnockedDown; // 審判がダウンを取ったときだけ（一瞬意識が飛んだ中腰・待っている間の中腰は言わない）
            var count = down ? DownCountHud.Count(fighter) : 0;
            var tired = fighter.Stamina < 0.2f;
            var gut = fighter.Core < 0.6f;
            if (down && !before.Down && !game.Resting)
            {
                Log(at, "knockdown", name, $"{name}、ダウン", Announcer);
                Log(at, "knockdown", name, DownCause(name, fighter), Analyst);
                Log(at, "body", name, BodyReport(game, fighter), Analyst);
                React(CrowdReaction.Roar, 0.8f, at, step, force: true);
            }

            if (!down && before.Down && !game.MatchOver)
            {
                // 休みの間に立った場合も書く（書かないと、台本の下書きが次のラウンドを「倒れたまま」で始めてしまう）。
                Log(at, "get_up", name, game.Resting ? $"{name}が休みの間に立ち上がった" : $"{name}が立ち上がった（カウント {before.Count}）", Announcer);
                React(CrowdReaction.Cheer, 0.6f, at, step, force: true);
            }

            if (down && count > before.Count && count >= 1 && !game.RoundOver && !game.Resting)
            {
                Log(at, "count", name, $"カウント {count}", Announcer);
                React(CrowdReaction.Count, 0.5f + (count * 0.05f), at, step, force: true);
            }

            if (tired && !before.Tired)
            {
                Log(at, "tired", name, $"{name}はスタミナが尽きかけている。手数が落ち、パンチが軽くなる", Analyst);
            }

            if (gut && !before.Gut)
            {
                Log(at, "body_hurt", name, $"{name}はボディが効いている。意識はあっても膝が落ちやすい", Analyst);
                React(CrowdReaction.Groan, 0.6f, at, step);
            }

            _seen[fighter] = (down, count, tired, gut);
        }

        var bothDown = game.Player.IsKnockedDown && game.Cpu.IsKnockedDown && !game.Resting;
        if (bothDown && !_bothDown)
        {
            Log(at, "both_down", null, "両者が同時に倒れている。カウントは両者に入り、先に立った方が続ける", Announcer);
        }

        _bothDown = bothDown;
    }

    /// <summary>会場のざわめき（全体）と反応を、音の列 <paramref name="audio"/> に混ぜる。</summary>
    public void MixInto(float[] audio, int sampleRate, int seed)
    {
        var ambience = CrowdSound.Ambience(audio.Length / (float)sampleRate, seed);
        for (var i = 0; i < audio.Length && i < ambience.Length; i++)
        {
            audio[i] += ambience[i];
        }

        foreach (var cue in _cues)
        {
            if (cue.Reaction is not { } reaction)
            {
                continue;
            }

            var samples = CrowdSound.React(reaction, cue.Intensity, cue.Seed);
            var start = (int)(cue.At * sampleRate);
            for (var i = 0; i < samples.Length && start + i < audio.Length; i++)
            {
                if (start + i >= 0)
                {
                    audio[start + i] += samples[i];
                }
            }
        }
    }

    /// <summary>出来事の記録を JSON に書く（{"red":…, "blue":…, "events":[{"t":秒, "type":…, "who":…, "text":…}]}）。</summary>
    public void WriteEvents(string path)
    {
        var doc = new Dictionary<string, object>
        {
            ["red"] = redName,
            ["blue"] = blueName,
            ["redStyle"] = redStyle ?? "",
            ["blueStyle"] = blueStyle ?? "",
            ["redProfile"] = RedProfile ?? "",
            ["blueProfile"] = BlueProfile ?? "",
            ["redDiscipline"] = RedDiscipline ?? "",
            ["blueDiscipline"] = BlueDiscipline ?? "",
            ["event"] = Event ?? "",
            ["bout"] = Bout ?? "",
            ["rules"] = RulesSheet,
            ["events"] = _events.OrderBy(e => (float)e["t"]).ToList(), // 先の時刻で書いた注記（休みのカット割り）も時刻の順に
        };
        if (RedDossier is not null)
        {
            doc["redDossier"] = RedDossier;
        }

        if (BlueDossier is not null)
        {
            doc["blueDossier"] = BlueDossier;
        }

        if (Program is not null)
        {
            doc["program"] = Program;
        }

        if (Outcome is not null)
        {
            doc["outcome"] = Outcome;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 解説（ゲームの仕組みに詳しい人）のための、この試合の決まりと仕組みの要点。台本を書く人が、画面で起きる「よく分からないこと」を
    /// 説明できるように（倒れている同士の動き・その場の K.O.・休みの間の再生など）。
    /// </summary>
    private static readonly string[] RulesSheet =
    [
        "この記録は WVC（Wonderful Voxing Council）の大会のテレビ中継のためのもの。競技の名前は Voxing。",
        "\"for\" が 実況 の出来事は画面で見えること（誰が何でどこを打った・ダウン・カウント・ラウンド）。解説 の出来事は体の中で起きていること（どこが壊れた・何が効いている・疲れ）と選手の狙い。",
        "実況は見えたことを伝えて解説に問いを振り、解説は体の中と作戦の言葉で答える。数字はラウンドと時間と、体の何割が失われたかだけ言う。",
        "倒れて 10 カウントで立てない、または倒れたまま動かなければ K.O.。最終ラウンドが終われば、与えたダメージの多い方の判定勝ち。",
        "ラウンドの間の休みで、浅い傷は少し治る（大会の決まり）。折れた骨・失った手足は戻らない。",
    ];

    /// <summary>
    /// 体の診断（解説の資料）を、ゲームを知らない人にも分かる言葉で: 失った手足・折れた骨、筋肉が削られた部位、臓器が失われた割合と
    /// 効いている度合い、目、意識、スタミナ。目立つものだけを言う（何も無ければ「大きな傷はない」）。
    /// </summary>
    private string BodyReport(BoxingGame game, Fighter f)
    {
        var name = NameOf(game, f);
        if (f.Voxels is not { } v)
        {
            return $"{name}: 大きな傷はない";
        }

        var notes = new List<string>();
        foreach (var (key, state) in _limbs.Where(kv => kv.Key.Item1 == f).Select(kv => (kv.Key, kv.Value)))
        {
            notes.Add(state == "切断" ? $"{PartName(f, key.Item2)}から先を失っている" : $"{PartName(f, key.Item2)}の骨が折れている");
        }

        var p = f.Parts;
        // 剛体の名前の Left/Right は本人の右/左（PartName の注を参照）。
        foreach (var (label, body) in new[] { ("顔と頭", p.Head), ("胸", p.Chest), ("右の上腕", p.LeftUpperArm), ("右の前腕", p.LeftForearm), ("左の上腕", p.RightUpperArm), ("左の前腕", p.RightForearm),
            ("右の太もも", p.LeftThigh), ("左の太もも", p.RightThigh) })
        {
            var lost = 1f - v.Muscle(body);
            if (lost >= 0.25f && !_limbs.ContainsKey((f, body)))
            {
                notes.Add($"{label}の肉が{Tenths(lost)}削られている");
            }
        }

        foreach (var (organ, id) in new[] { ("心臓", BoxerAnatomy.Heart), ("肺", BoxerAnatomy.Lung), ("肝臓", BoxerAnatomy.Liver), ("腸", BoxerAnatomy.Gut), ("脳", BoxerAnatomy.Brain) })
        {
            var lost = v.OrganLoss(id);
            if (lost >= 0.05f)
            {
                notes.Add($"{organ}の{Tenths(lost)}が失われている");
            }

            var bruise = v.BruiseOf(id);
            if (bruise >= 0.3f)
            {
                notes.Add(bruise >= 0.85f ? $"{organ}へのダメージが限界近くまでたまっている" : bruise >= 0.6f ? $"{organ}にかなり効いている" : $"{organ}に効いている");
            }
        }

        foreach (var (side, sight) in new[] { ("左", v.Vision.Left), ("右", v.Vision.Right) })
        {
            if (sight < 0.9f)
            {
                notes.Add(sight <= 0.01f ? $"{side}目が潰れて見えない" : sight < 0.5f ? $"{side}目がほとんど見えていない" : $"{side}目が腫れて見えにくい");
            }
        }

        if (f.Consciousness < 0.5f)
        {
            notes.Add(f.Consciousness < 0.15f ? "意識がない" : "意識がもうろうとしている");
        }

        notes.Add(f.Stamina < 0.25f ? "スタミナはほぼ尽きている" : f.Stamina < 0.5f ? "疲れが見える" : "スタミナはまだある");
        return $"{name}: {(notes.Count == 1 ? "大きな傷はない。" : "")}{string.Join("。", notes)}";
    }

    /// <summary>割合を「3割」「半分」「ほぼすべて」のような言葉に。</summary>
    private static string Tenths(float fraction) => fraction >= 0.95f ? "ほぼすべて" : fraction is >= 0.45f and < 0.55f ? "半分"
        : (int)MathF.Round(fraction * 10f) is var n && n <= 0 ? "1割近く" : $"{n}割";

    /// <summary>当たりの強さの言葉（ダメージの数字は言わない）。</summary>
    public static string Strength(float damage) => damage >= 30f ? "強烈な" : damage >= 20f ? "重い" : damage >= 12f ? "いい" : "";

    /// <summary>その一撃で体の中に起きたこと（解説の言葉）。目立つときだけ。</summary>
    private static string? Effect(Fighter victim, HitZone zone)
    {
        return zone switch
        {
            HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck when victim.Consciousness < 0.7f => "頭が揺れて、意識が飛びかけている",
            HitZone.Liver or HitZone.SolarPlexus or HitZone.Body when victim.Core < 0.6f => "ボディが効いて、足が止まりかけている",
            HitZone.Heart when victim.Breath < 0.6f => "胸を打たれて息が詰まり、スタミナが戻らなくなっている",
            _ => null,
        };
    }

    /// <summary>今の様子（解説の資料）: 疲れ・意識・ボディの効き。</summary>
    private string Condition(BoxingGame game, Fighter f)
    {
        var parts = new List<string>
        {
            f.Stamina < 0.25f ? "スタミナがほぼ尽きている" : f.Stamina < 0.5f ? "疲れが見える" : "まだ動ける",
        };
        if (f.Consciousness < 0.7f)
        {
            parts.Add("頭が効いてふらついている");
        }

        if (f.Core < 0.7f)
        {
            parts.Add("ボディが効いている");
        }

        return $"{NameOf(game, f)}は{string.Join("、", parts)}";
    }

    /// <summary>倒れた理由の見立て（解説の言葉）。</summary>
    private static string DownCause(string name, Fighter f)
        => f.Consciousness < 0.3f && f.Core < 0.6f ? $"{name}は頭とボディの両方が効いている"
            : f.Consciousness < 0.3f ? $"{name}は頭を揺らされて、意識が飛んでいる"
            : f.Core < 0.6f ? $"{name}はボディが効いて、意識はあるのに足が言うことをきかない"
            : f.Tone < 0.75f ? $"{name}は意識が薄れて、体に力が入らない"
            : $"{name}は足がもつれただけ。ダメージは少ない";

    /// <summary>ここまでの優劣（解説の言葉。与えたダメージの比で）。</summary>
    private string Lead(BoxingGame game)
    {
        var red = game.Cpu.Condition.TotalDamage;
        var blue = game.Player.Condition.TotalDamage;
        var (ahead, behind, more, less) = red >= blue ? (redName, blueName, red, blue) : (blueName, redName, blue, red);
        var ratio = more / MathF.Max(1f, less);
        return ratio < 1.15f ? "ここまでの打ち合いは、ほぼ互角" : ratio < 2f ? $"ここまでは{ahead}がやや優勢" : $"ここまでは{ahead}が{behind}を大きく上回っている";
    }

    /// <summary>決着の言葉（実況）。</summary>
    private string Finish(BoxingGame game)
    {
        if (game.Decision is { } decision)
        {
            return decision == "DRAW" ? "判定、引き分け" : $"判定で{(game.PlayerWins > game.CpuWins ? redName : blueName)}の勝ち";
        }

        var winner = game.PlayerWins > game.CpuWins ? redName : blueName;
        var message = game.Message.Split("  ")[0];
        var how = message switch
        {
            "10 COUNT K.O." => "10カウントのノックアウト",
            "K.O. — OUT COLD" => "倒れたまま動けずノックアウト",
            "RING OUT" => "リングの外へ落ちて決着",
            _ when message.Contains("HEART", StringComparison.Ordinal) => "心臓が止まってノックアウト",
            _ when message.Contains("BRAIN", StringComparison.Ordinal) => "脳が壊れてノックアウト",
            _ => "ノックアウト",
        };
        return $"{how}。{winner}の勝ち";
    }

    private string GearOf(BoxingGame game, Fighter f) => (f == game.Player ? RedGear : BlueGear) ?? "武器";

    /// <summary>
    /// 剛体 id の部位の名前（本人から見た左右つき）。Aigis の部位名は −X 側が "Left" で、+Z を向いた体では −X が本人の右なので、
    /// 名前の Left/Right を入れ替えて言う（腕の対応 <c>Fighter.Arm.Create</c>・脚の <c>Fighter.KickingMass</c> と同じ）。
    /// </summary>
    public static string PartName(Fighter f, int body)
    {
        var p = f.Parts;
        return body == p.Head ? "頭" : body == p.Chest ? "胸" : body == p.Pelvis ? "腰"
            : body == p.LeftUpperArm ? "右上腕" : body == p.RightUpperArm ? "左上腕" : body == p.LeftForearm ? "右前腕" : body == p.RightForearm ? "左前腕"
            : body == p.LeftThigh ? "右太もも" : body == p.RightThigh ? "左太もも" : body == p.LeftShin ? "右すね" : body == p.RightShin ? "左すね"
            : body == p.LeftFoot ? "右足" : body == p.RightFoot ? "左足" : "手足";
    }

    /// <summary>部位の種類と位置から、左右の剛体 id を決める（近い方）。</summary>
    private static int PartId(Fighter f, BodyPartKind kind, System.Numerics.Vector3 point)
    {
        var p = f.Parts;
        var (left, right) = kind switch
        {
            BodyPartKind.UpperArm => (p.LeftUpperArm, p.RightUpperArm),
            BodyPartKind.Forearm => (p.LeftForearm, p.RightForearm),
            BodyPartKind.Thigh => (p.LeftThigh, p.RightThigh),
            BodyPartKind.Shin => (p.LeftShin, p.RightShin),
            BodyPartKind.Foot or BodyPartKind.Toe => (p.LeftFoot, p.RightFoot),
            BodyPartKind.Head => (p.Head, p.Head),
            BodyPartKind.Chest => (p.Chest, p.Chest),
            _ => (p.Pelvis, p.Pelvis),
        };
        if (left == right)
        {
            return left;
        }

        var world = f.World;
        return System.Numerics.Vector3.DistanceSquared(world.GetState(left).Position, point) <= System.Numerics.Vector3.DistanceSquared(world.GetState(right).Position, point) ? left : right;
    }

    private static string PartLabel(Fighter f, BodyPartKind kind, System.Numerics.Vector3 point) => PartName(f, PartId(f, kind, point));

    private void React(CrowdReaction reaction, float intensity, float at, int step, bool force = false)
    {
        if (!force && at - _lastReaction < ReactionGap)
        {
            return;
        }

        _lastReaction = at;
        _cues.Add(new CrowdCue(at, reaction, intensity, step));
    }

    private void Log(float at, string type, string? who, string text, string role)
        => _events.Add(new Dictionary<string, object> { ["t"] = MathF.Round(at, 2), ["for"] = role, ["type"] = type, ["who"] = who ?? "", ["text"] = text });

    private string NameOf(BoxingGame game, Fighter fighter) => fighter == game.Player ? redName : blueName;

    /// <summary>当たった所の日本語の名前（実況の資料・リプレイの説明）。</summary>
    public static string ZoneName(HitZone zone) => zone switch
    {
        HitZone.Chin => "あご",
        HitZone.Temple => "こめかみ",
        HitZone.Head => "顔面",
        HitZone.Neck => "首",
        HitZone.Heart => "心臓の上",
        HitZone.Liver => "レバー（右わき腹）",
        HitZone.SolarPlexus => "みぞおち",
        HitZone.Body => "ボディ",
        HitZone.Shoulder => "肩",
        HitZone.Elbow => "肘",
        HitZone.Guard => "ガードの上",
        HitZone.Knee => "膝",
        _ => "脚",
    };
}
