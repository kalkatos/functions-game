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
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (!match.HasPlayer(request.PlayerId))
				return new ActionResponse { IsError = true, Message = "Player is not on that match." };
			if (match.IsEnded)
				return new ActionResponse { IsError = true, Message = "Match is over." };

			// TODO Check with the game rules and the game state if this action is allowed

			StateRegistry state = await service.GetState(request.MatchId);
			int index = state?.Index + 1 ?? 0;
			StateRegistry newState = state?.Clone() ?? CreateNewState(match);
			newState.Index = index;
			foreach (var item in request.PublicChanges)
			{
				if (newState.PublicProperties.ContainsKey(item.Key))
					newState.PublicProperties[item.Key] = item.Value;
				else
					newState.PublicProperties.Add(item.Key, item.Value);
			}
			foreach (var item in request.PrivateChanges)
			{
				PrivateState playerState = newState.PrivateStates.Where(state => state.PlayerId == request.PlayerId).First();
				if (playerState.Properties.ContainsKey(item.Key))
					playerState.Properties[item.Key] = item.Value;
				else
					playerState.Properties.Add(item.Key, item.Value);
			}
			newState.UpdateHash();
			if (state != null && state.Hash == newState.Hash)
				return new ActionResponse { IsError = true, Message = "Action is already registered." };

			// DEBUG
			Logger.LogWarning($"   [SendAction] Setting state: {JsonConvert.SerializeObject(newState)}");
			
			await service.SetState(request.MatchId, newState);
			return new ActionResponse { AlteredState = newState.GetStateInfo(request.PlayerId) };
		}

		// =========== Match =================

		public static async Task<StateResponse> GetMatchState (StateRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new StateResponse { IsError = true, Message = "Match id and player id may not be null." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new StateResponse { IsError = true, Message = "Match not found." };
			if (match.IsEnded)
				return new StateResponse { IsError = true, Message = "Match is over." };
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			StateRegistry currentState;
			if (!match.IsStarted)
				currentState = await InitializeMatch(match);
			else
				currentState = await service.GetState(request.MatchId);
			if (!HasHandshakingFromAllPlayers(currentState))
				return new StateResponse { IsError = true, Message = "Not every player is ready." };
			StateInfo info = currentState.GetStateInfo(request.PlayerId);
			if (info.Hash == request.LastHash)
				return new StateResponse { IsError = true, Message = "Current state is the same known state." };
			return new StateResponse { StateInfo = info };
		}

		public static async Task DeleteMatch (string matchId)
		{
			if (matchId == null)
				return;
			await service.DeleteMatchmakingHistory(null, matchId);
			//await service.DeleteActionHistory(matchId);
			await service.DeleteState(matchId);
			await service.DeleteMatchRegistry(matchId);
		}


		// ===========  P R I V A T E  =================

		// Temporarily public
		public static async Task<StateRegistry> InitializeMatch (MatchRegistry match)
		{
			match.IsStarted = true;
			await service.SetMatchRegistry(match);
			StateRegistry firstState = CreateNewState(match);
			Logger.LogWarning($"    [InitializeMatch] Setting state: {JsonConvert.SerializeObject(firstState)}");
			await service.SetState(match.MatchId, firstState);
			return firstState;
		}

		private static bool HasHandshakingFromAllPlayers (StateRegistry state)
		{
			if (state == null || state.PublicProperties == null || state.PrivateStates == null)
				return false;
			return state.PrivateStates.Count(item =>
					item.Properties.ContainsKey("Handshaking")
					&& item.Properties["Handshaking"] == "1") == state.PrivateStates.Length;
		}

		private static StateRegistry CreateNewState (MatchRegistry match)
		{
			PrivateState[] privateStates = new PrivateState[match.PlayerIds.Length];
			for (int i = 0; i < privateStates.Length; i++)
				privateStates[i] = new PrivateState { PlayerId = match.PlayerIds[i], Properties = new Dictionary<string, string>() };
			StateRegistry newRegistry = new StateRegistry
			{
				Index = 0,
				PublicProperties = new Dictionary<string, string>(),
				PrivateStates = privateStates
			};
			newRegistry.UpdateHash();
			return newRegistry;
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
		Task<StateRegistry> GetState (string matchId);
		Task SetState (string matchId, StateRegistry state);
		Task DeleteState (string matchId);
	}
}
