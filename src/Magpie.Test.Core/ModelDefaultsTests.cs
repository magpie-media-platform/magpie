using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;

using Magpie.Sdk.Auth;
using Magpie.Sdk.Diagnostics;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class ModelDefaultsTests
{
    [Test]
    public void NotSupported_PreservesDefaults_WhenAPreviousMetadataSearchPageIsModified()
    {
        // Arrange
        var page = MetadataSearchPage.NotSupported;

        // Act
        page.Supported = true;
        page.PageInfo.HasMore = true;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MetadataSearchPage.NotSupported.Supported, Is.False);
            Assert.That(MetadataSearchPage.NotSupported.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public void Unsupported_PreservesDefaults_WhenAPreviousProviderSearchPageInfoIsModified()
    {
        // Arrange
        var paging = ProviderSearchPageInfo.Unsupported;

        // Act
        paging.RequestedPage = 2;

        // Assert
        Assert.That(ProviderSearchPageInfo.Unsupported.RequestedPage, Is.EqualTo(1));
    }

    [Test]
    public void None_PreservesDefaults_WhenAPreviousAuthenticationAttemptIsModified()
    {
        // Arrange
        var authentication = AuthenticationAttempt.None;

        // Act
        authentication.RejectionReason = "Rejected";

        // Assert
        Assert.That(AuthenticationAttempt.None.RejectionReason, Is.Null);
    }

    [Test]
    public void Empty_PreservesDefaults_WhenAPreviousCandidateSearchResultIsModified()
    {
        // Arrange
        var empty = ProviderMetadataCandidateSearchResult.Empty;

        // Act
        empty.PageInfo.HasMore = true;

        // Assert
        Assert.That(ProviderMetadataCandidateSearchResult.Empty.PageInfo.HasMore, Is.False);
    }

    [Test]
    public void Empty_PreservesDefaults_WhenAPreviousDiagnosticContextIsModified()
    {
        // Arrange
        var diagnostics = DiagnosticContext.Empty;

        // Act
        diagnostics.MediaServers = [new MediaServer { Name = "Server", ProviderType = "Test" }];

        // Assert
        Assert.That(DiagnosticContext.Empty.MediaServers, Is.Empty);
    }
}
