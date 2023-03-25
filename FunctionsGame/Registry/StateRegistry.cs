using System.Collections.Generic;
using Kalkatos.Network.Model;
using Newtonsoft.Json;

namespace Kalkatos.FunctionsGame.Registry
{
	public class StateRegistry
	{
		public bool IsMatchEnded;
		public int TurnNumber;

		[JsonProperty] private Dictionary<string, string> publicProperties;
		[JsonProperty] private Dictionary<string, Dictionary<string, string>> privateProperties;
		private int hash;

		public int Hash => hash;

		public StateRegistry ()
		{
			publicProperties = new Dictionary<string, string>();
			privateProperties = new Dictionary<string, Dictionary<string, string>>();
		}

		public StateRegistry (string[] playerIds) : this ()
		{
			for (int i = 0; i < playerIds.Length; i++)
				privateProperties.Add(playerIds[i], new Dictionary<string, string>());
			UpdateHash();
		}

		public StateRegistry (Dictionary<string, Dictionary<string, string>> privateProperties) : this ()
		{
			this.privateProperties = privateProperties;
			UpdateHash();
		}

		public StateInfo GetStateInfo (string playerId)
		{
			Dictionary<string, string> publicPropertiesClone = new Dictionary<string, string>();
			Dictionary<string, string> privatePropertiesClone = new Dictionary<string, string>();
			foreach (var item in publicProperties)
				publicPropertiesClone[item.Key] = item.Value;
			foreach (var item in privateProperties[playerId])
				privatePropertiesClone[item.Key] = item.Value;
			StateInfo stateInfo = new StateInfo
			{
				PublicProperties = publicPropertiesClone,
				PrivateProperties = privatePropertiesClone,
				Hash = GetHash(publicPropertiesClone, privatePropertiesClone)
			};
			return stateInfo;
		}

		public StateRegistry Clone ()
		{
			var privatePropertiesClone = new Dictionary<string, Dictionary<string, string>>();
			foreach (var playerProperty in privateProperties)
			{
				var newPlayerProperty = (id: playerProperty.Key, prop: new Dictionary<string, string>());
				foreach (var keyAndValue in playerProperty.Value)
					newPlayerProperty.prop.Add(keyAndValue.Key, keyAndValue.Value);
				privatePropertiesClone.Add(newPlayerProperty.id, newPlayerProperty.prop);
			}
			StateRegistry newState = new StateRegistry(privatePropertiesClone);
			newState.UpsertPublicProperties(publicProperties);
			newState.IsMatchEnded = IsMatchEnded;
			newState.TurnNumber = TurnNumber;
			return newState;
		}

		public bool HasPublicProperty (string key)
		{ 
			return publicProperties.ContainsKey(key); 
		}

		public bool HasPrivateProperty (string id, string key)
		{
			return privateProperties.ContainsKey(id) && privateProperties[id].ContainsKey(key);
		}

		public string GetPublic (string key)
		{
			if (publicProperties.ContainsKey(key))
				return publicProperties[key];
			return "";
		}

		public string GetPrivate (string id, string key) 
		{
			if (privateProperties.ContainsKey(id) && privateProperties[id].ContainsKey(key))
				return privateProperties[id][key];
			return "";
		}

		public string[] GetPlayers ()
		{
			string[] result = new string[privateProperties.Keys.Count];
			int index = 0;
			foreach (var item in privateProperties.Keys)
				result[index++] = item;
			return result;
		}

		public void UpsertPublicProperty (string key, string value)
		{
			publicProperties[key] = value;
			UpdateHash();
		}

		public void UpsertAllPrivateProperties (params (string key, string value)[] keysAndValues)
		{
			foreach (var prop in privateProperties)
				foreach (var kv in keysAndValues)
					prop.Value[kv.key] = kv.value;
			UpdateHash();
		}

		public void UpsertProperties ((string key, string value)[] allPrivateProperties = null, (string id, string key, string value)[] idPrivateProperties = null, (string key, string value)[] publicProperties = null)
		{
			if (allPrivateProperties != null)
				foreach (var prop in privateProperties)
					foreach (var kv in allPrivateProperties)
						prop.Value[kv.key] = kv.value;
			if (idPrivateProperties != null)
				foreach (var item in idPrivateProperties)
					privateProperties[item.id][item.key] = item.value;
			if (publicProperties != null)
				foreach (var item in publicProperties)
					this.publicProperties[item.key] = item.value;
			UpdateHash();
		}

		public void UpsertPrivateProperties (params (string id, string key, string value)[] idKeyAndValue)
		{
			foreach (var item in idKeyAndValue)
				privateProperties[item.id][item.key] = item.value;
			UpdateHash();
		}

		public void UpsertPrivateProperties (string id, Dictionary<string, string> dict)
		{
			foreach (var item in dict)
				privateProperties[id][item.Key] = item.Value;
			UpdateHash();
		}

		public void UpsertPublicProperties (params (string key, string value)[] keysAndValues)
		{
			foreach (var item in keysAndValues)
				publicProperties[item.key] = item.value;
			UpdateHash();
		}

		public void UpsertPublicProperties (Dictionary<string, string> dict)
		{
			foreach (var item in dict)
				publicProperties[item.Key] = item.Value;
			UpdateHash();
		}

		public void UpdateHash ()
		{
			Dictionary<string, string>[] dictionaries = new Dictionary<string, string>[privateProperties.Count + 1];
			dictionaries[0] = publicProperties;
			int index = 1;
			foreach (var item in privateProperties)
				dictionaries[index++] = item.Value;
			hash = GetHash(dictionaries);
		}

		public int GetHash (params Dictionary<string, string>[] dicts)
		{
			unchecked
			{
				int hash = 23;
				foreach (var dict in dicts)
				{
					foreach (var item in dict)
					{
						foreach (char c in item.Key)
							hash = hash * 31 + c;
						foreach (char c in item.Value)
							hash = hash * 31 + c;
					}
				}
				return hash;
			}
		}
	}
}
