using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using SharpVectors.Converters;

namespace GTotalOptimizer.App.Visuals;

/// <summary>
/// Renders the owner's three standardized stage SVGs as native WPF vector drawings.
/// The SVGs own the shape, gradients and rim. WPF owns dynamic text and interactions.
/// </summary>
public sealed class ApprovedStageArtwork : SvgViewbox
{
    private sealed record ApprovedSources(
        IReadOnlyDictionary<string, string> Variants, Rect ViewBox, Geometry Shell);

    private static readonly Lazy<ApprovedSources> Sources =
        new(ReadApprovedSources, isThreadSafe: true);

    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(string),
            typeof(ApprovedStageArtwork),
            new PropertyMetadata("Yellow", OnVariantChanged));

    public string Variant
    {
        get => (string)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public ApprovedStageArtwork()
    {
        IsHitTestVisible = false;
        // The owner standardized all three SVGs to one aspect ratio and shell.
        // Never distort the 45-degree chamfers or stroke widths to fill a slot.
        Stretch = System.Windows.Media.Stretch.Uniform;
        TextAsGeometry = false;
        ApplyVariant();
    }

    private static void OnVariantChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((ApprovedStageArtwork)target).ApplyVariant();

    private void ApplyVariant()
    {
        if (!Sources.Value.Variants.TryGetValue(Variant, out var source))
            throw new InvalidOperationException("Unknown approved GCO stage button: " + Variant);
        SvgSource = source;
    }

    /// <summary>
    /// The hover uses the exact shell path from the validated author SVGs.
    /// Uniformly render that source geometry inside any WPF button slot.
    /// No separate approximated chamfer or window-specific offsets.
    /// </summary>
    internal static Geometry RenderedShell(Size slot)
    {
        var source = Sources.Value;
        var viewport = source.ViewBox;
        if (!(slot.Width > 0 && slot.Height > 0) ||
            !double.IsFinite(slot.Width) || !double.IsFinite(slot.Height))
            return Geometry.Empty;

        var scale = Math.Min(slot.Width / viewport.Width, slot.Height / viewport.Height);
        var offsetX = (slot.Width - viewport.Width * scale) / 2 - viewport.X * scale;
        var offsetY = (slot.Height - viewport.Height * scale) / 2 - viewport.Y * scale;
        var geometry = source.Shell.Clone();
        geometry.Transform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);
        geometry.Freeze();
        return geometry;
    }

    private static ApprovedSources ReadApprovedSources()
    {
        // GitHub commit 465565d introduced the individual standardized sources.
        // Do not recreate the deleted ZIP or patch one button's border at runtime.
        var assets = new[]
        {
            (Variant: "Yellow", File: "GCO_Active_Yellow_Button_v2.9.svg", Shell: "buttonShape"),
            (Variant: "Unavailable", File: "GCO_Inactive_Button_v3.3.svg", Shell: "outer"),
            (Variant: "Restore", File: "GCO_Restore_Profilers_Button_v1.4.svg", Shell: "shell")
        };

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? canonicalViewBox = null;
        string? canonicalShell = null;

        foreach (var asset in assets)
        {
            var uri = new Uri("pack://application:,,,/StageButtons/" + asset.File);
            var resource = System.Windows.Application.GetResourceStream(uri)
                ?? throw new InvalidDataException("Missing packaged stage SVG: " + asset.File);

            using var reader = new StreamReader(resource.Stream);
            var xml = reader.ReadToEnd();
            var svg = XDocument.Parse(xml);
            if (svg.Root?.Name != SvgNamespace + "svg")
                throw new InvalidDataException("Invalid stage SVG root: " + asset.File);

            var viewBox = (string?)svg.Root.Attribute("viewBox");
            var shell = svg.Descendants(SvgNamespace + "path")
                .SingleOrDefault(e => (string?)e.Attribute("id") == asset.Shell);
            var shellGeometry = (string?)shell?.Attribute("d");
            if (string.IsNullOrWhiteSpace(viewBox) || string.IsNullOrWhiteSpace(shellGeometry))
                throw new InvalidDataException("Missing viewBox or shell geometry: " + asset.File);

            canonicalViewBox ??= viewBox;
            canonicalShell ??= shellGeometry;
            if (viewBox != canonicalViewBox || shellGeometry != canonicalShell)
                throw new InvalidDataException(
                    "Stage SVGs must share the exact same viewBox and outer shell: " + asset.File);

            // WPF controls the current label, including the user's cyan active text.
            xml = Regex.Replace(xml, @"<text\b[^>]*>.*?</text>", "",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            result.Add(asset.Variant, xml);
        }
        var components = Regex.Split(canonicalViewBox ?? "",
            @"[\s,]+").Where(part => part.Length > 0).ToArray();
        if (components.Length != 4)
            throw new InvalidDataException("Invalid standardized stage SVG viewBox");
        var numbers = components.Select(part =>
            double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        var viewBox = new Rect(numbers[0], numbers[1], numbers[2], numbers[3]);
        if (!(viewBox.Width > 0 && viewBox.Height > 0))
            throw new InvalidDataException("Nonpositive standardized stage SVG viewBox");

        // WPF and SVG share the M/L/H/V/Z path syntax used by this canonical
        // shell. The same source path defines the picture AND its dynamic glow.
        var geometry = Geometry.Parse(canonicalShell
            ?? throw new InvalidDataException("Missing canonical stage shell"));
        geometry.Freeze();
        return new ApprovedSources(result, viewBox, geometry);
    }
}

/// <summary>
/// Cyan hover is a real outside-only band around the exact author SVG shell.
/// It follows the uniformly scaled native path at EVERY button width and
/// height, including intermediate sizes. No size converter or extra SVG.
/// </summary>
public sealed class StageShellHover : FrameworkElement
{
    private const double OutsideGap = 0.8;
    private const double BandWidth = 1.7;
    private static readonly System.Windows.Media.Brush CyanBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0xF2, 0xFF));

    static StageShellHover()
    {
        CyanBrush.Freeze();
    }

    internal static Geometry OuterBand(Size slot)
    {
        var shell = ApprovedStageArtwork.RenderedShell(slot);
        if (shell == Geometry.Empty)
            return Geometry.Empty;

        // A widened closed SVG shell produces an inside AND an outside band.
        // Subtract the actual filled shell, then the inner widened band, so
        // the bright hover never paints on the button body or SVG rim.
        var outer = shell.GetWidenedPathGeometry(new Pen(Brushes.White,
            2 * (OutsideGap + BandWidth)) { LineJoin = PenLineJoin.Miter });
        var inside = shell.GetWidenedPathGeometry(new Pen(Brushes.White,
            2 * OutsideGap) { LineJoin = PenLineJoin.Miter });
        var external = Geometry.Combine(outer, shell, GeometryCombineMode.Exclude, null);
        var band = Geometry.Combine(external, inside, GeometryCombineMode.Exclude, null);
        band.Freeze();
        return band;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (RenderSize.Width < 10 || RenderSize.Height < 10)
            return;
        context.DrawGeometry(CyanBrush, null, OuterBand(RenderSize));
    }
}
