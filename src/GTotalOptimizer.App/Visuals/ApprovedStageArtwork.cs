using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using SharpVectors.Converters;

namespace GTotalOptimizer.App.Visuals;

/// <summary>
/// Renders the owner's three approved stage-button SVGs as vector WPF drawings.
/// Only the artwork is sourced from SVG: a native ContentPresenter controls the
/// centered label, focus, enabled state and existing button command.
/// </summary>
public sealed class ApprovedStageArtwork : SvgViewbox
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Sources =
        new(ReadApprovedSources, isThreadSafe: true);

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
        Stretch = System.Windows.Media.Stretch.Fill;
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
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/StageButtons/Approved.zip"))
            ?? throw new InvalidOperationException("Approved stage button artwork is missing from the package.");

        using var source = resource.Stream;
        using var zip = new ZipArchive(source, ZipArchiveMode.Read);
        var names = new Dictionary<string, string>
        {
            ["Yellow"] = "GCO_Active_Yellow_Button_v2.8.svg",
            ["Unavailable"] = "GCO_Inactive_Button_v3.2.svg",
            ["Restore"] = "GCO_Restore_Profilers_Button_v1.4.svg"
        };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in names)
        {
            var entry = zip.GetEntry(pair.Value)
                ?? throw new InvalidDataException("Missing approved SVG: " + pair.Value);
            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();
            if (!xml.Contains("<svg", StringComparison.Ordinal) ||
                !xml.Contains("</svg>", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid approved SVG: " + pair.Value);

            // WPF text is bound to the current session state, never baked into art.
            xml = Regex.Replace(xml, @"<text\b[^>]*>.*?</text>", "",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            result.Add(pair.Key, xml);
        }
        // Preserve the three approved source SVGs, but make the active yellow
        // outer rim use the inactive design's own SVG paint definitions.
        // No extra WPF outline: the cyan rim is drawn on the exact SVG path.
        result["Yellow"] = UseInactiveSvgRim(result["Yellow"], result["Unavailable"]);
        return result;
    }

    private static string UseInactiveSvgRim(string yellowSvg, string inactiveSvg)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        var yellow = XDocument.Parse(yellowSvg, LoadOptions.PreserveWhitespace);
        var inactive = XDocument.Parse(inactiveSvg);
        var defs = yellow.Root?.Element(svg + "defs")
            ?? throw new InvalidDataException("Yellow SVG definitions missing.");
        var inactiveDefs = inactive.Root?.Element(svg + "defs")
            ?? throw new InvalidDataException("Inactive SVG definitions missing.");

        // Reuse the original v3.2 cyan gradient and soft halo verbatim.
        foreach (var id in new[] { "outline", "rimGlow" })
        {
            var source = inactiveDefs.Elements().SingleOrDefault(
                e => (string?)e.Attribute("id") == id)
                ?? throw new InvalidDataException("Inactive rim definition missing: " + id);
            if (defs.Elements().Any(e => (string?)e.Attribute("id") == id))
                throw new InvalidDataException("Duplicate yellow rim definition: " + id);
            defs.Add(new XElement(source));
        }

        var oldGoldRim = yellow.Root!.Elements(svg + "use").SingleOrDefault(
            e => (string?)e.Attribute("stroke") == "#F5D528")
            ?? throw new InvalidDataException("Yellow SVG outer rim changed unexpectedly.");
        oldGoldRim.SetAttributeValue("stroke", "#00DBED");
        oldGoldRim.SetAttributeValue("stroke-opacity", ".28");
        oldGoldRim.SetAttributeValue("stroke-width", "2.4");
        oldGoldRim.SetAttributeValue("filter", "url(#rimGlow)");
        defs.Elements(svg + "filter").SingleOrDefault(
            e => (string?)e.Attribute("id") == "outerGlow")?.Remove();

        // Exactly one fine cyan rim over the body, following the same
        // buttonShape geometry as the original yellow fill.
        var sheen = yellow.Root.Elements(svg + "g").SingleOrDefault(
            e => (string?)e.Attribute("mask") == "url(#stripeFade)")
            ?? throw new InvalidDataException("Yellow SVG sheen anchor changed unexpectedly.");
        sheen.AddAfterSelf(new XElement(svg + "use",
            new XAttribute("href", "#buttonShape"),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", "url(#outline)"),
            new XAttribute("stroke-width", "1.2")));
        return yellow.ToString(SaveOptions.DisableFormatting);
    }
}
