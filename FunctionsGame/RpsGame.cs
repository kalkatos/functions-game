using System;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Game.Rps
{
	public class RpsGame : IGame
	{
		private readonly string[] allowedMoves = new string[] { "ROCK", "PAPER", "SCISSORS" };
		private int firstTurnDelay = 30;
		private int playPhaseStartDelay = 5;
		private int turnDuration = 10;
		private int endTurnDelay = 7;
		private int targetVictoryPoints = 2;
		private Random rand = new Random();
		private const string humanMoves = "SPPRRSSPRSPRPSPRSRPSRPRPSPPRPSPSPRPSPRPSPSRPPSPRPSPSPSRPSRPSRPSRPSRPSPRPRPSPSRPSPRSRPSRPSRPSPPRSPRSRPSPRPRPRPSPRPSPRSPRPSRP";
		private int currentMove = -1;

		// Config Keys
		private const string firstTurnDelayKey = "FirstTurnDelay";
		private const string playPhaseStartDelayKey = "PlayPhaseStartDelay";
		private const string turnDurationKey = "TurnDuration";
		private const string endTurnDelayKey = "EndTurnDelay";
		private const string targetVictoryPointsKey = "TargetVictoryPoints";
		/*
		{
			"FirstTurnDelay":"30",
			"PlayPhaseStartDelay":"5",
			"TurnDuration":"10",
			"EndTurnDelay":"7",
			"TargetVictoryPoints":"2"
		}
		*/
		// Game Keys
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

		public string GameId { get => "rps"; }

		public void SetConfig (Dictionary<string, string> config)
		{
			if (config.ContainsKey(firstTurnDelayKey))
				firstTurnDelay = int.Parse(config[firstTurnDelayKey]);
			if (config.ContainsKey(playPhaseStartDelayKey))
				playPhaseStartDelay = int.Parse(config[playPhaseStartDelayKey]);
			if (config.ContainsKey(turnDurationKey))
				turnDuration = int.Parse(config[turnDurationKey]);
			if (config.ContainsKey(endTurnDelayKey))
				endTurnDelay = int.Parse(config[endTurnDelayKey]);
			if (config.ContainsKey(targetVictoryPointsKey))
				targetVictoryPoints = int.Parse(config[targetVictoryPointsKey]);
		}

		public bool IsActionAllowed (string playerId, StateInfo stateChanges, MatchRegistry match, StateRegistry state)
		{
			bool result = !stateChanges.HasAnyPublicProperty();
			result &= stateChanges.OnlyHasThesePrivateProperties(handshakingKey, myMoveKey);
			if (IsInPlayPhase(state))
				result &= stateChanges.IsPrivatePropertyEqualsIfPresent(myMoveKey, allowedMoves);
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
								(playPhaseEndTimeKey, utcNow.AddSeconds(turnDuration).ToString("u")));
						return newState;
					case (int)Phase.Play:
						if (utcNow >= playEndTime)
						{
							ApplyBotMoves(match, newState);
							CheckRpsLogic(newState);
						}
						return newState;
					case (int)Phase.Result:
						if (utcNow < turnEndTime)
							return lastState;
						isNewMatch = false;
						break;
					case (int)Phase.Ended:

						break;
					default:
						Logger.LogError("   [RPS] Unknown value of phase");
						break;
				}
			}
			else
				newState = new StateRegistry(match.PlayerIds);
			// New turn
			if (isNewMatch)
			{
				newState.UpsertAllPrivateProperties((myMoveKey, ""), (opponentMoveKey, ""), (winnerKey, ""), (myScoreKey, "0"), (opponentScoreKey, "0"));
				newState.UpsertPublicProperties(
					(playPhaseStartTimeKey, DateTime.UtcNow.AddSeconds(firstTurnDelay).ToString("u")),
					(playPhaseEndTimeKey, DateTime.UtcNow.AddSeconds(firstTurnDelay + turnDuration).ToString("u")),
					(turnEndTimeKey, DateTime.UtcNow.AddSeconds(firstTurnDelay + turnDuration + endTurnDelay).ToString("u")),
					(phaseKey, ((int)Phase.Sync).ToString()));
			}
			else
			{
				newState.UpsertAllPrivateProperties((myMoveKey, ""), (opponentMoveKey, ""), (winnerKey, ""));
				newState.UpsertPublicProperties(
					(playPhaseStartTimeKey, DateTime.UtcNow.AddSeconds(playPhaseStartDelay).ToString("u")),
					(playPhaseEndTimeKey, DateTime.UtcNow.AddSeconds(playPhaseStartDelay + turnDuration).ToString("u")),
					(turnEndTimeKey, DateTime.UtcNow.AddSeconds(playPhaseStartDelay + turnDuration + endTurnDelay).ToString("u")),
					(phaseKey, ((int)Phase.Sync).ToString()));
			}
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

		private enum Phase
		{
			Sync,
			Play,
			Result,
			Ended,
		}
	}
}