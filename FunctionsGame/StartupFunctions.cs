using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;
using Kalkatos.Network.Model;
using Kalkatos.FunctionsGame.Registry;

namespace Kalkatos.FunctionsGame
{
	public static class StartupFunctions
	{
		// TODO Change to return LoginResponse serialized
		[FunctionName(nameof(LogIn))]
		public static async Task<string> LogIn (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(LogIn)}] Request = {requestSerialized}");
			Logger.Setup(log);

			LoginRequest request = JsonConvert.DeserializeObject<LoginRequest>(requestSerialized);

			LoginResponse response = await MatchFunctions.LogIn(request);

			return JsonConvert.SerializeObject(response);
		}




		[FunctionName(nameof(SetPlayerData))]
		public static async Task<string> SetPlayerData (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string requestSerialized,
			ILogger log)
		{
			log.LogInformation($"   [{nameof(SetPlayerData)}] Request = {requestSerialized}");

			SetPlayerDataRequest request = JsonConvert.DeserializeObject<SetPlayerDataRequest>(requestSerialized);

			if (request.Data == null || request.Data.Count() == 0)
				return JsonConvert.SerializeObject(new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Request Data is null or empty." });

			// Get file
			BlockBlobClient playersBlob = new BlockBlobClient("UseDevelopmentStorage=true", "players", $"{request.PlayerId}.json");
			if (!await playersBlob.ExistsAsync())
				return JsonConvert.SerializeObject(new NetworkError { Tag = NetworkErrorTag.NotFound, Message = "Player not found." });

			// Read file
			using Stream readStream = await playersBlob.OpenReadAsync();
			string registrySerialized = Helper.ReadBytes(readStream);
			PlayerRegistry registry = JsonConvert.DeserializeObject<PlayerRegistry>(registrySerialized);

			// Change nickname if it is one of the changes
			if (request.Data.ContainsKey("Nickname"))
			{
				registry.Info.Nickname = request.Data["Nickname"];
				request.Data.Remove("Nickname");
			}

			foreach (var item in request.Data)
			{
				if (registry.Info.CustomData.ContainsKey(item.Key))
					registry.Info.CustomData[item.Key] = item.Value;
				else
					registry.Info.CustomData.Add(item.Key, item.Value);
			}

			// Write back to file
			using Stream writeStream = await playersBlob.OpenWriteAsync(true);
			writeStream.Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(registry)));

			return $"{{\"{nameof(Response.IsError)}\":false,\"{nameof(Response.Message)}\":\"Ok.\"}}";
		}
	}
}