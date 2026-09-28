using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

internal sealed class ContentFileServiceTemplateCases
{
    public static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(
            "{Title} ({Year}).{Ext}",
            "Inception",
            null,
            new DateTime(2010, 1, 1),
            "source.mkv",
            new[] { "Inception (2010).mkv" });
        yield return new TestCaseData(
            "{Title} ({Year}).{Ext}",
            "Solo",
            null,
            null,
            "source",
            new[] { "Solo" });
        yield return new TestCaseData(
            "{TITLE}|{title}|{Title}",
            "Inception",
            null,
            null,
            "source",
            new[] { "Inception Inception Inception" });
        yield return new TestCaseData(
            "{Title:lower}",
            "Inception",
            null,
            null,
            "source",
            new[] { "inception" });
        yield return new TestCaseData(
            "{Title:upper}",
            "Inception",
            null,
            null,
            "source",
            new[] { "INCEPTION" });
        yield return new TestCaseData(
            "{Title:reverse}",
            "Inception",
            null,
            null,
            "source",
            new[] { "Inception" });
        yield return new TestCaseData(
            "{Title}.mkv",
            "Lock, Stock: <Two> Smoking | Barrels?",
            null,
            null,
            "source",
            new[] { "Lock, Stock_ _Two_ Smoking _ Barrels_.mkv" });
        yield return new TestCaseData(
            "{Title} ({Year})/{Title} ({Year}).{Ext}",
            "Inception",
            null,
            new DateTime(2010, 1, 1),
            "source.mkv",
            new[] { "Inception (2010)", "Inception (2010).mkv" });
        yield return new TestCaseData(
            "{Title}\\{Title}.{Ext}",
            "Inception",
            null,
            null,
            "source.mkv",
            new[] { "Inception", "Inception.mkv" });
        yield return new TestCaseData(
            "{Title} ({Year}). /next",
            "Trailing",
            null,
            null,
            "source",
            new[] { "Trailing", "next" });
        yield return new TestCaseData(
            "{{not-a-token}} {Title}",
            "Brackets",
            null,
            null,
            "source",
            new[] { "{not-a-token} Brackets" });
        yield return new TestCaseData(
            "{Title",
            "Inception",
            null,
            null,
            "source",
            new[] { "{Title" });
        yield return new TestCaseData(
            "{}{Title}",
            "Inception",
            null,
            null,
            "source",
            new[] { "Inception" });
        yield return new TestCaseData(
            "{ Title : upper }",
            "Inception",
            null,
            null,
            "source",
            new[] { "INCEPTION" });
        yield return new TestCaseData(
            "{OriginalTitle}.{Ext}",
            "The Matrix",
            null,
            null,
            "source.mkv",
            new[] { "The Matrix", "source.mkv" });
        yield return new TestCaseData(
            "Movies/{Title} - released {Year}.mkv",
            "Inception",
            null,
            new DateTime(2010, 1, 1),
            "source",
            new[] { "Movies", "Inception - released 2010.mkv" });
        yield return new TestCaseData(
            "{Title}.mkv",
            "season1/episode2",
            null,
            null,
            "source",
            new[] { "season1_episode2.mkv" });
        yield return new TestCaseData(
            "{Title}.mkv",
            "../../etc",
            null,
            null,
            "source",
            new[] { "____etc.mkv" });
        yield return new TestCaseData(
            "{Title}/file.mkv",
            "..",
            null,
            null,
            "source",
            new[] { "_", "file.mkv" });
    }
}
