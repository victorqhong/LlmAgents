using AgentManager.Components;
using AgentManager.Data;
using AgentManager.Hubs;
using AgentManager.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMudServices();

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentSessionService>();
builder.Services.AddSingleton<AgentLogService>();
builder.Services.AddSingleton<AgentMessageService>();
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite("Data Source=agents.db"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<AgentHub>("/agentHub");
app.Run();
