using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface IService
{
	Task<string> GetData (string table, string partition, string key, string defaultValue);
	Task UpsertData (string table, string partition, string key, string value);
	Task DeleteData (string table, string partition, string key);
	Task<Dictionary<string, string>> GetAllData (string table, string partition, string query);
}
