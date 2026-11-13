using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace GTotalOptimizer.App.Visuals;

/// <summary>
/// One source of truth for GCO's crisp, 45-degree chamfers in DIPs.
/// Never stretch a normalized 1000x1000 Path: that distorts the angle.
/// </summary>
public static class ChamferGeometry
{
    public const double StandardCut = 14.0;

    public static double ScaledCut(FrameworkElement element, double designCut)
    {
        var window = Window.GetWindow(element);
        if (window is null) return designCut;
        var scale = Math.Clamp(Math.Min(window.ActualWidth / 1280.0,
                                        window.ActualHeight / 760.0), 0.80, 1.25);
        return designCut * scale;
    }

    public static Geometry Create(double width, double height, double cut, double inset = 0)
    {
        var left = inset;
        var top = inset;
        var right = Math.Max(left, width - inset);
        var bottom = Math.Max(top, height - inset);
        cut = Math.Clamp(cut, 0, Math.Min(right - left, bottom - top) / 3.0);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(left + cut, top), true, true);
            ctx.LineTo(new Point(right - cut, top), true, false);
            ctx.LineTo(new Point(right, top + cut), true, false);
            ctx.LineTo(new Point(right, bottom - cut), true, false);
            ctx.LineTo(new Point(right - cut, bottom), true, false);
            ctx.LineTo(new Point(left + cut, bottom), true, false);
            ctx.LineTo(new Point(left, bottom - cut), true, false);
            ctx.LineTo(new Point(left, top + cut), true, false);
        }
        path.Freeze();
        return path;
    }

    // Only the upper-right corner of the Game Root input is cut; all other
    // corners stay square. Geometry scales from the actual rendered dimensions.
    public static Geometry CreateTopRightCut(double width, double height,
        double cut, double inset = 0)
    {
        var left = inset;
        var top = inset;
        var right = Math.Max(left, width - inset);
        var bottom = Math.Max(top, height - inset);
        cut = Math.Clamp(cut, 0, Math.Min(right - left, bottom - top) / 2.0);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(left, top), true, true);
            ctx.LineTo(new Point(right - cut, top), true, false);
            ctx.LineTo(new Point(right, top + cut), true, false);
            ctx.LineTo(new Point(right, bottom), true, false);
            ctx.LineTo(new Point(left, bottom), true, false);
        }
        path.Freeze();
        return path;
    }

    // The INNER top is the one line BELOW the logo/header, NOT a parallel
    // duplicate of the outer top perimeter.
    // Straight inset runs ignore the decorative raster's circuit dents.
    public static Geometry CreateContentFrame(double width, double height,
        double headerTop, double inset, double cut)
    {
        var left = inset;
        var right = Math.Max(left, width - inset);
        var top = Math.Clamp(headerTop, inset, Math.Max(inset, height - inset));
        var bottom = Math.Max(top, height - inset);
        cut = Math.Clamp(cut, 0, Math.Min(right - left, bottom - top) / 3.0);

        // One straight inner baseline at the original raised corner height.
        // The previous 3-DIP drop/run-out created an unwanted step on both
        // sides. Do not trace the outer PNG's decorative dents.
        const double cornerRelief = 3.0;
        var lowerCut = Math.Max(cut, 17.0);
        var bottomLine = bottom - cornerRelief;

        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(left + cut, top), false, true);
            ctx.LineTo(new Point(right - cut, top), true, false);
            ctx.LineTo(new Point(right, top + cut), true, false);
            ctx.LineTo(new Point(right, bottomLine - lowerCut), true, false);
            ctx.LineTo(new Point(right - lowerCut, bottomLine), true, false);
            ctx.LineTo(new Point(left + lowerCut, bottomLine), true, false);
            ctx.LineTo(new Point(left, bottomLine - lowerCut), true, false);
            ctx.LineTo(new Point(left, top + cut), true, false);
        }
        path.Freeze();
        return path;
    }

    // One owner geometry for both left corners. The full-resolution portrait
    // is resized first; its tiny lower-left wedge is clipped only where it
    // meets the decorative outer border (which paints above the portrait).
    // This keeps the portrait out from under the chassis' bent corner.
    public static Geometry CreatePortraitClip(double width, double height,
        double cut, bool clipLowerLeft = true)
    {
        cut = Math.Clamp(cut, 0, Math.Min(width, height) / 3.0);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(cut, 0), true, true);
            ctx.LineTo(new Point(width, 0), true, false);
            ctx.LineTo(new Point(width, height), true, false);
            if (clipLowerLeft)
            {
                ctx.LineTo(new Point(cut, height), true, false);
                ctx.LineTo(new Point(0, height - cut), true, false);
            }
            else
                ctx.LineTo(new Point(0, height), true, false);
            ctx.LineTo(new Point(0, cut), true, false);
        }
        path.Freeze();
        return path;
    }
}

/// <summary>
/// One global source-anchored color field for GCO's INNER neon outlines.
/// Yellow originates near V at the lower left; cyan originates near the
/// owner's upper-right skyline. Each border samples these common scene-space
/// coordinates, so separate cards cannot share an artificial 50% midpoint.
/// The raster outer frame remains entirely owner-provided and untouched.
/// </summary>
public static class SpatialNeonField
{
    private static (Point Yellow, Point Cyan) Anchors(double width, double height) =>
        (new Point(width * .09, height * .80),
         new Point(width * .93, height * .12));

    public static Brush Create(FrameworkElement element, Brush fallback)
    {
        var window = Window.GetWindow(element);
        if (window?.Content is not FrameworkElement root ||
            root.ActualWidth < 1 || root.ActualHeight < 1)
            return fallback;
        Point origin;
        try { origin = element.TranslatePoint(new Point(0, 0), root); }
        catch (InvalidOperationException) { return fallback; }

        var (yellow, cyan) = Anchors(root.ActualWidth, root.ActualHeight);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(yellow.X - origin.X, yellow.Y - origin.Y),
            EndPoint = new Point(cyan.X - origin.X, cyan.Y - origin.Y)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 240, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(248, 229, 28), .28));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(111, 205, 186), .60));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 226, 242), .82));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 226, 242), 1));
        brush.Freeze();
        return brush;
    }

    // Geometry regression: the field must become cooler towards the
    // skyline and warmer towards the lower-left V on *every* card.
    public static double PositionAt(FrameworkElement element, Point localPoint)
    {
        var window = Window.GetWindow(element);
        if (window?.Content is not FrameworkElement root ||
            root.ActualWidth < 1 || root.ActualHeight < 1)
            throw new InvalidOperationException("Spatial neon requires a measured WPF root");
        var scene = element.TranslatePoint(localPoint, root);
        var (yellow, cyan) = Anchors(root.ActualWidth, root.ActualHeight);
        double dx = cyan.X - yellow.X;
        double dy = cyan.Y - yellow.Y;
        return Math.Clamp(((scene.X - yellow.X) * dx + (scene.Y - yellow.Y) * dy) /
                          (dx * dx + dy * dy), 0, 1);
    }
}

/// <summary>
/// Static WPF vector decoration with optional soft glow and neon corner fins.
/// Layout comes from the containing Grid; this element never measures to 1000 DIPs.
/// </summary>
public sealed class ChamferOutline : FrameworkElement
{
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CornerCutProperty =
        DependencyProperty.Register(nameof(CornerCut), typeof(double), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(ChamferGeometry.StandardCut, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlowProperty =
        DependencyProperty.Register(nameof(Glow), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpatialStrokeProperty =
        DependencyProperty.Register(nameof(SpatialStroke), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentCornersProperty =
        DependencyProperty.Register(nameof(AccentCorners), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MetallicFinishProperty =
        DependencyProperty.Register(nameof(MetallicFinish), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TopRightOnlyProperty =
        DependencyProperty.Register(nameof(TopRightOnly), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LitProperty =
        DependencyProperty.Register(nameof(Lit), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    // Stable vector material: no raster overlay, shader or frame-by-frame updates.
    private static readonly Brush Reflection = BuildSurfaceBrush(
        (Color.FromArgb(116, 215, 250, 255), 0),
        (Color.FromArgb(45, 137, 197, 210), .38),
        (Color.FromArgb(0, 20, 60, 70), 1));
    private static readonly Brush DiagonalFlare = BuildSurfaceBrush(
        (Color.FromArgb(0, 255, 255, 255), 0),
        (Color.FromArgb(75, 240, 253, 255), .48),
        (Color.FromArgb(0, 255, 255, 255), 1));
    private static Brush BuildSurfaceBrush(params (Color Shade, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1)
        };
        foreach (var (shade, offset) in stops)
            brush.GradientStops.Add(new GradientStop(shade, offset));
        brush.Freeze();
        return brush;
    }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double CornerCut { get => (double)GetValue(CornerCutProperty); set => SetValue(CornerCutProperty, value); }
    public bool Glow { get => (bool)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public bool SpatialStroke { get => (bool)GetValue(SpatialStrokeProperty); set => SetValue(SpatialStrokeProperty, value); }
    public bool AccentCorners { get => (bool)GetValue(AccentCornersProperty); set => SetValue(AccentCornersProperty, value); }
    public bool MetallicFinish { get => (bool)GetValue(MetallicFinishProperty); set => SetValue(MetallicFinishProperty, value); }
    public bool TopRightOnly { get => (bool)GetValue(TopRightOnlyProperty); set => SetValue(TopRightOnlyProperty, value); }
    public bool Lit { get => (bool)GetValue(LitProperty); set => SetValue(LitProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width < 6 || RenderSize.Height < 6) return;
        var thickness = Math.Max(0.5, StrokeThickness);
        var inset = Math.Max(thickness / 2.0 + 0.25, 1.0);
        var cut = ChamferGeometry.ScaledCut(this, CornerCut);
        var shape = TopRightOnly
            ? ChamferGeometry.CreateTopRightCut(RenderSize.Width, RenderSize.Height, cut, inset)
            : ChamferGeometry.Create(RenderSize.Width, RenderSize.Height, cut, inset);
        var stroke = SpatialStroke ? SpatialNeonField.Create(this, Stroke) : Stroke;

        if (Glow)
        {
            var soft = stroke.CloneCurrentValue();
            soft.Opacity = 0.12;
            var near = stroke.CloneCurrentValue();
            near.Opacity = 0.24;
            dc.DrawGeometry(null, new Pen(soft, thickness + 12) { LineJoin = PenLineJoin.Miter }, shape);
            dc.DrawGeometry(null, new Pen(near, thickness + 6) { LineJoin = PenLineJoin.Miter }, shape);
        }
        dc.DrawGeometry(Fill, new Pen(stroke, thickness) { LineJoin = PenLineJoin.Miter }, shape);

        if (MetallicFinish)
        {
            // Cast-metal base, chrome lip, angled specular window and dark lower edge.
            // The entire sheen is clipped to the SAME 45-degree material geometry.
            var w = RenderSize.Width;
            var h = RenderSize.Height;
            dc.PushClip(shape);
            dc.PushOpacity(Lit ? 1.0 : .27);
            dc.DrawRectangle(Reflection, null, new Rect(1, 1, w - 2,
                Math.Min(15, h * .46)));
            var facet = new StreamGeometry();
            using (var ctx = facet.Open())
            {
                ctx.BeginFigure(new Point(Math.Max(0, cut - 3), 0), true, true);
                ctx.LineTo(new Point(Math.Min(w, cut + w * .28), 0), true, false);
                ctx.LineTo(new Point(Math.Min(w, w * .23), h * .55), true, false);
                ctx.LineTo(new Point(0, h * .55), true, false);
            }
            facet.Freeze();
            dc.DrawGeometry(DiagonalFlare, null, facet);
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(135, 0, 7, 12)), null,
                new Rect(0, h - 3, w, 3));
            dc.Pop();
            dc.Pop();
            if (w > 30 && h > 18)
            {
                var rim = new Pen(new SolidColorBrush(
                    Color.FromArgb(Lit ? (byte)205 : (byte)46, 184, 236, 247)), 1.0);
                dc.DrawLine(rim, new Point(cut + 4, inset + 1.5),
                           new Point(w - cut - 4, inset + 1.5));
                var inner = ChamferGeometry.Create(w, h, Math.Max(2, cut - 1), inset + 2.3);
                dc.DrawGeometry(null, new Pen(
                    new SolidColorBrush(Color.FromArgb(Lit ? (byte)72 : (byte)24, 108, 173, 188)),
                    .7) { LineJoin = PenLineJoin.Miter }, inner);
            }
        }

        if (AccentCorners)
        {
            var y = new Pen(new SolidColorBrush(Color.FromRgb(255, 240, 0)), 4.3);
            var c = new Pen(new SolidColorBrush(Color.FromRgb(0, 226, 242)), 3.7);
            var w = RenderSize.Width;
            var h = RenderSize.Height;
            // Accent fins lie on the same 45-degree frame, not separate PNGs.
            dc.DrawLine(y, new Point(inset + cut + 10, inset),
                           new Point(Math.Min(w / 3, inset + cut + 78), inset));
            dc.DrawLine(y, new Point(inset, inset + cut + 10),
                           new Point(inset, inset + cut + 39));
            dc.DrawLine(c, new Point(Math.Max(w * .72, w - inset - cut - 78), h - inset),
                           new Point(w - inset - cut - 10, h - inset));
            dc.DrawLine(c, new Point(w - inset, h - inset - cut - 38),
                           new Point(w - inset, h - inset - cut - 10));
        }
    }
}

/// <summary>Portrait clipping follows the content chassis left corner only.</summary>
public static class ChamferClip
{
    public static readonly DependencyProperty CornerCutProperty =
        DependencyProperty.RegisterAttached("CornerCut", typeof(double), typeof(ChamferClip),
            new PropertyMetadata(0.0, Changed));

    // The portrait continues to use the existing left-only geometry.
    // Card artwork explicitly selects the same four-corner shape as its
    // original ChamferOutline, inset enough not to paint over its stroke.
    public static readonly DependencyProperty AllCornersProperty =
        DependencyProperty.RegisterAttached("AllCorners", typeof(bool), typeof(ChamferClip),
            new PropertyMetadata(false, Changed));

    public static void SetAllCorners(DependencyObject obj, bool value) =>
        obj.SetValue(AllCornersProperty, value);
    public static bool GetAllCorners(DependencyObject obj) =>
        (bool)obj.GetValue(AllCornersProperty);

    public static void SetCornerCut(DependencyObject obj, double value) =>
        obj.SetValue(CornerCutProperty, value);
    public static double GetCornerCut(DependencyObject obj) =>
        (double)obj.GetValue(CornerCutProperty);

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.SizeChanged -= Sized;
        element.SizeChanged += Sized;
        element.Loaded -= Loaded;
        element.Loaded += Loaded;
        Update(element);
    }

    private static void Sized(object sender, SizeChangedEventArgs e) => Update((FrameworkElement)sender);
    private static void Loaded(object sender, RoutedEventArgs e) => Update((FrameworkElement)sender);

    private static void Update(FrameworkElement e)
    {
        if (e.ActualWidth < 1 || e.ActualHeight < 1) return;
        var cut = ChamferGeometry.ScaledCut(e, GetCornerCut(e));
        e.Clip = GetAllCorners(e)
            ? ChamferGeometry.Create(e.ActualWidth, e.ActualHeight, cut, inset: 2.5)
            : ChamferGeometry.CreatePortraitClip(e.ActualWidth, e.ActualHeight,
                cut, clipLowerLeft: true);
    }
}


/// <summary>
/// Open upper/side portrait outline. It deliberately has NO lower edge or
/// lower-left diagonal stroke: yellow portrait and yellow outer border meet
/// without the former black seam. The image itself is clipped separately by
/// the authoritative CreatePortraitClip at its actual rendered size.
/// </summary>
public sealed class PortraitOutline : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = RenderSize.Width;
        var height = RenderSize.Height;
        if (width < 2 || height < 2) return;
        var cut = Math.Clamp(ChamferGeometry.ScaledCut(this,
            ChamferGeometry.StandardCut), 0, Math.Min(width, height) / 3.0);
        var outline = new StreamGeometry();
        using (var ctx = outline.Open())
        {
            ctx.BeginFigure(new Point(0, height - cut), false, false);
            ctx.LineTo(new Point(0, cut), true, false);
            ctx.LineTo(new Point(cut, 0), true, false);
            ctx.LineTo(new Point(width, 0), true, false);
            ctx.LineTo(new Point(width, height - cut), true, false);
        }
        outline.Freeze();
        var pen = new Pen(Brushes.Black, 1.6)
        { LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        dc.DrawGeometry(null, pen, outline);
    }
}

/// <summary>Equal-sized parallelogram tab with a genuine 45-degree slope.</summary>
public sealed class SlantedTabShape : FrameworkElement
{
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SlantedTabShape),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SlantedTabShape),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(SlantedTabShape),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LitProperty =
        DependencyProperty.Register(nameof(Lit), typeof(bool), typeof(SlantedTabShape),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool Lit { get => (bool)GetValue(LitProperty); set => SetValue(LitProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (w < h * 2 || h < 4) return;
        var inset = StrokeThickness / 2 + .25;
        var shift = (h - 2 * inset) * 0.92; // ~47.5°, just steeper than 45°.
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(inset + shift, inset), true, true);
            ctx.LineTo(new Point(w - inset, inset), true, false);
            ctx.LineTo(new Point(w - inset - shift, h - inset), true, false);
            ctx.LineTo(new Point(inset, h - inset), true, false);
        }
        geometry.Freeze();
        // Multi-level anodized metal: shaded solid body, lip, controlled shine,
        // deep lower edge and one angled glass reflection across the face.
        dc.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), geometry);
        dc.PushClip(geometry);
        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1)
        };
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(
            Lit ? (byte)135 : (byte)50, 255, 255, 255), 0));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(
            Lit ? (byte)36 : (byte)16, 215, 243, 252), .55));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 215, 243, 252), 1));
        sheen.Freeze();
        dc.DrawRectangle(sheen, null, new Rect(0, 0, w, h * .55));
        var sliver = new StreamGeometry();
        using (var c = sliver.Open())
        {
            c.BeginFigure(new Point(w * .32, 0), true, true);
            c.LineTo(new Point(w * .49, 0), true, false);
            c.LineTo(new Point(w * .37, h), true, false);
            c.LineTo(new Point(w * .27, h), true, false);
        }
        sliver.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(
            Lit ? (byte)42 : (byte)18, 205, 244, 248)), null, sliver);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 7, 12)),
                         null, new Rect(0, h - 3.2, w, 3.2));
        dc.Pop();
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(
            Lit ? (byte)240 : (byte)124, 234, 255, 255)), 1.2),
            new Point(inset + shift + 3, inset + 1.3),
            new Point(w - inset - 3, inset + 1.3));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(
            Lit ? (byte)132 : (byte)60, 0, 7, 12)), 1.5),
            new Point(inset + 4, h - inset - 1.4),
            new Point(w - inset - shift - 4, h - inset - 1.4));
    }
}


/// <summary>
/// Three clean original WPF line symbols: install/download, game controller
/// and layered collected data. Frozen vector geometry, no bitmaps or animation.
/// Cyan core strokes are backed by two translucent bloom strokes for soft glow.
/// </summary>
public enum StageEmblemKind { Chip, Ignition, Data }

public sealed class StageEmblem : FrameworkElement
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StageEmblemKind), typeof(StageEmblem),
            new FrameworkPropertyMetadata(StageEmblemKind.Chip,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public StageEmblemKind Kind
    {
        get => (StageEmblemKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static Geometry Path(string data)
    {
        var path = Geometry.Parse(data);
        path.Freeze();
        return path;
    }

    private static Pen Stroke(byte alpha, double width)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 226, 242));
        brush.Freeze();
        var pen = new Pen(brush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static readonly Pen OuterBloom = Stroke(32, 11.0);
    private static readonly Pen InnerBloom = Stroke(100, 6.8);
    private static readonly Pen CyanCore = Stroke(255, 3.8);
    // Ref-specific lighter bloom; preserve original pens for icons 1 and 3.
    private static readonly Pen GamepadOuterBloom = Stroke(24, 8.5);
    private static readonly Pen GamepadInnerBloom = Stroke(80, 5.7);
    private static readonly Pen GamepadCyanCore = Stroke(255, 3.15);
    private static readonly Pen IceHighlight = new(new SolidColorBrush(
        Color.FromArgb(167, 220, 255, 255)), 0.9)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round
    };
    // Face buttons need a tighter halo than the main outline so four distinct
    // dots remain visible at the small size in the workflow card.
    private static readonly Pen ButtonGlow = Stroke(85, 3.4);
    private static readonly Brush ButtonFill = new SolidColorBrush(Color.FromRgb(0, 226, 242));

    // Install: prominent down arrow entering a clean, open-topped tray.
    private static readonly Geometry InstallArrow =
        Path("M 32,8 L 32,39 M 21,29 L 32,40 L 43,29");
    private static readonly Geometry InstallTray =
        Path("M 11,42 L 11,50 Q 11,55 16,55 L 48,55 Q 53,55 53,50 L 53,42");

    // Direct vector trace from the owner's 700x96 cyan controller reference.
    // Coordinates follow the extracted stroke centerline (80x56 canvas):
    // twin rounded shoulders, straight recessed top, flat lower bridge,
    // deep curved grips. No hand-invented controller silhouette.
    private static readonly Geometry Gamepad =
        Path("M 13.4,11.7 " +
             "C 14.7,9.6 16.5,7.7 18.7,6.2 " +
             "C 20.6,4.9 22.9,3.9 25.0,4.0 " +
             "C 27.9,4.1 30.6,6.1 33.5,7.3 " +
             "C 38.3,7.3 42.5,7.3 47.3,7.3 " +
             "C 50.5,6.1 53.6,4.0 56.8,4.0 " +
             "C 59.4,4.0 62.1,5.4 64.2,7.3 " +
             "C 66.0,8.9 67.4,10.8 68.5,12.8 " +
             "C 72.4,19.9 73.0,28.2 73.8,37.0 " +
             "C 74.1,40.5 74.4,44.1 72.7,46.9 " +
             "C 71.8,48.4 70.2,49.7 68.5,50.2 " +
             "C 67.4,50.5 66.3,50.5 65.3,50.2 " +
             "C 59.6,48.8 57.4,39.8 52.6,37.0 " +
             "C 49.4,35.2 45.2,35.9 40.9,35.9 " +
             "C 36.6,35.9 32.3,35.2 29.3,37.0 " +
             "C 25.2,39.4 23.4,46.5 18.7,49.1 " +
             "C 16.1,50.5 12.6,50.7 10.2,49.1 " +
             "C 8.6,48.1 7.5,46.5 7.0,44.7 " +
             "C 6.4,42.6 6.7,40.4 7.0,38.1 " +
             "C 8.1,28.8 8.5,19.1 13.4,11.7 Z");
    // Filled D-pad contour traced from that same reference image.
    private static readonly Geometry GamepadCross =
        Path("M 27.1,15.0 L 24.0,15.0 L 24.0,18.3 " +
             "L 22.9,19.4 L 19.7,19.4 L 18.7,20.5 " +
             "L 18.7,23.8 L 22.9,23.8 L 24.0,24.9 " +
             "L 24.0,28.2 L 25.0,29.3 L 26.1,28.2 " +
             "L 27.1,28.2 L 27.1,24.9 L 28.2,23.8 " +
             "L 31.4,23.8 L 32.4,22.7 L 32.4,20.5 " +
             "L 31.4,19.4 L 30.3,20.5 L 29.3,20.5 " +
             "L 27.1,18.3 Z");

    // Collect: three easily read layers, with a closed upper data plate.
    private static readonly Geometry DataTop =
        Path("M 32,8 L 54,20 L 32,32 L 10,20 Z");
    private static readonly Geometry DataMiddle =
        Path("M 10,32 L 32,44 L 54,32");
    private static readonly Geometry DataBottom =
        Path("M 10,44 L 32,56 L 54,44");

    private static void DrawNeon(DrawingContext dc, Geometry geometry,
        bool useReferenceWeight = false)
    {
        dc.DrawGeometry(null, useReferenceWeight ? GamepadOuterBloom : OuterBloom, geometry);
        dc.DrawGeometry(null, useReferenceWeight ? GamepadInnerBloom : InnerBloom, geometry);
        dc.DrawGeometry(null, useReferenceWeight ? GamepadCyanCore : CyanCore, geometry);
        dc.DrawGeometry(null, IceHighlight, geometry);
    }

    private static void DrawButton(DrawingContext dc, Point center)
    {
        dc.DrawEllipse(null, ButtonGlow, center, 2.1, 2.1);
        dc.DrawEllipse(ButtonFill, null, center, 1.8, 1.8);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(190, 220, 255, 255)),
            null, new Point(center.X - 0.5, center.Y - 0.5), 0.75, 0.75);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width < 2 || RenderSize.Height < 2)
            return;

        // The two original icons retain 64x64 units; the directly traced
        // controller has its own natural 80x56 reference aspect ratio.
        dc.PushTransform(new ScaleTransform(
            RenderSize.Width / (Kind == StageEmblemKind.Ignition ? 80.0 : 64.0),
            RenderSize.Height / (Kind == StageEmblemKind.Ignition ? 56.0 : 64.0)));
        switch (Kind)
        {
            case StageEmblemKind.Chip:
                DrawNeon(dc, InstallArrow);
                DrawNeon(dc, InstallTray);
                break;
            case StageEmblemKind.Ignition:
                DrawNeon(dc, Gamepad, useReferenceWeight: true);
                dc.DrawGeometry(null, ButtonGlow, GamepadCross);
                dc.DrawGeometry(ButtonFill, null, GamepadCross);
                DrawButton(dc, new Point(54.7, 15.6));
                DrawButton(dc, new Point(60.2, 21.5));
                DrawButton(dc, new Point(54.7, 27.1));
                DrawButton(dc, new Point(49.3, 21.7));
                break;
            case StageEmblemKind.Data:
                DrawNeon(dc, DataTop);
                DrawNeon(dc, DataMiddle);
                DrawNeon(dc, DataBottom);
                break;
        }
        dc.Pop();
    }
}

/// <summary>
/// One bright yellow-left / cyan-right OUTER chassis. Black exists outside
/// the spine and in the inside channel; no broad grey border wash.
/// </summary>
public sealed class TechChassisOutline : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(TechChassisOutline),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    // The two outlines here are INVISIBLE DESIGN LIMITS, not painted rails.
    // The filled band is their difference. All circuitry is cut OUT of that
    // band, so no ornament can drift beyond either hard limit.
    private static Geometry CreateBand(double width, double height, double scale, double cut)
    {
        var outside = ChamferGeometry.Create(width, height, cut, 3.2 * scale);
        var inside = ChamferGeometry.Create(width, height, cut, 19.0 * scale);
        return Geometry.Combine(outside, inside, GeometryCombineMode.Exclude, null);
    }

    private static void CutRect(GeometryGroup holes, double x, double y, double width, double height)
    {
        holes.Children.Add(new RectangleGeometry(new Rect(x, y, width, height)));
    }

    private static void CutDot(GeometryGroup holes, double x, double y, double radius)
    {
        holes.Children.Add(new EllipseGeometry(new Point(x, y), radius, radius));
    }

    private static void CutShape(GeometryGroup holes, params Point[] points)
    {
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
        }
        path.Freeze();
        holes.Children.Add(path);
    }

    private static void CutTrace(GeometryGroup holes, double thickness, params Point[] points)
    {
        var route = new StreamGeometry();
        using (var ctx = route.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (var i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
        }
        route.Freeze();
        var pen = new Pen(Brushes.Black, thickness)
        {
            LineJoin = PenLineJoin.Miter,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        holes.Children.Add(route.GetWidenedPathGeometry(pen));
    }

    // An annular NEGATIVE hole: like the hollow PCB terminals in the
    // reference print, with the original neon material left in its centre.
    private static void CutRing(GeometryGroup holes, double x, double y,
        double radius, double wall)
    {
        var centre = new Point(x, y);
        var outer = new EllipseGeometry(centre, radius, radius);
        var coreRadius = Math.Max(0.4, radius - wall);
        var core = new EllipseGeometry(centre, coreRadius, coreRadius);
        holes.Children.Add(Geometry.Combine(outer, core,
            GeometryCombineMode.Exclude, null));
    }

    // Reinterprets the original Cyberpunk black-on-yellow artwork, NOT
    // a repeating technical pattern. Three distinct material systems:
    // long routed lines with spaced terminals, a solid tapered machinery
    // silhouette, and an asymmetric notched housing. Large untouched
    // fields between systems are intentional.
    private static void CarveTop(GeometryGroup c, double w, double s, double o, double i)
    {
        // WEST / circuit print: paired extended traces with angular bends.
        // The original reference uses TWO dominant parallel tracks with
        // drilled tips, not a uniform fan of many little terminal icons.
        var x = w * .068;
        CutTrace(c, 1.55*s,
            new Point(x, o + 4*s), new Point(x + 78*s, o + 4*s),
            new Point(x + 84*s, o + 10*s), new Point(x + 195*s, o + 10*s));
        CutRing(c, x + 195*s, o + 10*s, 2.8*s, 1.4*s);
        CutTrace(c, 1.45*s,
            new Point(x + 23*s, o + 13*s),
            new Point(x + 81*s, o + 13*s),
            new Point(x + 88*s, o + 6*s),
            new Point(x + 163*s, o + 6*s));
        CutDot(c, x + 163*s, o + 6*s, 2.8*s);
        // A standalone black contact is part of this circuit field, as in
        // the printed strip, but it is not repeated everywhere.
        CutDot(c, x + 37*s, o + 8.5*s, 2.3*s);

        // MID-WEST / solid-to-thin machinery blade. The head occupies much
        // of the band. Its narrow rear conductor gradually loses weight.
        // Three neighboring engravings are in one organized stripe cluster.
        x = w * .365;
        CutShape(c,
            new Point(x, o + 4*s),
            new Point(x + 43*s, o + 4*s),
            new Point(x + 49*s, o + 10*s),
            new Point(x + 107*s, o + 10*s),
            new Point(x + 109*s, o + 10*s),
            new Point(x + 166*s, o + 10*s),
            new Point(x + 166*s, o + 12*s),
            new Point(x + 109*s, o + 12*s),
            new Point(x + 107*s, o + 14*s),
            new Point(x + 49*s, o + 14*s),
            new Point(x + 43*s, o + 14*s),
            new Point(x, o + 14*s));
        // The long body is followed by a single coherent cross-hatched
        // cluster and a diminishing ladder, instead of scattered dashes.
        for (var n = 0; n < 6; n++)
        {
            var ax = x + (178 + 7*n)*s;
            CutTrace(c, 1.5*s,
                new Point(ax, o + 5*s), new Point(ax + 6*s, o + 11*s));
        }
        var widths = new[] { 12.0, 9.0, 6.0, 3.0 };
        var px = x + 230*s;
        foreach (var length in widths)
        {
            CutRect(c, px, o + 7*s, length*s, 5.5*s);
            px += (length + 4.3)*s;
        }

        // EAST / an enclosed offset stepped cut. One large cavity takes
        // precedence over small decorations. A narrow colored bridge splits
        // the notched tail. This is intentionally NOT a mirror of the west.
        x = w * .795;
        CutShape(c,
            new Point(x, o + 4*s),
            new Point(x + 54*s, o + 4*s),
            new Point(x + 66*s, o + 13*s),
            new Point(x + 109*s, o + 13*s),
            new Point(x + 116*s, o + 9*s),
            new Point(x + 116*s, o + 5*s),
            new Point(x + 85*s, o + 5*s),
            new Point(x + 78*s, o + 2.7*s),
            new Point(x + 8*s, o + 2.7*s));
        CutRect(c, x + 125*s, o + 5*s, 26*s, 4.8*s);
        CutRect(c, x + 156*s, o + 5*s, 13*s, 4.8*s);
        CutDot(c, x + 179*s, o + 8*s, 3.1*s);
    }

    private static void CarveLeft(GeometryGroup c, double h, double s, double o, double i)
    {
        // TOP / the reference's two uninterrupted routed conductors, each
        // one diagonal shoulder and a hollow or filled circular terminal.
        var y = h * .118;
        CutTrace(c, 1.45*s,
            new Point(o + 4*s, y),
            new Point(o + 4*s, y + 60*s),
            new Point(o + 11*s, y + 67*s),
            new Point(o + 11*s, y + 155*s));
        CutRing(c, o + 11*s, y + 155*s, 2.85*s, 1.4*s);
        CutTrace(c, 1.45*s,
            new Point(o + 13.5*s, y + 19*s),
            new Point(o + 13.5*s, y + 89*s),
            new Point(o + 7*s, y + 95.5*s),
            new Point(o + 7*s, y + 175*s));
        CutDot(c, o + 7*s, y + 175*s, 2.7*s);
        // One additional drilled hole provides the open yellow field
        // seen between the strong lines in the source strip.
        CutDot(c, o + 11.5*s, y + 20*s, 2.4*s);

        // MIDDLE / a substantial tapered cut based on the printed
        // stepped vertical component rather than a thin electrical wire.
        y = h * .403;
        CutShape(c,
            new Point(o + 6*s, y),
            new Point(o + 14*s, y),
            new Point(o + 14*s, y + 51*s),
            new Point(o + 9*s, y + 56*s),
            new Point(o + 9*s, y + 92*s),
            new Point(o + 15*s, y + 98*s),
            new Point(o + 15*s, y + 122*s),
            new Point(o + 8*s, y + 129*s),
            new Point(o + 8*s, y + 162*s),
            new Point(o + 5*s, y + 162*s),
            new Point(o + 5*s, y + 117*s),
            new Point(o + 3*s, y + 111*s),
            new Point(o + 3*s, y + 52*s),
            new Point(o + 6*s, y + 49*s));
        // Hatch group directly supports the larger body.
        for (var n = 0; n < 6; n++)
            CutTrace(c, 1.35*s,
                new Point(o + 4*s, y + (134 + n*6)*s),
                new Point(o + 10*s, y + (140 + n*6)*s));
        CutDot(c, o + 11*s, y + 178*s, 3.15*s);

        // BOTTOM / isolated mechanical leg, then shrinking stencil slots.
        // The thin uncut margin around the cavities makes them genuinely
        // boxed into the chassis on both edges.
        y = h * .760;
        CutShape(c,
            new Point(o + 5*s, y),
            new Point(o + 13*s, y),
            new Point(o + 13*s, y + 45*s),
            new Point(o + 9*s, y + 49*s),
            new Point(o + 9*s, y + 75*s),
            new Point(o + 5*s, y + 75*s));
        CutDot(c, o + 10*s, y + 85*s, 3.0*s);
        var heights = new[] { 13.0, 10.0, 7.0, 4.0 };
        var py = y + 100*s;
        foreach (var length in heights)
        {
            CutRect(c, o + 6*s, py, 7.5*s, length*s);
            py += (length + 4.5)*s;
        }
    }

    private static void CarveRight(GeometryGroup c, double w, double h,
        double s, double o, double i)
    {
        var r = w - o;
        // UPPER / reference-like long pointed machinery leg with two
        // thickness shifts, a long empty interior, then an etched ladder.
        var y = h * .086;
        CutShape(c,
            new Point(r - 13*s, y),
            new Point(r - 5*s, y),
            new Point(r - 5*s, y + 54*s),
            new Point(r - 12*s, y + 61*s),
            new Point(r - 12*s, y + 99*s),
            new Point(r - 7*s, y + 104*s),
            new Point(r - 7*s, y + 136*s),
            new Point(r - 14*s, y + 143*s),
            new Point(r - 14*s, y + 166*s),
            new Point(r - 9*s, y + 166*s),
            new Point(r - 9*s, y + 184*s),
            new Point(r - 15*s, y + 184*s),
            new Point(r - 15*s, y + 133*s),
            new Point(r - 16*s, y + 129*s),
            new Point(r - 16*s, y + 59*s),
            new Point(r - 13*s, y + 56*s));
        for (var n = 0; n < 5; n++)
            CutTrace(c, 1.4*s,
                new Point(r - 15*s, y + (192 + n*6)*s),
                new Point(r - 9*s, y + (198 + n*6)*s));
        CutDot(c, r - 11*s, y + 229*s, 2.8*s);

        // MID / a pair of long parallel conductors. Their bends point in
        // opposite directions, but share the same reference-style system.
        y = h * .459;
        CutTrace(c, 1.45*s,
            new Point(r - 5*s, y),
            new Point(r - 5*s, y + 62*s),
            new Point(r - 12*s, y + 69*s),
            new Point(r - 12*s, y + 143*s));
        CutRing(c, r - 12*s, y + 143*s, 2.85*s, 1.35*s);
        CutTrace(c, 1.45*s,
            new Point(r - 14*s, y + 16*s),
            new Point(r - 14*s, y + 79*s),
            new Point(r - 8*s, y + 85*s),
            new Point(r - 8*s, y + 164*s));
        CutDot(c, r - 8*s, y + 164*s, 2.85*s);

        // LOW / large black negative and sparse boxed rectangle ladder.
        y = h * .766;
        CutShape(c,
            new Point(r - 14*s, y),
            new Point(r - 5*s, y),
            new Point(r - 5*s, y + 44*s),
            new Point(r - 11*s, y + 50*s),
            new Point(r - 11*s, y + 79*s),
            new Point(r - 15*s, y + 79*s));
        CutDot(c, r - 9*s, y + 89*s, 3.2*s);
        var heights = new[] { 14.0, 10.0, 6.8, 3.4 };
        var py = y + 101*s;
        foreach (var length in heights)
        {
            CutRect(c, r - 14*s, py, 8*s, length*s);
            py += (length + 4.0)*s;
        }
    }

    private static void CarveBottom(GeometryGroup c, double w, double h,
        double s, double o, double i)
    {
        var bottom = h - o;
        var inner = h - i;

        // SOUTH-WEST / a long, blunt black component with a sharp shoulder
        // and its own hatch field. This is the rotated spirit of reference 3.
        var x = w * .084;
        CutShape(c,
            new Point(x, bottom - 15*s),
            new Point(x + 43*s, bottom - 15*s),
            new Point(x + 47*s, bottom - 11*s),
            new Point(x + 105*s, bottom - 11*s),
            new Point(x + 108*s, bottom - 10*s),
            new Point(x + 167*s, bottom - 10*s),
            new Point(x + 167*s, bottom - 8*s),
            new Point(x + 108*s, bottom - 8*s),
            new Point(x + 105*s, bottom - 6*s),
            new Point(x + 47*s, bottom - 6*s),
            new Point(x + 43*s, bottom - 5*s),
            new Point(x, bottom - 5*s));
        for (var n = 0; n < 6; n++)
        {
            var ax = x + (180 + n*7)*s;
            CutTrace(c, 1.45*s,
                new Point(ax, inner + 5*s),
                new Point(ax + 6*s, inner + 11*s));
        }
        var widthSteps = new[] { 13.0, 10.0, 7.0, 4.0 };
        var px = x + 238*s;
        foreach (var widthStep in widthSteps)
        {
            CutRect(c, px, inner + 6*s, widthStep*s, 5.3*s);
            px += (widthStep + 4.0)*s;
        }

        // SOUTH-CENTRE / the clean twin-track print from the original
        // reference: long 45-degree kinked paths with spaced drilled ends.
        x = w * .418;
        CutTrace(c, 1.55*s,
            new Point(x, bottom - 4*s),
            new Point(x + 68*s, bottom - 4*s),
            new Point(x + 75*s, bottom - 11*s),
            new Point(x + 192*s, bottom - 11*s));
        CutRing(c, x + 192*s, bottom - 11*s, 2.8*s, 1.4*s);
        CutTrace(c, 1.45*s,
            new Point(x + 23*s, bottom - 13*s),
            new Point(x + 80*s, bottom - 13*s),
            new Point(x + 87*s, bottom - 6*s),
            new Point(x + 166*s, bottom - 6*s));
        CutDot(c, x + 166*s, bottom - 6*s, 2.75*s);
        CutDot(c, x + 39*s, bottom - 8*s, 2.35*s);

        // SOUTHEAST / broad slotted, chamfered black recess that steps
        // into a narrow tail, followed by a decreasing socket array.
        x = w * .760;
        CutShape(c,
            new Point(x, inner + 3*s),
            new Point(x + 49*s, inner + 3*s),
            new Point(x + 53*s, inner + 7*s),
            new Point(x + 105*s, inner + 7*s),
            new Point(x + 108*s, inner + 8*s),
            new Point(x + 150*s, inner + 8*s),
            new Point(x + 150*s, inner + 10*s),
            new Point(x + 108*s, inner + 10*s),
            new Point(x + 105*s, inner + 12*s),
            new Point(x + 53*s, inner + 12*s),
            new Point(x + 49*s, inner + 13*s),
            new Point(x, inner + 13*s));
        CutRect(c, x + 158*s, inner + 5*s, 20*s, 5*s);
        CutRect(c, x + 183*s, inner + 5*s, 10*s, 5*s);
        CutRect(c, x + 199*s, inner + 5*s, 5*s, 5*s);
        CutDot(c, x + 214*s, inner + 9*s, 2.9*s);
    }

    private Size cachedSize = Size.Empty;
    private double cachedScale = -1;
    private Geometry? cachedCarving;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = RenderSize.Width;
        var height = RenderSize.Height;
        if (width < 300 || height < 300) return;

        var scale = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut) /
                    ChamferGeometry.StandardCut;
        if (cachedCarving is null || cachedSize != RenderSize || cachedScale != scale)
        {
            var cut = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut);
            var band = CreateBand(width, height, scale, cut);
            var outer = 3.2 * scale;
            var inner = 19.0 * scale;
            var holes = new GeometryGroup { FillRule = FillRule.Nonzero };
            CarveTop(holes, width, scale, outer, inner);
            CarveLeft(holes, height, scale, outer, inner);
            CarveRight(holes, width, height, scale, outer, inner);
            CarveBottom(holes, width, height, scale, outer, inner);
            // True subtraction keeps holes transparent and clips even the
            // longest groove strictly between the two invisible guide rails.
            cachedCarving = Geometry.Combine(band, holes, GeometryCombineMode.Exclude, null);
            cachedCarving.Freeze();
            cachedSize = RenderSize;
            cachedScale = scale;
        }

        // One colored shape with carved-out empty channels: no central spine,
        // no symmetric modules attached to a line, no painted guide rails.
        dc.DrawGeometry(Stroke, null, cachedCarving);

        // Tiny static luminous highlights stay INSIDE the surviving material,
        // leaving the exterior black and the inner separator unwashed.
        dc.PushClip(cachedCarving);
        var soft = new SolidColorBrush(Color.FromArgb(67, 255, 255, 255));
        var glint = new Pen(soft, 3.0 * scale);
        var middleY = 12.7 * scale;
        dc.DrawLine(glint, new Point(width*.18, middleY),
            new Point(width*.225, middleY));
        dc.DrawLine(glint, new Point(width*.82, middleY),
            new Point(width*.895, middleY));
        dc.DrawLine(glint, new Point(12.7*scale, height*.47),
            new Point(12.7*scale, height*.51));
        dc.DrawLine(glint, new Point(width-12.7*scale, height*.63),
            new Point(width-12.7*scale, height*.69));
        dc.Pop();
    }
}

/// <summary>
/// The thin INNER content frame: same neon left/right colors but its TOP is
/// the lower GCO header division. V and the tabs are flush against that line.
/// </summary>
public sealed class ContentChassisOutline : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ContentChassisOutline),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpatialStrokeProperty =
        DependencyProperty.Register(nameof(SpatialStroke), typeof(bool), typeof(ContentChassisOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HeaderHeightProperty =
        DependencyProperty.Register(nameof(HeaderHeight), typeof(double), typeof(ContentChassisOutline),
            new FrameworkPropertyMetadata(68.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FrameInsetProperty =
        DependencyProperty.Register(nameof(FrameInset), typeof(double), typeof(ContentChassisOutline),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool SpatialStroke { get => (bool)GetValue(SpatialStrokeProperty); set => SetValue(SpatialStrokeProperty, value); }
    public double HeaderHeight { get => (double)GetValue(HeaderHeightProperty); set => SetValue(HeaderHeightProperty, value); }
    public double FrameInset { get => (double)GetValue(FrameInsetProperty); set => SetValue(FrameInsetProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width < 90 || RenderSize.Height < HeaderHeight + 50) return;
        var cut = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut);
        var stroke = SpatialStroke ? SpatialNeonField.Create(this, Stroke) : Stroke;
        dc.DrawGeometry(null, new Pen(stroke, 1.15) { LineJoin = PenLineJoin.Miter },
            ChamferGeometry.CreateContentFrame(RenderSize.Width, RenderSize.Height,
                HeaderHeight, FrameInset, cut));
    }
}
