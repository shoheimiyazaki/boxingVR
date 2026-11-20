using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Arena.Shows.Wvc;
using MiniRing.Audio;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// ボット同士の試合を mp4 にする（GPU のヘッドレス描画 → ffmpeg）。
/// 構成: タイトル → 試合（技・守り・クリーンヒットの字幕、打撃音つき）→ 結果。
/// 試合は種で決まるので、同じ引数なら同じ動画になる。
/// </summary>
/// <param name="width">映像の幅 [px]。</param>
/// <param name="height">映像の高さ [px]。</param>
/// <param name="fps">毎秒のコマ数。</param>
/// <param name="breakStyle">ボクセルが砕ける音の方式。</param>
/// <param name="hitSounds">パンチが当たる音を入れるか。</param>
/// <param name="speed">試合の速さ（<see cref="GameSpeed"/>。標準 0.75 倍）。試合そのものを遅く進めて撮るので、映像は滑らかなまま遅くなる
/// （出来た動画を引き伸ばすのと違い、コマが重複しない）。音は映像の時刻に置き、音そのものの速さ・高さは変えない。</param>
/// <param name="tv">テレビ中継風（<see cref="MatchRules.Tv"/> の 30 秒 × 5 ラウンド、得点表と時計、会場の歓声、実況用の出来事の記録 *.events.json）。</param>
/// <param name="faceCam">試合中、画面の両脇に両コーナーの顔の窓を出す（<see cref="FaceCam"/>）。null なら出さない。</param>
/// <param name="tvRounds">テレビ中継のラウンドの数（1 ラウンドは <see cref="MatchRules.Tv"/> の 30 秒のまま）。</param>
/// <param name="tvEvent">テレビ中継の冒頭に出す大会名。</param>
/// <param name="tvBout">テレビ中継の冒頭に出す「第 1 試合」などの札（空なら出さない）。</param>
/// <param name="tvBreak">テレビ中継のラウンドの間の休み [s]（休みの間に治る量は標準と同じ）。</param>
/// <param name="render">
/// false なら描かない下回し: <see cref="HeadlessRenderer"/>・ffmpeg・音を作らず、試合と演出（<see cref="TvDirector"/>・リプレイの影の試合・<see cref="Broadcast"/>）だけを同じに回して
/// *.events.json だけを書く（コマは数えるだけ。物理の速さで終わる）。mp4・wav・タイムラインは書かない。
/// </param>
/// <param name="tvProgram">大会のプログラム。あればテレビ中継の冒頭のタイトルの前に「本日のプログラム」の表を出し、記録（*.events.json）にも書く。</param>
/// <param name="org">主催団体（団体のファイル。null なら WVC＝今の中継）。紋章・画面の色と名前・ラウンドの合図の音・歓声の大きさ・台本の資料の決まりを選ぶ。試合の決まり・場の形・照明は選ばない（別の仕組み）。</param>
/// <param name="belt">王座のベルトが懸かった試合か（勝敗の画面が勝者の名前の上にベルトを出す番組の型だけが使う。引き分けでは出さない）。</param>
/// <param name="live">
/// 生配信（<see cref="LiveOutput"/>）: mp4 の代わりに実時間で描いて URL へ流す。音は置いたそばから書き出す。events.json は書く（下回しと比べるため）。
/// <paramref name="render"/> が false とは一緒に使えない。
/// </param>
internal sealed class VideoMaker(int width, int height, int fps, BreakStyle breakStyle = VoxelBreakSound.DefaultStyle, bool hitSounds = true, FaceCam? faceCam = null, float speed = GameSpeed.Standard, bool tv = false,
    int tvRounds = 5, string tvEvent = "ファイトナイト", string tvBout = "", float tvBreak = 10f, bool render = true, LiveOptions? live = null, TvProgram? tvProgram = null, Organization? org = null, bool belt = false)
{
    private readonly Organization _org = org ?? Organization.Default;

    private readonly bool _off = live is not null && !render
        ? throw new ArgumentException("--live と --render off は一緒に使えません（生配信は描かないと流せません）。")
        : !render;

    /// <summary>
    /// テレビ中継の決まり。休みの長さを変えても、休みの間に治る量（速さ × 長さ）は標準（10 秒 × 150 個/秒）と同じにする（休みを長くした分だけ多く治らないように）。
    /// </summary>
    private readonly MatchRules? _rules = tv ? TvShowRules.Rules(tvRounds, tvBreak, org ?? Organization.Default) : null;

    /// <summary>
    /// 顔の窓。テレビ中継では番組の型（<see cref="ITvShow.Arrange"/>）が置き場を決める（WVC は左下・右下で、窓の下に名札とスタミナのメーター）。
    /// それ以外は渡されたまま（両脇の中ほど）。
    /// </summary>
    private readonly FaceCam? _faceCam = tv && faceCam is not null ? ShowFormats.Create(width, height, org ?? Organization.Default).Arrange(faceCam, width, height) : faceCam;

    /// <summary>試合を 1 歩進める時間の上限 [s]（ホストの 1 歩と同じ）。速さを落とすと 1 歩はこれより短くなる。</summary>
    private const float MaxStepDt = 1f / 60f;
    private float TitleSeconds => tv ? _scenes.Sum(s => s.Seconds) : 3.5f;

    /// <summary>試合の前の番組の場面の並び（団体の番組の型が決める。テレビ中継だけ。Make の始めに決める）。</summary>
    private IReadOnlyList<ShowScene> _scenes = [];

    /// <summary>決着の画面の長さ [s]（番組の型が決める。Make の始めに決める）。</summary>
    private float _resultSeconds = 9f;

    /// <summary>字幕（コンビネーション・守り）を置く高さ [px]（番組の型が決める。Make の始めに決める）。</summary>
    private float _captionY;

    private float ResultSeconds => tv ? _resultSeconds : 5f;

    /// <summary>K.O. の後、結果へ進むまでに撮る長さ [s]。ゲームが次のラウンドを始める（3.5 s）より短く。</summary>
    private const float AftermathSeconds = 3f;

    /// <summary>字幕に出す「重い一撃」のダメージ（1 発でクリーンヒットと言える程度）。</summary>
    private const float BigHitDamage = 15f;

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Blue = new(0.5f, 0.68f, 1f, 1f);

    /// <summary>見どころの一覧（映像の秒・試合の秒・種類・主役・強さ・中身）とまとめ。</summary>
    private static string HighlightsText(MiniRing.Highlights.MatchHighlights highlights, float titleSeconds, float speed)
    {
        var sb = new System.Text.StringBuilder("# 映像の秒  試合の秒  種類  主役（赤/青）  強さ  中身\n");
        foreach (var e in highlights.Events.OrderBy(e => e.Time))
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{titleSeconds + (e.Time / speed),7:0.00}  {e.Time,6:0.00}  {e.Kind,-10} {(e.Red ? "赤" : "青")}  {e.Intensity:0.00}  {e.Note}\n");
        }

        return sb.Append(highlights.Summarize().Line()).Append('\n').ToString();
    }

    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);

    /// <param name="red">赤コーナー（プレイヤー側）。</param>
    /// <param name="blue">青コーナー（CPU 側）。</param>
    /// <param name="seed">試合の種。</param>
    /// <param name="seconds">試合の長さ [s]。</param>
    /// <param name="outPath">書き出す mp4 のパス。</param>
    /// <param name="log">進み具合の書き出し先。</param>
    /// <param name="afterStep">試合を 1 歩進めるたびに呼ぶ（時刻 [s]）。ドリルの合図などに使う。</param>
    /// <param name="untilKo">
    /// K.O.（臓器が壊れる・10 カウント・リングアウト）で試合を終える。<paramref name="seconds"/> は打ち切りの上限になり、
    /// 上の数字は残り時間でなく経過時間、K.O. の後 <see cref="AftermathSeconds"/> 秒の倒れた様子まで撮って結果へ進む。
    /// </param>
    /// <param name="redWear">赤コーナーが手にはめる物（既定 グローブ）。</param>
    /// <param name="blueWear">青コーナーが手にはめる物（既定 グローブ）。</param>
    /// <param name="intentScript">赤の操作台本。指定時は指令を記録し、ボットの代わりに意図操作を使う。</param>
    /// <param name="setup">試合を作った直後に呼ぶ（目を潰しておくなど）。</param>
    /// <param name="redWeapon">赤コーナーが握る武器（刀・ロングソードはゲームと同じ武器の型のボット）。</param>
    /// <param name="blueWeapon">青コーナーが握る武器。</param>
    /// <param name="redCharacter">赤が名簿のキャラクターなら、その体格・顔・表示名（ボットのスタイルは <paramref name="red"/>）。</param>
    /// <param name="referee">レフェリーがいるか（false ならカウント中も攻撃でき、倒れた相手へ打ち下ろす）。</param>
    /// <param name="blueCharacter">青が名簿のキャラクターなら、その体格・顔・表示名。</param>
    public void Make(Contender red, Contender blue, int seed, float seconds, string outPath, TextWriter log, Action<float>? afterStep = null, bool untilKo = false,
        MiniRing.Weapons.HandWear redWear = MiniRing.Weapons.HandWear.Glove, MiniRing.Weapons.HandWear blueWear = MiniRing.Weapons.HandWear.Glove,
        IntentScript? intentScript = null, Action<BoxingGame>? setup = null, MiniRing.Weapons.WeaponKind? redWeapon = null, MiniRing.Weapons.WeaponKind? blueWeapon = null,
        MiniRing.Characters.FighterCharacter? redCharacter = null, MiniRing.Characters.FighterCharacter? blueCharacter = null, bool referee = true)
    {
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var corners = Corners(red, blue, seed, redWear, blueWear, redWeapon, blueWeapon,
            s => Keep(blueBots, blue.Create(s)), intentScript is null ? s => Keep(redBots, red.Create(s)) : null, referee);
        // 画面と実況ではリングネームで呼ぶ（ドリルで生まれた個体にもそれぞれの名前がある）。
        // 名簿のキャラクター（--red-char / --blue-char）は、体格・顔・表示名をそのキャラクターの値にする（ボットの性格はスタイル）。
        corners = TvShowRules.Stage(WithCharacters(corners, redCharacter, blueCharacter), _org);
        var redRing = redCharacter?.HudName ?? BotNames.RingNameOf(red.Name);
        var blueRing = blueCharacter?.HudName ?? BotNames.RingNameOf(blue.Name);
        using var game = new BoxingGame(seed: seed, corners: corners, rules: _rules, mood: _org.ArenaMood);
        setup?.Invoke(game);
        var intent = intentScript is null ? null : new IntentControl();
        var intentRound = game.Round;
        using var recordingFile = intentScript is null ? null : File.Create(Path.ChangeExtension(outPath, ".mrrep"));
        using var recording = recordingFile is null ? null : new MiniRing.Replay.ReplayRecorder(recordingFile, game, "intentvideo");
        if (tv)
        {
            seconds = MathF.Max(seconds, _rules!.TotalSeconds + MathF.Max(AftermathSeconds, _rules.EndSeconds) + 1f); // テレビ中継は試合の終わりまで撮る（--seconds が短くても）
        }

        var card = _rules is null ? null : new TvCard(tvEvent, tvBout,
            redCharacter is null ? TvCorner.Of(redRing, red.Name, redWear, redWeapon) : TvCorner.Of(redCharacter),
            blueCharacter is null ? TvCorner.Of(blueRing, blue.Name, blueWear, blueWeapon) : TvCorner.Of(blueCharacter), _rules, belt);
        // 試合の前の場面の生の絵のための影の試合（生成直後の T ポーズを映さない。本番の試合は進めない）。同じ種・両コーナー・決まり・雰囲気。
        var stage = card is null ? null : new BoxingGame(seed: seed,
            corners: TvShowRules.Stage(WithCharacters(Corners(red, blue, seed, redWear, blueWear, redWeapon, blueWeapon, s => blue.Create(s), s => red.Create(s), referee), redCharacter, blueCharacter), _org),
            rules: _rules, mood: _org.ArenaMood);
        if (stage is not null)
        {
            setup?.Invoke(stage);
        }

        var show = ShowFormats.Create(width, height, _org);
        _scenes = card is null ? [] : show.PreFight(card, tvProgram, redCharacter?.Profile is not null && blueCharacter?.Profile is not null);
        _resultSeconds = show.ResultSeconds;
        _captionY = show.CaptionY(height);
        var broadcast = card is null ? null : new Broadcast(redRing, blueRing, red.Name, blue.Name)
        {
            OrgId = _org.Id,
            RulesSheet = _org.RulesSheet,
            ReactionGain = _org.Crowd.ReactionGain,
            AmbienceGain = _org.Crowd.AmbienceGain,
            RedGear = GearName(redWeapon),
            BlueGear = GearName(blueWeapon),
            RedDiscipline = card.Red.Discipline,
            BlueDiscipline = card.Blue.Discipline,
            RedProfile = Profile(card.Red),
            BlueProfile = Profile(card.Blue),
            Event = tvEvent,
            Bout = tvBout,
            Program = tvProgram,
            ProgramBriefing = tvProgram?.Briefing(),
            RedDossier = redCharacter?.Profile?.Briefing(redCharacter.Name, redCharacter.HudName, redCharacter.Tagline),
            BlueDossier = blueCharacter?.Profile?.Briefing(blueCharacter.Name, blueCharacter.HudName, blueCharacter.Tagline),
        };
        // 実況・解説の LLM に渡す生の記録（姿勢・当たりの中身・削れた場所。*.telemetry.json）。読むだけで試合には触れない。
        var telemetry = broadcast is null ? null : new TvTelemetry(redRing, blueRing, redCharacter?.Profile?.HeightCm ?? 0, blueCharacter?.Profile?.HeightCm ?? 0);
        // 見どころ（MiniRing.Highlights。読むだけ）: 映像の秒で <out>.highlights.txt に書く。目で確かめる時刻の表（docs/EXCITEMENT.md）。
        var excitement = new MiniRing.Highlights.MatchHighlights(matchSeconds: seconds);
        // 台本の資料: 試合の前の場面（番組の型が決める並び。記録には prefight の印を付けて残す）。
        var sceneStart = 0f;
        foreach (var scene in _scenes)
        {
            broadcast?.NoteScene(sceneStart, scene.EventType, scene.Who, scene.Description);
            sceneStart += scene.Seconds;
        }

        // 体の図の読み手（折れた・切れた部位は中継の記録が覚えている）。
        var bodies = broadcast is null ? ShowBodies.None : new ShowBodies(broadcast.LimbOf);
        // 判定の得点（与えたダメージ = 相手の体の TotalDamage）の、各ラウンドの始まりの値。勝敗の画面の採点表に。
        var redRoundStart = new List<float>();
        var blueRoundStart = new List<float>();
        // ラウンドごとに当てた打撃の数（拳・蹴り・肘・握った武器。採点の札へ）と、休みの頭に締めた集計。
        var redHitsByRound = new List<int>();
        var blueHitsByRound = new List<int>();
        IReadOnlyList<WvcRoundTally> breakTallies = [];
        var finishRound = 1;
        var finishRoundTime = 0f;
        using var renderer = _off ? null : new HeadlessRenderer((uint)width, (uint)height);
        var screen = new Vector2(width, height);
        var panel = _faceCam?.Panel(screen, FaceSide.Left) ?? default; // 左右の窓は同じ大きさ
        // フェイスオフの場面（番組の型にあれば）は、窓が画面いっぱいなので別の置き場・別の大きさで描く。
        var faceOffCam = tv && faceCam is not null && _scenes.Any(s => s.Backdrop == ShowBackdrop.FaceOff) ? show.ArrangeFaceOff(faceCam, width, height) : null;
        var faceOffPanel = faceOffCam?.Panel(screen, FaceSide.Left) ?? default;
        using var faceOffRenderer = _off || faceOffCam is null ? null : new HeadlessRenderer((uint)faceOffPanel.WidthPx, (uint)faceOffPanel.HeightPx);
        // 顔の窓は窓の大きさで別に描いて書き写す（ヘッドレスの描画は 1 つのカメラで 1 枚。ホストでは重ねる窓として描く）。
        using var paneRenderer = _off || _faceCam is null ? null : new HeadlessRenderer((uint)panel.WidthPx, (uint)panel.HeightPx);
        // 下回し（描かない）は音も作らない（events.json に音は要らない）。
        var audioOn = !_off;
        var audio = new float[audioOn ? (int)((TitleSeconds + (seconds / speed) + ResultSeconds + 1f) * HitSound.SampleRate) : 0];
        // 生配信の音の混ぜ: 地の歓声は始めに混ぜ（会場の音の長さは audio 全体）、声は前もって声の線に置く。反応は増えた分だけ書き出す前に混ぜる。
        LiveAudioMixer? liveMixer = null;
        var cuesMixed = 0;
        if (live is not null)
        {
            if (broadcast is not null)
            {
                Broadcast.MixAmbience(audio, HitSound.SampleRate, seed, _org.Crowd.AmbienceGain);
            }

            liveMixer = new LiveAudioMixer(HitSound.SampleRate, audio.Length);
            if (live.VoicesManifest is { } manifest)
            {
                liveMixer.LoadManifest(manifest);
                log.WriteLine($"実況の声: {liveMixer.VoiceCount} 台詞（{manifest}）");
            }
        }

        var impactAudio = new ImpactAudio();
        var audioTimeline = new ImpactTimeline();
        var videoPath = Path.ChangeExtension(outPath, ".video.mp4");
        var wavPath = Path.ChangeExtension(outPath, ".wav");
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        var redCaption = new Caption();
        var blueCaption = new Caption();
        var flashes = new List<(string Text, Vector4 Color, float Until)>();
        var redCombos = new Dictionary<string, int>();
        var blueCombos = new Dictionary<string, int>();
        float redDamage = 0f, blueDamage = 0f;
        var frame = 0;
        string? finish = null;
        int? koWinner = null;
        var watch = Stopwatch.StartNew();
        long renderTicks = 0, writeTicks = 0; // 生配信の診断（環境変数 LIVE_TIMING=1 で最後に内訳を出す）: 描画・書き込み（歩調の待ちを含む）に使った時間

        // 途中で失敗しても、作りかけの映像・音声ファイルと ffmpeg を残さない。
        try
        {
            // 書き出しの道は 3 つ: ファイル（ffmpeg → mp4）、生配信（LiveOutput）、下回し（描かない。コマを数えるだけ）。
            using var liveOutput = live is null ? null : new LiveOutput(live, width, height, fps, HitSound.SampleRate, audio, liveMixer!, log);
            using (var ffmpeg = _off || live is not null ? null : StartFfmpeg(videoPath))
            {
                try
                {
                    var stdin = ffmpeg?.StandardInput.BaseStream;
                    void Emit(Matrix4x4 viewProjection, Vector3 eye, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<UiShapeDraw> shapes, bool withFace = false, BoxingGame? scene = null, FaceCam? faceOverride = null)
                    {
                        if (_off)
                        {
                            frame++;
                            return;
                        }

                        // scene: 描く試合（リプレイは影の試合）。顔の窓は本番の試合だけ。
                        var g = scene ?? game;
                        var face = withFace && (scene is null || ReferenceEquals(scene, stage)) ? faceOverride ?? _faceCam : null;
                        var facePaneRenderer = faceOverride is not null && ReferenceEquals(faceOverride, faceOffCam) ? faceOffRenderer : paneRenderer;
                        var renderStart = Stopwatch.GetTimestamp();
                        var rgba = renderer!.RenderPrimitiveScene(ArenaLook.Background, viewProjection, instances, lines, text, particles: g.BuildParticles(), lighting: g.BuildLighting(),
                            shadow: g.BuildShadow(), cameraPosition: eye, shapes: face is null ? shapes : [.. shapes, .. face.Frames(g, screen)], postProcess: g.BuildPostProcess(), voxels: g.BuildVoxelVolumes());
                        if (face is not null && facePaneRenderer is not null)
                        {
                            foreach (var facePane in face.Panes(g, screen))
                            {
                                var at = facePane.Panel;
                                var (faceViewProjection, faceEye) = face.Camera(g, facePane.Fighter, at.AspectRatio);
                                var pane = facePaneRenderer.RenderPrimitiveScene(ArenaLook.Background, faceViewProjection, instances, lines, [], particles: g.BuildParticles(), lighting: g.BuildLighting(),
                                    shadow: g.BuildShadow(), cameraPosition: faceEye, postProcess: g.BuildPostProcess(), voxels: g.BuildVoxelVolumes());
                                for (var row = 0; row < at.HeightPx; row++)
                                {
                                    Array.Copy(pane, row * at.WidthPx * 4, rgba, (((at.YPx + row) * width) + at.XPx) * 4, at.WidthPx * 4);
                                }
                            }
                        }

                        if (liveOutput is not null)
                        {
                            // 生配信: 増えた歓声の反応を混ぜてから、そのコマまでの音とともに実時間の歩調で流す。
                            renderTicks += Stopwatch.GetTimestamp() - renderStart;
                            var writeStart = Stopwatch.GetTimestamp();
                            cuesMixed = broadcast?.MixCues(audio, HitSound.SampleRate, cuesMixed) ?? cuesMixed;
                            liveOutput.WriteFrame(rgba, audio, liveMixer!);
                            writeTicks += Stopwatch.GetTimestamp() - writeStart;
                        }
                        else
                        {
                            stdin!.Write(rgba);
                        }

                        frame++;
                    }

                    // タイトル: 試合前の構えを背景に。テレビ中継は番組の型の場面の並び（団体ごとに別の番組）。
                    if (stage is not null)
                    {
                        // 影の試合を構えて立つ指令で 1.5 秒進めてから映し始める（腕を真横に広げた生成直後の姿勢を映さない）。
                        for (var warm = 0; warm < (int)MathF.Ceiling(1.5f * fps); warm++)
                        {
                            stage.StepStage(1f / fps, StagePose.Center);
                        }
                    }

                    BoxingRenderer.Fill(stage ?? game, showTargetGhosts: false, instances, lines);
                    var titleDirector = card is null ? null : new TvDirector(width, height, fps, MaxStepDt, redRing, blueRing, GearName(redWeapon), GearName(blueWeapon));
                    var stung = new HashSet<string>();
                    for (var t = 0f; t < TitleSeconds; t += 1f / fps)
                    {
                        if (card is null)
                        {
                            var (plainView, plainEye) = CameraAt(game, t * 0.15f);
                            Emit(plainView, plainEye, Title(redRing, blueRing, MathF.Min(1f, t / 0.6f)), Dim(0.6f));
                            continue;
                        }

                        var sceneIndex = 0;
                        var sceneFrom = 0f;
                        var cursor = 0f;
                        for (var k = 0; k < _scenes.Count; k++)
                        {
                            if (t >= cursor)
                            {
                                sceneIndex = k;
                                sceneFrom = cursor;
                            }

                            cursor += _scenes[k].Seconds;
                        }

                        var current = _scenes[sceneIndex];
                        var local = t - sceneFrom;
                        var (sceneText, sceneShapes) = show.Scene(current, card, tvProgram, local);
                        var set = stage!; // 試合の前の場面は影の試合を映す（card が無ければここへ来ない）
                        set.StepStage(1f / fps, current.Stage);
                        BoxingRenderer.Fill(set, showTargetGhosts: false, instances, lines);
                        var (vp, eye) = current.Backdrop switch
                        {
                            ShowBackdrop.CageOrbit => CageOrbitCamera(set, local),
                            ShowBackdrop.RedLow => titleDirector!.Ringside(set, set.Player, local),
                            ShowBackdrop.BlueLow => titleDirector!.Ringside(set, set.Cpu, local),
                            _ => WideCameraAt(set, t),
                        };
                        if (audioOn && current.StingAt is { } sting && local >= sting && stung.Add(current.Id) && BellSound.Synthesize(_org.Bell, RoundBell.Start) is { } horn)
                        {
                            Mix(audio, horn, t, 1f);
                        }

                        var faceOff = current.Backdrop == ShowBackdrop.FaceOff;
                        Emit(vp, eye, sceneText, sceneShapes, withFace: faceOff, faceOverride: faceOff ? faceOffCam : null, scene: set);
                    }

                    // 試合の前の場面は終わり。影の試合は捨てる（メモリを返す）。
                    stage?.Dispose();
                    stage = null;

                    // 試合。
                    // 1 コマで進める試合の時間（速さ / fps）を、MaxStepDt 以下の等しい歩に分ける（30 fps なら 0.75 倍で 1/80 s × 2 歩）。
                    var frameDt = speed / fps;
                    var stepsPerFrame = Math.Max(1, (int)MathF.Ceiling((frameDt / MaxStepDt) - 1e-3f));
                    var stepDt = frameDt / stepsPerFrame;
                    // テレビ中継の演出: カメラの切り替え・休みのカット割り・リプレイ（同じ試合をもう一度回した影の試合）。
                    var director = new TvDirector(width, height, fps, stepDt, redRing, blueRing, GearName(redWeapon), GearName(blueWeapon));
                    BoxingGame? replayGame = null;
                    var replayDone = 0; // 影の試合が進んだ歩の数
                    TvHighlight? replaying = null;
                    var replayDeviation = 0f; // 影の試合と本番の胸の位置の差の最大 [m]（決定的なら 0）
                    var mainTrack = new List<(Vector3 Red, Vector3 Blue)>();
                    var breakDoing = new Dictionary<Fighter, string?>(); // 休みの動作の、いま中継に伝えてある名前（選手ごと）
                    IReadOnlyList<TvSegment>? breakPlan = null;
                    var breakStartAt = 0f;
                    var lastShot = (TvShot.Main, (Fighter?)null);
                    var shotStartAt = 0f;
                    var replayBefore = (int)(TvDirector.ReplayBefore / stepDt);
                    var replayAfter = (int)(TvDirector.ReplayAfter / stepDt);
                    void StepReplay()
                    {
                        replayGame!.Step(default, stepDt);
                        var (r, b) = mainTrack[replayDone];
                        replayDeviation = MathF.Max(replayDeviation, MathF.Max(Vector3.Distance(r, replayGame.Player.ChestPosition), Vector3.Distance(b, replayGame.Cpu.ChestPosition)));
                        replayDone++;
                    }

                    void EnsureReplay()
                    {
                        if (replayGame is null)
                        {
                            var replayCorners = TvShowRules.Stage(WithCharacters(Corners(red, blue, seed, redWear, blueWear, redWeapon, blueWeapon, s => blue.Create(s), s => red.Create(s), referee), redCharacter, blueCharacter), _org);
                            replayGame = new BoxingGame(seed: seed, corners: replayCorners, rules: _rules, mood: _org.ArenaMood);
                            setup?.Invoke(replayGame);
                        }
                    }

                    // カット割り（休み・試合の始め・終わり）を、中継の出来事（台本の資料）にする。始まりの映像の時刻 from からの秒。
                    void NoteCuts(IReadOnlyList<TvSegment> plan, float from)
                    {
                        foreach (var segment in plan)
                        {
                            var who = segment.Red ? redRing : blueRing;
                            var what = segment.Shot switch
                            {
                                TvShot.Replay => $"リプレイ（第{segment.Highlight!.Round}ラウンド、スロー）: {segment.Highlight.Text}",
                                TvShot.Corner => $"リングの中のカメラ: {who} のコーナー",
                                TvShot.Ringside => $"リング脇のカメラマン: {who} を低い所から",
                                TvShot.Ceiling => $"天井のカメラ: {who} を真上から",
                                TvShot.Wound when segment.Wound!.Healing => $"手持ちカメラが {who} の{segment.Wound.Name}の傷にズームで寄る（アップ。治りかけ: 休みの間に削れた所が戻っていく）",
                                TvShot.Wound => $"手持ちカメラが {who} の{segment.Wound!.Name}の傷にズームで寄る（アップ）",
                                TvShot.Handheld => $"リングの中のカメラマンの手持ち: {who} を斜め前から（胸から上）",
                                TvShot.Wide => "リング全体のカメラ（高い所から、リングの 2 人を引きで）",
                                _ => "中継カメラ",
                            };
                            var type = segment.Shot switch { TvShot.Replay => "replay", TvShot.Wound => "wound_cam", _ => "cut" };
                            broadcast?.Note(from + segment.Start, type, segment.Shot == TvShot.Wound ? who : "", what);
                        }
                    }

                    var time = 0f;
                    float? endAt = null;
                    for (var step = 0; time < seconds && (endAt is null || time < endAt); step++)
                    {
                        if (intentScript is not null)
                        {
                            if (intentRound != game.Round)
                            {
                                intent = new IntentControl();
                                intentRound = game.Round;
                            }

                            intent!.Input = intentScript.At(time);
                            var input = new BoxingInput(intent.Read(game.PlayerSense, stepDt));
                            recording!.Step(game, input, stepDt);
                        }
                        else
                        {
                            game.Step(default, stepDt);
                        }
                        time += stepDt;
                        var at = TitleSeconds + (time / speed); // 映像の時刻（音を置く所）
                        broadcast?.Observe(game, at, step);
                        excitement.Observe(game, time, stepDt);
                        director.Observe(game, step);
                        if (redHitsByRound.Count > 0)
                        {
                            // 当てた打撃の数（採点の札）。数えるのは今のラウンド（記録の hit・weapon_hit と同じ打撃: 閾値を設けず全部）。
                            foreach (var attacker in game.RecentHits.Select(h => h.Attacker).Concat(game.WeaponHits.Select(h => h.Attacker)))
                            {
                                (attacker == game.Player ? redHitsByRound : blueHitsByRound)[^1]++;
                            }
                        }
                        if (tv && game.EmoteScene is { } emoteScene)
                        {
                            // 動作（休み・試合の始め・終わりのエモート）が始まったら、中継の出来事にする（実況・解説が、選手がいま何をしているかを知る）。
                            // 型は場面が分かる名前: break_action（休み）・start_action（試合の始め）・end_action（試合の終わり）。
                            // リプレイを流している間は、選手の今の動作は画面に映らない: 知らせず、リプレイが明けてもまだ続いていればその時に知らせる
                            // （休みのリプレイの最中に「深呼吸をしている」と実況していた。2026-11-19、ユーザー: 実況が見ているものと視聴者の見ているものが違う）。
                            var replayOnScreen = breakPlan?.Any(s => s.Shot == TvShot.Replay && at - breakStartAt >= s.Start && at - breakStartAt < s.End) ?? false;
                            foreach (var (fighter, name) in new[] { (game.Player, redRing), (game.Cpu, blueRing) })
                            {
                                if (replayOnScreen)
                                {
                                    continue;
                                }

                                var doing = game.BreakSourceOf(fighter)?.CurrentName;
                                if (doing != breakDoing.GetValueOrDefault(fighter))
                                {
                                    breakDoing[fighter] = doing;
                                    if (doing is not null)
                                    {
                                        var (type, when) = emoteScene switch
                                        {
                                            MiniRing.Break.BreakScene.Start => ("start_action", "入場してゴングを待つ間に"),
                                            MiniRing.Break.BreakScene.End => ("end_action", game.EndMood(fighter) > 0f ? "勝利の後に" : game.EndMood(fighter) < 0f ? "敗れた後に" : "試合の後に"),
                                            _ => ("break_action", "休みの間に"),
                                        };
                                        broadcast?.Note(at, type, name, $"{name}が{when}{BreakActionWords(doing)}");
                                    }
                                }
                            }
                        }
                        mainTrack.Add((game.Player.ChestPosition, game.Cpu.ChestPosition));
                        // リプレイは同じ試合を回し直すので、ボット同士の試合だけ（操作の台本で動かす赤は回し直せない）。
                        if (tv && intentScript is null && game.EmoteScene == MiniRing.Break.BreakScene.Break && breakPlan is null)
                        {
                            // 休みに入った: そのラウンドの見どころを選び、カット割りを決める（先に映すのは多く打たれた側のコーナー）。
                            var highlights = director.Highlights(game.MatchRound, 2, replayDone); // 休みのリプレイは 1〜2 本（重い順。PlanBreak が休みの長さで絞る）
                            var redTook = game.Player.Condition.TotalDamage - (blueRoundStart.Count > 0 ? blueRoundStart[^1] : 0f);
                            var blueTook = game.Cpu.Condition.TotalDamage - (redRoundStart.Count > 0 ? redRoundStart[^1] : 0f);
                            breakStartAt = at;
                            breakPlan = director.PlanBreak(_rules!.BreakSeconds / speed, highlights, redTook >= blueTook, director.Wounds());
                            NoteCuts(breakPlan, at);

                            breakTallies = Tallies(redRoundStart, blueRoundStart, redHitsByRound, blueHitsByRound, game.Cpu.Condition.TotalDamage, game.Player.Condition.TotalDamage);
                            if (show.IntervalBoardSeconds > 0f)
                            {
                                broadcast?.Note(at, "board", "", show.IntervalNote(game, redRing, blueRing, bodies, breakTallies));
                            }
                        }
                        else if (tv && game.EmoteScene == MiniRing.Break.BreakScene.Start && breakPlan is null)
                        {
                            // 試合の始め（入場からゴングまで）: 引き → 赤 → 青 → 引き。決まった秒数の場面なので番組の予定に載せる。
                            breakStartAt = at;
                            breakPlan = TvDirector.PlanIntro(_rules!.IntroSeconds / speed);
                            broadcast?.Note(at, "intro_scene", "", $"試合の始め（約 {_rules.IntroSeconds / speed:0} 秒）: 両選手が入場して最初のゴングを待つ間にエモートをする。カットは引き → {redRing} → {blueRing} → 引き");
                            NoteCuts(breakPlan, at);
                        }
                        else if (game.EmoteScene is null)
                        {
                            breakPlan = null;
                        }

                        while (redRoundStart.Count < game.MatchRound)
                        {
                            redRoundStart.Add(game.Cpu.Condition.TotalDamage);
                            blueRoundStart.Add(game.Player.Condition.TotalDamage);
                            redHitsByRound.Add(0);
                            blueHitsByRound.Add(0);
                        }

                        if (tv && endAt is null && game.MatchOver)
                        {
                            finishRound = game.MatchRound;
                            finishRoundTime = _rules!.RoundSeconds - game.RoundLeft;
                            // テレビ中継: K.O. か最終ラウンドの判定で試合が終わる。少し余韻を撮って結果へ。
                            finish = game.Decision ?? game.Message;
                            koWinner = game.PlayerWins > game.CpuWins ? 1 : game.CpuWins > game.PlayerWins ? -1 : 0;
                            endAt = time + (game.EmoteScene == MiniRing.Break.BreakScene.End ? _rules.EndSeconds : AftermathSeconds);
                            if (game.EmoteScene == MiniRing.Break.BreakScene.End)
                            {
                                // 決着の後（勝敗の画面の前）: 勝者のアップ → 敗者 → 引き。この間、立っている選手は終わりのエモートをする（倒れている敗者は倒れたまま）。
                                var winnerRed = koWinner > 0 ? true : koWinner < 0 ? false : (bool?)null;
                                var loser = koWinner > 0 ? game.Cpu : game.Player;
                                breakStartAt = at;
                                breakPlan = TvDirector.PlanEnd(_rules.EndSeconds / speed, winnerRed, loser.State != FighterState.Standing);
                                var winnerName = winnerRed is null ? null : winnerRed.Value ? redRing : blueRing;
                                var loserName = winnerRed is null ? null : winnerRed.Value ? blueRing : redRing;
                                broadcast?.Note(at, "end_scene", "", winnerName is null
                                    ? $"決着の後（約 {_rules.EndSeconds / speed:0} 秒）: 引き分け。{redRing}と{blueRing}がそれぞれエモートをする。カットは {redRing} → {blueRing} → 引き"
                                    : $"決着の後（約 {_rules.EndSeconds / speed:0} 秒）: 勝者 {winnerName} のアップ → 敗者 {loserName}{(loser.State != FighterState.Standing ? "（倒れたまま）" : "")} → 引き。選手は終わりのエモートをする");
                                NoteCuts(breakPlan, at);
                            }
                        }

                        if (audioOn && BellSound.Synthesize(_org.Bell, game.RecentBell) is { } gong)
                        {
                            Mix(audio, gong, at, 1f);
                        }

                        foreach (var voice in audioOn ? impactAudio.Push(time, ImpactAudio.Gather(game.RecentHits, game.RecentContacts, game.RecentShatters, hitSounds, [.. game.WeaponHits, .. game.SelfWeaponHits]), breakStyle) : [])
                        {
                            Mix(audio, voice.Pcm, TitleSeconds + ((float)voice.OccurredAt / speed), 1f);
                            audioTimeline.Add(voice, TitleSeconds + voice.OccurredAt / speed, frame / (double)fps);
                        }

                        foreach (var sever in audioOn ? game.RecentSevers : [])
                        {
                            Mix(audio, SeverSound.Synthesize(sever.Strength, step), at, 1f);
                        }

                        // 骨が折れた（肉でぶら下がる）: 画面に出し、記録に残す（どの打撃で・どこが折れたか、動画のどこを見ればよいか）。
                        foreach (var broken in game.BrokenBones)
                        {
                            flashes.Add(("BROKEN!", Gold, time + (1.2f * speed)));
                            log.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                $"bone broken at {time:0.0} s (video {at:0.0} s): {(broken.Victim == game.Player ? "red" : "blue")} {broken.Part}"));
                        }

                        if (untilKo && endAt is null && game.RoundOver)
                        {
                            // ラウンドが終わった理由（K.O. の種類と勝者）。ゲームは RoundResetDelay の後に次のラウンドを始めるので、その前に止める。
                            finish = game.Message;
                            koWinner = game.PlayerWins > game.CpuWins ? 1 : -1;
                            endAt = time + AftermathSeconds;
                            flashes.Add(("K.O.", Gold, endAt.Value));
                        }
                        if (audioOn && VoxelBreakSound.Landings(game.RecentLandings, step, breakStyle) is { } landingPcm && impactAudio.LimitBackground(time, landingPcm) is { } landed)
                        {
                            Mix(audio, landed, at, 1f);
                        }

                        foreach (var swing in audioOn ? game.RecentSwings : [])
                        {
                            if (SwingSound.Synthesize(swing.Speed, step) is { } whoosh)
                            {
                                Mix(audio, whoosh, at, 1f);
                            }
                        }

                        foreach (var shatter in game.RecentShatters)
                        {
                            if (shatter.Fatal)
                            {
                                if (audioOn)
                                {
                                    Mix(audio, VoxelBreakSound.Fatal(seed: 7, breakStyle), at, 1f);
                                }

                                flashes.Add(("FATAL", Gold, time + (1.2f * speed))); // 表示の長さは映像の秒で
                            }
                        }

                        afterStep?.Invoke(time);
                        foreach (var hit in game.RecentHits)
                        {
                            var redHit = hit.Attacker == game.Player;
                            if (redHit)
                            {
                                redDamage += hit.Damage;
                            }
                            else
                            {
                                blueDamage += hit.Damage;
                            }

                            if (hit.Damage >= BigHitDamage)
                            {
                                flashes.Add(($"{HitZones.Label(hit.Zone)}  {hit.Damage:0}", redHit ? Red : Blue, time + 0.7f));
                            }
                        }

                        UpdateCaption(redCaption, redBots, redCombos, time);
                        UpdateCaption(blueCaption, blueBots, blueCombos, time);
                        static string? LastMove(string? combo) => combo?.Split("  ")[^1];
                        telemetry?.Observe(game, time, at, (LastMove(redCaption.Combo), redCaption.Defense), (LastMove(blueCaption.Combo), blueCaption.Defense));
                        if (broadcast is not null)
                        {
                            // 技・守りの名前（ボットの頭の中から）を、実況の資料に足す。
                            foreach (var (caption, ring) in new[] { (redCaption, redRing), (blueCaption, blueRing) })
                            {
                                if (caption.ComboTime == time && caption.Combo is { } combo)
                                {
                                    broadcast.Note(at, "technique", ring, $"{ring}の狙い: {combo.Split("  ")[^1]}");
                                }

                                if (caption.DefenseTime == time && caption.Defense is { } defense && defense != caption.LastNotedDefense)
                                {
                                    broadcast.Note(at, "defense", ring, $"{ring}が{defense}でかわす");
                                }

                                caption.LastNotedDefense = caption.DefenseTime == time ? caption.Defense : null;
                            }
                        }
                        flashes.RemoveAll(f => f.Until < time);
                        if (step % stepsPerFrame != 0)
                        {
                            continue;
                        }

                        // カメラを決める: 休みはカット割りどおり、ラウンド中・決着の後はダウンで天井とリング脇を交互に。
                        var shot = TvShot.Main;
                        Fighter? subject = null;
                        TvSegment? cut = null;
                        var breakAt = at - breakStartAt;
                        // ダウンの経過はいつも見ておく（休みの間に忘れさせる。休みの後に古い時計で切り替えない）。
                        var live = tv ? director.Live(game, at) : (TvShot.Main, (Fighter?)null);
                        if (tv && breakPlan is not null)
                        {
                            cut = breakPlan.FirstOrDefault(s => breakAt >= s.Start && breakAt < s.End);
                            if (cut.Value.End <= cut.Value.Start)
                            {
                                cut = breakPlan[^1];
                            }

                            shot = cut.Value.Shot;
                            subject = cut.Value.Red ? game.Player : game.Cpu;
                        }
                        else if (tv)
                        {
                            (shot, subject) = live;
                        }

                        if ((shot, subject) != lastShot)
                        {
                            lastShot = (shot, subject);
                            shotStartAt = at;
                            if (shot is TvShot.Ceiling or TvShot.Ringside)
                            {
                                broadcast?.Note(at, "camera", subject == game.Player ? redRing : blueRing,
                                    shot == TvShot.Ceiling ? (game.InBreak ? "天井のカメラ（コーナーの選手を真上から）" : "天井のカメラ（倒れた選手を真上から）")
                                    : game.InBreak ? "リング脇のカメラマン（コーナーの選手を低い所から）" : "リング脇のカメラマン（床の高さから）");
                            }
                        }

                        var shotTime = at - shotStartAt;
                        // リプレイへ入る・出るときのワイプ（境目の前後 0.25 秒）。
                        var wipe = new List<UiShapeDraw>();
                        if (breakPlan is not null)
                        {
                            foreach (var s in breakPlan.Where(s => s.Shot == TvShot.Replay))
                            {
                                foreach (var edge in new[] { s.Start, s.End })
                                {
                                    if (MathF.Abs(breakAt - edge) < 0.25f)
                                    {
                                        wipe.AddRange(show.Wipe((breakAt - edge + 0.25f) / 0.5f));
                                    }
                                }
                            }
                        }

                        if (shot == TvShot.Replay && cut?.Highlight is { } highlight)
                        {
                            // リプレイ: 影の試合（同じ種・同じ歩）を場面の少し前まで描かずに進め、そこから 1 歩 1 コマで描く（スロー）。
                            EnsureReplay();
                            var first = highlight.Step - replayBefore;
                            var last = highlight.Step + replayAfter;
                            if (replaying != highlight)
                            {
                                replaying = highlight;
                                while (replayDone < first + 1)
                                {
                                    StepReplay();
                                }
                            }
                            else if (replayDone < last + 1 && replayDone < mainTrack.Count)
                            {
                                StepReplay();
                            }

                            BoxingRenderer.Fill(replayGame!, showTargetGhosts: false, instances, lines);
                            var (rvp, reye) = director.Replay(replayGame!, highlight, Math.Clamp((replayDone - 1 - first) / (float)(last - first), 0f, 1f));
                            var (replayText, replayShapes) = show.ReplayOverlay(highlight.Round, highlight.Text, at);
                            Emit(rvp, reye, replayText, [.. replayShapes, .. wipe], scene: replayGame);
                            continue;
                        }

                        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                        if (intent is not null)
                        {
                            var keys = intent.Input;
                            redCaption.Defense = $"KEYS ({keys.Direction.X:0.#},{keys.Direction.Y:0.#}) {(keys.Light ? "J" : "")} {(keys.Heavy ? "K" : "")} {(keys.Evade ? "SPACE" : "")}  {intent.CurrentDefense} {intent.CurrentTechnique}";
                            redCaption.DefenseTime = time;
                        }

                        var (vp, eye) = shot switch
                        {
                            TvShot.Ceiling => director.Ceiling(game, subject!, shotTime),
                            TvShot.Ringside => director.Ringside(game, subject!, shotTime),
                            TvShot.Corner => director.Corner(game, subject!, shotTime, at),
                            TvShot.Wound => director.Wound(game, subject!, cut!.Value.Wound!, shotTime, at),
                            TvShot.Handheld => director.Handheld(game, subject!, shotTime, at),
                            TvShot.Wide => director.Wide(game, shotTime),
                            _ => CameraAt(game, (TitleSeconds * 0.15f) + (time / speed * 0.15f)),
                        };
                        var overlay = MatchOverlay(redRing, blueRing, redCaption, blueCaption, flashes, redDamage, blueDamage, time, untilKo || tv ? null : seconds, game, vp);
                        IEnumerable<UiShapeDraw> hudShapes = tv ? TvHud.Shapes(game, screen) : StaminaBars(game);
                        if (tv && _faceCam is not null)
                        {
                            // 顔の窓を左下・右下に: 上の得点表の代わりに、時計と、窓の下の名札・スタミナ。
                            var (cornerText, cornerShapes) = show.MatchHud(game, _faceCam.Panel(screen, FaceSide.Left), _faceCam.Panel(screen, FaceSide.Right), redRing, blueRing, bodies);
                            overlay.InsertRange(0, cornerText);
                            hudShapes = cornerShapes;
                        }

                        if (tv && show.IntervalBoardSeconds > 0f && game.InBreak && breakPlan is not null)
                        {
                            // 休みの看板（番組の型にあれば。休みの最初だけ出る）。
                            var board = show.Interval(game, redRing, blueRing, bodies, breakAt, breakTallies);
                            overlay.AddRange(board.Text);
                            hudShapes = [.. hudShapes, .. board.Shapes];
                        }

                        Emit(vp, eye, overlay, [.. Bars(), .. hudShapes, .. DownCountHud.Shapes(game, vp, screen), .. wipe], withFace: true);
                        if (frame % (fps * 5) == 0)
                        {
                            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {time:0}s / {seconds:0}s ({watch.Elapsed.TotalSeconds:0}s elapsed)"));
                        }
                    }

                    // K.O. で決着したら、結果の前に決着の瞬間をリプレイで（最終ラウンドで一番重い場面。K.O. の一撃に上乗せしてある）。
                    if (tv && intentScript is null && game.Decision is null && finish is not null && director.Finale(0) is { } finale)
                    {
                        var first = finale.Step - replayBefore;
                        if (first + 1 < replayDone)
                        {
                            // 休みのリプレイで影の試合がもう通り過ぎていた（倒した一撃をすでに休みで流した）: 影の試合を初めから回し直す。
                            replayGame?.Dispose();
                            replayGame = null;
                            replayDone = 0;
                        }

                        EnsureReplay();
                        var last = Math.Min(finale.Step + replayAfter, mainTrack.Count - 1);
                        while (replayDone < first + 1)
                        {
                            StepReplay();
                        }

                        var startAt = frame / (float)fps;
                        var length = (last - first) / (float)fps;
                        broadcast?.Note(startAt, "replay", "", $"決着のリプレイ（スロー）: {finale.Text}");
                        while (true)
                        {
                            var rel = (frame / (float)fps) - startAt;
                            var wipe = rel < 0.25f ? show.Wipe(0.5f + (rel / 0.5f)) : rel > length - 0.25f ? show.Wipe((rel - length + 0.25f) / 0.5f) : [];
                            BoxingRenderer.Fill(replayGame!, showTargetGhosts: false, instances, lines);
                            var (rvp, reye) = director.Replay(replayGame!, finale, Math.Clamp((replayDone - 1 - first) / (float)(last - first), 0f, 1f));
                            var (replayText, replayShapes) = show.ReplayOverlay(finale.Round, finale.Text, frame / (float)fps);
                            Emit(rvp, reye, replayText, [.. replayShapes, .. wipe], scene: replayGame);
                            if (replayDone >= last + 1)
                            {
                                break;
                            }

                            StepReplay();
                        }
                    }

                    if (replayGame is not null)
                    {
                        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"replay: {replayDone} steps re-run, max chest deviation from the live match {replayDeviation * 1000f:0.000} mm"));
                        replayGame.Dispose();
                    }

                    // 結果。台本の資料に、勝敗の画面が始まる時刻と動画の終わりを書く。
                    broadcast?.Note(frame / (float)fps, "result", "", $"勝敗の画面（{ResultSeconds:0} 秒）");
                    broadcast?.Note((frame / (float)fps) + ResultSeconds, "end", "", "動画の終わり");
                    var winner = koWinner ?? (MathF.Abs(redDamage - blueDamage) <= MathF.Max(1f, 0.05f * (redDamage + blueDamage) / 2f) ? 0 : MathF.Sign(redDamage - blueDamage));
                    if (broadcast is not null && card is not null)
                    {
                        var outcome = new TvResult(winner, finish, finishRound, finishRoundTime, [], []);
                        // 勝ち上がりを決める道具が読む。lead は引き分けのときに勝ち上がる側（与えたダメージの多い側）。
                        var outcomeFields = new Dictionary<string, object>
                        {
                            ["winner"] = winner > 0 ? "red" : winner < 0 ? "blue" : "draw",
                            ["lead"] = redDamage >= blueDamage ? "red" : "blue",
                            ["method"] = outcome.Method,
                            ["cause"] = outcome.Cause,
                            ["round"] = finishRound,
                            ["roundTime"] = MathF.Round(finishRoundTime, 1),
                        };
                        if (outcome.DeathPhrase.Length > 0)
                        {
                            outcomeFields["deathPhrase"] = outcome.DeathPhrase; // 死で決着した試合の言葉（プログラムの表の済んだ試合の行が使う）
                        }

                        broadcast.Outcome = outcomeFields;
                    }
                    BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                    for (var t = 0f; t < ResultSeconds; t += 1f / fps)
                    {
                        var (vp, eye) = CameraAt(game, ((TitleSeconds + (time / speed)) * 0.15f) + (t * 0.15f));
                        if (card is not null)
                        {
                            var result = new TvResult(winner, finish, finishRound, finishRoundTime,
                                RoundScores(redRoundStart, game.Cpu.Condition.TotalDamage), RoundScores(blueRoundStart, game.Player.Condition.TotalDamage));
                            var (resultText, resultShapes) = show.Result(card, result, t, game, bodies);
                            Emit(vp, eye, resultText, resultShapes);
                        }
                        else
                        {
                            Emit(vp, eye, Result(redRing, blueRing, redDamage, blueDamage, winner, MathF.Min(1f, t / 0.6f), finish), Dim(0.6f * MathF.Min(1f, t / 0.6f)));
                        }
                    }

                    if (liveOutput is not null)
                    {
                        // 生配信の終わり: 残りの音（保留の打撃・反応）を混ぜ、動画の長さまで書き切る。
                        foreach (var voice in impactAudio.Flush(breakStyle))
                        {
                            Mix(audio, voice.Pcm, TitleSeconds + ((float)voice.OccurredAt / speed), 1f);
                        }

                        cuesMixed = broadcast?.MixCues(audio, HitSound.SampleRate, cuesMixed) ?? cuesMixed;
                        liveOutput.Finish(audio, liveMixer!);
                        if (Environment.GetEnvironmentVariable("LIVE_TIMING") == "1")
                        {
                            var total = watch.Elapsed.TotalSeconds;
                            var toSeconds = 1.0 / Stopwatch.Frequency;
                            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                                $"live timing: total {total:0.0}s = render {renderTicks * toSeconds:0.0}s + write(frame hand-off) {writeTicks * toSeconds:0.0}s [encoder stdin blocked {liveOutput.SendSeconds:0.0}s] + physics/other {total - ((renderTicks + writeTicks) * toSeconds):0.0}s"));
                        }
                    }
                    else if (ffmpeg is not null)
                    {
                        stdin!.Close();
                        ffmpeg.WaitForExit();
                        if (ffmpeg.ExitCode != 0)
                        {
                            throw new IOException($"ffmpeg (video) failed with exit code {ffmpeg.ExitCode}.");
                        }
                    }
                }
                catch
                {
                    if (ffmpeg is not null && !ffmpeg.HasExited)
                    {
                        ffmpeg.Kill();
                        ffmpeg.WaitForExit();
                    }

                    throw;
                }
            }

            excitement.Finish();
            File.WriteAllText(Path.ChangeExtension(outPath, ".highlights.txt"), HighlightsText(excitement, TitleSeconds, speed));

            if (_off || live is not null)
            {
                // 下回しと生配信は、ファイル（wav・mp4）を書かない。出来事の記録だけ書く（下回しと本番の events.json が同じことを比べられるように）。
                broadcast?.WriteEvents(Path.ChangeExtension(outPath, ".events.json"));
                telemetry?.Write(Path.ChangeExtension(outPath, ".telemetry.json"));
            }
            else
            {
                foreach (var voice in impactAudio.Flush(breakStyle))
                {
                    Mix(audio, voice.Pcm, TitleSeconds + ((float)voice.OccurredAt / speed), 1f);
                }

                if (broadcast is not null)
                {
                    broadcast.MixInto(audio, HitSound.SampleRate, seed);
                    broadcast.WriteEvents(Path.ChangeExtension(outPath, ".events.json"));
                    telemetry?.Write(Path.ChangeExtension(outPath, ".telemetry.json"));
                }

                WriteWav(wavPath, audio, (float)frame / fps);
                audioTimeline.Write(outPath);
                Run("ffmpeg", $"-y -loglevel error -i \"{videoPath}\" -i \"{wavPath}\" -c:v copy -c:a aac -b:a 160k -shortest \"{outPath}\"");
            }
        }
        finally
        {
            stage?.Dispose(); // 試合の前の場面の途中で例外になっても、影の試合の物理のワールドを返す
            File.Delete(videoPath);
            File.Delete(wavPath);
        }

        var mode = _off ? " [events only, not rendered]" : live is not null ? " [live]" : "";
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{outPath}{mode}: {frame} frames ({(float)frame / fps:0.0}s) in {watch.Elapsed.TotalSeconds:0}s"));
    }

    /// <summary>
    /// 動画の試合の両コーナー。<paramref name="redControl"/> が null なら赤は外の入力（意図操作の台本）で動かす。
    /// 武器を持たせた試合は、ゲームと同じ道（UseWeaponBots: 刀・ロングソードは武器の型の剣士）で撮る。
    /// </summary>
    internal static BoxingCorners Corners(Contender red, Contender blue, int seed, MiniRing.Weapons.HandWear redWear, MiniRing.Weapons.HandWear blueWear,
        MiniRing.Weapons.WeaponKind? redWeapon, MiniRing.Weapons.WeaponKind? blueWeapon, ControlFactory blueControl, ControlFactory? redControl, bool referee = true)
        => new BoxingCorners(blue.Name, blueControl, redControl,
            CpuStyleJson: redControl is not null ? null : (blue.Create(seed) as BoxerBot)?.Style.ToJson(),
            PlayerWear: redWear, CpuWear: blueWear, PlayerName: red.Name,
            PlayerBody: red.Build, CpuBody: blue.Build, PlayerFace: red.Face, CpuFace: blue.Face,
            CpuWeapon: blueWeapon, PlayerWeapon: redWeapon, CpuTwoHanded: FormSwordsman.Handles(blueWeapon), PlayerTwoHanded: FormSwordsman.Handles(redWeapon),
            RedBotStyle: redWeapon is null && blueWeapon is null ? null : (red.Create(seed) as BoxerBot)?.Style ?? BotCatalog.Style("champion"),
            UseWeaponBots: redWeapon is not null || blueWeapon is not null, Referee: referee).StartingAtCorners(); // 試合はコーナーから（動画・テレビ中継・名簿のリーグ）

    /// <summary>名簿のキャラクターは、体格・顔・表示名をそのキャラクターの値にする（ボットの性格はスタイル）。</summary>
    internal static BoxingCorners WithCharacters(BoxingCorners corners, MiniRing.Characters.FighterCharacter? red, MiniRing.Characters.FighterCharacter? blue)
    {
        corners = red is null ? corners : corners with { PlayerBody = red.Build, PlayerFace = red.Look.Parts, PlayerLook = red.Look, PlayerFaceSpec = red.Face, PlayerLabel = red.HudName, PlayerStyle = red.Resolve(blue?.Id), PlayerWeaponStyle = red.WeaponStyle };
        return blue is null ? corners : corners with { CpuBody = blue.Build, CpuFace = blue.Look.Parts, CpuLook = blue.Look, CpuFaceSpec = blue.Face, CpuLabel = blue.HudName, CpuWeaponStyle = blue.WeaponStyle };
    }

    /// <summary>紹介の板の中身を 1 行に（台本の資料）。</summary>
    private static string Profile(TvCorner corner) => $"{corner.Name}（{(corner.FullName.Length > 0 ? corner.FullName + "・" : "")}{corner.Discipline}・{corner.Detail}）";

    /// <summary>休みの動作の中継の言葉（<see cref="MiniRing.Break.BreakBook"/> の名前から）。</summary>
    private static string BreakActionWords(string name) => name switch
    {
        MiniRing.Break.BreakBook.DeepBreath => "深呼吸をしている",
        MiniRing.Break.BreakBook.Drink => "ボトルの水を飲んでいる",
        MiniRing.Break.BreakBook.Wipe => "タオルで顔と首の汗を拭いている",
        MiniRing.Break.BreakBook.Phone => "スマホの画面を見ている",
        MiniRing.Break.BreakBook.Call => "スマホを耳に当てて電話をしている",
        MiniRing.Break.BreakBook.ShoulderRoll => "肩を回している",
        MiniRing.Break.BreakBook.NeckRoll => "首を回している",
        MiniRing.Break.BreakBook.SlapFace => "グローブで自分の顔を叩いて気合を入れている",
        MiniRing.Break.BreakBook.Ropes => "コーナーのロープに腕を掛けてもたれている",
        MiniRing.Break.BreakBook.Glare => "相手のコーナーをにらんでいる",
        MiniRing.Break.BreakBook.ShakeArms => "腕をぶらぶらと振ってほぐしている",
        MiniRing.Break.BreakEmotes.Wave => "観客に手を振っている",
        MiniRing.Break.BreakEmotes.WaveTwo => "両手を大きく振って観客に応えている",
        MiniRing.Break.BreakEmotes.RaiseArms => "両手を高く挙げて観客に応えている",
        MiniRing.Break.BreakEmotes.FistPump => "拳を高く突き上げている",
        MiniRing.Break.BreakEmotes.BlowKiss => "観客席へ投げキスをしている",
        MiniRing.Break.BreakEmotes.PointCrowd => "観客席を順に指さしている",
        MiniRing.Break.BreakEmotes.Bow => "観客と相手に向かって深くお辞儀をしている",
        MiniRing.Break.BreakEmotes.ChestBeat => "拳で自分の胸を叩いている",
        MiniRing.Break.BreakEmotes.HypeCrowd => "耳に手を当てて歓声をあおっている",
        MiniRing.Break.BreakEmotes.GutsPose => "拳を引きつけてガッツポーズをしている",
        MiniRing.Break.BreakEmotes.RopePerch => "ロープに腕を掛けて体を預けている",
        MiniRing.Break.BreakEmotes.Roar => "両腕を広げて雄叫びをあげている",
        MiniRing.Break.BreakEmotes.Flex => "腕を曲げて力こぶを見せている",
        MiniRing.Break.BreakEmotes.PointUp => "片腕を天へ突き上げて指さしている",
        MiniRing.Break.BreakEmotes.LookCrowd => "客席をゆっくり見渡している",
        MiniRing.Break.BreakEmotes.ShadowBox => "シャドーボクシングをしている",
        MiniRing.Break.BreakEmotes.Bounce => "膝を使って軽く弾んでいる",
        MiniRing.Break.BreakEmotes.NeckCrack => "首を左右に倒してほぐしている",
        MiniRing.Break.BreakEmotes.GloveClap => "グローブを打ち合わせている",
        MiniRing.Break.BreakEmotes.MouthGuard => "マウスピースを口に入れている",
        MiniRing.Break.BreakEmotes.StretchArms => "両腕を頭の上へ伸ばして背伸びをしている",
        MiniRing.Break.BreakEmotes.SideStretch => "片腕を頭の上へ伸ばして体の側面を伸ばしている",
        MiniRing.Break.BreakEmotes.ForwardBend => "腰から体を折って前屈をしている",
        MiniRing.Break.BreakEmotes.FixGloves => "グローブを締め直している",
        MiniRing.Break.BreakEmotes.WipeNose => "グローブの甲で鼻をこすっている",
        MiniRing.Break.BreakEmotes.Yawn => "大きなあくびをしている",
        MiniRing.Break.BreakEmotes.Dance => "体を揺らして踊っている",
        MiniRing.Break.BreakEmotes.Selfie => "スマホで自撮りをしている",
        MiniRing.Break.BreakEmotes.Mirror => "スマホのカメラで自分の顔を確かめている",
        MiniRing.Break.BreakEmotes.Prayer => "両手を合わせて祈っている",
        MiniRing.Break.BreakEmotes.CrossSelf => "胸の前で十字を切っている",
        MiniRing.Break.BreakEmotes.Salute => "額の横に手を当てて敬礼をしている",
        MiniRing.Break.BreakEmotes.Applause => "拍手をしている",
        MiniRing.Break.BreakEmotes.Handshake => "相手の方へ歩いて手を差し出している",
        MiniRing.Break.BreakEmotes.LookSky => "天を仰いでいる",
        MiniRing.Break.BreakEmotes.HeadInHands => "頭を抱えてうなだれている",
        MiniRing.Break.BreakEmotes.HandsOnKnees => "膝に手をついて息をついている",
        MiniRing.Break.BreakEmotes.SitDown => "腰を落として座り込もうとしている",
        MiniRing.Break.BreakEmotes.WipeTears => "グローブで目元をぬぐっている",
        MiniRing.Break.BreakEmotes.ShakeHead => "腰に手を当てて首を振っている",
        MiniRing.Break.BreakEmotes.SlamFist => "悔しさに拳を何度も振り下ろしている",
        MiniRing.Break.BreakEmotes.TowelHead => "タオルを頭からかぶってうつむいている",
        MiniRing.Break.BreakEmotes.SportsDrink => "スポーツドリンクを飲んでいる",
        MiniRing.Break.BreakEmotes.Coffee => "紙コップのコーヒーをすすっている",
        MiniRing.Break.BreakEmotes.EnergyDrink => "エナジードリンクの缶をあおっている",
        MiniRing.Break.BreakEmotes.Milk => "牛乳パックから牛乳を飲んでいる",
        MiniRing.Break.BreakEmotes.StrawDrink => "ストローで紙パックのジュースを飲んでいる",
        MiniRing.Break.BreakEmotes.Jelly => "ゼリー飲料を吸っている",
        MiniRing.Break.BreakEmotes.SpitWater => "口に含んだ水を吐き出している",
        MiniRing.Break.BreakEmotes.Banana => "バナナを食べている",
        MiniRing.Break.BreakEmotes.Onigiri => "おにぎりを頬張っている",
        MiniRing.Break.BreakEmotes.Sandwich => "サンドイッチをかじっている",
        MiniRing.Break.BreakEmotes.Chocolate => "チョコバーをかじっている",
        MiniRing.Break.BreakEmotes.Apple => "りんごをかじっている",
        MiniRing.Break.BreakEmotes.Donut => "ドーナツを食べている",
        MiniRing.Break.BreakEmotes.Cigarette => "たばこを吸って煙を吐いている",
        MiniRing.Break.BreakEmotes.Gum => "ガムを噛んでいる",
        MiniRing.Break.BreakEmotes.Headphones => "ヘッドホンで音楽を聴いて首でリズムを取っている",
        MiniRing.Break.BreakEmotes.Earbud => "イヤホンを耳に入れて聴き入っている",
        MiniRing.Break.BreakEmotes.Hum => "口ずさみながら体を揺らしている",
        _ => name,
    };

    /// <summary>握る武器の呼び名（中継の言葉）。拳なら null。</summary>
    private static string? GearName(MiniRing.Weapons.WeaponKind? weapon) => weapon switch
    {
        MiniRing.Weapons.WeaponKind.Katana => "刀",
        MiniRing.Weapons.WeaponKind.Longsword => "ロングソード",
        MiniRing.Weapons.WeaponKind.WoodenClub => "こんぼう",
        MiniRing.Weapons.WeaponKind.Staff => "棒",
        MiniRing.Weapons.WeaponKind.Jo => "杖",
        MiniRing.Weapons.WeaponKind.Bat => "バット",
        MiniRing.Weapons.WeaponKind.IronSword => "剣",
        _ => null,
    };

    /// <summary>各ラウンドの始まりの累計と今の累計から、ラウンドごとの得点。</summary>
    /// <summary>終わったラウンドごとの集計（与えたダメージ = 判定と同じ量の差分と、当てた打撃の数）。<paramref name="redNow"/> は赤が与えた総ダメージ（青の体の TotalDamage）。</summary>
    private static List<WvcRoundTally> Tallies(List<float> redStart, List<float> blueStart, List<int> redHits, List<int> blueHits, float redNow, float blueNow)
    {
        var red = RoundScores(redStart, redNow);
        var blue = RoundScores(blueStart, blueNow);
        return [.. red.Select((damage, i) => new WvcRoundTally(damage, blue[i], redHits[i], blueHits[i]))];
    }

    private static float[] RoundScores(List<float> roundStart, float now)
        => [.. roundStart.Select((start, i) => (i + 1 < roundStart.Count ? roundStart[i + 1] : now) - start)];

    /// <summary>名前とダメージの下のスタミナのメーター（左右）。</summary>
    private IEnumerable<UiShapeDraw> StaminaBars(BoxingGame game)
        => [.. TvHud.StaminaBar(new Vector2(28f, 92f), 140f, 8f, game.Player), .. TvHud.StaminaBar(new Vector2(width - 28f - 140f, 92f), 140f, 8f, game.Cpu)];

    /// <summary>タイトル・結果の文字を読めるよう、画面全体を暗くする。</summary>
    private UiShapeDraw[] Dim(float alpha) => [new(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, alpha))];

    /// <summary>試合中の上下の帯（名前・ダメージと、技・守りの字幕の下地）。</summary>
    private UiShapeDraw[] Bars() =>
    [
        new(new Vector2(width * 0.5f, 45f), new Vector2(width, 90f), new Vector4(0f, 0f, 0f, 0.55f), FillColorEnd: new Vector4(0f, 0f, 0f, 0f), GradientAxis: Vector2.UnitY),
        new(new Vector2(width * 0.5f, height - 70f), new Vector2(width, 140f), new Vector4(0f, 0f, 0f, 0f), FillColorEnd: new Vector4(0f, 0f, 0f, 0.6f), GradientAxis: Vector2.UnitY),
    ];

    /// <summary>
    /// 2 人の中点を、リングの横から少し見下ろす。<paramref name="orbit"/> [rad] でゆっくり回り込む（横 ±35°）。
    /// </summary>
    private (Matrix4x4 ViewProjection, Vector3 Eye) CameraAt(BoxingGame game, float orbit)
    {
        var axis = game.Cpu.ChestPosition - game.Player.ChestPosition;
        axis.Y = 0f;
        // 赤（プレイヤー側）が画面の左、青が右に映る側から見る（字幕の左右と合わせる）。
        var side = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY)) : -Vector3.UnitX;
        // 両者の全身（頭と腰）が入るよう、4 点の真ん中を見て、4 点の水平の広がりに比例して引く（45° の画角の横幅は距離の 1.47 倍）。
        // 以前は胸と胸の間だけで決めていたので、倒れた相手に覆いかぶさると 0.9 m まで寄り、体の一部しか映らなかった（2026-11-19、ユーザー）。打ち合う間合いでは今までの 0.9 m。
        Vector3[] points = [game.Player.HeadPosition, game.Player.PelvisPosition, game.Cpu.HeadPosition, game.Cpu.PelvisPosition];
        var min = points.Aggregate(Vector3.Min);
        var max = points.Aggregate(Vector3.Max);
        var mid = (min + max) * 0.5f;
        var spread = MathF.Max(axis.Length(), MathF.Max(max.X - min.X, max.Z - min.Z));
        var reach = MathF.Max(0.9f, (spread + 0.5f) / 1.4f);
        // 離れている間は回り込みを小さくする（斜めから見ると手前の 1 人が画面の外へ出る）。
        var angle = 0.6f * MathF.Sin(orbit) * (0.9f / reach);
        var direction = Vector3.Transform(side, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        // 見下ろす高さ: 立ち合いは手前のロープ越し（0.38 / 0.9 ≈ 23°）、倒れた選手がいるほど高く俯瞰する（最大で約 48°）。横たわった体は低い角度では重なって見えない。
        var lying = MathF.Max(TvDirector.Lying(game.Player), TvDirector.Lying(game.Cpu));
        var horizontal = reach * (1f - (0.3f * lying));
        var eye = mid + (direction * horizontal) + new Vector3(0f, (0.38f / 0.9f + (0.7f * lying)) * reach, 0f);
        var target = mid - new Vector3(0f, 0.06f, 0f);
        eye = LivingRoom.KeepInside(eye, TvDirector.CameraMargin);
        return (Camera.ViewProjection(eye, target, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f), eye);
    }

    /// <summary>
    /// テレビ中継の冒頭: 部屋ごとリングを引きで見下ろし、ゆっくり回り込む（生成直後の構える前の姿勢が小さくしか映らない）。
    /// </summary>
    private (Matrix4x4 ViewProjection, Vector3 Eye) WideCameraAt(BoxingGame game, float seconds)
    {
        var mid = (game.Player.ChestPosition + game.Cpu.ChestPosition) * 0.5f;
        var axis = game.Cpu.ChestPosition - game.Player.ChestPosition;
        axis.Y = 0f;
        var side = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY)) : -Vector3.UnitX;
        var direction = Vector3.Transform(side, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f - (0.08f * seconds)));
        var eye = mid + (direction * 1.35f) + new Vector3(0f, 0.75f, 0f); // 部屋の壁・天井に入らない距離
        eye = LivingRoom.KeepInside(eye, TvDirector.CameraMargin);
        return (Camera.ViewProjection(eye, mid - new Vector3(0f, 0.1f, 0f), Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f), eye);
    }

    /// <summary>
    /// 檻（リング）の外をゆっくり回るカメラ（番組の型の冒頭の場面）: 檻の中心を、少し高い所から、リングの大きさに比例した距離で見る。見た目だけ。
    /// </summary>
    private (Matrix4x4 ViewProjection, Vector3 Eye) CageOrbitCamera(BoxingGame game, float seconds)
    {
        var center = game.Layout.Center;
        var size = game.Layout.Size;
        var level = (game.Player.ChestPosition.Y + game.Cpu.ChestPosition.Y) * 0.5f;
        var azimuth = 0.4f + (0.32f * seconds);
        var eye = new Vector3(center.X + (MathF.Sin(azimuth) * size * 1.05f), level + (size * 0.4f), center.Z - (MathF.Cos(azimuth) * size * 1.05f));
        var target = new Vector3(center.X, level - (size * 0.04f), center.Z);
        eye = LivingRoom.KeepInside(eye, TvDirector.CameraMargin);
        return (Camera.ViewProjection(eye, target, Vector3.UnitY, 48f * MathF.PI / 180f, (float)width / height, 0.02f, 30f), eye);
    }

    private IReadOnlyList<ScreenTextDraw> Title(string red, string blue, float fade)
    {
        var w = width;
        var h = height;
        return
        [
            new("MINI RING", new Vector2(w * 0.5f, h * 0.24f), 84f, Fade(White, fade), TextAnchor.Center),
            new("ボット対ボット", new Vector2(w * 0.5f, h * 0.38f), 40f, Fade(Grey, fade), TextAnchor.Center),
            new($"{red}  (赤)", new Vector2(w * 0.5f - 40f, h * 0.62f), 48f, Fade(Red, fade), TextAnchor.Right),
            new("vs", new Vector2(w * 0.5f, h * 0.62f), 36f, Fade(Grey, fade), TextAnchor.Center),
            new($"(青)  {blue}", new Vector2(w * 0.5f + 40f, h * 0.62f), 48f, Fade(Blue, fade), TextAnchor.Left),
            new("技・守り・打撃音はすべてボットと物理が決めたもの", new Vector2(w * 0.5f, h * 0.84f), 30f, Fade(Grey, fade), TextAnchor.Center),
        ];
    }

    private List<ScreenTextDraw> MatchOverlay(string red, string blue, Caption redCaption, Caption blueCaption,
        List<(string Text, Vector4 Color, float Until)> flashes, float redDamage, float blueDamage, float time, float? seconds, BoxingGame game, Matrix4x4 viewProjection)
    {
        var w = width;
        var h = height;
        var list = new List<ScreenTextDraw>
        {
            new(red, new Vector2(28f, 22f), 30f, Red),
            new(blue, new Vector2(w - 28f, 22f), 30f, Blue, TextAnchor.Right),
            new(string.Create(CultureInfo.InvariantCulture, $"DAMAGE {redDamage:0}"), new Vector2(28f, 60f), 22f, Red),
            new(string.Create(CultureInfo.InvariantCulture, $"DAMAGE {blueDamage:0}"), new Vector2(w - 28f, 60f), 22f, Blue, TextAnchor.Right),
            new(seconds is { } limit ? string.Create(CultureInfo.InvariantCulture, $"{MathF.Max(0f, limit - time):0}") : string.Create(CultureInfo.InvariantCulture, $"{(int)time / 60}:{(int)time % 60:00}"), new Vector2(w * 0.5f, 22f), 34f, White, TextAnchor.Center),
        };
        list.AddRange(DownCountHud.Text(game, viewProjection, new Vector2(w, h), f => f == game.Player ? red : blue, f => f == game.Player ? Red : Blue));
        if (game.Rules is not null)
        {
            // テレビ中継: 上の名前・ダメージ・時間の代わりに、得点表と時計。
            list.RemoveRange(0, 5);
            if (_faceCam is null)
            {
                list.InsertRange(0, TvHud.Text(game, new Vector2(w, h), red, blue));
            }
        }

        // 顔の窓を左下・右下に置くときは、字幕を窓の内側へずらす。
        var captionInset = tv && _faceCam is not null ? _faceCam.Panel(new Vector2(w, h), FaceSide.Left).WidthPx + 48f : 28f;
        var captionY = tv ? _captionY : h - 96f;
        AddCaption(list, redCaption, new Vector2(captionInset, captionY), TextAnchor.Left, Red, time);
        AddCaption(list, blueCaption, new Vector2(w - captionInset, captionY), TextAnchor.Right, Blue, time);
        for (var i = 0; i < flashes.Count; i++)
        {
            var (text, color, until) = flashes[i];
            var age = 0.7f - (until - time);
            list.Add(new ScreenTextDraw(text, new Vector2(w * 0.5f, (h * 0.22f) + (i * 44f) - (age * 30f)), 40f, Fade(color, MathF.Min(1f, (until - time) / 0.3f)), TextAnchor.Center));
        }

        return list;
    }

    private IReadOnlyList<ScreenTextDraw> Result(string red, string blue, float redDamage, float blueDamage, int winner, float fade, string? finish = null)
    {
        var w = width;
        var h = height;
        var verdict = winner == 0 ? "DRAW" : $"{(winner > 0 ? red : blue)} WINS";
        return
        [
            // K.O. の種類（"K.O. — BRAIN DESTROYED" / "10 COUNT K.O." / "RING OUT"。勝者の部分は上の大きな文字が言うので落とす）。
            .. finish is { Length: > 0 }
                ? new ScreenTextDraw[] { new(finish.Split("  ")[0], new Vector2(w * 0.5f, h * 0.14f), 40f, Fade(Gold, fade), TextAnchor.Center) }
                : [],
            new(verdict, new Vector2(w * 0.5f, h * 0.26f), 72f, Fade(winner > 0 ? Red : winner < 0 ? Blue : Gold, fade), TextAnchor.Center),
            new(string.Create(CultureInfo.InvariantCulture, $"{red}  {redDamage:0}"), new Vector2(w * 0.5f - 30f, h * 0.45f), 40f, Fade(Red, fade), TextAnchor.Right),
            new(string.Create(CultureInfo.InvariantCulture, $"{blueDamage:0}  {blue}"), new Vector2(w * 0.5f + 30f, h * 0.45f), 40f, Fade(Blue, fade), TextAnchor.Left),
            new("与えたダメージ", new Vector2(w * 0.5f, h * 0.55f), 30f, Fade(Grey, fade), TextAnchor.Center),
            new("ボット同士を戦わせて PDCA で改善", new Vector2(w * 0.5f, h * 0.82f), 30f, Fade(Grey, fade), TextAnchor.Center),
        ];
    }

    /// <summary>コーナーの字幕: 新しく始めたコンビネーション（カウンターを含む）と、いま出している守り。</summary>
    private static void UpdateCaption(Caption caption, List<IFighterControl> bots, Dictionary<string, int> seen, float time)
    {
        if (bots.Count == 0 || bots[^1] is not BoxerBot bot)
        {
            return;
        }

        foreach (var (name, count) in bot.Stats.Combos)
        {
            var key = $"{bots.Count}:{name}";
            if (seen.GetValueOrDefault(key) < count)
            {
                seen[key] = count;
                caption.Combo = ComboLabel(name);
                caption.ComboTime = time;
            }
        }

        if (bot.CurrentDefense != Defense.None)
        {
            caption.Defense = DefenseLabel(bot.CurrentDefense);
            caption.DefenseTime = time;
        }
    }

    private static void AddCaption(List<ScreenTextDraw> list, Caption caption, Vector2 at, TextAnchor anchor, Vector4 color, float time)
    {
        if (caption.Combo is { } combo && time - caption.ComboTime < 1.1f)
        {
            list.Add(new ScreenTextDraw(combo, at, 34f, Fade(color, 1f - MathF.Max(0f, (time - caption.ComboTime - 0.8f) / 0.3f)), anchor));
        }

        if (caption.Defense is { } defense && time - caption.DefenseTime < 0.6f)
        {
            list.Add(new ScreenTextDraw(defense, at + new Vector2(0f, 46f), 30f, Fade(Gold, 1f - MathF.Max(0f, (time - caption.DefenseTime - 0.3f) / 0.3f)), anchor));
        }
    }

    private static string ComboLabel(string name) => name switch
    {
        "slip-counter" => "スリップからカウンター",
        "duck-counter" => "ダッキングからカウンター",
        "pull-counter" => "スウェーからプルカウンター",
        "block-counter" => "ブロックからカウンター",
        "weave-counter" => "ウィービングからカウンター",
        "angle-counter" => "サイドステップからカウンター",
        "step-counter" => "バックステップからカウンター",
        "stop-jab" => "ストップジャブ",
        // 名簿の型なら技を並べる。知らない名前（ボットが後から足した型）は名前のまま出す。
        _ => Combo.All.FirstOrDefault(c => c.Name == name) is { } combo
            ? $"{name}  " + string.Join(" → ", combo.Steps.Select(TechniqueLabel))
            : name,
    };

    private static string TechniqueLabel(Technique t) => t switch
    {
        Technique.Jab => "ジャブ",
        Technique.Cross => "ストレート",
        Technique.LeadHook => "左フック",
        Technique.RearHook => "右フック",
        Technique.LeadUppercut => "左アッパー",
        Technique.RearUppercut => "右アッパー",
        Technique.BodyJab => "ボディジャブ",
        Technique.BodyCross => "ボディストレート",
        Technique.LeadBodyHook => "左ボディフック",
        Technique.RearBodyHook => "右ボディフック",
        Technique.Overhand => "オーバーハンド",
        Technique.Feint => "フェイント",
        _ => t.ToString(),
    };

    private static string DefenseLabel(Defense d) => d switch
    {
        Defense.Block => "ブロック",
        Defense.Duck => "ダッキング",
        Defense.Slip => "スリップ",
        Defense.Sway => "スウェー",
        Defense.StepBack => "バックステップ",
        Defense.Weave => "ウィービング",
        Defense.SideStep => "サイドステップ",
        _ => "",
    };

    private static Vector4 Fade(Vector4 color, float alpha) => color with { W = Math.Clamp(alpha, 0f, 1f) };

    /// <summary>16-bit モノラル PCM を <paramref name="at"/> 秒の位置に足し込む。</summary>
    internal static void Mix(float[] audio, byte[] pcm, float at, float gain)
    {
        var start = (int)(at * HitSound.SampleRate);
        for (var i = 0; i + 1 < pcm.Length && start + (i / 2) < audio.Length; i += 2)
        {
            audio[start + (i / 2)] += BitConverter.ToInt16(pcm, i) / 32768f * gain;
        }
    }

    internal static void WriteWav(string path, float[] audio, float seconds)
    {
        var count = Math.Min(audio.Length, (int)(seconds * HitSound.SampleRate));
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + (count * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(HitSound.SampleRate);
        writer.Write(HitSound.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            writer.Write((short)(MathF.Tanh(audio[i]) * 32000f)); // 重なった打撃音は柔らかくクリップ
        }
    }

    private Process StartFfmpeg(string videoPath)
    {
        var info = new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{videoPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        return Process.Start(info) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
    }

    private static void Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })
            ?? throw new IOException($"{file} を起動できません。");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException($"{file} failed with exit code {process.ExitCode}.");
        }
    }

    private static IFighterControl Keep(List<IFighterControl> list, IFighterControl control)
    {
        list.Add(control);
        return control;
    }

    private sealed class Caption
    {
        public string? Combo { get; set; }
        public float ComboTime { get; set; }
        public string? Defense { get; set; }
        public float DefenseTime { get; set; }

        /// <summary>最後に実況の資料へ書いた守り（続いている間は 1 回だけ書く）。</summary>
        public string? LastNotedDefense { get; set; }
    }
}
