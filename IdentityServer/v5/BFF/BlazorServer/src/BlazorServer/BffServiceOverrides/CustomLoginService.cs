using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading.Tasks;
using Duende.Bff;
using Microsoft.Extensions.Logging;

namespace BlazorServer.BffServiceOverrides
{
    /// <summary>
    /// Service for handling login requests
    /// </summary>
    public class CustomLoginService : ILoginService
    {
        private readonly BffOptions _options;
        private readonly ILogger<CustomLoginService> _logger;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="options"></param>
        public CustomLoginService(BffOptions options,
            ILogger<CustomLoginService> logger)
        {
            _options = options;
            _logger = logger;
        }
        
        /// <inheritdoc />
        public async Task ProcessRequestAsync(HttpContext context)
        {
            _logger.LogTrace("Enter ProcessRequestAsync");
            context.CheckForBffMiddleware(_options);
            
            var returnUrl = context.Request.Query[Constants.RequestParameters.ReturnUrl].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                if (!Util.IsLocalUrl(returnUrl))
                {
                    throw new Exception("returnUrl is not application local");
                }
            }

            var props = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? "/"
            };

            await context.ChallengeAsync(props);
            _logger.LogTrace("Exit ProcessRequestAsync");
        }
    }
}