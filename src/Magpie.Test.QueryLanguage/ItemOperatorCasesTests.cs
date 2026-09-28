using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Magpie.Models.Enums;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public sealed class ItemOperatorCasesTests
{
    private static readonly Dictionary<string, CriteriaValueOperator> OperatorTokens = new()
    {
        ["="] = CriteriaValueOperator.Equals,
        ["!="] = CriteriaValueOperator.NotEquals,
        [">"] = CriteriaValueOperator.GreaterThan,
        [">="] = CriteriaValueOperator.GreaterThanOrEqual,
        ["<"] = CriteriaValueOperator.LessThan,
        ["<="] = CriteriaValueOperator.LessThanOrEqual,
        ["contains"] = CriteriaValueOperator.ContainsString,
        ["!contains"] = CriteriaValueOperator.NotContainsString,
        ["startswith"] = CriteriaValueOperator.StartsWithString,
        ["!startswith"] = CriteriaValueOperator.NotStartsWithString,
        ["endswith"] = CriteriaValueOperator.EndsWithString,
        ["!endswith"] = CriteriaValueOperator.NotEndsWithString,
        ["null"] = CriteriaValueOperator.IsNull,
        ["!null"] = CriteriaValueOperator.IsNotNull,
    };

    private static readonly IReadOnlyList<ItemOperatorCase> Cases = JsonSerializer.Deserialize<List<ItemOperatorCase>>(
        new StreamReader(typeof(ItemOperatorCasesTests).Assembly.GetManifestResourceStream("item-operator-cases.json")!)
            .ReadToEnd(),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static IEnumerable<TestCaseData> Queries() =>
        Cases.Select(itemCase => new TestCaseData(itemCase.Query).SetArgDisplayNames(itemCase.Query));

    [TestCaseSource(nameof(Queries))]
    public void ParseContentFilter_Succeeds_WhenTheEditorItemOperatorCaseIsParsed(string query)
    {
        // Act
        var parsed = query.ParseContentFilter();

        // Assert
        Assert.That(parsed.IsSuccess, Is.True, query);
    }

    [Test]
    public void ItemOperatorCases_CoverEveryBackendItemOperator_WhenGroupedByItemValueType()
    {
        // Arrange
        var backend = FilterFieldRegistryExtensions.ContentCollectionFieldDefinitions
            .SelectMany(definition => definition.ItemFields.Values)
            .SelectMany(field => field.SupportedOperators.Select(op => (field.ValueType.Name, op)))
            .ToHashSet();

        // Act
        var covered = Cases.Select(itemCase => (itemCase.Type, OperatorTokens[itemCase.Operator])).ToHashSet();

        // Assert
        Assert.That(covered, Is.EquivalentTo(backend));
    }

    private sealed record ItemOperatorCase(string Type, string Operator, string Query);
}
