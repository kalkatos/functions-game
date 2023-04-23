using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using Kalkatos.FunctionsGame.Registry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame.Azure
{

	public class AzureService : IService
	{
		// ================================= G A M E ==========================================

		public async Task<GameRegistry> GetGameConfig (string gameId)
		{
			BlockBlobClient identifierFile = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "games", $"{gameId}.json");
			if (await identifierFile.ExistsAsync())
				using (Stream stream = await identifierFile.OpenReadAsync())
					return JsonConvert.DeserializeObject<GameRegistry>(Helper.ReadBytes(stream));
			return null;
		}

		// ================================= L O G I N ==========================================

		public async Task<string> GetPlayerId (string deviceId)
		{
			BlockBlobClient identifierFile = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{deviceId}");
			if (await identifierFile.ExistsAsync())
				using (Stream stream = await identifierFile.OpenReadAsync())
					return Helper.ReadBytes(stream);
			return null;
		}

		public async Task RegisterDeviceWithId (string deviceId, string playerId)
		{
			BlockBlobClient identifierFile = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{deviceId}");
			using (Stream stream = await identifierFile.OpenWriteAsync(true))
				stream.Write(Encoding.ASCII.GetBytes(playerId));
		}

		public async Task<PlayerRegistry> GetPlayerRegistry (string playerId)
		{
			BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{playerId}.json");
			if (await playerBlob.ExistsAsync())
				using (Stream stream = await playerBlob.OpenReadAsync())
					return JsonConvert.DeserializeObject<PlayerRegistry>(Helper.ReadBytes(stream));
			return null;
		}

		public async Task SetPlayerRegistry (PlayerRegistry registry)
		{
			BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{registry.PlayerId}.json");
			using (Stream stream = await playerBlob.OpenWriteAsync(true))
				stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(registry)));
		}

		public async Task DeletePlayerRegistry (string playerId)
		{
			BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{playerId}.json");
			if (await playerBlob.ExistsAsync())
				await playerBlob.DeleteAsync();
		}

		// ================================= M A T C H M A K I N G ==========================================

		public async Task<MatchmakingEntry[]> GetMatchmakingEntries (string region, string playerId, string matchId, MatchmakingStatus status)
		{
			await Task.Delay(1);
			TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
			List<PlayerLookForMatchEntity> query = null;
			if (!string.IsNullOrEmpty(region))
				query = tableClient.Query<PlayerLookForMatchEntity>(item => item.PartitionKey == region)?.ToList();
			if (!string.IsNullOrEmpty(playerId))
				query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId))?.ToList()
					?? tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId)?.ToList();
			if (!string.IsNullOrEmpty(matchId))
				query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.MatchId == matchId))?.ToList()
					?? tableClient.Query<PlayerLookForMatchEntity>(item => item.MatchId == matchId)?.ToList();
			if (status != MatchmakingStatus.Undefined)
			{
				int statusInt = (int)status;
				query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.Status == statusInt))?.ToList()
					?? tableClient.Query<PlayerLookForMatchEntity>(item => item.Status == statusInt)?.ToList();
			}
			if (query == null)
				return null;
			int count = query.Count;
			if (count > 0)
			{
				MatchmakingEntry[] result = new MatchmakingEntry[count];
				int index = 0;
				foreach (var item in query)
					result[index++] = item.ToEntry();
				return result;
			}
			return null;
		}

		public async Task UpsertMatchmakingEntry (MatchmakingEntry entry)
		{
			TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
			await tableClient.UpsertEntityAsync(
				new PlayerLookForMatchEntity
				{
					PartitionKey = entry.Region,
					RowKey = entry.PlayerId,
					MatchId = entry.MatchId,
					PlayerInfoSerialized = entry.PlayerInfoSerialized,
					Status = (int)entry.Status,
				});
		}

		public async Task DeleteMatchmakingHistory (string playerId, string matchId)
		{
			TableClient matchmakingTable = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
			Pageable<PlayerLookForMatchEntity> query;
			if (string.IsNullOrEmpty(matchId))
				query = matchmakingTable.Query<PlayerLookForMatchEntity>(entry => entry.RowKey == playerId);
			else if (string.IsNullOrEmpty(playerId))
				query = matchmakingTable.Query<PlayerLookForMatchEntity>(entry => entry.MatchId == matchId);
			else
				query = matchmakingTable.Query<PlayerLookForMatchEntity>(entry => entry.MatchId == matchId && entry.RowKey == playerId);
			foreach (var item in query)
				await matchmakingTable.DeleteEntityAsync(item.PartitionKey, item.RowKey);
		}

		// ================================= M A T C H ==========================================

		public async Task<MatchRegistry> GetMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{matchId}.json");
			if (await matchBlob.ExistsAsync())
				using (Stream stream = await matchBlob.OpenReadAsync(true))
					return JsonConvert.DeserializeObject<MatchRegistry>(Helper.ReadBytes(stream));
			return null;
		}

		public async Task DeleteMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{matchId}.json");
			try
			{
				if (await matchBlob.ExistsAsync())
					await matchBlob.DeleteAsync();
			}
			catch { }
		}

		public async Task SetMatchRegistry (MatchRegistry matchRegistry)
		{
			BlockBlobClient matchBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{matchRegistry.MatchId}.json");
			try
			{
				using (Stream stream = await matchBlob.OpenWriteAsync(true))
					stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(matchRegistry)));
			}
			catch { }
		}

		public async Task ScheduleCheckMatch (int millisecondsDelay, string matchId, int lastHash)
		{
			QueueClient checkMatchQueue = new QueueClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "check-match");
			string message = $"{matchId}|{lastHash}";
			var bytes = Encoding.UTF8.GetBytes(message);
			await checkMatchQueue.SendMessageAsync(Convert.ToBase64String(bytes), TimeSpan.FromMilliseconds(millisecondsDelay));
		}

		// ================================= S T A T E ==========================================

		public async Task<StateRegistry> GetState (string matchId)
		{
			BlockBlobClient stateBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
			StateRegistry state = null;
			if (await stateBlob.ExistsAsync())
				using (Stream stream = await stateBlob.OpenReadAsync())
					state = JsonConvert.DeserializeObject<StateRegistry>(Helper.ReadBytes(stream));
			else
				Logger.LogError($"   [GetState] State does not exist.");
			state?.UpdateHash();
			return state;
		}

		public async Task<bool> SetState (string matchId, StateRegistry oldState, StateRegistry newState)
		{
			//QueueClient queue = new QueueClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "set-state");
			//string message = $"{matchId}|{JsonConvert.SerializeObject(state)}";
			//var bytes = Encoding.UTF8.GetBytes(message);
			//await queue.SendMessageAsync(Convert.ToBase64String(bytes));

			BlockBlobClient stateBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
			bool stateSetSuccessfully = false;
			while (!stateSetSuccessfully)
			{
				try
				{
					StateRegistry stateRegistry = await GetState(matchId);
					if (stateRegistry != null && oldState != null && oldState.Hash != stateRegistry.Hash)
					{
						Logger.LogError($"   [SetState] states don't match ===\n old - {JsonConvert.SerializeObject(oldState, Formatting.Indented)}\n saved - {JsonConvert.SerializeObject(stateRegistry, Formatting.Indented)}");
						return false;
					}
					using (Stream stream = await stateBlob.OpenWriteAsync(true))
						stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(newState)));
					stateSetSuccessfully = true;
				}
				catch
				{
					return false;
				}
			}
			return true;
		}

		public async Task DeleteState (string matchId)
		{
			BlockBlobClient statesBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
			try
			{
				if (await statesBlob.ExistsAsync())
					await statesBlob.DeleteAsync();
			} 
			catch { }
		}

		public async Task<bool> GetBool (string key)
		{
			BlockBlobClient dataBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "rules", "data.json");
			Dictionary<string, string> dataDict = null;
			if (await dataBlob.ExistsAsync())
				using (Stream stream = await dataBlob.OpenReadAsync())
				{
					dataDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(Helper.ReadBytes(stream));
					if (dataDict.ContainsKey(key))
						return dataDict[key] == "1";
				}
			return false;
		}
	}

	public class PlayerLookForMatchEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Player region & other matchmaking data
		public string RowKey { get; set; } // Player ID
		public string PlayerInfoSerialized { get; set; }
		public string MatchId { get; set; }
		public int Status { get; set; }
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }

		public MatchmakingEntry ToEntry ()
		{
			return new MatchmakingEntry
			{
				Region = PartitionKey,
				PlayerId = RowKey,
				MatchId = MatchId,
				Status = (MatchmakingStatus)Status,
				PlayerInfoSerialized = PlayerInfoSerialized,
				Timestamp = Timestamp.Value.DateTime
			};
		}
	}
}