using System;
using System.Collections.Generic;
using System.Linq;
using Kalkatos.Network.Model;
using Newtonsoft.Json;

namespace Kalkatos.Network.Registry;

public class StateRegistry
{
	public bool IsMatchEnded;
	public int TurnNumber;

	[JsonProperty] private Dictionary<string, string> publicProperties;
	[JsonProperty] private Dictionary<string, Dictionary<string, string>> privateProperties;
	[JsonProperty] private HashSet<string> syncdPlayers;
	private int hash;

	public int Hash
	{
		get
		{
			if (hash == 0)
				UpdateHash();
			return hash;
		}
	}

	public StateRegistry ()
	{
		publicProperties = new Dictionary<string, string>();
		privateProperties = new Dictionary<string, Dictionary<string, string>>();
		syncdPlayers = new HashSet<string>();
	}

	public StateRegistry (Dictionary<string, Dictionary<string, string>> privateProperties) : this()
	{
		this.privateProperties = privateProperties;
		UpdateHash();
	}

	public StateRegistry (string[] playerIds) : this()
	{
		for (int i = 0; i < playerIds.Length; i++)
			privateProperties.Add(playerIds[i], new Dictionary<string, string>());
		UpdateHash();
	}

	public void Sync (string playerId)
	{
		if (!syncdPlayers.Contains(playerId))
		{
			syncdPlayers.Add(playerId);
			UpdateHash();
		}
	}

	public bool IsSyncdForAllPlayers ()
	{
		return GetPlayers().All(p => syncdPlayers.Contains(p));
	}

	public void ClearSync ()
	{
		syncdPlayers.Clear();
		UpdateHash();
	}

	public bool IsPlayerSyncd (string playerId)
	{
		return syncdPlayers.Contains(playerId);
	}

	public bool IsSameSync (StateRegistry other)
	{
		return syncdPlayers.SequenceEqual(other.syncdPlayers);
	}

	public string[] GetUnsyncdPlayers ()
	{
		return GetPlayers().Except(syncdPlayers).ToArray();
	}

	public StateInfo GetStateInfo (string playerId)
	{
		Dictionary<string, string> publicPropertiesClone = new Dictionary<string, string>();
		Dictionary<string, string> privatePropertiesClone = new Dictionary<string, string>();
		foreach (var item in publicProperties)
			publicPropertiesClone[item.Key] = item.Value;
		if (privateProperties.ContainsKey(playerId))
			foreach (var item in privateProperties[playerId])
				privatePropertiesClone[item.Key] = item.Value;
		StateInfo stateInfo = new StateInfo
		{
			PublicProperties = publicPropertiesClone,
			PrivateProperties = privatePropertiesClone,
			Hash = hash, // GetHash(publicPropertiesClone, privatePropertiesClone)
			IsMatchEnded = IsMatchEnded
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
		newState.IsMatchEnded = IsMatchEnded;
		newState.TurnNumber = TurnNumber;
		newState.syncdPlayers = new(syncdPlayers);
		newState.UpsertPublicProperties(publicProperties);
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

	public bool HasAnyPrivatePropertyWithValue (string key, string value)
	{
		foreach (var item in privateProperties)
			if (item.Value.ContainsKey(key) && item.Value[key] == value)
				return true;
		return false;
	}

	public bool HasAllPrivatePropertiesWithValue (string key, string value)
	{
		foreach (var item in privateProperties)
			if (!item.Value.ContainsKey(key) || item.Value[key] != value)
				return false;
		return true;
	}

	public string GetPublic (string key)
	{
		if (publicProperties.ContainsKey(key))
			return publicProperties[key];
		return "";
	}

	public bool TryGetPublic (string key, out string value)
	{
		return publicProperties.TryGetValue(key, out value);
	}

	public string GetPrivate (string id, string key)
	{
		if (privateProperties.ContainsKey(id) && privateProperties[id].ContainsKey(key))
			return privateProperties[id][key];
		return "";
	}

	public bool TryGetPrivate (string id, string key, out string value)
	{
		if (!privateProperties.ContainsKey(id))
		{
			value = null;
			return false;
		}
		return privateProperties[id].TryGetValue(key, out value);
	}

	public string[] GetPlayers ()
	{
		return privateProperties.Keys.ToArray();
	}

	public void RemovePlayer (string playerId)
	{
		privateProperties.Remove(playerId);
		UpdateHash();
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

	public void UpsertProperties (
		(string key, string value)[] allPrivateProperties = null,
		(string id, string key, string value)[] idPrivateProperties = null,
		(string key, string value)[] publicProperties = null,
		bool clearSync = true)
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
		if (clearSync)
			syncdPlayers.Clear();
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

	public DateTimeOffset? GetTimeFromPublic (string key)
	{
		if (TryGetPublic(key, out string value)
			&& DateTimeOffset.TryParse(value, out DateTimeOffset time))
			return time.ToUniversalTime();
		return null;
	}

	public DateTimeOffset? GetTimeFromPrivate (string id, string key)
	{
		if (TryGetPrivate(id, key, out string value)
			&& DateTimeOffset.TryParse(value, out DateTimeOffset time))
			return time.ToUniversalTime();
		return null;
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

	public static bool operator == (StateRegistry a, StateRegistry b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (ReferenceEquals(b, null))
			return false;
		if (ReferenceEquals(a, null))
			return false;
		return a.Hash == b.Hash && a.IsSameSync(b);
	}

	public static bool operator != (StateRegistry a, StateRegistry b)
	{
		if (ReferenceEquals(a, b))
			return false;
		if (ReferenceEquals(b, null))
			return true;
		if (ReferenceEquals(a, null))
			return true;
		return a.Hash != b.Hash || !a.IsSameSync(b);
	}

	public override bool Equals (object obj)
	{
		if (base.Equals(obj))
			return true;
		if (obj == null || !(obj is StateRegistry))
			return false;
		StateRegistry other = (StateRegistry)obj;
		return Hash == other.Hash && IsSameSync(other);
	}

	public override int GetHashCode ()
	{
		return Hash;
	}
}
