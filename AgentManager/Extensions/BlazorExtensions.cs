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

        builder.Services.AddSingleton<AgentSessionService>();
        builder.Services.AddSingleton<AgentLogService>();
        builder.Services.AddSingleton<AgentMessageService>();
        builder.Services.AddSingleton<ContainerService>();
        builder.Services.AddSingleton<WebsocketManager>();
    }

    public static void ConfigureBlazor(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
