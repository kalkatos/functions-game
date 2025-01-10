#if AZURE_FUNCTIONS

using Kalkatos.Network.Registry;
using Kalkatos.Network.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QueueTriggerAttribute = Microsoft.Azure.WebJobs.QueueTriggerAttribute;
using System.Linq;

namespace Kalkatos.Network.Azure;

public static class AzureReceptor
{
	[FunctionName(nameof(GetData))]
	public static async Task<string> GetData (
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		DataRequest request = JsonConvert.DeserializeObject<DataRequest>(requestSerialized);
		DataResponse response = await LoginFunctions.GetData(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(GetData)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(SetData))]
	public static async Task<string> SetData (
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		DataRequest request = JsonConvert.DeserializeObject<DataRequest>(requestSerialized);
		Response response = await LoginFunctions.SetData(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(SetData)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ S T A R T U P ████████████████████████████████████████████

	[FunctionName(nameof(LogIn))]
	public static async Task<string> LogIn (
	[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
	ILogger log)
	{
		Logger.Setup(log);
		LoginRequest request = JsonConvert.DeserializeObject<LoginRequest>(requestSerialized);
		LoginResponse response = await LoginFunctions.LogIn(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(LogIn)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(SetPlayerData))]
	public static async Task<string> SetPlayerData (
	[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
	ILogger log)
	{
		Logger.Setup(log);
		SetPlayerDataRequest request = JsonConvert.DeserializeObject<SetPlayerDataRequest>(requestSerialized);
		PlayerInfoResponse response = await LoginFunctions.SetPlayerData(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(SetPlayerData)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(GetGameSettings))]
	public static async Task<string> GetGameSettings (
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		GameDataRequest request = JsonConvert.DeserializeObject<GameDataRequest>(requestSerialized);
		GameDataResponse response = await LoginFunctions.GetGameSettings(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(GetGameSettings)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ M A T C H M A K I N G ████████████████████████████████████████████

	[FunctionName(nameof(FindMatch))]
	public static async Task<string> FindMatch (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		FindMatchRequest request = JsonConvert.DeserializeObject<FindMatchRequest>(requestSerialized);
		Response response = await MatchFunctions.FindMatch(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(FindMatch)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(GetMatch))]
	public static async Task<string> GetMatch (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);
		MatchResponse response = await MatchFunctions.GetMatch(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(GetMatch)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(LeaveMatch))]
	public static async Task<string> LeaveMatch (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);
		Response response = await MatchFunctions.LeaveMatch(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		log.LogWarning($"   [{nameof(LeaveMatch)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
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
		ActionRequest request = JsonConvert.DeserializeObject<ActionRequest>(requestSerialized);
		ActionResponse response = await MatchFunctions.SendAction(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			log.LogError($"   [{nameof(SendAction)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
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
		StateRequest request = JsonConvert.DeserializeObject<StateRequest>(requestSerialized);
		StateResponse response = await MatchFunctions.GetMatchState(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			log.LogError($"   [{nameof(GetMatchState)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
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
		int? newHash = await MatchFunctions.CheckMatch(matchId, lastHash);
		if (newHash.HasValue)
			await ((AzureService)Global.Service).ScheduleCheckMatch(matchId, newHash.Value);
	}

	// ████████████████████████████████████████ L E A D E R B O A R D █████████████████████████████████████████

	[FunctionName(nameof(AddLeaderboardEvent))]
	public static async Task<string> AddLeaderboardEvent (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		LeaderboardEventRequest request = JsonConvert.DeserializeObject<LeaderboardEventRequest>(requestSerialized);
		Response response = await LeaderboardFunctions.AddLeaderboardEvent(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			log.LogError($"   [{nameof(AddLeaderboardEvent)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(GetLeaderboard))]
	public static async Task<string> GetLeaderboard (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		LeaderboardRequest request = JsonConvert.DeserializeObject<LeaderboardRequest>(requestSerialized);
		LeaderboardResponse response = await LeaderboardFunctions.GetLeaderboard(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			log.LogError($"   [{nameof(GetLeaderboard)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████ A N A L Y T I C S █████████████████████████████████████████

	[FunctionName(nameof(SendEvent))]
	public static async Task<string> SendEvent (
		[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		Dictionary<string, string> request = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestSerialized);
		Response response = await AnalyticsFunctions.SendEvent(request["PlayerId"], request["Key"], request["Value"]);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			log.LogError($"   [{nameof(SendEvent)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ A S Y N C ████████████████████████████████████████████

	[FunctionName(nameof(AddAsyncObject))]
	public static async Task<string> AddAsyncObject (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		AddAsyncObjectRequest request = JsonConvert.DeserializeObject<AddAsyncObjectRequest>(requestSerialized);
		Response response = await AsyncFunctions.AddAsyncObject(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			log.LogError($"   [{nameof(AddAsyncObject)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[FunctionName(nameof(GetAsyncObjects))]
	public static async Task<string> GetAsyncObjects (
		[HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		try
		{
			AsyncObjectRequest request = JsonConvert.DeserializeObject<AsyncObjectRequest>(requestSerialized);
			AsyncObjectResponse response = await AsyncFunctions.GetAsyncObjects(request);
			string responseSerialized = JsonConvert.SerializeObject(response);
			if (response.IsError)
				log.LogError($"   [{nameof(GetAsyncObjects)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
			return responseSerialized;
		}
		catch (Exception e)
		{
			return e.Message;
		}
	}

	// ████████████████████████████████████████████ A D M I N ████████████████████████████████████████████

	[FunctionName(nameof(DeleteAllMatchesDebug))]
	public static async Task DeleteAllMatchesDebug (
		[HttpTrigger(AuthorizationLevel.Admin, "get", Route = null)] string any,
		ILogger log)
	{
		Logger.Setup(log);
		log.LogWarning($"   [{nameof(DeleteAllMatchesDebug)}] Deleting all matches.");
		Dictionary<string, string> allMatchesDict = await Global.Service.GetAllData(Global.MATCHES_TABLE, Global.DEFAULT_PARTITION, null);
		foreach (var match in allMatchesDict)
			await MatchFunctions.DeleteEverythingFromMatch(match.Key);
	}

	[FunctionName(nameof(AddDataToAllPlayersDebug))]
	public static async Task AddDataToAllPlayersDebug (
		[HttpTrigger(AuthorizationLevel.Admin, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		log.LogWarning($"   [{nameof(AddDataToAllPlayersDebug)}] Adding data to all players.");
		Dictionary<string, string> request = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestSerialized);
		if (request == null || request.Count == 0)
			log.LogError("Request came out empty.");
		Dictionary<string, string> allPlayersDict = await Global.Service.GetAllData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, null);
		foreach (var player in allPlayersDict)
		{
			PlayerRegistry registry = JsonConvert.DeserializeObject<PlayerRegistry>(player.Value);
			if (registry == null)
				continue;
			PlayerInfo info = registry.Info;
			if (info.CustomData == null)
				info.CustomData = new Dictionary<string, string>();
			foreach (var data in request)
				info.CustomData[data.Key] = data.Value;
			await Global.Service.UpsertData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, player.Key, JsonConvert.SerializeObject(registry));
		}
	}

	[FunctionName(nameof(AddGameData))]
	public static async Task AddGameData (
		[HttpTrigger(AuthorizationLevel.Admin, "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);
		log.LogWarning($"   [{nameof(AddDataToAllPlayersDebug)}] Adding data to game.");
		Dictionary<string, string> dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestSerialized);
		foreach (var kv in dict)
			await Global.Service.UpsertData("Data", "Game", kv.Key, kv.Value);
	}

	// ████████████████████████████████████████████   A U T H   ████████████████████████████████████████████

	[FunctionName(nameof(GetUrlForAuthWithGoogle)), Obsolete]
	public static async Task<string> GetUrlForAuthWithGoogle (
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] string requestSerialized,
		ILogger log)
	{
		Logger.Setup(log);

		Dictionary<string, string> options = new();
		if (!string.IsNullOrEmpty(requestSerialized))
		{
			try
			{
				options = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestSerialized);
			}
			catch
			{
				Logger.Log("    [GetUrlForAuthWithGoogle] Unexpected error when getting options from request.");
			}
		}

		string state;
		string newEntrySerialized = JsonConvert.SerializeObject(new AuthenticationEntry { Provider = "Google" });

		string callbackUrl = Environment.GetEnvironmentVariable("CALLBACK_FUNCTION");
		string clientId = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_ID");
		if (options.ContainsKey("Callback"))
			callbackUrl = options["Callback"];

		string url = "https://accounts.google.com/o/oauth2/v2/auth?" +
		$"client_id={clientId}" +
		$"&redirect_uri={callbackUrl}" +
		"&response_type=code" +
		"&access_type=offline" +
		"&scope=email profile";

		if (options.ContainsKey("State"))
			state = options["State"];
		else
			state = Guid.NewGuid().ToString();
		url += $"&state={state}";

		if (options.ContainsKey("Consent"))
			url += "&prompt=consent";
		if (options.ContainsKey("Hint"))
			url += $"&login_hint={options["Hint"]}";

		await Global.Service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, state, newEntrySerialized);
		return url;
	}

	[FunctionName(nameof(AuthenticationCallback))]
	public static async Task<IActionResult> AuthenticationCallback (
		[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest request,
		ILogger log)
	{
		Logger.Setup(log);
		var query = request.Query;
		bool isSuccess = await LoginFunctions.ReceiveAuthCallback(request.Query.ToDictionary(q => q.Key, q => q.Value[0]));
		if (isSuccess)
			return new RedirectResult(Environment.GetEnvironmentVariable("LOGIN_SUCCESS_REDIRECT_URL"));
		else
			return new NotFoundObjectResult("Error getting state entry.");


		/*
		Logger.LogWarning($"Callback content:");
		foreach (var item in query)
			Logger.LogWarning($"    {item.Key} : {item.Value[0]}");

		var state = query["state"][0];
		string entrySerialized = await Global.Service.GetData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, state, "");
		if (string.IsNullOrEmpty(entrySerialized))
		{
			string msg = $"Error getting state entry.";
			Logger.LogError(msg);
			return new NotFoundObjectResult(msg);
		}
		AuthenticationEntry entry = JsonConvert.DeserializeObject<AuthenticationEntry>(entrySerialized);
		entry.SetStatus(AuthStatus.Processing);
		await Global.Service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, state, JsonConvert.SerializeObject(entry));

		await Global.AuthService.UpdateEntry(query.ToDictionary(q => q.Key, q => q.Value[0]));

		var code = query["code"][0];
		string callbackUrl = Environment.GetEnvironmentVariable("CALLBACK_FUNCTION");
		string clientId = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_ID");
		string clientSecret = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_SECRET");

		var newContent = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{ "client_id" , clientId },
				{ "client_secret", clientSecret },
				{ "grant_type", "authorization_code" },
				{ "redirect_uri", callbackUrl },
				{ "code", code }
			});
		Logger.LogWarning($" -----  Getting Token --------");
		var response = await Global.HttpClient.PostAsync("https://oauth2.googleapis.com/token", newContent);
		string result = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
		{
			string msg = $"Error getting code: {JsonConvert.SerializeObject(response)}\n\n{result}";
			Logger.LogError(msg);
			entry.SetStatus(AuthStatus.Failed);
			await Global.Service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, state, JsonConvert.SerializeObject(entry));
			return new NotFoundObjectResult(msg);
		}
		Dictionary<string, dynamic> dict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(result);
		Logger.LogWarning("Response fields:");
		string accessToken = dict["access_token"] as string;
		string idToken = dict["id_token"] as string;
		string refreshToken = "";
		if (dict.ContainsKey("refresh_token"))
			refreshToken = dict["refresh_token"] as string;
		foreach (var item in dict)
			Logger.LogWarning($"    {item.Key} : {item.Value.ToString()}");

		string encryptedAccessToken = EncryptionHelper.Encrypt(accessToken, Environment.GetEnvironmentVariable("ACCESS_ENCRYPTION_KEY"));
		entry.Data["AccessToken"] = encryptedAccessToken;
		if (!string.IsNullOrEmpty(refreshToken))
			entry.Data["RefreshToken"] = EncryptionHelper.Encrypt(refreshToken, Environment.GetEnvironmentVariable("REFRESH_ENCRYPTION_KEY"));

		var tokenObj = jwtHandler.ReadJsonWebToken(idToken);
		Logger.LogWarning("Token fields:");
		Logger.LogWarning($"    sub : {tokenObj.Subject}");
		entry.UserInfo.UserId = tokenObj.Subject;
		foreach (var field in tokenObj.Claims)
		{
			Logger.LogWarning($"    {field.Type} : {field.Value}"); 
			switch (field.Type)
			{
				case "email":
					entry.UserInfo.Email = field.Value;
					break;
				case "name":
					entry.UserInfo.Name = field.Value;
					break;
				case "picture":
					entry.UserInfo.Picture = field.Value;
					break;
			}
		}

		entry.SetStatus(AuthStatus.Granted);

		await Global.Service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, state, JsonConvert.SerializeObject(entry));

		return new RedirectResult(Environment.GetEnvironmentVariable("LOGIN_SUCCESS_REDIRECT_URL"));
		*/
	}
}

#endif