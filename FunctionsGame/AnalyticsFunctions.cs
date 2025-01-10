using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public static class AnalyticsFunctions
{
	private static IService service = Global.Service;

	public static async Task<Response> SendEvent (string playerId, string key, string value)
	{
		if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(key))
			return new Response { IsError = true, Message = "Wrong parameters. PlayerId and Key must not be null." };
		var data = new { Key = key, Value = value };
		await service.UpsertData(Global.ANALYTICS_TABLE, playerId, Guid.NewGuid().ToString(), JsonConvert.SerializeObject(data));
		return new Response { IsError = false, Message = "OK" };
	}
}
