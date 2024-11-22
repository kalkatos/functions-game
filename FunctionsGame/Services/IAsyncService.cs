using Kalkatos.FunctionsGame.Registry;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public interface IAsyncService
    {
        Task<AsyncObjectRegistry> GetAsyncObject (string region, string id);
        Task<AsyncObjectRegistry[]> GetAsyncObjects (string region);
        Task UpsertAsyncObject (AsyncObjectRegistry registry);
    }
}
