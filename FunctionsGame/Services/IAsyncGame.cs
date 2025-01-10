using Kalkatos.Network.Model;
using Kalkatos.Network.Registry;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface IAsyncGame
{
	bool IsCorrectRequest (AddAsyncObjectRequest request);
	Task TreatObjectAdded (AsyncObjectRegistry newRegistry);
}
