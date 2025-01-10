using Kalkatos.Network.Registry;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface ILoginService
{
	Task<GameRegistry> GetGameConfig (string gameId);
	Task<string> GetPlayerId (string deviceId);
	Task RegisterDeviceWithId (string deviceId, string playerId);
	Task<PlayerRegistry> GetPlayerRegistry (string playerId);
	Task SetPlayerRegistry (PlayerRegistry registry);
	Task DeletePlayerRegistry (string playerId);
}
