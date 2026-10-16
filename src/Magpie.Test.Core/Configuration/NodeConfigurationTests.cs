using System;

using Magpie.Core.Config;
using Magpie.Models.Enums;

using Microsoft.Extensions.Configuration;

using NUnit.Framework;

namespace Magpie.Test.Core.Configuration;

[TestFixture]
public sealed class NodeConfigurationTests
{
    private const string Prefix = "MAGPIE_NODE_CONFIGURATION_TESTS_";

    private IConfiguration _configuration = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Environment.SetEnvironmentVariable(Prefix + "Magpie__Cluster__Roles__0", "JobRunner");
        Environment.SetEnvironmentVariable(Prefix + "Jobs__JobTypes__0", "MediaInfoExtractItem");
        Environment.SetEnvironmentVariable(Prefix + "Jobs__ConcurrencyLimits__MediaInfoExtractItem", "2");
        _configuration = new ConfigurationBuilder().AddEnvironmentVariables(Prefix).Build();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Environment.SetEnvironmentVariable(Prefix + "Magpie__Cluster__Roles__0", null);
        Environment.SetEnvironmentVariable(Prefix + "Jobs__JobTypes__0", null);
        Environment.SetEnvironmentVariable(Prefix + "Jobs__ConcurrencyLimits__MediaInfoExtractItem", null);
    }

    [Test]
    public void Get_BindsTheRolesJobListAndConcurrencyLimits_WhenEnvironmentVariablesSetThem()
    {
        // Act
        var host = _configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()!;
        var jobs = _configuration.GetSection(JobRuntimeOptions.SectionName).Get<JobRuntimeOptions>()!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.Cluster.Roles, Is.EqualTo([NodeRole.JobRunner]));
            Assert.That(jobs.JobTypes, Is.EqualTo(["MediaInfoExtractItem"]));
            Assert.That(jobs.ConcurrencyLimits["MediaInfoExtractItem"], Is.EqualTo(2));
        }
    }
}
