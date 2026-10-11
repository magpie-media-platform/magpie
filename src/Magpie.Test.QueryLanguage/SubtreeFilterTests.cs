using System.Linq;

using Magpie.Models.Enums;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public sealed class SubtreeFilterTests
{
    [Test]
    public void ParseContentFilter_PutsConditionsOnOneItem_WhenOneHasExpressionJoinsThemWithAnd()
    {
        // Arrange
        const string query = "subtree has (monitoring != Unmonitored and fulfillment = Missing)";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var subtree = result.Value!.ContentHierarchyItem.And.Single().Subtree;
        var item = subtree.Some.Single().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.Monitoring.And.Single().Operator, Is.EqualTo(CriteriaValueOperator.NotEquals));
            Assert.That(item.Monitoring.And.Single().Value, Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(item.Fulfillment.And.Single().Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(item.Fulfillment.And.Single().Value, Is.EqualTo(FulfillmentStatus.Missing));
            Assert.That(subtree.None, Is.Empty);
        }
    }

    [Test]
    public void ParseContentFilter_AddsAlternatives_WhenHasExpressionsAreJoinedByOr()
    {
        // Arrange
        const string query =
            "Fulfillment in (Missing, Partial) and " +
            "(subtree has (monitoring != Unmonitored and fulfillment = Missing) " +
            "or subtree has (monitoring != Unmonitored and fulfillment = Partial))";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var criteria = result.Value!.ContentHierarchyItem.And.Single();
        var group = criteria.Subtree.Some.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(criteria.Fulfillment.And.Single().Operator, Is.EqualTo(CriteriaValueOperator.In));
            Assert.That(
                group.Select(item => item.Fulfillment.And.Single().Value),
                Is.EqualTo([FulfillmentStatus.Missing, FulfillmentStatus.Partial]));
            Assert.That(
                group.Select(item => item.Monitoring.And.Single().Value),
                Is.EqualTo([MonitoringMode.Unmonitored, MonitoringMode.Unmonitored]));
        }
    }

    [Test]
    public void ParseContentFilter_AddsNoneCriteria_WhenNotHasIsUsed()
    {
        // Arrange
        const string query = "subtree !has (monitoring = Monitored)";

        // Act
        var result = query.ParseContentFilter();

        // Assert
        var subtree = result.Value!.ContentHierarchyItem.And.Single().Subtree;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subtree.Some, Is.Empty);
            Assert.That(subtree.None.Single().Monitoring.And.Single().Value, Is.EqualTo(MonitoringMode.Monitored));
        }
    }

    [TestCase("subtree has (fulfillment > Missing)")]
    [TestCase("subtree has (title = \"Dune\")")]
    public void ParseContentFilter_Fails_WhenTheSubtreeConditionIsNotSupported(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase(
        "subtree has (monitoring != Unmonitored and fulfillment = Missing)",
        "Subtree has (Monitoring != Unmonitored and Fulfillment = Missing)")]
    [TestCase(
        "subtree has (fulfillment = Missing) or subtree has (fulfillment = Partial)",
        "Subtree has (Fulfillment = Missing) or Subtree has (Fulfillment = Partial)")]
    [TestCase("subtree !has (monitoring = Monitored)", "Subtree !has (Monitoring = Monitored)")]
    public void FormatContentFilter_WritesTheSubtreeConditions_WhenTheCriteriaHaveSubtreeConditions(
        string query,
        string expected)
    {
        // Arrange
        var criteria = query.ParseContentFilter().Value!;

        // Act
        var formatted = criteria.FormatContentFilter();

        // Assert
        Assert.That(formatted, Is.EqualTo(expected));
    }
}
