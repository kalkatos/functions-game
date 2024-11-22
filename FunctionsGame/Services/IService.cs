using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public interface IService
    {
        Task<string> GetData (string table, string partition, string key, string defaultValue);
        Task UpsertData (string table, string partition, string key, string value);
        Task DeleteData (string table, string partition, string key);
        Task<Dictionary<string, string>> GetAllData (string table, string partition);
    }
}
