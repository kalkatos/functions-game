using System.Collections.Generic;
using System.Linq;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame.Registry
{
	public class StateRegistry
	{
		public int Index;
		public Dictionary<string, string> PublicProperties;
		public PrivateState[] PrivateStates;

		public StateInfo GetStateInfo (string playerId)
		{
			PrivateState playerPrivateState = PrivateStates.Where(item => item.PlayerId == playerId).First();
			StateInfo stateInfo = new StateInfo
			{
				PublicProperties = PublicProperties.ToDictionary(e => e.Key, e => e.Value),
				PrivateProperties = playerPrivateState.Properties.ToDictionary(e => e.Key, e => e.Value)
			};
			HashState(stateInfo);
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
				PrivateStates = privateStatesClone
			};
		}

		private void HashState (StateInfo stateInfo)
		{
			unchecked
			{
				stateInfo.Hash = 23;
				foreach (var item in stateInfo.PublicProperties)
				{
					foreach (char c in item.Key)
						stateInfo.Hash = stateInfo.Hash * 31 + c;
					foreach (char c in item.Value)
						stateInfo.Hash = stateInfo.Hash * 31 + c;
				}
				foreach (var item in stateInfo.PrivateProperties)
				{
					foreach (char c in item.Key)
						stateInfo.Hash = stateInfo.Hash * 31 + c;
					foreach (char c in item.Value)
						stateInfo.Hash = stateInfo.Hash * 31 + c;
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
