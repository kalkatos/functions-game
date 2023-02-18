using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
	public static class MatchFunctions
	{
#if AZURE_FUNCTIONS
		private static IService service = new Azure.AzureService();
#else
		private static IService service;
#endif
		private static IGame game = new Rps.RpsGame();
		private static Random rand = new Random();
		private const string consonantsUpper = "BCDFGHJKLMNPQRSTVWXZ";
		private const string consonantsLower = "bcdfghjklmnpqrstvwxz";
		private const string vowels = "aeiouy";

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
				playerRegistry = await service.GetPlayerRegistry(playerId);

			return new LoginResponse
			{
				IsAuthenticated = playerRegistry.IsAuthenticated,
				PlayerId = playerRegistry.PlayerId,
				PlayerAlias = playerRegistry.Info.Alias,
				SavedNickname = playerRegistry.Info.Nickname,
			};
		}

		public static async Task<Response> SetPlayerData (SetPlayerDataRequest request)
		{
			if (request.Data == null || request.Data.Count() == 0)
				return new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Request Data is null or empty." };
			if (string.IsNullOrEmpty(request.PlayerId))
				return new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Player ID is null or empty." };
			PlayerRegistry playerRegistry = await service.GetPlayerRegistry(request.PlayerId);
			if (playerRegistry == null)
				return new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Player not found." };
			if (request.Data.ContainsKey("Nickname"))
			{
				playerRegistry.Info.Nickname = request.Data["Nickname"];
				request.Data.Remove("Nickname");
			}
			foreach (var item in request.Data)
				playerRegistry.Info.CustomData[item.Key] = item.Value;
			await service.SetPlayerRegistry(playerRegistry);
			return new Response { Message = "Ok" };
		}

		// =========== Action =================

		public static async Task<ActionResponse> SendAction (ActionRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new ActionResponse { IsError = true, Message = "Problem retrieving the match." };
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

		// TODO Change to use service 
		public static async Task<MatchResponse> GetMatch (MatchRequest request)
		{
			if (request == null || string.IsNullOrEmpty(request.PlayerId))
				return new MatchResponse { IsError = true, Message = "Wrong Parameters." };

			if (string.IsNullOrEmpty(request.MatchId))
			{
				// Get the match id of the match to which that player is assigned in the matchmaking table
				var entries = await service.GetMatchmakingEntries(null, request.PlayerId, null, MatchmakingStatus.Undefined);
				if (entries == null || entries.Length == 0)
					return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
				if (entries.Length > 1)
					Logger.LogWarning($"[{nameof(GetMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {entries}");

				var playerEntry = entries[0];
				request.MatchId = playerEntry.MatchId;
				Logger.LogWarning($"   [{nameof(GetMatch)}] Found a match: {playerEntry.MatchId}");
			}

			// TODO Continue conversion of the GetMatch
			// Get the match with the id in the matches blob
			BlockBlobClient matchesBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{request.MatchId}.json");
			if (await matchesBlob.ExistsAsync())
			{
				PlayerInfo[] players = null;
				using (Stream stream = await matchesBlob.OpenReadAsync())
				{
					string serializedMatch = Helper.ReadBytes(stream);
					MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(serializedMatch);
					if (match.Status == (int)MatchStatus.Ended)
						return new MatchResponse { IsError = true, Message = $"Match is over." };
					players = new PlayerInfo[match.PlayerIds.Length];
					int playerIndex = 0;
					foreach (var player in match.PlayerInfos)
					{
						players[playerIndex] = player.Clone();
						playerIndex++;
					}
					Logger.LogWarning($"   [{nameof(GetMatch)}] Serialized match === {serializedMatch}");
				}

				return new MatchResponse
				{
					MatchId = request.MatchId,
					Players = players
				};
			}
			Logger.LogWarning($"   [{nameof(GetMatch)}] Found no match file with id {request.MatchId}");
			return new MatchResponse { IsError = true, Message = $"Match with id {request.MatchId} wasn't found." };
		}

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

					Logger.LogWarning("   [GetMatchState] StateRegistry = = " + JsonConvert.SerializeObject(currentState));

					return new StateResponse { StateInfo = currentState.GetStateInfo(request.PlayerId) };
				case (int)MatchStatus.Started:
				case (int)MatchStatus.Ended:
					currentState = await PrepareTurn(match, currentState);
					StateInfo info = currentState.GetStateInfo(request.PlayerId);
					if (info.Hash == request.LastHash)
						return new StateResponse { IsError = true, Message = "Current state is the same known state." };

					Logger.LogWarning("   [GetMatchState] StateRegistry = = " + JsonConvert.SerializeObject(currentState));

					return new StateResponse { StateInfo = info };
			}
			return new StateResponse { IsError = true, Message = "Match is in an unknown state." };
		}

		public static async Task DeleteMatch (string matchId)
		{
			Logger.LogWarning("   [DeleteMatch] " + matchId);
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
				Logger.LogError("   [PrepareTurn] Last state should not be null.");
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

		public static string CreateBotNickname ()
		{
			string result = "";
			for (int i = 0; i < 6; i++)
			{
				if (i == 0)
					result += consonantsUpper[rand.Next(0, consonantsUpper.Length)];
				else if (i % 2 == 0)
					result += consonantsLower[rand.Next(0, consonantsLower.Length)];
				else
					result += vowels[rand.Next(0, vowels.Length)];
			}
			return "Guest-" + result;
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
		Task<MatchmakingEntry[]> GetMatchmakingEntries (string region, string playerId, string matchId, MatchmakingStatus status);
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
