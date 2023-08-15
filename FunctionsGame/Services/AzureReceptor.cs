using Azure.Storage.Blobs.Specialized;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame.Azure
{
	public static class AzureReceptor
	{
		// ████████████████████████████████████████████ S T A R T U P ████████████████████████████████████████████

		[FunctionName(nameof(LogIn))]
		public static async Task<string> LogIn (
			[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
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
			[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(SetPlayerData)}] Request = {requestSerialized}");
			SetPlayerDataRequest request = JsonConvert.DeserializeObject<SetPlayerDataRequest>(requestSerialized);
			PlayerInfoResponse response = await MatchFunctions.SetPlayerData(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(SetPlayerData)}] === {responseSerialized}");
			return responseSerialized;
		}

		[FunctionName(nameof(GetGameSettings))]
		public static async Task<string> GetGameSettings (
			[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(GetGameSettings)}] Request = {requestSerialized}");
			GameDataRequest request = JsonConvert.DeserializeObject<GameDataRequest>(requestSerialized);
			GameDataResponse response = await MatchFunctions.GetGameSettings(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(GetGameSettings)}] === {responseSerialized}");
			return responseSerialized;
		}

		// ████████████████████████████████████████████ M A T C H M A K I N G ████████████████████████████████████████████

		[FunctionName(nameof(FindMatch))]
		public static async Task<string> FindMatch (
			[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
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
			[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
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

		[FunctionName(nameof(LeaveMatch))]
		public static async Task<string> LeaveMatch (
			[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(LeaveMatch)}] Request = {requestSerialized}");
			MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);
			Response response = await MatchFunctions.LeaveMatch(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			log.LogWarning($"   [{nameof(LeaveMatch)}] === {responseSerialized}");
			return responseSerialized;
		}

		// ████████████████████████████████████████████ A C T I O N ████████████████████████████████████████████

		[FunctionName(nameof(SendAction))]
		public static async Task<string> SendAction (
			[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
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

		// ████████████████████████████████████████████ S T A T E ████████████████████████████████████████████

		/// <summary>
		/// Gets an array of states starting from the index requested up until the last one available.
		/// </summary>
		/// <returns> A serialized <typeparamref screenName="StateResponse"/> with the array of states or error message. </returns>
		[FunctionName(nameof(GetMatchState))]
		public static async Task<string> GetMatchState (
			[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
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

		// ████████████████████████████████████████████ D E L E T E ████████████████████████████████████████████

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

		[FunctionName(nameof(DeleteAllMatchesDebug))]
		public static async Task DeleteAllMatchesDebug (
			[HttpTrigger(AuthorizationLevel.Admin, "get", Route = null)] string any,
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

		[FunctionName(nameof(AddDataToAllPlayersDebug))]
		public static async Task AddDataToAllPlayersDebug (
			[HttpTrigger(AuthorizationLevel.Admin, "post", Route = null)] string requestSerialized,
			[Blob("players", Connection = "AzureWebJobsStorage")] IEnumerable<string> blobs,
			ILogger log)
		{
			Logger.Setup(log);
			log.LogWarning($"   [{nameof(AddDataToAllPlayersDebug)}] Adding data to all players.");
			Dictionary<string, string> request = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestSerialized);
			if (request == null || request.Count == 0)
				log.LogError("Request came out empty.");
			foreach (var item in blobs)
			{
				PlayerRegistry registry = JsonConvert.DeserializeObject<PlayerRegistry>(item);
				if (registry == null)
					continue;
				PlayerInfo info = registry.Info;
				if (info.CustomData == null)
					info.CustomData = new Dictionary<string, string>();
				foreach (var data in request)
					info.CustomData[data.Key] = data.Value;
				BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{registry.PlayerId}.json");
				using (Stream stream = await playerBlob.OpenWriteAsync(true))
					stream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(registry)));
			}
		}
	}
}