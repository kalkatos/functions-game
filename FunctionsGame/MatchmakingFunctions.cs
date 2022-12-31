using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Kalkatos.FunctionsGame
{
	public static class MatchmakingFunctions
	{
		[FunctionName(nameof(FindMatch))]
		public static async Task<IActionResult> FindMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string playerId,
			ILogger log)
		{
			log.LogInformation($"Executing Find Match.");

			BlockBlobClient playerInfoFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");

			string playerRegion = "en";
			TableClient matchmakingTable = new TableClient("UseDevelopmentStorage=true", "matchmaking");
			var orchestratorQuery = matchmakingTable.QueryAsync<OrchestratorStatusEntity>((entity) => entity.PartitionKey == playerRegion);
			var orchestratorQueryEnumerator = orchestratorQuery.GetAsyncEnumerator();
			await orchestratorQueryEnumerator.MoveNextAsync();
			if (orchestratorQueryEnumerator.Current == null )
			{
				log.LogInformation($"No entry in the table for an orchestrator in region {playerRegion}.");
				// TODO Start the orchestrator for the region
			}

			var playerQuery = matchmakingTable.QueryAsync<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == playerRegion && entity.RowKey == playerId);
			var playerQueryEnumerator = playerQuery.GetAsyncEnumerator();
			await playerQueryEnumerator.MoveNextAsync();
			if (playerQueryEnumerator.Current == null)
				await matchmakingTable.AddEntityAsync(new PlayerLookForMatchEntity { PartitionKey = playerRegion, RowKey = playerId });
			else
				log.LogInformation($"Player is already registered for matchmaking.");

			return new OkObjectResult("Ok");
		}
	}

	public class PlayerLookForMatchEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Player region & other matchmaking data
		public string RowKey { get; set; } // Player ID
		public string Match { get; set; }
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	}

	public class OrchestratorStatusEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Player region & other matchmaking data
		public string RowKey { get; set; } // Anything
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	}
}