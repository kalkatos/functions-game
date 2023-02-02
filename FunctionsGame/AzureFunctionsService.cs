using System.Text;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;
using Kalkatos.Network.Model;
using Kalkatos.FunctionsGame.Registry;
using Azure.Data.Tables;
using System.Linq;
using System;

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

		// Match

		public async Task<MatchRegistry> GetMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{matchId}.json");
			using (Stream stream = await matchBlob.OpenReadAsync(true))
				return JsonConvert.DeserializeObject<MatchRegistry>(Helper.ReadBytes(stream));
		}

		public async Task DeleteMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{matchId}.json");
			if (await matchBlob.ExistsAsync())
				await matchBlob.DeleteAsync();
		}

		// Action

		public async Task<ActionInfo[]> GetActionHistory (string matchId, string[] players, string actionName)
		{
			await Task.Delay(1);
			TableClient actionsTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");
			Azure.Pageable<PlayerActionEntity> query;
			if (!string.IsNullOrEmpty(actionName))
			{
				if (players == null)
					query = actionsTable.Query<PlayerActionEntity>(entry =>
						entry.PartitionKey == matchId &&
						entry.ActionName == actionName);
				else
					query = actionsTable.Query<PlayerActionEntity>(entry =>
						entry.PartitionKey == matchId &&
						Array.IndexOf(players, entry.RowKey) >= 0 &&  // TODO Test getting action history with an array of players
						entry.ActionName == actionName);
			}
			else
			{
				if (players == null)
					query = actionsTable.Query<PlayerActionEntity>(entry =>
						entry.PartitionKey == matchId);
				else
					query = actionsTable.Query<PlayerActionEntity>(entry =>
						entry.PartitionKey == matchId &&
						Array.IndexOf(players, entry.RowKey) >= 0);
			}
			int count = query.Count();
			if (count == 0)
				return null;
			ActionInfo[] actions = new ActionInfo[count];
			int index = 0;
			foreach (var item in query)
			{
				actions[index] = new ActionInfo { PlayerAlias = item.PlayerAlias, ActionName = item.ActionName, Parameter = JsonConvert.DeserializeObject(item.SerializedParameter) };
				index++;
			}
			return actions;
		}

		public async Task DeleteActionHistory (string matchId)
		{
			TableClient actionsTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");
			Azure.Pageable<PlayerActionEntity> query = actionsTable.Query<PlayerActionEntity>(entry => entry.PartitionKey == matchId);
			foreach (var item in query)
				await actionsTable.DeleteEntityAsync(item.PartitionKey, item.RowKey);
		}

		// State

		public async Task<StateInfo[]> GetStateHistory (string matchId)
		{
			BlockBlobClient stateBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
			StateInfo[] stateHistory = null;
			if (await stateBlob.ExistsAsync())
				using (Stream stream = await stateBlob.OpenReadAsync())
					stateHistory = JsonConvert.DeserializeObject<StateInfo[]>(Helper.ReadBytes(stream));
			return stateHistory;
		}

		public async Task SetStateHistory (string matchId, StateInfo[] states)
		{
			BlockBlobClient stateBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
			using (Stream stream = await stateBlob.OpenWriteAsync(true))
				stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(states)));
		}

		public async Task DeleteStateHistory (string matchId)
		{
			BlockBlobClient statesBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
			if (await statesBlob.ExistsAsync())
				await statesBlob.DeleteAsync();
		}
	}
}