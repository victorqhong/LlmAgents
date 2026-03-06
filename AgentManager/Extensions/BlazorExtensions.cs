using AgentManager.Components;
using AgentManager.Services;
using AgentManager.Services.Containers;
using MudBlazor.Services;

namespace AgentManager.Extensions;

public static class BlazorExtensions
{
    public static void ConfigureBlazor(this WebApplicationBuilder builder)
    {
        builder.Services.AddMudServices();
        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddScoped<AgentSessionService>();
        builder.Services.AddScoped<AgentLogService>();
        builder.Services.AddScoped<AgentMessageService>();
        builder.Services.AddScoped<ContainerService>();
        builder.Services.AddScoped<WebsocketManager>();
    }

    public static void ConfigureBlazor(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
