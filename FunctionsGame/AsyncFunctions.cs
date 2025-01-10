using Kalkatos.Network.Model;
using Kalkatos.Network.Registry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.Network;

public static class AsyncFunctions
{
	private static IService service = Global.Service;
	private static IAsyncGame game = Global.AsyncGame;
	private static Random rand = Global.Random;

	public static async Task<AddAsyncObjectResponse> AddAsyncObject (AddAsyncObjectRequest request)
	{
		if (string.IsNullOrEmpty(request.Type) || string.IsNullOrEmpty(request.PlayerId))
			return new AddAsyncObjectResponse { IsError = true, Message = "Player id and region may not be null." };
		if (request.Info == null)
			return new AddAsyncObjectResponse { IsError = true, Message = "Async object info is null." };
		if (!game.IsCorrectRequest(request))
			return new AddAsyncObjectResponse { IsError = true, Message = "Data are incorrect for async game." };
		string id = request.Info.Id;
		if (!string.IsNullOrEmpty(id))
		{
			string registrySerialized = await service.GetData(Global.ASYNC_TABLE, request.Type, id, "");
			if (!string.IsNullOrEmpty(registrySerialized))
			{
				AsyncObjectRegistry oldRegistry = JsonConvert.DeserializeObject<AsyncObjectRegistry>(registrySerialized);
				if (oldRegistry != null && (oldRegistry.PlayerId != request.PlayerId || oldRegistry.Type != request.Type))
					return new AddAsyncObjectResponse { IsError = true, Message = $"No permission to change object of id {id}." };
			}
		}
		else
			id = Helper.GetRandomMatchAlias(6);
		AsyncObjectRegistry newRegistry = new AsyncObjectRegistry
		{
			Id = id,
			Type = request.Type,
			PlayerId = request.PlayerId,
			Author = request.Info.Author,
			Data = request.Info.Properties,
		};
		await service.UpsertData(Global.ASYNC_TABLE, request.Type, id, JsonConvert.SerializeObject(newRegistry));
		await game.TreatObjectAdded(newRegistry);
		return new AddAsyncObjectResponse { RegisteredId = id, Message = "OK" };
	}

	public static async Task<AsyncObjectResponse> GetAsyncObjects (AsyncObjectRequest request)
	{
		if (string.IsNullOrEmpty(request.Type))
			return new AsyncObjectResponse { IsError = true, Message = "Region may not be null." };
		var registries = await service.GetAllData(Global.ASYNC_TABLE, request.Type, request.Filter);
		if (registries == null || registries.Count == 0)
			return new AsyncObjectResponse { IsError = true, Message = "No object available." };
		AsyncObjectRegistry[] objs = registries.Values.Select(JsonConvert.DeserializeObject<AsyncObjectRegistry>).ToArray();
		int maxQuantity = 100;
		request.Quantity = Math.Clamp(request.Quantity, 1, maxQuantity);
		request.Quantity = Math.Min(request.Quantity, objs.Length);
		if (string.IsNullOrEmpty(request.Id))
		{
			if (request.Quantity == 1)
			{
				AsyncObjectRegistry randObj = objs[rand.Next(0, objs.Length)];
				return new AsyncObjectResponse
				{
					Message = "OK",
					Objects = new[] { randObj.GetInfo() }
				};
			}

			List<AsyncObjectRegistry> available = new(objs);
			List<AsyncObjectRegistry> selected = new();
			for (int i = 0; i < request.Quantity; i++)
			{
				int randIndex = rand.Next(0, available.Count);
				selected.Add(available[randIndex]);
				available.RemoveAt(randIndex);
			}
			return new AsyncObjectResponse
			{
				Message = "OK",
				Objects = selected.Select(x => x.GetInfo()).ToArray()
			};
		}
		AsyncObjectRegistry idRegistry = objs.FirstOrDefault(x => x.Id == request.Id);
		if (idRegistry == null)
			return new AsyncObjectResponse { IsError = true, Message = $"Object with Id {request.Id} was not found." };
		return new AsyncObjectResponse
		{
			Message = "OK",
			Objects = new[] { idRegistry.GetInfo() }
		};
	}
}
