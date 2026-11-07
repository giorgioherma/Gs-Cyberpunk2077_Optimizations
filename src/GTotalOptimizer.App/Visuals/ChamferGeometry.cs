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

    // The heavy parts of the chassis are authored shapes, NOT repeated
    // generic tabs. Their positions scale against the actual window size.
    // The shared base perimeter and its chamfers are still computed once.
    private static Geometry Plate(params Point[] points)
    {
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            context.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++)
                context.LineTo(points[i], true, false);
        }
        path.Freeze();
        return path;
    }

    private static void Rail(DrawingContext dc, Brush brush, double weight, params Point[] points)
    {
        var pen = new Pen(brush, weight) { LineJoin = PenLineJoin.Miter };
        for (var i = 1; i < points.Length; ++i)
            dc.DrawLine(pen, points[i - 1], points[i]);
    }

    private static void Hole(DrawingContext dc, Brush negative, double x, double y, double radius)
    {
        dc.DrawEllipse(negative, null, new Point(x, y), radius, radius);
    }

    private static void Terminal(DrawingContext dc, Brush color, Brush negative, double x, double y,
        double radius = 2.3)
    {
        dc.DrawEllipse(negative, new Pen(color, 1.35), new Point(x, y), radius, radius);
    }

    private static void FillPanel(DrawingContext dc, Brush fill, Brush glow,
        bool emphasize, params Point[] points)
    {
        var panel = Plate(points);
        if (emphasize)
            dc.DrawGeometry(null, new Pen(glow, 4.0), panel);
        dc.DrawGeometry(fill, null, panel);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        if (w < 230 || h < 260) return;

        var scale = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut) /
                    ChamferGeometry.StandardCut;
        var cut = ChamferGeometry.ScaledCut(this, ChamferGeometry.StandardCut);
        // ONE continuous stroke beneath the locally carved structural plates.
        // Its stroke is not surrounded by a second parallel frame.
        var half = 2.2 * scale;
        var e = half + .35;
        dc.DrawGeometry(null,
            new Pen(Stroke, 4.4 * scale) { LineJoin = PenLineJoin.Miter },
            ChamferGeometry.Create(w, h, cut, e));

        var l = e;
        var r = w - e;
        var t = e;
        var b = h - e;
        var s = scale;
        var yellow = new SolidColorBrush(Color.FromRgb(255, 240, 0));
        var cyan = new SolidColorBrush(Color.FromRgb(0, 226, 242));
        var black = new SolidColorBrush(Color.FromRgb(6, 13, 15));
        var warm = new SolidColorBrush(Color.FromArgb(56, 255, 240, 0));
        var cool = new SolidColorBrush(Color.FromArgb(56, 0, 226, 242));

        // TOP WEST: stepped overhang with two circuit-track branches,
        // short engraved ladder and a large black terminal.
        var tx = w * .145;
        FillPanel(dc, yellow, warm, true,
            new Point(tx, t - 2*s), new Point(tx + 158*s, t - 2*s),
            new Point(tx + 158*s, t + 3*s), new Point(tx + 151*s, t + 10*s),
            new Point(tx + 112*s, t + 10*s), new Point(tx + 104*s, t + 18*s),
            new Point(tx + 24*s, t + 18*s), new Point(tx + 14*s, t + 8*s),
            new Point(tx + 14*s, t + 3*s), new Point(tx, t + 3*s));
        Hole(dc, black, tx + 131*s, t + 5*s, 3.8*s);
        Rail(dc, black, 2.0*s, new Point(tx + 96*s, t + 7*s),
            new Point(tx + 111*s, t + 7*s), new Point(tx + 116*s, t + 12*s));
        for (var i = 0; i < 6; ++i)
            Rail(dc, black, 1.3*s,
                new Point(tx + (30 + i*8)*s, t + 5*s),
                new Point(tx + (35 + i*8)*s, t + 10*s));
        Terminal(dc, yellow, black, tx + 172*s, t + 13*s, 2.4*s);
        Rail(dc, yellow, 1.25*s,
            new Point(tx + 155*s, t + 5*s),
            new Point(tx + 164*s, t + 5*s),
            new Point(tx + 172*s, t + 13*s));

        // TOP EAST: a separate angular circuit bus with hollow dots,
        // a black split channel, and unequal line engravings.
        var ex = w * .695;
        FillPanel(dc, cyan, cool, true,
            new Point(ex, t - 2*s), new Point(ex + 165*s, t - 2*s),
            new Point(ex + 165*s, t + 3*s), new Point(ex + 146*s, t + 3*s),
            new Point(ex + 137*s, t + 12*s), new Point(ex + 76*s, t + 12*s),
            new Point(ex + 68*s, t + 20*s), new Point(ex + 33*s, t + 20*s),
            new Point(ex + 25*s, t + 12*s), new Point(ex + 17*s, t + 12*s),
            new Point(ex + 9*s, t + 4*s), new Point(ex, t + 4*s));
        Rail(dc, black, 2.2*s,
            new Point(ex + 43*s, t + 8*s), new Point(ex + 78*s, t + 8*s),
            new Point(ex + 84*s, t + 14*s));
        Hole(dc, black, ex + 100*s, t + 7*s, 3.8*s);
        for (var i = 0; i < 5; ++i)
            Rail(dc, black, 1.5*s,
                new Point(ex + (18 + 8*i)*s, t + 5*s),
                new Point(ex + (21 + 8*i)*s, t + 8*s));
        Rail(dc, cyan, 1.3*s,
            new Point(ex + 150*s, t + 5*s),
            new Point(ex + 155*s, t + 10*s),
            new Point(ex + 155*s, t + 17*s));
        Terminal(dc, cyan, black, ex + 155*s, t + 17*s);

        // TOP CENTER: fragmented connection with deliberately varied pitch,
        // not another painted thick bar.
        var mx = w * .51;
        Rail(dc, yellow, 1.35*s,
            new Point(mx - 34*s, t + 2*s),
            new Point(mx - 23*s, t + 13*s),
            new Point(mx - 7*s, t + 13*s));
        Terminal(dc, yellow, black, mx - 7*s, t + 13*s, 2*s);
        Rail(dc, cyan, 1.35*s,
            new Point(mx + 23*s, t + 2*s),
            new Point(mx + 34*s, t + 13*s),
            new Point(mx + 47*s, t + 13*s));
        Hole(dc, black, mx + 47*s, t + 13*s, 2.7*s);

        // LEFT UPPER: fine etched conductors transition to a heavy
        // shoulder; dot sizes and track lengths deliberately differ.
        var ya = h * .19;
        FillPanel(dc, yellow, warm, false,
            new Point(l - 2.7*s, ya),
            new Point(l + 4*s, ya),
            new Point(l + 4*s, ya + 14*s),
            new Point(l + 11*s, ya + 21*s),
            new Point(l + 11*s, ya + 66*s),
            new Point(l + 5*s, ya + 72*s),
            new Point(l + 5*s, ya + 91*s),
            new Point(l - 2.7*s, ya + 91*s));
        Rail(dc, black, 2.3*s,
            new Point(l + 7*s, ya + 28*s),
            new Point(l + 7*s, ya + 61*s));
        for (var i = 0; i < 4; ++i)
            Rail(dc, black, 1.5*s,
                new Point(l + 2*s, ya + (17 + 8*i)*s),
                new Point(l + 6*s, ya + (21 + 8*i)*s));
        Hole(dc, black, l + 5*s, ya + 78*s, 2.7*s);
        Rail(dc, yellow, 1.3*s,
            new Point(l + 4*s, ya + 90*s),
            new Point(l + 11*s, ya + 97*s),
            new Point(l + 11*s, ya + 114*s));
        Terminal(dc, yellow, black, l + 11*s, ya + 114*s);

        // LEFT MIDDLE: broad mechanical shoulder with an asymmetric black
        // lightning-channel, diagonal hatching and stacked negative pads.
        var yl = h * .43;
        FillPanel(dc, yellow, warm, true,
            new Point(l - 2.7*s, yl),
            new Point(l + 4*s, yl),
            new Point(l + 4*s, yl + 10*s),
            new Point(l + 15*s, yl + 21*s),
            new Point(l + 15*s, yl + 72*s),
            new Point(l + 8*s, yl + 79*s),
            new Point(l + 8*s, yl + 101*s),
            new Point(l - 2.7*s, yl + 101*s));
        dc.DrawGeometry(black, null, Plate(
            new Point(l + 3*s, yl + 18*s),
            new Point(l + 8*s, yl + 23*s),
            new Point(l + 8*s, yl + 63*s),
            new Point(l + 11*s, yl + 66*s),
            new Point(l + 11*s, yl + 78*s),
            new Point(l + 6*s, yl + 73*s),
            new Point(l + 6*s, yl + 26*s),
            new Point(l + 3*s, yl + 23*s)));
        for (var i = 0; i < 6; ++i)
            Rail(dc, black, 1.25*s,
                new Point(l + 10*s, yl + (31 + i*6)*s),
                new Point(l + 14*s, yl + (35 + i*6)*s));
        Hole(dc, black, l + 4*s, yl + 88*s, 3.3*s);

        // LEFT LOWER: a PCB-style bus and black segmented ladder with
        // staggered round nodes, not a copy of the top motif.
        var yl2 = h * .705;
        FillPanel(dc, yellow, warm, false,
            new Point(l - 2*s, yl2), new Point(l + 7*s, yl2),
            new Point(l + 7*s, yl2 + 11*s),
            new Point(l + 13*s, yl2 + 17*s),
            new Point(l + 13*s, yl2 + 80*s),
            new Point(l + 6*s, yl2 + 87*s),
            new Point(l + 6*s, yl2 + 107*s),
            new Point(l - 2*s, yl2 + 107*s));
        for (var i = 0; i < 7; ++i)
            dc.DrawRectangle(black, null,
                new Rect(l + 6*s, yl2 + (24 + i*7)*s, 5.2*s, 3.7*s));
        Hole(dc, black, l + 4*s, yl2 + 10*s, 3.1*s);
        Rail(dc, yellow, 1.3*s,
            new Point(l + 8*s, yl2 + 90*s),
            new Point(l + 13*s, yl2 + 95*s),
            new Point(l + 13*s, yl2 + 111*s));
        Terminal(dc, yellow, black, l + 13*s, yl2 + 111*s, 2.6*s);

        // RIGHT UPPER: angular bus and paired hollow circuit terminals.
        var yr = h * .205;
        FillPanel(dc, cyan, cool, false,
            new Point(r + 2.7*s, yr),
            new Point(r - 4*s, yr),
            new Point(r - 4*s, yr + 14*s),
            new Point(r - 12*s, yr + 22*s),
            new Point(r - 12*s, yr + 83*s),
            new Point(r - 6*s, yr + 89*s),
            new Point(r - 6*s, yr + 105*s),
            new Point(r + 2.7*s, yr + 105*s));
        Rail(dc, black, 2.4*s,
            new Point(r - 8*s, yr + 30*s),
            new Point(r - 8*s, yr + 69*s),
            new Point(r - 5*s, yr + 72*s));
        for (var i = 0; i < 5; ++i)
            Rail(dc, black, 1.3*s,
                new Point(r - 3*s, yr + (33 + i*8)*s),
                new Point(r - 6.6*s, yr + (36.6 + i*8)*s));
        Rail(dc, cyan, 1.2*s,
            new Point(r - 6*s, yr + 87*s),
            new Point(r - 13*s, yr + 94*s),
            new Point(r - 13*s, yr + 112*s));
        Terminal(dc, cyan, black, r - 13*s, yr + 112*s);

        // RIGHT CENTER: a tall, hard-edged flanged spine, a large carved
        // chevron and dotted broken conductor. Unlike the left it tapers.
        var yr2 = h * .47;
        FillPanel(dc, cyan, cool, true,
            new Point(r + 2.7*s, yr2),
            new Point(r - 7*s, yr2),
            new Point(r - 7*s, yr2 + 16*s),
            new Point(r - 15*s, yr2 + 24*s),
            new Point(r - 15*s, yr2 + 65*s),
            new Point(r - 9*s, yr2 + 71*s),
            new Point(r - 9*s, yr2 + 91*s),
            new Point(r + 2.7*s, yr2 + 91*s));
        dc.DrawGeometry(black, null, Plate(
            new Point(r - 4*s, yr2 + 22*s),
            new Point(r - 11*s, yr2 + 29*s),
            new Point(r - 11*s, yr2 + 49*s),
            new Point(r - 7*s, yr2 + 53*s),
            new Point(r - 7*s, yr2 + 69*s),
            new Point(r - 3*s, yr2 + 65*s),
            new Point(r - 3*s, yr2 + 26*s)));
        for (var i = 0; i < 5; ++i)
            Hole(dc, black, r - 12*s, yr2 + (36 + 6*i)*s, 1.15*s);
        Hole(dc, black, r - 5*s, yr2 + 80*s, 3.5*s);

        // RIGHT LOWER: diagonal stencil and sparse black perforations.
        var yr3 = h * .72;
        FillPanel(dc, cyan, cool, false,
            new Point(r + 2*s, yr3),
            new Point(r - 3*s, yr3),
            new Point(r - 3*s, yr3 + 10*s),
            new Point(r - 12*s, yr3 + 19*s),
            new Point(r - 12*s, yr3 + 67*s),
            new Point(r - 5*s, yr3 + 74*s),
            new Point(r - 5*s, yr3 + 95*s),
            new Point(r + 2*s, yr3 + 95*s));
        for (var i = 0; i < 7; ++i)
            Rail(dc, black, 1.4*s,
                new Point(r - 11*s, yr3 + (23 + i*5.3)*s),
                new Point(r - 6*s, yr3 + (28 + i*5.3)*s));
        for (var i = 0; i < 3; ++i)
            Hole(dc, black, r - 7*s, yr3 + (69 + i*8)*s, 2.0*s);

        // BOTTOM WEST: wider black diagonal fissure and incomplete box cuts.
        var bx = w * .17;
        FillPanel(dc, yellow, warm, true,
            new Point(bx, b + 2*s), new Point(bx + 169*s, b + 2*s),
            new Point(bx + 169*s, b - 4*s), new Point(bx + 143*s, b - 4*s),
            new Point(bx + 133*s, b - 14*s), new Point(bx + 71*s, b - 14*s),
            new Point(bx + 63*s, b - 22*s), new Point(bx + 25*s, b - 22*s),
            new Point(bx + 15*s, b - 12*s), new Point(bx + 15*s, b - 4*s),
            new Point(bx, b - 4*s));
        dc.DrawGeometry(black, null, Plate(
            new Point(bx + 32*s, b - 6*s),
            new Point(bx + 43*s, b - 17*s),
            new Point(bx + 57*s, b - 17*s),
            new Point(bx + 46*s, b - 6*s)));
        for (var i = 0; i < 6; ++i)
            Rail(dc, black, 1.2*s,
                new Point(bx + (70 + 8*i)*s, b - 5*s),
                new Point(bx + (74 + 8*i)*s, b - 9*s));
        Hole(dc, black, bx + 145*s, b - 6*s, 3.4*s);

        // BOTTOM CENTER: two split traces with different endings.
        var bc = w * .51;
        Rail(dc, cyan, 1.5*s,
            new Point(bc + 9*s, b), new Point(bc + 19*s, b - 10*s),
            new Point(bc + 38*s, b - 10*s));
        Terminal(dc, cyan, black, bc + 38*s, b - 10*s, 2.8*s);
        Rail(dc, yellow, 1.5*s,
            new Point(bc - 44*s, b), new Point(bc - 36*s, b - 8*s),
            new Point(bc - 23*s, b - 8*s));
        Hole(dc, black, bc - 23*s, b - 8*s, 2.3*s);

        // BOTTOM EAST: three-layer stepped plate with a ladder that turns
        // into a routed trace, finished by a small terminal ring.
        var bx2 = w * .745;
        FillPanel(dc, cyan, cool, true,
            new Point(bx2, b + 2*s), new Point(bx2 + 192*s, b + 2*s),
            new Point(bx2 + 192*s, b - 4*s),
            new Point(bx2 + 170*s, b - 4*s),
            new Point(bx2 + 158*s, b - 16*s),
            new Point(bx2 + 92*s, b - 16*s),
            new Point(bx2 + 84*s, b - 24*s),
            new Point(bx2 + 38*s, b - 24*s),
            new Point(bx2 + 27*s, b - 13*s),
            new Point(bx2 + 27*s, b - 4*s),
            new Point(bx2, b - 4*s));
        for (var i = 0; i < 7; ++i)
            dc.DrawRectangle(black, null,
                new Rect(bx2 + (49 + 8*i)*s, b - 13*s, 4.4*s, 3.3*s));
        Rail(dc, black, 2.0*s,
            new Point(bx2 + 119*s, b - 5*s),
            new Point(bx2 + 140*s, b - 5*s),
            new Point(bx2 + 149*s, b - 14*s));
        Hole(dc, black, bx2 + 171*s, b - 6*s, 3.6*s);
        Terminal(dc, cyan, black, bx2 + 205*s, b - 16*s, 2.4*s);
        Rail(dc, cyan, 1.25*s,
            new Point(bx2 + 189*s, b - 3*s),
            new Point(bx2 + 192*s, b - 6*s),
            new Point(bx2 + 192*s, b - 12*s),
            new Point(bx2 + 196*s, b - 16*s),
            new Point(bx2 + 205*s, b - 16*s));

        // Two very small interruptions stop the baseline feeling like a
        // monotonous neon rectangle while respecting the 45-degree corners.
        var gap = new Pen(black, 5.3*s);
        dc.DrawLine(gap, new Point(w*.45, t), new Point(w*.45 + 9*s, t));
        dc.DrawLine(gap, new Point(w*.64, b), new Point(w*.64 + 11*s, b));
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
