using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Kalkatos.FunctionsGame.Models;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame
{
    public static class StartupFunctions
    {
        [FunctionName(nameof(LogIn))]
        public static async Task<IActionResult> LogIn (
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] string identifier,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            bool isNullId = string.IsNullOrEmpty(identifier);
			if (isNullId)
                identifier = "<empty>";
			log.LogInformation($"Request with identifier: {identifier}");
            if (isNullId)
                return new BadRequestObjectResult(new NetworkError { Tag = NetworkErrorTag.WrongParameters, Message = "Identifier is null. Must be an unique identifier of the user." });

            await Task.Delay(100);

            LoginResponse response = new LoginResponse { IsNewUser = true, SessionKey = Guid.NewGuid().ToString() };

            return new OkObjectResult(response);
        }
    }
}