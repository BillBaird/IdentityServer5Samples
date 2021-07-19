using System;
using System.Threading.Tasks;
using IdentityModel.AspNetCore.AccessTokenManagement;
using System.Security.Claims;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

#nullable enable
namespace BlazorServer
{
    public class SqliteTokenStore : IUserAccessTokenStore, IDisposable, IAsyncDisposable
    {
        readonly ConcurrentDictionary<string, UserAccessToken> _tokens = new ConcurrentDictionary<string, UserAccessToken>();
        private readonly ILogger<SqliteTokenStore> _logger;
        readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1,1);
        private SqliteConnection? _connection;

        public SqliteTokenStore(ILogger<SqliteTokenStore> logger)
        {
            _logger = logger;
        }
        
        public async Task ClearTokenAsync(ClaimsPrincipal user, UserAccessTokenParameters? parameters = null)
        {
            var sub = user.FindFirst("sub")!.Value;
            _logger.LogDebug("Clear Token {@Sub}", sub);
            var conn = await GetConnectionAsync();
            var command = conn.CreateCommand();
            command.CommandText = "DELETE FROM tokenstore where sub = $sub;";
            command.Parameters.AddWithValue("$sub", sub);
            await _semaphoreSlim.WaitAsync();
            try
            {
                _tokens.TryRemove(sub, out _);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public async Task<UserAccessToken?> GetTokenAsync(ClaimsPrincipal user, UserAccessTokenParameters? parameters = null)
        {
            var sub = user.FindFirst("sub")!.Value;
            _logger.LogTrace("Get Token {@Sub}", sub);
            _tokens.TryGetValue(sub, out var value);
            if (value == null)
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    _logger.LogDebug("Read Token {@Sub} from TokenStore", sub);
                    var conn = await GetConnectionAsync();
                    var command = conn.CreateCommand();
                    command.CommandText =
                        "SELECT accessToken, expiration, refreshToken FROM tokenstore where sub = $sub;";
                    command.Parameters.AddWithValue("$sub", sub);
                    await using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        _logger.LogTrace("Token {@Sub} found", sub);
                        value = new UserAccessToken
                        {
                            AccessToken = reader.GetString(0),
                            Expiration = reader.GetDateTime(1),
                            RefreshToken = reader.GetString(2)
                        };
                        _tokens[sub] = value;
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            return value;
        }

        public async Task StoreTokenAsync(ClaimsPrincipal user, string accessToken, DateTimeOffset expiration, string? refreshToken = null, UserAccessTokenParameters? parameters = null)
        {
            var sub = user.FindFirst("sub")!.Value;
            _logger.LogDebug("Store Token {@Sub} in TokenStore", sub);
            var token = new UserAccessToken
            {
                AccessToken = accessToken,
                Expiration = expiration,
                RefreshToken = refreshToken
            };
            var conn = await GetConnectionAsync();
            var command = conn.CreateCommand();
            command.CommandText =
                @"INSERT INTO tokenstore VALUES($sub, $accessToken, $expiration, $refreshToken)
                        ON CONFLICT (sub) DO UPDATE SET accessToken = $accessToken, expiration = $expiration, refreshToken = $refreshToken WHERE sub = $sub;";
            command.Parameters.AddWithValue("$sub", sub);
            command.Parameters.AddWithValue("$accessToken", accessToken);
            command.Parameters.AddWithValue("$expiration", expiration);
            command.Parameters.AddWithValue("$refreshToken", refreshToken);
            await _semaphoreSlim.WaitAsync();
            try
            {
                _tokens[sub] = token;
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        private async Task<SqliteConnection> GetConnectionAsync()
        {
            if (_connection == null)
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    if (_connection == null)
                    {
                        _connection = new SqliteConnection("Data Source=tokenstore.db");
                        await _connection.OpenAsync();
                        await EnsureDbTablesAsync();
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            return _connection;
        }

        private async Task EnsureDbTablesAsync()
        {
            var command = _connection!.CreateCommand();
            command.CommandText =
                @"CREATE TABLE IF NOT EXISTS tokenstore(
                        sub CHAR(22) PRIMARY KEY ASC, 
                        accessToken TEXT NOT NULL, 
                        expiration DATETIME NOT NULL, 
                        refreshToken CHAR(64));                 
                        ";
            await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Sqlite TokenStore created");
        }
        
        public void Dispose()
        {
            _semaphoreSlim.Dispose();
            _connection?.Dispose();
            _connection = null;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            this.Dispose();
            return new ValueTask();
        }
    }
}