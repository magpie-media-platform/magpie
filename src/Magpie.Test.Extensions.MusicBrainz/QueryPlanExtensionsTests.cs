using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.QueryEngine;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class QueryPlanExtensionsTests
{
    private static readonly PlanPrecondition NoPrecondition = new() { Kind = PlanPreconditionKind.None };

    private static readonly FetchPlanNode Fetch = new()
    {
        Id = 7,
        Precondition = NoPrecondition,
        Contributes = "candidate rows",
        Target = QueryTarget.Artist,
        AccessMode = AccessMode.Lookup,
        AnchorKind = AnchorKind.Mbid,
        CombineMode = CombineMode.And,
        Filters = [],
        EntityKind = EntityKind.Artist,
        DirectValue = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
        LookupCapabilities = LookupCapability.UrlRelations
    };

    private static readonly QueryPlan Plan = new()
    {
        RequestedPage = 1,
        MaxResults = 25,
        Settings = new Settings(),
        Warnings = [],
        Fingerprint = "fp-trace",
        Root = Fetch
    };

    [Test]
    public void SerializePlan_RecordsLookupCapabilities_WhenTheFetchNodeRequestsThem()
    {
        // Arrange
        // Act
        var trace = Plan.SerializePlan();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(trace, Does.Contain("\"kind\": \"fetch\""));
            Assert.That(trace, Does.Contain("\"lookupCapabilities\": \"urlRelations\""));
        }
    }
}
