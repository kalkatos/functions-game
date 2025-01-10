using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface IAnalyticsService
{
	Task SendEvent (string playerId, string key, string value);
}
