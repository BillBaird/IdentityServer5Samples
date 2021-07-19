using System;
using System.Threading.Tasks;
using BlazorServer.BffServiceOverrides;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BlazorServer.Data;
using Duende.Bff;
using Microsoft.AspNetCore.Http;
using IdentityModel.AspNetCore.AccessTokenManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BlazorServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        public IServiceCollection Services { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddHttpContextAccessor();
            services.AddHttpClient();

            services.AddScoped<WeatherForecastService>();

            // Add BFF services to DI
            // ...also add server-side session management
            // ...also adds access token management
            services.AddBff()
            // After calling this, you can substitute your own override of any management endpoints.
            // This could be where we give it a Login Service which registers device information.
                .AddServerSideSessions();
            
            // management endpoints (override what was done in .AddBff();
            services.AddTransient<ILoginService, CustomLoginService>();
            services.AddTransient<ILogoutService, CustomLogoutService>();
            services.AddTransient<IUserService, CustomUserService>();
            services.AddTransient<IBackchannelLogoutService, CustomBackchannelLogoutService>();

            services.AddHttpClient("api_client", configureClient =>
            {
                configureClient.BaseAddress = new Uri("https://localhost:4999/");
            });

            services.AddHttpClient("api_client_6001", configureClient =>
            {
                configureClient.BaseAddress = new Uri("https://localhost:6001/");
            });

            // This TokenStore is memory only, relying on a ConcurrentDictionary.  While it works for demo
            // purposes, it does not scale to multiple instances, nor does it allow for federated use.  It
            // likely will need a physical backing store.  How is this done with federated login?  It may be
            // in the cookie (which this is trying to avoid).
            services.AddSingleton<IUserAccessTokenStore, CustomTokenStore>();

            // configure server-side authentication and session management
            services.AddAuthentication(options =>
                {
                    // OpenID Connect for challenge and signout - cookies for all the other operations
                    options.DefaultScheme = "cookie";
                    options.DefaultChallengeScheme = "oidc";
                    options.DefaultSignOutScheme = "oidc";
                })
                // The Cookie Handler does local session management
                // See https://docs.duendesoftware.com/identityserver/v5/bff/session/handlers/
                .AddCookie("cookie", options =>
                {
                    // host prefixed cookie name
                    // it is recommended to use a cookie name prefix if compatible with your application
                    options.Cookie.Name = "__Host-bff";
                    // strict SameSite handling
                    // use the highest available SameSite mode that is compatible with your application, e.g. strict, but at least lax
                    options.Cookie.SameSite = SameSiteMode.Strict;
                })
                .AddOpenIdConnect("oidc", options =>
                {
                    options.Authority = "https://localhost:19101";
                    
                    // confidential client using code flow + PKCE
                    options.ClientId = "interactive.confidential";
                    options.ClientSecret = "bffSecret";
                    options.ResponseType = "code";
                    // query response type is compatible with strict SameSite mode
                    options.ResponseMode = "query";
                    options.UsePkce = true;

                    // get claims without mappings
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.MapInboundClaims = false;
                    // save tokens into authentication session to enable automatic token management
                    options.SaveTokens = true;

                    // request scopes + refresh tokens
                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("api1");
                    //options.Scope.Add("email");
                    //options.Scope.Add("kyc");
                    // and refresh token
                    options.Scope.Add("offline_access");

                    // map the standard identity.Name and identity.Role to the claims "name" and "role".
                    options.TokenValidationParameters = new()
                    {
                        NameClaimType = "name",
                        RoleClaimType = "role"
                    };

                    options.Events.OnTokenValidated = async n => 
                    {
                        Logger(n).LogTrace("OnTokenValidated");
                        // Save off the Access Token since we may need it later
                        var svc = n.HttpContext.RequestServices.GetRequiredService<IUserAccessTokenStore>();
                        var exp = DateTimeOffset.UtcNow.AddSeconds(Double.Parse(n.TokenEndpointResponse.ExpiresIn));
                        await svc.StoreTokenAsync(n.Principal, n.TokenEndpointResponse.AccessToken, exp, n.TokenEndpointResponse.RefreshToken);
                    };

                    // These are simply for understanding and log when these events are raised.
                    options.Events.OnAuthenticationFailed = n => LogTrace(n, "OnAuthenticationFailed");
                    options.Events.OnAuthorizationCodeReceived = n => LogTrace(n, "OnAuthorizationCodeReceived");
                    options.Events.OnMessageReceived = n => LogTrace(n, "OnMessageReceived");
                    options.Events.OnRedirectToIdentityProvider = n => LogTrace(n, "OnRedirectToIdentityProvider");
                    options.Events.OnRedirectToIdentityProviderForSignOut = n => LogTrace(n, "OnRedirectToIdentityProviderForSignOut");
                    options.Events.OnSignedOutCallbackRedirect = n => LogTrace(n, "OnSignedOutCallbackRedirect");
                    options.Events.OnRemoteSignOut = n => LogTrace(n, "OnRemoteSignOut");
                    options.Events.OnTokenResponseReceived = n => LogTrace(n, "OnTokenResponseReceived");
                    options.Events.OnUserInformationReceived = n => LogTrace(n, "OnUserInformationReceived");
                });
        }

        Task LogTrace(BaseContext<OpenIdConnectOptions> context, string traceStr)
        {
            Logger(context).LogTrace(traceStr);
            return Task.CompletedTask;
        }

        private ILogger<Startup> _logger { get; set; }
        ILogger<Startup> Logger(BaseContext<OpenIdConnectOptions> context)
            => _logger ??= context.HttpContext.RequestServices.GetService<ILogger<Startup>>();

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseSerilogRequestLogging();
            
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseBff();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBffManagementEndpoints();

                endpoints.MapBlazorHub();
                
                endpoints.MapRemoteBffApiEndpoint("/userinfo", "https://localhost:6001/userinfo")
                    .RequireAccessToken();

                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}