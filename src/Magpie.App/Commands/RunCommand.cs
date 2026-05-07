using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Security;
using Magpie.Api;
using Magpie.App.Extensions;
using Magpie.Auth;
using Magpie.Data;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.App.Commands;

/// <summary>
/// Command that boots the Magpie host as an HTTP server, loads extensions, applies the
/// database schema, maps every API endpoint group, and runs until cancelled.
/// </summary>
internal sealed class RunCommand : Command
{
    public RunCommand() : base("run", "Run the Magpie HTTP host with extensions loaded")
    {
        SetAction(ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddMagpie();

        builder.AddMagpieExtensions();

        var app = builder.Build();

        await EnsureDatabaseAsync(app, cancellationToken).ConfigureAwait(false);

        if (app.Environment.IsDevelopment())
        {
            // Vite dev server runs on http://localhost:5173 by default. Allow the SPA to
            // call the API directly during local development.
            app.UseCors(policy => policy
                .WithOrigins("http://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());

            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // IUserManager is contributed by extensions. Without one, the Magpie scheme handler
        // ctor would throw on every authenticated request, so we drop the scheme entirely
        // and let authorization reject those routes.
        var userManager = app.Services.GetService<IUserManager>();
        if (userManager is null)
        {
            var schemeProvider = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
            schemeProvider.RemoveScheme(MagpieAuthSchemes.Magpie);
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Magpie.App.RunCommand");
            logger.LogWarning(
                "No IUserManager registration found; the '{Scheme}' authentication scheme has been disabled. "
                + "Authenticated endpoints will reject requests until an extension provides IUserManager.",
                MagpieAuthSchemes.Magpie);
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMagpieEndpoints();

        await app.RunAsync(cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private static async Task EnsureDatabaseAsync(WebApplication app, CancellationToken cancellationToken)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Magpie.App.RunCommand");
        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MagpieDbContext>();
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Database schema verified.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize the Magpie database.");
            throw;
        }
    }
}
