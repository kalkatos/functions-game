using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame
{
	public class MatchFunctions
	{
#if AZURE_FUNCTIONS
		private static IService service = new AzureFunctions.AzureFunctionsService();
#else
		private static IService service;
#endif

		public static async Task<LoginResponse> LogIn (LoginRequest request)
		{
			if (string.IsNullOrEmpty(request.Identifier))
				return new LoginResponse { IsError = true, Message = "Identifier is null. Must be an unique user identifier." };

			string playerId;
			PlayerRegistry playerRegistry;
			bool isRegistered = await service.IsRegisteredDevice(request.Identifier);
			if (isRegistered)
			{
				playerId = await service.GetPlayerId(request.Identifier);
				playerRegistry = await service.GetPlayerRegistry(playerId);
			}
			else
			{
				playerId = Guid.NewGuid().ToString();
				await service.RegisterDeviceWithId(request.Identifier, playerId);
				string newPlayerAlias = Guid.NewGuid().ToString();
				PlayerInfo newPlayerInfo = new PlayerInfo { Alias = newPlayerAlias, Nickname = request.Nickname };
				playerRegistry = new PlayerRegistry
				{
					PlayerId = playerId,
					Info = newPlayerInfo,
					Devices = new string[] { request.Identifier },
					Region = request.Region,
					LastAccess = DateTime.UtcNow,
					FirstAccess = DateTime.UtcNow
				};
				await service.SetPlayerRegistry(playerRegistry);
			}

			return new LoginResponse
			{
				IsAuthenticated = playerRegistry.IsAuthenticated,
				PlayerId = playerRegistry.PlayerId,
				PlayerAlias = playerRegistry.Info.Alias,
				SavedNickname = playerRegistry.Info.Nickname,
			};
		}

		public static async Task<StateResponse> GetMatchState (StateRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new StateResponse { IsError = true, Message = "Match id and player id may not be null." };
			if (request.LastIndex < 0)
				return new StateResponse { IsError = true, Message = "LastIndex must be higher or equal to zero." };
			if (request.LastIndex == 0)
				await WaitForAction(new WaitActionRequest { MatchId = request.MatchId, Action = "Handshaking" });
			StateInfo[] stateHistory = await service.GetStateHistory(request.PlayerId, request.MatchId);
			if (stateHistory == null || request.LastIndex >= stateHistory.Length)
				return new StateResponse { IsError = true, Message = "State is not available yet." };
			int amountRequested = stateHistory.Length - request.LastIndex;
			StateInfo[] requestedStates = new StateInfo[amountRequested];
			for (int requestedIndex = 0, historyIndex = stateHistory.Length - 1; 
				historyIndex >= 0; 
				requestedIndex++, historyIndex--)
			{
				requestedStates[requestedIndex] = stateHistory[historyIndex];
			}
			return new StateResponse { StateInfos = requestedStates };
		}

		public static async Task WaitForAction (WaitActionRequest request)
		{
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			// TODO Get each player or the listed players in request
			// TODO For each player, if they are in the request, check if they have done the action
			await Task.Delay(1);
		}
	}

	public class WaitActionRequest
	{
		public string MatchId;
		public string ExpectedPlayers;
		public string Action;
		public object ExpectedParameter;
	}

	public interface IService
	{
		// Log in
		Task<bool> IsRegisteredDevice (string deviceId);
		Task<string> GetPlayerId (string deviceId);
		Task RegisterDeviceWithId (string deviceId, string playerId);
		Task<PlayerRegistry> GetPlayerRegistry (string playerId);
		Task SetPlayerRegistry (PlayerRegistry registry);
		// Match
		Task<MatchRegistry> GetMatchRegistry (string matchId);
		// Action
		Task<ActionInfo> GetPlayerAction (ActionRequest request);
		// Get Match States
		Task<StateInfo[]> GetStateHistory (string playerId, string matchId);
	}
}
