using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Replay;

/// <summary>Aigis の <see cref="GameHost"/> 用: 普段どおり遊びながら、すべてのステップを自動で記録する。</summary>
public sealed class RecordingGame : IGame<BoxingInput>
{
    private readonly BoxingGame _game;
    private readonly ReplayRecorder _recorder;

    public RecordingGame(BoxingGame game, ReplayRecorder recorder)
    {
        _game = game;
        _recorder = recorder;
    }

    public PhysicsWorld World => _game.World;
    public string StatusLine => _game.StatusLine;
    public bool IsOver => _game.IsOver;

    public void Step(BoxingInput input, float dt) => _recorder.Step(_game, input, dt);

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables() => _game.BuildRenderables();

    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes() => _game.BuildVoxelVolumes();

    public IReadOnlyList<ParticleInstance> BuildParticles() => _game.BuildParticles();

    public SceneLighting BuildLighting() => _game.BuildLighting();

    public ShadowMapConfig? BuildShadow() => _game.BuildShadow();

    public PostProcessSettings? BuildPostProcess() => _game.BuildPostProcess();

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
        => _game.BuildOverlayText(viewProjection, screenSizePixels);

    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels)
        => _game.BuildOverlayShapes(viewProjection, screenSizePixels);

    public void Dispose() => _recorder.Dispose();
}

/// <summary>
/// Aigis の <see cref="GameHost"/> 用: 記録した試合を実時間で再生する（ホストの入力は使わない）。
/// VR で 72〜120Hz で記録したものも、記録の時間刻みどおりに進めるので同じ速さで見える。
/// </summary>
public sealed class ReplayGame : IGame<BoxingInput>
{
    private readonly ReplayPlayer _player;
    private readonly List<HitEvent> _frameHits = [];
    private readonly List<ShatterEvent> _frameShatters = [];
    private readonly List<DebrisLanding> _frameLandings = [];
    private readonly List<SwingEvent> _frameSwings = [];
    private float _clock;

    public ReplayGame(ReplayData data)
    {
        _player = new ReplayPlayer(data);
    }

    public BoxingGame Game => _player.Game;

    /// <summary>直前の <see cref="Step"/>（1 フレームに何ステップ進んでも）で起きた打撃。</summary>
    public IReadOnlyList<HitEvent> FrameHits => _frameHits;

    /// <summary>直前の <see cref="Step"/> で砕けたボクセル。</summary>
    public IReadOnlyList<ShatterEvent> FrameShatters => _frameShatters;

    /// <summary>直前の <see cref="Step"/> で床に落ちた破片。</summary>
    public IReadOnlyList<DebrisLanding> FrameLandings => _frameLandings;

    /// <summary>直前の <see cref="Step"/> で振り抜かれた拳。</summary>
    public IReadOnlyList<SwingEvent> FrameSwings => _frameSwings;

    /// <summary>このホストのフレームの間に拳が体をかすめた瞬間。</summary>
    public IReadOnlyList<GrazeEvent> FrameGrazes => _frameGrazes;

    /// <summary>このホストのフレームの間に部位が切り離された瞬間。</summary>
    public IReadOnlyList<SeverEvent> FrameSevers => _frameSevers;

    private readonly List<GrazeEvent> _frameGrazes = [];
    private readonly List<ContactSoundEvent> _frameContacts = [];

    /// <summary>この描画フレームの間に、物どうしが何も壊さずにぶつかった瞬間。</summary>
    public IReadOnlyList<ContactSoundEvent> FrameContacts => _frameContacts;
    private readonly List<SeverEvent> _frameSevers = [];

    /// <summary>このホストのフレームの間に鳴ったゴング（複数なら最後のもの）。</summary>
    public RoundBell FrameBell { get; private set; }
    public PhysicsWorld World => _player.Game.World;
    public bool IsOver => false;

    /// <summary>ホストが変化のたびに出力する行なので、刻々と変わる再生時刻は入れない（時刻は画面の表示に出す）。</summary>
    public string StatusLine => $"REPLAY {(_player.FirstDivergence is null ? "" : Verdict + "  ")}{_player.Game.StatusLine}";

    private string Verdict => _player.FirstDivergence is { } step
        ? $"DIVERGED at step {step}"
        : $"match {_player.ChecksumsMatched}";

    /// <summary>ホストの時間を進め、記録の時計がそこに追いつくまで再生する。入力は記録のものを使う。</summary>
    public void Step(BoxingInput input, float dt)
    {
        _clock += dt;
        _frameHits.Clear();
        _frameShatters.Clear();
        _frameLandings.Clear();
        _frameSwings.Clear();
        FrameBell = RoundBell.None;
        _frameGrazes.Clear();
        _frameSevers.Clear();
        _frameContacts.Clear();
        _player.PlayUntil(_clock, game =>
        {
            _frameGrazes.AddRange(game.RecentGrazes);
            _frameContacts.AddRange(game.RecentContacts);
            _frameSevers.AddRange(game.RecentSevers);
            FrameBell = game.RecentBell == RoundBell.None ? FrameBell : game.RecentBell;
            _frameHits.AddRange(game.RecentHits);
            _frameShatters.AddRange(game.RecentShatters);
            _frameLandings.AddRange(game.RecentLandings);
            _frameSwings.AddRange(game.RecentSwings);
        });
    }

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables() => _player.Game.BuildRenderables();

    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes() => _player.Game.BuildVoxelVolumes();

    public IReadOnlyList<ParticleInstance> BuildParticles() => _player.Game.BuildParticles();

    public SceneLighting BuildLighting() => _player.Game.BuildLighting();

    public ShadowMapConfig? BuildShadow() => _player.Game.BuildShadow();

    public PostProcessSettings? BuildPostProcess() => _player.Game.BuildPostProcess();

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        var list = new List<ScreenTextDraw>(_player.Game.BuildOverlayText(viewProjection, screenSizePixels))
        {
            new(string.Create(CultureInfo.InvariantCulture, $"REPLAY  {_player.Time:0.0} / {_player.Data.Duration:0.0} s  {Verdict}{(_player.Finished ? "  (END)" : "")}"),
                new Vector2(screenSizePixels.X * 0.5f, 56f), 22f,
                _player.FirstDivergence is null ? new Vector4(0.6f, 1f, 0.6f, 1f) : new Vector4(1f, 0.4f, 0.3f, 1f), TextAnchor.Center),
        };
        return list;
    }

    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels)
        => _player.Game.BuildOverlayShapes(viewProjection, screenSizePixels);

    public void Dispose() => _player.Dispose();
}
