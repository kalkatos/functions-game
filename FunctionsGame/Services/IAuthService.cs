using Kalkatos.Network.Registry;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface IAuthService
{
	string Name { get;}
	string GetAuthUrl (AuthOptions options);
	Task<bool> IsValid (AuthenticationEntry entry);
	Task<AuthenticationEntry> CreateEntryWithCallbackData (Dictionary<string, string> attributes);
}

public class AuthOptions
{
	public string AuthId;
	public AuthType AuthType;
	public string ReturningId;
}

public enum AuthType
{
	First,
	Returning
}
