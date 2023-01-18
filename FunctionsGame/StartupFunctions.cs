using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;
using Kalkatos.FunctionsGame.Models;
using Kalkatos.Network.Model;
using Kalkatos.FunctionsGame.Registry;

namespace Kalkatos.FunctionsGame
{
    public static class StartupFunctions
	{
		[FunctionName(nameof(LogIn))]
		public static async Task<IActionResult> LogIn (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] LoginRequest loginRequest,
			ILogger log)
		{
			log.LogInformation("Executing login.");

			if (Helper.VerifyNullParameter(loginRequest.Identifier, log))
				return new BadRequestObjectResult(new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Identifier is null. Must be an unique user identifier." });

			// Access players data
			BlockBlobClient identifierFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{loginRequest.Identifier}");
			PlayerRegistry playerRegistry;

			// If the file exists
			if (!await identifierFile.ExistsAsync())
			{
				// New user. Save identifier pointing to player id
				string newPlayerId = Guid.NewGuid().ToString();
				string newPlayerAlias = Guid.NewGuid().ToString();
				using Stream stream = await identifierFile.OpenWriteAsync(true);
				stream.Write(Encoding.ASCII.GetBytes(newPlayerId), 0, newPlayerId.Length);

				// Save player id pointing to player registry with identifier as one device
				BlockBlobClient playerFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{newPlayerId}.json");
				playerRegistry = new PlayerRegistry 
				{
					PlayerId = newPlayerId,
					PlayerAlias = newPlayerAlias,
					Nickname = loginRequest.Nickname,
					Devices = new string[] { loginRequest.Identifier }, 
					Region = loginRequest.Region,
					LastAccess = DateTime.UtcNow,
					FirstAccess = DateTime.UtcNow
				};
				string registrySerialized = JsonConvert.SerializeObject(playerRegistry);
				log.LogInformation("Player registry CREATED === " + registrySerialized);
				using Stream stream2 = await playerFile.OpenWriteAsync(true);
				stream2.Write(Encoding.ASCII.GetBytes(registrySerialized), 0, registrySerialized.Length);
			}
			else
			{
				log.LogInformation("Returning user! ===> ");
				// Existing user
				using Stream stream = await identifierFile.OpenReadAsync();
				string playerId = Helper.ReadBytes(stream);
				// Update last access time
				BlockBlobClient playerFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerId}.json");
				using Stream stream2 = await playerFile.OpenReadAsync();
				string registrySerialized = Helper.ReadBytes(stream2);
				playerRegistry = JsonConvert.DeserializeObject<PlayerRegistry>(registrySerialized);
				if (!playerRegistry.Devices.Contains(loginRequest.Identifier))
					playerRegistry.Devices = playerRegistry.Devices.Append(loginRequest.Identifier).ToArray();
				playerRegistry.LastAccess = DateTime.UtcNow;
				playerRegistry.Region = loginRequest.Region;
				registrySerialized = JsonConvert.SerializeObject(playerRegistry);
				using Stream stream3 = await playerFile.OpenWriteAsync(true);
				stream3.Write(Encoding.ASCII.GetBytes(registrySerialized), 0, registrySerialized.Length);
				log.LogInformation("Player registry saved === " + registrySerialized);
			}

			LoginResponse response = new LoginResponse 
			{
				IsAuthenticated = playerRegistry.IsAuthenticated, 
				PlayerId = playerRegistry.PlayerId,
				PlayerAlias = playerRegistry.PlayerAlias,
				SavedNickname = playerRegistry.Nickname,
			};

			return new OkObjectResult(response);
		}

		[FunctionName(nameof(LoadGameData))]
		public static async Task<IActionResult> LoadGameData (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] GameDataRequest gameDataRequest,
			ILogger log)
		{

			await Task.Delay(100);
			return new OkObjectResult("Ok");
		}

		[FunctionName(nameof(SetNickname))]
		public static async Task<IActionResult> SetNickname (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] SetNicknameRequest request,
			ILogger log)
		{
			// Get file
			BlockBlobClient playersBlob = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{request.PlayerId}.json");

			// Check if exists
			if (!await playersBlob.ExistsAsync())
				return new NotFoundObjectResult("Player not found.");

			// Read and Write new nickname
			using Stream readStream = await playersBlob.OpenReadAsync();
			string registrySerialized = Helper.ReadBytes(readStream);
			PlayerRegistry registry = JsonConvert.DeserializeObject<PlayerRegistry>(registrySerialized);
			registry.Nickname = request.Nickname;
			using Stream writeStream = await playersBlob.OpenWriteAsync(true);
			writeStream.Write(Encoding.ASCII.GetBytes(registrySerialized));

			return new OkObjectResult("Ok");
		}
	}
}