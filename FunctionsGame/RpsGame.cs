using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Rps
{
	public class RpsGame : IGame
	{
		private readonly string[] allowedMoves = new string[] { "ROCK", "PAPER", "SCISSORS" };
		private int playPhaseStartDelay = 5;
		private int turnDuration = 10;
		private int endTurnDelay = 7;
		private int targetVictoryPoints = 2;
		private Random rand = new Random();
		private const string humanMoves = "SPPRRSSPRPSPSPRPSPRSRSSPRRSPPRSPRPPRSPRPSRPPPSRPSRPSRPPRSPRPSPRSRPSRPPRRPSPRSRPSRPPRPSPPRPRPSPRPSPRSPRPSRP";
		private int currentMove = -1;
		private GameRegistry gameSettings;

		// Config Keys
		private const string playPhaseStartDelayKey = "PlayPhaseStartDelay";
		private const string turnDurationKey = "TurnDuration";
		private const string endTurnDelayKey = "EndTurnDelay";
		private const string targetVictoryPointsKey = "TargetVictoryPoints";

		// Game Keys
		private const string phaseKey = "Phase";
		private const string handshakingKey = "Handshaking";
		private const string myMoveKey = "MyMove";
		private const string playPhaseStartTimeKey = "PlayPhaseStartTime";
		private const string playPhaseEndTimeKey = "PlayPhaseEndTime";
		private const string turnEndTimeKey = "TurnEndTime";
		private const string opponentMoveKey = "OpponentMove";
		private const string turnWinnerKey = "TurnWinner";
		private const string matchWinnerKey = "MatchWinner";
		private const string myScoreKey = "MyScore";
		private const string opponentScoreKey = "OpponentScore";

		public string GameId { get => "rps"; }

		public GameRegistry Settings
		{
			get => gameSettings;
			set
			{
				gameSettings = value;
				UpdateSettings(value.Settings);
			}
		}

		public bool IsActionAllowed (string playerId, StateInfo stateChanges, MatchRegistry match, StateRegistry state)
		{
			bool result = !stateChanges.HasAnyPublicProperty();
			result &= stateChanges.OnlyHasThesePrivateProperties(handshakingKey, myMoveKey);
			if (IsInPlayPhase(state))
				result &= stateChanges.IsPrivatePropertyEqualsIfPresent(myMoveKey, allowedMoves);
			return result;
		}

		public StateRegistry CreateFirstState (MatchRegistry match)
		{
			StateRegistry newState = new StateRegistry(match.PlayerIds);
			newState.UpsertPublicProperties(
				(playPhaseStartTimeKey, DateTime.MaxValue.ToString("u")),
				(playPhaseEndTimeKey, DateTime.MaxValue.ToString("u")),
				(turnEndTimeKey, DateTime.MaxValue.ToString("u")),
				(phaseKey, ((int)Phase.Sync).ToString()));
			newState.UpsertAllPrivateProperties((handshakingKey, ""), (myMoveKey, ""), (opponentMoveKey, ""), (turnWinnerKey, ""), (myScoreKey, "0"), 
				(opponentScoreKey, "0"), (matchWinnerKey, ""));
			return newState;
		}

		public StateRegistry PrepareTurn (MatchRegistry match, StateRegistry lastState)
		{
			StateRegistry newState = lastState.Clone();
			DateTime utcNow = DateTime.UtcNow;
			bool isFirstTurn = newState.TurnNumber == 0;

			if (!isFirstTurn)
			{
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
								(playPhaseEndTimeKey, utcNow.AddSeconds(turnDuration).ToString("u")));
						return newState;
					case (int)Phase.Play:
						if (HasBothPlayersSentTheirMoves(match.PlayerIds, newState) || utcNow >= playEndTime)
						{
							ApplyBotMoves(match, newState);
							CheckRpsLogic(newState);
						}
						return newState;
					case (int)Phase.Result:
						if (utcNow < turnEndTime)
							return lastState;
						else
						{
							CheckMatchEnded(newState);
							if (newState.IsMatchEnded)
								return newState;
						}
						break;
					case (int)Phase.Ended:
						return lastState;
					default:
						Logger.LogError("   [RPS] Unknown value of phase");
						break;
				}
			}

			// New turn
			newState.TurnNumber++;
			newState.UpsertAllPrivateProperties((myMoveKey, ""), (opponentMoveKey, ""), (turnWinnerKey, ""));
			newState.UpsertPublicProperties(
				(playPhaseStartTimeKey, DateTime.UtcNow.AddSeconds(playPhaseStartDelay).ToString("u")),
				(playPhaseEndTimeKey, DateTime.UtcNow.AddSeconds(playPhaseStartDelay + turnDuration).ToString("u")),
				(turnEndTimeKey, DateTime.UtcNow.AddSeconds(playPhaseStartDelay + turnDuration + endTurnDelay).ToString("u")),
				(phaseKey, ((int)Phase.Sync).ToString()));
			return newState;
		}

		private void UpdateSettings (Dictionary<string, string> settings)
		{
			if (settings.ContainsKey(playPhaseStartDelayKey))
				playPhaseStartDelay = int.Parse(settings[playPhaseStartDelayKey]);
			if (settings.ContainsKey(turnDurationKey))
				turnDuration = int.Parse(settings[turnDurationKey]) + 2;
			if (settings.ContainsKey(endTurnDelayKey))
				endTurnDelay = int.Parse(settings[endTurnDelayKey]);
			if (settings.ContainsKey(targetVictoryPointsKey))
				targetVictoryPoints = int.Parse(settings[targetVictoryPointsKey]);
		}

		private DateTime ExtractTime (string key, StateRegistry state)
		{
			if (state != null && state.HasPublicProperty(key) && DateTime.TryParse(state.GetPublic(key), out DateTime time))
				return time.ToUniversalTime();
			return default;
		}

		private bool IsInPlayPhase (StateRegistry state)
		{
			if (state == null)
				return false;
			return DateTime.UtcNow > ExtractTime(playPhaseStartTimeKey, state) && DateTime.UtcNow < ExtractTime(playPhaseEndTimeKey, state);
		}

		private bool HasBothPlayersSentTheirMoves (string[] players, StateRegistry state)
		{
			int counter = 0;
			foreach (var player in players)
				if (player[0] == 'X' || !string.IsNullOrEmpty(state.GetPrivate(player, myMoveKey)))
					counter++;
			return counter == players.Length;
		}

		private void CheckRpsLogic (StateRegistry state)
		{
			string[] playerList = state.GetPlayers();
			string p1Move = state.GetPrivate(playerList[0], myMoveKey);
			string p2Move = state.GetPrivate(playerList[1], myMoveKey);
			int turnWinner = GetWinner(p1Move, p2Move);
			int p1Score = int.Parse(state.GetPrivate(playerList[0], myScoreKey)) + Math.Max(turnWinner * -1, 0);
			int p2Score = int.Parse(state.GetPrivate(playerList[1], myScoreKey)) + Math.Max(turnWinner, 0);
			state.UpsertPrivateProperties(
				(playerList[0], opponentMoveKey, p2Move),
				(playerList[1], opponentMoveKey, p1Move),
				(playerList[0], turnWinnerKey, turnWinner == 1 ? "Opponent" : turnWinner == 0 ? "Tie" : "Me"),
				(playerList[1], turnWinnerKey, turnWinner == 1 ? "Me" : turnWinner == 0 ? "Tie" : "Opponent"),
				(playerList[0], myScoreKey, p1Score.ToString()),
				(playerList[1], myScoreKey, p2Score.ToString()),
				(playerList[0], opponentScoreKey, p2Score.ToString()),
				(playerList[1], opponentScoreKey, p1Score.ToString()));
			state.UpsertPublicProperties(
				(phaseKey, ((int)Phase.Result).ToString()),
				(turnEndTimeKey, DateTime.UtcNow.AddSeconds(endTurnDelay).ToString("u")));

			int GetWinner (string move1, string move2)
			{
				switch (move1)
				{
					case "ROCK":
						switch (move2) { case "ROCK": return 0; case "PAPER": return 1; case "SCISSORS": case "": return -1; }
						break;
					case "PAPER":
						switch (move2) { case "ROCK": case "": return -1; case "PAPER": return 0; case "SCISSORS": return 1; }
						break;
					case "SCISSORS":
						switch (move2) { case "ROCK": return 1; case "PAPER": case "": return -1; case "SCISSORS": return 0; }
						break;
					case "":
						if (move2 != "")
							return 1;
						break;
				}
				return 0;
			}
		}

		private void ApplyBotMoves (MatchRegistry match, StateRegistry lastState)
		{
			if (!match.HasBots)
				return;

			foreach (string id in match.PlayerIds)
			{
				if (id[0] == 'X')
					lastState.UpsertPrivateProperties((id, myMoveKey, GetBotMove()));
			}

			string GetBotMove ()
			{
				//allowedMoves[rand.Next(0, allowedMoves.Length)];
				if (currentMove < 0)
					currentMove = rand.Next(0, humanMoves.Length);
				char move = humanMoves[currentMove];
				currentMove = (currentMove + 1) % humanMoves.Length;
				if (move == 'R')
					return "ROCK";
				if (move == 'P')
					return "PAPER";
				return "SCISSORS";
			}
		}

		private void CheckMatchEnded (StateRegistry state)
		{
			string[] playerList = state.GetPlayers();
			int p1Score = int.Parse(state.GetPrivate(playerList[0], myScoreKey));
			int p2Score = int.Parse(state.GetPrivate(playerList[1], myScoreKey));
			int matchWinner = (p1Score >= targetVictoryPoints) ? -1 : (p2Score >= targetVictoryPoints) ? 1 : 0;
			if (matchWinner != 0)
			{
				state.IsMatchEnded = true;
				state.UpsertPrivateProperties(
					(playerList[0], matchWinnerKey, matchWinner == 1 ? "Opponent" : "Me"),
					(playerList[1], matchWinnerKey, matchWinner == 1 ? "Me" : "Opponent"));
				state.UpsertPublicProperties((phaseKey, ((int)Phase.Ended).ToString()));
			}
		}

		private enum Phase
		{
			Sync,
			Play,
			Result,
			Ended,
		}
	}
}