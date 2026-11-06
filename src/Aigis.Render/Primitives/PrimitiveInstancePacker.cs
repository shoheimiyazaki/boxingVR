using System.Numerics;
using System.Runtime.CompilerServices;

namespace Aigis.Render;

/// <summary>
/// One <c>vkCmdDraw</c>'s worth of a packed frame: a contiguous run of instances that share the
/// geometry to be drawn.
/// <para>
/// <b>A named type rather than the tuple this used to be</b>, because 2026-09-03 gave the run a
/// second way to say what geometry it is — <see cref="Mesh"/> — and a four-element tuple of
/// <c>(kind, offset, count, mesh)</c> reads at the call site as nothing at all. The field names are
/// unchanged, so a consumer that only asked for <c>Kind</c>/<c>Offset</c>/<c>Count</c> needs no edit.
/// </para>
/// </summary>
/// <param name="Kind">The unit shape whose shared vertex buffer this run draws — meaningful only
/// when <paramref name="Mesh"/> is <see langword="null"/>.</param>
/// <param name="Offset">Where the run starts, in INSTANCES (not floats), for use directly as
/// <c>vkCmdDraw</c>'s <c>firstInstance</c>.</param>
/// <param name="Count">How many instances are in the run — <c>vkCmdDraw</c>'s
/// <c>instanceCount</c>.</param>
/// <param name="Mesh">The imported geometry to draw instead of <paramref name="Kind"/>'s unit shape,
/// or <see langword="null"/> for an ordinary primitive run. A renderer binds this mesh's own vertex
/// buffer at binding 0 in place of the shared one; everything else about the draw is identical,
/// which is why an imported model takes the same lighting, shadows and depth prepass as a cube.</param>
public readonly record struct PrimitiveBatch(PrimitiveKind Kind, int Offset, int Count, MeshData? Mesh = null);

/// <summary>
/// TASK-152: turns a frame's <see cref="PrimitiveInstance"/> list into one instance-rate vertex
/// buffer, grouped into up to three contiguous blocks — Cube, then Sphere, then Capsule — so each
/// renderer issues at most one <c>vkCmdDraw</c> per <see cref="PrimitiveKind"/> actually present,
/// instead of one per instance. A humanoid's 18 parts or a 6-patient scene's ~700-900 instances
/// (see tasks/backlog-details.md#task-152) cost the same handful of draw calls as a single cube.
/// <para>
/// Lives outside either renderer for the same reason <see cref="ParticlePacker"/> does (see its
/// doc comment): it is identical CPU-side work for <see cref="HeadlessRenderer"/> and
/// <see cref="WindowedRenderer"/> (docs/plans/human-play.md principle 3), and it is the part that
/// can silently be wrong — a broken batching/order/validation bug — in a way a passing screenshot
/// hides, so it is asserted by plain unit tests here instead of needing a GPU.
/// </para>
/// <para>
/// <b>Why grouping by kind cannot change the rendered image</b>: the primitive pipeline is fully
/// opaque (<c>BlendEnable = false</c>) with a standard less-than depth test, so occlusion between
/// opaque instances is decided by the depth buffer, not by submission order — reordering draws
/// changes nothing pixel-visible <i>except</i> for exactly-coincident depth ties, where whichever
/// fragment is rasterised first wins (<c>CompareOp.Less</c> only overwrites a strictly smaller
/// depth). That edge case is exactly why TASK-152's acceptance bar is a pixel-for-pixel diff
/// against the pre-change goldens, not a "looks plausible" screenshot check.
/// </para>
/// <para>
/// An instance of this is owned per renderer and reused every frame: buffers only ever grow, so a
/// steady scene settles into zero allocations per frame (mirrors <see cref="ParticlePacker"/>).
/// </para>
/// </summary>
public sealed class PrimitiveInstancePacker
{
    /// <summary>Floats per instance: mvp(mat4=16) + colour(4) + shape(4) + rotation(4) = 28
    /// (bit-for-bit the layout <c>BoxPush</c> used as a push constant) plus TASK-148's
    /// shadowMvp(mat4=16) = 44, plus TASK-149's worldCenter(vec4=4, xyz=world-space instance
    /// center, w unused) = 48, plus TASK-168's worldScale(vec4=4) = 52 total, i.e. 208 bytes.
    /// Must stay in lockstep with
    /// <see cref="ShaderSource.PrimitiveVert"/>/<see cref="ShaderSource.ShadowDepthVert"/> and
    /// both renderers' primitive-instanced/shadow pipeline vertex input state.
    /// <para>
    /// TASK-149's <c>worldCenter</c> is <see cref="PrimitiveInstance.Model"/>'s translation
    /// column, read directly rather than decomposed — cheaper than <see cref="DecomposeOf"/>'s
    /// full <see cref="Matrix4x4.Decompose"/> (a translation is just <c>M41/M42/M43</c>, no
    /// polar/SVD work needed), and it is the one piece of per-instance world-space data
    /// <see cref="ShaderSource.PrimitiveFrag"/>'s point-light term needs but the pre-composed
    /// <c>mvp</c>/<c>shadowMvp</c> matrices cannot supply (see <see cref="PointLight"/>'s doc
    /// comment for why this is a per-object, not per-fragment, approximation).</para>
    /// <para>
    /// TASK-168 appended a 13th..14th field, <c>worldScale</c> (vec4, xyz = the scale
    /// <see cref="PrimitiveInstance.Model"/> encodes, w unused) = 52 floats, i.e. 208 bytes. This is
    /// what turns <c>vWorldCenter</c>'s per-OBJECT position into a true per-FRAGMENT world position
    /// in <see cref="ShaderSource.PrimitiveVert"/>: the vertex stage already had the rotation
    /// (<c>inRotation</c>) and the translation (<c>inWorldCenter</c>), so the scale was the only
    /// missing factor of <c>Model</c> — a cube's size is baked into <c>Model</c> and is NOT in
    /// <c>inShape</c> (every cube caller passes <see cref="PrimitiveInstance.Dimensions"/> ==
    /// <see cref="Vector3.Zero"/>). Deriving the world position from these three costs 4 floats per
    /// instance (+8.3% stride) instead of the 16 floats (+33%) a second packed <c>model</c> matrix
    /// would, and costs ZERO extra CPU: the scale falls out of the <see cref="Matrix4x4.Decompose"/>
    /// call <see cref="DecomposeOf"/> was already making for the rotation and previously discarded.
    /// </para>
    /// <para>
    /// 2026-09-19 appended a 15th field, <c>finish</c> (vec4: x = <see cref="SurfaceFinish.Roughness"/>
    /// or -1 for "no finish", y = <see cref="SurfaceFinish.Metallic"/>, zw reserved) = 56 floats,
    /// i.e. 224 bytes. See <see cref="WriteInstance"/>'s own comment for why this needed a new field
    /// rather than a spare lane, and <see cref="SurfaceFinish"/> for why the sentinel is negative.
    /// </para></summary>
    public const int FloatsPerInstance = 56;

    /// <summary>Growth floor for the per-instance buffer — mirrors
    /// <c>ParticleQuad.InstanceMinimumCapacity</c>'s reasoning: a small scene should not
    /// reallocate on nearly every frame as instance count wobbles near zero.</summary>
    public const int InstanceMinimumCapacity = 256;

    /// <summary>Fixed submission order for the kind blocks — arbitrary but stable, so two frames
    /// with the same instances always submit identically (determinism, hard rule 5, is about
    /// simulation order; this is submission order, which the task explicitly allows to change,
    /// but it should still not vary frame-to-frame for the same input for its own sake).</summary>
    private static readonly PrimitiveKind[] KindOrder =
        [PrimitiveKind.Cube, PrimitiveKind.Sphere, PrimitiveKind.Capsule, PrimitiveKind.Cylinder];

    /// <summary>TASK-150: fixed submission order for the material sub-blocks within each kind —
    /// <see cref="MaterialKind.Standard"/> first, same "arbitrary but stable" reasoning as
    /// <see cref="KindOrder"/>. Instances are grouped by (Kind, Material) pair, not Kind alone, so
    /// the color, sun-shadow and fixture-shadow passes each select their eligible sub-blocks from
    /// the same packed buffer. New materials are appended to keep existing enum indices and batch
    /// submission order stable.</summary>
    private static readonly MaterialKind[] MaterialOrder =
    [
        MaterialKind.Standard, MaterialKind.ShadowCastOnly, MaterialKind.Emissive, MaterialKind.OutlineShell,
        MaterialKind.XRayOutlineShell, MaterialKind.AdditiveGlow, MaterialKind.XRayFill,
        MaterialKind.BladeGlint, MaterialKind.ShadowReceiveOnly,
    ];

    /// <summary>The materials the ordinary COLOR pass draws — i.e. which sub-blocks reach
    /// <see cref="VisibleBatches"/>. <see cref="MaterialKind.Emissive"/> and
    /// <see cref="MaterialKind.OutlineShell"/> belong here and <see cref="MaterialKind.ShadowCastOnly"/>
    /// does not: an emissive surface is visible (it just shades itself) and an outline shell is the
    /// only thing that draws the outline at all, whereas a shadow-cast-only one is deliberately not
    /// visible.
    /// <para><see cref="MaterialKind.XRayOutlineShell"/> is excluded too, and for a third reason again:
    /// it IS visible, but only from its own depth-test-disabled pass (<see cref="XRayBatches"/>) drawn
    /// after this one. Including it here would draw it twice, the first time correctly occluded — which
    /// is exactly what it exists not to be.</para>
    /// <para><see cref="MaterialKind.AdditiveGlow"/> is excluded for the same reason
    /// <see cref="MaterialKind.XRayOutlineShell"/> is: it IS visible, but only from its own BLENDING
    /// pass (<see cref="GlowBatches"/>) drawn after this one. Letting it in here would draw it twice —
    /// once opaque and dithered, which is precisely what that material exists not to be.</para></summary>
    private static bool IsVisibleMaterial(MaterialKind material) =>
        material is not (MaterialKind.ShadowCastOnly or MaterialKind.XRayOutlineShell
            or MaterialKind.AdditiveGlow or MaterialKind.XRayFill or MaterialKind.BladeGlint);

    /// <summary>Sun-shadow occluders. X-ray cues, additive light, and
    /// <see cref="MaterialKind.ShadowReceiveOnly"/> do not block the sun. Fixture shadows use the
    /// intersection of this set and <see cref="IsVisibleMaterial"/>, excluding hidden ceilings.</summary>
    private static bool IsOccludingMaterial(MaterialKind material) =>
        material is not (MaterialKind.XRayOutlineShell or MaterialKind.AdditiveGlow
            or MaterialKind.XRayFill or MaterialKind.BladeGlint or MaterialKind.ShadowReceiveOnly);

    // Computed, not a hand-maintained literal (code-reviewer finding, TASK-150): a future
    // PrimitiveKind or MaterialKind addition that forgot to update a hardcoded "6" would silently
    // index past the end of _counts/_cursors/_batches/_visibleBatches at runtime with no
    // compile-time signal. Static fields initialize before instance fields, so this is safe to
    // use for the instance array sizes below.
    private static readonly int GroupCount = KindOrder.Length * MaterialOrder.Length;

    private float[] _buffer = [];
    private readonly int[] _counts = new int[GroupCount];
    private readonly int[] _cursors = new int[GroupCount];
    private PrimitiveBatch[] _batches = [];
    private int _batchCount;
    private PrimitiveBatch[] _visibleBatches = [];
    private int _visibleBatchCount;
    private PrimitiveBatch[] _fixtureShadowBatches = [];
    private int _fixtureShadowBatchCount;
    private PrimitiveBatch[] _xrayBatches = [];
    private int _xrayBatchCount;
    private PrimitiveBatch[] _glowBatches = [];
    private int _glowBatchCount;

    /// <summary>
    /// This frame's meshed groups — one per (<see cref="MeshData"/> reference,
    /// <see cref="MaterialKind"/>) pair present, in FIRST-APPEARANCE order.
    /// <para>
    /// <b>Dynamic, unlike the kind groups, because there is no fixed set of meshes.</b>
    /// <see cref="PrimitiveKind"/> has four values so its groups are a flat array indexed by
    /// arithmetic; a mesh is identified by reference and a scene may hold any number, so the group
    /// list is discovered per frame. Found by LINEAR SCAN rather than a dictionary: a frame holds a
    /// handful of distinct meshes (Triage's ambulance is five, one per material of one OBJ), and a
    /// scan over that costs less than hashing a tuple key per instance — and, unlike a dictionary,
    /// allocates nothing, which is the property <see cref="_buffer"/>'s own reuse exists to protect.
    /// </para>
    /// <para>
    /// The array is reused between frames; <see cref="_meshGroupTally"/> is how much of it is live.
    /// </para>
    /// </summary>
    private (MeshData Mesh, MaterialKind Material, int Count, int Cursor)[] _meshGroups = [];
    private int _meshGroupTally;

    /// <summary>
    /// Last frame's <see cref="PrimitiveInstance.Model"/> at each INPUT index, and what
    /// <see cref="DecomposeOf"/> made of it — so a model that did not move costs one 16-float compare
    /// instead of a <see cref="Matrix4x4.Decompose"/> plus <see cref="ValidateDecomposable"/>'s recompose.
    /// <para>
    /// <b>Why (2026-10-14).</b> Triage's hospital stages carry ~8,200 static off-map backdrop pieces
    /// (<c>OffMapBackdrop</c>), re-described in the same order every frame, and the decompose was the
    /// largest per-instance cost of the packer: measured on the four-AI Level4 round (10,161 instances)
    /// <c>Pack</c> was 3.6 ms a frame — the single largest CPU item, and in a window it runs AFTER the
    /// fence wait, where the GPU cannot hide it.
    /// </para>
    /// <para>
    /// <b>Exact, not approximate</b>: a hit requires an equal matrix (<c>==</c> on every component),
    /// and the same input always decomposes to the same output, so every packed float is what a miss
    /// would have written (the one latitude <c>==</c> allows is +0 against -0, which can at most flip
    /// the sign of a zero). Only a SUCCESSFUL decompose is stored, so a matrix that throws
    /// keeps throwing every frame. Keyed by position, not by identity, because instances are value
    /// types: a producer that inserts one piece early in its list turns the rest into misses for one
    /// frame, which is the old cost, never a wrong answer.
    /// </para>
    /// <para>
    /// Valid for the first <see cref="_cachedCount"/> indices, which is set only when a
    /// <see cref="Pack(IReadOnlyList{PrimitiveInstance}, Matrix4x4, Matrix4x4?)"/> COMPLETES: a frame
    /// that throws part-way leaves these arrays describing two different frames, so the next one starts
    /// from nothing rather than trusting them.
    /// </para>
    /// </summary>
    private Matrix4x4[] _decomposedModels = [];
    private (Vector4 Rotation, Vector3 Scale)[] _decomposed = [];

    /// <summary>A non-list, non-array input copied into a span — see <see cref="AsSpan"/>.</summary>
    private PrimitiveInstance[] _scratch = [];

    /// <summary>What <see cref="IsOutsideViewAndCastsNothing"/> decided per input index. Pass 1 writes
    /// it, pass 2 reads it, and the next frame reuses an entry whose model and camera have not moved —
    /// a fixed gameplay camera over a static backdrop is exactly that, every frame.</summary>
    private CullState[] _cull = [];
    private Matrix4x4 _cachedViewProjection;
    private int _cachedCount;

    private enum CullState : byte
    {
        /// <summary>Not a culling candidate at all (see <see cref="IsCullCandidate"/>) — never reused,
        /// so a candidate that takes over its index is always tested.</summary>
        NotCandidate,
        InView,
        Culled,
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int GroupIndex(PrimitiveKind kind, MaterialKind material) => ((int)kind * MaterialOrder.Length) + (int)material;

    /// <summary>
    /// The index into <see cref="_meshGroups"/> for one (mesh, material) pair, appending a new group
    /// when <paramref name="create"/> and none exists.
    /// <para>Reference equality on the mesh, deliberately: that is <see cref="MeshData"/>'s own
    /// identity contract (one GPU buffer per instance of it), so two groups sharing a buffer is
    /// exactly the case worth merging into one draw and two equal-but-separate uploads is exactly the
    /// case that must not be.</para>
    /// </summary>
    private int MeshGroupFor(MeshData mesh, MaterialKind material, bool create)
    {
        for (var i = 0; i < _meshGroupTally; i++)
        {
            if (ReferenceEquals(_meshGroups[i].Mesh, mesh) && _meshGroups[i].Material == material)
            {
                return i;
            }
        }

        if (!create)
        {
            // Unreachable while pass 2 only ever asks about pairs pass 1 counted. Thrown rather than
            // returned as -1 so a future edit that breaks that pairing fails here instead of writing
            // an instance over another group's block.
            throw new InvalidOperationException(
                $"Mesh group ({mesh.Name}, {material}) was written without having been counted — the packer's two passes disagree.");
        }

        if (_meshGroupTally == _meshGroups.Length)
        {
            Array.Resize(ref _meshGroups, Math.Max(4, _meshGroups.Length * 2));
        }

        _meshGroups[_meshGroupTally] = (mesh, material, 0, 0);
        return _meshGroupTally++;
    }

    /// <summary>One entry per (<see cref="PrimitiveKind"/>, <see cref="MaterialKind"/>) pair that
    /// has at least one instance this frame — never one per instance. <c>Offset</c> is in
    /// instances (not floats), for use as <c>vkCmdDraw</c>'s <c>firstInstance</c>.
    /// <para>
    /// <b>Every OCCLUDING material</b>, including <see cref="MaterialKind.ShadowCastOnly"/> — this is
    /// what the sun shadow depth pass draws (TASK-150: a shadow-cast-only surface must still occlude
    /// light), so a color-pass caller wanting only what the camera can see must use
    /// <see cref="VisibleBatches"/> instead. Light-transmitting
    /// <see cref="MaterialKind.ShadowReceiveOnly"/> surfaces draw in color but are excluded here.
    /// </para>
    /// </summary>
    /// <para>
    /// <b>Meshed runs come last</b> (2026-09-03), after every kind block, and each is one
    /// <see cref="MeshData"/>. That order is what keeps a mesh-free frame byte-identical to before
    /// the feature existed: no existing batch's <c>Offset</c> moves, so no golden re-mints.
    /// </para>
    public ReadOnlySpan<PrimitiveBatch> Batches => _batches.AsSpan(0, _batchCount);

    /// <summary>Ordinary color-pass geometry, including
    /// <see cref="MaterialKind.ShadowReceiveOnly"/> surfaces that are absent from the sun-shadow
    /// <see cref="Batches"/>. Invisible <see cref="MaterialKind.ShadowCastOnly"/> ceilings are absent.</summary>
    public ReadOnlySpan<PrimitiveBatch> VisibleBatches => _visibleBatches.AsSpan(0, _visibleBatchCount);

    /// <summary>Visible, shadow-casting geometry for local fixture depth maps. Invisible ceilings
    /// and visible light-transmitting surfaces are both omitted.</summary>
    public ReadOnlySpan<PrimitiveBatch> FixtureShadowBatches =>
        _fixtureShadowBatches.AsSpan(0, _fixtureShadowBatchCount);

    /// <summary>The <see cref="MaterialKind.XRayOutlineShell"/> sub-blocks — drawn by their own
    /// depth-test-disabled pipeline AFTER <see cref="VisibleBatches"/>, and by nothing else. Disjoint
    /// from both <see cref="VisibleBatches"/> and <see cref="Batches"/>, so an x-ray shell rasterizes
    /// exactly once per frame and never into a shadow map (see
    /// <see cref="MaterialKind.XRayOutlineShell"/>'s own doc comment).</summary>
    public ReadOnlySpan<PrimitiveBatch> XRayBatches => _xrayBatches.AsSpan(0, _xrayBatchCount);

    /// <summary>The <see cref="MaterialKind.AdditiveGlow"/> sub-blocks — drawn by their own BLENDING
    /// pipeline after <see cref="VisibleBatches"/>, and by nothing else. Disjoint from both
    /// <see cref="VisibleBatches"/> and <see cref="Batches"/>, so a glow is blended exactly once per
    /// frame and never reaches a shadow map.
    /// <para><b>Deliberately NOT sorted.</b> Blending is normally order-dependent, which is why this
    /// packer's whole batching model would have to be abandoned to support alpha-over. Addition is
    /// commutative, so these can keep the same (kind, material) grouping every other material gets and
    /// still composite correctly in any order — see <see cref="MaterialKind.AdditiveGlow"/>.</para></summary>
    public ReadOnlySpan<PrimitiveBatch> GlowBatches => _glowBatches.AsSpan(0, _glowBatchCount);

    /// <summary>Instances actually packed this frame: every one offered, less those
    /// <see cref="IsOutsideViewAndCastsNothing"/> left out because no pixel of this frame could show
    /// them. Never fewer for being INVALID — unlike <see cref="ParticlePacker"/>, an invalid primitive
    /// is a hard error, not something to silently drop, and it is validated before it is culled; see
    /// <see cref="Validate"/>.</summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Whether anything packed this frame SHADES from the camera's position, and therefore whether
    /// the renderer's <c>cameraPosition</c> argument was load-bearing — true when any instance
    /// carried a <see cref="SurfaceFinish"/> or one of the camera-distance-faded
    /// <see cref="SurfaceDetail"/> classes.
    /// <para>
    /// Exists so a missing view direction FAILS rather than renders subtly wrong. Without it, a
    /// caller that forgot the argument gets a scene shaded as though the eye were at the world
    /// origin: highlights on the wrong faces, environment reflections pointing the wrong way, and
    /// fine surface detail fading by distance-from-origin. Every one of those merely looks a little
    /// off, which is precisely the failure mode <see cref="DecomposeOf"/>'s own throw exists to
    /// refuse — and the reason this is a flag set during the pass the packer was making anyway
    /// rather than a second scan of the instance list every frame.
    /// </para>
    /// </summary>
    public bool RequiresViewDirection { get; private set; }

    /// <summary>The packed instance-buffer data for exactly <see cref="TotalCount"/> instances, in
    /// <see cref="Batches"/> order. A span, not the array, so the reused backing store cannot be
    /// captured or resized by a caller.</summary>
    public ReadOnlySpan<float> Data => _buffer.AsSpan(0, TotalCount * FloatsPerInstance);

    /// <summary>
    /// Packs <paramref name="instances"/> for this frame. Overwrites the previous frame's
    /// contents; nothing is retained between calls except the buffers themselves.
    /// </summary>
    /// <param name="instances">This frame's cube/sphere/capsule instances, in any order.</param>
    /// <param name="viewProjection">The matrix this frame is rendered with — folded into each
    /// instance's packed <c>mvp</c> up front, exactly as the old per-instance push constant did
    /// (<c>instance.Model * viewProjection</c>), so the shader still receives a ready-made mvp.</param>
    /// <param name="shadowViewProjection">TASK-148: the sun's shadow view-projection
    /// (<see cref="Camera.DirectionalShadowViewProjection"/>), folded into each instance's
    /// packed <c>shadowMvp</c> the same way. Defaults to <see cref="Matrix4x4.Identity"/> when
    /// shadows are disabled for the frame — harmless, since the shader only samples it when
    /// the caller's <c>shadowParams.x</c> flag says to.</param>
    /// <exception cref="ArgumentOutOfRangeException">A primitive's dimensions are invalid (see
    /// <see cref="Validate"/>) or its <see cref="PrimitiveKind"/> is unrecognized. Thrown before
    /// any instance is written — same hard-fail contract the old per-instance validate-then-draw
    /// loop had (a bad instance stops the whole frame, never just a suffix of it).</exception>
    public void Pack(IReadOnlyList<PrimitiveInstance> instances, Matrix4x4 viewProjection, Matrix4x4? shadowViewProjection = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        Pack(AsSpan(instances), viewProjection, shadowViewProjection ?? Matrix4x4.Identity);
    }

    /// <summary>
    /// The list as a span, so both passes read each ~130-byte instance by reference instead of copying
    /// it out through the interface indexer twice (2026-10-14, part of the same measurement as
    /// <see cref="_decomposed"/>). Every renderer caller passes a <see cref="List{T}"/> or an array;
    /// anything else is copied once into a reused scratch array, which is what the indexer cost anyway.
    /// </summary>
    private ReadOnlySpan<PrimitiveInstance> AsSpan(IReadOnlyList<PrimitiveInstance> instances)
    {
        switch (instances)
        {
            case List<PrimitiveInstance> list:
                return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            case PrimitiveInstance[] array:
                return array;
        }

        if (_scratch.Length < instances.Count)
        {
            _scratch = new PrimitiveInstance[instances.Count];
        }

        for (var i = 0; i < instances.Count; i++)
        {
            _scratch[i] = instances[i];
        }

        // A shorter list than last time must not keep the longer one's meshes alive.
        Array.Clear(_scratch, instances.Count, _scratch.Length - instances.Count);
        return _scratch.AsSpan(0, instances.Count);
    }


    /// <summary>
    /// The per-frame body. <b>Compiled optimized from the first call</b>
    /// (<see cref="MethodImplOptions.AggressiveOptimization"/>, as is every per-instance helper it
    /// calls — an optimized caller still runs a tier-0 callee it did not inline), 2026-10-14: under tiered compilation it ran as unoptimized tier-0 code for the first ~45
    /// frames of a session — the window's call counting is held back while the stage's own startup is
    /// still JIT-ing — and at ~10,000 instances that was 17-20 ms of Pack per frame for about two
    /// seconds after every launch, the whole of a 37 ms p99 on the load case. Optimized, it is ~0.5 ms
    /// from the first frame; the price is only that dynamic PGO does not see these four methods.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Pack(ReadOnlySpan<PrimitiveInstance> instances, Matrix4x4 viewProjection, Matrix4x4 shadowVp)
    {
        // Taken, and the cache closed, until this frame completes — see _decomposedModels.
        var cached = _cachedCount;
        _cachedCount = 0;
        var sameView = _cachedViewProjection == viewProjection;
        Array.Clear(_counts);
        _meshGroupTally = 0;
        TotalCount = 0;
        RequiresViewDirection = false;
        if (instances.Length == 0)
        {
            _batchCount = 0;
            _visibleBatchCount = 0;
            _fixtureShadowBatchCount = 0;
            _xrayBatchCount = 0;
            _glowBatchCount = 0;
            return;
        }

        // Pass 1: validate every instance (same check, same exception, same order as the old
        // per-instance loop) and count each group's block size — a (Kind, Material) group for an
        // ordinary primitive, a (Mesh, Material) one for an imported mesh.
        EnsureInputCapacity(instances.Length);
        for (var i = 0; i < instances.Length; i++)
        {
            ref readonly var instance = ref instances[i];
            Validate(instance);
            RequiresViewDirection |= ShadesFromTheCamera(instance);
            _cull[i] = !IsCullCandidate(instance) ? CullState.NotCandidate
                : sameView && i < cached && _cull[i] != CullState.NotCandidate && _decomposedModels[i] == instance.Model ? _cull[i]
                : IsOutsideClipVolume(instance, viewProjection) ? CullState.Culled
                : CullState.InView;
            if (_cull[i] == CullState.Culled)
            {
                continue;
            }

            TotalCount++;
            if (instance.Mesh is { } mesh)
            {
                var meshGroup = MeshGroupFor(mesh, instance.Material, create: true);
                _meshGroups[meshGroup].Count++;
            }
            else
            {
                _counts[GroupIndex(instance.Kind, instance.Material)]++;
            }
        }

        EnsureCapacity(TotalCount);

        var offset = 0;
        _batchCount = 0;
        _visibleBatchCount = 0;
        _fixtureShadowBatchCount = 0;
        _xrayBatchCount = 0;
        _glowBatchCount = 0;
        for (var k = 0; k < KindOrder.Length; k++)
        {
            for (var m = 0; m < MaterialOrder.Length; m++)
            {
                var group = GroupIndex(KindOrder[k], MaterialOrder[m]);
                _cursors[group] = offset;
                if (_counts[group] > 0)
                {
                    AppendBatch(new PrimitiveBatch(KindOrder[k], offset, _counts[group]), MaterialOrder[m]);
                }

                offset += _counts[group];
            }
        }

        // The meshed groups take the tail of the buffer, AFTER every kind block. That is what keeps
        // a mesh-free frame byte-identical to one packed before meshes existed — every existing
        // batch's offset is unmoved, so no golden re-mints (the same "append, never insert"
        // discipline MaterialOrder's own doc comment follows).
        for (var g = 0; g < _meshGroupTally; g++)
        {
            _meshGroups[g].Cursor = offset;
            AppendBatch(
                new PrimitiveBatch(PrimitiveKind.Cube, offset, _meshGroups[g].Count, _meshGroups[g].Mesh),
                _meshGroups[g].Material);
            offset += _meshGroups[g].Count;
        }

        // Pass 2: write each instance into its own group's block, in original relative order — a
        // stable partition, so two same-group instances never swap relative to each other.
        for (var i = 0; i < instances.Length; i++)
        {
            ref readonly var instance = ref instances[i];
            if (_cull[i] == CullState.Culled)
            {
                // Still checked, so a sheared model fails the frame whether or not it is in view
                // (on a still model this is the cache's one compare).
                _ = DecomposeCached(i, instance.Model, cached);
                continue;
            }

            int write;
            if (instance.Mesh is { } mesh)
            {
                var meshGroup = MeshGroupFor(mesh, instance.Material, create: false);
                write = _meshGroups[meshGroup].Cursor * FloatsPerInstance;
                _meshGroups[meshGroup].Cursor++;
            }
            else
            {
                var group = GroupIndex(instance.Kind, instance.Material);
                write = _cursors[group] * FloatsPerInstance;
                _cursors[group]++;
            }

            WriteInstance(_buffer, write, instance, DecomposeCached(i, instance.Model, cached), viewProjection, shadowVp);
        }

        _cachedViewProjection = viewProjection;
        _cachedCount = instances.Length;
    }

    /// <summary>
    /// Whether this instance can be left out of the frame with no pixel changing: it casts no shadow
    /// of any kind (<see cref="MaterialKind.ShadowReceiveOnly"/>, so it is in no shadow pass and only
    /// the camera can see it) and its bounds lie wholly outside the camera's clip volume.
    /// <para>
    /// <b>Why (2026-10-14).</b> 8,191 of the four-AI Level4 round's 10,161 instances are
    /// <c>OffMapBackdrop</c>'s town, most of it behind or beside a camera that looks down at the ward.
    /// Every one was packed, uploaded (224 bytes each) and run through the vertex stage twice (depth
    /// prepass and colour) every frame, to be clipped.
    /// </para>
    /// <para>
    /// <b>Cube and cylinder only, with an exactly affine model</b>: both are drawn inside the unit box
    /// (±0.5 on every axis) carried by <see cref="PrimitiveInstance.Model"/> alone, so the box's own
    /// clip-space image is <c>Model * viewProjection</c> applied to that unit box — the matrix
    /// <see cref="WriteInstance"/> bakes, tested as an ORIENTED box, which is tighter than a world AABB
    /// for a turned piece. A sphere's or capsule's radius rides in <c>Dimensions</c> and a mesh's extent
    /// is its own; the backdrop needs neither. A model with any perspective term (which
    /// <see cref="ValidateDecomposable"/> tolerates up to 1e-3) is not a candidate, since the unit box
    /// would not then be where it is drawn.
    /// </para>
    /// <para>
    /// The test is the standard homogeneous one: the box is outside when all eight corners fail the
    /// SAME clip plane, which is linear and so holds for corners behind the eye as well. Conservative:
    /// a box straddling a plane, or lying on one to within rounding, is kept.
    /// </para>
    /// </summary>
    internal static bool IsOutsideViewAndCastsNothing(in PrimitiveInstance instance, in Matrix4x4 viewProjection) =>
        IsCullCandidate(instance) && IsOutsideClipVolume(instance, viewProjection);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool IsCullCandidate(in PrimitiveInstance instance) =>
        instance.Material == MaterialKind.ShadowReceiveOnly
        && instance.Mesh is null
        && instance.Kind is PrimitiveKind.Cube or PrimitiveKind.Cylinder
        && instance.Model.M14 == 0f && instance.Model.M24 == 0f && instance.Model.M34 == 0f && instance.Model.M44 == 1f;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool IsOutsideClipVolume(in PrimitiveInstance instance, in Matrix4x4 viewProjection)
    {
        // Row-vector convention: the unit box's centre maps to row 4 and its local half-axes (0.5 each)
        // to half of rows 1-3.
        var mvp = instance.Model * viewProjection;
        var c = new Vector4(mvp.M41, mvp.M42, mvp.M43, mvp.M44);
        var x = new Vector4(mvp.M11, mvp.M12, mvp.M13, mvp.M14) * 0.5f;
        var y = new Vector4(mvp.M21, mvp.M22, mvp.M23, mvp.M24) * 0.5f;
        var z = new Vector4(mvp.M31, mvp.M32, mvp.M33, mvp.M34) * 0.5f;

        return AllCornersOutside(new Vector4(1f, 0f, 0f, 1f), c, x, y, z) // x >= -w
            || AllCornersOutside(new Vector4(-1f, 0f, 0f, 1f), c, x, y, z) // x <= w
            || AllCornersOutside(new Vector4(0f, 1f, 0f, 1f), c, x, y, z) // y >= -w
            || AllCornersOutside(new Vector4(0f, -1f, 0f, 1f), c, x, y, z) // y <= w
            || AllCornersOutside(new Vector4(0f, 0f, 1f, 0f), c, x, y, z) // z >= 0 (Vulkan depth [0, 1])
            || AllCornersOutside(new Vector4(0f, 0f, -1f, 1f), c, x, y, z); // z <= w

        // The largest value the plane takes over the eight corners centre ± x ± y ± z, against a small
        // margin: this arithmetic is not the GPU's per-vertex clip bit for bit, so a box whose face lies
        // ON a clip plane is kept rather than decided by rounding.
        static bool AllCornersOutside(Vector4 plane, Vector4 centre, Vector4 x, Vector4 y, Vector4 z) =>
            Vector4.Dot(plane, centre) + MathF.Abs(Vector4.Dot(plane, x)) + MathF.Abs(Vector4.Dot(plane, y))
                + MathF.Abs(Vector4.Dot(plane, z)) < -1e-4f * (MathF.Abs(centre.W) + 1f);
    }

    /// <summary><see cref="DecomposeOf"/> + <see cref="ValidateDecomposable"/>, skipped when the
    /// model at this input index is the one decomposed last frame — see <see cref="_decomposed"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private (Vector4 Rotation, Vector3 Scale) DecomposeCached(int index, in Matrix4x4 model, int cached)
    {
        if (index < cached && _decomposedModels[index] == model)
        {
            return _decomposed[index];
        }

        var result = DecomposeOf(model);
        ValidateDecomposable(model, result.Rotation, result.Scale);
        _decomposedModels[index] = model;
        _decomposed[index] = result;
        return result;
    }

    /// <summary>Files each run into its color, sun-shadow, fixture-shadow, x-ray and glow pass lists.
    /// Meshed and primitive instances share exactly the same membership policy.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void AppendBatch(PrimitiveBatch batch, MaterialKind material)
    {
        if (IsOccludingMaterial(material))
        {
            _batches[_batchCount++] = batch;
        }

        if (IsVisibleMaterial(material))
        {
            _visibleBatches[_visibleBatchCount++] = batch;
            if (IsOccludingMaterial(material))
            {
                _fixtureShadowBatches[_fixtureShadowBatchCount++] = batch;
            }
        }
        else if (material is MaterialKind.XRayOutlineShell or MaterialKind.XRayFill)
        {
            _xrayBatches[_xrayBatchCount++] = batch;
        }
        else if (material is MaterialKind.AdditiveGlow or MaterialKind.BladeGlint)
        {
            // BladeGlint rides the glow pass verbatim: same blending pipeline, same after-the-colour
            // -pass position. What separates the two is one branch in the fragment shader, which is
            // a decision about a PIXEL and not about a draw.
            _glowBatches[_glowBatchCount++] = batch;
        }
    }

    /// <summary>Recovers the rotation AND scale <see cref="PrimitiveInstance.Model"/> encodes, from
    /// ONE <see cref="Matrix4x4.Decompose"/> call — the rotation for the vertex shader's normal
    /// transform (which is all TASK-152 needed, when this was <c>RotationOf</c> and threw the scale
    /// away), and since TASK-168 the scale as well, for the per-fragment world position
    /// <see cref="ShaderSource.PrimitiveVert"/> derives for the spot-light term (see
    /// <see cref="FloatsPerInstance"/>'s doc comment). <see cref="PrimitiveInstance"/> only carries
    /// the composed <see cref="Matrix4x4"/>, so recovering either needs a decompose; see
    /// tasks/backlog-details.md#task-152 for why deleting this call entirely would need a public API
    /// change to <see cref="PrimitiveInstance"/>.
    /// <para>
    /// The returned pair is only meaningful because <c>Model</c> is a scale-then-rotate-then-translate
    /// product for every caller — an invariant that is real but was, before TASK-168, merely
    /// conventional. <see cref="ValidateDecomposable"/> now pins it per instance, because the scale
    /// half positions fragments rather than merely tilting a normal.
    /// </para></summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static (Vector4 Rotation, Vector3 Scale) DecomposeOf(Matrix4x4 model)
    {
        if (!Matrix4x4.Decompose(model, out var scale, out var rotation, out _))
        {
            // TASK-168, arch-reviewer + rendering-reviewer finding (both MEDIUM): this used to
            // silently substitute Quaternion.Identity, which was survivable while the decomposed
            // rotation only affected a normal (a wrong normal is a shading artifact). It is not
            // survivable now that the scale from the SAME call positions every fragment for the
            // spot-light term: a silently-wrong scale puts vWorldPos somewhere the geometry is not,
            // so a cone would light the wrong place with no diagnostic signal at all. Fail loud
            // instead, matching SceneLighting.Validate's discipline for every other lighting input.
            throw new ArgumentOutOfRangeException(
                nameof(model),
                model,
                $"{nameof(PrimitiveInstance)}.{nameof(PrimitiveInstance.Model)} could not be decomposed into scale/rotation/translation. " +
                "A primitive's Model must be a scale-then-rotate-then-translate product (see PrimitiveInstance's doc comment) — " +
                "a singular (zero-scale) or sheared matrix has no such factorization, and the renderer needs one both for the " +
                "vertex normal and, since TASK-168, for the per-fragment world position the spot-light term shades from.");
        }

        rotation = Quaternion.Normalize(rotation);
        return (new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W), scale);
    }

    /// <summary>Same bounds check both renderers ran per-instance before every draw
    /// (TASK-152 moved it here, once per instance per frame, same as before — not cached across
    /// frames even for immutable level geometry, since that reduction is a separate, unaddressed
    /// concern; see tasks/backlog-details.md#task-152 item (c)).</summary>
    /// <summary>
    /// Whether this instance's shading reads the camera position — a <see cref="SurfaceFinish"/>
    /// (view direction for the specular lobe and the environment reflection) or one of the fine
    /// <see cref="SurfaceDetail"/> classes (camera distance for the amplitude fade). See
    /// <see cref="RequiresViewDirection"/> for what the answer is used for.
    /// <para>The detail test is a RANGE above <see cref="SurfaceDetail.SlabSeamsFollowInstance"/>
    /// rather than a list, matching the shader's own <c>detail &gt; 4.5</c> dispatch, so a class
    /// added to the fine group is covered here automatically instead of being remembered.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static bool ShadesFromTheCamera(in PrimitiveInstance instance) =>
        instance.Finish is not null || instance.Detail > SurfaceDetail.SlabSeamsFollowInstance;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static void Validate(in PrimitiveInstance instance)
    {
        // A meshed instance's size is in its own vertices, so there is no radius to bounds-check --
        // but its KIND still matters, because the vertex shader picks a per-vertex deformation off it
        // and only Cube/Cylinder select the passthrough branch. Declared as a Sphere, an imported
        // model would be silently rescaled by Dimensions.X (zero for every mesh caller, i.e.
        // collapsed to a point) with nothing to see but an absent model. Fail loudly instead.
        if (instance.Mesh is { } mesh)
        {
            if (instance.Kind is PrimitiveKind.Cube or PrimitiveKind.Cylinder)
            {
                return;
            }

            throw new ArgumentOutOfRangeException(
                nameof(instance),
                $"A {nameof(PrimitiveInstance)} carrying a {nameof(PrimitiveInstance.Mesh)} ('{mesh.Name}') must declare "
                + $"{nameof(PrimitiveKind)}.{nameof(PrimitiveKind.Cube)} — the vertex shader's per-kind deformation is selected "
                + $"by {nameof(PrimitiveInstance.Kind)}, and only Cube/Cylinder pass a vertex through unaltered. "
                + $"Kind={instance.Kind} at ({instance.Model.Translation.X:0.##}, {instance.Model.Translation.Y:0.##}, "
                + $"{instance.Model.Translation.Z:0.##})");
        }

        var valid = instance.Kind switch
        {
            // Cube and Cylinder carry their size in Model, not Dimensions — nothing to bounds-check
            // here for either (a degenerate Model is caught by ValidateDecomposable instead).
            PrimitiveKind.Cube or PrimitiveKind.Cylinder => true,
            PrimitiveKind.Sphere => instance.Dimensions.X > 0f && float.IsFinite(instance.Dimensions.X),
            PrimitiveKind.Capsule => instance.Dimensions.X > 0f && instance.Dimensions.Y >= 0f &&
                float.IsFinite(instance.Dimensions.X) && float.IsFinite(instance.Dimensions.Y),
            _ => false,
        };
        if (!valid)
        {
            // The message names the OFFENDING INSTANCE, because the previous one did not and that cost two
            // debugging rounds on a crash a player hit twice (2026-08-24). "Primitive dimensions must be
            // finite and nonnegative" with a stack trace ending in the renderer says only "some primitive,
            // somewhere in this frame" — and a frame holds hundreds from a dozen unrelated producers, so
            // the only way forward was to guess which and write a test per guess. Kind, dimensions and the
            // model's own translation together identify it: the position says WHERE in the world it is,
            // which is usually enough to name the producer on sight.
            throw new ArgumentOutOfRangeException(
                nameof(instance),
                $"Primitive dimensions must be finite and nonnegative; radius must be positive. "
                + $"Kind={instance.Kind} Dimensions={instance.Dimensions} "
                + $"Material={instance.Material} "
                + $"at ({instance.Model.Translation.X:0.##}, {instance.Model.Translation.Y:0.##}, "
                + $"{instance.Model.Translation.Z:0.##})");
        }
    }

    /// <summary>
    /// TASK-168, arch-reviewer finding (MEDIUM): pins the "<see cref="PrimitiveInstance.Model"/> is a
    /// scale-then-rotate-then-translate product" invariant that <see cref="DecomposeOf"/> depends on,
    /// instead of leaving it as an unenforced authoring convention.
    /// <para>
    /// The invariant genuinely holds for every caller today, but not for the obvious reason: while
    /// most compose <c>CreateScale(size) * CreateTranslation(offset) * parent</c>,
    /// <c>PlayerVisualRig</c> sandwiches a scale BETWEEN two rotations, which is shear-free only
    /// because that particular scale is isotropic. A future non-uniform scale in that position would
    /// still rasterize correctly — <c>mvp</c> is the composed matrix and never consults this
    /// factorization — while silently placing <c>vWorldPos</c> somewhere the geometry is not, visible
    /// only as a spot light pooling in the wrong place. <see cref="Matrix4x4.Decompose"/> returns
    /// <see langword="true"/> for such a matrix, so its own return value cannot catch this; only
    /// recomposing and comparing can.
    /// </para>
    /// <para>
    /// Cost is ~30 flops per instance per frame (two 4x4 multiplies against a tolerance), against the
    /// full <see cref="Matrix4x4.Decompose"/> this same method already performs — immaterial next to
    /// it, and it runs in the same single pass, so it adds no traversal. The tolerance is RELATIVE to
    /// the matrix magnitude: a stage-scale translation of ~30 world units carries float error far
    /// above any fixed epsilon, so an absolute tolerance would either false-positive on real content
    /// or be too loose to catch anything.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="model"/> has no
    /// scale/rotate/translate factorization — it shears, or a scale sits between two rotations with
    /// non-uniform factors.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static void ValidateDecomposable(Matrix4x4 model, Vector4 rotation, Vector3 scale)
    {
        var recomposed = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W))
            * Matrix4x4.CreateTranslation(model.M41, model.M42, model.M43);

        // Relative tolerance, scaled by the LINEAR (3x3) magnitude only — never by translation
        // (transform-auditor TASK-168 finding, MEDIUM): the comparison below looks exclusively at the
        // 3x3 block, so folding a prop's distance from the origin into its tolerance made the guard
        // weaker the further out the content sat. A Triage prop at world (30, 0, 30) got a tolerance of
        // ~0.03 against 3x3 entries of ~0.28, i.e. a 10% shear would have passed silently — exactly the
        // failure this method exists to catch. The floor keeps a near-zero linear part comparable, and
        // it also sets the effective absolute tolerance at 1e-3 for anything smaller than unit scale —
        // fine while content stays well under ~1e3 units (Decompose's float32 error is ~1e-6 relative),
        // but a stage built at thousands of units would want this revisited (code-reviewer, LOW).
        var magnitude = MathF.Max(LinearMagnitude(model), LinearMagnitude(recomposed));
        var tolerance = MathF.Max(magnitude, 1f) * 1e-3f;
        if (MaxComponentDifference(model, recomposed) <= tolerance)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(model),
            model,
            $"{nameof(PrimitiveInstance)}.{nameof(PrimitiveInstance.Model)} does not factor into scale-then-rotate-then-translate " +
            $"(recomposing the decomposed scale {scale} and rotation differs from it by more than {tolerance}). " +
            "The renderer derives each fragment's world position from that factorization (see PrimitiveInstancePacker.FloatsPerInstance), " +
            "so a sheared Model — typically a NON-UNIFORM scale applied between two rotations, rather than at the innermost leaf of the " +
            "transform chain — would rasterize correctly but light incorrectly. Move the non-uniform scale to the innermost factor.");
    }

    /// <summary>Largest absolute entry of the LINEAR 3x3 block — all nine, not just the diagonal, since
    /// a rotation spreads a scale across off-diagonal entries. Translation is deliberately excluded;
    /// see <see cref="ValidateDecomposable"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static float LinearMagnitude(in Matrix4x4 m)
    {
        var worst = MathF.Abs(m.M11);
        worst = MathF.Max(worst, MathF.Abs(m.M12));
        worst = MathF.Max(worst, MathF.Abs(m.M13));
        worst = MathF.Max(worst, MathF.Abs(m.M21));
        worst = MathF.Max(worst, MathF.Abs(m.M22));
        worst = MathF.Max(worst, MathF.Abs(m.M23));
        worst = MathF.Max(worst, MathF.Abs(m.M31));
        worst = MathF.Max(worst, MathF.Abs(m.M32));
        worst = MathF.Max(worst, MathF.Abs(m.M33));
        return worst;
    }

    /// <summary>Compares the 3x3 linear block AND the 4th column. The 4th column matters
    /// (transform-auditor TASK-168 finding, LOW): a PROJECTIVE <c>Model</c> — one with perspective terms
    /// in <c>M14/M24/M34</c> or an <c>M44</c> that is not 1 — would otherwise recompose as "equal" while
    /// the shader's affine reconstruction of <c>vWorldPos</c> silently drops the perspective divide.
    /// Translation (<c>M41..M43</c>) is excluded because <see cref="ValidateDecomposable"/> copies it
    /// verbatim rather than deriving it, so it is equal by construction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static float MaxComponentDifference(in Matrix4x4 a, in Matrix4x4 b)
    {
        var worst = MathF.Abs(a.M11 - b.M11);
        worst = MathF.Max(worst, MathF.Abs(a.M12 - b.M12));
        worst = MathF.Max(worst, MathF.Abs(a.M13 - b.M13));
        worst = MathF.Max(worst, MathF.Abs(a.M14 - b.M14));
        worst = MathF.Max(worst, MathF.Abs(a.M21 - b.M21));
        worst = MathF.Max(worst, MathF.Abs(a.M22 - b.M22));
        worst = MathF.Max(worst, MathF.Abs(a.M23 - b.M23));
        worst = MathF.Max(worst, MathF.Abs(a.M24 - b.M24));
        worst = MathF.Max(worst, MathF.Abs(a.M31 - b.M31));
        worst = MathF.Max(worst, MathF.Abs(a.M32 - b.M32));
        worst = MathF.Max(worst, MathF.Abs(a.M33 - b.M33));
        worst = MathF.Max(worst, MathF.Abs(a.M34 - b.M34));
        worst = MathF.Max(worst, MathF.Abs(a.M44 - b.M44));
        return worst;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WriteInstance(
        float[] buffer, int floatOffset, in PrimitiveInstance instance, (Vector4 Rotation, Vector3 Scale) decomposed,
        Matrix4x4 viewProjection, Matrix4x4 shadowViewProjection)
    {
        var write = floatOffset;
        WriteMatrix(buffer, ref write, instance.Model * viewProjection);

        var color = instance.Color;
        buffer[write++] = color.X;
        buffer[write++] = color.Y;
        buffer[write++] = color.Z;
        buffer[write++] = color.W;

        var shapeKind = instance.Kind switch
        {
            // 0 selects the vertex shader's "no per-kind deformation" branch. Cylinder shares it
            // with Cube deliberately (see PrimitiveMeshes.BuildCylinderVertices): its size lives in
            // Model, so no new shader branch — and therefore no PrecompiledShaders regeneration and
            // no re-minted golden — was needed to add the kind.
            PrimitiveKind.Cube or PrimitiveKind.Cylinder => 0f,
            PrimitiveKind.Sphere => 1f,
            PrimitiveKind.Capsule => 2f,
            _ => throw new ArgumentOutOfRangeException(nameof(instance), $"Unsupported primitive kind: {instance.Kind}."),
        };
        var dimensions = instance.Dimensions;
        buffer[write++] = dimensions.X;
        buffer[write++] = dimensions.Y;
        buffer[write++] = dimensions.Z;
        buffer[write++] = shapeKind;

        var (rotation, scale) = decomposed;
        buffer[write++] = rotation.X;
        buffer[write++] = rotation.Y;
        buffer[write++] = rotation.Z;
        buffer[write++] = rotation.W;

        // TASK-148: same "bake model into the final matrix on the CPU" technique as inMvp0..3
        // above, just against the shadow view-projection instead of the camera's.
        WriteMatrix(buffer, ref write, instance.Model * shadowViewProjection);

        // TASK-149: the instance's own world-space center, for the point-light term's
        // direction/attenuation math — see FloatsPerInstance's doc comment for why this is a
        // translation read, not a decompose, and why it is per-object rather than per-fragment.
        buffer[write++] = instance.Model.M41;
        buffer[write++] = instance.Model.M42;
        buffer[write++] = instance.Model.M43;
        // 2026-08-20: SurfaceDetail's selector, carried in worldCenter's previously-unused .w lane
        // for exactly the reason MaterialKind.Emissive uses worldScale's -- the shader already
        // receives this vec4, so a free lane costs no stride growth and moves no attribute offset
        // that PrimitiveLightingUboLayoutTests pins. The fragment shader compares it against
        // integer-valued constants, so it must round-trip exactly: an enum ordinal in [0, 4] is
        // exactly representable in float32, and the shader's own comparisons are against whole-number
        // midpoints (0.5 / 1.5 / 2.5 / 3.5), so there is no tolerance to get wrong. Inserting a member
        // in the MIDDLE of the enum would shift every later class into the next band --
        // SurfaceDetailTests.EverySurfaceDetailOrdinal_FallsInItsOwnBandOfTheShadersThresholds is that
        // gate.
        buffer[write++] = (float)instance.Detail;

        // TASK-168: the third and last factor of Model, so the vertex shader can reconstruct a TRUE
        // per-fragment world position (quatRotate(rotation, local * scale) + worldCenter) rather than
        // only the per-object centre above. Reuses the decompose already done for `rotation` — see
        // FloatsPerInstance's doc comment for why the scale and not a packed model matrix.
        buffer[write++] = scale.X;
        buffer[write++] = scale.Y;
        buffer[write++] = scale.Z;
        // The MATERIAL selector, carried in worldScale's previously-unused .w rather than in a 15th
        // field: the shader already receives this vec4, so a free lane costs no stride growth and
        // leaves every std140/attribute offset PrimitiveLightingUboLayoutTests pins exactly where it
        // was.
        //
        // The codes are the SHADER's own numbering, not the enum's ordinals, and the two must not be
        // conflated: MaterialKind.ShadowCastOnly never reaches the color pass at all (it is filtered
        // out of VisibleBatches), so it has no code of its own and shares Standard's 0. Emissive
        // stays 1 exactly as it was before OutlineShell existed, which is what keeps every existing
        // golden byte-identical. Whole numbers in [0, 2] are exact in float32 and the shader
        // compares them against the same whole numbers, so there is no tolerance to get wrong.
        buffer[write++] = instance.Material switch
        {
            // MaterialKind.XRayFill shades exactly as Emissive does and takes Emissive's code
            // for that reason -- what separates the two is which batch list they land in, and
            // therefore which pipeline draws them, which the shader has no reason to know about.
            MaterialKind.Emissive or MaterialKind.XRayFill => 1f,
            // Both shell materials share code 2: the SHADER's behaviour is identical for them (same
            // inverted hull, same near-side discard, same literal colour). What separates an x-ray
            // shell from an ordinary one is which batch list it lands in and therefore which pipeline
            // draws it -- a CPU-side decision the shader has no reason to know about.
            MaterialKind.OutlineShell or MaterialKind.XRayOutlineShell => 2f,
            // AdditiveGlow shades EXACTLY as Emissive does (code 1's own branch: self-lit, driven past
            // 1.0, tone-mapped) and takes a code of its own only so the fragment stage can tell "do not
            // dither my alpha, it is a blend weight" apart from "shade me as a lamp". Kept above the
            // shells' 2 so no existing code moves and every existing golden stays byte-identical.
            MaterialKind.AdditiveGlow => 3f,
            // 2026-09-20. 5, not 4: the fragment stage's `glow` gate is `vMaterial > 2.5` and this
            // material wants it (its alpha is a blend weight, not coverage), so it has to sit above
            // AdditiveGlow's 3 rather than beside it. 4 is deliberately left unused so the two
            // additive codes stay adjacent to that gate and nothing that predates them moves.
            MaterialKind.BladeGlint => 5f,
            _ => 0f,
        };

        // 2026-09-19: SurfaceFinish, appended as a 15th field for the fourth time and for exactly the
        // reason TASK-149's, TASK-168's and TASK-245's additions were appended rather than
        // interspersed -- every attribute offset above stays byte-for-byte where the pipelines and
        // PrimitiveInstanceLayoutTests already pin it.
        //
        // A 15th vec4 rather than a spare lane, because there is no spare lane left: worldCenter.w
        // carries SurfaceDetail and worldScale.w carries MaterialKind, and shape.z is Dimensions.Z,
        // which is a caller-supplied value this packer has no licence to overwrite (it is documented
        // as unused by the SHADER, which is not the same as unused by the API).
        //
        // ROUGHNESS IS NEGATIVE WHEN THERE IS NO FINISH, and that sentinel is the whole compatibility
        // story: the fragment shader branches on it and takes its pre-2026-09-19 path, so an instance
        // that never heard of this field renders bit-for-bit as it did. -1 rather than, say, 0,
        // because 0 is a legal roughness (a mirror) -- see SurfaceFinish's own doc comment.
        var finish = instance.Finish;
        if (finish is { } authored && !authored.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instance),
                authored,
                $"{nameof(PrimitiveInstance)}.{nameof(PrimitiveInstance.Finish)} must have finite "
                + $"{nameof(SurfaceFinish.Roughness)} and {nameof(SurfaceFinish.Metallic)} in [0, 1]. "
                + "A non-finite or out-of-range shading parameter has no diagnostic signal in the rendered "
                + "frame at all -- it merely looks slightly wrong -- which is why it is rejected here rather "
                + "than clamped (the same discipline DecomposeOf applies to a non-decomposable Model).");
        }

        buffer[write++] = finish?.Roughness ?? -1f;
        buffer[write++] = finish?.Metallic ?? 0f;
        // 2026-10-06: the two lanes held here are the surface texture. z = TextureLayer + 1, so the
        // zero this buffer has always written means "untextured" (and a reused buffer can never leak
        // a stale layer); w = metres per repeat, 0 meaning the shader's default, any negative meaning
        // a photo decal (see PrimitiveInstance) -- normalised to -1 so the shader tests one value.
        buffer[write++] = instance.TextureLayer >= 0 ? instance.TextureLayer + 1f : 0f;
        buffer[write++] = !float.IsFinite(instance.TextureTileMetres) ? 0f
            : instance.TextureTileMetres > 0f ? instance.TextureTileMetres
            : instance.TextureTileMetres < 0f ? -1f
            : 0f;
    }

    /// <summary>Writes <paramref name="m"/>'s 16 components in the same sequential order
    /// (M11..M14, M21..M24, M31..M34, M41..M44) the old <c>BoxPush</c> push constant's raw memory
    /// layout used — i.e. GLSL's <c>mat4(col0, col1, col2, col3)</c> reconstruction from the four
    /// instance-attribute vec4s consumes the exact same bytes a single push-constant mat4 upload
    /// did, so the shader's arithmetic is bit-for-bit unchanged.</summary>
    private static void WriteMatrix(float[] buffer, ref int write, in Matrix4x4 m)
    {
        buffer[write++] = m.M11; buffer[write++] = m.M12; buffer[write++] = m.M13; buffer[write++] = m.M14;
        buffer[write++] = m.M21; buffer[write++] = m.M22; buffer[write++] = m.M23; buffer[write++] = m.M24;
        buffer[write++] = m.M31; buffer[write++] = m.M32; buffer[write++] = m.M33; buffer[write++] = m.M34;
        buffer[write++] = m.M41; buffer[write++] = m.M42; buffer[write++] = m.M43; buffer[write++] = m.M44;
    }

    /// <summary>The per-INPUT-index stores (the decompose cache and the cull states), grown before
    /// pass 1 because that pass already writes them. <see cref="Array.Resize{T}"/> keeps the entries a
    /// still-valid cache depends on.</summary>
    private void EnsureInputCapacity(int inputs)
    {
        if (_cull.Length < inputs)
        {
            // Doubling, so a list that grows by one a frame does not resize three arrays every frame.
            var capacity = Math.Max(inputs, Math.Max(_cull.Length * 2, InstanceMinimumCapacity));
            Array.Resize(ref _decomposedModels, capacity);
            Array.Resize(ref _decomposed, capacity);
            Array.Resize(ref _cull, capacity);
        }
    }

    /// <summary>
    /// Grows the per-frame stores to fit this frame. Called after pass 1, so
    /// <see cref="_meshGroupTally"/> is already known.
    /// <para>
    /// <b>The batch arrays are sized on their own terms, not the instance buffer's</b> (2026-09-03).
    /// They used to be reallocated only inside the buffer-growth branch, which was correct while
    /// their size was the compile-time constant <see cref="GroupCount"/> and they were therefore
    /// never too small. A meshed frame adds one batch slot per (mesh, material) group discovered at
    /// runtime, so a scene that gains a mesh WITHOUT gaining instances — swapping a box shell for an
    /// imported model is exactly that — would have written past the end of an array the old branch
    /// had no reason to revisit.
    /// </para>
    /// </summary>
    private void EnsureCapacity(int instances)
    {
        if (_buffer.Length < instances * FloatsPerInstance)
        {
            var capacity = Math.Max(instances, InstanceMinimumCapacity);
            _buffer = new float[capacity * FloatsPerInstance];
        }

        // Every kind group plus every mesh group could each contribute one batch, and a batch lands
        // in several lists — so one array of this length per list is enough for any
        // frame, with no per-frame arithmetic about which list gets what.
        var batchCapacity = GroupCount + _meshGroupTally;
        if (_batches.Length < batchCapacity)
        {
            _batches = new PrimitiveBatch[batchCapacity];
            _visibleBatches = new PrimitiveBatch[batchCapacity];
            _fixtureShadowBatches = new PrimitiveBatch[batchCapacity];
            _xrayBatches = new PrimitiveBatch[batchCapacity];
            _glowBatches = new PrimitiveBatch[batchCapacity];
        }
    }
}
