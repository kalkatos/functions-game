using Microsoft.IdentityModel.JsonWebTokens;
using System;
using System.Net.Http;

namespace Kalkatos.Network;

public static class Global
{
	private static Random random = new Random();
	private static HttpClient httpClient;
	private static JsonWebTokenHandler jwtHandler;

	private static IService service;
	private static IAuthService authService;
	private static IGame game;
	private static IAsyncGame asyncGame;

	internal const string ACTIONS_TABLE = "Actions";
	internal const string STATES_TABLE = "States";
	internal const string MATCHES_TABLE = "Matches";
	internal const string DATA_TABLE = "Data";
	internal const string PLAYERS_TABLE = "Players";
	internal const string MATCHMAKING_TABLE = "Matchmaking";
	internal const string LEADERBOARD_TABLE = "Leaderboard";
	internal const string ASYNC_TABLE = "Async";
	internal const string ANALYTICS_TABLE = "Analytics";
	internal const string USER_DATA_TABLE = "UserData";
	internal const string AUTH_TABLE = "Auth";

	internal const string DEFAULT_PARTITION = "Default";
	internal const string GAME_PARTITION = "Game";
	internal const string LEADERBOARD_PARTITION = "Leaderboard";

	internal const string RETREATED_KEY = "Retreated";
	internal const string START_MATCH_KEY = "StartMatch";
	internal const string NICKNAME_KEY = "Nickname";
	internal const string LAST_LEADERBOARD_EVENT_KEY = "LastLeaderboardEvent";
	internal const string LEADERBOARD_INDEXES_KEY = "LeaderboardIndexes";
	internal const string LAST_LEADERBOARD_UPDATE_KEY = "LastLeaderboardUpdate";

	internal const int AUTH_TIMEOUT = 300;

	public static IService Service
	{
		get
		{
			if (service == null)
				service = (IService)GetInstanceOfObject(Environment.GetEnvironmentVariable("IService"), new SampleService());
			return service;
		}
	}

	public static IGame Game
	{
		get
		{
			if (game == null)
				game = (IGame)GetInstanceOfObject(Environment.GetEnvironmentVariable("IGame"), new Rps.RpsGame());
			return game;
		}
	}

	public static IAsyncGame AsyncGame
	{
		get
		{
			if (asyncGame == null)
				asyncGame = (IAsyncGame)GetInstanceOfObject(Environment.GetEnvironmentVariable("IAsyncGame"), new SampleAsyncGame());
			return asyncGame;
		}
	}

	public static IAuthService AuthService
	{
		get
		{
			if (authService == null)
				authService = (IAuthService)GetInstanceOfObject(Environment.GetEnvironmentVariable("IAuthService"), new SampleAuthService());
			return authService;
		}
	}

	public static Random Random => random;

	public static HttpClient HttpClient
	{
		get
		{
			if (httpClient == null)
				httpClient = new HttpClient();
			return httpClient;
		}
	}

	public static JsonWebTokenHandler JwtHandler
	{
		get
		{
			if (jwtHandler == null)
				jwtHandler = new JsonWebTokenHandler();
			return jwtHandler;
		}
	}

	private static object GetInstanceOfObject (string typeFullName, object fallbackObj)
	{
		try
		{
			string typeName = typeFullName;
			Type type = Type.GetType(typeName);
			return Activator.CreateInstance(type); 
		}
		catch (Exception)
		{
			return fallbackObj;
		}
	}
}
