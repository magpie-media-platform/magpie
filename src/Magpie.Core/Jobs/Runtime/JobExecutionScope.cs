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

internal sealed class JobExecutionScope(
    IServiceScopeFactory scopeFactory,
    IJobRegistry registry,
    ILoggerFactory loggerFactory) : IJobExecutionScope, IJobExecutionScopeFactory
{
    private readonly AsyncLocal<Guid?> _currentExecutionId = new();
    private readonly AsyncServiceScope? _scope;
    private Guid? _previousExecutionId;
    private bool _hasParentContext;

    private JobExecutionScope(
        IServiceScopeFactory scopeFactory,
        IJobRegistry registry,
        ILoggerFactory loggerFactory,
        AsyncServiceScope scope,
        AsyncLocal<Guid?> currentExecutionId)
        : this(scopeFactory, registry, loggerFactory)
    {
        _scope = scope;
        _currentExecutionId = currentExecutionId;
    }

    public Guid? CurrentExecutionId => _currentExecutionId.Value;

    public IJobExecutionScope CreateScope(Guid executionId)
    {
        return new JobExecutionScope(
            scopeFactory,
            registry,
            loggerFactory,
            scopeFactory.CreateAsyncScope(),
            _currentExecutionId);
    }

    public void SetParentContext(Guid executionId)
    {
        var currentExecutionId = _currentExecutionId.Value;
        if (currentExecutionId is not null)
        {
            loggerFactory.CreateLogger<JobExecutionScope>().LogWarning(
                "The job execution scope will replace ambient parent execution {CurrentExecutionId} " +
                "with {ExecutionId}",
                currentExecutionId,
                executionId);
        }

        if (!_hasParentContext)
        {
            _previousExecutionId = currentExecutionId;
            _hasParentContext = true;
        }

        _currentExecutionId.Value = executionId;
    }

    public bool TryGetHandler(JobSpec spec, [MaybeNullWhen(false)] out IJob handler)
    {
        if (!registry.TryGetHandlerType(spec, out var handlerType))
        {
            handler = null;
            return false;
        }

        handler = (IJob)_scope!.Value.ServiceProvider.GetRequiredService(handlerType);

        return true;
    }

    public ValueTask DisposeAsync()
    {
        if (_hasParentContext)
        {
            _currentExecutionId.Value = _previousExecutionId;
        }

        return _scope is { } scope ? scope.DisposeAsync() : ValueTask.CompletedTask;
    }
}
