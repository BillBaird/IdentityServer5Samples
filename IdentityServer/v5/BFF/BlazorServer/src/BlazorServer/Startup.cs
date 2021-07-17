using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BlazorServer.Data;
using Microsoft.AspNetCore.Http;
using IdentityModel.AspNetCore.AccessTokenManagement;
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

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddHttpContextAccessor();
            services.AddHttpClient();

            services.AddScoped<WeatherForecastService>();

            services.AddBff();
            // After calling this, you can substitute your own override of any management endpoints.
            // This could be where we give it a Login Service which registers device information.

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

            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "cookie";
                    options.DefaultChallengeScheme = "oidc";
                    options.DefaultSignOutScheme = "oidc";
                })
                .AddCookie("cookie", options =>
                {
                    options.Cookie.Name = "__Host-bff";
                    options.Cookie.SameSite = SameSiteMode.Strict;
                })
                .AddOpenIdConnect("oidc", options =>
                {
                    options.Authority = "https://localhost:19101";
                    options.ClientId = "interactive.confidential";
                    options.ClientSecret = "bffSecret";
                    options.ResponseType = "code";
                    options.ResponseMode = "query";

                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.MapInboundClaims = false;
                    options.SaveTokens = true;

                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("api1");
                    //options.Scope.Add("email");
                    //options.Scope.Add("kyc");
                    options.Scope.Add("offline_access");

                    options.TokenValidationParameters = new()
                    {
                        NameClaimType = "name",
                        RoleClaimType = "role"
                    };

                    options.Events.OnTokenValidated = async n => 
                    {
                        // Save off the Access Token since we may need it later
                        var svc = n.HttpContext.RequestServices.GetRequiredService<IUserAccessTokenStore>();
                        var exp = DateTimeOffset.UtcNow.AddSeconds(Double.Parse(n.TokenEndpointResponse.ExpiresIn));
                        await svc.StoreTokenAsync(n.Principal, n.TokenEndpointResponse.AccessToken, exp, n.TokenEndpointResponse.RefreshToken);
                    };
                });
        }

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