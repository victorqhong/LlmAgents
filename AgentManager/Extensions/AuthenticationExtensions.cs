using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AgentManager.Configuration;
using AgentManager.Entities;
using AgentManager.Services;
using LlmAgents.Api;
using LlmAgents.Api.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AgentManager.Extensions;

public static class AuthenticationExtensions
{
    public static void ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        var githubOauthClientId = builder.Configuration["Oauth:GitHubClientId"];
        var githubOauthClientSecret = builder.Configuration["Oauth:GitHubClientSecret"];

        ArgumentException.ThrowIfNullOrEmpty(githubOauthClientId);
        ArgumentException.ThrowIfNullOrEmpty(githubOauthClientSecret);

        var authOptions = new AuthorizationOptions();
        builder.Configuration.GetSection(AuthorizationOptions.SectionName).Bind(authOptions);
        builder.Services.AddSingleton(authOptions);

        var jwtOptions = new JwtOptions();
        builder.Configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);
        builder.Services.AddSingleton(jwtOptions);

        builder.Services.AddScoped<UserAuthorizationService>();
        builder.Services.AddScoped<RefreshTokenService>();

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = "GitHub";
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.AccessDeniedPath = "/access-denied";

                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
            })
            .AddGitHub(options =>
            {
                options.ClientId = githubOauthClientId;
                options.ClientSecret = githubOauthClientSecret;

                options.CallbackPath = "/signin-github";

                options.Scope.Add("user:email");
                options.Scope.Add("read:user");

                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;

                options.Events.OnCreatingTicket = async context =>
                {
                    var email = context.Identity?.FindFirst(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
                    if (!string.IsNullOrEmpty(email))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email));
                    }

                    var login = context.Identity?.FindFirst(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value;
                    if (!string.IsNullOrEmpty(login))
                    {
                        context.Identity?.AddClaim(new Claim(ClaimTypes.Name, login));

                        var authService = context.HttpContext.RequestServices.GetRequiredService<UserAuthorizationService>();

                        if (!await authService.IsUserAllowedAsync(login))
                        {
                            context.Fail("User is not authorized to access this application");
                            return;
                        }

                        var roles = await authService.GetUserRolesAsync(login);
                        foreach (var role in roles)
                        {
                            context.Identity?.AddClaim(new Claim(ClaimTypes.Role, role));
                        }

                        await authService.GetOrCreateUserAsync(login, email ?? string.Empty);
                    }
                };

                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/access-denied?error=authentication_failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs/agent"))
                        {
                            ctx.Token = token;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy => 
                policy.RequireRole(UserRoles.Admin))
            .AddPolicy("UserAccess", policy => 
                policy.RequireRole(UserRoles.Admin, UserRoles.User))
            .AddPolicy("ReadOnlyAccess", policy => 
                policy.RequireRole(UserRoles.Admin, UserRoles.User, UserRoles.ReadOnly));
    }

    public static void ConfigureAuthentication(this WebApplication app)
    {
        app.MapGet("/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, ["GitHub"]));
        app.MapGet("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        });

        app.MapPost("/auth/github", async (HttpContext httpContext) =>
        {
            var tokenRequest = await httpContext.Request.ReadFromJsonAsync<TokenRequest>();
            var accessToken = tokenRequest?.AccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                return Results.BadRequest();
            }

            var gitHubUser = await GitHubApi.GetUserAsync(accessToken);
            if (gitHubUser == null)
            {
                return Results.BadRequest();
            }

            var userAuthorizationService = httpContext.RequestServices.GetRequiredService<UserAuthorizationService>();

            var user = await userAuthorizationService.GetOrCreateUserAsync(gitHubUser.login, string.Empty);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var jwtOptions = httpContext.RequestServices.GetRequiredService<JwtOptions>();
            var jwt = CreateJwt(user, jwtOptions);

            var refreshTokenService = httpContext.RequestServices.GetRequiredService<RefreshTokenService>();
            var refreshToken = await refreshTokenService.CreateAsync(user);

            var token = new HubAuthToken
            {
                AccessToken = jwt,
                RefreshToken = refreshToken,
                ExpiresIn = jwtOptions.ExpirationMinutes,
                ExpireTime = DateTime.UtcNow.AddMinutes(jwtOptions.ExpirationMinutes)
            };

            return Results.Ok(token);
        });
        app.MapPost("/auth/refresh", async (HttpContext httpContext) =>
        {
            var refreshRequest = await httpContext.Request.ReadFromJsonAsync<RefreshRequest>();
            var refreshToken = refreshRequest?.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Results.BadRequest();
            }

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

            var refreshTokenService = httpContext.RequestServices.GetRequiredService<RefreshTokenService>();
            var token = await refreshTokenService.FindAsync(hash);
            if (token == null || token.User == null)
            {
                return Results.BadRequest();
            }

            if (token.ExpiresAt < DateTime.Now || token.RevokedAt < DateTime.Now)
            {
                return Results.Unauthorized();
            }

            await refreshTokenService.RevokeAsync(token);
            var newRefreshToken = await refreshTokenService.CreateAsync(token.User);

            var jwtOptions = httpContext.RequestServices.GetRequiredService<JwtOptions>();
            var jwt = CreateJwt(token.User, jwtOptions);

            var authToken = new HubAuthToken
            {
                AccessToken = jwt,
                RefreshToken = newRefreshToken,
                ExpiresIn = jwtOptions.ExpirationMinutes,
                ExpireTime = DateTime.UtcNow.AddMinutes(jwtOptions.ExpirationMinutes)
            };

            return Results.Ok(authToken);
        });
    }

    public static string CreateJwt(UserEntity user, JwtOptions options)
    {
        var claims = new List<Claim>()
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.GitHubLogin),
            new("github_id", user.GitHubLogin.ToString())
        };

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.ExpirationMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
