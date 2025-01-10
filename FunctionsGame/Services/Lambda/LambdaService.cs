#if LAMBDA_FUNCTIONS

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Kalkatos.Network.Lambda;

public class LambdaService : IService
{
	private AmazonDynamoDBClient client;

	public LambdaService ()
	{
		client = new AmazonDynamoDBClient();
	}

	public async Task DeleteData (string table, string partition, string key)
	{
		DeleteItemRequest request = new DeleteItemRequest
		{
			TableName = table,
			Key = new()
			{
				{ "Category", new AttributeValue(partition) },
				{ "Id", new AttributeValue(key) }
			}
		};
		_ = await client.DeleteItemAsync(request);
	}

	public async Task<Dictionary<string, string>> GetAllData (string table, string partition, string query)
	{
		string category = $":{partition}";
		ScanRequest scanRequest = new ScanRequest
		{
			TableName = table,
			ProjectionExpression = "Id, Value",
			ExpressionAttributeValues = new Dictionary<string, AttributeValue>
			{
				{ category, new AttributeValue { S = partition } }
			},
			FilterExpression = $"Category={category}"
		};
		Dictionary<string, string> result = new();
		ScanResponse scanResult = await client.ScanAsync(scanRequest);
		if (scanResult.Items.Count == 1 && scanResult.Items[0].ContainsKey("Error"))
			return result;
		foreach (var item in scanResult.Items)
			result.Add(item["Id"].S, item["Value"].S);
		FilterWithQuery();
		return result;

		void FilterWithQuery ()
		{
			if (string.IsNullOrEmpty(query))
				return;
			query = query.Replace("'", "");
			string[] statements = query.Split(" and ");
			foreach (string statement in statements)
			{
				string[] split = statement.Split(" ");
				if (split.Length < 3)
					continue;
				List<KeyValuePair<string, string>> toRemove = new();
				foreach (var kv in result)
				{
					Dictionary<string, string> data = new();
					Helper.DismemberData(ref data, kv.Key, kv.Value);
					if ((split[1] == "eq" && data.ContainsKey(split[0]) && data[split[0]] != split[2])
						|| (split[1] == "ne" && data.ContainsKey(split[0]) && data[split[0]] == split[2]))
						toRemove.Add(kv);
				}
				result = result.Except(toRemove).ToDictionary();
			}
		}
	}

	public async Task<string> GetData (string table, string partition, string key, string defaultValue)
	{
		GetItemRequest request = new()
		{
			TableName = table,
			Key = new() 
			{ 
				{ "Category", new AttributeValue(partition) },
				{ "Id", new AttributeValue(key) }
			}
		};
		var response = await client.GetItemAsync(request);
		if (response.Item.ContainsKey("Error") || !response.Item.ContainsKey("Value"))
			return defaultValue;
		return response.Item["Value"].S;
	}

	public async Task UpsertData (string table, string partition, string key, string value)
	{
		Dictionary<string, string> dataDismembered = new();
		Helper.DismemberData(ref dataDismembered, key, value);
		var itemData = new Dictionary<string, AttributeValue>()
		{
			{ "Category", new AttributeValue(partition) },
			{ "Id", new AttributeValue(key) }
		};
		foreach (var item in dataDismembered)
			if (!string.IsNullOrEmpty(item.Value))
				itemData.Add(item.Key, new AttributeValue(item.Value));
		var request = new PutItemRequest
		{
			TableName = table,
			Item = itemData
		};

		await client.PutItemAsync(request);
	}
}

#endif
