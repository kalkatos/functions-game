using Kalkatos.Network.Model;
using Kalkatos.FunctionsGame.Registry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public static class LeaderboardFunctions
    {
        private static ILoginService loginService = GlobalConfigurations.LoginService;
        private static IMatchService matchService = GlobalConfigurations.MatchService;
        private static ILeaderboardService leaderboardService = GlobalConfigurations.LeaderboardService;
        private static IDataService dataService = GlobalConfigurations.DataService;

        private const int PAGE_SIZE = 20;
        private const float UPDATE_THRESHOLD = 60;

        public static async Task<Response> AddLeaderboardEvent (LeaderboardEventRequest request)
        {
            if (string.IsNullOrEmpty(request.GameId) || string.IsNullOrEmpty(request.PlayerId))
                return new Response { IsError = true, Message = "Game id and player id may not be null." };
            PlayerRegistry playerRegistry = await loginService.GetPlayerRegistry(request.PlayerId);
            if (playerRegistry == null)
                return new Response { IsError = true, Message = "Player does not exist." };
            if (string.IsNullOrEmpty(request.Key))
                request.Key = "Default";
            LeaderboardRegistry eventRegistry = new LeaderboardRegistry
            {
                GameId = request.GameId,
                PlayerId = request.PlayerId,
                PlayerName = playerRegistry.Info.Nickname,
                Key = request.Key,
                Value = request.Value,
            };
            if (request.CustomData != null)
                eventRegistry.DataSerialized = JsonConvert.SerializeObject(request.CustomData);
            await dataService.SetValue("LastLeaderboardEventAdded", DateTime.UtcNow.ToString());
            await leaderboardService.AddLeaderboardEvent(eventRegistry);
            return new Response { Message = "Event registered successfully." };
        }

        public static async Task<LeaderboardResponse> GetLeaderboard (LeaderboardRequest request)
        {
            if (string.IsNullOrEmpty(request.GameId))
                return new LeaderboardResponse { IsError = true, Message = "Game id may not be null." };
            LeaderboardRegistry[] events = await leaderboardService.GetLeaderboardEvents(request.GameId, request.Key);
            if (events == null || events.Length == 0)
                return new LeaderboardResponse { IsError = true, Message = "No leaderboard event was found." };
            string serializedIndexes = await dataService.GetValue("LeaderboardIndexes", "");
            if (string.IsNullOrEmpty(serializedIndexes))
            {
                serializedIndexes = "";
                for (int i = 0; i < 10; i++)
                {
                    if (i > 0)
                        serializedIndexes += ",";
                    serializedIndexes += Guid.NewGuid().ToString();
                }
                await dataService.SetValue("LeaderboardIndexes", serializedIndexes);
            }
            string[] indexes = serializedIndexes.Split(',');
            string lastUpdateStr = await dataService.GetValue("LastLeaderboardUpdate", "");
            DateTime lastUpdate = string.IsNullOrEmpty(lastUpdateStr) ? DateTime.UtcNow.AddSeconds(-(UPDATE_THRESHOLD + 1)) : DateTime.Parse(lastUpdateStr);
            string lastEventAddedTimeStr = await dataService.GetValue("LastLeaderboardEventAdded", "");
            DateTime lastEventAddedTime = string.IsNullOrEmpty(lastEventAddedTimeStr) ? DateTime.MaxValue : DateTime.Parse(lastEventAddedTimeStr);
            string message = "";
            if ((DateTime.UtcNow - lastUpdate).TotalSeconds > UPDATE_THRESHOLD
                || lastEventAddedTime > lastUpdate)
            {
                Logger.LogWarning($"[{nameof(GetLeaderboard)}] Updating Leaderboard");
                message = $"Leaderboard Updated | Elapsed time = {(DateTime.UtcNow - lastUpdate).TotalSeconds} > 60 && {lastEventAddedTime} > {lastUpdate}";
                events = events.OrderByDescending(e => e.Value).ToArray();
                int currentPage = 0;
                string currentPageId = indexes[0];
                string previousPageId = "";
                string nextPageId = indexes[1];
                for (int i = 0; i < events.Length; i++)
                {
                    int page = i / PAGE_SIZE;
                    if (page != currentPage)
                    {
                        currentPage = page;
                        previousPageId = currentPageId;
                        currentPageId = nextPageId;
                        if (page + 1 >= indexes.Length)
                        {
                            nextPageId = Guid.NewGuid().ToString();
                            serializedIndexes += $",{nextPageId}";
                            indexes = indexes.Append(nextPageId).ToArray();
                            await dataService.SetValue("LeaderboardIndexes", serializedIndexes);
                        }
                        else
                            nextPageId = indexes[page + 1];
                    }
                    events[i].Rank = i + 1;
                    events[i].PageId = currentPageId;
                    events[i].PreviousPageId = previousPageId;
                    events[i].NextPageId = nextPageId;
                }
                await dataService.SetValue("LastLeaderboardUpdate", DateTime.UtcNow.ToString());
                await leaderboardService.UpdateLeaderboardEvents(events);
            }
            List<LeaderboardPlayerInfo> leaderboardInfo = new();
            string previousPageId2 = "";
            string nextPageId2 = "";
            LeaderboardRegistry[] requestedEvents = null;
            if (!string.IsNullOrEmpty(request.PlayerId))
            {
                LeaderboardRegistry playerEvent = events.FirstOrDefault(x => x.PlayerId == request.PlayerId);
                if (playerEvent == null)
                    return new LeaderboardResponse { IsError = true, Message = $"No leaderboard event was found for player {request.PlayerId}." };
                requestedEvents = events.Where(x => x.PageId == playerEvent.PageId).OrderByDescending(e => e.Value).ToArray();
                previousPageId2 = playerEvent.PreviousPageId;
                nextPageId2 = playerEvent.NextPageId;
            }
            else
            {
                if (string.IsNullOrEmpty(request.PageId))
                    request.PageId = indexes[0];
                requestedEvents = events.Where(x => x.PageId == request.PageId).OrderByDescending(e => e.Value).ToArray();
                if (requestedEvents == null || requestedEvents.Length == 0)
                    return new LeaderboardResponse { IsError = true, Message = $"No leaderboard event was found for page id {request.PageId}." };
                previousPageId2 = requestedEvents[0].PreviousPageId;
                nextPageId2 = requestedEvents[0].NextPageId;
            }
            foreach (var item in requestedEvents)
            {
                var info = new LeaderboardPlayerInfo
                {
                    Nickname = item.PlayerName,
                    Value = item.Value,
                };
                if (!string.IsNullOrEmpty(item.DataSerialized))
                    info.CustomData = JsonConvert.DeserializeObject<Dictionary<string, string>>(item.DataSerialized);
                leaderboardInfo.Add(info);
            }
            return new LeaderboardResponse
            {
                Leaderboard = leaderboardInfo.ToArray(),
                PreviousPageId = previousPageId2,
                NextPageId = nextPageId2,
                Message = message
            };        
        }
    }
}
