using FunctionsGame.Registry;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public interface ILeaderboardService
	{
		Task AddLeaderboardEvent (LeaderboardEventRegistry registry);
		Task<Dictionary<string, string>> GetLeaderboard (string gameId, string pageId);
	}
}
