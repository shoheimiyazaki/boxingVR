using System.Globalization;
using System.Text.Json;

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
    private const float SnapshotEvery = 5f;

    private float _nextSnapshot;
    private bool _bothDown;

    /// <summary>試合の外側から分かる出来事（技の名前・守りの名前など、ボットの頭の中から）を記録に足す。</summary>
    public void Note(float at, string type, string who, string text) => Log(at, type, who, text);

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
        }

        if (game.MatchRound != _round && !game.InBreak && !game.MatchOver)
        {
            _round = game.MatchRound;
            Log(at, "round_start", null, $"第{_round}ラウンド開始");
            React(CrowdReaction.Cheer, 0.4f, at, step, force: true);
        }

        if (game.InBreak && !_inBreak)
        {
            Log(at, "round_end", null, $"第{game.MatchRound}ラウンド終了。{Score(game)}");
            React(CrowdReaction.Cheer, 0.5f, at, step, force: true);
        }

        _inBreak = game.InBreak;
        if (game.MatchOver && !_over)
        {
            _over = true;
            var how = game.Decision ?? game.Message;
            Log(at, game.Decision is null ? "ko" : "decision", null, $"{how}。{Score(game)}");
            React(CrowdReaction.Eruption, 1f, at, step, force: true);
        }

        if (at >= _nextSnapshot && !game.MatchOver)
        {
            _nextSnapshot = at + SnapshotEvery;
            Log(at, "state", null, $"{State(game, game.Player)} ／ {State(game, game.Cpu)}");
        }

        foreach (var hit in game.RecentHits)
        {
            var who = NameOf(game, hit.Attacker);
            var victim = hit.Victim;
            Log(at, "hit", who, string.Create(CultureInfo.InvariantCulture,
                $"{who} の {(hit.Hand == Hand.Left ? "左" : "右")} が {NameOf(game, victim)} の{ZoneName(hit.Zone)}に（ダメージ {hit.Damage:0}、拳の速さ {hit.Speed:0.0} m/s、" +
                $"乗った質量 {hit.ImpactMass:0.0} kg・うち体重 {hit.BodyShare * 100f:0}%、受けた側の意識 {victim.Consciousness:0.00}・腹 {victim.Core:0.00}・息 {victim.Breath:0.00}・スタミナ {victim.Stamina:0.00}）"));
            if (hit.Damage >= RoarDamage)
            {
                React(CrowdReaction.Roar, MathF.Min(1f, hit.Damage / 40f), at, step, force: true);
            }
            else if (hit.Damage >= CheerDamage)
            {
                React(CrowdReaction.Cheer, hit.Damage / RoarDamage, at, step);
            }
        }

        foreach (var _ in game.RecentGrazes)
        {
            if (at - _lastOoh > 2.5f)
            {
                _lastOoh = at;
                React(CrowdReaction.Ooh, 0.5f, at, step);
            }
        }

        foreach (var _ in game.RecentSevers)
        {
            Log(at, "sever", null, "腕か脚が切り落とされた");
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
                Log(at, "knockdown", name, $"{name} ダウン。{DownCause(fighter)}");
                React(CrowdReaction.Roar, 0.8f, at, step, force: true);
            }

            if (!down && before.Down && !game.MatchOver && !game.Resting)
            {
                Log(at, "get_up", name, $"{name} が立ち上がった（カウント {before.Count}）");
                React(CrowdReaction.Cheer, 0.6f, at, step, force: true);
            }

            if (down && count > before.Count && count >= 1 && !game.RoundOver && !game.Resting)
            {
                Log(at, "count", name, $"カウント {count}");
                React(CrowdReaction.Count, 0.5f + (count * 0.05f), at, step, force: true);
            }

            if (tired && !before.Tired)
            {
                Log(at, "tired", name, $"{name} のスタミナが尽きかけている（{fighter.Stamina:0.00}）");
            }

            if (gut && !before.Gut)
            {
                Log(at, "body_hurt", name, $"{name} にボディが効いている（腹の働き {fighter.Core:0.00}、膝が落ちる）");
                React(CrowdReaction.Groan, 0.6f, at, step);
            }

            _seen[fighter] = (down, count, tired, gut);
        }

        var bothDown = game.Player.IsKnockedDown && game.Cpu.IsKnockedDown && !game.Resting;
        if (bothDown && !_bothDown)
        {
            Log(at, "both_down", null, "両者が同時に倒れている（打ち合いの相打ち・足がもつれた）。カウントは両者に入り、先に立った方が続ける。倒れた側は拳を出さず両手を床に着く（体の反射）");
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
            ["rules"] = RulesSheet,
            ["events"] = _events,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 解説（ゲームの仕組みに詳しい人）のための、この試合の決まりと仕組みの要点。台本を書く人が、画面で起きる「よく分からないこと」を
    /// 説明できるように（倒れている同士の動き・その場の K.O.・休みの間の再生など）。
    /// </summary>
    private static readonly string[] RulesSheet =
    [
        "体は目標をバネで追う人形（アニメーションは無い）。拳の重さは体の軸と力の向きの関係（体重が乗る割合）と速さで決まる。",
        "意識 = 脳の働き − 脳しんとう。脳しんとうは頭が首の上でねじられた角速度の 2 乗（あご・こめかみを振り抜くと大きい）。脳が 10% 削られるごとに脳の働きは半分。",
        "意識が 0.75 を切ると体の張りが抜け始め、0.3 を切ると膝をつき、0.12 で床に伸びる。腹（肝臓・腸）が効くと意識があっても膝が落ちる（リバーブロー）。",
        "ボディの打撃は皮膚の上からでも臓器に届く（打撲 = 一時的）。胸（心臓・肺）を打たれると息が戻らずスタミナが回復しない。",
        "スタミナは筋肉の仕事で減り、心肺で戻る。0.6 を切ると拳を運ぶ力が落ち、パンチが遅く軽くなる。",
        "倒れた側は拳を出さず両手を床に着いて起き上がろうとする。意識が少しでもあれば体は動く。完全に意識が無く 4 秒動かなければ、10 カウントを待たずその場で K.O.。",
        "立っている側は相手が倒れている間は打たずに下がる。両者が同時に倒れることもある（相打ち）。",
        "ラウンドの間の 10 秒で、削れた体は一定の速さで内側から治る（ゲームのルール）。倒れたまま休みに入った者は、次のラウンドの始めに数え直し。",
        "狙いどころ（あご・こめかみ・露出した脳・肝臓・みぞおち・心臓・ガードの腕）はボットのスタイルごとの好みで選ぶ。体重が乗る範囲（本来の狙いから腕の長さの 0.4 倍・28° まで）でしか狙いを動かさない。",
    ];

    private string State(BoxingGame game, Fighter f)
        => string.Create(CultureInfo.InvariantCulture,
            $"{NameOf(game, f)}: スタミナ {f.Stamina:0.00}・意識 {f.Consciousness:0.00}・腹 {f.Core:0.00}・息 {f.Breath:0.00}・受けたダメージ {f.Condition.TotalDamage:0}");

    /// <summary>倒れた理由の見立て（仕組みの言葉で）: 意識が飛んだ・腹が効いた・その両方・足がもつれた（意識も腹も保っている）。</summary>
    private static string DownCause(Fighter f)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"意識 {f.Consciousness:0.00}（脳しんとう {f.Concussion:0.00}）、腹 {f.Core:0.00}、体の張り {f.Tone:0.00}、スタミナ {f.Stamina:0.00}。");
        return text + (f.Consciousness < 0.3f && f.Core < 0.6f ? "見立て: 頭と腹の両方が効いている"
            : f.Consciousness < 0.3f ? "見立て: 頭がねじられて意識が飛んだ"
            : f.Core < 0.6f ? "見立て: 腹（肝臓）が効いて、意識はあるのに膝が落ちた"
            : f.Tone < 0.75f ? "見立て: 意識が薄れて体の張りが抜けた"
            : "見立て: 意識も腹も保っている。押し合い・体勢の崩れで倒れた（スリップに近い）");
    }

    private void React(CrowdReaction reaction, float intensity, float at, int step, bool force = false)
    {
        if (!force && at - _lastReaction < ReactionGap)
        {
            return;
        }

        _lastReaction = at;
        _cues.Add(new CrowdCue(at, reaction, intensity, step));
    }

    private void Log(float at, string type, string? who, string text)
        => _events.Add(new Dictionary<string, object> { ["t"] = MathF.Round(at, 2), ["type"] = type, ["who"] = who ?? "", ["text"] = text });

    private string NameOf(BoxingGame game, Fighter fighter) => fighter == game.Player ? redName : blueName;

    private string Score(BoxingGame game)
        => string.Create(CultureInfo.InvariantCulture, $"与えたダメージ {redName} {game.Cpu.Condition.TotalDamage:0} - {game.Player.Condition.TotalDamage:0} {blueName}");

    private static string ZoneName(HitZone zone) => zone switch
    {
        HitZone.Chin => "あご",
        HitZone.Temple => "こめかみ",
        HitZone.Head => "顔",
        HitZone.Neck => "首",
        HitZone.Heart => "心臓の上",
        HitZone.Liver => "レバー",
        HitZone.SolarPlexus => "みぞおち",
        HitZone.Body => "ボディ",
        HitZone.Shoulder => "肩",
        HitZone.Elbow => "肘",
        HitZone.Guard => "ガードの上",
        HitZone.Knee => "膝",
        _ => "脚",
    };
}
