using Kalkatos.Network.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public static class MatchFunctions
{
	private static IService service = Global.Service;
	private static IGame game = Global.Game;

	// ████████████████████████████████████████████ A C T I O N ████████████████████████████████████████████

	public static async Task<ActionResponse> SendAction (ActionRequest request)
	{
		if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
			return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
		if (request.Action == null || (!request.Action.HasAnyPublicChange() && !request.Action.HasAnyPrivateChange()))
			return new ActionResponse { IsError = true, Message = "Action is null or empty." };
		string matchSerialized = await service.GetData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
		if (string.IsNullOrEmpty(matchSerialized))
			return new ActionResponse { IsError = true, Message = "Problem retrieving the match." };
		MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);
		if (!match.HasPlayer(request.PlayerId))
			return new ActionResponse { IsError = true, Message = "Player is not on that match." };
		if (match.IsEnded)
			return new ActionResponse { IsError = true, Message = "Match is over." };
		string stateSerialized = await service.GetData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
		StateRegistry state = JsonConvert.DeserializeObject<StateRegistry>(stateSerialized);

		if (request.Action.HasAnyChangeWithKey(Global.START_MATCH_KEY) && state.TurnNumber == -1)
		{
			await CheckGameSettings();
			if (match.PlayerIds.Length < game.Settings.MinPlayersPerMatch)
				return new ActionResponse { IsError = true, Message = "Match does not have enough players." };
			await StartMatch(match, state);
			return new ActionResponse { Message = "Match started." };
		}

		if (!game.IsActionAllowed(request.PlayerId, request.Action, match, state))
			return new ActionResponse { IsError = true, Message = "Action is not allowed." };
		string id = Guid.NewGuid().ToString();
		ActionRegistry action = new ActionRegistry
		{
			Id = id,
			MatchId = request.MatchId,
			PlayerId = request.PlayerId,
			Action = request.Action,
		};
		await service.UpsertData(Global.ACTIONS_TABLE, request.MatchId, id, JsonConvert.SerializeObject(action));
		return new ActionResponse { Message = "Action registered successfully." };
	}

	// ████████████████████████████████████████████ M A T C H ████████████████████████████████████████████

	public static async Task<Response> FindMatch (FindMatchRequest request)
	{
		if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.GameId))
			return new Response { IsError = true, Message = "Wrong Parameters." };
		string entrySerialized = await service.GetData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (!string.IsNullOrEmpty(entrySerialized))
		{
			MatchmakingEntry entry = JsonConvert.DeserializeObject<MatchmakingEntry>(entrySerialized);
			if (entry.Status == MatchmakingStatus.Searching
				&& (DateTimeOffset.UtcNow - entry.Timestamp).TotalSeconds < 60)
				return new Response { IsError = true, Message = "Is still searching for match." };
		}
		string playerSerialized = await service.GetData("Players", Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (string.IsNullOrEmpty(playerSerialized))
			return new Response { IsError = true, Message = "Player not found." };
		PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerSerialized);
		string playerInfoSerialized = JsonConvert.SerializeObject(playerRegistry.Info);
		MatchmakingEntry newEntry = new MatchmakingEntry
		{
			Region = Global.DEFAULT_PARTITION,
			PlayerId = request.PlayerId,
			Status = MatchmakingStatus.Searching,
			StatusDescription = MatchmakingStatus.Searching.ToString(),
			UseLobby = request.UseLobby,
			PlayerInfoSerialized = playerInfoSerialized
		};
		entrySerialized = JsonConvert.SerializeObject(newEntry);
		await service.UpsertData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, entrySerialized);
		Logger.LogWarning($"[FindMatch]   >>>  MatchmakingEntry registered: {entrySerialized}");
		await TryToMatchPlayers(request.GameId, Global.DEFAULT_PARTITION);
		return new Response { Message = "Find match request registered successfully." };
	}

	public static async Task<MatchResponse> GetMatch (MatchRequest request)
	{
		if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.GameId))
			return new MatchResponse { IsError = true, Message = "Wrong Parameters." };

		bool isGettingByAlias = !string.IsNullOrEmpty(request.Alias);
		if (string.IsNullOrEmpty(request.MatchId))
		{
			// Try to get entries two times 
			for (int attempt = 1; attempt <= 2; attempt++)
			{
				string entrySerialized = "";
				if (isGettingByAlias)
				{
					entrySerialized = await service.GetData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, request.Alias, "");
					if (string.IsNullOrEmpty(entrySerialized))
						return new MatchResponse { IsError = true, Message = $"Didn't find any match with alias {request.Alias}." };
				}
				else
				{
					entrySerialized = await service.GetData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
					if (string.IsNullOrEmpty(entrySerialized))
						return new MatchResponse { IsError = true, Message = $"Player must be registered for matchmaking first." };
				}
				MatchmakingEntry entry = JsonConvert.DeserializeObject<MatchmakingEntry>(entrySerialized);
				if (entry.Status == MatchmakingStatus.FailedWithNoPlayers)
					return new MatchResponse { IsError = true, Message = $"Matchmaking failed with no players." };
				if (entry.Status == MatchmakingStatus.Canceled)
					return new MatchResponse { IsError = true, Message = $"Match is cancelled." };
				if (attempt == 1 && string.IsNullOrEmpty(entry.MatchId))
					await TryToMatchPlayers(request.GameId, Global.DEFAULT_PARTITION);
				request.MatchId = entry.MatchId;
			}
			if (string.IsNullOrEmpty(request.MatchId))
				return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
		}
		string matchSerialized = await service.GetData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
		if (string.IsNullOrEmpty(matchSerialized))
			return new MatchResponse { IsError = true, Message = $"Match with id {request.MatchId} wasn't found." };
		MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);
		if (isGettingByAlias && !match.PlayerIds.Contains(request.PlayerId))
		{
			if (match.IsStarted)
				return new MatchResponse { IsError = true, Message = "Match is already started, cannot add players anymore." };
			PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(await service.GetData("Players", Global.DEFAULT_PARTITION, request.PlayerId, ""));
			await CheckGameSettings();
			if (match.PlayerIds.Length >= game.Settings.MaxPlayersPerMatch)
				return new MatchResponse { IsError = true, Message = "Match is full." };
			match.AddPlayer(playerRegistry);
			await service.UpsertData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(match));
			await CreateFirstStateAndRegister(match);
			string playerInfoSerialized = JsonConvert.SerializeObject(playerRegistry.Info);
			MatchmakingEntry entry = new MatchmakingEntry
			{
				Region = Global.DEFAULT_PARTITION,
				PlayerId = request.PlayerId,
				Status = MatchmakingStatus.InLobby,
				StatusDescription = MatchmakingStatus.InLobby.ToString(),
				UseLobby = true,
				Alias = request.Alias,
				MatchId = match.MatchId,
				PlayerInfoSerialized = playerInfoSerialized,
			};
			await service.UpsertData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.PlayerId, JsonConvert.SerializeObject(entry));
		}
		PlayerInfo[] playerInfos = new PlayerInfo[match.PlayerInfos.Length];
		for (int i = 0; i < match.PlayerInfos.Length; i++)
			playerInfos[i] = match.PlayerInfos[i].Clone();
		return new MatchResponse
		{
			MatchId = request.MatchId,
			Players = playerInfos,
			IsEnded = match.IsEnded,
			Alias = match.Alias,
			IsStarted = match.IsStarted,
		};
	}

	public static async Task<Response> LeaveMatch (MatchRequest request)
	{
		if (request == null || string.IsNullOrEmpty(request.PlayerId))
			return new Response { IsError = true, Message = "Wrong Parameters." };

		if (string.IsNullOrEmpty(request.MatchId))
		{
			string entrySerialized = await service.GetData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
			if (string.IsNullOrEmpty(entrySerialized))
				return new Response { IsError = true, Message = $"Didn't find any match for player." };
			MatchmakingEntry playerEntry = JsonConvert.DeserializeObject<MatchmakingEntry>(entrySerialized);
			if (string.IsNullOrEmpty(playerEntry.MatchId))
			{
				await service.DeleteData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, request.PlayerId);
				return new Response { Message = $"Leave match executed by wiping matchmaking entries." };
			}
			request.MatchId = playerEntry.MatchId;
		}

		string matchSerialized = await service.GetData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
		if (string.IsNullOrEmpty(matchSerialized))
			return new Response { IsError = true, Message = "Problem getting match." };
		MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);
		if (!match.PlayerIds.Contains(request.PlayerId))
			return new Response { IsError = true, Message = "Player is not in that match." };
		if (match.IsEnded)
			return new Response { Message = "Match is already over." };
		string stateSerialized = await service.GetData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
		if (string.IsNullOrEmpty(stateSerialized))
			return new Response { IsError = true, Message = "Problem getting the match state." };
		StateRegistry currentState = JsonConvert.DeserializeObject<StateRegistry>(stateSerialized);
		if (currentState.IsMatchEnded)
			return new Response { Message = "Match is already over." };

		if (!match.IsStarted)
		{
			int index = Array.IndexOf(match.PlayerIds, request.PlayerId);
			match.PlayerIds = match.PlayerIds.Where(p => p != request.PlayerId).ToArray();
			var infoList = match.PlayerInfos.ToList();
			infoList.RemoveAt(index);
			match.PlayerInfos = infoList.ToArray();
			currentState = game.CreateFirstState(match);
			await service.UpsertData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, JsonConvert.SerializeObject(currentState));
			await service.UpsertData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, JsonConvert.SerializeObject(match));
		}
		else
		{
			StateRegistry newState = currentState.Clone();
			newState.UpsertPrivateProperties((request.PlayerId, Global.RETREATED_KEY, "1"));
			await service.UpsertData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, JsonConvert.SerializeObject(newState));
			newState = await PrepareTurn(request.PlayerId, match, newState);
		}
		return new Response { Message = $"Added player as retreated in {request.MatchId} successfully." };
	}

	public static async Task DeleteEverythingFromMatch (string matchId)
	{
		Logger.LogWarning("   [DeleteMatch] " + matchId);
		if (string.IsNullOrEmpty(matchId))
			return;
		var matchmakingDict = await service.GetAllData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, $"MatchId eq '{matchId}'");
		foreach (var kv in matchmakingDict)
		{
			MatchmakingEntry entry = JsonConvert.DeserializeObject<MatchmakingEntry>(kv.Value);
			await service.DeleteData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.PlayerId);
			if (!string.IsNullOrEmpty(entry.Alias))
				await service.DeleteData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.Alias);
		}
		await service.DeleteData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, matchId);
		await service.DeleteData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, matchId);
		var actionsDict = await service.GetAllData(Global.ACTIONS_TABLE, matchId, null);
		foreach (var kv in actionsDict)
		{
			ActionRegistry registry = JsonConvert.DeserializeObject<ActionRegistry>(kv.Value);
			await service.DeleteData(Global.ACTIONS_TABLE, matchId, registry.Id);
		}
	}

	public static async Task<int?> CheckMatch (string matchId, int lastHash)
	{
		string stateSerialized = await service.GetData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, matchId, "");
		if (string.IsNullOrEmpty(stateSerialized))
		{
			Logger.LogWarning($"   [CheckMatch] Deleting match {matchId}. Reason: no state.");
			await DeleteEverythingFromMatch(matchId);
			return null;
		}
		StateRegistry state = JsonConvert.DeserializeObject<StateRegistry>(stateSerialized);
		if (state.TurnNumber >= 0)
		{
			if (state.Hash == lastHash)
			{
				Logger.LogWarning($"   [CheckMatch] Deleting match {matchId}. Reason: no modifications (hash={lastHash})");
				await DeleteEverythingFromMatch(matchId);
				return null;
			}
		}
		else // In Lobby
		{
			string matchSerialized = await service.GetData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, matchId, "");
			if (string.IsNullOrEmpty(matchSerialized))
			{
				Logger.LogWarning($"   [CheckMatch] Deleting match {matchId}. Reason: no match registry.");
				await DeleteEverythingFromMatch(matchId);
				return null;
			}
			MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);
			await CheckGameSettings();
			if (state.TurnNumber == -1 && (DateTimeOffset.UtcNow - match.CreatedTime).TotalSeconds >= game.Settings.LobbyDuration)
			{
				Logger.LogWarning($"   [CheckMatch] Deleting match {matchId}. Reason: lobby {match.Alias} expired.");
				await DeleteEverythingFromMatch(matchId);
				return null;
			}
		}
		Logger.LogWarning($"   [CheckMatch] Match {matchId} not deleted. A new check should be scheduled.");
		return state.Hash;
	}

	// ████████████████████████████████████████████ S T A T E ████████████████████████████████████████████

	public static async Task<StateResponse> GetMatchState (StateRequest request)
	{
		try
		{
			if (string.IsNullOrEmpty(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player id may not be null." };
			if (string.IsNullOrEmpty(request.MatchId))
				return new StateResponse { IsError = true, Message = "Match id may not be null." };
			string matchSerialized = await service.GetData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
			if (string.IsNullOrEmpty(matchSerialized))
				return new StateResponse { IsError = true, Message = "Match not found." };
			MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			string stateSerialized = await service.GetData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, request.MatchId, "");
			StateRegistry currentState = JsonConvert.DeserializeObject<StateRegistry>(stateSerialized);
			if (currentState == null)
				return new StateResponse { IsError = true, Message = "Service problem when getting current state." };
			if (currentState.TryGetPrivate(request.PlayerId, Global.RETREATED_KEY, out string value) && value == "1")
				return new StateResponse { IsError = true, Message = "Player has left match." };
			StateInfo info = currentState.GetStateInfo(request.PlayerId);
			if (!match.IsStarted)
				return new StateResponse { Message = "Match has not started yet.", StateInfo = info };
			if (match.IsEnded || currentState.IsMatchEnded)
				return new StateResponse { Message = "Match is ended.", StateInfo = info };
			currentState = await PrepareTurn(request.PlayerId, match, currentState);
			info = currentState.GetStateInfo(request.PlayerId);
			if (currentState.Hash == request.LastHash)
				return new StateResponse { Message = "It is the same known state.", StateInfo = info };
			return new StateResponse { StateInfo = info };
		}
		catch (Exception e)
		{
			return new StateResponse { IsError = true, Message = e.Message };
		}
	}

	#region Private
	// ███████████████████████████████████████████ P R I V A T E ████████████████████████████████████████████

	private static async Task StartMatch (MatchRegistry match, StateRegistry state)
	{
		if (match.IsStarted)
			return;
		int stateHash = state.Hash;
		state.TurnNumber = 0;
		match.IsStarted = true;
		match.StartTime = DateTimeOffset.UtcNow;
		await service.UpsertData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(match));
		await service.UpsertData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(state));
	}

	private static async Task TryToMatchPlayers (string gameId, string region)
	{
		await CheckGameSettings();
		// Get settings for matchmaking
		GameRegistry settings = game.Settings;
		int playerCount = settings.MinPlayersPerMatch;
		float maxWaitToMatchWithBots = (settings.HasSetting("MaxWait") && float.TryParse(settings.GetValue("MaxWait"), out float wait)) ? wait : 6.0f;
		string actionForNoPlayers = settings.HasSetting("ActionForNoPlayers") ? settings.GetValue("ActionForNoPlayers") : "MatchWithBots";
		// Get entries for that region
		MatchmakingEntry[] entries = (await service.GetAllData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, null)).Values
			.Select(JsonConvert.DeserializeObject<MatchmakingEntry>)
			.ToArray();

		List<MatchmakingEntry> matchCandidates = new List<MatchmakingEntry>();
		for (int i = 0; i < entries.Length; i++)
		{
			if (entries[i] == null)
				continue;
			if (entries[i].Status == MatchmakingStatus.Searching)
			{
				matchCandidates.Add(entries[i]);
				if (entries[i].UseLobby)
				{
					await CreateMatch(matchCandidates, false, true);
					return;
				}
			}
		}
		while (matchCandidates.Count >= playerCount)
		{
			List<MatchmakingEntry> range = matchCandidates.GetRange(0, playerCount);
			matchCandidates.RemoveRange(0, playerCount);
			await CreateMatch(range, false, false);
		}
		if (matchCandidates.Count == 0)
			return;
		DateTimeOffset entriesMaxTimestamp = matchCandidates.Max(e => e.Timestamp);
		if ((DateTimeOffset.UtcNow - entriesMaxTimestamp).TotalSeconds >= maxWaitToMatchWithBots)
		{
			if (actionForNoPlayers == "MatchWithBots")
			{
				// Match with bots
				int candidatesCount = matchCandidates.Count;
				for (int i = 0; i < playerCount - candidatesCount; i++)
				{
					// Add bot entry to the matchmaking table
					string botId = "X" + Guid.NewGuid().ToString();
					string botAlias = Guid.NewGuid().ToString();
					PlayerInfo botInfo = game.CreateBot(settings.BotSettings);
					matchCandidates.Add(
						new MatchmakingEntry
						{
							PlayerId = botId,
							PlayerInfoSerialized = JsonConvert.SerializeObject(botInfo),
							Region = region,
							Status = MatchmakingStatus.Searching,
							StatusDescription = MatchmakingStatus.Searching.ToString(),
							Timestamp = DateTimeOffset.UtcNow
						});
				}
				await CreateMatch(matchCandidates, true, false);
			}
			else
			{
				foreach (var entry in matchCandidates)
				{
					entry.Status = MatchmakingStatus.FailedWithNoPlayers;
					entry.StatusDescription = MatchmakingStatus.FailedWithNoPlayers.ToString();
					await service.UpsertData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.PlayerId, JsonConvert.SerializeObject(entry));
				}
			}
		}

		async Task CreateMatch (List<MatchmakingEntry> entries, bool hasBots, bool isLobby)
		{
			string debugLog = "[CreateMatch] Creating ids array  |  ";
			try
			{
				string matchId = Guid.NewGuid().ToString();
				string[] ids = new string[entries.Count];
				debugLog += $"Creating infos array  |  ";
				PlayerInfo[] infos = new PlayerInfo[entries.Count];
				debugLog += $"Getting a random alias  |  ";
				string alias = Helper.GetRandomMatchAlias(4, false);
				for (int i = 0; i < entries.Count; i++)
				{
					MatchmakingEntry entry = entries[i];
					debugLog += $"Analysing entry {entry.PlayerId}  |  ";
					ids[i] = entry.PlayerId;
					infos[i] = JsonConvert.DeserializeObject<PlayerInfo>(entry.PlayerInfoSerialized);
					if (entry.PlayerId[0] == 'X')
						continue;
					entry.Alias = alias;
					entry.MatchId = matchId;
					entry.Status = isLobby ? MatchmakingStatus.InLobby : MatchmakingStatus.Matched;
					entry.StatusDescription = entry.Status.ToString();
					debugLog += $"Upserting matchmaking entry  |  ";
					string entrySerialized = JsonConvert.SerializeObject(entry);
					await service.UpsertData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.PlayerId, entrySerialized);
					if (entry.UseLobby)
						await service.UpsertData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.Alias, entrySerialized);
				}
				MatchRegistry match = new MatchRegistry
				{
					GameId = gameId,
					MatchId = matchId,
					CreatedTime = DateTimeOffset.UtcNow,
					PlayerIds = ids,
					PlayerInfos = infos,
					Alias = alias,
					Region = region,
					HasBots = hasBots,
					UseLobby = isLobby,
				};
				debugLog += $"Registering match  |  ";
				await service.UpsertData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, matchId, JsonConvert.SerializeObject(match));
				debugLog += $"Creating first state  |  ";
				StateRegistry state = await CreateFirstStateAndRegister(match);
				if (!isLobby)
				{
					debugLog += $"Scheduling check match  |  ";
					await StartMatch(match, state);
				}
			}
			catch (Exception e)
			{
				throw new Exception($"Problem creating match: {e.Message}  >>>> {debugLog}\n{e.StackTrace}");
			}
		}
	}

	private static async Task<StateRegistry> CreateFirstStateAndRegister (MatchRegistry match)
	{
		await CheckGameSettings();
		StateRegistry newState = game.CreateFirstState(match);
		if (match.UseLobby)
			newState.TurnNumber = -1;
		await service.UpsertData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(newState));
		return newState;
	}

	private static async Task<StateRegistry> PrepareTurn (string requesterId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions = null)
	{
		string debug = " <PrepareTurn> ";
		try
		{
			if (lastState == null)
				Logger.LogError("   [PrepareTurn] Last state should not be null.");
			debug += "Getting actions | ";
			if (actions == null)
				actions = (await service.GetAllData(Global.ACTIONS_TABLE, match.MatchId, $"IsProcessed eq 'False'"))
					.Values.Select(JsonConvert.DeserializeObject<ActionRegistry>)
					.ToList();
			debug += "Prep turn in Game | ";
			StateRegistry newState = game.PrepareTurn(requesterId, match, lastState, actions);
			if (newState == lastState)
				return lastState;
			debug += "Update actions | ";
			foreach (var action in actions)
				await service.UpsertData(Global.ACTIONS_TABLE, match.MatchId, action.Id, JsonConvert.SerializeObject(action));
			string registeredStateSerialized = null;
			StateRegistry registeredState = null;
			if (newState.IsMatchEnded && !match.IsEnded)
			{
				match.IsEnded = true;
				match.EndedTime = DateTimeOffset.UtcNow;
				await service.UpsertData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(match));
				var matchmakingDict = await service.GetAllData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, $"MatchId eq '{match.MatchId}'");
				foreach (var kv in matchmakingDict)
				{
					MatchmakingEntry entry = JsonConvert.DeserializeObject<MatchmakingEntry>(kv.Value);
					await service.DeleteData(Global.MATCHMAKING_TABLE, Global.DEFAULT_PARTITION, entry.PlayerId);
				}
				registeredStateSerialized = await service.GetData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, "");
				registeredState = JsonConvert.DeserializeObject<StateRegistry>(registeredStateSerialized);
				if (registeredState != lastState)
				{
					Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
					debug += "RE-Prepare turn | ";
					return await PrepareTurn(requesterId, match, registeredState, actions.Where(a => !a.IsProcessed).ToList());
				}
				await service.UpsertData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(newState));
				return newState;
			}
			debug += "Setting new state | ";
			registeredStateSerialized = await service.GetData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, "");
			registeredState = JsonConvert.DeserializeObject<StateRegistry>(registeredStateSerialized);
			if (registeredState != lastState)
			{
				Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
				debug += "RE-Prepare turn | ";
				return await PrepareTurn(requesterId, match, registeredState, actions.Where(a => !a.IsProcessed).ToList());
			}
			await service.UpsertData(Global.STATES_TABLE, Global.DEFAULT_PARTITION, match.MatchId, JsonConvert.SerializeObject(newState));
			return newState;
		}
		catch (Exception e)
		{
			throw new Exception($"{e.Message} >>> {debug} \nStack Trace:\n{e.StackTrace}");
		}
	}

	public static async Task CheckGameSettings ()
	{
		string settingsSerialized = await service.GetData(Global.DATA_TABLE, Global.GAME_PARTITION, game.Name, "");
		GameRegistry settings = null;
		if (string.IsNullOrEmpty(settingsSerialized))
		{
			Logger.LogError("Game has no settings. Creating a default one.");
			settings = new GameRegistry();
			await service.UpsertData(Global.DATA_TABLE, Global.GAME_PARTITION, game.Name, JsonConvert.SerializeObject(settings));
		}
		else
			settings = JsonConvert.DeserializeObject<GameRegistry>(settingsSerialized);
		game.SetSettings(settings);
	}
	#endregion
}
