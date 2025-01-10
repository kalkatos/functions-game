using Kalkatos.Network.Registry;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kalkatos.Network;

internal class SampleAuthService : IAuthService
{
	public string Name => "Sample";

	public string GetAuthUrl (AuthOptions options)
	{
		throw new System.NotImplementedException();
	}

	public Task<bool> IsValid (AuthenticationEntry entry)
	{
		throw new System.NotImplementedException();
	}

	public Task<AuthenticationEntry> CreateEntryWithCallbackData (Dictionary<string, string> attributes)
	{
		throw new System.NotImplementedException();
	}
}
