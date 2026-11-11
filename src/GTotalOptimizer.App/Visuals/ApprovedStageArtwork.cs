using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
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
        return result;
    }
}
