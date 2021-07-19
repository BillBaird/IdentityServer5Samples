using System;
using System.Linq;
using System.Threading.Tasks;
using Duende.Bff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace BlazorServer.BffServiceOverrides
{
    internal static class Extensions
    {
        private const string BffMiddlewareMarker = "Duende.Bff.BffMiddlewareMarker";

        public static void CheckForBffMiddleware(this HttpContext context, BffOptions options)
        {
            if (options.EnforceBffMiddleware)
            {
                var found = context.Items.TryGetValue(BffMiddlewareMarker, out _);
                if (!found)
                {
                    throw new InvalidOperationException(
                        "The BFF middleware is missing in the pipeline. Add 'app.UseBff' after 'app.UseRouting' but before 'app.UseAuthorization'");
                }
            }
        }

        public static bool CheckAntiForgeryHeader(this HttpContext context, BffOptions options)
        {
            var antiForgeryHeader = context.Request.Headers[options.AntiForgeryHeaderName].FirstOrDefault();
            return antiForgeryHeader != null && antiForgeryHeader == options.AntiForgeryHeaderValue;
        }

        public static async Task<string> GetManagedAccessToken(this HttpContext context, TokenType tokenType)
        {
            string token;

            if (tokenType == TokenType.User)
            {
                token = await context.GetUserAccessTokenAsync();
            }
            else if (tokenType == TokenType.Client)
            {
                token = await context.GetClientAccessTokenAsync();
            }
            else
            {
                token = await context.GetUserAccessTokenAsync();

                if (string.IsNullOrEmpty(token))
                {
                    token = await context.GetClientAccessTokenAsync();
                }
            }

            return token;
        }
    }
}