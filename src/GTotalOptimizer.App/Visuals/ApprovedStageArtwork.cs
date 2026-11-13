using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using SharpVectors.Converters;

namespace GTotalOptimizer.App.Visuals;

/// <summary>
/// Renders the owner's three standardized stage SVGs as native WPF vector drawings.
/// The SVGs own the shape, gradients and rim. WPF owns dynamic text and interactions.
/// </summary>
public sealed class ApprovedStageArtwork : SvgViewbox
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Sources =
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
        if (!Sources.Value.TryGetValue(Variant, out var source))
            throw new InvalidOperationException("Unknown approved GCO stage button: " + Variant);
        SvgSource = source;
    }

    private static IReadOnlyDictionary<string, string> ReadApprovedSources()
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
        return result;
    }
}

/// <summary>
/// Match the visible 760:112 SVG within its WPF button's row.
/// The SVG uses Uniform scaling, so a fullscreen-wide Button is NOT
/// the same width as its drawn chamfer; hover must never span the row.
/// </summary>
public sealed class StageHoverWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 ||
            values[0] is not double availableWidth ||
            values[1] is not double height ||
            !double.IsFinite(availableWidth) || !double.IsFinite(height) ||
            availableWidth <= 0 || height <= 0)
            return DependencyProperty.UnsetValue;

        return Math.Min(availableWidth, height * (760.0 / 112.0));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
