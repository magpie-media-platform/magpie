using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

internal sealed class ContentFileServiceTemplateCases
{
    public static IEnumerable<TestCaseData> Cases()
    {
        yield return Case(
            "{Title} ({Year}).{Ext}",
            "Inception (2010).mkv",
            ("Title", "Inception"),
            ("Year", "2010"),
            ("Ext", "mkv"));
        yield return Case("{Title} ({Year}).{Ext}", "Solo ()", ("Title", "Solo"));
        yield return Case("{TITLE}|{title}|{Title}", "Inception Inception Inception", ("Title", "Inception"));
        yield return Case("{Title:lower}", "inception", ("Title", "Inception"));
        yield return Case("{Title:upper}", "INCEPTION", ("Title", "Inception"));
        yield return Case("{Title:reverse}", "Inception", ("Title", "Inception"));
        yield return Case(
            "{Title}.mkv",
            "Lock, Stock_ _Two_ Smoking _ Barrels_.mkv",
            ("Title", "Lock, Stock: <Two> Smoking | Barrels?"));
        yield return Case(
            "{Title} ({Year})/{Title} ({Year}).{Ext}",
            "Inception (2010)/Inception (2010).mkv",
            ("Title", "Inception"),
            ("Year", "2010"),
            ("Ext", "mkv"));
        yield return Case("{Title}\\{Title}.{Ext}", "Inception\\Inception.mkv", ("Title", "Inception"), ("Ext", "mkv"));
        yield return Case("{Title} ({Year}). /next", "Trailing ()/next", ("Title", "Trailing"), ("Year", string.Empty));
        yield return Case("{{not-a-token}} {Title}", "{not-a-token} Brackets", ("Title", "Brackets"));
        yield return Case("{Title", "{Title", ("Title", "Inception"));
        yield return Case("{}{Title}", "Inception", ("Title", "Inception"));
        yield return Case("{ Title : upper }", "INCEPTION", ("Title", "Inception"));
        yield return Case("{OriginalTitle}.{Ext}", ".mkv", ("OriginalTitle", null), ("Ext", "mkv"));
        yield return Case(
            "Movies/{Title} - released {Year}.mkv",
            "Movies/Inception - released 2010.mkv",
            ("Title", "Inception"),
            ("Year", "2010"));
        yield return Case("{Title}.mkv", "season1_episode2.mkv", ("Title", "season1/episode2"));
        yield return Case("{Title}.mkv", "____etc.mkv", ("Title", "../../etc"));
        yield return Case("{Title}/file.mkv", "_/file.mkv", ("Title", ".."));
    }

    private static TestCaseData Case(string template, string expected, params (string Name, string? Value)[] values)
    {
        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            tokens[value.Name] = value.Value;
        }

        return new TestCaseData(template, tokens, expected);
    }
}
