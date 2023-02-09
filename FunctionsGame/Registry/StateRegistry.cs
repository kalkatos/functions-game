using System.Collections.Generic;
using System.Linq;
using Azure.Core;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame.Registry
{
	public class StateRegistry
	{
		public int Index;
		public readonly Dictionary<string, string> PublicMatchProperties;
		public readonly PlayerProperties[] PublicPlayerProperties;
		public readonly PlayerProperties[] PrivateProperties;

		private int hash;

		public int Hash => hash;

		public StateRegistry (string[] playerIds)
		{
			PublicMatchProperties = new Dictionary<string, string>();
			PrivateProperties = new PlayerProperties[playerIds.Length];
			PublicPlayerProperties = new PlayerProperties[playerIds.Length];
			for (int i = 0; i < playerIds.Length; i++)
			{
				PrivateProperties[i] = new PlayerProperties { PlayerId = playerIds[i], Properties = new Dictionary<string, string>() };
				PublicPlayerProperties[i] = new PlayerProperties { PlayerId = playerIds[i], Properties = new Dictionary<string, string>() };
			}
			UpdateHash();
		}

		public StateRegistry (PlayerProperties[] publicPlayerProperties, PlayerProperties[] privateProperties)
		{
			PublicMatchProperties = new Dictionary<string, string>();
			PublicPlayerProperties = publicPlayerProperties;
			PrivateProperties = privateProperties;
			UpdateHash();
		}

		public StateInfo GetStateInfo (string playerId)
		{
			PlayerProperties playerPrivateState = PrivateProperties.Where(item => item.PlayerId == playerId).First();
			StateInfo stateInfo = new StateInfo
			{
				PublicProperties = PublicMatchProperties.ToDictionary(e => e.Key, e => e.Value),
				PrivateProperties = playerPrivateState.Properties.ToDictionary(e => e.Key, e => e.Value),
				Hash = Hash
			};
			return stateInfo;
		}

		public StateRegistry Clone ()
		{
			PlayerProperties[] privatePropertiesClone = new PlayerProperties[PrivateProperties.Length];
			for (int i = 0; i < PrivateProperties.Length; i++)
				privatePropertiesClone[i] = PrivateProperties[i].Clone();
			PlayerProperties[] publicPlayerPropertiesClone = new PlayerProperties[PublicPlayerProperties.Length];
			for (int i = 0; i < PublicPlayerProperties.Length; i++)
				publicPlayerPropertiesClone[i] = PublicPlayerProperties[i].Clone();
			return new StateRegistry(publicPlayerPropertiesClone, privatePropertiesClone);
		}

		public void UpsertPublicProperty (string key, string value, string[] valuesToChange = null, string[] valuesToNotChange = null)
		{
			if (PublicMatchProperties.ContainsKey(key))
			{
				if (valuesToChange != null)
				{
					if (!valuesToChange.Contains(PublicMatchProperties[key]))
						return;
				}
				if (valuesToNotChange != null)
				{
					if (valuesToNotChange.Contains(PublicMatchProperties[key]))
						return;
				}
			}
			PublicMatchProperties[key] = value;
			UpdateHash();
		}

		// TODO Encapsulate all properties and call update hash after every change in data
		public void UpdateHash ()
		{
			unchecked
			{
				hash = 23;
				foreach (var item in PublicMatchProperties)
				{
					foreach (char c in item.Key)
						hash = hash * 31 + c;
					foreach (char c in item.Value)
						hash = hash * 31 + c;
				}
				foreach (var item in PrivateProperties)
				{
					foreach (var playerState in item.Properties)
					{
						foreach (char c in playerState.Key)
							hash = hash * 31 + c;
						foreach (char c in playerState.Value)
							hash = hash * 31 + c;
					}
				}
			}
		}
	}

	public class PlayerProperties
	{
		public string PlayerId;
		public Dictionary<string, string> Properties;

		public PlayerProperties Clone () 
		{
			return new PlayerProperties
			{
				PlayerId = PlayerId,
				Properties = Properties.ToDictionary(e => e.Key, e => e.Value)
			};
		}
	}
}
