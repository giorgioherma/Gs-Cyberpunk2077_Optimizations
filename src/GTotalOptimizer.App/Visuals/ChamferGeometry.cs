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

    // Design the colored chassis by subtracting a FEW linked assemblies,
    // not dozens of independent glyphs. All coordinates are inside the same
    // invisible 3.2..19 DIP rails; 45-degree bends are always straight.
    private static void CarveTop(GeometryGroup c, double w, double s, double o, double i)
    {
        // ASSEMBLY 1 / WEST FAN: one shared carved feed, three uneven parallel
        // traces, each one angular bend and a drilled terminal. The fan reads
        // as a single PCB component rather than four unrelated marks.
        var x = w * .052;
        CutRect(c, x, o + 4*s, 3*s, 11*s);
        for (var k = 0; k < 3; k++)
        {
            var y0 = o + (5 + 4*k)*s;
            var y1 = o + (10 + (k == 0 ? 0 : k == 1 ? 3 : -3))*s;
            var bendX = x + (43 + 13*k)*s;
            var diagonal = Math.Abs(y1 - y0);
            var endX = x + (125 + 12*k)*s;
            CutTrace(c, 1.35*s,
                new Point(x + 3*s, y0),
                new Point(bendX, y0),
                new Point(bendX + diagonal, y1),
                new Point(endX, y1));
            CutDot(c, endX, y1, (k == 1 ? 2.25 : 2.55)*s);
        }
        // One shoulder bite belongs to the fan's feed, not to a separate glyph.
        CutShape(c, new Point(x - 6*s, o - 2*s),
            new Point(x + 7*s, o - 2*s), new Point(x + 7*s, o + 3*s),
            new Point(x + 2*s, o + 8*s), new Point(x - 6*s, o + 8*s));

        // ASSEMBLY 2 / WEST DRIVE: a long black power body, fat at the front,
        // narrowing to a hairline tail, followed by slashes and a descending
        // component ladder. All subordinate markings align with the same bus.
        x = w * .273;
        CutShape(c,
            new Point(x, o + 4*s), new Point(x + 26*s, o + 4*s),
            new Point(x + 35*s, o + 13*s), new Point(x + 74*s, o + 13*s),
            new Point(x + 83*s, o + 10*s), new Point(x + 115*s, o + 10*s),
            new Point(x + 115*s, o + 8.3*s), new Point(x + 81*s, o + 8.3*s),
            new Point(x + 72*s, o + 6*s), new Point(x + 34*s, o + 6*s),
            new Point(x + 28*s, o + 2*s), new Point(x, o + 2*s));
        // The slashes are packed together and have consistent inclination.
        for (var k = 0; k < 5; k++)
        {
            var sx = x + (94 + 7*k)*s;
            CutTrace(c, 1.45*s,
                new Point(sx, o + 5*s), new Point(sx + 7*s, o + 12*s));
        }
        var widths = new[] { 12.0, 9.3, 6.8, 4.0 };
        var lx = x + 139*s;
        for (var k = 0; k < widths.Length; k++)
        {
            CutRect(c, lx, o + 8.0*s, widths[k]*s, (5.2 - .6*k)*s);
            lx += (widths[k] + 4.1)*s;
        }

        // ASSEMBLY 3 / CENTER HOUSING: a long, wholly enclosed chamfered
        // window plus its narrow registration rail, then one pointed arrow.
        // Thin colored walls survive on BOTH sides of the main black cavity.
        x = w * .515;
        CutShape(c,
            new Point(x, o + 4*s), new Point(x + 49*s, o + 4*s),
            new Point(x + 55*s, o + 10*s), new Point(x + 55*s, i - 3*s),
            new Point(x + 7*s, i - 3*s), new Point(x, i - 10*s));
        CutTrace(c, 1.45*s,
            new Point(x + 66*s, o + 5*s),
            new Point(x + 105*s, o + 5*s),
            new Point(x + 114*s, o + 14*s),
            new Point(x + 150*s, o + 14*s));
        CutDot(c, x + 150*s, o + 14*s, 2.65*s);
        CutShape(c,
            new Point(x + 160*s, o + 6*s),
            new Point(x + 180*s, o + 6*s),
            new Point(x + 180*s, o + 3*s),
            new Point(x + 194*s, o + 10*s),
            new Point(x + 180*s, o + 17*s),
            new Point(x + 180*s, o + 14*s),
            new Point(x + 160*s, o + 14*s));

        // ASSEMBLY 4 / EAST TERMINAL: a black bus and a set of unequal
        // socket pads enclosed in one local system. A narrow color bridge
        // interrupts the bus by leaving uncut material between two segments.
        x = w * .788;
        CutTrace(c, 1.7*s,
            new Point(x, o + 12*s), new Point(x + 11*s, o + 12*s),
            new Point(x + 19*s, o + 4*s), new Point(x + 56*s, o + 4*s));
        CutTrace(c, 1.7*s,
            new Point(x + 62*s, o + 4*s),
            new Point(x + 90*s, o + 4*s),
            new Point(x + 100*s, o + 14*s),
            new Point(x + 122*s, o + 14*s));
        CutDot(c, x + 122*s, o + 14*s, 2.65*s);
        CutShape(c,
            new Point(x + 26*s, o + 8*s),
            new Point(x + 41*s, o + 8*s),
            new Point(x + 47*s, o + 14*s),
            new Point(x + 47*s, i - 2.5*s),
            new Point(x + 26*s, i - 2.5*s));
        for (var k = 0; k < 4; k++)
            CutRect(c, x + (132 + k*10)*s, o + 5*s,
                (8.0 - 1.6*k)*s, 5*s);
    }

    private static void CarveLeft(GeometryGroup c, double h, double s, double o, double i)
    {
        // ASSEMBLY 1 / UPPER FAN: a single horizontal feeder supplies three
        // descending conductors. Different bend positions and terminals keep
        // it grouped but not mechanically repetitive.
        var y = h * .092;
        CutRect(c, o + 4*s, y, 11*s, 3*s);
        for (var k = 0; k < 3; k++)
        {
            var x0 = o + (5 + 4*k)*s;
            var x1 = o + (10 + (k == 0 ? 0 : k == 1 ? 3 : -3))*s;
            var yBend = y + (29 + k*14)*s;
            var shift = Math.Abs(x1 - x0);
            var yEnd = y + (91 + k*11)*s;
            CutTrace(c, 1.3*s,
                new Point(x0, y + 3*s),
                new Point(x0, yBend),
                new Point(x1, yBend + shift),
                new Point(x1, yEnd));
            CutDot(c, x1, yEnd, (k==0 ? 2.7 : 2.3)*s);
        }

        // ASSEMBLY 2 / CENTRAL POWER BUS: a tall wide entry with stepped
        // narrowing tail, diagonal hatching and progressively shorter pads.
        y = h * .39;
        CutShape(c,
            new Point(o + 3*s, y), new Point(o + 12*s, y),
            new Point(o + 15*s, y + 3*s), new Point(o + 15*s, y + 33*s),
            new Point(o + 10*s, y + 38*s), new Point(o + 10*s, y + 77*s),
            new Point(o + 8*s, y + 86*s), new Point(o + 8*s, y + 111*s),
            new Point(o + 6*s, y + 111*s), new Point(o + 6*s, y + 79*s),
            new Point(o + 4*s, y + 70*s), new Point(o + 4*s, y + 6*s));
        for (var k = 0; k < 5; k++)
            CutTrace(c, 1.35*s,
                new Point(o + 4.5*s, y + (43 + k*7)*s),
                new Point(o + 10*s, y + (48.5 + k*7)*s));
        var tallPads = new[] { 12.0, 9.5, 7.0, 4.5 };
        var py = y + 122*s;
        foreach (var length in tallPads)
        {
            CutRect(c, o + 7*s, py, 6.0*s, length*s);
            py += (length + 4.5)*s;
        }

        // ASSEMBLY 3 / LOWER HOUSING: two enclosed cavities separated by
        // a small solid bridge, connected visually to one angular long run.
        y = h * .755;
        CutShape(c,
            new Point(o + 4*s, y), new Point(i - 3*s, y),
            new Point(i - 3*s, y + 43*s),
            new Point(o + 9*s, y + 50*s),
            new Point(o + 4*s, y + 50*s));
        CutRect(c, o + 5*s, y + 54*s, 8*s, 21*s);
        CutTrace(c, 1.8*s,
            new Point(o + 7*s, y + 83*s),
            new Point(o + 7*s, y + 103*s),
            new Point(o + 13*s, y + 109*s),
            new Point(o + 13*s, y + 141*s));
        CutDot(c, o + 13*s, y + 141*s, 2.8*s);
    }

    private static void CarveRight(GeometryGroup c, double w, double h,
        double s, double o, double i)
    {
        var r = w - o;
        // ASSEMBLY 1 / UPPER ARMATURE: a chamfered enclosed slot begins
        // broad, narrows into a long black trace, then enters a shrinking
        // ladder (a single descending technological silhouette).
        var y = h * .077;
        CutShape(c,
            new Point(r - 14*s, y), new Point(r - 5*s, y),
            new Point(r - 5*s, y + 41*s),
            new Point(r - 11*s, y + 47*s),
            new Point(r - 11*s, y + 91*s),
            new Point(r - 13*s, y + 91*s),
            new Point(r - 13*s, y + 47*s),
            new Point(r - 15*s, y + 45*s));
        for (var k = 0; k < 6; k++)
            CutTrace(c, 1.25*s,
                new Point(r - 15*s, y + (103 + 7*k)*s),
                new Point(r - 10*s, y + (108 + 7*k)*s));
        var decreasing = new[] { 12.0, 10.0, 7.5, 5.5 };
        var stepY = y + 157*s;
        foreach (var len in decreasing)
        {
            CutRect(c, r - 13.5*s, stepY, 7*s, len*s);
            stepY += (len + 3.9)*s;
        }

        // ASSEMBLY 2 / CENTRE FAN: shared vertical feed at the outer face
        // supplies three angular terminal tracks; a big arrow-point cavity
        // is part of the SAME assembly instead of a detached mark.
        y = h * .393;
        CutRect(c, r - 15*s, y, 11*s, 3.2*s);
        for (var k = 0; k < 3; k++)
        {
            var x0 = r - (5 + 4*k)*s;
            var x1 = r - (10 + (k == 0 ? 0 : k == 1 ? 3 : -3))*s;
            var bendY = y + (31 + k*15)*s;
            var bendWidth = Math.Abs(x1 - x0);
            var yEnd = y + (97 + 11*k)*s;
            CutTrace(c, 1.3*s,
                new Point(x0, y + 3.2*s),
                new Point(x0, bendY),
                new Point(x1, bendY + bendWidth),
                new Point(x1, yEnd));
            CutDot(c, x1, yEnd, (k==0 ? 2.7 : 2.35)*s);
        }
        CutShape(c,
            new Point(r - 15*s, y + 145*s),
            new Point(r - 5*s, y + 145*s),
            new Point(r - 5*s, y + 156*s),
            new Point(r - 3*s, y + 156*s),
            new Point(r - 10*s, y + 170*s),
            new Point(r - 17*s, y + 156*s),
            new Point(r - 15*s, y + 156*s));

        // ASSEMBLY 3 / LOWER NETWORK: two enclosed notched cavities
        // separated by a thin intact cyan bridge, followed by a long
        // angular trace. Unlike the left wall, this assembly is more open.
        y = h * .777;
        CutShape(c,
            new Point(r - 14*s, y), new Point(r - 5*s, y),
            new Point(r - 5*s, y + 39*s),
            new Point(r - 12*s, y + 46*s),
            new Point(r - 14*s, y + 46*s));
        CutRect(c, r - 14*s, y + 51*s, 8.5*s, 22*s);
        CutTrace(c, 1.75*s,
            new Point(r - 9*s, y + 82*s),
            new Point(r - 9*s, y + 102*s),
            new Point(r - 14*s, y + 107*s),
            new Point(r - 14*s, y + 125*s));
        CutDot(c, r - 14*s, y + 125*s, 2.6*s);
    }

    private static void CarveBottom(GeometryGroup c, double w, double h,
        double s, double o, double i)
    {
        var bot = h - o;
        var inner = h - i;

        // ASSEMBLY 1 / SOUTHWEST: common feed and three bent terminals.
        var x = w * .057;
        CutRect(c, x, inner + 3*s, 3.0*s, 11*s);
        for (var k = 0; k < 3; k++)
        {
            var y0 = bot - (5 + 4*k)*s;
            var y1 = bot - (10 + (k==0 ? 0 : k==1 ? 3 : -3))*s;
            var bendX = x + (37 + 14*k)*s;
            var turn = Math.Abs(y1 - y0);
            var xEnd = x + (130 + 9*k)*s;
            CutTrace(c, 1.35*s,
                new Point(x + 3*s, y0),
                new Point(bendX, y0),
                new Point(bendX + turn, y1),
                new Point(xEnd, y1));
            CutDot(c, xEnd, y1, (k==1 ? 2.3 : 2.65)*s);
        }

        // ASSEMBLY 2 / WESTERN HOUSING: fully boxed cavity, wide black
        // bevel and smaller connected pads that taper toward the center.
        x = w * .277;
        CutShape(c,
            new Point(x, inner + 3.5*s),
            new Point(x + 63*s, inner + 3.5*s),
            new Point(x + 71*s, inner + 11.5*s),
            new Point(x + 71*s, bot - 3.0*s),
            new Point(x + 7*s, bot - 3.0*s),
            new Point(x, bot - 10*s));
        CutTrace(c, 2.0*s,
            new Point(x + 79*s, bot - 5*s),
            new Point(x + 92*s, bot - 18*s),
            new Point(x + 125*s, bot - 18*s),
            new Point(x + 132*s, bot - 11*s));
        for (var k = 0; k < 5; k++)
            CutTrace(c, 1.45*s,
                new Point(x + (139 + 7*k)*s, inner + 4*s),
                new Point(x + (145 + 7*k)*s, inner + 10*s));
        var pads = new[] { 13.0, 9.5, 7.0, 4.0 };
        var px = x + 181*s;
        foreach (var len in pads)
        {
            CutRect(c, px, inner + 8*s, len*s, 5.0*s);
            px += (len + 4.4)*s;
        }

        // ASSEMBLY 3 / SOUTHEAST DRIVE: a broad tapered leading cut,
        // two parallel angular followers and an interior arrow.
        x = w * .566;
        CutShape(c,
            new Point(x, bot - 4*s), new Point(x + 31*s, bot - 4*s),
            new Point(x + 41*s, bot - 14*s),
            new Point(x + 93*s, bot - 14*s),
            new Point(x + 101*s, bot - 10*s),
            new Point(x + 139*s, bot - 10*s),
            new Point(x + 139*s, bot - 8*s),
            new Point(x + 98*s, bot - 8*s),
            new Point(x + 90*s, bot - 6*s),
            new Point(x + 38*s, bot - 6*s),
            new Point(x + 29*s, bot + 3*s),
            new Point(x, bot + 3*s));
        for (var k = 0; k < 2; k++)
        {
            var yy = inner + (4.5 + 3.8*k)*s;
            CutTrace(c, 1.25*s,
                new Point(x + (144 + k*10)*s, yy),
                new Point(x + (173 + k*10)*s, yy),
                new Point(x + (179 + k*10)*s, yy + 6*s));
            CutDot(c, x + (179 + k*10)*s, yy + 6*s, 2.15*s);
        }

        // ASSEMBLY 4 / EAST ARROW & LADDER: large connected arrow recess,
        // followed by diminishing black connector pads and a small socket.
        x = w * .828;
        CutShape(c,
            new Point(x, inner + 4*s),
            new Point(x + 25*s, inner + 4*s),
            new Point(x + 25*s, inner + 2*s),
            new Point(x + 43*s, inner + 11*s),
            new Point(x + 25*s, inner + 20*s),
            new Point(x + 25*s, inner + 15*s),
            new Point(x, inner + 15*s));
        for (var k = 0; k < 5; k++)
            CutRect(c, x + (56 + 12*k)*s, inner + 5*s,
                (10.5 - 1.5*k)*s, (7.5 - .6*k)*s);
        CutTrace(c, 1.5*s,
            new Point(x + 122*s, bot - 5*s),
            new Point(x + 143*s, bot - 5*s),
            new Point(x + 150*s, bot - 12*s));
        CutDot(c, x + 150*s, bot - 12*s, 2.6*s);
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
