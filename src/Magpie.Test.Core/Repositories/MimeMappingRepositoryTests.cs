using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class MimeMappingRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMimeMappingRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new MimeMappingRepository(
            _context,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            eventBus.Object,
            NullLogger<MimeMappingRepository>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task CreateUpdateDeleteAsync_PublishesLifecycleEvents_WhenWritesSucceed()
    {
        // Arrange
        var mapping = new MimeMapping
        {
            Extension = ".lifecycle",
            MimeType = "application/x-before"
        };

        // Act
        await _subject.CreateAsync(mapping, createdByUserId: null).ConfigureAwait(false);
        mapping.MimeType = "application/x-after";
        await _subject.UpdateAsync(mapping, modifiedByUserId: null).ConfigureAwait(false);
        await _subject.DeleteAsync(mapping.Extension, deletedByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _publishedEvents.Select(magpieEvent => magpieEvent.Name),
                Is.EqualTo(
                [
                    MagpieEvents.Names.MimeMappingCreated,
                    MagpieEvents.Names.MimeMappingUpdated,
                    MagpieEvents.Names.MimeMappingDeleted
                ]));
            Assert.That(
                _publishedEvents,
                Has.All.Matches<MagpieEvent>(magpieEvent =>
                    magpieEvent.Context.Values.Contains(mapping.Extension, StringComparer.Ordinal)));
            Assert.That(_publishedEvents[1].Context.Values, Does.Contain("mimeType"));
        }
    }
}
