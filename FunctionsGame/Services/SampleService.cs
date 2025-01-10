using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Kalkatos.Network;

internal class SampleService : IService
{
	public Task DeleteData (string table, string partition, string key)
	{
		// Delete data from DB
		throw new NotImplementedException();
	}

	public Task<Dictionary<string, string>> GetAllData (string table, string partition, string query)
	{
		// Get a batch of data from DB
		throw new NotImplementedException();
	}

	public Task<string> GetData (string table, string partition, string key, string defaultValue)
	{
		// Get some data from DB
		throw new NotImplementedException();
	}

	public Task UpsertData (string table, string partition, string key, string value)
	{
		// Update or Insert data into the DB
		throw new NotImplementedException();
	}
}
