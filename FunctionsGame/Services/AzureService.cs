using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using Kalkatos.Network.Registry;
using Kalkatos.Network.ShadowAlchemy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kalkatos.Network.Azure
{

    public class AzureService : ILoginService, IMatchService, ILeaderboardService, IDataService, IAnalyticsService, IAsyncService
    {
        // ████████████████████████████████████████████ G A M E ████████████████████████████████████████████

        public async Task<GameRegistry> GetGameConfig (string gameId)
        {
            BlockBlobClient identifierFile = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "games", $"{gameId}.json");
            bool retry = true;
            while (retry)
            {
                try
                {
                    if (await identifierFile.ExistsAsync())
                        using (Stream stream = await identifierFile.OpenReadAsync())
                            return JsonConvert.DeserializeObject<GameRegistry>(Helper.ReadBytes(stream, Encoding.UTF8));
                    retry = false;
                }
                catch (RequestFailedException ex)
                {
                    if (ex.ErrorCode != "ConditionNotMet")
                        throw;
                }
            }
            return new GameRegistry();
        }

        // ████████████████████████████████████████████ L O G I N ████████████████████████████████████████████

        public async Task<string> GetPlayerId (string deviceId)
        {
            BlockBlobClient identifierFile = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{deviceId}");
            bool retry = true;
            while (retry)
            {
                try
                {
                    if (await identifierFile.ExistsAsync())
                        using (Stream stream = await identifierFile.OpenReadAsync())
                            return Helper.ReadBytes(stream, Encoding.UTF8);
                    retry = false;
                }
                catch (RequestFailedException ex)
                {
                    if (ex.ErrorCode != "ConditionNotMet")
                        throw;
                }
            }
            return null;
        }

        public async Task RegisterDeviceWithId (string deviceId, string playerId)
        {
            BlockBlobClient identifierFile = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{deviceId}");
            using (Stream stream = await identifierFile.OpenWriteAsync(true))
                stream.Write(Encoding.UTF8.GetBytes(playerId));
        }

        public async Task<PlayerRegistry> GetPlayerRegistry (string playerId)
        {
            BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{playerId}.json");
            bool retry = true;
            while (retry)
            {
                try
                {
                    if (await playerBlob.ExistsAsync())
                        using (Stream stream = await playerBlob.OpenReadAsync())
                            return JsonConvert.DeserializeObject<PlayerRegistry>(Helper.ReadBytes(stream, Encoding.UTF8));
                    retry = false;
                }
                catch (RequestFailedException ex)
                {
                    if (ex.ErrorCode != "ConditionNotMet")
                        throw;
                }
            }
            return null;
        }

        public async Task SetPlayerRegistry (PlayerRegistry registry)
        {
            BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{registry.PlayerId}.json");
            using (Stream stream = await playerBlob.OpenWriteAsync(true))
                stream.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(registry)));
        }

        public async Task DeletePlayerRegistry (string playerId)
        {
            BlockBlobClient playerBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "players", $"{playerId}.json");
            if (await playerBlob.ExistsAsync())
                await playerBlob.DeleteAsync();
        }

        // ████████████████████████████████████████████ M A T C H M A K I N G ████████████████████████████████████████████

        public async Task<MatchmakingEntry[]> GetMatchmakingEntries (string region, string matchId, string playerId, string alias, MatchmakingStatus status)
        {
            await Task.Delay(1);
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
            List<PlayerLookForMatchEntity> query = null;
            if (!string.IsNullOrEmpty(region))
                query = tableClient.Query<PlayerLookForMatchEntity>(item => item.PartitionKey == region)?.ToList();
            if (!string.IsNullOrEmpty(playerId))
                query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId))?.ToList()
                    ?? tableClient.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId)?.ToList();
            if (!string.IsNullOrEmpty(matchId))
                query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.MatchId == matchId))?.ToList()
                    ?? tableClient.Query<PlayerLookForMatchEntity>(item => item.MatchId == matchId)?.ToList();
            if (!string.IsNullOrEmpty(alias))
                query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.Alias == alias))?.ToList()
                    ?? tableClient.Query<PlayerLookForMatchEntity>(item => item.Alias == alias)?.ToList();
            if (status != MatchmakingStatus.Undefined)
            {
                int statusInt = (int)status;
                query = query?.Intersect(tableClient.Query<PlayerLookForMatchEntity>(item => item.Status == statusInt))?.ToList()
                    ?? tableClient.Query<PlayerLookForMatchEntity>(item => item.Status == statusInt)?.ToList();
            }
            if (query == null)
                return null;
            int count = query.Count;
            if (count > 0)
            {
                MatchmakingEntry[] result = new MatchmakingEntry[count];
                int index = 0;
                foreach (var item in query)
                    result[index++] = item.ToEntry();
                return result;
            }
            return null;
        }

        public async Task UpsertMatchmakingEntry (MatchmakingEntry entry)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
            await tableClient.UpsertEntityAsync(
                new PlayerLookForMatchEntity
                {
                    PartitionKey = entry.Region,
                    RowKey = entry.PlayerId,
                    MatchId = entry.MatchId,
                    PlayerInfoSerialized = entry.PlayerInfoSerialized,
                    Status = (int)entry.Status,
                    Alias = entry.Alias,
                    UseLobby = entry.UseLobby,
                });
        }

        public async Task DeleteMatchmakingHistory (string playerId, string matchId)
        {
            TableClient matchmakingTable = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Matchmaking");
            List<PlayerLookForMatchEntity> query = null;
            if (!string.IsNullOrEmpty(playerId))
                query = query?.Intersect(matchmakingTable.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId))?.ToList()
                    ?? matchmakingTable.Query<PlayerLookForMatchEntity>(item => item.RowKey == playerId)?.ToList();
            if (!string.IsNullOrEmpty(matchId))
                query = query?.Intersect(matchmakingTable.Query<PlayerLookForMatchEntity>(item => item.MatchId == matchId))?.ToList()
                    ?? matchmakingTable.Query<PlayerLookForMatchEntity>(item => item.MatchId == matchId)?.ToList();
            if (query == null)
                return;
            foreach (var item in query)
                await matchmakingTable.DeleteEntityAsync(item.PartitionKey, item.RowKey);
        }

        // ████████████████████████████████████████████ M A T C H ████████████████████████████████████████████

        public async Task<MatchRegistry> GetMatchRegistry (string matchId)
        {
            BlockBlobClient matchBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{matchId}.json");
            bool retry = true;
            while (retry)
            {
                try
                {
                    if (await matchBlob.ExistsAsync())
                        using (Stream stream = await matchBlob.OpenReadAsync(true))
                            return JsonConvert.DeserializeObject<MatchRegistry>(Helper.ReadBytes(stream, Encoding.UTF8));
                    retry = false;
                }
                catch (RequestFailedException ex)
                {
                    if (ex.ErrorCode != "ConditionNotMet")
                        throw;
                }
            }
            return null;
        }

        public async Task DeleteMatchRegistry (string matchId)
        {
            BlockBlobClient matchBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{matchId}.json");
            try
            {
                if (await matchBlob.ExistsAsync())
                    await matchBlob.DeleteAsync();
            }
            catch { }
        }

        public async Task SetMatchRegistry (MatchRegistry matchRegistry)
        {
            BlockBlobClient matchBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "matches", $"{matchRegistry.MatchId}.json");
            try
            {
                using (Stream stream = await matchBlob.OpenWriteAsync(true))
                    stream.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(matchRegistry)));
            }
            catch { }
        }

        public async Task ScheduleCheckMatch (int millisecondsDelay, string matchId, int lastHash)
        {
            QueueClient checkMatchQueue = new QueueClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "check-match");
            string message = $"{matchId}|{lastHash}";
            var bytes = Encoding.UTF8.GetBytes(message);
            await checkMatchQueue.SendMessageAsync(Convert.ToBase64String(bytes), TimeSpan.FromMilliseconds(millisecondsDelay));
        }

        // ████████████████████████████████████████████ S T A T E ████████████████████████████████████████████

        public async Task<StateRegistry> GetState (string matchId)
        {
            BlockBlobClient stateBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
            bool retry = true;
            while (retry)
            {
                try
                {
                    if (await stateBlob.ExistsAsync())
                    {
                        using (Stream stream = await stateBlob.OpenReadAsync())
                        {
                            StateRegistry state = JsonConvert.DeserializeObject<StateRegistry>(Helper.ReadBytes(stream, Encoding.UTF8));
                            state?.UpdateHash();
                            return state;
                        }
                    }
                    retry = false;
                }
                catch (RequestFailedException ex)
                {
                    if (ex.ErrorCode != "ConditionNotMet")
                        throw;
                }
            }
            return null;
        }

        public async Task<bool> SetState (string matchId, int? oldStateHash, StateRegistry newState)
        {
            BlockBlobClient stateBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
            bool stateSetSuccessfully = false;
            while (!stateSetSuccessfully)
            {
                try
                {
                    StateRegistry stateRegistry = await GetState(matchId);
                    if (stateRegistry != null && oldStateHash.HasValue && oldStateHash.Value != stateRegistry.Hash)
                    {
                        Logger.LogError($"   [SetState] states don't match ===\n saved - {JsonConvert.SerializeObject(stateRegistry, Formatting.Indented)}");
                        return false;
                    }
                    using (Stream stream = await stateBlob.OpenWriteAsync(true))
                        stream.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(newState)));
                    stateSetSuccessfully = true;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        public async Task DeleteState (string matchId)
        {
            BlockBlobClient statesBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "states", $"{matchId}.json");
            try
            {
                if (await statesBlob.ExistsAsync())
                    await statesBlob.DeleteAsync();
            }
            catch { }
        }

        // ████████████████████████████████████████████ A C T I O N ████████████████████████████████████████████

        public async Task AddAction (string matchId, string playerId, ActionRegistry action)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Actions");
            await tableClient.AddEntityAsync(
                new ActionEntity
                {
                    PartitionKey = matchId,
                    RowKey = action.Id,
                    PlayerId = playerId,
                    IsProcessed = false,
                    SerializedAction = JsonConvert.SerializeObject(action)
                });
        }

        public async Task UpdateActions (string matchId, List<ActionRegistry> actionList)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Actions");
            var query = tableClient.QueryAsync<ActionEntity>(t => t.PartitionKey == matchId);
            foreach (ActionRegistry action in actionList)
            {
                ActionEntity entity = new ActionEntity
                {
                    PartitionKey = matchId,
                    RowKey = action.Id,
                    PlayerId = action.PlayerId,
                    SerializedAction = JsonConvert.SerializeObject(action),
                    IsProcessed = action.IsProcessed,
                };
                await tableClient.UpsertEntityAsync(entity);
            }
        }

        public async Task<List<ActionRegistry>> GetActions (string matchId)
        {
            List<ActionRegistry> resultList = new List<ActionRegistry>();
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Actions");
            var query = tableClient.QueryAsync<ActionEntity>(t => t.PartitionKey == matchId && !t.IsProcessed);
            if (query == null)
                return resultList;
            await foreach (var item in query)
                resultList.Add(JsonConvert.DeserializeObject<ActionRegistry>(item.SerializedAction));
            return resultList;
        }

        public async Task DeleteActions (string matchId)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Actions");
            var query = tableClient.QueryAsync<ActionEntity>(t => t.PartitionKey == matchId);
            await foreach (ActionEntity item in query)
                tableClient.DeleteEntity(item.PartitionKey, item.RowKey);
        }

        // ████████████████████████████████████████████ O T H E R ████████████████████████████████████████████

        public async Task<bool> GetBool (string key)
        {
            BlockBlobClient dataBlob = new BlockBlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "rules", "data.json");
            Dictionary<string, string> dataDict = null;
            bool retry = true;
            while (retry)
            {
                try
                {
                    if (await dataBlob.ExistsAsync())
                    {
                        using (Stream stream = await dataBlob.OpenReadAsync())
                        {
                            dataDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(Helper.ReadBytes(stream, Encoding.UTF8));
                            if (dataDict.ContainsKey(key))
                                return dataDict[key] == "1";
                        }
                    }
                    retry = false;
                }
                catch (RequestFailedException ex)
                {
                    if (ex.ErrorCode != "ConditionNotMet")
                        throw;
                }
            }
            return false;
        }

        public async Task LogError (string error, string group, string metadata)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Errors");
            if (string.IsNullOrEmpty(group))
                group = "General";
            string id = Guid.NewGuid().ToString();
            await tableClient.UpsertEntityAsync(new ErrorEntity { PartitionKey = group, RowKey = id, Error = error, Metadata = metadata });
        }

        // █████████████████████████████████████████ L E A D E R B O A R D █████████████████████████████████████████

        public async Task AddLeaderboardEvent (LeaderboardRegistry registry)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Leaderboard");
            string gameId = registry.GameId;
            try
            {
                Response<LeaderboardEntity> response = await tableClient.GetEntityAsync<LeaderboardEntity>(gameId, registry.PlayerId);
                if (response.Value == null || response.Value.Value > registry.Value)
                    return;
            }
            catch
            {
                // Ignore
            }
            if (string.IsNullOrEmpty(gameId))
                gameId = "Unknown";
            await tableClient.UpsertEntityAsync(
                new LeaderboardEntity
                {
                    PartitionKey = gameId,
                    RowKey = registry.PlayerId,
                    PlayerName = registry.PlayerName,
                    Key = registry.Key,
                    Value = registry.Value,
                    DataSerialized = registry.DataSerialized
                });
        }

        public async Task<LeaderboardRegistry[]> GetLeaderboardEvents (string gameId, string key)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Leaderboard");
            var query = tableClient.QueryAsync<LeaderboardEntity>(t => t.PartitionKey == gameId && t.Key == key);
            List<LeaderboardRegistry> leaderboard = new();
            await foreach (var entity in query)
            {
                LeaderboardRegistry info = new LeaderboardRegistry
                {
                    GameId = entity.PartitionKey,
                    PlayerId = entity.RowKey,
                    Key = entity.Key,
                    Value = entity.Value,
                    PlayerName = entity.PlayerName,
                    Rank = entity.Rank,
                    PageId = entity.PageId,
                    NextPageId = entity.NextPageId,
                    DataSerialized = entity.DataSerialized,
                };
                leaderboard.Add(info);
            }
            return leaderboard.ToArray();
        }

        public async Task UpdateLeaderboardEvents (LeaderboardRegistry[] registries)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Leaderboard");
            foreach (var registry in registries)
            {
                LeaderboardEntity entity = new LeaderboardEntity
                {
                    PartitionKey = registry.GameId,
                    RowKey = registry.PlayerId,
                    Key = registry.Key,
                    Value = registry.Value,
                    PlayerName = registry.PlayerName,
                    Rank = registry.Rank,
                    PageId = registry.PageId,
                    NextPageId = registry.NextPageId,
                    DataSerialized = registry.DataSerialized,
                };
                await tableClient.UpsertEntityAsync(entity);
            }
        }

        // █████████████████████████████████████████████ D A T A █████████████████████████████████████████████

        public async Task<string> GetValue (string key, string defaultValue)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Data");
            try
            {
                var response = await tableClient.GetEntityAsync<DataEntity>("Default", key);
                return response.Value.Value;
            }
            catch
            {
                return defaultValue;
            }
        }

        public async Task SetValue (string key, string value)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Data");
            DataEntity entity = new DataEntity { PartitionKey = "Default", RowKey = key, Value = value };
            await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task Delete (string key)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Data");
            await tableClient.DeleteEntityAsync("Default", key);
        }

        // █████████████████████████████████████████ A N A L Y T I C S █████████████████████████████████████████

        public async Task SendEvent (string playerId, string key, string value)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Analytics");
            AnalyticsEntity entity = new AnalyticsEntity 
            { 
                PartitionKey = "Default", 
                RowKey = Guid.NewGuid().ToString(), 
                PlayerId = playerId,
                Key = key, 
                Value = value
            };
            await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        // █████████████████████████████████████████████ A S Y N C █████████████████████████████████████████████

        public async Task UpsertAsyncObject (AsyncObjectRegistry registry)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Async");
            TableEntity dynamicEntity = new TableEntity
            {
                PartitionKey = registry.Region,
                RowKey = registry.Id
            };
            dynamicEntity.Add("Author", registry.Author);
            dynamicEntity.Add("PlayerId", registry.PlayerId);
            foreach (var item in registry.Data)
                dynamicEntity.Add(item.Key, item.Value);
            await tableClient.UpsertEntityAsync(dynamicEntity, TableUpdateMode.Replace);
        }

        public async Task<AsyncObjectRegistry[]> GetAsyncObjects (string region)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Async");
            var query = tableClient.QueryAsync<TableEntity>(t => t.PartitionKey == region);
            List<AsyncObjectRegistry> objs = new();
            await foreach (var entity in query)
                objs.Add(entity.ToAsyncObjectRegistry());
            return objs.ToArray();
        }

        public async Task<AsyncObjectRegistry> GetAsyncObject (string region, string id)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "Async");
            try
            {
                var response = await tableClient.GetEntityAsync<TableEntity>(region, id);
                if (!response.HasValue)
                    return null;
                TableEntity entity = response.Value;
                return entity.ToAsyncObjectRegistry();
            }
            catch
            {
                return null;
            }
        }
    }

    public class AzureService2 : IService
    {
        public async Task DeleteData (string table, string partition, string key)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
            await tableClient.CreateIfNotExistsAsync();
            await tableClient.DeleteEntityAsync(partition, key);
        }

        public async Task<Dictionary<string, string>> GetAllData (string table, string partition)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
            await tableClient.CreateIfNotExistsAsync();
            var query = tableClient.QueryAsync<TableEntity>(t => t.PartitionKey == partition, 100);
            Dictionary<string, string> result = new();
            await foreach (var entity in query)
                result.Add(entity.RowKey, entity["Value"] as string);
            return result;
        }

        public async Task<string> GetData (string table, string partition, string key, string defaultValue)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
            await tableClient.CreateIfNotExistsAsync();
            var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(partition, key);
            if (!response.HasValue)
                return defaultValue;
            TableEntity entity = response.Value;
            return entity["Value"] as string;
        }

        public async Task UpsertData (string table, string partition, string key, string value)
        {
            TableClient tableClient = new TableClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), table);
            await tableClient.CreateIfNotExistsAsync();
            
            TableEntity entity = new TableEntity
            {
                PartitionKey = partition,
                RowKey = key
            };
            Register(key, value);
            await tableClient.UpsertEntityAsync(entity);

            void Register (string subKey, string data, bool isFirstLevel = true)
            {
                if (isFirstLevel)
                    entity.Add("Value", data);
                if (data.TryParseAsDict(out Dictionary<string, string> dataDict) && dataDict != null)
                {
                    if (!isFirstLevel)
                        entity.Add(subKey, data);
                    foreach (var item in dataDict)
                        if (string.IsNullOrEmpty(subKey) || isFirstLevel)
                            Register(item.Key, item.Value, false);
                        else
                            Register($"{subKey}.{item.Key}", item.Value, false);
                }
                else if (!isFirstLevel)
                    entity.Add(subKey, data);
            }
        }
    }

    public static class TableEntityExtensions
    {
        private static JsonSerializerSettings serializationSettings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };

        public static bool TryParseAsDict (this string data, out Dictionary<string, string> result)
        {
            try
            {
                result = JsonConvert.DeserializeObject<Dictionary<string, string>>(data, serializationSettings);
                return true;
            }
            catch (Exception)
            {
                result = null;
                return false;
            }
        }

        public static AsyncObjectRegistry ToAsyncObjectRegistry (this TableEntity entity)
        {
            AsyncObjectRegistry registry = new AsyncObjectRegistry
            {
                Region = entity.PartitionKey,
                Id = entity.RowKey,
                Data = new()
            };
            foreach (var item in entity)
            {
                switch (item.Key)
                {
                    case "Author":
                        registry.Author = (string)item.Value;
                        break;
                    case "PlayerId":
                        registry.PlayerId = (string)item.Value;
                        break;
                    case "odata.etag":
                    case "RowKey":
                    case "PartitionKey":
                        break;
                    default:
                        if (!(item.Value is string))
                            break;
                        registry.Data.Add(item.Key, (string)item.Value);
                        break;
                }
            }
            return registry;
        }
    }

    public class AnalyticsEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // Default
        public string RowKey { get; set; } // Random Guid
        public string PlayerId { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    public class DataEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // Default
        public string RowKey { get; set; } // Key
        public string Value { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    public class PlayerLookForMatchEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // Player region & other matchmaking data
        public string RowKey { get; set; } // Player ID
        public string PlayerInfoSerialized { get; set; }
        public string MatchId { get; set; }
        public int Status { get; set; }
        public string Alias { get; set; }
        public bool UseLobby { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public MatchmakingEntry ToEntry ()
        {
            return new MatchmakingEntry
            {
                Region = PartitionKey,
                PlayerId = RowKey,
                MatchId = MatchId,
                Status = (MatchmakingStatus)Status,
                PlayerInfoSerialized = PlayerInfoSerialized,
                Alias = Alias,
                UseLobby = UseLobby,
                Timestamp = Timestamp.Value.DateTime
            };
        }
    }

    public class ActionEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // Match ID
        public string RowKey { get; set; } // Random ID
        public string PlayerId { get; set; }
        public bool IsProcessed { get; set; }
        public string SerializedAction { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }


    public class ErrorEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // Group
        public string RowKey { get; set; } // ID
        public string Error { get; set; }
        public string Metadata { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    public class LeaderboardEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // Game
        public string RowKey { get; set; } // Player ID
        public string PlayerName { get; set; }
        public string Key { get; set; }
        public double Value { get; set; }
        public string DataSerialized { get; set; }
        public int Rank { get; set; }
        public string PageId { get; set; }
        public string NextPageId { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}