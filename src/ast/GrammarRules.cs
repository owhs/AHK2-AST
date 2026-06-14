using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public class GrammarRules
{
    public string Version { get; set; }
    public int MaxSkipTokens { get; set; }

    public GrammarRules()
    {
        Version = "2.0";
        MaxSkipTokens = 50;
    }

    /// <summary>
    /// Loads grammar rules from a JSON string.
    /// Uses simple manual parsing to avoid System.Web.Script dependency.
    /// </summary>
    public static GrammarRules LoadFromJson(string json)
    {
        var rules = new GrammarRules();

        // Extract version
        Match m = Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
        if (m.Success) rules.Version = m.Groups[1].Value;

        // Extract max_skip_tokens
        m = Regex.Match(json, @"""max_skip_tokens""\s*:\s*(\d+)");
        if (m.Success) rules.MaxSkipTokens = int.Parse(m.Groups[1].Value);

        return rules;
    }
}
