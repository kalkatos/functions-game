using Kalkatos.FunctionsGame.Registry;
using System.Collections.Generic;
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
		Task<MatchmakingEntry[]> GetMatchmakingEntries (string region, string matchId, string playerId, MatchmakingStatus status);
		Task UpsertMatchmakingEntry (MatchmakingEntry entry);
		Task DeleteMatchmakingHistory (string playerId, string matchId);
		// Action
		Task AddAction (string matchId, string playerId, ActionRegistry action);
		Task<List<ActionRegistry>> GetActions (string matchId);
		Task UpdateActions (string matchId, List<ActionRegistry> actionList);
		Task DeleteActions (string matchId);
		// Match
		Task<MatchRegistry> GetMatchRegistry (string matchId);
		Task SetMatchRegistry (MatchRegistry matchRegistry);
		Task DeleteMatchRegistry (string matchId);
		// States
		Task<StateRegistry> GetState (string matchId);
		Task<bool> SetState (string matchId, int? oldStateHash, StateRegistry newState);
		Task DeleteState (string matchId);
		// General
		Task ScheduleCheckMatch (int millisecondsDelay, string matchId, int lastHash);
		Task<bool> GetBool (string key);
		Task LogError (string error, string group, string metadata);
	}
}
