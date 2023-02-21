using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame
{
	public interface IGame
	{
		string GameId { get; }
		bool IsActionAllowed (string playerId, StateInfo stateChanges, MatchRegistry match, StateRegistry state);
		StateRegistry CreateFirstState (MatchRegistry match);
		StateRegistry PrepareTurn (MatchRegistry match, StateRegistry lastState);
	}
}
