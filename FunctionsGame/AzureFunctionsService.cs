using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;
using Kalkatos.Network.Model;
using Kalkatos.FunctionsGame.Registry;
using Azure.Data.Tables;
using System.Linq;
using Microsoft.Win32;

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

		// Match

		public async Task<MatchRegistry> GetMatchRegistry (string matchId)
		{
			BlockBlobClient matchBlob = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{matchId}.json");
			using (Stream stream = await matchBlob.OpenReadAsync(true))
				return JsonConvert.DeserializeObject<MatchRegistry>(Helper.ReadBytes(stream));
		}

		// Turn

		public async Task<StateInfo[]> GetStateHistory (string playerId, string matchId)
		{
			BlockBlobClient stateBlob = new BlockBlobClient("UseDevelopmentStorage=true", "states", $"{matchId}.json");
			StateInfo[] stateHistory = null;
			if (await stateBlob.ExistsAsync())
				using (Stream stream = await stateBlob.OpenReadAsync())
					stateHistory = JsonConvert.DeserializeObject<StateInfo[]>(Helper.ReadBytes(stream));
			return stateHistory;
		}

		// Action

		public async Task<ActionInfo> GetPlayerAction (ActionRequest request)
		{
			await Task.Delay(1);
			TableClient actionsTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");
			Azure.Pageable<PlayerActionEntity> query;
			int count;
			PlayerActionEntity lastAction;
			if (!string.IsNullOrEmpty(request.ActionName))
			{
				query = actionsTable.Query<PlayerActionEntity>(entry =>
					entry.PartitionKey == request.MatchId &&
					entry.RowKey == request.PlayerId &&
					entry.ActionName == request.ActionName);
				count = query.Count();
				if (count == 0)
					return null;
				if (count == 1)
					return new ActionInfo { ActionName = request.ActionName, Parameter = JsonConvert.DeserializeObject<object>(query.First().SerializedParameter) };
				lastAction = query.MaxBy(entry => entry.Timestamp.Value.UtcTicks);
				return new ActionInfo { ActionName = lastAction.ActionName, Parameter = JsonConvert.DeserializeObject<object>(lastAction.SerializedParameter) };
			}
			query = actionsTable.Query<PlayerActionEntity>(entry =>
				entry.PartitionKey == request.MatchId &&
				entry.RowKey == request.PlayerId);
			count = query.Count();
			if (count == 0)
				return null;
			lastAction = query.MaxBy(entry => entry.Timestamp.Value.UtcTicks);
			return new ActionInfo { ActionName = lastAction.ActionName, Parameter = JsonConvert.DeserializeObject<object>(lastAction.SerializedParameter) };
		}
	}
}