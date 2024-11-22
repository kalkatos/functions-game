using Kalkatos.Network.Model;
using Kalkatos.FunctionsGame.Registry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
    public static class AsyncFunctions
    {
        private static IAsyncService service = GlobalConfigurations.AsyncService;
        private static IAsyncGame game = GlobalConfigurations.AsyncGame;
        private static Random rand = new Random();

        public static async Task<AddAsyncObjectResponse> AddAsyncObject (AddAsyncObjectRequest request)
        {
            if (string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.PlayerId))
                return new AddAsyncObjectResponse { IsError = true, Message = "Player id and region may not be null." };
            if (request.Info == null)
                return new AddAsyncObjectResponse { IsError = true, Message = "Async object info is null." };
            if (!game.IsCorrectRequest(request))
                return new AddAsyncObjectResponse { IsError = true, Message = "Data are incorrect for async game." };
            string id = request.Info.Id;
            if (!string.IsNullOrEmpty(id))
            {
                AsyncObjectRegistry oldRegistry = await service.GetAsyncObject(request.Region, id);
                if (oldRegistry != null && (oldRegistry.PlayerId != request.PlayerId || oldRegistry.Region != request.Region))
                    return new AddAsyncObjectResponse { IsError = true, Message = $"No permission to change object of id {id}." };
            }
            else
                id = Helper.GetRandomMatchAlias();
            AsyncObjectRegistry newRegistry = new AsyncObjectRegistry
            {
                Id = id,
                Region = request.Region,
                PlayerId = request.PlayerId,
                Author = request.Info.Author,
                Data = request.Info.Properties,
            };
            await service.UpsertAsyncObject(newRegistry);
            return new AddAsyncObjectResponse { RegisteredId = id, Message = "OK" };
        }

        public static async Task<AsyncObjectResponse> GetAsyncObjects (AsyncObjectRequest request)
        {
            if (string.IsNullOrEmpty(request.Region)) 
                return new AsyncObjectResponse { IsError = true, Message = "Region may not be null." };
            AsyncObjectRegistry[] objs = await service.GetAsyncObjects(request.Region);
            if (objs == null || objs.Length == 0)
                return new AsyncObjectResponse { IsError = true, Message = "No object available." };
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
                        Objects = [randObj.GetInfo()]
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
                Objects = [idRegistry.GetInfo()]
            };
        }
    }
}
