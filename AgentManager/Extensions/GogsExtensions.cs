using AgentManager.Configuration;
using AgentManager.Services;

namespace AgentManager.Extensions;

public static class GogsExtensions
{
    public static void ConfigureGogs(this WebApplicationBuilder builder)
    {
        var accessToken = builder.Configuration["Gogs:AccessToken"];
        var baseAddress = builder.Configuration["Gogs:BaseAddress"];

        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        ArgumentException.ThrowIfNullOrEmpty(baseAddress);

        var gogsOptions = new GogsOptions
        {
            BaseAddress = baseAddress,
            AccessToken = accessToken
        };

        builder.Services.AddSingleton(gogsOptions);

        builder.Services.AddHttpClient<GogsService.GogsHttpClient>(client =>
        {
            client.BaseAddress = new Uri(gogsOptions.BaseAddress);
            client.DefaultRequestHeaders.Add("Authorization", $"token {accessToken}");
        });

        builder.Services.AddSingleton<GogsService>();
    }
}

