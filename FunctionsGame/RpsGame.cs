using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Game.Rps
{
	public class RpsGame : IGame
	{
		private readonly string[] allowedMoves = new string[] { "ROCK", "PAPER", "SCISSORS" };

		private const string handshakingKey = "Handshaking";
		private const string myMoveKey = "MyMove";
		private const string playPhaseStartTimeKey = "PlayPhaseStartTime";
		private const string playPhaseEndTimeKey = "PlayPhaseEndTime";
		private const string turnEndTimeKey = "TurnEndTime";
		private const string opponentMoveKey = "OpponentMove";

		public bool IsActionAllowed (string playerId, StateInfo stateChanges, MatchRegistry match, StateRegistry state)
		{
			bool result = !stateChanges.HasAnyPublicProperty();
			result &= stateChanges.OnlyHasThesePrivateProperties(handshakingKey, myMoveKey);
			if (IsInPlayPhase(state))
				result &= stateChanges.IsPrivatePropertyEqualsIfPresent(myMoveKey, allowedMoves);
			else
				result &= !stateChanges.HasPrivateProperty(myMoveKey);
			return result;
		}

		public StateRegistry PrepareTurn (MatchRegistry match, StateRegistry lastState)
		{
			StateRegistry newState = lastState?.Clone() ?? new StateRegistry(match.PlayerIds);
			if (lastState != null)
			{
				DateTime utcNow = DateTime.UtcNow;
				DateTime playEndTime = ExtractTime(playPhaseEndTimeKey, lastState);
				DateTime turnEndTime = ExtractTime(turnEndTimeKey, lastState);
				if (utcNow < playEndTime)
					return lastState;
				else if (utcNow >= playEndTime && utcNow < turnEndTime)
				{
					// TODO Set turn result in newState
					return newState;
				}
			}
			// New turn
			newState.PublicMatchProperties[playPhaseStartTimeKey] = DateTime.UtcNow.AddSeconds(5).ToString("u");
			newState.PublicMatchProperties[turnEndTimeKey] = DateTime.UtcNow.AddSeconds(15).ToString("u");
			foreach (var item in newState.PrivateProperties)
			{
				item.Properties[myMoveKey] = "";
				item.Properties[opponentMoveKey] = "";
			}
			return newState;
		}

		private DateTime ExtractTime (string key, StateRegistry state)
		{
			if (state.PublicMatchProperties.TryGetValue(key, out string time) && DateTime.TryParse(time, out DateTime timeParsed))
				return timeParsed.ToUniversalTime();
			return default;
		}

		private bool IsInPlayPhase (StateRegistry state)
		{
			return DateTime.UtcNow > ExtractTime(playPhaseStartTimeKey, state) && DateTime.UtcNow < ExtractTime(turnEndTimeKey, state);
		}
	}
}