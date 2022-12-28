using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Kalkatos.FunctionsGame
{
	public static class MatchmakingFunctions
	{
		[FunctionName(nameof(FindMatch))]
		public static async Task<IActionResult> FindMatch (
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string identifier,
			ILogger log)
		{
			await Task.Delay(100);
			return new OkObjectResult("Ok");
		}
	}
}