using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using Kalkatos.FunctionsGame.Registry;
using Newtonsoft.Json;
using System;
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
			// TODO Check the other parameters too
			var query = tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId);
			int count = query.Count();
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

		public async Task DeleteMatchmakingHistory (string playerId, string matchId)
		{
			TableClient matchmakingTable = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
			global::Azure.Pageable<PlayerLookForMatchEntity> query;
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
			state?.UpdateHash();
			return state;
		}

		public async Task SetState (string matchId, StateRegistry state)
		{
			BlockBlobClient stateBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
			bool stateSetSuccessfully = false;
			while (!stateSetSuccessfully)
			{
				try
				{
					using (Stream stream = await stateBlob.OpenWriteAsync(true))
						stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(state)));
					stateSetSuccessfully = true;
				}
				catch
				{
					Logger.LogError("   [SetState] Retrying set");
					await Task.Delay(100);
				}
			}
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
	}
}