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

    // Top is composed as several connected but non-repeating circuit regions.
    // Every bright-to-dark change is a real subtraction from the colored band.
    private static void CarveTop(GeometryGroup c, double w, double s, double o, double i)
    {
        double y = (o + i) / 2;
        // Left shoulder: unequal ladder, looping trace, full round negative pad.
        CutTrace(c, 2.1*s, new Point(33*s, y-4*s), new Point(45*s, y+8*s),
            new Point(88*s, y+8*s), new Point(98*s, y-2*s));
        CutDot(c, 88*s, y+8*s, 3.2*s);
        for (var k=0; k<6; k++)
            CutTrace(c, 1.35*s, new Point((110+6.8*k)*s,o+5*s),
                new Point((114+6.8*k)*s,o+9*s));
        CutRect(c, 159*s, o+5*s, 20*s, 3*s);
        CutShape(c, new Point(191*s,o-3*s),new Point(214*s,o-3*s),
            new Point(204*s,o+7*s),new Point(191*s,o+7*s));
        CutTrace(c, 2.1*s, new Point(223*s,o+14*s),new Point(235*s,o+14*s),
            new Point(244*s,o+5*s),new Point(261*s,o+5*s));
        CutDot(c, 261*s,o+5*s,3.0*s);

        // Western bus: broken pads, stair-stepped route and a big negative hole.
        var a=w*.245;
        CutShape(c, new Point(a-12*s,i+3*s),new Point(a+17*s,i+3*s),
            new Point(a+17*s,i-4*s),new Point(a+7*s,i-14*s),
            new Point(a-4*s,i-14*s),new Point(a-12*s,i-6*s));
        CutDot(c,a+37*s,y+3*s,4.0*s);
        CutTrace(c,2.4*s,new Point(a+48*s,o+6*s),new Point(a+61*s,o+6*s),
            new Point(a+71*s,o+16*s),new Point(a+93*s,o+16*s));
        CutDot(c,a+93*s,o+16*s,3.1*s);
        for(var k=0;k<5;k++) CutRect(c,a+(106+7*k)*s,o+5*s,3.6*s,8.7*s);
        CutTrace(c,1.3*s,new Point(a+156*s,i+3*s),new Point(a+156*s,i-7*s),
            new Point(a+166*s,i-17*s),new Point(a+176*s,i-17*s));

        // Offset center: unequal diagonal etchings, a segmented black channel.
        var m=w*.51;
        for(var k=0;k<5;k++)
            CutTrace(c,1.35*s,new Point(m-87*s+(k*9*s),o+5*s),
                new Point(m-81*s+(k*9*s),o+11*s));
        // Two long angular legs with a 5-DIP colored bridge preserved
        // between them. Flat plateaus and 45-degree bends only.
        CutTrace(c,2.25*s,new Point(m-47*s,o+5*s),
            new Point(m-37*s,o+15*s),new Point(m-17*s,o+15*s));
        CutTrace(c,2.25*s,new Point(m-12*s,o+15*s),
            new Point(m+29*s,o+15*s),new Point(m+39*s,o+5*s));
        CutDot(c,m+39*s,o+5*s,2.8*s);
        CutRect(c,m+44*s,o+7*s,7*s,3*s);
        CutRect(c,m+58*s,o+7*s,3*s,3*s);
        CutRect(c,m+68*s,o+7*s,12*s,3*s);
        CutShape(c,new Point(m+93*s,o-3*s),new Point(m+113*s,o-3*s),
            new Point(m+101*s,o+9*s),new Point(m+93*s,o+9*s));

        // Eastern branch: a different topology; asymmetric terminal routing.
        var e=w*.76;
        CutTrace(c,2.4*s,new Point(e-107*s,i+4*s),new Point(e-107*s,i-5*s),
            new Point(e-96*s,i-16*s),new Point(e-67*s,i-16*s));
        CutDot(c,e-67*s,i-16*s,3.4*s);
        for(var k=0;k<6;k++)
            CutTrace(c,1.5*s,new Point(e-49*s+k*8*s,o+6*s),
                new Point(e-44*s+k*8*s,o+11*s));
        CutShape(c,new Point(e+6*s,i+3*s),new Point(e+40*s,i+3*s),
            new Point(e+40*s,i-5*s),new Point(e+30*s,i-15*s),
            new Point(e+16*s,i-15*s),new Point(e+6*s,i-5*s));
        CutRect(c,e+48*s,o+6*s,14*s,3.5*s);
        CutDot(c,e+75*s,y,4.1*s);
        CutTrace(c,2*s,new Point(e+92*s,o+14*s),new Point(e+111*s,o+14*s),
            new Point(e+119*s,o+6*s),new Point(e+136*s,o+6*s));
        CutDot(c,e+136*s,o+6*s,2.7*s);

        // Short near-corner finish rather than a repeated mirrored module.
        CutTrace(c,1.3*s,new Point(w-84*s,o+5*s),new Point(w-76*s,o+13*s),
            new Point(w-63*s,o+13*s));
        for(var k=0;k<3;k++)
            CutRect(c,w-(55-7*k)*s,o+6*s,3*s,5*s);

        // Dense but nonuniform inlay in former empty top sections.
        // These windows are boxed into the band from BOTH sides.
        var box = w*.405;
        CutShape(c,new Point(box,o+5*s),new Point(box+29*s,o+5*s),
            new Point(box+36*s,o+12*s),new Point(box+36*s,i-3*s),
            new Point(box+6*s,i-3*s),new Point(box,o+11*s));
        CutDot(c,box+44*s,o+9*s,2.25*s);

        // A noticeably longer /----\ trace. The tiny uncut interval is a
        // deliberate colored bridge, not a painted-over corrective patch.
        var run = w*.609;
        CutTrace(c,1.85*s,new Point(run,o+5*s),
            new Point(run+8*s,o+13*s),new Point(run+38*s,o+13*s));
        CutTrace(c,1.85*s,new Point(run+43*s,o+13*s),
            new Point(run+63*s,o+13*s),new Point(run+71*s,o+5*s));
        CutDot(c,run+71*s,o+5*s,2.9*s);
        CutRect(c,run+25*s,o+5*s,8*s,2.4*s);

        // One long, fully enclosed variable-height slot; an exterior edge
        // notch nearby provides a different silhouette, not another slot.
        var boxed = w*.883;
        CutShape(c,new Point(boxed,o+5*s),new Point(boxed+43*s,o+5*s),
            new Point(boxed+50*s,o+12*s),new Point(boxed+50*s,i-2.7*s),
            new Point(boxed+12*s,i-2.7*s),new Point(boxed+5*s,o+10*s));
        CutShape(c,new Point(boxed+56*s,o-3*s),new Point(boxed+68*s,o-3*s),
            new Point(boxed+62*s,o+5*s),new Point(boxed+56*s,o+5*s));
        CutRect(c,boxed+71*s,o+8*s,4*s,4*s);

    }

    private static void CarveLeft(GeometryGroup c, double h, double s, double o, double i)
    {
        double mid=(o+i)/2;
        // Top-left rail: one long elbow terminating inside the band.
        var a=h*.12;
        CutTrace(c,2.2*s,new Point(o+5*s,a),new Point(o+5*s,a+17*s),
            new Point(o+15*s,a+27*s),new Point(o+15*s,a+61*s));
        CutDot(c,o+15*s,a+61*s,3.2*s);
        for(var k=0;k<5;k++)
            CutTrace(c,1.4*s,new Point(o+4*s,a+(75+7*k)*s),
                new Point(o+9*s,a+(80+7*k)*s));

        // Mid-upper: outside bite alternates with a smaller inside bite.
        var b=h*.295;
        CutShape(c,new Point(o-5*s,b),new Point(o+12*s,b),
            new Point(o+12*s,b+12*s),new Point(o+4*s,b+20*s),
            new Point(o-5*s,b+20*s));
        CutTrace(c,2.6*s,new Point(mid,b+32*s),new Point(mid,b+53*s),
            new Point(mid+6*s,b+59*s),new Point(mid+6*s,b+82*s));
        CutDot(c,mid+6*s,b+82*s,3.6*s);
        for(var k=0;k<4;k++)
            CutRect(c,o+5*s,b+(92+7*k)*s,8*s,3.5*s);

        // Center: zigzag negative channel, dotted split and thick chevron.
        var d=h*.49;
        CutTrace(c,2.8*s,new Point(o+5*s,d),new Point(o+5*s,d+16*s),
            new Point(o+14*s,d+25*s),new Point(o+14*s,d+52*s),
            new Point(o+6*s,d+60*s),new Point(o+6*s,d+90*s));
        CutDot(c,o+6*s,d+90*s,4.1*s);
        CutShape(c,new Point(i+4*s,d+27*s),new Point(i-7*s,d+27*s),
            new Point(i-14*s,d+34*s),new Point(i-14*s,d+52*s),
            new Point(i+4*s,d+52*s));
        for(var k=0;k<5;k++)
            CutDot(c,o+7*s,d+(102+8*k)*s,1.55*s);

        // Lower: a tall staggered ladder, then two isolated black terminals.
        var e=h*.73;
        CutRect(c,o+5*s,e,3.7*s,31*s);
        for(var k=0;k<8;k++)
            CutRect(c,o+11*s,e+(7*k)*s,5.5*s,3.3*s);
        CutShape(c,new Point(i+4*s,e+73*s),new Point(i-5*s,e+73*s),
            new Point(i-13*s,e+81*s),new Point(i-13*s,e+103*s),
            new Point(i+4*s,e+103*s));
        CutDot(c,o+9*s,e+118*s,3.7*s);
        CutTrace(c,1.3*s,new Point(o+9*s,e+118*s),new Point(o+9*s,e+133*s));
        CutDot(c,o+9*s,e+133*s,2.5*s);

        // Small enclosed socket near the shoulder and a long staggered
        // interior channel with a narrow material bridge at mid-height.
        var first = 37*s;
        CutShape(c,new Point(o+4*s,first),new Point(i-4*s,first),
            new Point(i-2.8*s,first+8*s),new Point(i-2.8*s,first+31*s),
            new Point(o+6*s,first+31*s),new Point(o+4*s,first+27*s));
        CutDot(c,o+9*s,first+37*s,2.6*s);
        var longRun = h*.332;
        CutTrace(c,1.7*s,new Point(o+5*s,longRun),
            new Point(o+12*s,longRun+7*s),
            new Point(o+12*s,longRun+44*s));
        CutTrace(c,1.7*s,new Point(o+12*s,longRun+49*s),
            new Point(o+12*s,longRun+79*s),
            new Point(o+6*s,longRun+85*s));
        CutDot(c,o+6*s,longRun+85*s,2.6*s);

        // Two isolated cutouts, separated by a slim COLOR bridge. Neither
        // opens to the outer or inner rail; the small split remains solid.
        var notch = h*.667;
        CutShape(c,new Point(o+4*s,notch),new Point(i-3*s,notch),
            new Point(i-3*s,notch+15*s),new Point(o+7*s,notch+20*s),
            new Point(o+4*s,notch+20*s));
        CutRect(c,o+4*s,notch+24*s,8.2*s,17*s);
        CutShape(c,new Point(o-3*s,h*.903),new Point(o+10*s,h*.903),
            new Point(o+10*s,h*.903+6*s),new Point(o+4*s,h*.903+12*s),
            new Point(o-3*s,h*.903+12*s));

    }

    private static void CarveRight(GeometryGroup c, double w, double h,
        double s, double o, double i)
    {
        double R=w-o;
        // Upper-right: tapered serpentine and off-center terminal.
        var a=h*.115;
        CutTrace(c,2.4*s,new Point(R-6*s,a),new Point(R-6*s,a+22*s),
            new Point(R-15*s,a+31*s),new Point(R-15*s,a+78*s));
        CutDot(c,R-15*s,a+78*s,4.2*s);
        for(var k=0;k<4;k++)
            CutTrace(c,1.4*s,new Point(R-5*s,a+(92+7*k)*s),
                new Point(R-10*s,a+(97+7*k)*s));
        CutShape(c,new Point(R+5*s,a+136*s),new Point(R-10*s,a+136*s),
            new Point(R-10*s,a+143*s),new Point(R-3*s,a+150*s),
            new Point(R+5*s,a+150*s));

        // Right upper-middle: stacked pads and a discontinuous black bus.
        var b=h*.35;
        for(var k=0;k<6;k++)
            CutRect(c,R-17*s,b+k*7.6*s,6*s,3.5*s);
        CutTrace(c,2.3*s,new Point(R-6*s,b+9*s),new Point(R-6*s,b+57*s),
            new Point(R-13*s,b+64*s),new Point(R-13*s,b+86*s));
        CutDot(c,R-13*s,b+86*s,3.0*s);
        CutShape(c,new Point(R+4*s,b+105*s),new Point(R-7*s,b+105*s),
            new Point(R-17*s,b+115*s),new Point(R-17*s,b+127*s),
            new Point(R+4*s,b+127*s));

        // Central flank: a notched lightning channel, mixed perforations.
        var d=h*.57;
        CutShape(c,new Point(R-21*s,d),new Point(R-8*s,d),
            new Point(R-8*s,d+17*s),new Point(R-16*s,d+25*s),
            new Point(R-16*s,d+47*s),new Point(R-7*s,d+56*s),
            new Point(R-7*s,d+65*s),new Point(R-21*s,d+65*s));
        for(var k=0;k<6;k++)
            CutDot(c,R-(7+(k%2)*6)*s,d+(75+k*8)*s,(k%3==0?2.8:1.6)*s);
        CutTrace(c,2.1*s,new Point(R-4*s,d+133*s),
            new Point(R-13*s,d+142*s),new Point(R-13*s,d+167*s));
        CutDot(c,R-13*s,d+167*s,3.4*s);

        // Bottom flank: black slash field, three asymmetric tiny sockets.
        var e=h*.83;
        for(var k=0;k<7;k++)
            CutTrace(c,1.35*s,new Point(R-17*s,e+k*6*s),
                new Point(R-12*s,e+(5+k*6)*s));
        CutRect(c,R-9*s,e+57*s,3.6*s,24*s);
        CutDot(c,R-15*s,e+88*s,3.2*s);
        CutDot(c,R-6*s,e+103*s,2*s);
        CutDot(c,R-13*s,e+113*s,2.5*s);

        // Two unlike enclosed cavities: one with a chamfer, one bridged.
        // The two boundary rails remain entirely invisible.
        var pocket = h*.076;
        CutShape(c,new Point(R-14*s,pocket),new Point(R-5*s,pocket),
            new Point(R-5*s,pocket+37*s),new Point(R-10*s,pocket+43*s),
            new Point(R-14*s,pocket+43*s));
        CutRect(c,R-14*s,pocket+48*s,8*s,9*s);
        var longRun = h*.318;
        CutTrace(c,1.7*s,new Point(R-6*s,longRun),
            new Point(R-13*s,longRun+7*s),
            new Point(R-13*s,longRun+59*s));
        CutTrace(c,1.7*s,new Point(R-13*s,longRun+65*s),
            new Point(R-13*s,longRun+95*s),
            new Point(R-6*s,longRun+102*s));
        CutDot(c,R-6*s,longRun+102*s,2.8*s);

        // Staggered enclosed windows with different depths and a narrow
        // uncut strip crossing the black channel.
        var split = h*.705;
        CutShape(c,new Point(R-15*s,split),new Point(R-5*s,split),
            new Point(R-5*s,split+27*s),new Point(R-11*s,split+33*s),
            new Point(R-15*s,split+33*s));
        CutRect(c,R-15*s,split+36.5*s,8.8*s,20*s);
        CutDot(c,R-11*s,split+64*s,2.2*s);
        var low = h*.938;
        CutShape(c,new Point(R-10*s,low-9*s),new Point(R+3*s,low-9*s),
            new Point(R+3*s,low),new Point(R-6*s,low),
            new Point(R-10*s,low-4*s));

    }

    private static void CarveBottom(GeometryGroup c, double w, double h,
        double s, double o, double i)
    {
        double y=h-(o+i)/2, bot=h-o, inner=h-i;
        // Western broken conductor, unlike the top-left looping route.
        CutTrace(c,2.2*s,new Point(43*s,y+5*s),new Point(58*s,y-10*s),
            new Point(90*s,y-10*s),new Point(100*s,y));
        CutDot(c,90*s,y-10*s,4*s);
        for(var k=0;k<7;k++)
            CutRect(c,(117+7.3*k)*s,inner+5*s,3.7*s,8*s);
        CutShape(c,new Point(184*s,bot+4*s),new Point(207*s,bot+4*s),
            new Point(207*s,bot-7*s),new Point(196*s,bot-18*s),
            new Point(184*s,bot-18*s));
        CutTrace(c,1.7*s,new Point(222*s,y+6*s),new Point(232*s,y-4*s),
            new Point(260*s,y-4*s));
        CutDot(c,260*s,y-4*s,3*s);

        // West-central: nested black offsets and a dotted slash matrix.
        var a=w*.27;
        CutTrace(c,2.7*s,new Point(a,bot-6*s),new Point(a+14*s,bot-20*s),
            new Point(a+51*s,bot-20*s),new Point(a+60*s,bot-11*s));
        CutDot(c,a+51*s,bot-20*s,3.6*s);
        for(var k=0;k<6;k++)
            CutTrace(c,1.3*s,new Point(a+(80+8*k)*s,inner+5*s),
                new Point(a+(85+8*k)*s,inner+10*s));
        CutRect(c,a+150*s,bot-11*s,20*s,3*s);
        CutRect(c,a+178*s,bot-11*s,5*s,3*s);

        // Unequal center bridge and aggressively carved inward step.
        var mid=w*.505;
        CutShape(c,new Point(mid-18*s,inner-4*s),
            new Point(mid+14*s,inner-4*s),new Point(mid+14*s,inner+7*s),
            new Point(mid+4*s,inner+17*s),new Point(mid-7*s,inner+17*s),
            new Point(mid-18*s,inner+6*s));
        CutTrace(c,2.2*s,new Point(mid+29*s,bot-7*s),
            new Point(mid+45*s,bot-7*s),new Point(mid+55*s,bot-17*s),
            new Point(mid+78*s,bot-17*s));
        CutDot(c,mid+78*s,bot-17*s,4*s);
        for(var k=0;k<5;k++) CutRect(c,mid+(93+8*k)*s,inner+5*s,4*s,5.5*s);

        // South-east, intentionally denser with a curved-feeling stepped bus.
        var e=w*.79;
        CutTrace(c,2.5*s,new Point(e-93*s,bot-6*s),
            new Point(e-80*s,bot-19*s),new Point(e-51*s,bot-19*s),
            new Point(e-42*s,bot-10*s),new Point(e-10*s,bot-10*s));
        CutDot(c,e-51*s,bot-19*s,3.1*s);
        CutShape(c,new Point(e+9*s,inner-4*s),new Point(e+41*s,inner-4*s),
            new Point(e+41*s,inner+6*s),new Point(e+32*s,inner+15*s),
            new Point(e+9*s,inner+15*s));
        for(var k=0;k<8;k++)
            CutTrace(c,1.2*s,new Point(e+(57+7*k)*s,inner+5*s),
                new Point(e+(61+7*k)*s,inner+9*s));
        CutDot(c,e+130*s,y+3*s,4.2*s);
        CutTrace(c,1.7*s,new Point(e+143*s,y+6*s),
            new Point(e+153*s,y-4*s),new Point(e+170*s,y-4*s));
        CutDot(c,e+170*s,y-4*s,2.8*s);
        CutRect(c,w-79*s,inner+6*s,15*s,3*s);
        CutDot(c,w-54*s,inner+11*s,3.2*s);

        // Hollow panels of different lengths, not just holes cut inward
        // from a single edge. The small gap between cuts is solid material.
        var west = w*.218;
        CutShape(c,new Point(west,inner+4*s),new Point(west+42*s,inner+4*s),
            new Point(west+50*s,inner+12*s),new Point(west+50*s,bot-4*s),
            new Point(west+5*s,bot-4*s),new Point(west,bot-9*s));
        CutRect(c,west+55*s,inner+5*s,4.5*s,8.2*s);
        var centerWindow = w*.417;
        CutShape(c,new Point(centerWindow,inner+4*s),
            new Point(centerWindow+20*s,inner+4*s),
            new Point(centerWindow+26*s,inner+10*s),
            new Point(centerWindow+26*s,bot-4*s),
            new Point(centerWindow+4*s,bot-4*s),
            new Point(centerWindow,bot-8*s));
        CutRect(c,centerWindow+31*s,inner+4*s,14*s,4*s);

        // Long /----\ flat-topped conductor and a colored bridge dividing
        // its black run; the return leg ends in a black drilled terminal.
        var longRun = w*.628;
        CutTrace(c,1.9*s,new Point(longRun,bot-5*s),
            new Point(longRun+10*s,bot-15*s),
            new Point(longRun+49*s,bot-15*s));
        CutTrace(c,1.9*s,new Point(longRun+54*s,bot-15*s),
            new Point(longRun+93*s,bot-15*s),
            new Point(longRun+103*s,bot-5*s));
        CutDot(c,longRun+103*s,bot-5*s,3*s);

        // A thin interior slit and one outer-edge interruption intentionally
        // have different widths, unlike the earlier repeating marks.
        var east = w*.892;
        CutRect(c,east,inner+4*s,31*s,2.8*s);
        CutShape(c,new Point(east+39*s,bot+3*s),new Point(east+52*s,bot+3*s),
            new Point(east+46*s,bot-4*s),new Point(east+39*s,bot-4*s));

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
