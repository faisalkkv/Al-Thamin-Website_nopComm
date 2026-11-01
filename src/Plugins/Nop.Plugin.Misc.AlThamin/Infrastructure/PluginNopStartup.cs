using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.AlThamin.Services;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AlThamin.Infrastructure;

/// <summary>
/// Registers plugin services; runs after the core registrations so the legacy URL helper replaces the default one
/// </summary>
public class PluginNopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LegacyUrlService>();
        services.AddScoped<ContentImporter>();
        services.AddScoped<INopUrlHelper, LegacyNopUrlHelper>();
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 3000;
}

/// <summary>
/// Adds the legacy URL middleware after static files (99) and before routing (400)
/// </summary>
public class LegacyUrlStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void Configure(IApplicationBuilder application)
    {
        application.UseMiddleware<LegacyUrlMiddleware>();
    }

    public int Order => 350;
}

/// <summary>
/// Restores the legacy path once routing (400) has picked the endpoint, before authentication (500)
/// </summary>
public class LegacyPathRestoreStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void Configure(IApplicationBuilder application)
    {
        application.UseMiddleware<LegacyPathRestoreMiddleware>();
    }

    public int Order => 450;
}
