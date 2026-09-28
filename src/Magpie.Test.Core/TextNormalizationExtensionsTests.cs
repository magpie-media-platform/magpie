using Magpie.Core;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class TextNormalizationExtensionsTests
{
    [TestCase("abcd", "ABCD")]
    [TestCase("AbCd", "ABCD")]
    [TestCase("abcd_id", "ABCD")]
    [TestCase("ABCD-ID", "ABCD")]
    [TestCase("abcdid", "ABCD")]
    [TestCase("  abcd  ", "ABCD")]
    [TestCase("a b c d", "ABCD")]
    [TestCase("id", "ID")]
    [TestCase("ID", "ID")]
    [TestCase("_id", "ID")]
    [TestCase("aid", "A")]
    [TestCase("", "")]
    public void NormalizeExternalIdentifierName_ReturnsCanonicalKey_WhenSpellingVaries(string input, string expected)
    {
        // Act
        var actual = input.NormalizeExternalIdentifierName();

        // Assert
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
    [TestCase("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D")]
    [TestCase("a1b2c3d4e5f64a7b8c9d0e1f2a3b4c5d")]
    [TestCase("{a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d}")]
    [TestCase("(a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d)")]
    [TestCase("{0xa1b2c3d4,0xe5f6,0x4a7b,{0x8c,0x9d,0x0e,0x1f,0x2a,0x3b,0x4c,0x5d}}")]
    [TestCase("  {a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d}  ")]
    public void NormalizeExternalIdentifierValue_ReturnsCanonicalGuid_WhenSpellingVaries(string input)
    {
        // Act
        var actual = input.NormalizeExternalIdentifierValue();

        // Assert
        Assert.That(actual, Is.EqualTo("A1B2C3D4E5F64A7B8C9D0E1F2A3B4C5D"));
    }

    [TestCase("0123", "123")]
    [TestCase("123", "123")]
    [TestCase("  0000042  ", "42")]
    [TestCase("0", "0")]
    public void NormalizeExternalIdentifierValue_DropsZeroPadding_WhenValueIsAnInteger(string input, string expected)
    {
        // Act
        var actual = input.NormalizeExternalIdentifierValue();

        // Assert
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("+123", "+123")]
    [TestCase("-123", "-123")]
    [TestCase("1,234", "1,234")]
    [TestCase("1.234", "1.234")]
    [TestCase("0x1F", "0X1F")]
    [TestCase("1 234", "1 234")]
    [TestCase("ab0123", "AB0123")]
    [TestCase("abcd-1", "ABCD-1")]
    [TestCase("  ab1a  ", "AB1A")]
    [TestCase("99999999999999999999", "99999999999999999999")]
    public void NormalizeExternalIdentifierValue_UppercasesTrimmedText_WhenValueIsNotAPlainInteger(
        string input,
        string expected)
    {
        // Act
        var actual = input.NormalizeExternalIdentifierValue();

        // Assert
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("Mixed CASE", "mixed case")]
    [TestCase("Crème Brûlée", "creme brulee")]
    [TestCase("Director's Cut", "directors cut")]
    [TestCase("Director’s Cut", "directors cut")]
    [TestCase("  one\t two\r\nthree  ", "one two three")]
    [TestCase(null, "")]
    [TestCase("", "")]
    public void NormalizeContentSearchPhrase_ReturnsExpectedPhrase_WhenInputVaries(string? phrase, string expected)
    {
        // Act
        var result = phrase.NormalizeContentSearchPhrase();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Mr. Robot", "Mr. Robot")]
    [TestCase("  Title: Name?  ", "Title Name")]
    [TestCase("a  \t b", "a b")]
    [TestCase("\u0001Name", "Name")]
    [TestCase("<>:\"/\\|?*", "")]
    [TestCase("Name...", "Name")]
    [TestCase("Name .", "Name ")]
    [TestCase("...", "")]
    [TestCase(" Name B ", "Name B")]
    [TestCase("", "")]
    public void NormalizeContentPathSegment_ReturnsWindowsSafeSegment_WhenInputVaries(string source, string expected)
    {
        // Act
        var result = source.NormalizeContentPathSegment();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Firefly", 2002, "Firefly (2002)")]
    [TestCase("What?", null, "What")]
    public void ToGeneratedContentPath_AppendsTheYear_WhenOneIsKnown(string title, int? year, string expected)
    {
        // Act
        var result = title.ToGeneratedContentPath(year);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }
}
