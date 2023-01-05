using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Kalkatos.FunctionsGame.Models;
using Kalkatos.Network.Model;
using System.IO;
using Azure.Storage.Blobs.Specialized;
using FunctionsGame.NetworkModel;
using Newtonsoft.Json;
using System.Text;

namespace Kalkatos.FunctionsGame
{
	public static class StartupFunctions
	{
		[FunctionName(nameof(LogIn))]
		public static async Task<IActionResult> LogIn (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string identifier,
			ILogger log)
		{
			log.LogInformation("Executing login.");

			if (Helper.VerifyNullParameter(identifier, log))
				return new BadRequestObjectResult(new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Identifier is null. Must be an unique user identifier." });

			// Access players blob
			BlockBlobClient identifierFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{identifier}");
			PlayerRegistry playerRegistry;

			if (!await identifierFile.ExistsAsync())
			{
				// New user. Save identifier pointing to player id
				string newPlayerId = Guid.NewGuid().ToString();
				using Stream stream = await identifierFile.OpenWriteAsync(true);
				stream.Write(Encoding.ASCII.GetBytes(newPlayerId), 0, newPlayerId.Length);

				// Save player id pointing to player registry with identifier as one device
				BlockBlobClient playerFile = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{newPlayerId}.json");
				playerRegistry = new PlayerRegistry { PlayerId = newPlayerId, Devices = new string[] { identifier }, LastAccess = DateTime.UtcNow, 
					FirstAccess = DateTime.UtcNow };
				string registrySerialized = JsonConvert.SerializeObject(playerRegistry);
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
				playerRegistry.LastAccess = DateTime.UtcNow;
				registrySerialized = JsonConvert.SerializeObject(playerRegistry);
				using Stream stream3 = await playerFile.OpenWriteAsync(true);
				stream3.Write(Encoding.ASCII.GetBytes(registrySerialized), 0, registrySerialized.Length);
			}

			LoginResponse response = new LoginResponse { IsAuthenticated = playerRegistry.IsAuthenticated, PlayerId = playerRegistry.PlayerId };

			return new OkObjectResult(response);
		}

	}
}