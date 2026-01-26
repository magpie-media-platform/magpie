#pragma warning disable IDE0130
namespace Microsoft.AspNetCore.Builder;
#pragma warning restore IDE0130

/// <summary>
/// Application builder extensions for Magpie authentication middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Magpie authentication and authorization middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The web application.</param>
    public static WebApplication UseMagpieAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
