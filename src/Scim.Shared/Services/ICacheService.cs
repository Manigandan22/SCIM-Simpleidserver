using System.Threading.Tasks;

namespace Scim.Shared.Services
{
    public interface ICacheService
    {
        Task SetAsync<T>(string key, T value);
        Task<T?> GetAsync<T>(string key);
    }
}
