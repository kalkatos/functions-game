using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Core;

namespace Kalkatos.FunctionsGame
{
    public static class MatchFunctions
    {
        private static ILoginService loginService = GlobalConfigurations.LoginService;
        private static IMatchService matchService = GlobalConfigurations.MatchService;
        private static IGame game = GlobalConfigurations.Game;

        private const int CHECK_MATCH_DELAY = 30;
        private const int LOBBY_DURATION = 3600;

        // ████████████████████████████████████████████ A C T I O N ████████████████████████████████████████████

        public static async Task<ActionResponse> SendAction (ActionRequest request)
        {
            if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
                return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
            if (request.Action == null || (!request.Action.HasAnyPublicChange() && !request.Action.HasAnyPrivateChange()))
                return new ActionResponse { IsError = true, Message = "Action is null or empty." };
            MatchRegistry match = await matchService.GetMatchRegistry(request.MatchId);
            if (match == null)
                return new ActionResponse { IsError = true, Message = "Problem retrieving the match." };
            if (!match.HasPlayer(request.PlayerId))
                return new ActionResponse { IsError = true, Message = "Player is not on that match." };
            if (match.IsEnded)
                return new ActionResponse { IsError = true, Message = "Match is over." };
            StateRegistry state = await matchService.GetState(request.MatchId);

            if (request.Action.HasPublicChange("StartMatch") && state.TurnNumber == -1)
            {
                await StartMatch(match, state);
                return new ActionResponse { Message = "Match started." };
            }

            if (!game.IsActionAllowed(request.PlayerId, request.Action, match, state))
                return new ActionResponse { IsError = true, Message = "Action is not allowed." };
            await matchService.AddAction(request.MatchId, request.PlayerId, 
                new ActionRegistry 
                { 
                    Id = Guid.NewGuid().ToString(),
                    MatchId = request.MatchId, 
                    PlayerId = request.PlayerId, 
                    Action = request.Action, 
                });
            return new ActionResponse { Message = "Action registered successfully." };
        }

        // ████████████████████████████████████████████ M A T C H ████████████████████████████████████████████

        public static async Task<Response> FindMatch (FindMatchRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.GameId))
                return new Response { IsError = true, Message = "Wrong Parameters." };
            MatchmakingEntry[] entries = await matchService.GetMatchmakingEntries(request.Region, null, request.PlayerId, null, MatchmakingStatus.Undefined);
            if (entries != null && entries.Length > 1)
                foreach (MatchmakingEntry entry in entries)
                    await matchService.DeleteMatchmakingHistory(entry.PlayerId, entry.MatchId);
            string playerInfoSerialized = JsonConvert.SerializeObject((await loginService.GetPlayerRegistry(request.PlayerId)).Info);
            MatchmakingEntry newEntry = new MatchmakingEntry
            {
                Region = request.Region,
                PlayerId = request.PlayerId,
                Status = MatchmakingStatus.Searching,
                UseLobby = request.UseLobby,
                PlayerInfoSerialized = playerInfoSerialized
            };
            await matchService.UpsertMatchmakingEntry(newEntry);
            Logger.LogWarning($"[FindMatch]   >>>  MatchmakingEntry registered: {JsonConvert.SerializeObject(newEntry)}");
            await TryToMatchPlayers(request.GameId, request.Region);
            return new Response { Message = "Find match request registered successfully." };
        }

        public static async Task<MatchResponse> GetMatch (MatchRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.GameId))
                return new MatchResponse { IsError = true, Message = "Wrong Parameters." };

            bool isGettingByAlias = !string.IsNullOrEmpty(request.Alias);
            if (string.IsNullOrEmpty(request.MatchId))
            {
                // Try to get entries two times 
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    MatchmakingEntry[] entries = null;
                    if (isGettingByAlias)
                    {
                        // Requesting by alias
                        entries = await matchService.GetMatchmakingEntries(null, null, null, request.Alias, MatchmakingStatus.Undefined);
                        if (entries == null || entries.Length == 0)
                            return new MatchResponse { IsError = true, Message = $"Didn't find any match with alias {request.Alias}." };
                    }
                    else
                    {
                        // Get the match id of the match assigned to the player or find matches
                        entries = await matchService.GetMatchmakingEntries(null, null, request.PlayerId, null, MatchmakingStatus.Undefined);
                        if (entries == null || entries.Length == 0)
                            return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
                    }
                    if (entries.Length > 1)
                        Logger.LogError($"[{nameof(GetMatch)}] More than one entry found! Player = {request.PlayerId} Alias = {request.Alias} Query = {JsonConvert.SerializeObject(entries)}");
                    MatchmakingEntry playerEntry = entries[0];
                    if (playerEntry.Status == MatchmakingStatus.FailedWithNoPlayers)
                        return new MatchResponse { IsError = true, Message = $"Matchmaking failed with no players." };
                    if (playerEntry.Status == MatchmakingStatus.Canceled)
                        return new MatchResponse { IsError = true, Message = $"Match is cancelled." };
                    if (attempt == 1 && string.IsNullOrEmpty(playerEntry.MatchId))
                        await TryToMatchPlayers(request.GameId, request.Region);
                    else
                        request.MatchId = playerEntry.MatchId;
                }
                if (string.IsNullOrEmpty(request.MatchId))
                    return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
            }
            MatchRegistry match = await matchService.GetMatchRegistry(request.MatchId);
            if (match == null)
                return new MatchResponse { IsError = true, Message = $"Match with id {request.MatchId} wasn't found." };
            if (match.IsEnded)
            {
                await DeleteMatch(match.MatchId);
                return new MatchResponse { IsError = true, Message = $"Match is over.", IsOver = true };
            }
            if (isGettingByAlias 
                && !match.PlayerIds.Contains(request.PlayerId))
            {
                if (match.IsStarted)
                    return new MatchResponse { IsError = true, Message = "Match is already started, cannot add players anymore." };
                PlayerRegistry playerRegistry = await loginService.GetPlayerRegistry(request.PlayerId);
                match.AddPlayer(playerRegistry);
                await matchService.SetMatchRegistry(match);
                await CreateFirstStateAndRegister(match);
                string playerInfoSerialized = JsonConvert.SerializeObject(playerRegistry.Info);
                MatchmakingEntry newEntry = new MatchmakingEntry
                {
                    Region = request.Region,
                    PlayerId = request.PlayerId,
                    Status = MatchmakingStatus.InLobby,
                    UseLobby = true,
                    Alias = request.Alias,
                    MatchId = match.MatchId,
                    PlayerInfoSerialized = playerInfoSerialized,
                };
                await matchService.UpsertMatchmakingEntry(newEntry);
            }
            PlayerInfo[] playerInfos = new PlayerInfo[match.PlayerInfos.Length];
            for (int i = 0; i < match.PlayerInfos.Length; i++)
                playerInfos[i] = match.PlayerInfos[i].Clone();
            return new MatchResponse
            {
                MatchId = request.MatchId,
                Players = playerInfos,
                IsOver = match.IsEnded,
                Alias = match.Alias,
                IsStarted = match.IsStarted,
            };
        }

        public static async Task<Response> LeaveMatch (MatchRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region))
                return new Response { IsError = true, Message = "Wrong Parameters." };

            if (string.IsNullOrEmpty(request.MatchId))
            {
                MatchmakingEntry[] entries = await matchService.GetMatchmakingEntries(null, null, request.PlayerId, null, MatchmakingStatus.Undefined);
                if (entries == null || entries.Length == 0)
                    return new Response { IsError = true, Message = $"Didn't find any match for player." };
                MatchmakingEntry playerEntry = default;
                if (entries.Length > 1)
                    Logger.LogError($"[{nameof(GetMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {JsonConvert.SerializeObject(entries)}");
                playerEntry = entries[0];
                if (string.IsNullOrEmpty(playerEntry.MatchId))
                {
                    await matchService.DeleteMatchmakingHistory(request.PlayerId, null);
                    return new Response { Message = $"Leave match executed by wiping matchmaking entries." };
                }
                request.MatchId = playerEntry.MatchId;
            }

            StateRegistry currentState = await matchService.GetState(request.MatchId);
            if (currentState == null)
                return new Response { IsError = true, Message = "Problem getting the match state." };
            StateRegistry newState = currentState.Clone();
            newState.UpsertPrivateProperties((request.PlayerId, "Retreated", "1"));
            await matchService.SetState(request.MatchId, currentState.Hash, newState);
            newState = await PrepareTurn (request.PlayerId, await matchService.GetMatchRegistry(request.MatchId), newState);

            Logger.LogWarning($"   [{nameof(LeaveMatch)}] \r\n>> Request :: {JsonConvert.SerializeObject(request, Formatting.Indented)}\r\n>> StateRegistry :: {JsonConvert.SerializeObject(newState, Formatting.Indented)}");

            return new Response { Message = $"Added player as retreated in {request.MatchId} successfully." };
        }

        public static async Task DeleteMatch (string matchId)
        {
            Logger.LogWarning("   [DeleteMatch] " + matchId);
            if (string.IsNullOrEmpty(matchId))
                return;
            await matchService.DeleteMatchmakingHistory(null, matchId);
            await matchService.DeleteState(matchId);
            await matchService.DeleteMatchRegistry(matchId);
            await matchService.DeleteActions(matchId);
        }

        public static async Task CheckMatch (string matchId, int lastHash)
        {
            StateRegistry state = await matchService.GetState(matchId);
            if (state == null)
                return;
            if ((state.TurnNumber == 0
                    && !HasHandshakingFromAllPlayers(state, await matchService.GetActions(matchId))) 
                || (state.TurnNumber >= 0 && state.Hash == lastHash))
                await DeleteMatch(matchId);
            else
            {
                MatchRegistry match = await matchService.GetMatchRegistry(matchId);
                if (match == null)
                    return;
                if (state.TurnNumber == -1 && (DateTime.UtcNow - match.CreatedTime).TotalMinutes >= (LOBBY_DURATION - 60))
                {
                    await DeleteMatch(matchId);
                    return;
                }
                GameRegistry gameRegistry = await loginService.GetGameConfig(match.GameId);
                await matchService.ScheduleCheckMatch(gameRegistry.RecurrentCheckMatchDelay * 1000, matchId, state.Hash);
            }
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
                MatchRegistry match = await matchService.GetMatchRegistry(request.MatchId);
                if (match == null)
                    return new StateResponse { IsError = true, Message = "Match not found." };
                if (!match.HasPlayer(request.PlayerId))
                    return new StateResponse { IsError = true, Message = "Player is not on that match." };
                StateRegistry currentState = await matchService.GetState(request.MatchId);
                List<ActionRegistry> actions = await matchService.GetActions(request.MatchId);
                if (currentState == null)
                    return new StateResponse { IsError = true, Message = "Get state error." };
                if (currentState.HasAnyPrivatePropertyWithValue("Retreated", "1"))
                    return new StateResponse { IsError = true, Message = "Player has left match." };
                StateInfo info = currentState.GetStateInfo(request.PlayerId);
                if (!match.IsStarted)
                    return new StateResponse { IsError = true, Message = "Match has not started yet.", StateInfo = info };
                if (!HasHandshakingFromAllPlayers(currentState, actions))
                    return new StateResponse { IsError = true, Message = "Not every player is ready.", StateInfo = info };
                currentState = await PrepareTurn(request.PlayerId, match, currentState, actions);
                if (info.Hash == request.LastHash)
                    return new StateResponse { IsError = true, Message = "Current state is the same known state." };
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
            match.StartTime = DateTime.UtcNow;
            await matchService.SetMatchRegistry(match);
            await matchService.SetState(match.MatchId, stateHash, state);
            await matchService.ScheduleCheckMatch(CHECK_MATCH_DELAY * 1000, match.MatchId, 0);
        }

        private static async Task TryToMatchPlayers (string gameId, string region)
        {
            // Get settings for matchmaking
            GameRegistry gameRegistry = await loginService.GetGameConfig(gameId);
            int playerCount = (gameRegistry.HasSetting("PlayerCount") && int.TryParse(gameRegistry.GetValue("PlayerCount"), out int count)) ? count : 2;
            float maxWaitToMatchWithBots = (gameRegistry.HasSetting("MaxWait") && float.TryParse(gameRegistry.GetValue("MaxWait"), out float wait)) ? wait : 6.0f;
            string actionForNoPlayers = gameRegistry.HasSetting("ActionForNoPlayers") ? gameRegistry.GetValue("ActionForNoPlayers") : "MatchWithBots";
            // Get entries for that region
            MatchmakingEntry[] entries = await matchService.GetMatchmakingEntries(region, null, null, null, MatchmakingStatus.Undefined);

            if (entries != null)
                Logger.LogWarning($"[TryToMatchPlayers] ! ! ! ! Entries already registered: {JsonConvert.SerializeObject(entries, Formatting.Indented)}");

            List<MatchmakingEntry> matchCandidates = new List<MatchmakingEntry>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                    continue;
                Logger.LogWarning($"[TryToMatchPlayers] Analysing entry: {JsonConvert.SerializeObject(entries[i], Formatting.Indented)}");
                if (entries[i].Status == MatchmakingStatus.Searching)
                {
                    matchCandidates.Add(entries[i]);
                    if (entries[i].UseLobby)
                    {
                        Logger.LogWarning($"[TryToMatchPlayers] Entry uses lobby");
                        await CreateMatch(matchCandidates, false, true);
                        return;
                    }
                }
            }
            Logger.LogWarning($"[TryToMatchPlayers] Match candidates: {JsonConvert.SerializeObject(matchCandidates.Select(x => JsonConvert.DeserializeObject<PlayerInfo>(x.PlayerInfoSerialized).Nickname))}");
            while (matchCandidates.Count >= playerCount)
            {
                List<MatchmakingEntry> range = matchCandidates.GetRange(0, playerCount);
                matchCandidates.RemoveRange(0, playerCount);
                await CreateMatch(range, false, false);
            }
            if (matchCandidates.Count == 0)
                return;
            DateTime entriesMaxTimestamp = matchCandidates.Max(e => e.Timestamp);
            if ((DateTime.UtcNow - entriesMaxTimestamp).TotalSeconds >= maxWaitToMatchWithBots)
            {
                Logger.LogWarning($"[TryToMatchPlayers] Matching with bots: {JsonConvert.SerializeObject(matchCandidates.Select(x => JsonConvert.DeserializeObject<PlayerInfo>(x.PlayerInfoSerialized).Nickname))}");
                if (actionForNoPlayers == "MatchWithBots")
                {
                    // Match with bots
                    int candidatesCount = matchCandidates.Count;
                    for (int i = 0; i < playerCount - candidatesCount; i++)
                    {
                        // Add bot entry to the matchmaking table
                        string botId = "X" + Guid.NewGuid().ToString();
                        string botAlias = Guid.NewGuid().ToString();
                        PlayerInfo botInfo = game.CreateBot(gameRegistry.BotSettings);
                        matchCandidates.Add(
                            new MatchmakingEntry
                            {
                                PlayerId = botId,
                                PlayerInfoSerialized = JsonConvert.SerializeObject(botInfo),
                                Region = region,
                                Status = MatchmakingStatus.Searching,
                                Timestamp = DateTime.UtcNow
                            });
                    }
                    await CreateMatch(matchCandidates, true, false);
                }
                else
                {
                    foreach (var item in matchCandidates)
                    {
                        item.Status = MatchmakingStatus.FailedWithNoPlayers;
                        await matchService.UpsertMatchmakingEntry(item);
                    }
                }
            }
            Logger.LogWarning($"[TryToMatchPlayers] Just wait...");

            async Task CreateMatch (List<MatchmakingEntry> entries, bool hasBots, bool isLobby)
            {
                string matchId = Guid.NewGuid().ToString();
                Logger.LogWarning($"[CreateMatch] Creating ids array");
                string[] ids = new string[entries.Count];
                Logger.LogWarning($"[CreateMatch] Creating infos array");
                PlayerInfo[] infos = new PlayerInfo[entries.Count];
                Logger.LogWarning($"[CreateMatch] Getting a random alias");
                string alias = Helper.GetRandomMatchAlias();
                for (int i = 0; i < entries.Count; i++)
                {
                    MatchmakingEntry entry = entries[i];
                    Logger.LogWarning($"[CreateMatch] Analysing entry {JsonConvert.SerializeObject(entry, Formatting.Indented)}");
                    ids[i] = entry.PlayerId;
                    infos[i] = JsonConvert.DeserializeObject<PlayerInfo>(entry.PlayerInfoSerialized);
                    if (entry.PlayerId[0] == 'X')
                        continue;
                    entry.Alias = alias;
                    entry.MatchId = matchId;
                    entry.Status = isLobby ? MatchmakingStatus.InLobby : MatchmakingStatus.Matched;
                    Logger.LogWarning($"[CreateMatch] Upserting matchmaking entry");
                    await matchService.UpsertMatchmakingEntry(entry);
                }
                MatchRegistry match = new MatchRegistry
                {
                    GameId = gameId,
                    MatchId = matchId,
                    CreatedTime = DateTime.UtcNow,
                    PlayerIds = ids,
                    PlayerInfos = infos,
                    Alias = alias,
                    Region = region,
                    HasBots = hasBots,
                    UseLobby = isLobby,
                };
                Logger.LogWarning($"[CreateMatch] Registering match");
                await matchService.SetMatchRegistry(match);
                Logger.LogWarning($"[CreateMatch] Creating first state");
                StateRegistry state = await CreateFirstStateAndRegister(match);
                Logger.LogWarning($"[CreateMatch] Scheduling check match");
                if (isLobby)
                    await matchService.ScheduleCheckMatch(LOBBY_DURATION * 1000, matchId, 0);
                else
                    await StartMatch(match, state);
            }
        }

        private static async Task<StateRegistry> CreateFirstStateAndRegister (MatchRegistry match)
        {
            StateRegistry newState = game.CreateFirstState(match);
            if (match.UseLobby)
                newState.TurnNumber = -1;
            await matchService.SetState(match.MatchId, null, newState);
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
                    actions = await matchService.GetActions(match.MatchId);
                debug += "Prep turn in Game | ";
                StateRegistry newState = game.PrepareTurn(requesterId, match, lastState, actions);
                debug += "Update actions | ";
                await matchService.UpdateActions(match.MatchId, actions);
                if (newState.IsMatchEnded && !match.IsEnded)
                {
                    match.IsEnded = true;
                    await matchService.SetMatchRegistry(match);
                    await matchService.DeleteMatchmakingHistory(null, match.MatchId);
                    if (!await matchService.SetState(match.MatchId, lastState.Hash, newState))
                    {
                        Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
                        return await PrepareTurn(requesterId, match, await matchService.GetState(match.MatchId), actions);
                    }
                    GameRegistry gameRegistry = await loginService.GetGameConfig(match.GameId);
                    await matchService.ScheduleCheckMatch(gameRegistry.FinalCheckMatchDelay * 1000, match.MatchId, newState.Hash);
                    return newState;
                }
                if (lastState != null && newState.Hash == lastState.Hash)
                    return lastState;
                debug += "Setting new state | ";
                if (!await matchService.SetState(match.MatchId, lastState.Hash, newState))
                {
                    Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
                    debug += "RE-Prepare turn | ";
                    return await PrepareTurn(requesterId, match, await matchService.GetState(match.MatchId), actions);
                }
                return newState;
            }
            catch (Exception e)
            {
                throw new Exception($"{e.Message} >>> {debug}");
            }
        }

        private static bool HasHandshakingFromAllPlayers (StateRegistry state, List<ActionRegistry> actions)
        {
            string[] players = state.GetPlayers();
            int count = 0;
            string playersWithHandshaking = "";
            foreach (var player in players)
                if (player[0] == 'X' 
                    || !string.IsNullOrEmpty(state.GetPrivate(player, "Handshaking")) 
                    || (actions.Find(x => x.PlayerId == player && x.Action.IsPrivateChangeEqualsIfPresent("Handshaking", "1")) != null))
                {
                    count++;
                    playersWithHandshaking += $"| {player}";
                }
            if (count != players.Length)
                Logger.LogError($"   [HasHandshakingFromAllPlayers] Player with handshaking = {count} = {playersWithHandshaking}\r\n  >>>  STATE  >>>>\r\n{JsonConvert.SerializeObject(state, Formatting.Indented)}\r\n   >>>   ACTIONS   >>>\r\n{JsonConvert.SerializeObject(actions, Formatting.Indented)}");
            return count == players.Length;
        }
        #endregion
    }
}
