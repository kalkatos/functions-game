using System.Collections.Generic;
using System.Linq;
using Azure.Core;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame.Registry
{
	public class StateRegistry
	{
		public int Index;
		public Dictionary<string, string> PublicProperties;
		public PrivateState[] PrivateStates;
		public int Hash;

		public StateInfo GetStateInfo (string playerId)
		{
			PrivateState playerPrivateState = PrivateStates.Where(item => item.PlayerId == playerId).First();
			StateInfo stateInfo = new StateInfo
			{
				PublicProperties = PublicProperties.ToDictionary(e => e.Key, e => e.Value),
				PrivateProperties = playerPrivateState.Properties.ToDictionary(e => e.Key, e => e.Value),
				Hash = Hash
			};
			return stateInfo;
		}

		public StateRegistry Clone ()
		{
			PrivateState[] privateStatesClone = new PrivateState[PrivateStates.Length];
			for (int i = 0; i < PrivateStates.Length; i++)
				privateStatesClone[i] = PrivateStates[i].Clone();
			return new StateRegistry
			{
				Index = Index,
				PublicProperties = PublicProperties.ToDictionary(e => e.Key, e => e.Value),
				PrivateStates = privateStatesClone,
				Hash = Hash
			};
		}

		public void UpsertPublicProperty (string key, string value, string[] valuesToChange = null, string[] valuesToNotChange = null)
		{
			if (!PublicProperties.ContainsKey(key))
			{
				PublicProperties.Add(key, value);
				UpdateHash();
			}
			else
			{
				if (valuesToChange != null)
				{
					if (!valuesToChange.Contains(PublicProperties[key]))
						return;
				}
				if (valuesToNotChange != null)
				{
					if (valuesToNotChange.Contains(PublicProperties[key]))
						return;
				}
				PublicProperties[key] = value;
				UpdateHash();
			}
		}

		// TODO Encapsulate all properties and call update hash after every change in data
		public void UpdateHash ()
		{
			unchecked
			{
				Hash = 23;
				foreach (var item in PublicProperties)
				{
					foreach (char c in item.Key)
						Hash = Hash * 31 + c;
					foreach (char c in item.Value)
						Hash = Hash * 31 + c;
				}
				foreach (var item in PrivateStates)
				{
					foreach (var playerState in item.Properties)
					{
						foreach (char c in playerState.Key)
							Hash = Hash * 31 + c;
						foreach (char c in playerState.Value)
							Hash = Hash * 31 + c;
					}
				}
			}
		}
	}

	public class PrivateState
	{
		public string PlayerId;
		public Dictionary<string, string> Properties;

		public PrivateState Clone () 
		{
			return new PrivateState
			{
				PlayerId = PlayerId,
				Properties = Properties.ToDictionary(e => e.Key, e => e.Value)
			};
		}
	}
}
