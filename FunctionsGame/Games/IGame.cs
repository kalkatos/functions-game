using Kalkatos.Network.Registry;
using Kalkatos.Network.Model;
using System.Collections.Generic;

namespace Kalkatos.Network;

public interface IGame
{
	string Name { get; }
	GameRegistry Settings { get; }
	void SetSettings (GameRegistry settings);
	bool IsActionAllowed (string playerId, ActionInfo action, MatchRegistry match, StateRegistry state);
	StateRegistry CreateFirstState (MatchRegistry match);
	StateRegistry PrepareTurn (string playerId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions);
	PlayerInfo CreateBot (Dictionary<string, string> settings);
}
