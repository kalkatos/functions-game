﻿using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Rps
{
    public class RpsGame : IGame
	{
		private readonly string[] allowedMoves = new string[] { "ROCK", "PAPER", "SCISSORS", "NOTHING" };
		private int maxTurnDuration = 20;
		private int endTurnDelay = 15;
		private int playerTimeout = 20;
		private int targetVictoryPoints = 2;
		private const string humanMoves = "SPPRRSSPRPSPSPRPSPRSRSSPRRSPPRSPRPPRSPRPSRPPPSRPSRPSRPPRSPRPSPRSRPSRPPRRPSPRSRPSRPPRPSPPRPRPSPRPSPRSPRPSRP";
		private int currentMove = -1;
		private Random rand;

		// Config Keys
		private const string turnDurationKey = "TurnDuration";
		private const string endTurnDelayKey = "EndTurnDelay";
		private const string playerTimeoutKey = "PlayerTimeout";
		private const string targetVictoryPointsKey = "TargetVictoryPoints";
		// Game Keys
		private const string phaseKey = "Phase";
		private const string handshakingKey = "Handshaking";
		private const string myMoveKey = "MyMove";
		private const string retreatedPlayersKey = "Retreated";
		private const string turnResultSyncKey = "TurnResultSync";
		private const string turnStartedTimeKey = "TurnStartedTime";
		private const string turnEndedTimeKey = "TurnEndedTime";
		private const string opponentMoveKey = "OpponentMove";
		private const string turnWinnerKey = "TurnWinner";
		private const string matchWinnerKey = "MatchWinner";
		private const string myScoreKey = "MyScore";
		private const string opponentScoreKey = "OpponentScore";

		// ████████████████████████████████████████████ P U B L I C ████████████████████████████████████████████

		public string Name => "rps";

		private Random Rand
		{
			get
			{
				if (rand == null)
					rand = new Random();
				return rand;
			}
		}

		public void SetSettings (GameRegistry gameRegistry)
		{
			if (gameRegistry.Settings == null)
				return;
            Dictionary<string, string> settings = gameRegistry.Settings;
			if (settings.ContainsKey(turnDurationKey))
				maxTurnDuration = int.Parse(settings[turnDurationKey]);
			if (settings.ContainsKey(endTurnDelayKey))
				endTurnDelay = int.Parse(settings[endTurnDelayKey]);
			if (settings.ContainsKey(playerTimeoutKey))
				playerTimeout = int.Parse(settings[playerTimeoutKey]);
			if (settings.ContainsKey(targetVictoryPointsKey))
				targetVictoryPoints = int.Parse(settings[targetVictoryPointsKey]);
		}

		public bool IsActionAllowed (string playerId, ActionInfo action, MatchRegistry match, StateRegistry state)
		{
			bool result = !action.HasAnyPublicChange();
			result &= action.OnlyHasThesePrivateChanges(handshakingKey, myMoveKey);
			if (IsInPlayPhase(state))
				result &= action.IsPrivateChangeEqualsIfPresent(myMoveKey, allowedMoves);
			return result;
		}

		public StateRegistry CreateFirstState (MatchRegistry match)
		{
			StateRegistry newState = new StateRegistry(match.PlayerIds);
			newState.UpsertProperties(
				publicProperties: new (string key, string value)[]
				{
					(turnStartedTimeKey, DateTime.MinValue.ToString("u")),
					(turnEndedTimeKey, DateTime.MaxValue.ToString("u")),
					(phaseKey, ((int)Phase.Sync).ToString())
				},
				allPrivateProperties: new (string key, string value)[]
				{
					(handshakingKey, ""), (myMoveKey, ""), (opponentMoveKey, ""), (turnWinnerKey, ""), (myScoreKey, "0"),
					(opponentScoreKey, "0"), (matchWinnerKey, ""), (turnResultSyncKey, "0")
				});
			return newState;
		}

		public StateRegistry PrepareTurn (string playerId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions)
		{
			StateRegistry newState = lastState.Clone();
			DateTime utcNow = DateTime.UtcNow;
			bool isFirstTurn = newState.TurnNumber == 0;
			if (IsMatchEnded(newState))
				return newState;

			// Treat actions
			foreach (ActionRegistry item in actions)
			{
				newState.UpsertPublicProperties(item.Action.PublicChanges);
				newState.UpsertPrivateProperties(item.PlayerId, item.Action.PrivateChanges);
				item.IsProcessed = true;
			}

			if (!isFirstTurn)
			{
				DateTime turnStartedTime = ExtractTime(turnStartedTimeKey, lastState);
				DateTime turnEndedTime = ExtractTime(turnEndedTimeKey, lastState);

				int phase = 0;
				if (newState.HasPublicProperty(phaseKey))
					phase = int.Parse(newState.GetPublic(phaseKey));
				switch (phase)
				{
					case (int)Phase.Sync:
					case (int)Phase.Play:
						if (phase == (int)Phase.Sync)
							newState.UpsertPublicProperties((phaseKey, ((int)Phase.Play).ToString()));
						if (HasBothPlayersSentTheirMoves(match.PlayerIds, newState))
						{
							ApplyBotMoves(match, newState);
							CheckRpsLogic(newState);
							return newState;
						}
						else if ((utcNow - turnStartedTime).TotalSeconds >= maxTurnDuration + playerTimeout)
						{
							EndMatchWithRetreatedPlayer(newState, playerId);
							return newState;
						}
						else
							return newState;
					case (int)Phase.Result:
						if (!newState.HasPrivateProperty(playerId, turnResultSyncKey) || int.Parse(newState.GetPrivate(playerId, turnResultSyncKey)) < newState.TurnNumber)
							newState.UpsertPrivateProperties((playerId, turnResultSyncKey, newState.TurnNumber.ToString()));
						if (HasBothPlayersGotResult(newState))
						{
							break;
						}
						else if ((utcNow - turnEndedTime).TotalSeconds >= endTurnDelay + playerTimeout)
						{
							EndMatchWithRetreatedPlayer(newState, playerId);
							return newState;
						}
						else
							return newState;
					case (int)Phase.Ended:
						return lastState;
					default:
						Logger.LogError("   [RPS] Unknown value of phase");
						break;
				}
			}

			// New turn
			newState.TurnNumber++;
			newState.UpsertProperties(
				allPrivateProperties: new (string key, string value)[] { 
					(myMoveKey, ""), (opponentMoveKey, ""), (turnWinnerKey, "") },
				publicProperties: new (string key, string value)[]
				{
					(turnStartedTimeKey, DateTime.UtcNow.ToString("u")),

					(phaseKey, ((int)Phase.Sync).ToString())
				});
			return newState;
		}

		public PlayerInfo CreateBot (Dictionary<string, string> settings)
		{
			Random rand = Rand;
			int numberOfImages = 10;
			var customData = new Dictionary<string, string> { { "Avatar", rand.Next(13).ToString() } };
			string randomImageIndex = rand.Next(numberOfImages).ToString();
			string[] indexes = new string[] { randomImageIndex, randomImageIndex, randomImageIndex};
			int changeAmount = Math.Max(0, rand.Next(6) - 3);
			for (int i = 0; i < changeAmount; i++)
				indexes[rand.Next(3)] = rand.Next(numberOfImages).ToString();
			customData.Add("RockImage", indexes[0]);
			customData.Add("PaperImage", indexes[1]);
			customData.Add("ScissorsImage", indexes[2]);
			return new PlayerInfo
			{
				Alias = $"X{Guid.NewGuid()}",
				Nickname = Helper.GetRandomNickname_AdjectiveNoun(),
				CustomData = customData
			};
		}

		// ████████████████████████████████████████████ P R I V A T E ████████████████████████████████████████████

		private DateTime ExtractTime (string key, StateRegistry state)
		{
			if (state != null && state.HasPublicProperty(key) && DateTime.TryParse(state.GetPublic(key), out DateTime time))
				return time.ToUniversalTime();
			return default;
		}

		private bool IsInPlayPhase (StateRegistry state)
		{
			if (state == null || !state.HasPublicProperty(phaseKey))
				return false;
			int phase = int.Parse(state.GetPublic(phaseKey));
			return phase == (int)Phase.Sync || phase == (int)Phase.Play;
		}

		private bool HasBothPlayersSentTheirMoves (string[] players, StateRegistry state)
		{
			int counter = 0;
			foreach (var player in players)
				if (player[0] == 'X' || !string.IsNullOrEmpty(state.GetPrivate(player, myMoveKey)))
					counter++;
			return counter == players.Length;
		}

		private bool HasBothPlayersGotResult (StateRegistry state)
		{
			foreach (var id in state.GetPlayers())
			{
				if (id[0] == 'X')
					continue;
				if (!state.HasPrivateProperty(id, turnResultSyncKey))
					return false;
				if (int.Parse(state.GetPrivate(id, turnResultSyncKey)) < state.TurnNumber)
					return false;
			}
			return true;
		}

		private void CheckRpsLogic (StateRegistry state)
		{
			string[] playerList = state.GetPlayers();
			if (playerList.Length < 2)
				return;
			string p1Move = state.GetPrivate(playerList[0], myMoveKey);
			string p2Move = state.GetPrivate(playerList[1], myMoveKey);
			int turnWinner = GetWinner(p1Move, p2Move);
			int p1Score = int.Parse(state.GetPrivate(playerList[0], myScoreKey)) + Math.Max(turnWinner * -1, 0);
			int p2Score = int.Parse(state.GetPrivate(playerList[1], myScoreKey)) + Math.Max(turnWinner, 0);
			state.UpsertProperties(
				idPrivateProperties: new(string id, string key, string value)[]
				{
					(playerList[0], opponentMoveKey, p2Move),
					(playerList[1], opponentMoveKey, p1Move),
					(playerList[0], turnWinnerKey, turnWinner == 1 ? "Opponent" : turnWinner == 0 ? "Tie" : "Me"),
					(playerList[1], turnWinnerKey, turnWinner == 1 ? "Me" : turnWinner == 0 ? "Tie" : "Opponent"),
					(playerList[0], myScoreKey, p1Score.ToString()),
					(playerList[1], myScoreKey, p2Score.ToString()),
					(playerList[0], opponentScoreKey, p2Score.ToString()),
					(playerList[1], opponentScoreKey, p1Score.ToString())
				},
				publicProperties: new (string key, string value)[]
				{
					(phaseKey, ((int)Phase.Result).ToString()),
					(turnEndedTimeKey, DateTime.UtcNow.ToString("u"))
				});

			int GetWinner (string move1, string move2)
			{
				switch (move1)
				{
					case "ROCK":
						switch (move2) { case "ROCK": return 0; case "PAPER": return 1; case "SCISSORS": case "": case "NOTHING": return -1; }
						break;
					case "PAPER":
						switch (move2) { case "ROCK": case "": case "NOTHING": return -1; case "PAPER": return 0; case "SCISSORS": return 1; }
						break;
					case "SCISSORS":
						switch (move2) { case "ROCK": return 1; case "PAPER": case "": case "NOTHING": return -1; case "SCISSORS": return 0; }
						break;
					case "":
					case "NOTHING":
						if (move2 != "" && move2 != "NOTHING")
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
				if (currentMove < 0)
					currentMove = Rand.Next(0, humanMoves.Length);
				char move = humanMoves[currentMove];
				currentMove = (currentMove + 1) % humanMoves.Length;
				if (move == 'R')
					return "ROCK";
				if (move == 'P')
					return "PAPER";
				return "SCISSORS";
			}
		}

		private bool IsMatchEnded (StateRegistry state)
		{
			string[] playerList = state.GetPlayers();
			int p1Score = int.Parse(state.GetPrivate(playerList[0], myScoreKey));
			int p2Score = int.Parse(state.GetPrivate(playerList[1], myScoreKey));
			int matchWinner = (p1Score >= targetVictoryPoints) ? -1 : (p2Score >= targetVictoryPoints) ? 1 : 0;

			if (state.HasPrivateProperty(playerList[0], retreatedPlayersKey))
			{
				if (state.HasPrivateProperty(playerList[1], retreatedPlayersKey))
					EndMatch(state, 0, true);
				else
					EndMatch(state, 1, true);
				return true;
			}
			else if (state.HasPrivateProperty(playerList[1], retreatedPlayersKey))
			{
				EndMatch(state, -1, true);
				return true;
			}

			if (matchWinner != 0)
			{
				EndMatch(state, matchWinner);
				return true;
			}
			return false;
		}

		private void EndMatchWithRetreatedPlayer (StateRegistry state, string invoker)
		{
			string[] playerList = state.GetPlayers();
			int matchWinner = 0;
			if (int.Parse(state.GetPrivate(playerList[0], turnResultSyncKey)) == state.TurnNumber
				&& int.Parse(state.GetPrivate(playerList[1], turnResultSyncKey)) < state.TurnNumber)
				matchWinner = -1;
			else if (int.Parse(state.GetPrivate(playerList[0], turnResultSyncKey)) < state.TurnNumber
				&& int.Parse(state.GetPrivate(playerList[1], turnResultSyncKey)) == state.TurnNumber)
				matchWinner = 1;
			else if (playerList[0] == invoker)
				matchWinner = -1;
			else if (playerList[1] == invoker)
				matchWinner = 1;
			EndMatch(state, matchWinner, true);
		}

		private void EndMatch (StateRegistry state, int matchWinner, bool hasPlayerLeft = false)
		{
			string[] playerList = state.GetPlayers();
			state.IsMatchEnded = true;
			state.UpsertProperties(
				idPrivateProperties: new (string id, string key, string value)[]
				{
					(playerList[0], matchWinnerKey, (matchWinner == 0 ? "Tie" : (matchWinner == 1 ? "Opponent" : (hasPlayerLeft ? "OppRetreat" : "Me")))),
					(playerList[1], matchWinnerKey, (matchWinner == 0 ? "Tie" : (matchWinner == -1 ? "Opponent" : (hasPlayerLeft ? "OppRetreat" : "Me"))))
				},
				publicProperties: new (string key, string value)[] { (phaseKey, ((int)Phase.Ended).ToString()) });
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