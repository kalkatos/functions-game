using Azure.Core;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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

		// ================================= L O G I N ==========================================

		public static async Task<LoginResponse> LogIn (LoginRequest request)
		{
			if (string.IsNullOrEmpty(request.Identifier) || string.IsNullOrEmpty(request.GameId))
				return new LoginResponse { IsError = true, Message = "Identifier is null. Must be an unique user identifier." };
			GameRegistry gameRegistry = await service.GetGameConfig(request.GameId);
			game.SetSettings(gameRegistry);
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

			bool mustRunLocally = await service.GetBool("MustRunLocally");

			return new LoginResponse
			{
				IsAuthenticated = playerRegistry.IsAuthenticated,
				PlayerId = playerRegistry.PlayerId,
				MyInfo = playerRegistry.Info,
				MustRunLocally = mustRunLocally
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
			if (playerRegistry.Info.CustomData == null)
				playerRegistry.Info.CustomData = new System.Collections.Generic.Dictionary<string, string>();
			foreach (var item in request.Data)
				playerRegistry.Info.CustomData[item.Key] = item.Value;
			await service.SetPlayerRegistry(playerRegistry);
			return new Response { Message = "Ok" };
		}

		// ================================= A C T I O N ==========================================

		public static async Task<ActionResponse> SendAction (ActionRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
			if (request.Action == null || (!request.Action.HasAnyPublicChange() && !request.Action.HasAnyPrivateChange()))
				return new ActionResponse { IsError = true, Message = "Action is null or empty." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new ActionResponse { IsError = true, Message = "Problem retrieving the match." };
			if (!match.HasPlayer(request.PlayerId))
				return new ActionResponse { IsError = true, Message = "Player is not on that match." };
			if (match.Status == (int)MatchStatus.Ended)
				return new ActionResponse { IsError = true, Message = "Match is over." };
			StateRegistry newState = null;
			for (int attempt = 0; attempt <= 5; ++attempt)
			{
				if (attempt >= 5)
					return new ActionResponse { IsError = true, Message = "Max attempts to Send Action reached." };
				StateRegistry state = await service.GetState(request.MatchId);
				if (state == null)
				{
					Logger.LogError("   [SendAction] State came out null. Retrying...");
					continue;
				}
				if (!game.IsActionAllowed(request.PlayerId, request.Action, match, state))
					return new ActionResponse { IsError = true, Message = "Action is not allowed." };
				newState = state.Clone();
				if (request.Action.PublicChanges != null)
					newState.UpsertPublicProperties(request.Action.PublicChanges);
				if (request.Action.PrivateChanges != null)
					newState.UpsertPrivateProperties(request.PlayerId, request.Action.PrivateChanges);
				if (state != null && state.Hash == newState.Hash)
				{
					Logger.LogError($"   [SendAction] state hash and newState hash are equal:\n>> State = {JsonConvert.SerializeObject(state)}\n\n>> NewState = {JsonConvert.SerializeObject(newState)}");
					return new ActionResponse { IsError = true, Message = "Action is already registered." };
				}
				if (!await service.SetState(request.MatchId, state, newState))
				{
					Logger.LogError("   [SendAction] States didn't match, retrying....");
					continue;
				}
				break;
			}
			Logger.LogWarning($"   [SendAction] State after action = {JsonConvert.SerializeObject(newState)}");
			return new ActionResponse { AlteredState = newState.GetStateInfo(request.PlayerId), Message = "Action registered successfully." };
		}

		// ================================= M A T C H ==========================================

		public static async Task<Response> FindMatch (FindMatchRequest request)
		{
			if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.GameId))
				return new MatchResponse { IsError = true, Message = "Wrong Parameters." };
			MatchmakingEntry[] entries = await service.GetMatchmakingEntries(request.Region, request.PlayerId, null, MatchmakingStatus.Undefined);
			if (entries != null && entries.Length > 1)
				foreach (MatchmakingEntry entry in entries)
					await service.DeleteMatchmakingHistory(entry.PlayerId, entry.MatchId);
			string playerInfoSerialized = JsonConvert.SerializeObject((await service.GetPlayerRegistry(request.PlayerId)).Info);
			await service.UpsertMatchmakingEntry(
				new MatchmakingEntry 
				{ 
					Region = request.Region, 
					PlayerId = request.PlayerId, 
					Status = MatchmakingStatus.Searching,
					PlayerInfoSerialized = playerInfoSerialized
				});

			await TryToMatchPlayers(request.GameId, request.Region);

			return new Response { Message = "Find match request registered successfully." };
		}

		public static async Task<MatchResponse> GetMatch (MatchRequest request)
		{
			if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.GameId))
				return new MatchResponse { IsError = true, Message = "Wrong Parameters." };

			if (string.IsNullOrEmpty(request.MatchId))
			{
				// Try to get entries two times 
				for (int attempt = 1; attempt <= 2; attempt++)
				{
					// Get the match id of the match assigned to the player or find matches
					MatchmakingEntry[] entries = await service.GetMatchmakingEntries(null, request.PlayerId, null, MatchmakingStatus.Undefined);
					if (entries == null || entries.Length == 0)
						return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
					if (entries.Length > 1)
						Logger.LogWarning($"[{nameof(GetMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {entries}");
					var playerEntry = entries[0];
					if (attempt == 1 && string.IsNullOrEmpty(playerEntry.MatchId))
						await TryToMatchPlayers(request.GameId, request.GameId);
					else
						request.MatchId = playerEntry.MatchId;
				}
				if (string.IsNullOrEmpty(request.MatchId))
					return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
			}
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new MatchResponse { IsError = true, Message = $"Match with id {request.MatchId} wasn't found." };
			if (match.Status == (int)MatchStatus.Ended)
				return new MatchResponse { IsError = true, Message = $"Match is over." };
			PlayerInfo[] playerInfos = new PlayerInfo[match.PlayerInfos.Length];
			for (int i = 0; i < match.PlayerInfos.Length; i++)
				playerInfos[i] = match.PlayerInfos[i].Clone();
			return new MatchResponse
			{
				MatchId = request.MatchId,
				Players = playerInfos
			};
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
			_ = Task.Run(async () => 
			{
				GameRegistry gameRegistry = await service.GetGameConfig(game.GameId);
				await service.ScheduleCheckMatch(gameRegistry.FirstCheckMatchDelay * 1000, matchId, 0); 
			});
		}

		public static async Task CheckMatch (string matchId, int lastHash)
		{
			StateRegistry state = await service.GetState(matchId);
			if (state == null)
				return;
			if (!HasHandshakingFromAllPlayers(state) || state.Hash == lastHash)
				await DeleteMatch(matchId);
			else
			{
				GameRegistry gameRegistry = await service.GetGameConfig(game.GameId);
				await service.ScheduleCheckMatch(gameRegistry.RecurrentCheckMatchDelay * 1000, matchId, state.Hash); 
			}
		}

		// ================================= S T A T E ==========================================

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
					currentState = await PrepareTurn (request.PlayerId, match, currentState);

					Logger.LogWarning("   [GetMatchState] StateRegistry = = " + JsonConvert.SerializeObject(currentState));

					return new StateResponse { StateInfo = currentState.GetStateInfo(request.PlayerId) };
				case (int)MatchStatus.Started:
				case (int)MatchStatus.Ended:
					currentState = await PrepareTurn(request.PlayerId, match, currentState);
					StateInfo info = currentState.GetStateInfo(request.PlayerId);
					if (info.Hash == request.LastHash)
						return new StateResponse { IsError = true, Message = "Current state is the same known state." };

					Logger.LogWarning("   [GetMatchState] StateRegistry = = " + JsonConvert.SerializeObject(currentState));

					return new StateResponse { StateInfo = info };
			}
			return new StateResponse { IsError = true, Message = "Match is in an unknown state." };
		}

		// Temp
		public static void CreateFirstState (MatchRegistry match)
		{
			_ = Task.Run(async () => await CreateFirstStateAndRegister(match));
		}

		// ================================= P R I V A T E ==========================================

		private static async Task<string> TryToMatchPlayers (string gameId, string region)
		{
			// TODO TryToMatchPlayers
			// Get settings for matchmaking
			GameRegistry gameRegistry = await service.GetGameConfig(gameId);
			int minPlayers = gameRegistry.HasSetting("MinPlayerCount") ? int.Parse(gameRegistry.GetValue("MinPlayerCount")) : 2;
			// Get entries for that region
			MatchmakingEntry[] entries = await service.GetMatchmakingEntries(region, null, null, MatchmakingStatus.Undefined);
			if (entries.Length >= minPlayers) 
			{
				List<MatchmakingEntry> matchCandidates = new List<MatchmakingEntry>();
				for (int i = 0; i < entries.Length; i++)
				{
					if (entries[i] == null)
						continue;
					if (entries[i].Status == MatchmakingStatus.Searching)
						matchCandidates.Add(entries[i]);
				}
				for (int i = 0; i < matchCandidates.Count; i += minPlayers)
				{
					string matchId = Guid.NewGuid().ToString();
					string[] ids = new string[minPlayers];
					for (int j = i; j < i + minPlayers; j++)
					{
						MatchmakingEntry entry = matchCandidates[j];
						entry.MatchId = matchId;
						entry.Status = MatchmakingStatus.Matched;
						ids[j - i] = entry.PlayerId;
						await service.UpsertMatchmakingEntry(entry);
					}
					MatchRegistry match = new MatchRegistry
					{
						MatchId = matchId,
						CreatedTime = DateTime.UtcNow,
						PlayerIds = ids,
						Region = region,

					};
				}
			}
			// If enough players, match them
			// Else if entries are old enough and can match with bots, do so

			async void CreateMatch (MatchmakingEntry[] entries)
			{

			}
			
			return null;
		}

		private static async Task<StateRegistry> CreateFirstStateAndRegister (MatchRegistry match)
		{
			StateRegistry newState = game.CreateFirstState(match);
			await service.SetState(match.MatchId, null, newState);
			return newState;
		}

		private static async Task<StateRegistry> PrepareTurn (string requesterId, MatchRegistry match, StateRegistry lastState)
		{
			if (lastState == null)
				Logger.LogError("   [PrepareTurn] Last state should not be null.");
			StateRegistry newState = game.PrepareTurn(requesterId, match, lastState);
			if (newState.IsMatchEnded && match.Status != (int)MatchStatus.Ended)
			{
				match.Status = (int)MatchStatus.Ended;
				await service.SetMatchRegistry(match);
				if (!await service.SetState(match.MatchId, lastState, newState))
				{
					Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
					return await PrepareTurn(requesterId, match, await service.GetState(match.MatchId)); 
				}
				GameRegistry gameRegistry = await service.GetGameConfig(game.GameId);
				await service.ScheduleCheckMatch(gameRegistry.FinalCheckMatchDelay * 1000, match.MatchId, newState.Hash);
				return newState;
			}
			if (lastState != null && newState.Hash == lastState.Hash)
				return lastState;
			if (!await service.SetState(match.MatchId, lastState, newState))
			{
				Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
				return await PrepareTurn(requesterId, match, await service.GetState(match.MatchId)); 
			}
			return newState;
		}

		private static bool HasHandshakingFromAllPlayers (StateRegistry state)
		{
			if (state == null)
			{
				Logger.LogError($"   [HasHandshakingFromAllPlayers] State is null");
				return false;
			}
			var players = state.GetPlayers();
			int count = 0;
			string playersWithHandshaking = "";
			foreach (var player in players)
				if (player[0] == 'X' || !string.IsNullOrEmpty(state.GetPrivate(player, "Handshaking")))
				{
					count++;
					playersWithHandshaking += $"| {player}";
				}
			if (count != players.Length)
				Logger.LogError($"   [HasHandshakingFromAllPlayers] Player with handshaking = {count} = {playersWithHandshaking}\n{JsonConvert.SerializeObject(state)}");
			return count == players.Length;
		}

		// Temporarily public
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
}
