using System.Net;
using AgentManager.Data;
using AgentManager.Extensions;
using AgentManager.Hubs;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite("Data Source=agents.db"));

var proxyServerIp = builder.Configuration["Proxy:ServerIp"];
if (!string.IsNullOrEmpty(proxyServerIp) && IPAddress.TryParse(proxyServerIp, out var ipAddress))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(ipAddress);
    });
}

builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 1 * 1024 * 1024;
});

builder.ConfigureBlazor();
builder.ConfigureLxcClient();
builder.ConfigureProvisioning();
builder.ConfigureGogs();
builder.ConfigureAuthentication();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.ConfigureBlazor();
app.ConfigureAuthentication();

app.MapHub<AgentHub>("/hubs/agent");

app.Run();

