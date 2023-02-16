using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Kalkatos.FunctionsGame
{
	public static class TurnFunctions
	{
		//[FunctionName(nameof(StartMatch))]
		//public static async Task StartMatch (
		//	[BlobTrigger("matches", Connection = "AzureWebJobsStorage")] BlockBlobClient matchesBlobClient,
		//	//[DurableClient] IDurableOrchestrationClient durableFunctionsClient,
		//	ILogger log)
		//{
		//	log.LogInformation($"   [{nameof(StartMatch)}] New match created.");
		//	Logger.Setup(log);

		//	string matchRegistrySerialized;
		//	using (Stream stream = await matchesBlobClient.OpenReadAsync())
		//		matchRegistrySerialized = Helper.ReadBytes(stream);
		//	log.LogInformation($"   [{nameof(StartMatch)}] Match info got === {matchRegistrySerialized}");
		//	MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchRegistrySerialized);
		//	//await durableFunctionsClient.StartNewAsync(nameof(TurnOrchestrator), match.MatchId, new TurnOrchestratorInfo { Match = match });
		//	QueueClient queueClient = new QueueClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "match-deletion");
		//	await queueClient.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(match.MatchId)), TimeSpan.FromSeconds(30));
		//}



		[FunctionName(nameof(DeleteMatchDebug))]
		public static async Task DeleteMatchDebug (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string matchId,
			ILogger log)
		{
			log.LogWarning($"   [{nameof(DeleteMatchDebug)}] Deleting match: {matchId}");
			Logger.Setup(log);

			await MatchFunctions.DeleteMatch(matchId);
		}




		// TODO Add one to delete all matches!
		[FunctionName(nameof(DeleteAllMatchesDebug))]
		public static async Task DeleteAllMatchesDebug (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string any,
			[Blob("matches", Connection = "AzureWebJobsStorage")] IEnumerable<string> blobs,
			ILogger log)
		{
			log.LogWarning($"   [{nameof(DeleteAllMatchesDebug)}] Deleting all matches.");
			Logger.Setup(log);

			foreach (var item in blobs)
			{
				MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(item);
				await MatchFunctions.DeleteMatch(match.MatchId);
			}
		}




		[FunctionName(nameof(SendAction))]
		public static async Task<string> SendAction (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log
			)
		{
			log.LogWarning($"   [{nameof(SendAction)}] Started.");

			ActionRequest request = JsonConvert.DeserializeObject<ActionRequest>(requestSerialized);

			ActionResponse response = await MatchFunctions.SendAction(request);

			return JsonConvert.SerializeObject(response);

			//// Check request
			//if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.PlayerAlias) || string.IsNullOrEmpty(request.MatchId))
			//{
			//	log.LogError($"   [{nameof(SendAction)}] Wrong Parameters. Request = {requestSerialized}");
			//	return JsonConvert.SerializeObject(new ActionResponse { IsError = true, Message = "Wrong Parameters." });
			//}

			//// Check if player is in the match
			//BlockBlobClient matchesBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{request.MatchId}.json");
			//if (await matchesBlob.ExistsAsync())
			//{
			//	using (Stream stream = await matchesBlob.OpenReadAsync())
			//	{
			//		MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(Helper.ReadBytes(stream));
			//		if (!match.HasPlayer(request.PlayerId))
			//			return JsonConvert.SerializeObject(new ActionResponse { IsError = true, Message = "Player is not registered for that match." });
			//	}
			//}
			//else
			//	return JsonConvert.SerializeObject(new ActionResponse { IsError = true, Message = "Match does not exist." });

			//// Open action table
			//TableClient actionTable = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "ActionHistory");

			//bool isActionDefined = false;

			//// TODO Check game rules if this action is expected


			//// Check default actions
			//switch (request.ActionName)
			//{
			//	case "Play":
			//	case "Move":
			//	case "Handshaking":
			//	case "LeaveMatch":
			//		isActionDefined = true;
			//		await actionTable.UpsertEntityAsync(new PlayerActionEntity
			//		{
			//			PartitionKey = request.MatchId,
			//			RowKey = request.PlayerId,
			//			PlayerAlias = request.PlayerAlias,
			//			ActionName = request.ActionName,
			//			SerializedParameter = request.SerializedParameter,
			//		});
			//		break;
			//}

			//// TODO Check parameter

			//if (!isActionDefined)
			//{
			//	log.LogError($"   [{nameof(SendAction)}] Action not defined. Request = {requestSerialized}");
			//	return JsonConvert.SerializeObject(new ActionResponse { IsError = true, Message = $"Action not defined: {request.ActionName}" }); 
			//}
			//log.LogInformation($"   [{nameof(SendAction)}] Action Registered. Request = {requestSerialized}");
			//return JsonConvert.SerializeObject(new ActionResponse { Message = $"Action {request.ActionName} registered." });
		}




		/// <summary>
		/// Gets an array of states starting from the index requested up until the last one available.
		/// </summary>
		/// <returns> A serialized <typeparamref screenName="StateResponse"/> with the array of states or error message. </returns>
		[FunctionName(nameof(GetMatchState))]
		public static async Task<string> GetMatchState (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log
			)
		{
			log.LogWarning($"   [{nameof(GetMatchState)}] Started.");
			Logger.Setup(log);

			StateRequest request = JsonConvert.DeserializeObject<StateRequest>(requestSerialized);

			StateResponse response = await MatchFunctions.GetMatchState(request);

			return JsonConvert.SerializeObject(response);
		}




		[FunctionName(nameof(TurnOrchestrator))]
		public static async Task TurnOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			//log.LogInformation($"   [{nameof(TurnOrchestrator)}] Started.");

			TurnOrchestratorInfo info = context.GetInput<TurnOrchestratorInfo>();
			MatchRegistry match = info.Match;
			TurnSettings settings = info.TurnSettings;

			//StateInfo state = await MatchFunctions.PrepareTurn(info.TurnIndex, match, gameSettings);
			//info.TurnIndex++;

			//if ()
			//{
			//	DateTime nextRun = context.CurrentUtcDateTime.AddSeconds(gameSettings.DelayBetweenRuns);
			//	await context.CreateTimer(nextRun, CancellationToken.None);
			//	context.ContinueAsNew(info);
			//}
			//else
			//	log.LogInformation($"   [{nameof(MatchmakingOrchestrator)}] The orchestrator has reached max attempts and is finishing off.");

			// DEBUG
			//DateTime nextCheck = context.CurrentUtcDateTime.AddSeconds(30);
			//await context.CreateTimer(nextCheck, CancellationToken.None);
			//await context.CallActivityAsync(nameof(DeleteMatch), match);

			// TODO Wait for each player handshake


			// TODO Run turn in loops

			await Task.Delay(1);
		}




		// TODO Move to clean up functions
		[FunctionName(nameof(DeleteMatch))]
		public static void DeleteMatch (
			[ActivityTrigger] MatchRegistry match,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			ILogger log)
		{
			log.LogWarning($"   [{nameof(DeleteMatch)}] Deleting {match.MatchId}");

			// Delete match blob file
			BlockBlobClient matchesBlobClient = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{match.MatchId}.json");
			if (matchesBlobClient.Exists())
				matchesBlobClient.Delete();

			log.LogInformation($"   [{nameof(DeleteMatch)}] Deleting matchmaking entries related to match {match.MatchId}");

			// Delete in matchmaking
			foreach (var player in match.PlayerIds)
			{
				var response = tableClient.DeleteEntity(match.Region, player, ETag.All);
				if (response.IsError)
					log.LogError($"   [{nameof(DeleteMatch)}] Error deleting matchmaking entry for player {player} === Message = {response.ReasonPhrase}");
			}

			// TODO Delete Action History

			// TODO Delete State History

			// TODO Delete bots bound to that match
		}

		[FunctionName(nameof(CheckMatch))]
		public static async Task CheckMatch (
			[QueueTrigger("check-match", Connection = "AzureWebJobsStorage")] string message,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(CheckMatch)}] Checking match from queue with message: {message}");

			string[] messageSplit = message.Split('|');
			string matchId = messageSplit[0];
			int lastHash = int.Parse(messageSplit[1]);

			await MatchFunctions.CheckMatch(matchId, lastHash);
		}
	}

	public class TurnOrchestratorInfo
	{
		public int TurnIndex;
		public MatchRegistry Match;
		public TurnSettings TurnSettings;
		public StateInfo LastState;
	}

	public class TurnSettings
	{
		public float DelayBetweenRuns;
	}

	public class PlayerActionEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Match ID
		public string RowKey { get; set; } // Player ID
		public string Content { get; set; }
		public string PlayerAlias { get; set; }
		public string ActionName { get; set; }
		public string SerializedParameter { get; set; }
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	} 
}