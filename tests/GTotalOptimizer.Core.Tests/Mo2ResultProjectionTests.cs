using GTotalOptimizer.Core;
using System.Text.Json.Nodes;

internal static class Mo2ResultProjectionTests
{
    public static void Run(string game, string overwrite, Mo2OverlayPreparation prepared)
    {
        var journal = JsonNode.Parse(File.ReadAllText(prepared.JournalFile))!.AsObject();
        var fixture = journal["fixtureRoot"]!.GetValue<string>();
        const string cet = "bin/x64/plugins/cyber_engine_tweaks/CET_Runtime_Profile_Markers.csv";
        const string red = "red4ext/plugins/G-REDscript-Profiler/RESULTS/Capture_0001_TEST_1000/GRSP_Summary.csv";
        const string stop = "red4ext/plugins/G-REDscript-Profiler/RESULTS/Capture_0001_TEST_1000/GCO-F11-Complete.flag";
        const string other = "red4ext/plugins/Codeware/user.log";
        string Put(string root, string relative, string value)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
            return path;
        }
        var cetFile = Put(game, cet, "CAPTURE_START,PAUSE");
        var redFile = Put(overwrite, red, "original redscript data");
        var stopFile = Put(prepared.StagingDirectory, stop, "F11_STOP");
        var foreign = Path.Combine(overwrite, other);
        Mo2NativeOutputReset.ProjectForCollection(journal, fixture);
        if (File.ReadAllText(Path.Combine(fixture, cet)) != "CAPTURE_START,PAUSE" ||
            File.ReadAllText(Path.Combine(fixture, red)) != "original redscript data" ||
            File.ReadAllText(Path.Combine(fixture, stop)) != "F11_STOP" ||
            File.Exists(Path.Combine(fixture, other)) ||
            !File.Exists(cetFile) || !File.Exists(redFile) || !File.Exists(stopFile) ||
            File.ReadAllText(foreign) != "unrelated Overwrite data")
            throw new InvalidDataException("MO2_OWNER_OUTPUT_PROJECTION_OR_FOREIGN_PRESERVATION_FAILED");

        // Never silently prefer one overlapping file or mix it with stale
        // fixture data. The read-only admission leaves both originals intact.
        var conflict = Put(overwrite, cet, "different owner result");
        try
        {
            Mo2NativeOutputReset.ProjectForCollection(journal, fixture);
            throw new Exception("MO2_AMBIGUOUS_NATIVE_RESULT_ACCEPTED");
        }
        catch (InvalidDataException) { }
        if (File.ReadAllText(cetFile) != "CAPTURE_START,PAUSE" ||
            File.ReadAllText(conflict) != "different owner result")
            throw new InvalidDataException("MO2_AMBIGUOUS_NATIVE_RESULT_MODIFIED");
        File.Delete(conflict);

        // Missing completion proof must vanish from the fixture too.
        File.Delete(stopFile);
        Mo2NativeOutputReset.ProjectForCollection(journal, fixture);
        if (File.Exists(Path.Combine(fixture, stop)))
            throw new InvalidDataException("MO2_STALE_F11_PROOF_RETAINED");
        Mo2NativeOutputReset.Discard(journal);
        Mo2NativeOutputReset.ProjectForCollection(journal, fixture);
        if (File.Exists(Path.Combine(fixture, cet)) || File.Exists(Path.Combine(fixture, red)) ||
            File.Exists(cetFile) || File.Exists(redFile) ||
            File.ReadAllText(foreign) != "unrelated Overwrite data")
            throw new InvalidDataException("MO2_RESULT_PROJECTION_RESET_FAILED");
        Console.WriteLine("PASS MO2 result projection from physical/Overwrite/stage, ambiguity and stale F11 refusal");
    }
}
