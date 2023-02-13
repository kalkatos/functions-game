using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kalkatos.FunctionsGame.Game;
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
		private static IGame game = new Game.Rps.RpsGame();

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
			if (match.Status == (int)MatchStatus.Ended)
				return new ActionResponse { IsError = true, Message = "Match is over." };
			StateRegistry state = await service.GetState(request.MatchId);
			if (!game.IsActionAllowed(request.PlayerId, request.Changes, match, state))
				return new ActionResponse { IsError = true, Message = "Action is not allowed." };
			StateRegistry newState = state?.Clone() ?? await PrepareTurn(match, null);
			if (request.Changes.PublicProperties != null)
				newState.UpsertPublicProperties(request.Changes.PublicProperties);
			if (request.Changes.PrivateProperties != null)
				newState.UpsertPrivateProperties(request.PlayerId, request.Changes.PrivateProperties);
			if (state != null && state.Hash == newState.Hash)
				return new ActionResponse { IsError = true, Message = "Action is already registered." };
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
			if (match.Status == (int)MatchStatus.Ended)
				return new StateResponse { IsError = true, Message = "Match is over." };
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			StateRegistry currentState = await service.GetState(request.MatchId);
			if (!HasHandshakingFromAllPlayers(currentState))
				return new StateResponse { IsError = true, Message = "Not every player is ready." };
			switch (match.Status)
			{
				case (int)MatchStatus.AwaitingPlayers:
					await StartMatch(match, currentState);
					return new StateResponse { StateInfo = currentState.GetStateInfo(request.PlayerId) };
				case (int)MatchStatus.Started:
					currentState = await PrepareTurn(match, currentState);
					StateInfo info = currentState.GetStateInfo(request.PlayerId);
					if (info.Hash == request.LastHash)
						return new StateResponse { IsError = true, Message = "Current state is the same known state." };
					return new StateResponse { StateInfo = info };
				case (int)MatchStatus.Ended:
					return new StateResponse { IsError = true, Message = "Match is over." };
			}
			return new StateResponse { IsError = true, Message = "Match is in an unknown state." };
		}

		public static async Task DeleteMatch (string matchId)
		{
			Logger.Log("   [DeleteMatch] " + matchId);
			if (string.IsNullOrEmpty(matchId))
				return;
			await service.DeleteMatchmakingHistory(null, matchId);
			await service.DeleteState(matchId);
			await service.DeleteMatchRegistry(matchId);
		}

		// Temp
		public static void VerifyMatch (string matchId)
		{
			_ = Task.Run(async () => await ScheduleTask(45000, async () => await DeleteMatchIfNoHandshaking(matchId)));
		}

		// Temp
		public static void CreateFirstState (MatchRegistry match)
		{
			_ = Task.Run(async () => await PrepareTurn(match, null));
		}

		// ===========  P R I V A T E  =================

		private static async Task ScheduleTask (int milliseconds, Action callback)
		{
			await Task.Delay(milliseconds);
			callback?.Invoke();
		}

		private static async Task DeleteMatchIfNoHandshaking (string matchId)
		{
			StateRegistry state = await service.GetState(matchId);
			if (!HasHandshakingFromAllPlayers(state))
			{
				await DeleteMatch(matchId);
			}
		}

		private static async Task<StateRegistry> StartMatch (MatchRegistry match, StateRegistry lastState)
		{
			match.Status = (int)MatchStatus.Started;
			match.StartTime = DateTime.UtcNow;
			await service.SetMatchRegistry(match);
			return await PrepareTurn(match, lastState);
		}

		private static async Task<StateRegistry> PrepareTurn (MatchRegistry match, StateRegistry lastState)
		{
			if (lastState == null)
				game.SetConfig(await service.GetGameConfig(game.GameId));
			StateRegistry newState = game.PrepareTurn(match, lastState);
			if (lastState != null && newState.Hash == lastState.Hash)
				return lastState;
			await service.SetState(match.MatchId, newState);
			return newState;
		}

		private static bool HasHandshakingFromAllPlayers (StateRegistry state)
		{
			if (state == null)
				return false; 
			var players = state.GetPlayers();
			int count = 0;
			foreach (var player in players) 
				if (!string.IsNullOrEmpty(state.GetPrivate(player, "Handshaking")))
					count++;
			return count == players.Length;
		}
	}

	public interface IService
	{
		// Game
		Task<Dictionary<string, string>> GetGameConfig (string gameId);

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
