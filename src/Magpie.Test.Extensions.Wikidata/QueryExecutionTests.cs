using System.Linq;

using Magpie.Extensions.Wikidata.QueryEngine;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class QueryExecutionTests
{
    [Test]
    public void Skip_RetainsPrunedAccounting_WhenStageAlsoRan()
    {
        // Arrange
        var execution = new QueryExecution("mixed-stage");
        execution.Skip("mixed", "WDQS", "The first window was pruned.", avoidedLogicalInvocations: 1);
        using (var invocation = execution.Start("mixed", "WDQS"))
        {
            invocation.RecordRows(2, 1);
        }

        // Act
        execution.Skip("mixed", "WDQS", "A later window was pruned.", avoidedLogicalInvocations: 1);
        execution.Freeze(
            returnedCount: 1,
            hasMore: false,
            boundHit: false,
            terminationReason: "Complete.",
            warningCount: 0);
        var json = execution.SerializeExecution();

        // Assert
        var stage = execution.Stages.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stage.Status, Is.EqualTo("RAN"));
            Assert.That(stage.SkippedInvocations, Is.EqualTo(2));
            Assert.That(stage.AvoidedLogicalInvocations, Is.EqualTo(2));
            Assert.That(execution.PrunedStages, Is.EqualTo(1));
            Assert.That(execution.AvoidedLogicalInvocations, Is.EqualTo(2));
            Assert.That(json, Does.Contain("\"skippedInvocations\":2"));
            Assert.That(json, Does.Contain("\"prunedStages\":1"));
        }
    }

    [Test]
    public void Start_DiscardsLateMutation_WhenExecutionIsFrozen()
    {
        // Arrange
        var execution = new QueryExecution("frozen");
        using (var invocation = execution.Start("wdqsWindows", "WDQS"))
        {
            invocation.RecordHttpRequest();
        }

        execution.Freeze(
            returnedCount: 0,
            hasMore: false,
            boundHit: false,
            terminationReason: "The caller cancelled Wikidata execution.",
            warningCount: 0);

        // Act
        using (var late = execution.Start("lateMutation", "local"))
        {
            late.RecordHttpRequest();
            late.RecordRowsProduced(10);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsFrozen, Is.True);
            Assert.That(execution.Requests, Is.EqualTo(1));
            Assert.That(execution.Stages, Has.Count.EqualTo(1));
            Assert.That(execution.Stages[0].Name, Is.EqualTo("wdqsWindows"));
        }
    }

    [Test]
    public void RecordSparqlRequest_IsOwnedAndExcludedFromSerialization_WhenTwoExecutionsRecord()
    {
        // Arrange
        var owner = new QueryExecution("owner");
        using (var invocation = owner.Start(QueryDiagnostics.WdqsWindows, "WDQS"))
        {
            invocation.RecordSparqlRequest("SELECT owner-secret", captureTrace: true);
        }

        var joiner = new QueryExecution("joiner");
        using (var invocation = joiner.Start(QueryDiagnostics.WdqsWindows, "WDQS"))
        {
            invocation.RecordSparqlRequest("SELECT joiner-secret", captureTrace: true);
        }

        owner.Freeze(
            returnedCount: 0,
            hasMore: false,
            boundHit: false,
            terminationReason: "Complete.",
            warningCount: 0);
        joiner.Freeze(
            returnedCount: 0,
            hasMore: false,
            boundHit: false,
            terminationReason: "Complete.",
            warningCount: 0);

        // Act
        var ownerJson = owner.SerializeExecution();
        var joinerJson = joiner.SerializeExecution();
        var ownerRecords = owner.DrainSparqlTraceRecords();
        var joinerRecords = joiner.DrainSparqlTraceRecords();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.Requests, Is.EqualTo(1));
            Assert.That(joiner.Requests, Is.EqualTo(1));
            Assert.That(ownerRecords, Has.Count.EqualTo(1));
            Assert.That(ownerRecords[0].QueryText, Is.EqualTo("SELECT owner-secret"));
            Assert.That(joinerRecords, Has.Count.EqualTo(1));
            Assert.That(joinerRecords[0].QueryText, Is.EqualTo("SELECT joiner-secret"));
            Assert.That(ownerJson, Does.Not.Contain("owner-secret"));
            Assert.That(ownerJson, Does.Not.Contain("joiner-secret"));
            Assert.That(joinerJson, Does.Not.Contain("owner-secret"));
            Assert.That(joinerJson, Does.Not.Contain("joiner-secret"));
            Assert.That(ownerJson, Does.Not.Contain("sparqlTraceRecords"));
            Assert.That(joinerJson, Does.Not.Contain("sparqlTraceRecords"));
        }
    }
}
