using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Models;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class JobExecutionScope : IJobExecutionScope, IJobExecutionScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AsyncLocal<Guid?> _currentExecutionId;
    private readonly AsyncLocal<Guid?> _currentCorrelationId;
    private readonly AsyncServiceScope? _scope;
    private Guid? _previousExecutionId;
    private Guid? _previousCorrelationId;
    private bool _hasParentContext;

    public JobExecutionScope(
        IServiceScopeFactory scopeFactory,
        IJobRegistry registry,
        ILoggerFactory loggerFactory)
        : this(scopeFactory, registry, loggerFactory, scope: null, new(), new())
    {
    }

    private JobExecutionScope(
        IServiceScopeFactory scopeFactory,
        IJobRegistry registry,
        ILoggerFactory loggerFactory,
        AsyncServiceScope? scope,
        AsyncLocal<Guid?> currentExecutionId,
        AsyncLocal<Guid?> currentCorrelationId)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _loggerFactory = loggerFactory;
        _scope = scope;
        _currentExecutionId = currentExecutionId;
        _currentCorrelationId = currentCorrelationId;
    }

    public Guid? CurrentExecutionId => _currentExecutionId.Value;

    public Guid? CurrentCorrelationId => _currentCorrelationId.Value;

    public IJobExecutionScope CreateScope(Guid executionId)
    {
        var scope = new JobExecutionScope(
            _scopeFactory,
            _registry,
            _loggerFactory,
            _scopeFactory.CreateAsyncScope(),
            _currentExecutionId,
            _currentCorrelationId);

        scope.SetParentContext(executionId, _currentCorrelationId.Value);

        return scope;
    }

    public void SetParentContext(Guid executionId, Guid? correlationId)
    {
        var currentExecutionId = _currentExecutionId.Value;
        if (currentExecutionId is not null && currentExecutionId != executionId)
        {
            _loggerFactory.CreateLogger<JobExecutionScope>().LogWarning(
                "The job execution scope will replace ambient parent execution {CurrentExecutionId} " +
                "with {ExecutionId}",
                currentExecutionId,
                executionId);
        }

        if (!_hasParentContext)
        {
            _previousExecutionId = currentExecutionId;
            _previousCorrelationId = _currentCorrelationId.Value;
            _hasParentContext = true;
        }

        _currentExecutionId.Value = executionId;
        _currentCorrelationId.Value = correlationId;
    }

    public bool TryGetHandler(JobSpec spec, [MaybeNullWhen(false)] out IJob handler)
    {
        if (!_registry.TryGetHandlerType(spec, out var handlerType))
        {
            handler = null;
            return false;
        }

        handler = (IJob)_scope!.Value.ServiceProvider.GetRequiredService(handlerType);

        return true;
    }

    /// <inheritdoc />
    public Task FlushProgressAsync(CancellationToken cancellationToken = default)
    {
        return _scope?.ServiceProvider.GetService<IProgressReporter>()?.FlushAsync(cancellationToken)
            ?? Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_hasParentContext)
        {
            _currentExecutionId.Value = _previousExecutionId;
            _currentCorrelationId.Value = _previousCorrelationId;
        }

        return _scope is { } scope ? scope.DisposeAsync() : ValueTask.CompletedTask;
    }
}
