using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Abstractions.System;
using Magpie.Api.Logging;
using Magpie.Data;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.EndpointGroups;

internal sealed class SystemEndpointGroup(
    IMagpieSystem magpieSystem,
    IRootFolderRepository rootFolderRepository,
    IScheduleTriggerRepository scheduleTriggerRepository,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    MagpieDbContext dbContext,
    IDatabaseManagementProvider databaseManagementProvider,
    LogBuffer logBuffer,
    ILogger<SystemEndpointGroup> logger) : IApiEndpointGroup
{
    private sealed record CreateSetupAdminRequest
    {
        public required string Username { get; init; }
        public required string Password { get; init; }
    }

    public string Name => "system";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("health", GetSystemHealth)
            .WithName(nameof(GetSystemHealth))
            .Produces<HealthResponse>()
            .AllowAnonymous();

        builder.MapGet("health/database", GetDatabaseHealth)
            .WithName(nameof(GetDatabaseHealth))
            .Produces<ServiceHealthResponse>()
            .RequireViewer();

        builder.MapGet("health/scheduler", GetSchedulerHealth)
            .WithName(nameof(GetSchedulerHealth))
            .Produces<ServiceHealthResponse>()
            .RequireViewer();

        builder.MapGet("info", GetSystemInfo)
            .WithName(nameof(GetSystemInfo))
            .Produces<SystemInfo>()
            .RequireViewer();

        builder.MapGet("disk-space", GetDiskSpace)
            .WithName(nameof(GetDiskSpace))
            .Produces<DiskSpaceInfo[]>()
            .RequireViewer();

        builder.MapGet("logs", GetLogs)
            .WithName(nameof(GetLogs))
            .Produces<LogEntry[]>()
            .RequireViewer();

        builder.MapGet("setup/status", GetSetupStatus)
            .WithName(nameof(GetSetupStatus))
            .Produces<SetupStatusResponse>()
            .AllowAnonymous();

        builder.MapPost("setup/admin", CreateSetupAdmin)
            .WithName(nameof(CreateSetupAdmin))
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AllowAnonymous();
    }

    private static IResult GetSystemHealth()
    {
        return Results.Ok(new HealthResponse
        {
            Status = "Healthy"
        });
    }

    private async Task<IResult> GetDatabaseHealth(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (canConnect)
            {
                return Results.Ok(new ServiceHealthResponse
                {
                    Name = databaseManagementProvider.Provider,
                    Status = "healthy",
                    Message = "Database connection successful",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
            else
            {
                return Results.Ok(new ServiceHealthResponse
                {
                    Name = databaseManagementProvider.Provider,
                    Status = "unhealthy",
                    Message = "Database connection failed",
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Results.Ok(new ServiceHealthResponse
            {
                Name = databaseManagementProvider.Provider,
                Status = "unhealthy",
                Message = ex.Message,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
    }

    private async Task<IResult> GetSchedulerHealth(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var triggers = await scheduleTriggerRepository.ListDueTriggersAsync(DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();

            var triggerCount = triggers.Count();
            return Results.Ok(new ServiceHealthResponse
            {
                Name = "Scheduler",
                Status = "healthy",
                Message = $"Scheduler infrastructure accessible, {triggerCount} due trigger(s)",
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Results.Ok(new ServiceHealthResponse
            {
                Name = "Scheduler",
                Status = "unhealthy",
                Message = ex.Message,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
    }

    private IResult GetSystemInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";

        return Results.Ok(new SystemInfo()
        {
            Version = version,
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SystemRoot = magpieSystem.SystemRoot,
            NodeId = magpieSystem.NodeId,
            StartedAt = magpieSystem.StartedAt
        });
    }

    private async Task<IResult> GetDiskSpace(CancellationToken cancellationToken)
    {
        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var diskSpaceInfos = new List<DiskSpaceInfo>();

        foreach (var folder in rootFolders)
        {
            try
            {
                var driveInfo = GetDriveInfoForPath(folder.Path);
                if (driveInfo is not null)
                {
                    diskSpaceInfos.Add(new DiskSpaceInfo
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Path = folder.Path,
                        TotalBytes = driveInfo.TotalSize,
                        FreeBytes = driveInfo.AvailableFreeSpace,
                        UsedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace
                    });
                }
                else
                {
                    diskSpaceInfos.Add(new DiskSpaceInfo
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Path = folder.Path,
                        TotalBytes = 0,
                        FreeBytes = 0,
                        UsedBytes = 0
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get disk space for path {Path}", folder.Path);
                diskSpaceInfos.Add(new DiskSpaceInfo
                {
                    Id = folder.Id,
                    Name = folder.Name,
                    Path = folder.Path,
                    TotalBytes = 0,
                    FreeBytes = 0,
                    UsedBytes = 0
                });
            }
        }

        return Results.Ok(diskSpaceInfos.ToArray());
    }

    private IResult GetLogs()
    {
        return Results.Ok(logBuffer.GetEntries());
    }

    private async Task<IResult> GetSetupStatus(CancellationToken cancellationToken)
    {
        var result = await userRepository.QueryAsync(
            new UserQuery { Role = Role.Admin },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);

        var setupRequired = result.TotalItems == 0;
        return Results.Ok(new SetupStatusResponse
        {
            IsRequired = setupRequired,
            IsComplete = !setupRequired
        });
    }

    private async Task<IResult> CreateSetupAdmin(
        CreateSetupAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Trim().Length < 3)
        {
            return Results.Problem(
                detail: "Username must be at least 3 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return Results.Problem(
                detail: "Password must be at least 8 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await userRepository.QueryAsync(
            new UserQuery { Role = Role.Admin },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);

        if (existing.TotalItems > 0)
        {
            return Results.Problem(
                detail: "Setup is already complete. An admin account already exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var hashedPassword = await passwordHasher.HashPasswordAsync(request.Password, cancellationToken)
            .ConfigureAwait(false);

        var user = new User
        {
            Username = request.Username.Trim(),
            PasswordHash = hashedPassword.Hash,
            PasswordSalt = hashedPassword.Salt,
            Role = Role.Admin,
            ProviderType = "local",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await userRepository.CreateAsync(user, null, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Setup admin account created for user {Username}", created.Username);

        return Results.Created($"api/users/{created.Id}", created.ToResponse());
    }

    private static DriveInfo? GetDriveInfoForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady &&
                    string.Equals(d.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
