using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public interface IAnalyticsService
    {
        Task SendEvent (string playerId, string key, string value);
    }
}
