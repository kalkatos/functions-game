using Kalkatos.Network.Model;
using Kalkatos.Network.Registry;
using System.Threading.Tasks;

namespace Kalkatos.Network;

internal class SampleAsyncGame : IAsyncGame
{
	public bool IsCorrectRequest (AddAsyncObjectRequest request)
	{
			return true;
	}

	public async Task TreatObjectAdded (AsyncObjectRegistry obj)
	{
		await Task.Delay(50);
	}
}
