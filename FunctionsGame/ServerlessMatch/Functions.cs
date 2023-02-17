using System;
using System.Collections.Generic;
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
		private static IGame game = new Rps.RpsGame();
		private static Random rand = new Random();

		// =========== Log In =================

		public static async Task<LoginResponse> LogIn (LoginRequest request)
		{
			if (string.IsNullOrEmpty(request.Identifier) || string.IsNullOrEmpty(request.GameId))
				return new LoginResponse { IsError = true, Message = "Identifier is null. Must be an unique user identifier." };

			game.Settings = await service.GetGameConfig(request.GameId);
			PlayerRegistry playerRegistry;
			string playerId = await service.GetPlayerId(request.Identifier);
			if (string.IsNullOrEmpty(playerId))
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
			else
			{
				playerId = await service.GetPlayerId(request.Identifier);
				playerRegistry = await service.GetPlayerRegistry(playerId);
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
			StateRegistry newState = state?.Clone() ?? await CreateFirstStateAndRegister(match);
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
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			StateRegistry currentState = await service.GetState(request.MatchId);
			if (!HasHandshakingFromAllPlayers(currentState))
				return new StateResponse { IsError = true, Message = "Not every player is ready." };
			switch (match.Status)
			{
				case (int)MatchStatus.AwaitingPlayers:
					match.Status = (int)MatchStatus.Started;
					match.StartTime = DateTime.UtcNow;
					await service.SetMatchRegistry(match);
					currentState = await PrepareTurn (match, currentState);

					Logger.Log("   [GetMatchState] " + JsonConvert.SerializeObject(currentState));

					return new StateResponse { StateInfo = currentState.GetStateInfo(request.PlayerId) };
				case (int)MatchStatus.Started:
				case (int)MatchStatus.Ended:
					currentState = await PrepareTurn(match, currentState);
					StateInfo info = currentState.GetStateInfo(request.PlayerId);
					if (info.Hash == request.LastHash)
						return new StateResponse { IsError = true, Message = "Current state is the same known state." };

					Logger.Log("   [GetMatchState] " + JsonConvert.SerializeObject(currentState));

					return new StateResponse { StateInfo = info };
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
			_ = Task.Run(async () => { await service.ScheduleCheckMatch(game.Settings.FirstCheckMatchDelay * 1000 + rand.Next(0, 300), matchId, 0); });
		}

		// Temp
		public static void CreateFirstState (MatchRegistry match)
		{
			_ = Task.Run(async () => await CreateFirstStateAndRegister(match));
		}

		public static async Task CheckMatch (string matchId, int lastHash)
		{
			StateRegistry state = await service.GetState(matchId);
			if (state == null)
				return;
			if (!HasHandshakingFromAllPlayers(state) || state.Hash == lastHash)
				await DeleteMatch(matchId);
			else
				await service.ScheduleCheckMatch(game.Settings.RecurrentCheckMatchDelay * 1000 + rand.Next(0, 300), matchId, state.Hash);
		}

		// ===========  P R I V A T E  =================

		private static async Task<StateRegistry> CreateFirstStateAndRegister (MatchRegistry match)
		{
			StateRegistry newState = game.CreateFirstState(match);
			await service.SetState(match.MatchId, newState);
			return newState;
		}

		private static async Task<StateRegistry> PrepareTurn (MatchRegistry match, StateRegistry lastState)
		{
			if (lastState == null)
				Logger.Log("   [PrepareTurn] Last state should not be null.");
			StateRegistry newState = game.PrepareTurn(match, lastState);
			if (newState.IsMatchEnded)
			{
				match.Status = (int)MatchStatus.Ended;
				await service.SetMatchRegistry(match);
				await service.SetState(match.MatchId, newState);
				await service.ScheduleCheckMatch(game.Settings.FinalCheckMatchDelay * 1000 + rand.Next(0, 300), match.MatchId, newState.Hash);
				return newState;
			}
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
				if (player[0] == 'X' || !string.IsNullOrEmpty(state.GetPrivate(player, "Handshaking")))
					count++;
			return count == players.Length;
		}
	}

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
		Task DeleteMatchmakingHistory (string playerId, string matchId);
		// Match
		Task<MatchRegistry> GetMatchRegistry (string matchId);
		Task SetMatchRegistry (MatchRegistry matchRegistry);
		Task DeleteMatchRegistry (string matchId);
		// States
		Task<StateRegistry> GetState (string matchId);
		Task SetState (string matchId, StateRegistry state);
		Task DeleteState (string matchId);
		// General
		Task ScheduleCheckMatch (int millisecondsDelay, string matchId, int lastHash);
	}
}
