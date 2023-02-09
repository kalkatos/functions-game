using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Game
{
	public interface IGame
	{
		bool IsActionAllowed (string playerId, StateInfo stateChanges, MatchRegistry match, StateRegistry state);
		StateRegistry PrepareTurn (MatchRegistry match, StateRegistry lastState);
	}
}
