using System;
using System.Collections.Generic;
using System.Linq;
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

		// =========== Log In =================

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

		// =========== Action =================

		public static async Task<ActionResponse> SendAction (ActionRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId) || string.IsNullOrEmpty(request.PlayerAlias))
				return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (!match.HasPlayer(request.PlayerId))
				return new ActionResponse { IsError = true, Message = "Player is not on that match." };
			if (match.IsEnded)
				return new ActionResponse { IsError = true, Message = "Match is over." };

			// TODO Check with the game rules if this action is allowed
			
			StateRegistry[] stateHistory = await service.GetStateHistory(request.MatchId);
			StateRegistry lastState = stateHistory[stateHistory.Length - 1];
			foreach (var item in request.Content)
			{
				if (lastState.PublicProperties.ContainsKey(item.Key))
				{
					lastState.PublicProperties[item.Key] = item.Value;
					continue;
				}
				PrivateState playerState = lastState.PrivateStates.Where(state => state.PlayerId == request.PlayerId).First();
				if (playerState.Properties.ContainsKey(item.Key))
					playerState.Properties[item.Key] = item.Value;
			}
			return new ActionResponse { Message = "Action registered successfully." };
		}

		// =========== Match =================

		public static async Task<StateResponse> GetMatchState (StateRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new StateResponse { IsError = true, Message = "Match id and player id may not be null." };
			if (request.LastIndex < 0)
				return new StateResponse { IsError = true, Message = "LastIndex must be higher or equal to zero." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new StateResponse { IsError = true, Message = "Match not found." };
			if (match.IsEnded)
				return new StateResponse { IsError = true, Message = "Match is over." };
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			if (request.LastIndex == 0 && !match.IsStarted)
				return await CheckHandshaking(request.PlayerId, match);
			StateRegistry[] stateHistory = await service.GetStateHistory(request.MatchId);
			if (stateHistory != null)
				if (request.LastIndex >= stateHistory.Length)
					return new StateResponse { IsError = true, Message = "State is not ready yet." };
			int amountRequested = stateHistory.Length - request.LastIndex;
			StateInfo[] requestedStates = new StateInfo[amountRequested];
			for (int requestedIndex = 0, historyIndex = stateHistory.Length - 1;
					historyIndex >= 0;
					requestedIndex++, historyIndex--)
				requestedStates[requestedIndex] = stateHistory[historyIndex].GetStateInfo(request.PlayerId);
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

		// ===========  P R I V A T E  =================

		private static async Task<StateResponse> CheckHandshaking (string playerId, MatchRegistry match)
		{
			ActionInfo[] actionHistory = await service.GetActionHistory(match.MatchId, null, "Handshaking");
			string[] playerAliasesInMatch = match.PlayerInfos.Select(info => info.Alias).Distinct().ToArray();
			string[] playerAliasesWithAction = actionHistory.Select(action => action.PlayerAlias).Distinct().ToArray();

			// DEBUG
			Logger.Log($"    [GetMatchState] players in match: {JsonConvert.SerializeObject(playerAliasesInMatch)} \n Players with Handshaking: {JsonConvert.SerializeObject(playerAliasesWithAction)}");

			if (!playerAliasesInMatch.SequenceEqual(playerAliasesWithAction))
				return new StateResponse { IsError = true, Message = "Not every player is ready." };
			match.IsStarted = true;
			await service.SetMatchRegistry(match);
			StateRegistry stateRegistry = PrepareTurn(match, null);
			StateRegistry[] newStateHistory = new StateRegistry[] { stateRegistry };
			await service.SetStateHistory(match.MatchId, newStateHistory);
			StateInfo stateInfo = stateRegistry.GetStateInfo(playerId);
			return new StateResponse { StateInfos = new StateInfo[] { stateInfo } };
		}

		private static StateRegistry PrepareTurn (MatchRegistry match, StateRegistry[] stateHistory)
		{
			int currentIndex = stateHistory?.Length ?? 0;
			PrivateState[] privateStates = new PrivateState[match.PlayerIds.Length];
			for (int i = 0; i < privateStates.Length; i++)
				privateStates[i] = new PrivateState { PlayerId = match.PlayerIds[i], Properties = new Dictionary<string, string>() };
			return new StateRegistry
			{
				Index = currentIndex,
				PublicProperties = new Dictionary<string, string>(),
				PrivateStates = privateStates
			};
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
		Task SetMatchRegistry (MatchRegistry matchRegistry);
		Task DeleteMatchRegistry (string matchId);
		// Action
		//Task RegisterAction (string matchId, string playerId, Dictionary<string, string> content);
		//Task<ActionInfo[]> GetActionHistory (string matchId, string[] players, string actionName);
		//Task DeleteActionHistory (string matchId);
		// States
		Task<StateRegistry[]> GetStateHistory (string matchId);
		Task SetStateHistory (string matchId, StateRegistry[] states);
		Task DeleteStateHistory (string matchId);
	}
}
