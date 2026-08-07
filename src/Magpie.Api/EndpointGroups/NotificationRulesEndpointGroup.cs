using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("notification-rules", "notification-rules", "Manage notification rules")]
internal sealed class NotificationRulesEndpointGroup : IApiEndpointGroup
{
    public string Name => "notification-rules";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetNotificationRulesAsync)
            .WithName(nameof(GetNotificationRulesAsync))
            .Produces<IReadOnlyList<NotificationRule>>()
            .RequireRequester();

        builder.MapPost("", CreateNotificationRuleAsync)
            .WithName(nameof(CreateNotificationRuleAsync))
            .Produces<NotificationRule>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("test", TestNotificationRuleSettingsAsync)
            .WithName(nameof(TestNotificationRuleSettingsAsync))
            .Produces<ConfigurationTestResult>()
            .RequireManager();

        builder.MapGet("{id:guid}", GetNotificationRuleAsync)
            .WithName(nameof(GetNotificationRuleAsync))
            .Produces<NotificationRule>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPatch("{id:guid}", PatchNotificationRuleAsync)
            .WithName(nameof(PatchNotificationRuleAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteNotificationRuleAsync)
            .WithName(nameof(DeleteNotificationRuleAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/test", TestNotificationRuleAsync)
            .WithName(nameof(TestNotificationRuleAsync))
            .Produces<ConfigurationTestResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();
    }

    [MagpieCommand("list", "GET", "", "List notification rules")]
    internal async Task<IResult> GetNotificationRulesAsync(
        [FromServices] INotificationService notificationService,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        var rules = await notificationService
            .ListAsync(user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        var redacted = await RedactSecretsAsync(notificationService, rules, cancellationToken).ConfigureAwait(false);

        return Results.Ok(redacted);
    }

    [MagpieCommand("create", "POST", "", "Create a notification rule")]
    internal async Task<IResult> CreateNotificationRuleAsync(
        [FromServices] INotificationService notificationService,
        AuthenticatedUser user,
        [FromBody] NotificationRuleCreate create,
        CancellationToken cancellationToken)
    {
        var owned = user.IsInRole(Role.Admin) ? create : create with { OwnerUserId = user.Id };
        var created = await notificationService.CreateAsync(owned, user.Id, cancellationToken)
            .ConfigureAwait(false);

        var response = await RedactSecretsAsync(notificationService, created, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", response);
    }

    [MagpieCommand("test-settings", "POST", "test", "Test unsaved notification rule settings")]
    internal async Task<IResult> TestNotificationRuleSettingsAsync(
        [FromServices] INotificationService notificationService,
        [FromBody] NotificationRuleCreate create,
        CancellationToken cancellationToken)
    {
        var result = await notificationService.TestSettingsAsync(create, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a notification rule")]
    internal async Task<IResult> GetNotificationRuleAsync(
        [FromServices] INotificationService notificationService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var rule = await notificationService.GetAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return rule is null
            ? Results.NotFound()
            : Results.Ok(await RedactSecretsAsync(notificationService, rule, cancellationToken).ConfigureAwait(false));
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a notification rule")]
    internal async Task<IResult> PatchNotificationRuleAsync(
        [FromServices] INotificationService notificationService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] NotificationRulePatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await notificationService
            .PatchAsync(id, patch, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a notification rule")]
    internal async Task<IResult> DeleteNotificationRuleAsync(
        [FromServices] INotificationService notificationService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await notificationService
            .DeleteAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("test", "POST", "{id:guid}/test", "Send a test notification for a rule")]
    internal async Task<IResult> TestNotificationRuleAsync(
        [FromServices] INotificationService notificationService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await notificationService
            .TestAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IReadOnlyList<NotificationRule>> RedactSecretsAsync(
        INotificationService notificationService,
        IReadOnlyList<NotificationRule> rules,
        CancellationToken cancellationToken)
    {
        var schemasByProviderType =
            new Dictionary<string, IReadOnlyList<SettingSchemaDefinition>>(StringComparer.OrdinalIgnoreCase);
        var redacted = new List<NotificationRule>(rules.Count);
        foreach (var rule in rules)
        {
            if (!schemasByProviderType.TryGetValue(rule.ProviderType, out var schema))
            {
                schema = await notificationService.GetSettingsSchemaAsync(rule.ProviderType, cancellationToken)
                    .ConfigureAwait(false);
                schemasByProviderType[rule.ProviderType] = schema;
            }

            redacted.Add(rule with { Settings = rule.Settings.RedactSecrets(schema) });
        }

        return redacted;
    }

    private static async Task<NotificationRule> RedactSecretsAsync(
        INotificationService notificationService,
        NotificationRule rule,
        CancellationToken cancellationToken)
    {
        var schema = await notificationService.GetSettingsSchemaAsync(rule.ProviderType, cancellationToken)
            .ConfigureAwait(false);

        return rule with { Settings = rule.Settings.RedactSecrets(schema) };
    }
}
