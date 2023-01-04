using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Kalkatos.FunctionsGame
{
	public static class MatchmakingFunctions
	{
		[FunctionName(nameof(FindMatch))]
		public static async Task<IActionResult> FindMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string playerId,
			[DurableClient] IDurableOrchestrationClient durableFunctionsClient,
			ILogger log)
		{
			log.LogInformation($"Executing Find Match.");

			BlockBlobClient playerInfoFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");
			// TODO Get player region and other matchmaking related info
			string playerRegion = "en";
			
			// Check if there is an entry in matchmaking for this player, if not add one
			TableClient matchmakingTable = new TableClient("UseDevelopmentStorage=true", "matchmaking");
			var playerQuery = matchmakingTable.QueryAsync<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == playerRegion && entity.RowKey == playerId);
			var playerQueryEnumerator = playerQuery.GetAsyncEnumerator();
			if (!await playerQueryEnumerator.MoveNextAsync())
				await matchmakingTable.AddEntityAsync(new PlayerLookForMatchEntity { PartitionKey = playerRegion, RowKey = playerId });
			else
				// TODO Check if the entry for this player is too old, case which we should get another one
				log.LogInformation($"Player is already registered for matchmaking.");

			// Check orchestrator
			string orchestratorId = $"Orchestrator-{playerRegion}";
			DurableOrchestrationStatus functionStatus = await durableFunctionsClient.GetStatusAsync(orchestratorId);

			if (functionStatus == null 
				|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed 
				|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed
				|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
			{
				log.LogInformation($"No orchestrator running for this region ({playerRegion}), starting a new one.");
				await durableFunctionsClient.StartNewAsync(nameof(RunOrchestrator), orchestratorId, new OrchestratorInfo { ExecutionCount = 0, Region = playerRegion });
			}
			else
				log.LogInformation($"Orchestrator already running for region ({playerRegion}). Json = {JsonConvert.SerializeObject(functionStatus)}");

			return new OkObjectResult("Ok");
		}

		#region Orchestrator ======================================================================================

		[FunctionName(nameof(RunOrchestrator))]
		public static async Task RunOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			// Wait to check
			DateTime nextCheck = context.CurrentUtcDateTime.AddSeconds(2);
			await context.CreateTimer(nextCheck, CancellationToken.None);

			log.LogInformation($"Starting new orchestration run.");

			OrchestratorInfo info = context.GetInput<OrchestratorInfo>();
			TableMatchmakingResult matchmakingResult = await context.CallActivityAsync<TableMatchmakingResult>(nameof(PairPlayersInTable), info.Region);
			switch (matchmakingResult)
			{
				case TableMatchmakingResult.NoPlayers:
				case TableMatchmakingResult.UnmatchedPlayersWaiting:
					log.LogInformation($"Execution number {info.ExecutionCount}. Scheduling next attempt to match players.");
					info.ExecutionCount++;
					break;
				case TableMatchmakingResult.PlayersMatched:
					log.LogInformation($"Players Matched!!!");
					break;
			}

			int maxAttempts = 5;
			// Try 5 times while no player is found to match
			if (info.ExecutionCount < maxAttempts)
				context.ContinueAsNew(info);
			else
				log.LogInformation($"The orchestrator has reached max attempts and is finishing off.");
		}

		[FunctionName(nameof(PairPlayersInTable))]
		public static TableMatchmakingResult PairPlayersInTable (
			[ActivityTrigger] string region,
			[Table("matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			//[Blob("matches", FileAccess.Write, Connection = "AzureWebJobsStorage")] BlobBaseClient matchesBlob,
			ILogger log)
		{
			log.LogInformation("Looking for players to match.");
			var query = tableClient.Query<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == region);
			if (query.Count() == 0)
			{
				log.LogInformation("No players to match.");
				return TableMatchmakingResult.NoPlayers;
			}
			List<string> matchingPlayersList = new List<string>();
			bool hadAMatch = false;
			foreach (var item in query)
			{
				string currentPlayer = item.RowKey;
				if (matchingPlayersList.Contains(currentPlayer) || !string.IsNullOrEmpty(item.MatchId))
					continue;
				matchingPlayersList.Add(currentPlayer);
				// TODO ↓↓↓↓↓↓ Adapt for more players. Get this number from game rules 
				if (matchingPlayersList.Count == 2)
				{
					log.LogInformation($"Players matching: {JsonConvert.SerializeObject(matchingPlayersList)}");
					var p1 = tableClient.GetEntity<PlayerLookForMatchEntity>(region, matchingPlayersList[0]).Value;
					var p2 = tableClient.GetEntity<PlayerLookForMatchEntity>(region, matchingPlayersList[1]).Value;
					string newMatchId = Guid.NewGuid().ToString();
					string p1Alias = Guid.NewGuid().ToString();
					string p2Alias = Guid.NewGuid().ToString();
					p1.MatchId = newMatchId;
					p2.MatchId = newMatchId;
					p1.MyAlias = p1Alias;
					p2.MyAlias = p2Alias;
					log.LogInformation($"Starting updates... ...");
					tableClient.UpdateEntity(p1, p1.ETag, TableUpdateMode.Replace);
					tableClient.UpdateEntity(p2, p2.ETag, TableUpdateMode.Replace);
					// TODO Create the match in blob container
					log.LogInformation($"Matched players {matchingPlayersList[0].Substring(0, 8)}+ and {matchingPlayersList[1].Substring(0, 8)}+ in match {newMatchId}");
					matchingPlayersList.Clear();
					hadAMatch = true;
				}
			}
			if (hadAMatch)
				return TableMatchmakingResult.PlayersMatched;
			log.LogInformation($"Not enough players to match ({matchingPlayersList.Count}).");
			return TableMatchmakingResult.UnmatchedPlayersWaiting;
		}

		[FunctionName(nameof(ReadData))]
		public static string ReadData (
			[ActivityTrigger] string input,
			[Blob("test/custom-entry.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string readInfo,
			ILogger log)
		{
			log.LogInformation($"=========>   Read: {readInfo}");
			return readInfo;
		}

		[FunctionName(nameof(WriteData))]
		public static void WriteData (
			[ActivityTrigger] string input,
			[Blob("test/custom-entry.json", FileAccess.Write, Connection = "AzureWebJobsStorage")] out string output,
			ILogger log)
		{
			output = input;
			log.LogInformation($"=========>   Writen: {input}");
		}

		#endregion =======================================================================================
	}

	public enum TableMatchmakingResult
	{
		NoPlayers,
		PlayersMatched,
		UnmatchedPlayersWaiting
	}

	internal class OrchestratorInfo
	{
		public string Region;
		public int ExecutionCount;
	}

	internal class PlayerLookForMatchEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Player region & other matchmaking data
		public string RowKey { get; set; } // Player ID
		public string MatchId { get; set; }
		public string MyAlias { get; set; } // This player alias in that match
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	}

	//public class OrchestratorStatusEntity : ITableEntity
	//{
	//	public string PartitionKey { get; set; } // Player region & other matchmaking data
	//	public string RowKey { get; set; } // Anything
	//	public DateTimeOffset? Timestamp { get; set; }
	//	public ETag ETag { get; set; }
	//}
}