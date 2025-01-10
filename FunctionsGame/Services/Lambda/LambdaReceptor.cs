#if LAMBDA_FUNCTIONS

using Amazon.Lambda.Core;
using Amazon.Lambda.Annotations;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System.Text;

namespace Kalkatos.Network.Lambda;

public class LambdaReceptor
{
	// ████████████████████████████████████████████ S T A R T U P ████████████████████████████████████████████

	[LambdaFunction]
	public async Task<string> LogIn (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string streamContent = Helper.ReadBytes(stream, Encoding.UTF8);
		dynamic startObj = JsonConvert.DeserializeObject<dynamic>(streamContent);
		string requestSerialized = startObj.body;
		LoginRequest request = JsonConvert.DeserializeObject<LoginRequest>(requestSerialized);
		LoginResponse response = await LoginFunctions.LogIn(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		Logger.Log($"   [{nameof(LogIn)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[LambdaFunction]
	public async Task<string> SetPlayerData (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		SetPlayerDataRequest request = JsonConvert.DeserializeObject<SetPlayerDataRequest>(requestSerialized);
		PlayerInfoResponse response = await LoginFunctions.SetPlayerData(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		Logger.Log($"   [{nameof(SetPlayerData)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[LambdaFunction]
	public async Task<string> GetGameSettings (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		GameDataRequest request = JsonConvert.DeserializeObject<GameDataRequest>(requestSerialized);
		GameDataResponse response = await LoginFunctions.GetGameSettings(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		Logger.Log($"   [{nameof(GetGameSettings)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ M A T C H M A K I N G ████████████████████████████████████████████

	[LambdaFunction]
	public async Task<string> FindMatch (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		FindMatchRequest request = JsonConvert.DeserializeObject<FindMatchRequest>(requestSerialized);
		Response response = await MatchFunctions.FindMatch(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		Logger.Log($"   [{nameof(FindMatch)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[LambdaFunction]
	public async Task<string> GetMatch (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);
		MatchResponse response = await MatchFunctions.GetMatch(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		Logger.Log($"   [{nameof(GetMatch)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[LambdaFunction]
	public async Task<string> LeaveMatch (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		MatchRequest request = JsonConvert.DeserializeObject<MatchRequest>(requestSerialized);
		Response response = await MatchFunctions.LeaveMatch(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		Logger.Log($"   [{nameof(LeaveMatch)}] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ A C T I O N ████████████████████████████████████████████

	[LambdaFunction]
	public async Task<string> SendAction (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		ActionRequest request = JsonConvert.DeserializeObject<ActionRequest>(requestSerialized);
		ActionResponse response = await MatchFunctions.SendAction(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(SendAction)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ S T A T E ████████████████████████████████████████████

	/// <summary>
	/// Gets an array of states starting from the index requested up until the last one available.
	/// </summary>
	/// <returns> A serialized <typeparamref screenName="StateResponse"/> with the array of states or error message. </returns>
	[LambdaFunction]
	public async Task<string> GetMatchState (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		StateRequest request = JsonConvert.DeserializeObject<StateRequest>(requestSerialized);
		StateResponse response = await MatchFunctions.GetMatchState(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(GetMatchState)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ D E L E T E ████████████████████████████████████████████

	[LambdaFunction]
	public async Task CheckMatch (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string message = Helper.ReadBytes(stream, Encoding.UTF8);
		Logger.Log($"   [{nameof(CheckMatch)}] Checking match from queue with message: {message}");
		string[] messageSplit = message.Split('|');
		string matchId = messageSplit[0];
		int lastHash = int.Parse(messageSplit[1]);
		int? newHash = await MatchFunctions.CheckMatch(matchId, lastHash);
		if (newHash.HasValue)
		{
			// TODO Schedule a new check using AWS SQS
		}
	}

	// ████████████████████████████████████████ L E A D E R B O A R D █████████████████████████████████████████

	[LambdaFunction]
	public async Task<string> AddLeaderboardEvent (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		LeaderboardEventRequest request = JsonConvert.DeserializeObject<LeaderboardEventRequest>(requestSerialized);
		Response response = await LeaderboardFunctions.AddLeaderboardEvent(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(AddLeaderboardEvent)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[LambdaFunction]
	public async Task<string> GetLeaderboard (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		LeaderboardRequest request = JsonConvert.DeserializeObject<LeaderboardRequest>(requestSerialized);
		LeaderboardResponse response = await LeaderboardFunctions.GetLeaderboard(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(GetLeaderboard)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████ A N A L Y T I C S █████████████████████████████████████████

	[LambdaFunction]
	public async Task<string> SendEvent (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		Dictionary<string, string> request = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestSerialized);
		Response response = await AnalyticsFunctions.SendEvent(request["PlayerId"], request["Key"], request["Value"]);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(SendEvent)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	// ████████████████████████████████████████████ A S Y N C ████████████████████████████████████████████

	[LambdaFunction]
	public async Task<string> AddAsyncObject (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		AddAsyncObjectRequest request = JsonConvert.DeserializeObject<AddAsyncObjectRequest>(requestSerialized);
		Response response = await AsyncFunctions.AddAsyncObject(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(AddAsyncObject)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}

	[LambdaFunction]
	public async Task<string> GetAsyncObjects (Stream stream, ILambdaContext context)
	{
		Logger.Setup(context.Logger);
		string requestSerialized = Helper.ReadBytes(stream, Encoding.UTF8);
		AsyncObjectRequest request = JsonConvert.DeserializeObject<AsyncObjectRequest>(requestSerialized);
		AsyncObjectResponse response = await AsyncFunctions.GetAsyncObjects(request);
		string responseSerialized = JsonConvert.SerializeObject(response);
		if (response.IsError)
			Logger.LogError($"   [{nameof(GetAsyncObjects)} :: Error] \r\n>> Request :: {requestSerialized} \r\n>> Response :: {responseSerialized}");
		return responseSerialized;
	}
}

#endif
