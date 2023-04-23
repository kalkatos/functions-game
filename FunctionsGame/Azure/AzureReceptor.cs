using Azure.Data.Tables;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame.Azure
{
	public static class AzureReceptor
	{
		// ================================= S T A R T U P ==========================================

		[FunctionName(nameof(LogIn))]
		public static async Task<string> LogIn (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(LogIn)}] Request = {requestSerialized}");
			LoginRequest request = JsonConvert.DeserializeObject<LoginRequest>(requestSerialized);
			LoginResponse response = await MatchFunctions.LogIn(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(LogIn)}] === {responseSerialized}");
			return responseSerialized;
		}

		[FunctionName(nameof(SetPlayerData))]
		public static async Task<string> SetPlayerData (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(SetPlayerData)}] Request = {requestSerialized}");
			SetPlayerDataRequest request = JsonConvert.DeserializeObject<SetPlayerDataRequest>(requestSerialized);
			Response response = await MatchFunctions.SetPlayerData(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(SetPlayerData)}] === {responseSerialized}");
			return responseSerialized;
		}

		// ================================= M A T C H M A K I N G ==========================================

		[FunctionName(nameof(FindMatch))]
		public static async Task<string> FindMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(FindMatch)}] Request = {requestSerialized}");
			FindMatchRequest request = JsonConvert.DeserializeObject<FindMatchRequest>(requestSerialized);
			Response response = await MatchFunctions.FindMatch(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(FindMatch)}] === {responseSerialized}");
			return responseSerialized;
		}

		[FunctionName(nameof(GetMatch))]
		public static async Task<string> GetMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(GetMatch)}] Request = {requestSerialized}");
			MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);
			MatchResponse response = await MatchFunctions.GetMatch(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(GetMatch)}] === {responseSerialized}");
			return responseSerialized;
		}

		// ================================= A C T I O N ==========================================

		[FunctionName(nameof(SendAction))]
		public static async Task<string> SendAction (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log
			)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(SendAction)}] Request = {requestSerialized}");
			ActionRequest request = JsonConvert.DeserializeObject<ActionRequest>(requestSerialized);
			ActionResponse response = await MatchFunctions.SendAction(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(SendAction)}] === {responseSerialized}");
			return responseSerialized;
		}

		// ================================= S T A T E ==========================================

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
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(GetMatchState)}] Request = {requestSerialized}");
			StateRequest request = JsonConvert.DeserializeObject<StateRequest>(requestSerialized);
			StateResponse response = await MatchFunctions.GetMatchState(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(GetMatchState)}] === {responseSerialized}");
			return responseSerialized;
		}

		// ================================= D E L E T E ==========================================

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

		[FunctionName(nameof(DeleteMatchDebug))]
		public static async Task DeleteMatchDebug (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string matchId,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(DeleteMatchDebug)}] Deleting match: {matchId}");
			await MatchFunctions.DeleteMatch(matchId);
		}

		[FunctionName(nameof(DeleteAllMatchesDebug))]
		public static async Task DeleteAllMatchesDebug (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string any,
			[Blob("matches", Connection = "AzureWebJobsStorage")] IEnumerable<string> blobs,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(DeleteAllMatchesDebug)}] Deleting all matches.");
			foreach (var item in blobs)
			{
				MatchRegistry match = JsonConvert.DeserializeObject<MatchRegistry>(item);
				await MatchFunctions.DeleteMatch(match.MatchId);
			}
		}
	}
}