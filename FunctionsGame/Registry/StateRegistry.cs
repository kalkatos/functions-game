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
			Dictionary<string, string> mergedDict = new Dictionary<string, string>();
			PublicProperties.ToList().ForEach(x => mergedDict.Add(x.Key, x.Value));
			PrivateStates.Where(item => item.PlayerId == playerId).First().Properties.ToList().ForEach(x => mergedDict.Add(x.Key, x.Value));
			return new StateInfo { Properties = mergedDict };
		}
	}

	public class PrivateState
	{
		public string PlayerId;
		public Dictionary<string, string> Properties;
	}
}
