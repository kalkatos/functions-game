using Kalkatos.Network.Registry;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public interface ILeaderboardService
{
	Task AddLeaderboardEvent (LeaderboardRegistry registry);
	Task<LeaderboardRegistry[]> GetLeaderboardEvents (string gameId, string key);
	Task UpdateLeaderboardEvents (LeaderboardRegistry[] registries);
}
