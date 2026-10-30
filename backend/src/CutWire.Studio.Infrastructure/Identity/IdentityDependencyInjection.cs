using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AspNet.Security.OAuth.Discord;
using AspNet.Security.OAuth.GitHub;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Infrastructure.Email;
using CutWire.Studio.Infrastructure.Imaging;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Oidc;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Infrastructure.Identity;

public static class IdentityDependencyInjection
{
    public static IServiceCollection AddStudioIdentity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();

        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.Section));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));
        services.AddHttpContextAccessor();
        services.AddSingleton<PublicUrl>();
        services.AddSingleton<IImageInspector, SkiaImageInspector>();
        services.AddSingleton<IEmailSender>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<EmailOptions>>().Value.Smtp.Host)
                ? ActivatorUtilities.CreateInstance<FileEmailSender>(sp)
                : ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp));

        if (!string.IsNullOrWhiteSpace(auth.DataProtectionKeysPath))
            services.AddDataProtection()
                .SetApplicationName("cutwire-studio")
                .PersistKeysToFileSystem(new DirectoryInfo(auth.DataProtectionKeysPath));

        var secureCookies = !environment.IsDevelopment();

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                o.ClaimsIdentity.UserIdClaimType = Claims.Subject;
                o.ClaimsIdentity.UserNameClaimType = Claims.Name;
                o.ClaimsIdentity.RoleClaimType = Claims.Role;
                o.ClaimsIdentity.EmailClaimType = Claims.Email;
            })
            .AddEntityFrameworkStores<StudioDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager<StudioSignInManager>();

        services.AddOptions<IdentityPasskeyOptions>().Configure<PublicUrl, IOptions<AuthOptions>>((o, url, authOptions) =>
        {
            o.ServerDomain = new Uri(url.Base).Host;
            var allowed = new HashSet<string>([url.Origin, .. authOptions.Value.AllowedOrigins], StringComparer.OrdinalIgnoreCase);
            o.ValidateOrigin = ctx => ValueTask.FromResult(ctx.Origin is not null && allowed.Contains(ctx.Origin));
        });

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = secureCookies ? "__Host-studio" : "studio";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/admin"))
                {
                    var back = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString.Value);
                    ctx.Response.Redirect("/account/sign-in?return=" + back);
                    return Task.CompletedTask;
                }

                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                if (!ctx.Request.Path.StartsWithSegments("/admin")) return Task.CompletedTask;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                return ctx.Response.WriteAsync(
                    "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Forbidden</title>" +
                    "<link rel=\"stylesheet\" href=\"/admin/admin.css\"></head><body class=\"denied\"><main>" +
                    "<h1>403</h1><p>You do not have access to this page.</p><p><a href=\"/admin\">Back to the dashboard</a></p></main></body></html>");
            };
        });
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddScoped<BootstrapAdmin>();
        services.AddScoped<AccountService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<PasskeyService>();
        services.AddScoped<ExternalLoginService>();
        services.AddScoped<ConnectedAppService>();
        services.AddScoped<AuthSeeder>();

        AddExternalProviders(services, auth.External);
        AddOpenIddict(services, auth.OpenIddict, environment);

        services.AddScoped<DynamicClientRegistration>();
        services.AddScoped<ConsentService>();
        services.AddScoped<OidcPrincipalFactory>();
        services.AddSingleton<IRecurringJob, DynamicClientCleanupJob>();

        return services;
    }

    private static void AddExternalProviders(IServiceCollection services, ExternalOptions external)
    {
        var builder = services.AddAuthentication();

        if (external.GitHub.Enabled)
            builder.AddGitHub(o =>
            {
                o.ClientId = external.GitHub.ClientId!;
                o.ClientSecret = external.GitHub.ClientSecret ?? "";
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Scope.Add("user:email");
                o.Events.OnCreatingTicket = async ctx =>
                {
                    var identity = ctx.Identity;
                    if (identity is null) return;
                    foreach (var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Email).ToList())
                        identity.RemoveClaim(claim);

                    using var request = new HttpRequestMessage(HttpMethod.Get, ((GitHubAuthenticationOptions)ctx.Options).UserEmailsEndpoint);
                    request.Headers.Authorization = new("Bearer", ctx.AccessToken);
                    request.Headers.Accept.ParseAdd("application/json");
                    request.Headers.UserAgent.ParseAdd("CutWire-Studio");
                    using var response = await ctx.Backchannel.SendAsync(request, ctx.HttpContext.RequestAborted);
                    if (!response.IsSuccessStatusCode) return;

                    using var doc = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(ctx.HttpContext.RequestAborted),
                        cancellationToken: ctx.HttpContext.RequestAborted);
                    foreach (var entry in doc.RootElement.EnumerateArray())
                    {
                        if (entry.TryGetProperty("primary", out var primary) && primary.GetBoolean() &&
                            entry.TryGetProperty("verified", out var verified) && verified.GetBoolean() &&
                            entry.TryGetProperty("email", out var address) && address.GetString() is { Length: > 0 } value)
                        {
                            identity.AddClaim(new(System.Security.Claims.ClaimTypes.Email, value));
                            identity.AddClaim(new(ExternalLoginService.EmailVerifiedClaim, "true"));
                            return;
                        }
                    }
                };
            });

        if (external.Google.Enabled)
            builder.AddGoogle(o =>
            {
                o.ClientId = external.Google.ClientId!;
                o.ClientSecret = external.Google.ClientSecret ?? "";
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.ClaimActions.MapJsonKey(ExternalLoginService.EmailVerifiedClaim, "email_verified");
            });

        if (external.Discord.Enabled)
            builder.AddDiscord(o =>
            {
                o.ClientId = external.Discord.ClientId!;
                o.ClientSecret = external.Discord.ClientSecret ?? "";
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Scope.Add("email");
                o.ClaimActions.MapJsonKey(ExternalLoginService.EmailVerifiedClaim, "verified");
            });
    }

    private static void AddOpenIddict(IServiceCollection services, OpenIddictKeyOptions keys, IHostEnvironment environment)
    {
        var hasCertificates = !string.IsNullOrWhiteSpace(keys.SigningCertificatePath) &&
                              !string.IsNullOrWhiteSpace(keys.EncryptionCertificatePath);
        if (!hasCertificates && !keys.UseEphemeralKeys && !environment.IsDevelopment())
            throw new InvalidOperationException(
                "Auth:OpenIddict:SigningCertificatePath and EncryptionCertificatePath must be configured outside Development.");

        services.AddOptions<OpenIddictServerOptions>().Configure<PublicUrl>((o, url) =>
        {
            o.Issuer = new Uri(url.Base);
            foreach (var resource in url.Resources) o.Resources.Add(new Uri(resource));
        });

        services.AddOpenIddict()
            .AddCore(o =>
            {
                o.UseEntityFrameworkCore().UseDbContext<StudioDbContext>().ReplaceDefaultEntities<Guid>();
                o.ReplaceApplicationManager(typeof(StudioApplicationManager<>));
            })
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetDeviceAuthorizationEndpointUris("/connect/device")
                    .SetEndUserVerificationEndpointUris("/connect/verify")
                    .SetUserInfoEndpointUris("/connect/userinfo")
                    .SetRevocationEndpointUris("/connect/revocation")
                    .SetIntrospectionEndpointUris("/connect/introspection")
                    .SetEndSessionEndpointUris("/connect/end-session")
                    .SetConfigurationEndpointUris("/.well-known/openid-configuration", "/.well-known/oauth-authorization-server")
                    .SetJsonWebKeySetEndpointUris("/.well-known/jwks");

                o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().AllowDeviceAuthorizationFlow();
                o.RequireProofKeyForCodeExchange();
                o.RegisterScopes(OidcClients.ServerScopes);
                o.SetAccessTokenLifetime(TimeSpan.FromHours(1));
                o.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
                o.DisableAccessTokenEncryption();

                if (hasCertificates)
                {
                    o.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(
                        keys.SigningCertificatePath!, keys.SigningCertificatePassword));
                    o.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(
                        keys.EncryptionCertificatePath!, keys.EncryptionCertificatePassword));
                }
                else if (keys.UseEphemeralKeys)
                {
                    o.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
                }
                else
                {
                    o.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate();
                }

                var aspNetCore = o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableEndUserVerificationEndpointPassthrough();
                if (environment.IsDevelopment()) aspNetCore.DisableTransportSecurityRequirement();

                o.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(b =>
                    b.UseInlineHandler(ctx =>
                    {
                        ctx.Metadata["registration_endpoint"] = new Uri(ctx.Options.Issuer!, "/connect/register").AbsoluteUri;
                        ctx.Metadata["token_endpoint_auth_methods_supported"] =
                            JsonSerializer.SerializeToElement(new[] { "none", "client_secret_post", "client_secret_basic" });
                        return default;
                    }));
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
            });
    }
}
