using System;
using System.Threading.Tasks;

namespace Scim.Domain.Services
{
    public interface IIdempotencyService
    {
        Task<bool> AcquireLockAsync(string key, TimeSpan expiry);
    }
}
