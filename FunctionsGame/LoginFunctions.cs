using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Kalkatos.FunctionsGame
{
    public static class LoginFunctions
    {
        private static ILoginService service = GlobalConfigurations.LoginService;
        private static IGame game = GlobalConfigurations.Game;

        public static async Task<LoginResponse> LogIn (LoginRequest request)
        {
            if (string.IsNullOrEmpty(request.Identifier) || string.IsNullOrEmpty(request.GameId))
                return new LoginResponse { IsError = true, Message = "Wrong parameters. Identifier and GameId must not be null." };
            Logger.LogWarning("[LogIn] Getting player");
            PlayerRegistry playerRegistry;
            string playerId = await service.GetPlayerId(request.Identifier);
            Logger.LogWarning($"[LogIn] Player id: {playerId}");
            Logger.LogWarning("[LogIn] Getting game registry");
            GameRegistry gameRegistry = await service.GetGameConfig(request.GameId);
            Logger.LogWarning($"[LogIn] Game registry: {JsonConvert.SerializeObject(gameRegistry)}");
            Logger.LogWarning($"[LogIn] Setting game settings. Game: {game}");
            Logger.LogWarning($"[LogIn] Game serialized: {JsonConvert.SerializeObject(game)}");
            game?.SetSettings(gameRegistry);
            if (string.IsNullOrEmpty(playerId))
            {
                Logger.LogWarning("[LogIn] New player, registering device");
                playerId = Guid.NewGuid().ToString();
                await service.RegisterDeviceWithId(request.Identifier, playerId);
                string newPlayerAlias = Guid.NewGuid().ToString();
                Logger.LogWarning("[LogIn] Creating new player info");
                PlayerInfo newPlayerInfo = new PlayerInfo { Alias = newPlayerAlias, Nickname = Helper.GetRandomNickname_GuestPlus6Letters(), CustomData = gameRegistry?.DefaultPlayerCustomData };
                playerRegistry = new PlayerRegistry
                {
                    PlayerId = playerId,
                    Info = newPlayerInfo,
                    Devices = new string[] { request.Identifier },
                    Region = request.Region,
                    LastAccess = DateTime.UtcNow,
                    FirstAccess = DateTime.UtcNow
                };
                Logger.LogWarning("[LogIn] Registering player");
                await service.SetPlayerRegistry(playerRegistry);
            }
            else
            {
                playerRegistry = await service.GetPlayerRegistry(playerId);
                if (!playerRegistry.Devices.Contains(request.Identifier))
                    playerRegistry.Devices = playerRegistry.Devices.Append(request.Identifier).ToArray();
                playerRegistry.LastAccess = DateTime.UtcNow;
                await service.SetPlayerRegistry(playerRegistry);
            }
            return new LoginResponse
            {
                IsAuthenticated = playerRegistry.IsAuthenticated,
                PlayerId = playerRegistry.PlayerId,
                MyInfo = playerRegistry.Info,
            };
        }

        public static async Task<PlayerInfoResponse> SetPlayerData (SetPlayerDataRequest request)
        {
            if (request.Data == null || request.Data.Count() == 0)
                return new PlayerInfoResponse { IsError = true, Message = "Request Data is null or empty." };
            if (string.IsNullOrEmpty(request.PlayerId))
                return new PlayerInfoResponse { IsError = true, Message = "Player ID is null or empty." };
            PlayerRegistry playerRegistry = await service.GetPlayerRegistry(request.PlayerId);
            if (playerRegistry == null)
                return new PlayerInfoResponse { IsError = true, Message = "Player not found." };
            bool hasChangedNickname = request.Data.ContainsKey("Nickname");
            if (hasChangedNickname)
            {
                playerRegistry.Info.Nickname = request.Data["Nickname"]; //Helper.GetRandomNickname_AdjectiveNoun();
                request.Data.Remove("Nickname");
            }
            if (playerRegistry.Info.CustomData == null)
                playerRegistry.Info.CustomData = new Dictionary<string, string>();
            foreach (var item in request.Data)
                if (playerRegistry.Info.CustomData.ContainsKey(item.Key))
                    playerRegistry.Info.CustomData[item.Key] = item.Value;
            await service.SetPlayerRegistry(playerRegistry);
            return new PlayerInfoResponse { PlayerInfo = playerRegistry.Info.Clone(), Message = "Data changed successfully!" };
        }

        public static async Task<GameDataResponse> GetGameSettings (GameDataRequest request)
        {
            if (string.IsNullOrEmpty(request.GameId) || string.IsNullOrEmpty(request.PlayerId))
                return new GameDataResponse { IsError = true, Message = "Wrong parameters." };
            PlayerRegistry playerRegistry = await service.GetPlayerRegistry(request.PlayerId);
            if (playerRegistry == null)
                return new GameDataResponse { IsError = true, Message = "Player not registered." };
            GameRegistry gameRegistry = await service.GetGameConfig(request.GameId);
            return new GameDataResponse { Settings = gameRegistry.Settings };
        }
    }
}
