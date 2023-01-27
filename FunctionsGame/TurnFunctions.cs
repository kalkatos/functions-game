using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Kalkatos.FunctionsGame.Registry;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
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
			log.LogInformation($"[{nameof(StartMatch)}] New match created.");

			using (Stream stream = await matchesBlobClient.OpenReadAsync())
			{
				string matchRegistrySerialized = Helper.ReadBytes(stream);
				log.LogInformation($"[{nameof(StartMatch)}] Match info got === {matchRegistrySerialized}");
				await durableFunctionsClient.StartNewAsync(nameof(TurnOrchestrator), null, matchRegistrySerialized);
			}
		}

		[FunctionName(nameof(TurnOrchestrator))]
		public static async Task TurnOrchestrator (
			[OrchestrationTrigger] IDurableOrchestrationContext context,
			ILogger log)
		{
			log.LogInformation($"[{nameof(TurnOrchestrator)}] Started.");

			string matchSerialized = context.GetInput<string>();
			MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(matchSerialized);

			log.LogInformation($"[{nameof(TurnOrchestrator)}] Working on match === {match.MatchId}");

			// DEBUG
			DateTime nextCheck = context.CurrentUtcDateTime.AddSeconds(20);
			await context.CreateTimer(nextCheck, CancellationToken.None);
			await context.CallActivityAsync(nameof(DeleteMatch), match);

			// TODO Wait for each player proof of life

			// TODO Run turn in loops
		}

		// TODO Move to clean up functions
		[FunctionName(nameof(DeleteMatch))]
		public static void DeleteMatch (
			[ActivityTrigger] MatchRegistry match,
			[Table("Matchmaking", Connection = "AzureWebJobsStorage")] TableClient tableClient,
			ILogger log)
		{
			log.LogWarning($"[{nameof(DeleteMatch)}] Deleting {match.MatchId}");

			// Delete match blob file
			BlockBlobClient matchesBlobClient = new BlockBlobClient("UseDevelopmentStorage=true", "matches", $"{match.MatchId}.json");
			matchesBlobClient.Delete();

			log.LogInformation($"[{nameof(DeleteMatch)}] Deleting matchmaking entries related to match {match.MatchId}");

			// Delete in matchmaking
			foreach (var player in match.PlayerIds)
			{
				Response response = tableClient.DeleteEntity(match.Region, player, ETag.All);
				if (response.IsError)
					log.LogError($"[{nameof(DeleteMatch)}] Error deleting Matchmaking entry for player {player} ==== Message = {response.ReasonPhrase}");
			}

			// TODO Delete bots
		}
	}

	public class PlayerActionEntity : ITableEntity
	{
		public string PartitionKey { get; set; } // Match ID
		public string RowKey { get; set; } // Player Alias
		public DateTimeOffset? Timestamp { get; set; }
		public ETag ETag { get; set; }
	}
}