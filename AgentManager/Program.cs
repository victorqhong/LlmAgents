using AgentManager.Data;
using AgentManager.Extensions;
using AgentManager.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite("Data Source=agents.db"));
builder.Services.AddSignalR();

builder.ConfigureBlazor();
builder.ConfigureLxcClient();
builder.ConfigureAuthentication();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.ConfigureBlazor();
app.ConfigureAuthentication();

app.MapHub<AgentHub>("/hubs/agent");

app.Run();

