using System.Numerics;
using Aigis.Player;
using Aigis.Render;

namespace MiniRing;

/// <summary>顔の窓を画面のどちらの脇に置くか。</summary>
public enum FaceSide
{
    /// <summary>左の脇（赤コーナー＝プレイヤー。HUD の YOU と同じ側）。</summary>
    Left,

    /// <summary>右の脇（青コーナー＝CPU。HUD の CPU と同じ側）。</summary>
    Right,
}

/// <summary>1 つの顔の窓: 誰の顔を、画面のどこに。</summary>
public readonly record struct FacePane(Fighter Fighter, RenderViewport Panel, Vector4 FrameColor);

/// <summary>
/// 画面の両脇に出す、両ボクサーの顔のライブ映像（往年の FPS の HUD にある顔の窓のような）。描画だけの仕掛けで、試合の計算には触れない。
/// 左が赤コーナー（プレイヤー）、右が青コーナー（CPU）。
/// <para><b>カメラは頭のボクセルの格子に固定する。</b>頭の剛体（＝頭のボクセルの格子）の座標系で、顔の正面（ローカル +Z）の
/// 決まった位置から頭の中心を向き、上は頭のローカル +Y。だから窓の中では顔がいつも正面・同じ大きさに見え、削れ・へこみ・
/// 血の変化がそのまま読める。逆に、まわり（グローブ・相手・リング・部屋）は頭の揺れに合わせて窓の中で振り回される。
/// 打たれた瞬間に背景が大きく流れるのは、頭が実際にそれだけ回ったから（見た目を足していない）。</para>
/// <para><b>映すのは実際のリングの上の顔。</b>別の絵を作らず、同じ場面（同じ体・グローブ・破片・部屋）を
/// 窓ごとにもう 1 つのカメラで描く。ホストは <see cref="Views"/> の 2 つ目以降を重ねる窓（<see cref="CameraView.IsOverlay"/>）として描く。</para>
/// <para><b>ヘッドレス（動画・スナップショット）との違い。</b>ヘッドレスは窓を別の 1 枚として描いて書き写すので、仕上げ（周辺減光・光のにじみ）が
/// 窓の中心で掛かり、窓の下の HUD は窓で上書きされる。ウィンドウでは周辺減光は画面全体に 1 つで、HUD は窓の上に描かれる。
/// だから両脇の窓の位置に HUD の文字を置かないこと（動画で消える）。</para>
/// </summary>
public sealed record FaceCam
{
    /// <summary>既定の窓: 画面の左右の端、縦の中ほどより少し下。</summary>
    public static FaceCam Default { get; } = new();

    /// <summary>窓の一辺（正方形）の、画面の高さに対する割合。上の得点・下の打撃の表示に掛からない大きさ。</summary>
    public float SizeOfScreenHeight { get; init; } = 0.3f;

    /// <summary>窓と画面の左右の端のすき間 [px]。</summary>
    public float MarginPixels { get; init; } = 24f;

    /// <summary>窓の中心の高さ（画面の高さに対する割合、上が 0）。</summary>
    public float CenterOfScreenHeight { get; init; } = 0.55f;

    /// <summary>縦の画角 [度]。狭いほど顔の歪みが少ない。</summary>
    public float FovYDegrees { get; init; } = 60f;

    /// <summary>
    /// 頭の中心から顔の正面へカメラを出す距離（頭の高さの半分を 1 とする）。頭の大きさに比例させるので、体の縮尺を変えても窓の中の顔は同じ大きさ。
    /// 4 では顔が窓の高さの 4 割ほどで小さかった（ユーザーの評価）ので、顔が 1.5 倍に見えるよう 4 / 1.5 ≈ 2.7 にした（画角が同じなら見かけの大きさは距離に反比例）。
    /// 近いほど構えたグローブはカメラの後ろへ回って写らなくなり、顔のすぐ前まで来た拳だけが写る。
    /// </summary>
    public float DistanceInHeadHalfHeights { get; init; } = 2.7f;

    /// <summary>カメラの向く先を、頭の中心から上へずらす量（頭の高さの半分を 1 とする）。目の高さを窓の真ん中に寄せる。</summary>
    public float AimAboveCenter { get; init; } = 0.1f;

    /// <summary>近い面 [m]。顔のすぐ前まで来た拳が切れないよう、ボクセル（5 mm）と同じくらいに小さく。</summary>
    public float NearPlane { get; init; } = 0.004f;

    /// <summary>遠い面 [m]。部屋の奥の壁まで入る長さ。</summary>
    public float FarPlane { get; init; } = 20f;

    /// <summary>赤コーナー（左の窓）の縁の色。HUD の YOU の色に合わせる。</summary>
    public Vector4 RedFrameColor { get; init; } = new(1f, 0.45f, 0.4f, 1f);

    /// <summary>青コーナー（右の窓）の縁の色。HUD の CPU の色に合わせる。</summary>
    public Vector4 BlueFrameColor { get; init; } = new(0.5f, 0.65f, 1f, 1f);

    /// <summary>窓の縁の太さ [px]。</summary>
    public float FramePixels { get; init; } = 3f;

    /// <summary>画面の大きさから、<paramref name="side"/> の脇の窓の四角（左上が原点・下向き Y のピクセル）を決める。画面に収まらない大きさにはしない。</summary>
    public RenderViewport Panel(Vector2 screenSizePixels, FaceSide side)
    {
        // 2 つの窓が重ならないよう、一辺は画面の幅の半分（からすき間を引いたもの）まで。
        var size = (int)MathF.Max(1f, MathF.Min(screenSizePixels.Y * SizeOfScreenHeight, (screenSizePixels.X * 0.5f) - (2f * MarginPixels)));
        var x = side == FaceSide.Left
            ? (int)MathF.Min(MarginPixels, MathF.Max(0f, screenSizePixels.X - size))
            : (int)MathF.Max(0f, screenSizePixels.X - MarginPixels - size);
        var y = (int)Math.Clamp((screenSizePixels.Y * CenterOfScreenHeight) - (size * 0.5f), 0f, MathF.Max(0f, screenSizePixels.Y - size));
        return new RenderViewport(x, y, size, size);
    }

    /// <summary>このフレームの顔の窓: 左に赤コーナー（プレイヤー）、右に青コーナー（CPU）。</summary>
    public IReadOnlyList<FacePane> Panes(BoxingGame game, Vector2 screenSizePixels)
    {
        ArgumentNullException.ThrowIfNull(game);
        return
        [
            new FacePane(game.Player, Panel(screenSizePixels, FaceSide.Left), RedFrameColor),
            new FacePane(game.Cpu, Panel(screenSizePixels, FaceSide.Right), BlueFrameColor),
        ];
    }

    /// <summary>
    /// <paramref name="fighter"/> の頭に固定したカメラ: 目の位置と、ワールドから切り取り空間への変換。
    /// 頭の剛体の今の姿勢だけから決まる（追いかけ・平滑化をしない。格子に固定なので）。
    /// </summary>
    public (Matrix4x4 ViewProjection, Vector3 Eye) Camera(BoxingGame game, Fighter fighter, float aspect)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(fighter);
        var head = game.World.GetState(fighter.Parts.Head);
        var halfHeight = fighter.HalfExtentsOf(fighter.Parts.Head).Y;
        var eye = head.Position + Vector3.Transform(new Vector3(0f, 0f, DistanceInHeadHalfHeights * halfHeight), head.Orientation);
        var target = head.Position + Vector3.Transform(new Vector3(0f, AimAboveCenter * halfHeight, 0f), head.Orientation);
        var up = Vector3.Transform(Vector3.UnitY, head.Orientation);
        var viewProjection = Aigis.Render.Camera.ViewProjection(eye, target, up, FovYDegrees * MathF.PI / 180f, aspect, NearPlane, FarPlane);
        return (viewProjection, eye);
    }

    /// <summary>1 つの顔の窓の描き方（窓の中へ、下の画面の上に重ねる）。</summary>
    public CameraView View(BoxingGame game, FacePane pane)
    {
        var (viewProjection, eye) = Camera(game, pane.Fighter, pane.Panel.AspectRatio);
        return CameraView.Create(pane.Panel, eye, _ => viewProjection, isOverlay: true);
    }

    /// <summary>
    /// 1 フレームの描き方: 画面全体を <paramref name="main"/> で、その上に両ボクサーの顔の窓を重ねる（重ねる窓は最後）。
    /// ホストの <c>GameHost.Options.DynamicViews</c> に渡す。
    /// </summary>
    public IReadOnlyList<CameraView> Views(IGameCamera main, BoxingGame game, Vector2 screenSizePixels)
    {
        ArgumentNullException.ThrowIfNull(main);
        var full = new RenderViewport(0, 0, Math.Max(1, (int)screenSizePixels.X), Math.Max(1, (int)screenSizePixels.Y));
        var views = new List<CameraView> { CameraView.Create(full, main.Eye, main.ViewProjection) };
        views.AddRange(Panes(game, screenSizePixels).Select(pane => View(game, pane)));
        return views;
    }

    /// <summary>窓の縁（中は透明・コーナーの色）。HUD の図形として、窓の外側に描く。</summary>
    public IReadOnlyList<UiShapeDraw> Frames(BoxingGame game, Vector2 screenSizePixels)
        => [.. Panes(game, screenSizePixels).Select(pane =>
        {
            var panel = pane.Panel;
            var size = new Vector2(panel.WidthPx, panel.HeightPx) + new Vector2(FramePixels * 2f);
            var center = new Vector2(panel.XPx + (panel.WidthPx * 0.5f), panel.YPx + (panel.HeightPx * 0.5f));
            return new UiShapeDraw(center, size, Vector4.Zero, Vector4.Zero, pane.FrameColor, FramePixels);
        })];
}
