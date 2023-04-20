using Kalkatos.FunctionsGame.Registry;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
	public interface IService
	{
		// Game
		Task<GameRegistry> GetGameConfig (string gameId);
		// Log in
		Task<string> GetPlayerId (string deviceId);
		Task RegisterDeviceWithId (string deviceId, string playerId);
		Task<PlayerRegistry> GetPlayerRegistry (string playerId);
		Task SetPlayerRegistry (PlayerRegistry registry);
		Task DeletePlayerRegistry (string playerId);
		// Matchmaking
		Task<MatchmakingEntry[]> GetMatchmakingEntries (string region, string playerId, string matchId, MatchmakingStatus status);
		Task UpsertMatchmakingEntry (MatchmakingEntry entry);
		Task DeleteMatchmakingHistory (string playerId, string matchId);
		// Action
		//Task<ActionRegistry> GetAction (string playerId, string matchId);
		//Task SetAction (string playerId, string matchId, ActionRegistry action);
		//Task DeleteAction (string playerId, string matchId);
		// Match
		Task<MatchRegistry> GetMatchRegistry (string matchId);
		Task SetMatchRegistry (MatchRegistry matchRegistry);
		Task DeleteMatchRegistry (string matchId);
		// States
		Task<StateRegistry> GetState (string matchId);
		Task<bool> SetState (string matchId, StateRegistry oldState, StateRegistry newState);
		Task DeleteState (string matchId);
		// General
		Task ScheduleCheckMatch (int millisecondsDelay, string matchId, int lastHash);
		Task<bool> GetBool (string key);
	}
}
