using Kalkatos.Network.Model;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public static class AnalyticsFunctions
    {
        private static IAnalyticsService service = GlobalConfigurations.AnalyticsService;

        public static async Task<Response> SendEvent (string playerId, string key, string value)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(key))
                return new Response { IsError = true, Message = "Wrong parameters. PlayerId and Key must not be null." };
            await service.SendEvent(playerId, key, value);
            return new Response { IsError = false, Message = "OK" };
        }
    }
}
