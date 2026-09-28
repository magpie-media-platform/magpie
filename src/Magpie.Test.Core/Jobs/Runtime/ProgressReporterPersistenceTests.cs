using System;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Config;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class ProgressReporterPersistenceTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IJobExecutionRepository _executions = null!;
    private ProgressReporter _subject = null!;
    private ServiceProvider _services = null!;
    private Guid _executionId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        var execution = new JobExecution { JobType = "progress-fixture", Status = JobExecutionStatus.Running };
        _context.JobExecutions.Add(execution);
        _context.SaveChanges();
        _executionId = execution.Id;
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var factory = new Mock<IJobExecutionScopeFactory>();
        factory.SetupGet(scope => scope.CurrentExecutionId).Returns(_executionId);
        var audit = new AuditRecordRepository(_context, Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance, transactions, factory.Object, TimeProvider.System);
        _executions = new JobExecutionRepository(
            _context,
            audit,
            transactions,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            TimeProvider.System,
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        _services = new ServiceCollection().AddSingleton(_executions).BuildServiceProvider();
        _subject = new ProgressReporter(factory.Object, _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProgressReporter>.Instance, new JobRuntimeOptions());
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subject.DisposeAsync().ConfigureAwait(false);
        await _services.DisposeAsync().ConfigureAwait(false);
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task FlushAsync_PreservesTheClosingMessage_WhenAReportArrivesAfterCompletion()
    {
        // Arrange
        _subject.Report(0.5, "working");
        _subject.Report(1, "finished");

        // Act
        await _subject.FlushAsync().ConfigureAwait(false);
        var completed = await _executions.TryMarkRunningCompletedAsync(_executionId, default).ConfigureAwait(false);
        _subject.Report(0.25, "too late");
        await _subject.FlushAsync().ConfigureAwait(false);
        var persisted = await _executions.GetByIdAsync(_executionId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(completed, Is.True);
            Assert.That(persisted!.Status, Is.EqualTo(JobExecutionStatus.Completed));
            Assert.That(persisted.StatusMessage, Is.EqualTo("finished"));
            Assert.That(persisted.Progress, Is.EqualTo(100));
        }
    }
}
