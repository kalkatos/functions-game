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
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
	public static class TurnFunctions
	{
		[FunctionName(nameof(StartMatch))]
		public static async Task StartMatch (
			[BlobTrigger("matches", Connection = "AzureWebJobsStorage")] BlockBlobClient matchesBlobClient,
			[DurableClient] IDurableOrchestrationClient durableFunctionsClient,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(StartMatch)}] New match created.");

			using (Stream stream = await matchesBlobClient.OpenReadAsync())
			{
				string matchRegistrySerialized = Helper.ReadBytes(stream);
				log.LogInformation($"   [{nameof(StartMatch)}] Match info got === {matchRegistrySerialized}");
				MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchRegistrySerialized);
				await durableFunctionsClient.StartNewAsync(nameof(TurnOrchestrator), match.MatchId, matchRegistrySerialized);
			}
		}

		[FunctionName(nameof(TurnOrchestrator))]
		public static async Task TurnOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(TurnOrchestrator)}] Started.");

			string matchSerialized = context.GetInput<string>();
			MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);

			log.LogInformation($"   [{nameof(TurnOrchestrator)}] Working on match === {match.MatchId}");

			// DEBUG
			DateTime nextCheck = context.CurrentUtcDateTime.AddSeconds(60);
			await context.CreateTimer(nextCheck, CancellationToken.None);
			await context.CallActivityAsync(nameof(DeleteMatch), match);

			// TODO Wait for each player handshake


			// TODO Run turn in loops
		}

		[FunctionName(nameof(SendAction))]
		public static async Task<string> SendAction (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log
			)
		{
			log.LogWarning($"   [{nameof(SendAction)}] Started.");

			// TODO Register action
			ActionRequest request = JsonConvert.DeserializeObject<ActionRequest>(requestSerialized);

			// Check request
			if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
			{
				log.LogError($"   [{nameof(SendAction)}] Wrong Parameters. Request = {requestSerialized}");
				return JsonConvert.SerializeObject(new ActionResponse { IsError = true, Message = "Wrong Parameters." });
			}

			// Open action table
			TableClient actionTable = new TableClient("UseDevelopmentStorage=true", "ActionHistory");

			bool isActionDefined = false;
			// TODO Check game rules if this action is expected

			// Check default actions
			switch (request.ActionName)
			{
				case "Action1":
				case "Action2":
				case "Action3":
				case "LeaveMatch":
					isActionDefined = true;
					await actionTable.AddEntityAsync<PlayerActionEntity>(new PlayerActionEntity
					{
						PartitionKey = request.MatchId,
						RowKey = request.PlayerId,
						ActionName = request.ActionName,
						SerializedParameter = request.SerializedParameter,
					});
					break;
			}

			if (!isActionDefined)
			{
				log.LogError($"   [{nameof(SendAction)}] Action not defined. Request = {requestSerialized}");
				return JsonConvert.SerializeObject(new ActionResponse { IsError = true, Message = $"Action not defined: {request.ActionName}" }); 
			}
			log.LogInformation($"   [{nameof(SendAction)}] Action Registered. Request = {requestSerialized}");
			return JsonConvert.SerializeObject(new ActionResponse { Message = $"Action {request.ActionName} registered." });
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
			BlockBlobClient matchesBlobClient = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{match.MatchId}.json");
			if (matchesBlobClient.Exists())
				matchesBlobClient.Delete();

			log.LogInformation($"   [{nameof(DeleteMatch)}] Deleting matchmaking entries related to match {match.MatchId}");

			// Delete in matchmaking
			foreach (var player in match.PlayerIds)
			{
				Response response = tableClient.DeleteEntity(match.Region, player, ETag.All);
				if (response.IsError)
					log.LogError($"   [{nameof(DeleteMatch)}] Error deleting matchmaking entry for player {player} === Message = {response.ReasonPhrase}");
			}

			// TODO Delete bots
		}
	}

	public class PlayerActionEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Match ID
		public string RowKey { get; set; } // Player Alias
		public string ActionName { get; set; }
		public string SerializedParameter { get; set; }
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	} 
}