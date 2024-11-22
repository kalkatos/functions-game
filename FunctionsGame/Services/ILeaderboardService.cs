using Kalkatos.FunctionsGame.Registry;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public interface ILeaderboardService
    {
        Task AddLeaderboardEvent (LeaderboardRegistry registry);
        Task<LeaderboardRegistry[]> GetLeaderboardEvents (string gameId, string key);
        Task UpdateLeaderboardEvents (LeaderboardRegistry[] registries);
    }
}
