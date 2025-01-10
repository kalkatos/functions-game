#if AZURE_FUNCTIONS

using Azure.Data.Tables;
using Azure.Storage.Queues;
using Kalkatos.Network.Registry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Kalkatos.Network.Azure;

public class AzureService : IService
{
	public async Task DeleteData (string table, string partition, string key)
	{
		TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
		await tableClient.CreateIfNotExistsAsync();
		await tableClient.DeleteEntityAsync(partition, key);
	}

	public async Task<Dictionary<string, string>> GetAllData (string table, string partition, string filter)
	{
		TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
		await tableClient.CreateIfNotExistsAsync();
		if (string.IsNullOrEmpty(partition))
			partition = Global.DEFAULT_PARTITION;
		if (string.IsNullOrEmpty(filter))
			filter = $"PartitionKey eq '{partition}'";
		else
			filter = $"PartitionKey eq '{partition}' and {filter}";
		var tableQuery = tableClient.QueryAsync<TableEntity>(filter);
		Dictionary<string, string> result = new();
		await foreach (var entity in tableQuery)
			result.Add(entity.RowKey, entity["Value"] as string);
		return result;
	}

	public async Task<string> GetData (string table, string partition, string key, string defaultValue)
	{
		TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
		await tableClient.CreateIfNotExistsAsync();
		var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(partition, key);
		if (!response.HasValue)
			return defaultValue;
		TableEntity entity = response.Value;
		return entity["Value"] as string;
	}

	public async Task UpsertData (string table, string partition, string key, string value)
	{
		TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
		await tableClient.CreateIfNotExistsAsync();

		TableEntity entity = new TableEntity
		{
			PartitionKey = partition,
			RowKey = key
		};
		Dictionary<string, string> dict = new();
		Helper.DismemberData(ref dict, key, value);
		foreach (var item in dict)
			entity.Add(item.Key, item.Value);

		if (table == Global.MATCHES_TABLE)
		{
			string savedMatch = await GetData(table, partition, key, "");
			if (string.IsNullOrEmpty(savedMatch))
				await ScheduleCheckMatch(key, 0);
		}

		if (table == Global.STATES_TABLE)
		{
			StateRegistry state = JsonConvert.DeserializeObject<StateRegistry>(value);
			Logger.LogWarning("Registering state with hash: " + state.Hash);
		}

		await tableClient.UpsertEntityAsync(entity);
	}

	internal async Task ScheduleCheckMatch (string matchId, int lastHash)
	{
		await MatchFunctions.CheckGameSettings();
		int millisecondsDelay = Global.Game.Settings.CheckMatchDelay * 1000;
		QueueClient checkMatchQueue = new QueueClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "check-match");
		await checkMatchQueue.CreateIfNotExistsAsync();
		string message = $"{matchId}|{lastHash}";
		var bytes = Encoding.UTF8.GetBytes(message);
		Logger.LogWarning($"   [ScheduleCheckMatch] New match check in {millisecondsDelay}ms for match {matchId}");
		await checkMatchQueue.SendMessageAsync(Convert.ToBase64String(bytes), TimeSpan.FromMilliseconds(millisecondsDelay));
	}
}


#endif