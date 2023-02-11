using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System;

namespace Kalkatos.FunctionsGame.Game.Rps
{
	public class RpsGame : IGame
	{
		private readonly string[] allowedMoves = new string[] { "ROCK", "PAPER", "SCISSORS" };

		private const string phaseKey = "Phase";
		private const string handshakingKey = "Handshaking";
		private const string myMoveKey = "MyMove";
		private const string playPhaseStartTimeKey = "PlayPhaseStartTime";
		private const string playPhaseEndTimeKey = "PlayPhaseEndTime";
		private const string turnEndTimeKey = "TurnEndTime";
		private const string opponentMoveKey = "OpponentMove";
		private const string winnerKey = "Winner";
		private const string myScoreKey = "MyScore";
		private const string opponentScoreKey = "OpponentScore";

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
			StateRegistry newState;
			bool isNewMatch = true;
			if (lastState != null)
			{
				newState = lastState.Clone();
				DateTime utcNow = DateTime.UtcNow;
				DateTime playStartTime = ExtractTime(playPhaseStartTimeKey, lastState);
				DateTime playEndTime = ExtractTime(playPhaseEndTimeKey, lastState);
				DateTime turnEndTime = ExtractTime(turnEndTimeKey, lastState);

				int phase = 0;
				if (newState.HasPublicProperty(phaseKey))
					phase = int.Parse(newState.GetPublic(phaseKey));
				switch (phase)
				{
					case (int)Phase.Sync:
						if (utcNow >= playStartTime)
							newState.UpsertPublicProperties(
								(phaseKey, ((int)Phase.Play).ToString()),
								(playPhaseEndTimeKey, utcNow.AddSeconds(10).ToString("u")));
						return newState;
					case (int)Phase.Play:
						if (utcNow >= playEndTime)
							CheckRpsLogic(newState);
						return newState;
					case (int)Phase.Result:
						if (utcNow < turnEndTime)
							return lastState;
						isNewMatch = false;
						break;
					default:
						Logger.LogError("Unknown value of phase");
						break;
				}
			}
			else
				newState = new StateRegistry(match.PlayerIds);
			// New turn
			if (isNewMatch)
				newState.UpsertAllPrivateProperties((myMoveKey, ""), (opponentMoveKey, ""), (winnerKey, ""), (myScoreKey, "0"), (opponentScoreKey, "0"));
			else
				newState.UpsertAllPrivateProperties((myMoveKey, ""), (opponentMoveKey, ""), (winnerKey, ""));
			newState.UpsertPublicProperties(
				(playPhaseStartTimeKey, DateTime.UtcNow.AddSeconds(5).ToString("u")),
				(playPhaseEndTimeKey, DateTime.UtcNow.AddSeconds(15).ToString("u")),
				(turnEndTimeKey, DateTime.UtcNow.AddSeconds(22).ToString("u")),
				(phaseKey, ((int)Phase.Sync).ToString()));
			return newState;
		}

		private DateTime ExtractTime (string key, StateRegistry state)
		{
			if (state.HasPublicProperty(key) && DateTime.TryParse(state.GetPublic(key), out DateTime time))
				return time.ToUniversalTime();
			return default;
		}

		private bool IsInPlayPhase (StateRegistry state)
		{
			return DateTime.UtcNow > ExtractTime(playPhaseStartTimeKey, state) && DateTime.UtcNow < ExtractTime(playPhaseEndTimeKey, state);
		}

		private void CheckRpsLogic (StateRegistry state)
		{
			string[] playerList = state.GetPlayers();
			string p1Move = state.GetPrivate(playerList[0], myMoveKey);
			string p2Move = state.GetPrivate(playerList[1], myMoveKey);
			int winner = GetWinner(p1Move, p2Move);
			int p1Score = int.Parse(state.GetPrivate(playerList[0], myScoreKey)) + Math.Max(winner * -1, 0);
			int p2Score = int.Parse(state.GetPrivate(playerList[1], myScoreKey)) + Math.Max(winner, 0);
			state.UpsertPrivateProperties(
				(playerList[0], opponentMoveKey, p2Move),
				(playerList[1], opponentMoveKey, p1Move),
				(playerList[0], winnerKey, winner == 1 ? "Opponent" : winner == 0 ? "Tie" : "Me"),
				(playerList[1], winnerKey, winner == 1 ? "Me" : winner == 0 ? "Tie" : "Opponent"),
				(playerList[0], myScoreKey, p1Score.ToString()),
				(playerList[1], myScoreKey, p2Score.ToString()),
				(playerList[0], opponentScoreKey, p2Score.ToString()),
				(playerList[1], opponentScoreKey, p1Score.ToString()));
			state.UpsertPublicProperties(
				(phaseKey, ((int)Phase.Result).ToString()),
				(turnEndTimeKey, DateTime.UtcNow.AddSeconds(7).ToString("u")));

			int GetWinner (string move1, string move2)
			{
				switch (move1)
				{
					case "ROCK":
						switch (move2) { case "ROCK": return 0; case "PAPER": return 1; case "SCISSORS": return -1; }
						break;
					case "PAPER":
						switch (move2) { case "ROCK": return -1; case "PAPER": return 0; case "SCISSORS": return 1; } 
						break;
					case "SCISSORS":
						switch (move2) { case "ROCK": return 1; case "PAPER": return -1; case "SCISSORS": return 0; } 
						break;
				}
				return 0;
			}
		}

		private enum Phase
		{
			Sync,
			Play,
			Result
		}
	}
}