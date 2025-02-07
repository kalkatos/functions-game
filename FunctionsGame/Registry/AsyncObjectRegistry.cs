﻿using Kalkatos.Network.Model;
using System.Collections.Generic;
using System.Linq;

namespace Kalkatos.Network.Registry;

public class AsyncObjectRegistry
{
	public string Type;
	public string Id;
	public string Author;
	public string PlayerId;
	public Dictionary<string, string> Data;

	public AsyncObjectInfo GetInfo ()
	{
		return new AsyncObjectInfo
		{
			Author = Author,
			Id = Id,
			Properties = Data.ToDictionary(x => x.Key, x => x.Value)
		};
	}
}
