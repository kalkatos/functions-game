using Kalkatos.Network.Registry;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface IAsyncService
{
	Task<AsyncObjectRegistry> GetAsyncObject (string region, string id);
	Task<AsyncObjectRegistry[]> GetAsyncObjects (string region);
	Task UpsertAsyncObject (AsyncObjectRegistry registry);
}
