using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Kalkatos.FunctionsGame.Models;
using Kalkatos.Network.Model;
using Azure.Storage.Blobs.Specialized;
using FunctionsGame.NetworkModel;
using Newtonsoft.Json;

namespace Kalkatos.FunctionsGame
{
	public static class StartupFunctions
	{
		[FunctionName(nameof(LogIn))]
		public static async Task<IActionResult> LogIn (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string connectInfoSerialized,
			ILogger log)
		{
			log.LogInformation("Executing login.");

			PlayerConnectInfo playerConnectInfo = JsonConvert.DeserializeObject<PlayerConnectInfo>(connectInfoSerialized);

			if (Helper.VerifyNullParameter(playerConnectInfo.Identifier, log))
				return new BadRequestObjectResult(new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Identifier is null. Must be an unique user identifier." });

			// Access players blob
			BlockBlobClient identifierFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{playerConnectInfo.Identifier}");
			PlayerRegistry playerRegistry;

			if (!await identifierFile.ExistsAsync())
			{
				// New user. Save identifier pointing to player id
				string newPlayerId = Guid.NewGuid().ToString();
				using Stream stream = await identifierFile.OpenWriteAsync(true);
				stream.Write(Encoding.ASCII.GetBytes(newPlayerId), 0, newPlayerId.Length);

				// Save player id pointing to player registry with identifier as one device
				BlockBlobClient playerFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{newPlayerId}.json");
				playerRegistry = new PlayerRegistry 
				{ 
					PlayerId = newPlayerId, 
					Devices = new string[] { playerConnectInfo.Identifier }, 
					Region = playerConnectInfo.Region,
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
				if (!playerRegistry.Devices.Contains(playerConnectInfo.Identifier))
					playerRegistry.Devices = playerRegistry.Devices.Append(playerConnectInfo.Identifier).ToArray();
				playerRegistry.LastAccess = DateTime.UtcNow;
				playerRegistry.Region = playerConnectInfo.Region;
				registrySerialized = JsonConvert.SerializeObject(playerRegistry);
				using Stream stream3 = await playerFile.OpenWriteAsync(true);
				stream3.Write(Encoding.ASCII.GetBytes(registrySerialized), 0, registrySerialized.Length);
				log.LogInformation("Player registry saved === " + registrySerialized);
			}

			LoginResponse response = new LoginResponse { IsAuthenticated = playerRegistry.IsAuthenticated, PlayerId = playerRegistry.PlayerId };

			return new OkObjectResult(response);
		}

	}
}