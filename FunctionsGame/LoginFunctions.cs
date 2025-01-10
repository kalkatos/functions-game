using Kalkatos.Network.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Kalkatos.Network;

public static class LoginFunctions
{
	private static IService service = Global.Service;
	private static IAuthService authService = Global.AuthService;

	public static async Task<LoginResponse> LogIn (LoginRequest request)
	{
		if (string.IsNullOrEmpty(request.Identifier)
			|| string.IsNullOrEmpty(request.DeviceId)
			|| string.IsNullOrEmpty(request.GameId))
			return new LoginResponse { IsError = true, Message = "Wrong parameters. Identifier, DeviceId and GameId cannot be null." };
		Logger.Log("[LogIn] Getting player");
		PlayerRegistry playerRegistry;
		LoginResponse response;
		string playerId = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.Identifier, "");
		if (string.IsNullOrEmpty(playerId) && request.Identifier != request.DeviceId)
			playerId = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.DeviceId, "");
		if (string.IsNullOrEmpty(playerId))
		{
			Logger.Log("[LogIn] New player, registering device");
			playerId = Guid.NewGuid().ToString();
			await service.UpsertData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.Identifier, playerId);
			Logger.Log("[LogIn] Getting game data");
			string gameRegistrySerialized = await service.GetData(Global.DATA_TABLE, Global.GAME_PARTITION, request.GameId, "");
			GameRegistry gameRegistry = JsonConvert.DeserializeObject<GameRegistry>(gameRegistrySerialized);
			string newPlayerAlias = Guid.NewGuid().ToString();
			Logger.Log("[LogIn] Creating new player info");
			PlayerInfo newPlayerInfo = new PlayerInfo { Alias = newPlayerAlias, Nickname = Helper.GetRandomNickname_GuestPlus6Letters(), CustomData = gameRegistry?.DefaultPlayerCustomData };
			playerRegistry = new PlayerRegistry
			{
				PlayerId = playerId,
				Info = newPlayerInfo,
				Devices = [request.DeviceId],
				Region = request.Region,
				LastAccess = DateTimeOffset.UtcNow,
				FirstAccess = DateTimeOffset.UtcNow
			};
			response = GetAnonymousResponse();
		}
		else
		{
			string playerSerialized = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, playerId, "");
			if (string.IsNullOrEmpty(playerSerialized))
				return new LoginResponse { IsError = true, Message = "Error finding player." };
			playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerSerialized);
			if (!playerRegistry.Devices.Contains(request.DeviceId))
				playerRegistry.Devices = playerRegistry.Devices.Append(request.DeviceId).ToArray();
			if (!playerRegistry.IsUsingAuthentication)
			{
				if (request.MustAuthenticate)
					response = await GetUrlLoginResponse(AuthType.First);
				else
					response = GetAnonymousResponse();
			}
			else
			{
				string entrySerialized;
				if (playerRegistry.UserInfo != null && !string.IsNullOrEmpty(playerRegistry.UserInfo.UserId))
					entrySerialized = await service.GetData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, playerRegistry.UserInfo.UserId, "");
				else
				{
					string userId = await service.GetData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, playerId, "");
					if (string.IsNullOrEmpty(userId))
						return new LoginResponse { IsError = true, Message = "Error finding player auth entry." };
					entrySerialized = await service.GetData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, userId, "");
				}

				if (string.IsNullOrEmpty(entrySerialized))
					return new LoginResponse { IsError = true, Message = "Error finding authentication entry." };
				AuthenticationEntry entry = JsonConvert.DeserializeObject<AuthenticationEntry>(entrySerialized);

				switch (entry.Status)
				{
					case AuthStatus.WaitingAuthentication:
						if ((DateTimeOffset.UtcNow - entry.CreationDate).TotalSeconds < Global.AUTH_TIMEOUT)
							return new LoginResponse { IsError = true, Message = "Authentication process has not finished yet." };
						else
							response = await GetUrlLoginResponse(AuthType.First);
						break;
					case AuthStatus.Granted:
					case AuthStatus.Concluded:
						response = await CreateAuthResponse(entry);
						break;
					default:
						response = await GetUrlLoginResponse(AuthType.First);
						break;
				}
			}
		}

		// TODO Implement the use of session tokens

		Logger.Log("[LogIn] Updating player");
		playerRegistry.LastAccess = DateTimeOffset.UtcNow;
		await service.UpsertData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, playerId, JsonConvert.SerializeObject(playerRegistry));

		return response;

		LoginResponse GetAnonymousResponse ()
		{
			return new LoginResponse
			{
				IsAuthenticated = false,
				PlayerId = playerRegistry.PlayerId,
				MyInfo = playerRegistry.Info,
			};
		}

		async Task<LoginResponse> GetUrlLoginResponse (AuthType authType, AuthenticationEntry entry = null)
		{
			playerRegistry.IsUsingAuthentication = true;
			string ticket = Guid.NewGuid().ToString();
			string url;
			if (authType == AuthType.First)
				url = authService.GetAuthUrl(new AuthOptions { AuthId = ticket, AuthType = AuthType.First });
			else
			{
				var options = new AuthOptions
				{
					AuthType = AuthType.Returning,
					ReturningId = entry.UserInfo.Email,
					AuthId = ticket
				};
				url = authService.GetAuthUrl(options);
			}
			if (entry == null || entry.UserInfo == null || string.IsNullOrEmpty(entry.UserInfo.UserId))
			{
				entry = new AuthenticationEntry
				{
					PlayerId = playerId,
					Provider = authService.Name,
					AuthTicket = ticket,
					Status = AuthStatus.WaitingAuthentication,
					StatusDescription = AuthStatus.WaitingAuthentication.ToString()
				};
				await service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, ticket, playerId);
				await service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, playerId, AuthStatus.WaitingAuthentication.ToString());
			}
			else
			{
				entry.SetStatus(AuthStatus.WaitingAuthentication);
				entry.AuthTicket = ticket;
				await service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, entry.UserInfo.UserId, JsonConvert.SerializeObject(entry));
			}
			return new UrlLoginResponse
			{
				IsAuthenticated = true,
				AuthUrl = url,
				Message = "The URL provided must be used to authenticate the user."
			};
		}

		async Task<LoginResponse> CreateAuthResponse (AuthenticationEntry entry)
		{
			if (entry.Status == AuthStatus.Granted)
			{
				playerRegistry.UserInfo = entry.UserInfo;
				entry.SetStatus(AuthStatus.Concluded);
			}
			bool isValidEntry = await authService.IsValid(entry);
			if (!isValidEntry)
			{
				Logger.LogWarning("Auth tokens have expired, need new auth from user.");
				return await GetUrlLoginResponse(AuthType.Returning, entry);
			}
			await service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, entry.UserInfo.UserId, JsonConvert.SerializeObject(entry));
			return new AuthLoginResponse
			{
				IsAuthenticated = true,
				PlayerId = playerRegistry.PlayerId,
				MyInfo = playerRegistry.Info,
				UserInfo = new UserInfo(entry.UserInfo)
			};
		}
	}

	private static async Task DeleteAllDataRelatedToPlayer (string anonymousPlayerId, string existingPlayerId)
	{
		var playerSerialized = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, anonymousPlayerId, "");
		var player = JsonConvert.DeserializeObject<PlayerRegistry>(playerSerialized);
		foreach (var device in player.Devices)
			await service.UpsertData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, device, existingPlayerId);
		await service.DeleteData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, anonymousPlayerId);
		var allData = await service.GetAllData(Global.USER_DATA_TABLE, anonymousPlayerId, null);
		foreach (var data in allData)
			await service.DeleteData(Global.USER_DATA_TABLE, anonymousPlayerId, data.Key);
		await service.DeleteData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, anonymousPlayerId);
	}

	public static async Task<bool> ReceiveAuthCallback (Dictionary<string, string> data)
	{
		bool result = true;
		Logger.LogWarning($"Callback content:");
		foreach (var item in data)
			Logger.LogWarning($"    {item.Key} : {item.Value}");

		AuthenticationEntry entry = await authService.CreateEntryWithCallbackData(data);
		if (entry == null)
		{
			Logger.LogError("Error creating entry with auth service.");
			return false;
		}
		if (entry.Status == AuthStatus.Failed)
		{
			Logger.LogError("Authentication failed.");
			return false;
		}
		string playerId = await service.GetData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, entry.AuthTicket, "");
		if (string.IsNullOrEmpty(playerId))
		{
			entry.SetStatus(AuthStatus.Failed);
			Logger.LogError("Coudn't player id from ticket");
			return false;
		}
		if (string.IsNullOrEmpty(entry.PlayerId))
			entry.PlayerId = playerId;
		entry.SetStatus(AuthStatus.Granted);

		// Check existing player
		string existingEntry = await service.GetData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, entry.UserInfo.UserId, "");
		if (!string.IsNullOrEmpty(existingEntry))
		{
			var savedEntry = JsonConvert.DeserializeObject<AuthenticationEntry>(existingEntry);
			await DeleteAllDataRelatedToPlayer(playerId, savedEntry.PlayerId);
			playerId = savedEntry.PlayerId;
			entry.PlayerId = savedEntry.PlayerId;
		}

		await service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, entry.UserInfo.UserId, JsonConvert.SerializeObject(entry));
		await service.UpsertData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, playerId, entry.UserInfo.UserId);
		await service.DeleteData(Global.AUTH_TABLE, Global.DEFAULT_PARTITION, entry.AuthTicket);
		return result;
	}

	public static async Task<PlayerInfoResponse> SetPlayerData (SetPlayerDataRequest request)
	{
		if (request.Data == null || request.Data.Count() == 0)
			return new PlayerInfoResponse { IsError = true, Message = "Request Data is null or empty." };
		if (string.IsNullOrEmpty(request.PlayerId))
			return new PlayerInfoResponse { IsError = true, Message = "Player ID is null or empty." };
		string playerRegistrySerialized = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (string.IsNullOrEmpty(playerRegistrySerialized))
			return new PlayerInfoResponse { IsError = true, Message = "Player not found." };
		PlayerRegistry playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(playerRegistrySerialized);
		bool hasChangedNickname = request.Data.ContainsKey(Global.NICKNAME_KEY);
		if (hasChangedNickname)
		{
			playerRegistry.Info.Nickname = request.Data[Global.NICKNAME_KEY]; //Helper.GetRandomNickname_AdjectiveNoun();
			request.Data.Remove(Global.NICKNAME_KEY);
		}
		if (playerRegistry.Info.CustomData == null)
			playerRegistry.Info.CustomData = new Dictionary<string, string>();
		foreach (var item in request.Data)
			if (playerRegistry.Info.CustomData.ContainsKey(item.Key))
				playerRegistry.Info.CustomData[item.Key] = item.Value;
		await service.UpsertData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, JsonConvert.SerializeObject(playerRegistry));
		return new PlayerInfoResponse { PlayerInfo = playerRegistry.Info.Clone(), Message = "Data changed successfully!" };
	}

	public static async Task<GameDataResponse> GetGameSettings (GameDataRequest request)
	{
		if (string.IsNullOrEmpty(request.GameId) || string.IsNullOrEmpty(request.PlayerId))
			return new GameDataResponse { IsError = true, Message = "Wrong parameters." };
		string playerRegistrySerialized = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (string.IsNullOrEmpty(playerRegistrySerialized))
			return new GameDataResponse { IsError = true, Message = "Player not registered." };
		string gameRegistrySerialized = await service.GetData(Global.DATA_TABLE, Global.GAME_PARTITION, request.GameId, "");
		if (string.IsNullOrEmpty(gameRegistrySerialized))
			return new GameDataResponse { IsError = true, Message = "Game has no data registered." };
		GameRegistry gameRegistry = JsonConvert.DeserializeObject<GameRegistry>(gameRegistrySerialized);
		return new GameDataResponse { Settings = gameRegistry.GetFullSettings() };
	}

	public static async Task<DataResponse> GetData (DataRequest request)
	{
		if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Key))
			return new DataResponse { IsError = true, Message = "Wrong parameters." };
		string playerRegistrySerialized = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (string.IsNullOrEmpty(playerRegistrySerialized))
			return new DataResponse { IsError = true, Message = "Player not found." };
		string value = await Global.Service.GetData(Global.USER_DATA_TABLE, request.PlayerId, request.Key, request.DefaultValue);
		DataResponse response = new DataResponse { Data = value };
		return response;
	}

	public static async Task<Response> SetData (DataRequest request)
	{
		if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Key))
			return new Response { IsError = true, Message = "Wrong parameters." };
		string playerRegistrySerialized = await service.GetData(Global.PLAYERS_TABLE, Global.DEFAULT_PARTITION, request.PlayerId, "");
		if (string.IsNullOrEmpty(playerRegistrySerialized))
			return new Response { IsError = true, Message = "Player not found." };
		await Global.Service.UpsertData(Global.USER_DATA_TABLE, request.PlayerId, request.Key, request.Value);
		return new Response { Message = "OK" };
	}
}
