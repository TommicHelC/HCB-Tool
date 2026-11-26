using System;
using System.Text;

namespace HCB.RevitAddin.Features.RibbonManager;

/// <summary>
/// Builds compact ribbon tab and panel titles, e.g. "Work Plane" -> "WPlane", "Mechanical" -> "Mech".
/// Panel width in Revit is often driven by the title bar, so short titles make panels narrower.
/// </summary>
internal static class RibbonPanelNameShortener
{
    private const int KeepUpToLength = 6;
    private const int LongWordLength = 7;
    private const int TruncatedWordLength = 5;

    private static readonly Dictionary<string, string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Work Plane"] = "WPlane",
        ["Room & Area"] = "Room&Area",
        ["Point Cloud"] = "PtCloud",
        ["Color Fill"] = "ClrFill",
        ["Visual Programming"] = "VisProg",
        ["Design Options"] = "DsgnOpt",
        ["Family Editor"] = "FamEd",
        ["Model Site"] = "Site",
        ["Conceptual Mass"] = "Mass",
        ["Plumbing & Piping"] = "Plumb",
        ["Manage Project"] = "Project",
        ["Massing & Site"] = "Mass&Site",
        ["User Interface"] = "UI"
    };

    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tools"] = string.Empty,
        ["Tool"] = string.Empty,
        ["Panel"] = string.Empty,
        ["and"] = "&",
        ["Architecture"] = "Arch",
        ["Architectural"] = "Arch",
        ["Structure"] = "Struct",
        ["Structural"] = "Struct",
        ["Mechanical"] = "Mech",
        ["Electrical"] = "Elec",
        ["Plumbing"] = "Plumb",
        ["Fabrication"] = "Fab",
        ["Annotation"] = "Anno",
        ["Annotate"] = "Anno",
        ["Dimension"] = "Dim",
        ["Dimensions"] = "Dim",
        ["Properties"] = "Props",
        ["Selection"] = "Select",
        ["Geometry"] = "Geom",
        ["Clipboard"] = "Clip",
        ["Connection"] = "Conn",
        ["Connections"] = "Conn",
        ["Configuration"] = "Config",
        ["Settings"] = "Set",
        ["Parameters"] = "Params",
        ["Parameter"] = "Param",
        ["Visibility"] = "Vis",
        ["Graphics"] = "Gfx",
        ["Presentation"] = "Pres",
        ["Windows"] = "Win",
        ["Composition"] = "Comp",
        ["Collaborate"] = "Collab",
        ["Coordinate"] = "Coord",
        ["Coordination"] = "Coord",
        ["Synchronize"] = "Sync",
        ["Management"] = "Mgmt",
        ["Project"] = "Proj",
        ["Location"] = "Loc",
        ["Inquiry"] = "Inq",
        ["Options"] = "Opt",
        ["Phasing"] = "Phase",
        ["Reports"] = "Rpt",
        ["Schedules"] = "Sched",
        ["Analysis"] = "Anlys",
        ["Analytical"] = "Anlyt",
        ["Energy"] = "Enrgy",
        ["Equipment"] = "Equip",
        ["Systems"] = "Sys",
        ["System"] = "Sys",
        ["Element"] = "Elem",
        ["Elements"] = "Elem",
        ["Circulation"] = "Circ",
        ["Massing"] = "Mass",
        ["Conceptual"] = "Concept",
        ["Families"] = "Fam",
        ["Family"] = "Fam",
        ["Measure"] = "Meas",
        ["Detail"] = "Dtl",
        ["Symbol"] = "Sym",
        ["Import"] = "Imp",
        ["Topography"] = "Topo",
        ["Reinforcement"] = "Rebar",
        ["Foundation"] = "Fdn",
        ["Opening"] = "Open",
        ["Library"] = "Lib",
        ["Analyze"] = "Anlys",
        ["Precast"] = "Prec",
        ["Collaboration"] = "Collab",
        ["External"] = "Ext",
        ["Revisions"] = "Rev",
        ["Interoperability"] = "Interop",
        ["Navigation"] = "Nav",
        ["Materials"] = "Mat",
        ["Material"] = "Mat",
        ["Section"] = "Sect",
        ["Rendering"] = "Render",
        ["Communicate"] = "Comm",
        ["Interface"] = "UI",
        ["Additional"] = "More",
        ["Template"] = "Tmpl",
        ["Templates"] = "Tmpl",
        ["Coordinates"] = "Coord",
        ["Workshare"] = "Wshare",
        ["Worksets"] = "Wsets",
        ["Datum"] = "Datum",
        ["Create"] = "Create",
        ["Modify"] = "Modify",
        ["Sheet"] = "Sheet",
        ["Model"] = "Model"
    };

    public static string Shorten(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        string trimmed = title.Trim();
        if (trimmed.Length <= KeepUpToLength)
        {
            return trimmed;
        }

        if (Phrases.TryGetValue(trimmed, out string? phrase))
        {
            return phrase;
        }

        string working = trimmed;
        foreach (KeyValuePair<string, string> pair in Phrases)
        {
            working = ReplaceIgnoreCase(working, pair.Key, pair.Value);
        }

        string[] parts = working.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // A single unknown word is usually a product/brand name (Twinmotion, Enscape) - keep it whole.
        if (parts.Length == 1 && !Words.ContainsKey(parts[0]))
        {
            return working.Length < trimmed.Length ? working : trimmed;
        }
        List<string> output = new(parts.Length);
        foreach (string part in parts)
        {
            string word = ShortenWord(part);
            if (word.Length > 0)
            {
                output.Add(word);
            }
        }

        string result = string.Join(" ", output);
        return string.IsNullOrWhiteSpace(result) || result.Length >= trimmed.Length ? trimmed : result;
    }

    private static string ShortenWord(string word)
    {
        if (Words.TryGetValue(word, out string? mapped))
        {
            return mapped;
        }

        // Keep separators, acronyms, digits, brand-like camelCase names and words that are already short.
        if (word.Length < LongWordLength || !word.All(char.IsLetter) || word.Skip(1).Any(char.IsUpper))
        {
            return word;
        }

        return word.Substring(0, TruncatedWordLength) + ".";
    }

    private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
    {
        int index = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return input;
        }

        StringBuilder builder = new(input.Length);
        int start = 0;
        while (index >= 0)
        {
            builder.Append(input, start, index - start).Append(newValue);
            start = index + oldValue.Length;
            index = input.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
        }

        builder.Append(input, start, input.Length - start);
        return builder.ToString();
    }
}
