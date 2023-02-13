using System.Text;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;
using Kalkatos.FunctionsGame.Registry;
using Azure.Data.Tables;

namespace Kalkatos.FunctionsGame.AzureFunctions
{

	public class AzureFunctionsService : IService
	{
		// Log In

		public async Task<bool> IsRegisteredDevice (string deviceId)
		{
			BlockBlobClient identifierFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{deviceId}");
			return await identifierFile.ExistsAsync();
		}

		public async Task<string> GetPlayerId (string deviceId)
		{
			BlockBlobClient identifierFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{deviceId}");
			using (Stream stream = await identifierFile.OpenReadAsync())
				return Helper.ReadBytes(stream);
		}

		public async Task RegisterDeviceWithId (string deviceId, string playerId)
		{
			BlockBlobClient identifierFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{deviceId}");
			using (Stream stream = await identifierFile.OpenWriteAsync(true))
				stream.Write(Encoding.ASCII.GetBytes(playerId));
		}

		public async Task<PlayerRegistry> GetPlayerRegistry (string playerId)
		{
			BlockBlobClient playerBlob = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");
			using (Stream stream = await playerBlob.OpenReadAsync())
				return JsonConvert.DeserializeObject<PlayerRegistry>(Helper.ReadBytes(stream));
		}

		public async Task SetPlayerRegistry (PlayerRegistry registry)
		{
			BlockBlobClient playerBlob = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{registry.PlayerId}.json");
			using (Stream stream = await playerBlob.OpenWriteAsync(true))
				stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(registry)));
		}

		public async Task DeletePlayerRegistry (string playerId)
		{
			BlockBlobClient playerBlob = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");
			if (await playerBlob.ExistsAsync())
				await playerBlob.DeleteAsync();
		}

		// Matchmaking

		public async Task DeleteMatchmakingHistory (string playerId, string matchId)
		{
			TableClient matchmakingTable = new TableClient("UseDevelopmentStorage=true", "Matchmaking");
			Azure.Pageable<PlayerLookForMatchEntity> query;
			if (string.IsNullOrEmpty(matchId))
				query = matchmakingTable.Query<PlayerLookForMatchEntity>(entry => entry.RowKey == playerId);
			else if (string.IsNullOrEmpty(playerId))
				query = matchmakingTable.Query<PlayerLookForMatchEntity>(entry => entry.MatchId == matchId);
			else
				query = matchmakingTable.Query<PlayerLookForMatchEntity>(entry => entry.MatchId == matchId && entry.RowKey == playerId);
			foreach (var item in query)
				await matchmakingTable.DeleteEntityAsync(item.PartitionKey, item.RowKey);
		}

		// Match

		public async Task<MatchRegistry> GetMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{matchId}.json");
			if (await matchBlob.ExistsAsync())
				using (Stream stream = await matchBlob.OpenReadAsync(true))
					return JsonConvert.DeserializeObject<MatchRegistry>(Helper.ReadBytes(stream));
			return null;
		}

		public async Task DeleteMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{matchId}.json");
			if (await matchBlob.ExistsAsync())
				await matchBlob.DeleteAsync();
		}

		public async Task SetMatchRegistry (MatchRegistry matchRegistry)
		{
			BlockBlobClient matchBlob = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{matchRegistry.MatchId}.json");
			using Stream stream = await matchBlob.OpenWriteAsync(true);
			stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(matchRegistry)));
		}

		// Action
		//public async Task RegisterAction (string matchId, string playerId, Dictionary<string, string> content)
		//{
		//	TableClient actionsTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");
		//	await actionsTable.UpsertEntityAsync(new PlayerActionEntity { PartitionKey = matchId, RowKey = playerId, Content = JsonConvert.SerializeObject(content) });
		//}
		//public async Task<ActionInfo[]> GetActionHistory (string matchId, string[] players, string actionName)
		//{
		//	await Task.Delay(1);
		//	TableClient actionsTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");
		//	Azure.Pageable<PlayerActionEntity> query;
		//	if (!string.IsNullOrEmpty(actionName))
		//	{
		//		if (players == null)
		//			query = actionsTable.Query<PlayerActionEntity>(entry =>
		//				entry.PartitionKey == matchId &&
		//				entry.ActionName == actionName);
		//		else
		//			query = actionsTable.Query<PlayerActionEntity>(entry =>
		//				entry.PartitionKey == matchId &&
		//				Array.IndexOf(players, entry.RowKey) >= 0 &&  // TODO Test getting action history with an array of players
		//				entry.ActionName == actionName);
		//	}
		//	else
		//	{
		//		if (players == null)
		//			query = actionsTable.Query<PlayerActionEntity>(entry =>
		//				entry.PartitionKey == matchId);
		//		else
		//			query = actionsTable.Query<PlayerActionEntity>(entry =>
		//				entry.PartitionKey == matchId &&
		//				Array.IndexOf(players, entry.RowKey) >= 0);
		//	}
		//	int count = query.Count();
		//	if (count == 0)
		//		return null;
		//	ActionInfo[] actions = new ActionInfo[count];
		//	int index = 0;
		//	foreach (var item in query)
		//	{
		//		actions[index] = new ActionInfo { PlayerAlias = item.PlayerAlias, ActionName = item.ActionName, Parameter = JsonConvert.DeserializeObject(item.SerializedParameter) };
		//		index++;
		//	}
		//	return actions;
		//}
		//public async Task DeleteActionHistory (string matchId)
		//{
		//	TableClient actionsTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");
		//	Azure.Pageable<PlayerActionEntity> query = actionsTable.Query<PlayerActionEntity>(entry => entry.PartitionKey == matchId);
		//	foreach (var item in query)
		//		await actionsTable.DeleteEntityAsync(item.PartitionKey, item.RowKey);
		//}
		// State

		public async Task<StateRegistry> GetState (string matchId)
		{
			BlockBlobClient stateBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
			StateRegistry state = null;
			if (await stateBlob.ExistsAsync())
				using (Stream stream = await stateBlob.OpenReadAsync())
					state = JsonConvert.DeserializeObject<StateRegistry>(Helper.ReadBytes(stream));
			return state;
		}

		public async Task SetState (string matchId, StateRegistry state)
		{
			BlockBlobClient stateBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
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
					Logger.Log("   [SetState] Retrying set");
					await Task.Delay(100);
				}
			}
		}

		public async Task DeleteState (string matchId)
		{
			BlockBlobClient statesBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
			if (await statesBlob.ExistsAsync())
				await statesBlob.DeleteAsync();
		}
	}
}