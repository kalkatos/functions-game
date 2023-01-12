using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using FunctionsGame.NetworkModel;
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

			// Get player region and other matchmaking related info
			BlockBlobClient playerInfoFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");
			using Stream stream = await playerInfoFile.OpenReadAsync();
			string playerRegistrySerialized = Helper.ReadBytes(stream);
			PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerRegistrySerialized);
			string playerRegion = playerRegistry.Region;

			// Check if there is an entry in matchmaking for this player, if not add one
			TableClient matchmakingTable = new TableClient("UseDevelopmentStorage=true", "matchmaking");
			var playerQuery = matchmakingTable.QueryAsync<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == playerRegion && entity.RowKey == playerId);
			var playerQueryEnumerator = playerQuery.GetAsyncEnumerator();
			if (!await playerQueryEnumerator.MoveNextAsync())
			{
				log.LogInformation($"No matchmaking registered for player. Registering...");
				await matchmakingTable.AddEntityAsync(new PlayerLookForMatchEntity { PartitionKey = playerRegion, RowKey = playerId, Status = (int)MatchmakingStatus.Searching }); 
			}
			else
			{
				log.LogInformation($"There is an entry for this player in matchmaking table...");
				PlayerLookForMatchEntity entry = playerQueryEnumerator.Current;
				// TODO Check if the entry for this player is too old, case which we should get another one
				switch (entry.Status)
				{
					case (int)MatchmakingStatus.Searching:
					case (int)MatchmakingStatus.Matched:
						log.LogInformation($"Player is already registered for matchmaking.");
						break;
					case (int)MatchmakingStatus.Backfilling:
						break;
					case (int)MatchmakingStatus.Failed:
					case (int)MatchmakingStatus.FailedWithNoPlayers:
						log.LogInformation($"Renewing player matchmaking entry.");
						playerQueryEnumerator.Current.Status = (int)MatchmakingStatus.Searching;
						await matchmakingTable.UpdateEntityAsync(entry, entry.ETag, TableUpdateMode.Replace);
						break;
				}
			}

			// Check orchestrator
			string orchestratorId = $"Orchestrator-{playerRegion}";
			DurableOrchestrationStatus functionStatus = await durableFunctionsClient.GetStatusAsync(orchestratorId);

			if (functionStatus == null
				|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed
				|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed
				|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
			{
				log.LogInformation($"No orchestrator running for this region ({playerRegion}), starting a new one.");
				await durableFunctionsClient.StartNewAsync(nameof(MatchmakingOrchestrator), orchestratorId, new OrchestratorInfo { ExecutionCount = 0, Region = playerRegion });
			}
			else
				log.LogInformation($"Orchestrator already running for region ({playerRegion}). Json = {JsonConvert.SerializeObject(functionStatus)}");

			return new OkObjectResult("Ok");
		}

		#region Orchestrator ======================================================================================

		[FunctionName(nameof(MatchmakingOrchestrator))]
		public static async Task MatchmakingOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			OrchestratorInfo info = context.GetInput<OrchestratorInfo>();
			if (info.Rules == null)
				info.Rules = await context.CallActivityAsync<ServerRules>(nameof(GetServerRules), null);

			// Wait to check
			DateTime nextCheck = context.CurrentUtcDateTime.AddSeconds(info.Rules.DelayBetweenAttempts);
			await context.CreateTimer(nextCheck, CancellationToken.None);

			log.LogInformation($"Starting new orchestration run.");


			TableMatchmakingResult matchmakingResult = await context.CallActivityAsync<TableMatchmakingResult>(nameof(PairPlayersInTable), info);
			switch (matchmakingResult)
			{
				case TableMatchmakingResult.NoPlayers:
				case TableMatchmakingResult.UnmatchedPlayersWaiting:
					info.ExecutionCount++;
					log.LogInformation($"Execution number {info.ExecutionCount}.");
					break;
				case TableMatchmakingResult.PlayersMatchedWithBots:
				case TableMatchmakingResult.PlayersMatched:
					info.ExecutionCount = 0;
					log.LogInformation($"Players Matched!!!");
					break;
			}
			
			// Try X times while no player is found to match
			if (info.ExecutionCount < info.Rules.MaxAttempts)
				context.ContinueAsNew(info);
			else
				log.LogInformation($"The orchestrator has reached max attempts and is finishing off.");
		}

		[FunctionName(nameof(PairPlayersInTable))]
		public static TableMatchmakingResult PairPlayersInTable (
			[ActivityTrigger] OrchestratorInfo info,
			[Table("matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			[Blob("rules/server-rules.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string serializedRules,
			ILogger log)
		{
			log.LogInformation("Looking for players to match.");

			// Get players looking for match
			int searchingStatus = (int)MatchmakingStatus.Searching;
			var query = tableClient.Query<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == info.Region && entity.Status == searchingStatus);
			if (query.Count() == 0)
			{
				log.LogInformation("No players to match.");
				return TableMatchmakingResult.NoPlayers;
			}

			// Get server rules
			ServerRules rules = JsonConvert.DeserializeObject<ServerRules>(serializedRules);
			List<PlayerLookForMatchEntity> matchingPlayersList = new List<PlayerLookForMatchEntity>();
			bool hadAMatch = false;
			// Run through query matching available players
			foreach (var item in query)
			{
				string currentPlayer = item.RowKey;
				if (matchingPlayersList.Find(p => p.RowKey == currentPlayer) != null || !string.IsNullOrEmpty(item.MatchId) || item.Status != (int)MatchmakingStatus.Searching)
					continue;
				matchingPlayersList.Add(item);
				// TODO Backfill
				if (matchingPlayersList.Count == rules.MinPlayerCount)
				{
					string playerList = JsonConvert.SerializeObject(matchingPlayersList);
					log.LogInformation($"Players matching: {playerList}");
					string newMatchId = Guid.NewGuid().ToString();
					CreateMatch(matchingPlayersList);
					log.LogInformation($"Matched players in match {newMatchId} === {playerList}");
					matchingPlayersList.Clear();
					hadAMatch = true;
				}
			}
			if (hadAMatch)
				return TableMatchmakingResult.PlayersMatched;

			if (info.ExecutionCount == rules.MaxAttempts - 1)
			{
				if (rules.ActionForNoPlayers == MatchmakingNoPlayerAction.MatchWithBots)
				{
					// Check if it's everything alright with the number of players and min number of players
					if (rules.MinPlayerCount < matchingPlayersList.Count)
						log.LogWarning($"Something is wrong: {rules.MinPlayerCount} is less than {matchingPlayersList.Count}");

					log.LogInformation($"Max attempts reached ({rules.MaxAttempts}). Filling with bots.");
					int numberOfBots = rules.MinPlayerCount - matchingPlayersList.Count;
					// Create bot in table
					for (int i = 0; i < numberOfBots; i++)
					{
						// Add bot entry to the matchmaking table
						string botId = Guid.NewGuid().ToString();
						PlayerLookForMatchEntity botEntity = new PlayerLookForMatchEntity { PartitionKey = info.Region, RowKey = botId };
						tableClient.AddEntity(botEntity);
						matchingPlayersList.Add(tableClient.GetEntity<PlayerLookForMatchEntity>(info.Region, botId).Value);

						//TODO Register each bot
					}
					CreateMatch(matchingPlayersList);
					matchingPlayersList.Clear();
					return TableMatchmakingResult.PlayersMatchedWithBots;
				}

				log.LogInformation($"Max attempts reached ({rules.MaxAttempts}). Returning failed matchmaking state.");
				foreach (var item in matchingPlayersList)
				{
					item.Status = (int)MatchmakingStatus.FailedWithNoPlayers;
					tableClient.UpdateEntity(item, item.ETag, TableUpdateMode.Replace);
				}
				return TableMatchmakingResult.NoPlayers;
			}
			log.LogInformation($"Not enough players to match ({matchingPlayersList.Count}).");
			return TableMatchmakingResult.UnmatchedPlayersWaiting;

			void CreateMatch (List<PlayerLookForMatchEntity> entities)
			{
				string newMatchId = Guid.NewGuid().ToString();

				string[] players = new string[entities.Count];
				for (int i = 0; i < entities.Count; i++)
				{
					PlayerLookForMatchEntity entity = entities[i];
					string alias = Guid.NewGuid().ToString();
					entity.MatchId = newMatchId;
					entity.MyAlias = alias;
					entity.Status = (int)MatchmakingStatus.Matched;
					tableClient.UpdateEntity(entity, entity.ETag, TableUpdateMode.Replace);
					players[i] = alias;
				}

				// Create the match in blob container
				MatchRegistry matchInfo = new MatchRegistry
				{
					MatchId = newMatchId,
					Players = players
				};
				BlockBlobClient blobClient = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{newMatchId}.json");
				using Stream stream = blobClient.OpenWrite(true);
				stream.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(matchInfo)));
			}
		}

		[FunctionName(nameof(GetServerRules))]
		public static ServerRules GetServerRules (
			[ActivityTrigger] string input,
			[Blob("rules/server-rules.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string readInfo,
			ILogger log)
		{
			log.LogInformation($"{nameof(GetServerRules)} === {readInfo}");
			return JsonConvert.DeserializeObject<ServerRules>(readInfo);
		}

		/*
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
		*/

		#endregion =======================================================================================

		[FunctionName(nameof(GetMatch))]
		public static async Task<string> GetMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string playerId,
			[Table("matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			ILogger log
			)
		{
			// TODO Get the match id of the match to which that player is assigned in the matchmaking table

			// TODO Get the match with the id in the matches blob
			//BlockBlobClient blobClient = new BlockBlobClient("UseDevelopmentStorage=true", "matches");

			// TODO Return the match serialized 

			await Task.Delay(500);
			return null;
		}

		// TODO Function to update ServerRules
	}

	public enum TableMatchmakingResult
	{
		NoPlayers,
		PlayersMatched,
		PlayersMatchedWithBots,
		UnmatchedPlayersWaiting
	}

	public enum MatchmakingStatus
	{
		Searching = 0,
		Matched = 1,
		Backfilling = 2,
		Failed = 3,
		FailedWithNoPlayers = 4,
	}

	public class OrchestratorInfo
	{
		public string Region;
		public int ExecutionCount;
		public ServerRules Rules;
	}

	public class PlayerLookForMatchEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Player region & other matchmaking data
		public string RowKey { get; set; } // Player ID
		public string MatchId { get; set; }
		public string MyAlias { get; set; } // This player alias in that match
		public int Status { get; set; }
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	}

	public class ServerRules
	{
		// Matchmaking
		public float DelayBetweenAttempts { get; set; }
		public int MaxAttempts { get; set; }
		public int MinPlayerCount { get; set; }
		public int MaxPlayerCount { get; set; }
		public bool HasBackfill { get; set; }
		public float WaitingTimeForBackfill { get; set; }
		public bool DoBackfillWithBots { get; set; }
		public MatchmakingNoPlayerAction ActionForNoPlayers { get; set; }
	}

	public enum MatchmakingNoPlayerAction
	{
		ReturnFailed,
		MatchWithBots,
	}
}