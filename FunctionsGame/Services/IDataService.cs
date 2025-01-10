using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface IDataService
{
	Task<string> GetValue (string key, string defaultValue);
	Task SetValue (string key, string value);
	Task Delete (string key);
}
