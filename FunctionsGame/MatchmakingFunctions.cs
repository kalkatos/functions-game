using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Kalkatos.FunctionsGame.Azure;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Response = Kalkatos.Network.Model.Response;

namespace Kalkatos.FunctionsGame
{
	public static class MatchmakingFunctions
	{
		//[FunctionName(nameof(FindMatchOld))]
		//public static async Task<string> FindMatchOld (
		//	[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string playerId,
		//	[DurableClient] IDurableOrchestrationClient durableFunctionsClient,
		//	ILogger log)
		//{
		//	log.LogWarning($"   [{nameof(FindMatchOld)}] Executing Find Match.");

		//	string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
		//	// Get player region and other matchmaking related info
		//	BlockBlobClient playerInfoFile = new BlockBlobClient(connectionString, "players", $"{playerId}.json");
		//	using Stream stream = await playerInfoFile.OpenReadAsync();
		//	string playerRegistrySerialized = Helper.ReadBytes(stream);
		//	PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerRegistrySerialized);
		//	string playerRegion = playerRegistry.Region;

		//	// Check if there is an entry in matchmaking for this player, if not add one
		//	TableClient matchmakingTable = new TableClient(connectionString, "Matchmaking");
		//	var playerQuery = matchmakingTable.Query<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == playerRegion && entity.RowKey == playerId);
		//	if (playerQuery.Count() > 0)
		//	{
		//		log.LogWarning($"   [{nameof(FindMatchOld)}] Found previous matchmaking entries...");
		//		foreach (var item in playerQuery)
		//			await matchmakingTable.DeleteEntityAsync(item.PartitionKey, item.RowKey, item.ETag);
		//	}
		//	await matchmakingTable.AddEntityAsync(new PlayerLookForMatchEntity
		//	{
		//		PartitionKey = playerRegion,
		//		RowKey = playerId,
		//		PlayerInfoSerialized = JsonConvert.SerializeObject(playerRegistry.Info),
		//		Status = (int)MatchmakingStatus.Searching
		//	});

		//	// Check orchestrator
		//	string orchestratorId = $"Orchestrator-{playerRegion}";
		//	DurableOrchestrationStatus functionStatus = await durableFunctionsClient.GetStatusAsync(orchestratorId);

		//	if (functionStatus == null
		//		|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed
		//		|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed
		//		|| functionStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
		//	{
		//		log.LogWarning($"   [{nameof(FindMatchOld)}] No orchestrator running for this region ({playerRegion}), starting a new one.");
		//		await durableFunctionsClient.StartNewAsync(nameof(MatchmakingOrchestrator), orchestratorId, new MatchmakingOrchestratorInfo { ExecutionCount = 0, Region = playerRegion });
		//	}
		//	else
		//		log.LogWarning($"   [{nameof(FindMatchOld)}] Orchestrator already running for region ({playerRegion}). Json = {JsonConvert.SerializeObject(functionStatus)}");

		//	return $"{{\"{nameof(Response.IsError)}\":false,\"{nameof(Response.Message)}\":\"Ok.\"}}";
		//}




		[FunctionName(nameof(LeaveMatch))]
		public static async Task<string> LeaveMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			ILogger log
			)
		{
			log.LogWarning($"   [{nameof(LeaveMatch)}] Started.");

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
				log.LogWarning($"   [{nameof(LeaveMatch)}] Found no match. Query = {query}");
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
					log.LogWarning($"   [{nameof(LeaveMatch)}] Canceled successfully. Player = {request.PlayerId} Query = {query}");
				}
				else if (item.Status == (int)MatchmakingStatus.Matched)
				{
					string actionResponseSerialized = await AzureReceptor.SendAction(JsonConvert.SerializeObject(new ActionRequest
					{
						PlayerId = request.PlayerId,
						MatchId = item.MatchId,
						// TODO Actually leave the match
					}), log);
					log.LogWarning($"   [{nameof(LeaveMatch)}] Sent action to leave match. ActionResponse = {actionResponseSerialized}");
					return JsonConvert.SerializeObject(new MatchResponse { MatchId = item.MatchId, Message = "Sent action to leave match." });
				}
			}

			return JsonConvert.SerializeObject(new MatchResponse { Message = "Left match successfully." });
		}

		/*
		#region Orchestrator ======================================================================================


		[FunctionName(nameof(MatchmakingOrchestrator))]
		public static async Task MatchmakingOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			log.LogWarning($"   [{nameof(MatchmakingOrchestrator)}] Starting new orchestration run.");

			MatchmakingOrchestratorInfo info = context.GetInput<MatchmakingOrchestratorInfo>();
			if (info.Rules == null)
			{
				info.Rules = await context.CallActivityAsync<MatchmakingRules>(nameof(GetMatchmakingRules), null);
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
						log.LogWarning($"   [{nameof(MatchmakingOrchestrator)}] Execution number {info.ExecutionCount}.");
						break;
					case TableMatchmakingResult.PlayersMatchedWithBots:
					case TableMatchmakingResult.PlayersMatched:
						info.ExecutionCount = 0;
						log.LogWarning($"   [{nameof(MatchmakingOrchestrator)}] Players Matched!!!");
						break;
				}

				// Try X times while no player is found to match
				if (info.ExecutionCount < info.Rules.MaxAttempts)
					context.ContinueAsNew(info);
				else
					log.LogWarning($"   [{nameof(MatchmakingOrchestrator)}] The orchestrator has reached max attempts and is finishing off.");
			}
		}





		[FunctionName(nameof(PairPlayersInTable))]
		public static TableMatchmakingResult PairPlayersInTable (
			[ActivityTrigger] MatchmakingOrchestratorInfo info,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			[Blob("rules/matchmaking-rules.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string serializedRules,
			ILogger log)
		{
			log.LogWarning($"   [{nameof(PairPlayersInTable)}] Looking for players to match.");

			// Get players looking for match
			int searchingStatus = (int)MatchmakingStatus.Searching;
			var query = tableClient.Query<PlayerLookForMatchEntity>((entity) => entity.PartitionKey == info.Region && entity.Status == searchingStatus);
			if (query.Count() == 0)
			{
				log.LogWarning($"   [{nameof(PairPlayersInTable)}] No players to match.");
				return TableMatchmakingResult.NoPlayers;
			}

			// Get server rules
			MatchmakingRules rules = JsonConvert.DeserializeObject<MatchmakingRules>(serializedRules);
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
					log.LogWarning($"   [{nameof(PairPlayersInTable)}] Players matching: {playerList}");
					string newMatchId = Guid.NewGuid().ToString();
					CreateMatch(matchingPlayersList, info.Region, false);
					log.LogWarning($"   [{nameof(PairPlayersInTable)}] Matched players in match {newMatchId} === {playerList}");
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

					log.LogWarning($"   [{nameof(PairPlayersInTable)}] Max attempts reached ({rules.MaxAttempts}). Filling with bots.");
					int numberOfBots = rules.MinPlayerCount - matchingPlayersList.Count;
					// Create bot in table
					for (int i = 0; i < numberOfBots; i++)
					{
						// Add bot entry to the matchmaking table
						string botId = "X" + Guid.NewGuid().ToString();
						string botAlias = Guid.NewGuid().ToString();
						PlayerInfo botInfo = new PlayerInfo
						{
							Alias = botAlias,
							Nickname = MatchFunctions.CreateBotNickname(),
						};
						PlayerLookForMatchEntity botEntity = new PlayerLookForMatchEntity
						{
							PartitionKey = info.Region,
							RowKey = botId,
							PlayerInfoSerialized = JsonConvert.SerializeObject(botInfo),
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

				log.LogWarning($"   [{nameof(PairPlayersInTable)}] Max attempts reached ({rules.MaxAttempts}). Returning failed matchmaking state.");
				foreach (var item in matchingPlayersList)
				{
					item.Status = (int)MatchmakingStatus.FailedWithNoPlayers;
					tableClient.UpdateEntity(item, item.ETag, TableUpdateMode.Replace);
				}
				return TableMatchmakingResult.NoPlayers;
			}
			log.LogWarning($"   [{nameof(PairPlayersInTable)}] Not enough players to match ({matchingPlayersList.Count}).");
			return TableMatchmakingResult.UnmatchedPlayersWaiting;

			void CreateMatch (List<PlayerLookForMatchEntity> entities, string region, bool hasBots)
			{
				string newMatchId = Guid.NewGuid().ToString();

				PlayerInfo[] playerInfos = new PlayerInfo[entities.Count];
				string[] playerIds = new string[entities.Count];
				for (int i = 0; i < entities.Count; i++)
				{
					PlayerLookForMatchEntity entity = entities[i];
					entity.MatchId = newMatchId;
					entity.Status = (int)MatchmakingStatus.Matched;
					tableClient.UpdateEntity(entity, entity.ETag, TableUpdateMode.Replace);
					playerIds[i] = entity.RowKey;
					playerInfos[i] = JsonConvert.DeserializeObject<PlayerInfo>(entity.PlayerInfoSerialized);
					region = entity.PartitionKey;
				}

				// Create the match in blob container
				MatchRegistry matchInfo = new MatchRegistry
				{
					MatchId = newMatchId,
					PlayerInfos = playerInfos,
					PlayerIds = playerIds,
					Region = region,
					HasBots = hasBots,
					Status = (int)MatchStatus.AwaitingPlayers,
					CreatedTime = DateTime.UtcNow,
				};

				BlockBlobClient blobClient = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{newMatchId}.json");
				using Stream stream = blobClient.OpenWrite(true);
				stream.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(matchInfo)));

				MatchFunctions.CreateFirstState(matchInfo);

				Logger.Setup(log);
				MatchFunctions.VerifyMatch(newMatchId);
			}
		}





		[FunctionName(nameof(GetMatchmakingRules))]
		public static MatchmakingRules GetMatchmakingRules (
			[ActivityTrigger] string input,
			[Blob("rules/matchmaking-rules.json", FileAccess.Read, Connection = "AzureWebJobsStorage")] string readInfo,
			ILogger log)
		{
			log.LogWarning($"   [{nameof(GetMatchmakingRules)}] {nameof(GetMatchmakingRules)} === {readInfo}");
			return JsonConvert.DeserializeObject<MatchmakingRules>(readInfo);
		}


		#endregion =======================================================================================
		*/
	}

	public enum TableMatchmakingResult
	{
		NoPlayers,
		PlayersMatched,
		PlayersMatchedWithBots,
		UnmatchedPlayersWaiting
	}

	public class MatchmakingOrchestratorInfo
	{
		public string Region;
		public int ExecutionCount;
		public MatchmakingRules Rules;
	}

	
}