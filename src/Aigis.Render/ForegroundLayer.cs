using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// An optional second set of 3D primitives drawn ON TOP of the screen-space UI shapes
/// (<see cref="UiShapeDraw"/>) and UNDER overlay images and text, through its own
/// <see cref="ViewProjection"/> — e.g. small character figures walking on a menu panel.
/// <para>
/// Frame order with a layer present: scene → UI shapes → <b>depth cleared over the whole frame</b>
/// → foreground depth prepass + colour → overlay images → text. Clearing depth (not colour) is what
/// lets the layer sit on top of both the scene and the panels while still self-occluding correctly.
/// </para>
/// <para>
/// The layer never casts a shadow (it is excluded from every shadow pass) and never receives the
/// SUN's shadow: its shadow coordinates are packed with <see cref="NoShadowViewProjection"/>, whose
/// clip w is −1, which fails the fragment shader's <c>w &gt; 0</c> gate. It is otherwise lit by the
/// scene's lighting set (view 0's in split screen) evaluated at the layer's own world positions —
/// so scene point/spot lights, fixture shadows and an irradiance volume act on it as if its space
/// were the scene's. Menu use (sun + ambient) is unaffected; keep figures away from such scenes or
/// accept it. <see cref="Eye"/> is carried for API completeness; the renderers currently use the
/// scene's eye for specular.
/// </para>
/// <para>Only the opaque/visible batches are drawn — x-ray shells and additive glow in a foreground
/// layer are not rendered.</para>
/// </summary>
/// <param name="Instances">The figures, in the layer's own world space.</param>
/// <param name="ViewProjection">The layer's camera, Vulkan clip convention (see <see cref="Camera"/>).</param>
/// <param name="Eye">The layer camera's world position.</param>
public sealed record ForegroundLayer(IReadOnlyList<PrimitiveInstance> Instances, Matrix4x4 ViewProjection, Vector3 Eye)
{
    /// <summary>Shadow view-projection both renderers pack the layer with: every position maps to
    /// clip w = −1, so the sun shadow lookup (gated on w &gt; 0) is skipped. Identity would NOT do —
    /// a figure near the origin would sample the scene's shadow map at a meaningless texel.</summary>
    internal static readonly Matrix4x4 NoShadowViewProjection = new(
        0f, 0f, 0f, 0f,
        0f, 0f, 0f, 0f,
        0f, 0f, 0f, 0f,
        0f, 0f, 0f, -1f);
}
