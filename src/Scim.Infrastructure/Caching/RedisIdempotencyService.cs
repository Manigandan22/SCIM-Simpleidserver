using Scim.Domain.Services;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Scim.Infrastructure.Caching
{
    public class RedisIdempotencyService : IIdempotencyService
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisIdempotencyService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task<bool> AcquireLockAsync(string key, TimeSpan expiry)
        {
            var db = _redis.GetDatabase();
            // StringSet with When.NotExists implements simple locking/deduplication
            return await db.StringSetAsync(key, "locked", expiry, When.NotExists);
        }
    }
}
