using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

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

    // The INNER top is the one line BELOW the logo/header, NOT a parallel
    // duplicate of the outer top perimeter.
    public static Geometry CreateContentFrame(double width, double height,
        double headerTop, double inset, double cut)
    {
        var left = inset;
        var right = Math.Max(left, width - inset);
        var top = Math.Clamp(headerTop, inset, Math.Max(inset, height - inset));
        var bottom = Math.Max(top, height - inset);
        cut = Math.Clamp(cut, 0, Math.Min(right - left, bottom - top) / 3.0);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(left + cut, top), false, true);
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

    // Portrait shares the chassis LEFT corners only; no independent image
    // frame or diagonals on its right side.
    public static Geometry CreatePortraitClip(double width, double height, double cut)
    {
        cut = Math.Clamp(cut, 0, Math.Min(width, height) / 3.0);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(cut, 0), true, true);
            ctx.LineTo(new Point(width, 0), true, false);
            ctx.LineTo(new Point(width, height), true, false);
            ctx.LineTo(new Point(cut, height), true, false);
            ctx.LineTo(new Point(0, height - cut), true, false);
            ctx.LineTo(new Point(0, cut), true, false);
        }
        path.Freeze();
        return path;
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
    public static readonly DependencyProperty AccentCornersProperty =
        DependencyProperty.Register(nameof(AccentCorners), typeof(bool), typeof(ChamferOutline),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double CornerCut { get => (double)GetValue(CornerCutProperty); set => SetValue(CornerCutProperty, value); }
    public bool Glow { get => (bool)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public bool AccentCorners { get => (bool)GetValue(AccentCornersProperty); set => SetValue(AccentCornersProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width < 6 || RenderSize.Height < 6) return;
        var thickness = Math.Max(0.5, StrokeThickness);
        var inset = Math.Max(thickness / 2.0 + 0.25, 1.0);
        var cut = ChamferGeometry.ScaledCut(this, CornerCut);
        var shape = ChamferGeometry.Create(RenderSize.Width, RenderSize.Height, cut, inset);

        if (Glow)
        {
            var soft = Stroke.CloneCurrentValue();
            soft.Opacity = 0.12;
            var near = Stroke.CloneCurrentValue();
            near.Opacity = 0.24;
            dc.DrawGeometry(null, new Pen(soft, thickness + 12) { LineJoin = PenLineJoin.Miter }, shape);
            dc.DrawGeometry(null, new Pen(near, thickness + 6) { LineJoin = PenLineJoin.Miter }, shape);
        }
        dc.DrawGeometry(Fill, new Pen(Stroke, thickness) { LineJoin = PenLineJoin.Miter }, shape);

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
        e.Clip = ChamferGeometry.CreatePortraitClip(e.ActualWidth, e.ActualHeight, cut);
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
        var shift = h - 2 * inset; // equal delta-X/delta-Y => exact 45°.
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(inset + shift, inset), true, true);
            ctx.LineTo(new Point(w - inset, inset), true, false);
            ctx.LineTo(new Point(w - inset - shift, h - inset), true, false);
            ctx.LineTo(new Point(inset, h - inset), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), geometry);
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

    // All drawing is scaled from actual RenderSize. The step shoulders use
    // equal X/Y movements and stay true 45 degrees at any supported DPI.
    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // Browser-tab-like bulge in a HORIZONTAL border, not a parallel border.
    // inward = +1 at the top, -1 at the bottom.
    private static Geometry HorizontalStep(double x, double edgeY, double length,
        double depth, double inward, double scale)
    {
        var s = scale;
        var y = edgeY;
        return Polygon(
            new Point(x, y - inward * 2.2 * s),
            new Point(x + length, y - inward * 2.2 * s),
            new Point(x + length, y + inward * 1.5 * s),
            new Point(x + length - 6 * s, y + inward * 7.5 * s),
            new Point(x + length - 12 * s, y + inward * 7.5 * s),
            new Point(x + length - (12 * s + depth - 7.5 * s), y + inward * depth),
            new Point(x + 10 * s + depth - 3 * s, y + inward * depth),
            new Point(x + 10 * s, y + inward * 3 * s),
            new Point(x + 10 * s, y + inward * 1.5 * s),
            new Point(x, y + inward * 1.5 * s));
    }

    // One local mechanical ledge within the ORIGINAL side border channel.
    // The inner extent is intentionally < 5 DIPs: the content-frame black
    // separation is never replaced by a new full-length neon border.
    private static Geometry VerticalStep(double edgeX, double y, double inward, double s)
    {
        return Polygon(
            new Point(edgeX - inward * 5.0 * s, y),
            new Point(edgeX + inward * .8 * s, y),
            new Point(edgeX + inward * .8 * s, y + 8 * s),
            new Point(edgeX + inward * 4.25 * s, y + 11.45 * s),
            new Point(edgeX + inward * 4.25 * s, y + 55.55 * s),
            new Point(edgeX + inward * .8 * s, y + 59 * s),
            new Point(edgeX + inward * .8 * s, y + 70 * s),
            new Point(edgeX - inward * 5.0 * s, y + 70 * s));
    }

    // Cut short circuits as real BACKGROUND-BLACK negative space inside a
    // thickened section. Dots are holes, not extra color-coded outlines.
    private static void HorizontalCuts(DrawingContext dc, Brush black,
        double x, double edgeY, double length, double inward, double s, bool deep)
    {
        var slot = new Pen(black, 1.35 * s);
        if (deep)
        {
            for (var i = 0; i < 4; i++)
            {
                var px = x + (37 + 8 * i) * s;
                dc.DrawLine(slot,
                    new Point(px, edgeY + inward * 6 * s),
                    new Point(px + 4 * s, edgeY + inward * 10 * s));
            }
        }
        var center = new Point(x + length - 35 * s, edgeY + inward * (deep ? 8.2 : 5) * s);
        dc.DrawLine(new Pen(black, 1.65 * s),
            new Point(center.X - 17 * s, center.Y),
            new Point(center.X - 4 * s, center.Y));
        dc.DrawEllipse(black, null, center, 2.9 * s, 2.9 * s);
    }

    private static void VerticalCuts(DrawingContext dc, Brush black,
        double edgeX, double y, double inward, double s)
    {
        var pen = new Pen(black, 1.15 * s);
        for (var i = 0; i < 5; ++i)
        {
            var cy = y + (20 + 5.25 * i) * s;
            dc.DrawLine(pen,
                new Point(edgeX - inward * 2.7 * s, cy),
                new Point(edgeX + inward * 1.1 * s, cy + 3.8 * s));
        }
        var center = new Point(edgeX - inward * 1.1 * s, y + 48 * s);
        dc.DrawLine(new Pen(black, 1.3 * s),
            new Point(center.X, center.Y - 8 * s),
            new Point(center.X, center.Y - 3 * s));
        dc.DrawEllipse(black, null, center, 2.45 * s, 2.45 * s);
    }

    private static void Node(DrawingContext dc, Brush line, Brush black,
        double x, double y, double inward, double s)
    {
        var pen = new Pen(line, 1.25 * s) { LineJoin = PenLineJoin.Miter };
        var bend = new Point(x + inward * 2.7 * s, y + 2.7 * s);
        var end = new Point(bend.X, y + 21 * s);
        dc.DrawLine(pen, new Point(x, y), bend);
        dc.DrawLine(pen, bend, end);
        dc.DrawEllipse(black, new Pen(line, 1.4 * s), end, 2.6 * s, 2.6 * s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (w < 90 || h < 90) return;

        var s = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut) /
                ChamferGeometry.StandardCut;
        // Core perimeter is still ONE line. Thick parts are local silhouettes,
        // not parallel, full-length layers stacked around the content.
        var thickness = 3.0 * s;
        var inset = thickness / 2 + .45;
        var cut = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut);
        var edge = ChamferGeometry.Create(w, h, cut, inset);
        dc.DrawGeometry(null, new Pen(Stroke, thickness) { LineJoin = PenLineJoin.Miter }, edge);

        var left = inset;
        var right = w - inset;
        var top = inset;
        var bottom = h - inset;
        var yellow = new SolidColorBrush(Color.FromRgb(255, 240, 0));
        var cyan = new SolidColorBrush(Color.FromRgb(0, 226, 242));
        var black = new SolidColorBrush(Color.FromRgb(6, 13, 15));
        var warmGlow = new SolidColorBrush(Color.FromArgb(62, 255, 240, 0));
        var coolGlow = new SolidColorBrush(Color.FromArgb(62, 0, 226, 242));

        // Two top and two bottom browser-style steps. The lower ones are
        // deliberately shallower to protect the pinned footer/Continue area.
        var topLength = 152 * s;
        var bottomLength = 114 * s;
        var topGoldX = w * .24;
        var topCyanX = w * .72;
        var bottomGoldX = w * .30;
        var bottomCyanX = w * .76;
        var topGold = HorizontalStep(topGoldX, top, topLength, 13.0 * s, +1, s);
        var topCyan = HorizontalStep(topCyanX, top, topLength * .88, 13.0 * s, +1, s);
        var bottomGold = HorizontalStep(bottomGoldX, bottom, bottomLength, 8.5 * s, -1, s);
        var bottomCyan = HorizontalStep(bottomCyanX, bottom, bottomLength * 1.05, 8.5 * s, -1, s);

        // Targeted glow only around the tabs, without lighting the black
        // gap all along the rest of the chassis.
        dc.DrawGeometry(null, new Pen(warmGlow, 4.5 * s), topGold);
        dc.DrawGeometry(null, new Pen(coolGlow, 4.5 * s), topCyan);
        dc.DrawGeometry(null, new Pen(warmGlow, 3.0 * s), bottomGold);
        dc.DrawGeometry(null, new Pen(coolGlow, 3.0 * s), bottomCyan);
        dc.DrawGeometry(yellow, null, topGold);
        dc.DrawGeometry(cyan, null, topCyan);
        dc.DrawGeometry(yellow, null, bottomGold);
        dc.DrawGeometry(cyan, null, bottomCyan);

        HorizontalCuts(dc, black, topGoldX, top, topLength, +1, s, true);
        HorizontalCuts(dc, black, topCyanX, top, topLength * .88, +1, s, true);
        HorizontalCuts(dc, black, bottomGoldX, bottom, bottomLength, -1, s, false);
        HorizontalCuts(dc, black, bottomCyanX, bottom, bottomLength * 1.05, -1, s, false);

        // Stronger vertical tabs, still below the inner content frame's side
        // line and leaving a visible dark gutter between the two structures.
        var leftY = h * .40;
        var rightY = h * .56;
        var leftTab = VerticalStep(left, leftY, +1, s);
        var rightTab = VerticalStep(right, rightY, -1, s);
        dc.DrawGeometry(yellow, null, leftTab);
        dc.DrawGeometry(cyan, null, rightTab);
        VerticalCuts(dc, black, left, leftY, +1, s);
        VerticalCuts(dc, black, right, rightY, -1, s);

        // Discrete circuit-node branches; never another continuous rail.
        Node(dc, yellow, black, left, h * .18, +1, s);
        Node(dc, cyan, black, right, h * .28, -1, s);
        Node(dc, yellow, black, left, h * .77, +1, s);
        Node(dc, cyan, black, right, h * .80, -1, s);

        // A couple of hard-edged discontinuities retain the sketched
        // broken-tech rhythm without weakening the chamfered corners.
        var negativeBreak = new Pen(black, thickness + 1.6 * s);
        dc.DrawLine(negativeBreak,
            new Point(w * .54, top), new Point(w * .54 + 7 * s, top));
        dc.DrawLine(negativeBreak,
            new Point(w * .51, bottom), new Point(w * .51 + 8 * s, bottom));
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
    public static readonly DependencyProperty HeaderHeightProperty =
        DependencyProperty.Register(nameof(HeaderHeight), typeof(double), typeof(ContentChassisOutline),
            new FrameworkPropertyMetadata(68.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FrameInsetProperty =
        DependencyProperty.Register(nameof(FrameInset), typeof(double), typeof(ContentChassisOutline),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double HeaderHeight { get => (double)GetValue(HeaderHeightProperty); set => SetValue(HeaderHeightProperty, value); }
    public double FrameInset { get => (double)GetValue(FrameInsetProperty); set => SetValue(FrameInsetProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width < 90 || RenderSize.Height < HeaderHeight + 50) return;
        var cut = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut);
        dc.DrawGeometry(null, new Pen(Stroke, 1.15) { LineJoin = PenLineJoin.Miter },
            ChamferGeometry.CreateContentFrame(RenderSize.Width, RenderSize.Height,
                HeaderHeight, FrameInset, cut));
    }
}
