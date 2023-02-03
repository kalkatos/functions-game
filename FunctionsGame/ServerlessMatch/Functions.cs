using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;

namespace Kalkatos.FunctionsGame
{
	public class MatchFunctions
	{
#if AZURE_FUNCTIONS
		private static IService service = new AzureFunctions.AzureFunctionsService();
#else
		private static IService service;
#endif

		public static async Task<LoginResponse> LogIn (LoginRequest request)
		{
			if (string.IsNullOrEmpty(request.Identifier))
				return new LoginResponse { IsError = true, Message = "Identifier is null. Must be an unique user identifier." };

			string playerId;
			PlayerRegistry playerRegistry;
			bool isRegistered = await service.IsRegisteredDevice(request.Identifier);
			if (isRegistered)
			{
				playerId = await service.GetPlayerId(request.Identifier);
				playerRegistry = await service.GetPlayerRegistry(playerId);
			}
			else
			{
				playerId = Guid.NewGuid().ToString();
				await service.RegisterDeviceWithId(request.Identifier, playerId);
				string newPlayerAlias = Guid.NewGuid().ToString();
				PlayerInfo newPlayerInfo = new PlayerInfo { Alias = newPlayerAlias, Nickname = request.Nickname };
				playerRegistry = new PlayerRegistry
				{
					PlayerId = playerId,
					Info = newPlayerInfo,
					Devices = new string[] { request.Identifier },
					Region = request.Region,
					LastAccess = DateTime.UtcNow,
					FirstAccess = DateTime.UtcNow
				};
				await service.SetPlayerRegistry(playerRegistry);
			}

			return new LoginResponse
			{
				IsAuthenticated = playerRegistry.IsAuthenticated,
				PlayerId = playerRegistry.PlayerId,
				PlayerAlias = playerRegistry.Info.Alias,
				SavedNickname = playerRegistry.Info.Nickname,
			};
		}

		public static async Task<StateResponse> GetMatchState (StateRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new StateResponse { IsError = true, Message = "Match id and player id may not be null." };
			if (request.LastIndex < 0)
				return new StateResponse { IsError = true, Message = "LastIndex must be higher or equal to zero." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new StateResponse { IsError = true, Message = "Match not found." };
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			if (request.LastIndex == 0)
			{
				ActionInfo[] actionHistory = await service.GetActionHistory(request.MatchId, null, "Handshaking");
				string[] playerAliasesInMatch = match.PlayerInfos.Select(info => info.Alias).Distinct().ToArray();
				string[] playerAliasesWithAction = actionHistory.Select(action => action.PlayerAlias).Distinct().ToArray();

				// DEBUG
				Logger.Log($"    [GetMatchState] players in match: {JsonConvert.SerializeObject(playerAliasesInMatch)} \n Players with Handshaking: {JsonConvert.SerializeObject(playerAliasesWithAction)}");

				if (!playerAliasesInMatch.SequenceEqual(playerAliasesWithAction))
					return new StateResponse { IsError = true, Message = "Not every player is ready." };
			}
			StateInfo[] stateHistory = await service.GetStateHistory(request.MatchId);
			if (stateHistory != null && request.LastIndex > stateHistory.Length)
				return new StateResponse { IsError = true, Message = "State is not ready yet." };
			StateInfo state = await PrepareTurn(match, stateHistory);
			if (stateHistory == null)
				stateHistory = new StateInfo[] { state };
			else
				stateHistory.Append(state);
			await service.SetStateHistory(request.MatchId, stateHistory);
			int amountRequested = stateHistory.Length - request.LastIndex;
			StateInfo[] requestedStates = new StateInfo[amountRequested];
			for (int requestedIndex = 0, historyIndex = stateHistory.Length - 1; 
				historyIndex >= 0; 
				requestedIndex++, historyIndex--)
			{
				requestedStates[requestedIndex] = stateHistory[historyIndex];
			}
			return new StateResponse { StateInfos = requestedStates };
		}

		public static async Task DeleteMatch (string matchId)
		{
			if (matchId == null) 
				return;
			await service.DeleteMatchmakingHistory(null, matchId);
			await service.DeleteActionHistory(matchId);
			await service.DeleteStateHistory(matchId);
			await service.DeleteMatchRegistry(matchId);
		}

		private static async Task<StateInfo> PrepareTurn (MatchRegistry match, StateInfo[] stateHistory)
		{
			await Task.Delay(100);
			int currentIndex = stateHistory?.Length ?? 0;
			return new StateInfo { Index = currentIndex, Properties = { { "Index", currentIndex.ToString() } } };
		}
	}

	public interface IService
	{
		// Log in
		Task<bool> IsRegisteredDevice (string deviceId);
		Task<string> GetPlayerId (string deviceId);
		Task RegisterDeviceWithId (string deviceId, string playerId);
		Task<PlayerRegistry> GetPlayerRegistry (string playerId);
		Task SetPlayerRegistry (PlayerRegistry registry);
		Task DeletePlayerRegistry (string playerId);
		// Matchmaking
		Task DeleteMatchmakingHistory (string playerId, string matchId);
		// Match
		Task<MatchRegistry> GetMatchRegistry (string matchId);
		Task DeleteMatchRegistry (string matchId);
		// Action
		Task<ActionInfo[]> GetActionHistory (string matchId, string[] players, string actionName);
		Task DeleteActionHistory (string matchId);
		// States
		Task<StateInfo[]> GetStateHistory (string matchId);
		Task SetStateHistory (string matchId, StateInfo[] states);
		Task DeleteStateHistory (string matchId);
	}
}
