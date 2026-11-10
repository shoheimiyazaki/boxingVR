using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

#pragma warning disable CA1716 // "Step" collides with a VB keyword, but it is this engine's core
                              // vocabulary (PhysicsWorld.Step, FixedStep, the agent's step verb);
                              // renaming it here would make the contract read unlike everything else.

namespace Aigis.Player;

/// <summary>
/// The contract every <c>games/Game.*</c> title implements: a fixed-step <see cref="Step"/>
/// driven by a decoupled input struct, plus one scene description consumed by both the live
/// window (<see cref="GameHost"/>) and headless verification (<c>Aigis.Playtest</c>).
/// <para>
/// This is deliberately a compiler-checked interface rather than a documented convention: a
/// game that satisfies it is automatically playtestable by a scripted input sequence, rendered
/// off-screen, and reported on — with zero game-specific plumbing. A game that drifts from it
/// fails to build instead of quietly growing a second, unverifiable shape.
/// </para>
/// </summary>
/// <typeparam name="TInput">The game's own per-tick intent struct. Never a live device read —
/// the identical value must be producible by a script, which is what makes a run reproducible.</typeparam>
public interface IGame<in TInput> : IDisposable
{
    /// <summary>The game's physics world. Hosts observe it (state snapshots, debug captures);
    /// they never step it directly — <see cref="Step"/> owns advancing the simulation.</summary>
    public PhysicsWorld World { get; }

    /// <summary>One-line, low-cardinality state summary (e.g. <c>"Playing shots=1/3 score=2"</c>).
    /// Hosts print it when it changes and playtests record it as a timeline, so it must change
    /// only on a meaningful transition — never every frame (no raw positions or live timers).</summary>
    public string StatusLine { get; }

    /// <summary>True once the round is decided (won/lost/round-over). Lets a scripted playtest
    /// stop at the natural end without a per-game predicate.</summary>
    public bool IsOver { get; }

    /// <summary>Advances exactly one fixed step under <paramref name="input"/>.</summary>
    public void Step(TInput input, float dt);

    /// <summary>Describes the current scene once, for whoever is drawing it — a live swapchain
    /// or a headless off-screen render. The only scene description in the game.</summary>
    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables();

    /// <summary>
    /// This frame's particle billboards — footstep dust, blood spray, explosions (see
    /// <see cref="ParticleSystem"/>, whose <c>BuildInstances()</c> is the usual one-line body).
    /// Drawn over the scene from <see cref="BuildRenderables"/> and under its debug lines.
    /// <para>
    /// A separate member with a default rather than a third element in
    /// <see cref="BuildRenderables"/>'s tuple: effects are optional decoration, and widening that
    /// tuple would have forced an edit into every existing game, playtest and render test for a
    /// feature most of them do not use. The split also keeps the honest distinction that
    /// particles are stepped by the game (in <see cref="Step"/>, so a scripted replay reproduces
    /// them exactly) and merely <i>read</i> here.
    /// </para>
    /// </summary>
    public IReadOnlyList<ParticleInstance> BuildParticles() => [];

    /// <summary>
    /// This frame's world-space ground decals — the dynamic stain sheets a blood trail is painted
    /// into (see <see cref="DecalQuadDraw"/>). Drawn between the opaque scene and
    /// <see cref="BuildParticles"/>'s billboards, because a stain is floor and a billboard floats
    /// above it.
    /// <para>
    /// Same instance-identity contract as <see cref="BuildOverlayImages"/>, for the same reason:
    /// build each <see cref="DynamicTexture"/> once (per level, per surface) and return that SAME
    /// instance every frame. The renderers hold the GPU image per instance and re-upload only when
    /// its <see cref="DynamicTexture.Revision"/> moves, so a frame in which nothing bleeds costs
    /// nothing; a caller that mints a sheet per frame is failed loudly rather than leaked silently.
    /// </para>
    /// <para>
    /// Declared here, on the contract, so it needs no per-game plumbing: the windowed
    /// <see cref="GameHost"/> and the headless <c>Aigis.Playtest</c> runner both forward it, which
    /// is what puts the same stains in a real window and in an agent's filmstrip (hard rule 6).
    /// Painting into the sheet belongs in <see cref="Step"/>, not here — this member only
    /// <i>reads</i>, exactly as <see cref="BuildParticles"/> does, so a scripted replay reproduces
    /// the stains it produced.
    /// </para>
    /// </summary>
    public IReadOnlyList<DecalQuadDraw> BuildDecals() => [];

    /// <summary>
    /// This frame's voxel volumes (<see cref="Aigis.Render.Voxels.VoxelVolumeDraw"/>): each one's surface is built on the GPU
    /// from its 3D texture and drawn with the opaque scene. Same instance-identity contract as <see cref="BuildDecals"/>:
    /// keep one <see cref="Aigis.Render.Voxels.VoxelVolume"/> per volume and return it every frame; the renderer uploads
    /// only when its revision moves. Only reads — change the volumes in <see cref="Step"/>.
    /// </summary>
    public IReadOnlyList<Aigis.Render.Voxels.VoxelVolumeDraw> BuildVoxelVolumes() => [];

    /// <summary>Screen-space HUD text for this frame. <paramref name="viewProjection"/> is the
    /// exact matrix the 3D scene beneath it is rendered with this frame (so a world-anchored HUD
    /// element projected via <see cref="Camera.TryWorldToScreen"/> is correctly placed), and
    /// <paramref name="screenSizePixels"/> is that same frame's render-target size. Defaults to
    /// no overlay for a game without a HUD.</summary>
    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels) => [];

    /// <summary>
    /// TASK-159: this frame's textured screen-space HUD elements — treatment/item icons, a patient
    /// portrait — drawn over <see cref="BuildOverlayText"/>'s text. Same parameters, same pixel
    /// convention (origin top-left, Y down), so an icon and its label are positioned in one
    /// coordinate system. Defaults to none, so no existing game changes.
    /// <para>
    /// Every <see cref="OverlayImageDraw.Texture"/> must be a long-lived instance — build it in the
    /// game's constructor or a static field and return the SAME instance each frame. The renderers
    /// cache the GPU upload by instance identity and will throw rather than silently leak if a
    /// caller mints a new <see cref="TextureData"/> per frame; see <see cref="TextureData"/>'s doc
    /// comment.
    /// </para>
    /// <para>
    /// Declared here, on the contract, precisely so it needs no per-game plumbing: both the windowed
    /// <see cref="GameHost"/> and the headless <c>Aigis.Playtest</c> runner forward it, so a game
    /// implementing this gets icons in a real window AND in an agent's filmstrip with no extra
    /// wiring (CLAUDE.md's <c>games/</c> contract, and hard rule 6).
    /// </para>
    /// </summary>
    public IReadOnlyList<OverlayImageDraw> BuildOverlayImages(Matrix4x4 viewProjection, Vector2 screenSizePixels) => [];

    /// <summary>
    /// This frame's screen-space UI surfaces — the cards, panels, pills, dividers, gauge tracks,
    /// focus rings, scrims and drop shadows a HUD is built out of (see <see cref="UiShapeDraw"/>).
    /// Same parameters and same pixel convention as <see cref="BuildOverlayText"/> and
    /// <see cref="BuildOverlayImages"/>, so a card, the icon on it and the label over it are all
    /// positioned in one coordinate system.
    /// <para>
    /// <b>Drawn UNDER the other two.</b> Shapes first, then icons, then text: a panel is the
    /// background for its own contents, which is the one ordering that never needs a per-element
    /// override. Within this list, order is back-to-front (painter's algorithm) — emit a shadow
    /// before the card that casts it.
    /// </para>
    /// <para>
    /// Declared here, on the contract, for the same reason <see cref="BuildOverlayImages"/> is: both
    /// the windowed <see cref="GameHost"/> and the headless <c>Aigis.Playtest</c> runner forward it,
    /// so a game gets real surfaces in a window AND in an agent's filmstrip with no extra wiring
    /// (hard rule 6). Defaults to none, so no existing game changes.
    /// </para>
    /// <para>
    /// Unlike <see cref="BuildOverlayImages"/> there is nothing to cache and no residency cap: a
    /// shape is pure push-constant data with no GPU resource behind it, so building a fresh list
    /// every frame — including shapes whose size animates — is the intended usage, not a leak.
    /// </para>
    /// </summary>
    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels) => [];

    /// <summary>
    /// Optional 3D primitives drawn ON TOP of <see cref="BuildOverlayShapes"/> and under overlay
    /// images/text, through the layer's own camera — e.g. small character figures walking on a menu
    /// panel. See <see cref="ForegroundLayer"/> for the draw order and what the layer does not do
    /// (no shadows; lit by the scene's lighting). <paramref name="screenSizePixels"/> is the same
    /// render size <see cref="BuildOverlayShapes"/> receives, so the game can build a camera that
    /// maps its figures onto panel pixels.
    /// <para>Forwarded by <see cref="GameHost"/> and the headless playtest runner alike (hard rule
    /// 6). In an automatic split-screen layout (more than one view, not game-supplied) the host does
    /// NOT draw it, because the layer's camera would have to be re-fitted to the primary viewport;
    /// menus are single-view. Defaults to none, so no existing game changes.</para>
    /// </summary>
    public ForegroundLayer? BuildForegroundLayer(Vector2 screenSizePixels) => null;

    /// <summary>
    /// TASK-130: called once per <see cref="GameHost"/> update tick with whether the system menu
    /// key (Escape / gamepad Start) is currently held — <b>every</b> tick, not only on the rising
    /// edge, so an override can reset its own "was held" bookkeeping on the tick the key is
    /// released (the same rising-edge-inside-the-game convention <c>TriageGame.Step</c> already
    /// uses for <c>ConfirmPressed</c>/<c>PauseTogglePressed</c>; see that type's own doc comment).
    /// Returns whether the game handled the key itself this tick, so <see cref="GameHost"/> must
    /// NOT fall back to closing the window.
    /// <para>
    /// <b>Additive, not a breaking change to the contract.</b> The default always returns
    /// <c>false</c>, which reproduces every existing <c>games/Game.*</c> title's original
    /// behavior exactly: <see cref="GameHost"/> still closes the window outright while the key is
    /// held, because nothing here claims to have handled it. Only <c>Game.Triage</c>'s
    /// <c>TriageShell</c> overrides this (to open/close its pause menu instead) — see that type's
    /// own doc comment for why the state machine lives there and not in <see cref="GameHost"/>.
    /// </para>
    /// </summary>
    public bool HandleSystemMenuKey(bool systemMenuKeyHeld) => false;

    /// <summary>
    /// TASK-130: true once the game itself has decided the window should close — e.g. "quit game"
    /// chosen from a system menu opened via <see cref="HandleSystemMenuKey"/>. <see cref="GameHost"/>
    /// checks this every tick in addition to the direct Escape/Start path, so a menu-driven quit
    /// and a direct-key quit both end up closing the window through the same one call. Default
    /// <c>false</c>: a game that never overrides <see cref="HandleSystemMenuKey"/> never sets this
    /// either, so it plays no part in that game's (unchanged) direct-quit behavior.
    /// </summary>
    public bool QuitRequested => false;

    /// <summary>
    /// Whether this game wants its window to cover the whole display — the player's own display
    /// setting, offered by <c>Game.Triage</c>'s options screen (2026-09-03).
    /// <para>
    /// <b>A continuous preference, not a one-shot request</b>, unlike <see cref="QuitRequested"/>
    /// above: <see cref="GameHost"/> reads it every tick and assigns it to the window, which ignores
    /// a value it is already at. That direction — the game owns the wish, the host owns the window —
    /// is what keeps a game from needing a window reference it is deliberately never given (the same
    /// split <see cref="QuitRequested"/> and <see cref="HandleSystemMenuKey"/> already make), and it
    /// means a game may set it from a menu, from a saved setting, or from nothing at all.
    /// </para>
    /// <para>
    /// Default <see langword="false"/>: every existing <c>games/Game.*</c> title opens windowed
    /// exactly as before. A game that never overrides this never causes a single state assignment.
    /// </para>
    /// </summary>
    public bool PrefersFullscreen => false;

    /// <summary>
    /// TASK-149: this frame's whole-scene lighting (directional/ambient/point lights), passed to
    /// both hosts' render call. Defaults to <see cref="SceneLighting.Default"/> — reproduces every
    /// existing <c>games/Game.*</c> title's original (implicit) lighting exactly, since neither
    /// renderer's <c>lighting</c> parameter was ever set by a game before this task (see
    /// tasks/backlog-details.md's "Lighting and shading programme" shared context — TASK-148's
    /// own commit note records that <c>games/Game.Triage</c> "still sets no <c>SceneLighting</c>
    /// at all today"). Same additive shape as <see cref="HandleSystemMenuKey"/>/
    /// <see cref="QuitRequested"/> above — a default body means every existing implementer keeps
    /// compiling and behaving identically without touching this file again; only a game that
    /// actually authors point lights (currently just <c>Game.Triage</c>, from its level's
    /// authored <c>LIGHT</c> entries) needs to override it.
    /// </summary>
    public SceneLighting BuildLighting() => SceneLighting.Default;

    /// <summary>
    /// TASK-150 fix (transform-auditor HIGH finding): this frame's optional sun shadow-map
    /// configuration, passed to both hosts' render call exactly like <see cref="BuildLighting"/>
    /// is. Defaults to <see langword="null"/> — reproduces every existing <c>games/Game.*</c>
    /// title's original behaviour exactly (no shadow pass), since neither renderer's
    /// <c>shadow</c> parameter was ever set by a game before this fix; only
    /// <c>tools/Aigis.Benchmark</c> and render-only tests passed one directly to the renderer,
    /// which is exactly the gap this member closes — without it, no real host (window or
    /// headless playtest) ever ran the shadow pass at all, no matter what a game authored.
    /// <para>
    /// Same additive shape as <see cref="BuildLighting"/>/<see cref="HandleSystemMenuKey"/> above:
    /// a default body means every existing implementer keeps compiling and behaving identically
    /// without touching this file again; only a game that actually wants the sun shadow pass live
    /// (currently just <c>Game.Triage</c>, whose <c>MaterialKind.ShadowCastOnly</c> interior
    /// ceilings otherwise occlude nothing observable) needs to override it.
    /// </para>
    /// </summary>
    public ShadowMapConfig? BuildShadow() => null;

    /// <summary>
    /// 2026-10-05: this frame's screen-space finishing pass (tilt-shift, bloom, grade, vignette — see
    /// <see cref="PostProcessSettings"/>), passed to the single-view render call like
    /// <see cref="BuildShadow"/>. <see langword="null"/>, the default, draws exactly as before; a
    /// split-screen frame ignores it (its views would each need their own focus band).
    /// </summary>
    public PostProcessSettings? BuildPostProcess() => null;

    /// <summary>
    /// 2026-10-16 (画質設定): the fraction of the window's resolution this game wants its 3D scene drawn
    /// at — a player's quality choice for a GPU that cannot fill the window. Honoured on frames that
    /// have a finishing pass (<see cref="BuildPostProcess"/>), whose offscreen scene target is what
    /// shrinks; the HUD always draws at full resolution. 1, the default, is full size. A window-only
    /// presentation setting: the headless renderer ignores it, so a screenshot shows the scale-1 picture.
    /// </summary>
    public float RenderScale => 1f;

    /// <summary>
    /// 2026-08-20: what the camera sees where no geometry is drawn — the render clear colour, passed
    /// to both hosts' single-view render call exactly like <see cref="BuildLighting"/> is.
    /// <para>
    /// Added because a game that owns its own sun (Triage's <c>DaylightCycle</c>) must also own the
    /// sky: a fixed dusk-blue backdrop behind a midday scene reads as fog, and behind a midnight
    /// scene it is brighter than anything in the frame. A clear colour is not a light and takes no
    /// part in shading — it is the one pixel value the lighting cannot reach.
    /// </para>
    /// <para>
    /// Defaults to the literal both hosts hardcoded before this member existed, so every
    /// <c>games/Game.*</c> title that does not override it renders byte-identically. The SPLIT-view
    /// path (a multi-viewport layout) deliberately keeps its own black
    /// gutter clear instead of calling this — see <see cref="GameHost"/> for why storeys read as
    /// distinct screens.
    /// </para>
    /// </summary>
    public Vector4 BuildClearColor() => DefaultClearColor;

    /// <summary>The pre-2026-08-20 hardcoded host clear colour, named so
    /// <see cref="BuildClearColor"/>'s default and every host's fallback are the same one value
    /// rather than three copies of a literal.</summary>
    public static readonly Vector4 DefaultClearColor = new(0.10f, 0.11f, 0.14f, 1f);
}
