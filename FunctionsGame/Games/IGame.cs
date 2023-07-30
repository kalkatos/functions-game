using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame
{
	public interface IGame
	{
		void SetSettings (GameRegistry settings);
		bool IsActionAllowed (string playerId, ActionInfo action, MatchRegistry match, StateRegistry state);
		StateRegistry CreateFirstState (MatchRegistry match);
		StateRegistry PrepareTurn (string playerId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions);
	}
}
