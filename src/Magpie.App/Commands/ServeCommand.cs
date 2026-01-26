using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Magpie.App.Commands;

internal sealed class ServeCommand : Command
{
    private static readonly Option<string> BindOption = new("--bind", "-b")
    {
        Description = "Bind address for the API server",
        DefaultValueFactory = _ => "0.0.0.0"
    };

    private static readonly Option<int> PortOption = new("--port", "-p")
    {
        Description = "Port for the API server",
        DefaultValueFactory = _ => 5000
    };

    private static readonly Option<string?> ConfigOption = new("--config", "-c")
    {
        Description = "Path to configuration file"
    };

    private static readonly Option<string> ProviderOption = new("--provider")
    {
        Description = "Database provider (sqlite, postgresql, mysql, sqlserver)",
        DefaultValueFactory = _ => "sqlite"
    };

    private static readonly Option<string> ConnectionOption = new("--connection")
    {
        Description = "Database connection string",
        Required = true
    };

    private static readonly Option<bool> DevModeOption = new("--dev-mode")
    {
        Description = "Enable development mode (bypasses authentication)"
    };

    public ServeCommand() : base("serve", "Start the Magpie API server")
    {
        Options.Add(BindOption);
        Options.Add(PortOption);
        Options.Add(ConfigOption);
        Options.Add(ProviderOption);
        Options.Add(ConnectionOption);
        Options.Add(DevModeOption);

        SetAction(ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var bind = parseResult.GetValue(BindOption)!;
        var port = parseResult.GetValue(PortOption);
        var provider = parseResult.GetValue(ProviderOption)!;
        var connectionString = parseResult.GetValue(ConnectionOption)!;
        var devMode = parseResult.GetValue(DevModeOption);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{bind}:{port}");

        if (!TryAddDatabaseProvider(builder.Services, provider, connectionString))
        {
            Console.Error.WriteLine($"Error: Unknown database provider '{provider}'.");
            Console.Error.WriteLine("Valid providers: sqlite, postgresql, mysql, sqlserver");
            return 1;
        }

        builder.Services.AddMagpieServices();
        builder.Services.AddMagpieApi();
        builder.Services.AddMagpieAuth();
        builder.Services.AddLocalAuthenticator();

        if (devMode)
        {
            builder.Services.AddNoAuthProvider();
        }
        else
        {
            builder.Services.AddBearerAuthProvider();
        }

        var app = builder.Build();

        app.UseMagpieAuth();
        app.UseMagpieApi();

        Console.WriteLine($"Starting Magpie API server on http://{bind}:{port}");

        if (devMode)
        {
            Console.WriteLine("WARNING: Development mode enabled - authentication bypassed");
        }

        await app.RunAsync().ConfigureAwait(false);

        return 0;
    }

    private static bool TryAddDatabaseProvider(IServiceCollection services, string provider, string connectionString)
    {
        switch (provider.ToLowerInvariant())
        {
            case "sqlite":
                services.AddSQLiteDatabase(connectionString);
                return true;
            case "postgresql":
                services.AddPostgreSQLDatabase(connectionString);
                return true;
            case "mysql":
                services.AddMySQLDatabase(connectionString);
                return true;
            case "sqlserver":
                services.AddSqlServerDatabase(connectionString);
                return true;
            default:
                return false;
        }
    }
}
