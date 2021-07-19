using System;
using System.Threading.Tasks;
using IdentityModel.AspNetCore.AccessTokenManagement;
using System.Security.Claims;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

#nullable enable
namespace BlazorServer
{
    public class CustomTokenStore : IUserAccessTokenStore
    {
        readonly ConcurrentDictionary<string, UserAccessToken> _tokens = new ConcurrentDictionary<string, UserAccessToken>();
        private readonly ILogger<CustomTokenStore> _logger;

        public CustomTokenStore(ILogger<CustomTokenStore> logger)
        {
            _logger = logger;
        }
        
        public Task ClearTokenAsync(ClaimsPrincipal user, UserAccessTokenParameters? parameters = null)
        {
            var sub = user.FindFirst("sub")!.Value;
            _logger.LogDebug("Clear Token {@Sub}", sub);
            _tokens.TryRemove(sub, out _);
            return Task.CompletedTask;
        }

        public Task<UserAccessToken?> GetTokenAsync(ClaimsPrincipal user, UserAccessTokenParameters? parameters = null)
        {
            var sub = user.FindFirst("sub")!.Value;
            _logger.LogTrace("Get Token {@Sub}", sub);
            _tokens.TryGetValue(sub, out var value);
            return Task.FromResult(value);
        }

        public Task StoreTokenAsync(ClaimsPrincipal user, string accessToken, DateTimeOffset expiration, string? refreshToken = null, UserAccessTokenParameters? parameters = null)
        {
            var sub = user.FindFirst("sub")!.Value;
            _logger.LogDebug("Store Token {@Sub} in TokenStore", sub);
            var token = new UserAccessToken
            {
                AccessToken = accessToken,
                Expiration = expiration,
                RefreshToken = refreshToken
            };
            _tokens[sub] = token;
            return Task.CompletedTask;
        }
    }
}