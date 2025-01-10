using Kalkatos.Network.Model;
using Kalkatos.Network.Registry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public static class LeaderboardFunctions
{
	private static IService service = Global.Service;

	private const int PAGE_SIZE = 20;
	private const float UPDATE_THRESHOLD = 60;

	public static async Task<Response> AddLeaderboardEvent (LeaderboardEventRequest request)
	{
		if (string.IsNullOrEmpty(request.GameId) || string.IsNullOrEmpty(request.PlayerId))
			return new Response { IsError = true, Message = "Game id and player id may not be null." };
		string playerRegistrySerialized = await service.GetData("Players", Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (string.IsNullOrEmpty(playerRegistrySerialized))
			return new Response { IsError = true, Message = "Player does not exist." };
		PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerRegistrySerialized);
		if (string.IsNullOrEmpty(request.Key))
			request.Key = Global.DEFAULT_PARTITION;
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
		await service.UpsertData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LAST_LEADERBOARD_EVENT_KEY, DateTimeOffset.UtcNow.ToString());
		await service.UpsertData(Global.LEADERBOARD_TABLE, request.GameId, request.PlayerId, JsonConvert.SerializeObject(eventRegistry));
		return new Response { Message = "Event registered successfully." };
	}

	public static async Task<LeaderboardResponse> GetLeaderboard (LeaderboardRequest request)
	{
		if (string.IsNullOrEmpty(request.GameId) || string.IsNullOrEmpty(request.PlayerId))
			return new LeaderboardResponse { IsError = true, Message = "Game id and player id may not be null." };
		var dataDict = await service.GetAllData(Global.LEADERBOARD_TABLE, request.GameId, null);
		if (dataDict != null || dataDict.Count == 0)
			return new LeaderboardResponse { IsError = true, Message = "No leaderboard event was found." };
		List<LeaderboardRegistry> registryList = new();
		foreach (var item in dataDict)
			try
			{
				registryList.Add(JsonConvert.DeserializeObject<LeaderboardRegistry>(item.Value));
			}
			catch (Exception)
			{
				return new LeaderboardResponse { IsError = true, Message = $"Error parsing leaderboard data. Item: {item.Key}" };
			}
		LeaderboardRegistry[] events = registryList.ToArray();
		string serializedIndexes = await service.GetData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LEADERBOARD_INDEXES_KEY, "");
		if (string.IsNullOrEmpty(serializedIndexes))
		{
			serializedIndexes = "";
			for (int i = 0; i < 10; i++)
			{
				if (i > 0)
					serializedIndexes += ",";
				serializedIndexes += Guid.NewGuid().ToString();
			}
			await service.UpsertData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LEADERBOARD_INDEXES_KEY, serializedIndexes);
		}
		string[] indexes = serializedIndexes.Split(',');
		string lastUpdateStr = await service.GetData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LAST_LEADERBOARD_UPDATE_KEY, "");
		DateTimeOffset lastUpdate = string.IsNullOrEmpty(lastUpdateStr) ? DateTimeOffset.UtcNow.AddSeconds(-(UPDATE_THRESHOLD + 1)) : DateTimeOffset.Parse(lastUpdateStr);
		string lastEventAddedTimeStr = await service.GetData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LAST_LEADERBOARD_EVENT_KEY, "");
		DateTimeOffset lastEventAddedTime = string.IsNullOrEmpty(lastEventAddedTimeStr) ? DateTimeOffset.MaxValue : DateTimeOffset.Parse(lastEventAddedTimeStr);
		string message = "";
		if ((DateTimeOffset.UtcNow - lastUpdate).TotalSeconds > UPDATE_THRESHOLD
			|| lastEventAddedTime > lastUpdate)
		{
			Logger.LogWarning($"[{nameof(GetLeaderboard)}] Updating Leaderboard");
			message = $"Leaderboard Updated | Elapsed time = {(DateTimeOffset.UtcNow - lastUpdate).TotalSeconds} > 60 && {lastEventAddedTime} > {lastUpdate}";
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
						await service.UpsertData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LEADERBOARD_INDEXES_KEY, serializedIndexes);
					}
					else
						nextPageId = indexes[page + 1];
				}
				events[i].Rank = i + 1;
				events[i].PageId = currentPageId;
				events[i].PreviousPageId = previousPageId;
				events[i].NextPageId = nextPageId;
			}
			foreach (var @event in events)
				await service.UpsertData(Global.LEADERBOARD_TABLE, request.PlayerId, @event.Key, JsonConvert.SerializeObject(@event));
			await service.UpsertData(Global.DATA_TABLE, Global.LEADERBOARD_TABLE, Global.LAST_LEADERBOARD_UPDATE_KEY, DateTimeOffset.UtcNow.ToString());
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
