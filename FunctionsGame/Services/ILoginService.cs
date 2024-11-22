using Kalkatos.FunctionsGame.Registry;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public interface ILoginService
	{
        Task<GameRegistry> GetGameConfig (string gameId);
        Task<string> GetPlayerId (string deviceId);
        Task RegisterDeviceWithId (string deviceId, string playerId);
        Task<PlayerRegistry> GetPlayerRegistry (string playerId);
        Task SetPlayerRegistry (PlayerRegistry registry);
        Task DeletePlayerRegistry (string playerId);
    }
}
