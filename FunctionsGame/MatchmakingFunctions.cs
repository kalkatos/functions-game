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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;

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
			log.LogInformation($"   [{nameof(FindMatch)}] Executing Find Match.");

			// Get player region and other matchmaking related info
			BlockBlobClient playerInfoFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");
			using Stream stream = await playerInfoFile.OpenReadAsync();
			string playerRegistrySerialized = Helper.ReadBytes(stream);
			PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerRegistrySerialized);
			string playerRegion = playerRegistry.Region;

			// Check if there is an entry in matchmaking for this player, if not add one
			TableClient matchmakingTable = new TableClient("UseDevelopmentStorage=true", "Matchmaking");
			var playerQuery = matchmakingTable.QueryAsync<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == playerRegion && entity.RowKey == playerId);
			var playerQueryEnumerator = playerQuery.GetAsyncEnumerator();
			if (!await playerQueryEnumerator.MoveNextAsync())
			{
				log.LogInformation($"   [{nameof(FindMatch)}] No matchmaking registered for player. Registering...");
				await matchmakingTable.AddEntityAsync(new PlayerLookForMatchEntity
				{
					PartitionKey = playerRegion,
					RowKey = playerId,
					PlayerAlias = playerRegistry.PlayerAlias,
					Status = (int)MatchmakingStatus.Searching
				});
			}
			else
			{
				log.LogInformation($"   [{nameof(FindMatch)}] There is an entry for this player in matchmaking table...");
				PlayerLookForMatchEntity entry = playerQueryEnumerator.Current;
				// TODO Check if the entry for this player is too old, case which we should get another one
				switch (entry.Status)
				{
					case (int)MatchmakingStatus.Searching:
					case (int)MatchmakingStatus.Matched:
						log.LogInformation($"   [{nameof(FindMatch)}] Player is already registered for matchmaking.");
						break;
					case (int)MatchmakingStatus.Backfilling:
						break;
					default:
						log.LogInformation($"   [{nameof(FindMatch)}] Renewing player matchmaking entry.");
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
				log.LogInformation($"   [{nameof(FindMatch)}] No orchestrator running for this region ({playerRegion}), starting a new one.");
				await durableFunctionsClient.StartNewAsync(nameof(MatchmakingOrchestrator), orchestratorId, new MatchmakingOrchestratorInfo { ExecutionCount = 0, Region = playerRegion });
			}
			else
				log.LogInformation($"   [{nameof(FindMatch)}] Orchestrator already running for region ({playerRegion}). Json = {JsonConvert.SerializeObject(functionStatus)}");

			return new OkObjectResult("Ok");
		}

		#region Orchestrator ======================================================================================

		[FunctionName(nameof(MatchmakingOrchestrator))]
		public static async Task MatchmakingOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(MatchmakingOrchestrator)}] Starting new orchestration run.");

			MatchmakingOrchestratorInfo info = context.GetInput<MatchmakingOrchestratorInfo>();
			if (info.Rules == null)
			{
				info.Rules = await context.CallActivityAsync<ServerRules>(nameof(GetServerRules), null);
				await TryPairing();
			}

			// Wait to check
			DateTime nextCheck = context.CurrentUtcDateTime.AddSeconds(info.Rules.DelayBetweenAttempts);
			await context.CreateTimer(nextCheck, CancellationToken.None);

			await TryPairing();

			async Task TryPairing ()
			{

				TableMatchmakingResult matchmakingResult = await context.CallActivityAsync<TableMatchmakingResult>(nameof(PairPlayersInTable), info);
				switch (matchmakingResult)
				{
					case TableMatchmakingResult.NoPlayers:
					case TableMatchmakingResult.UnmatchedPlayersWaiting:
						info.ExecutionCount++;
						log.LogInformation($"   [{nameof(MatchmakingOrchestrator)}] Execution number {info.ExecutionCount}.");
						break;
					case TableMatchmakingResult.PlayersMatchedWithBots:
					case TableMatchmakingResult.PlayersMatched:
						info.ExecutionCount = 0;
						log.LogInformation($"   [{nameof(MatchmakingOrchestrator)}] Players Matched!!!");
						break;
				}

				// Try X times while no player is found to match
				if (info.ExecutionCount < info.Rules.MaxAttempts)
					context.ContinueAsNew(info);
				else
					log.LogInformation($"   [{nameof(MatchmakingOrchestrator)}] The orchestrator has reached max attempts and is finishing off.");
			}
		}

		[FunctionName(nameof(PairPlayersInTable))]
		public static TableMatchmakingResult PairPlayersInTable (
			[ActivityTrigger] MatchmakingOrchestratorInfo info,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			[Blob("rules/server-rules.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string serializedRules,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(PairPlayersInTable)}] Looking for players to match.");

			// Get players looking for match
			int searchingStatus = (int)MatchmakingStatus.Searching;
			var query = tableClient.Query<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == info.Region && entity.Status == searchingStatus);
			if (query.Count() == 0)
			{
				log.LogInformation($"   [{nameof(PairPlayersInTable)}] No players to match.");
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
					log.LogInformation($"   [{nameof(PairPlayersInTable)}] Players matching: {playerList}");
					string newMatchId = Guid.NewGuid().ToString();
					CreateMatch(matchingPlayersList, info.Region, false);
					log.LogInformation($"   [{nameof(PairPlayersInTable)}] Matched players in match {newMatchId} === {playerList}");
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
						log.LogWarning($"   [{nameof(PairPlayersInTable)}] Something is wrong: {rules.MinPlayerCount} is less than {matchingPlayersList.Count}");

					log.LogInformation($"   [{nameof(PairPlayersInTable)}] Max attempts reached ({rules.MaxAttempts}). Filling with bots.");
					int numberOfBots = rules.MinPlayerCount - matchingPlayersList.Count;
					// Create bot in table
					for (int i = 0; i < numberOfBots; i++)
					{
						// Add bot entry to the matchmaking table
						string botId = Guid.NewGuid().ToString();
						string botAlias = Guid.NewGuid().ToString();
						PlayerLookForMatchEntity botEntity = new PlayerLookForMatchEntity
						{
							PartitionKey = info.Region,
							RowKey = botId,
							PlayerAlias = botAlias,
							Status = (int)MatchmakingStatus.Matched
						};
						tableClient.AddEntity(botEntity);
						matchingPlayersList.Add(tableClient.GetEntity<PlayerLookForMatchEntity>(info.Region, botId).Value);

						//TODO Register each bot

					}
					CreateMatch(matchingPlayersList, info.Region, true);
					matchingPlayersList.Clear();
					return TableMatchmakingResult.PlayersMatchedWithBots;
				}

				log.LogInformation($"   [{nameof(PairPlayersInTable)}] Max attempts reached ({rules.MaxAttempts}). Returning failed matchmaking state.");
				foreach (var item in matchingPlayersList)
				{
					item.Status = (int)MatchmakingStatus.FailedWithNoPlayers;
					tableClient.UpdateEntity(item, item.ETag, TableUpdateMode.Replace);
				}
				return TableMatchmakingResult.NoPlayers;
			}
			log.LogInformation($"   [{nameof(PairPlayersInTable)}] Not enough players to match ({matchingPlayersList.Count}).");
			return TableMatchmakingResult.UnmatchedPlayersWaiting;

			void CreateMatch (List<PlayerLookForMatchEntity> entities, string region, bool hasBots)
			{
				string newMatchId = Guid.NewGuid().ToString();

				string[] playerAliases = new string[entities.Count];
				string[] playerIds = new string[entities.Count];
				for (int i = 0; i < entities.Count; i++)
				{
					PlayerLookForMatchEntity entity = entities[i];
					entity.MatchId = newMatchId;
					entity.Status = (int)MatchmakingStatus.Matched;
					tableClient.UpdateEntity(entity, entity.ETag, TableUpdateMode.Replace);
					playerAliases[i] = entity.PlayerAlias;
					playerIds[i] = entity.RowKey;
					region = entity.PartitionKey;
				}

				// Create the match in blob container
				MatchRegistry matchInfo = new MatchRegistry
				{
					MatchId = newMatchId,
					PlayerAliases = playerAliases,
					PlayerIds = playerIds,
					Region = region,
					HasBots = hasBots,
					IsEnded = false,
					CreatedTime = DateTime.UtcNow,
					LastUpdatedTime = DateTime.UtcNow,
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
			log.LogInformation($"   [{nameof(GetServerRules)}] {nameof(GetServerRules)} === {readInfo}");
			return JsonConvert.DeserializeObject<ServerRules>(readInfo);
		}

		/*
		[FunctionName(nameof(ReadData))]
		public static string ReadData (
			[ActivityTrigger] string input,
			[Blob("test/custom-entry.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string readInfo,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(StartMatch)}] =========>   Read: {readInfo}");
			return readInfo;
		}

		[FunctionName(nameof(WriteData))]
		public static void WriteData (
			[ActivityTrigger] string input,
			[Blob("test/custom-entry.json", FileAccess.Write, Connection = "AzureWebJobsStorage")] out string output,
			ILogger log)
		{
			output = input;
			log.LogInformation($"   [{nameof(StartMatch)}] =========>   Writen: {input}");
		}
		*/

		#endregion =======================================================================================

		[FunctionName(nameof(GetMatch))]
		public static async Task<string> GetMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			ILogger log
			)
		{
			MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);

			if (request == null || string.IsNullOrEmpty(request.PlayerId))
			{
				log.LogInformation($"   [{nameof(GetMatch)}] Wrong Parameters. Request = {request}, Player ID = {request?.PlayerId ?? "<empty>"}");
				return JsonConvert.SerializeObject(new MatchResponse { IsError = true, Message = "Wrong Parameters." });
			}

			if (string.IsNullOrEmpty(request.MatchId))
			{
				// Get the match id of the match to which that player is assigned in the matchmaking table
				var query = tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == request.PlayerId);
				if (query == null || query.Count() == 0)
				{
					log.LogInformation($"   [{nameof(GetMatch)}] Found no match. Query = {query}");
					return JsonConvert.SerializeObject(new MatchResponse { IsError = true, Message = $"Didn't find any match for player." });
				}
				if (query.Count() > 1)
					log.LogWarning($"[{nameof(GetMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {query}");

				var playerEntry = query.First();
				string matchId = playerEntry.MatchId;
				request.MatchId = matchId;
				log.LogInformation($"   [{nameof(GetMatch)}] Found a match: {matchId}");
			}

			// Get the match with the id in the matches blob
			BlockBlobClient matchesBlob = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{request.MatchId}.json");
			if (await matchesBlob.ExistsAsync())
			{
				string[] players = null;
				using (Stream stream = await matchesBlob.OpenReadAsync())
				{
					string serializedMatch = Helper.ReadBytes(stream);
					MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(serializedMatch);
					players = match.PlayerAliases.Clone() as string[];
					log.LogInformation($"   [{nameof(GetMatch)}] Serialized match === {serializedMatch}");
				}

				return JsonConvert.SerializeObject(new MatchResponse
				{
					MatchId = request.MatchId,
					Players = players
				});
			}
			log.LogInformation($"   [{nameof(GetMatch)}] Found no match file with id {request.MatchId}");
			return JsonConvert.SerializeObject(new MatchResponse
			{
				IsError = true,
				Message = $"Match with id {request.MatchId} wasn't found."
			});
		}

		[FunctionName(nameof(LeaveMatch))]
		public static async Task<string> LeaveMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			ILogger log
			)
		{
			log.LogInformation($"   [{nameof(LeaveMatch)}] Started.");

			MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);

			if (request == null || string.IsNullOrEmpty(request.PlayerId))
			{
				log.LogError($"[{nameof(LeaveMatch)}] Wrong Parameters. Player ID = {request?.PlayerId ?? "<empty>"}, Request = {requestSerialized}");
				return JsonConvert.SerializeObject(new MatchResponse { IsError = true, Message = "Wrong Parameters." });
			}

			// Get the match id of the match to which that player is assigned in the matchmaking table
			var query = tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == request.PlayerId);
			if (query == null || query.Count() == 0)
			{
				log.LogInformation($"   [{nameof(LeaveMatch)}] Found no match. Query = {query}");
				return JsonConvert.SerializeObject(new MatchResponse { Message = $"Didn't find any match for player." });
			}
			if (query.Count() > 1)
				log.LogWarning($"[{nameof(LeaveMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {query}");

			foreach (var item in query)
			{
				if (item.Status == (int)MatchmakingStatus.Searching)
				{
					item.Status = (int)MatchmakingStatus.Canceled;
					await tableClient.UpdateEntityAsync(item, item.ETag);
					log.LogInformation($"   [{nameof(LeaveMatch)}] Canceled successfully. Player = {request.PlayerId} Query = {query}");
				}
				else if (item.Status == (int)MatchmakingStatus.Matched)
				{
					string actionResponseSerialized = await TurnFunctions.SendAction(JsonConvert.SerializeObject(new ActionRequest
					{
						ActionName = "LeaveMatch",
						PlayerId = request.PlayerId,
						MatchId = item.MatchId
					}), log);
					log.LogInformation($"   [{nameof(LeaveMatch)}] Sent action to leave match. ActionResponse = {actionResponseSerialized}");
					return JsonConvert.SerializeObject(new MatchResponse { MatchId = item.MatchId, Message = "Sent action to leave match." });
				}
			}

			return JsonConvert.SerializeObject(new MatchResponse { Message = "Left match successfully." });
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
		Canceled = 5,
	}

	public class MatchmakingOrchestratorInfo
	{
		public string Region;
		public int ExecutionCount;
		public ServerRules Rules;
	}

	public class PlayerLookForMatchEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Player region & other matchmaking data
		public string RowKey { get; set; } // Player ID
		public string PlayerAlias { get; set; }
		public string MatchId { get; set; }
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

	/*
	{
	"DelayBetweenAttempts":3,
	"MaxAttempts":3,
	"MinPlayerCount":2,
	"MaxPlayerCount":2,
	"HasBackfill":false,
	"WaitingTimeForBackfill":5,
	"DoBackfillWithBots":false,
	"ActionForNoPlayers":1,
	}
	 
	 */
	public enum MatchmakingNoPlayerAction
	{
		ReturnFailed,
		MatchWithBots,
	}
}