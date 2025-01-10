using Kalkatos.Network.Registry;
using Kalkatos.Network.Model;
using System;
using System.Collections.Generic;

namespace Kalkatos.Network.Rps;

public class RpsGame : IGame
{
	private readonly string[] allowedMoves = ["R", "P", "S", "_"];
	private int maxTurnDuration = 20;
	private int endTurnDelay = 15;
	private int playerTimeout = 20;
	private int targetVictoryPoints = 2;
	private const string humanMoves = "SPPRRSSPRPSPSPRPSPRSRSSPRRSPPRSPRPPRSPRPSRPPPSRPSRPSRPPRSPRPSPRSRPSRPPRRPSPRSRPSRPPRPSPPRPRPSPRPSPRSPRPSRP";
	private int currentMove = -1;
	private GameRegistry settings;

	// Config Keys
	private const string turnDurationKey = "TurnDuration";
	private const string endTurnDelayKey = "EndTurnDelay";
	private const string playerTimeoutKey = "PlayerTimeout";
	private const string targetVictoryPointsKey = "TargetVictoryPoints";
	// Game Keys
	private const string phaseKey = "Phase";
	private const string myMoveKey = "MyMove";
	private const string retreatedPlayersKey = "Retreated";
	private const string turnStartedTimeKey = "TurnStartedTime";
	private const string turnEndedTimeKey = "TurnEndedTime";
	private const string opponentMoveKey = "OpponentMove";
	private const string turnWinnerKey = "TurnWinner";
	private const string matchWinnerKey = "MatchWinner";
	private const string myScoreKey = "MyScore";
	private const string opponentScoreKey = "OpponentScore";

	// ████████████████████████████████████████████ P U B L I C ████████████████████████████████████████████

	public string Name => "rps";
	public GameRegistry Settings => settings;

	private Random Rand => Global.Random;

	public void SetSettings (GameRegistry gameRegistry)
	{
		settings = gameRegistry;
		if (gameRegistry.Settings == null)
			return;
		Dictionary<string, string> settingsDict = gameRegistry.Settings;
		if (settingsDict.ContainsKey(turnDurationKey))
			maxTurnDuration = int.Parse(settingsDict[turnDurationKey]);
		if (settingsDict.ContainsKey(endTurnDelayKey))
			endTurnDelay = int.Parse(settingsDict[endTurnDelayKey]);
		if (settingsDict.ContainsKey(playerTimeoutKey))
			playerTimeout = int.Parse(settingsDict[playerTimeoutKey]);
		if (settingsDict.ContainsKey(targetVictoryPointsKey))
			targetVictoryPoints = int.Parse(settingsDict[targetVictoryPointsKey]);
	}

	public bool IsActionAllowed (string playerId, ActionInfo action, MatchRegistry match, StateRegistry state)
	{
		bool result = !action.HasAnyPublicChange();
		result &= action.OnlyHasThesePrivateChanges(myMoveKey);
		if (IsInPlayPhase(state))
			result &= action.IsPrivateChangeEqualsIfPresent(myMoveKey, allowedMoves);
		return result;
	}

	public StateRegistry CreateFirstState (MatchRegistry match)
	{
		StateRegistry newState = new StateRegistry(match.PlayerIds);
		newState.UpsertProperties(
			publicProperties:
			[
				(turnStartedTimeKey, DateTimeOffset.MinValue.ToString("u")),
				(turnEndedTimeKey, DateTimeOffset.MaxValue.ToString("u")),
				(phaseKey, ((int)Phase.Sync).ToString())
			],
			allPrivateProperties:
			[
				(myMoveKey, ""), (opponentMoveKey, ""), (turnWinnerKey, ""), (myScoreKey, "0"),
				(opponentScoreKey, "0"), (matchWinnerKey, "")
			]);
		return newState;
	}

	public StateRegistry PrepareTurn (string playerId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions)
	{
		StateRegistry newState = lastState.Clone();
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;

		if (newState.HasAnyPrivatePropertyWithValue(retreatedPlayersKey, "1"))
		{
			newState.IsMatchEnded = true;
			return newState;
		}

		if (IsMatchEnded(newState))
			return newState;

		// Treat actions
		foreach (ActionRegistry item in actions)
		{
			if (item.IsProcessed)
				continue;
			newState.UpsertPublicProperties(item.Action.PublicChanges);
			newState.UpsertPrivateProperties(item.PlayerId, item.Action.PrivateChanges);
			item.IsProcessed = true;
		}

		DateTimeOffset turnStartedTime = newState.GetTimeFromPublic(turnStartedTimeKey).Value;
		DateTimeOffset turnEndedTime = newState.GetTimeFromPublic(turnEndedTimeKey).Value;
		int phase = 0;
		if (newState.HasPublicProperty(phaseKey))
			phase = int.Parse(newState.GetPublic(phaseKey));

		switch (phase)
		{
			case (int)Phase.Sync:
				if (newState.IsSyncdForAllPlayers())
				{
					newState.TurnNumber = 1;
					newState.UpsertProperties(
						publicProperties:
						[
							(phaseKey, ((int)Phase.Play).ToString()),
							(turnStartedTimeKey, utcNow.ToString("u"))
						],
						clearSync: true);
				}
				else
				{
					newState.Sync(playerId);
					foreach (string id in match.PlayerIds)
						if (id[0] == 'X')
							newState.Sync(id);
				}
				return newState;
			case (int)Phase.Play:
				if (HasBothPlayersSentTheirMoves(match.PlayerIds, newState))
				{
					ApplyBotMoves(match, newState);
					CheckRpsLogic(newState);
					return newState;
				}
				break;
			case (int)Phase.Result:
				if (newState.IsSyncdForAllPlayers())
				{
					if (IsMatchEnded(newState))
						return newState;

					// New turn
					newState.TurnNumber++;
					newState.UpsertProperties(
						allPrivateProperties: [
							(myMoveKey, ""), (opponentMoveKey, ""), (turnWinnerKey, "") ],
						publicProperties:
						[
							(turnStartedTimeKey, DateTimeOffset.UtcNow.ToString("u")),
							(phaseKey, ((int)Phase.Play).ToString())
						],
						clearSync: true);
					return newState;
				}
				else
				{
					newState.Sync(playerId);
					foreach (string id in match.PlayerIds)
						if (id[0] == 'X')
							newState.Sync(id);
				}
				return newState;
			case (int)Phase.Ended:
				return lastState;
			default:
				Logger.LogError("   [RPS] Unknown value of phase");
				break;
		}
		return newState;
	}

	public PlayerInfo CreateBot (Dictionary<string, string> settings)
	{
		Random rand = Rand;
		int numberOfImages = 10;
		var customData = new Dictionary<string, string> { { "Avatar", rand.Next(13).ToString() } };
		string randomImageIndex = rand.Next(numberOfImages).ToString();
		string[] indexes = [randomImageIndex, randomImageIndex, randomImageIndex];
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
			idPrivateProperties: new (string id, string key, string value)[]
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
				(turnEndedTimeKey, DateTimeOffset.UtcNow.ToString("u"))
			},
			clearSync: true);

		int GetWinner (string move1, string move2)
		{
			switch (move1)
			{
				case "R":
					return move2 switch { "R" => 0, "P" => 1, _ => -1 };
				case "P":
					return move2 switch { "P" => 0, "S" => 1, _ => -1 };
				case "S":
					return move2 switch { "S" => 0, "R" => 1, _ => -1 };
				default:
					if (move2 == "R" || move2 == "P" || move2 == "S")
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
			return move.ToString();
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

	private void EndMatch (StateRegistry state, int matchWinner, bool hasPlayerLeft = false)
	{
		string[] playerList = state.GetPlayers();
		state.IsMatchEnded = true;
		state.UpsertProperties(
			idPrivateProperties:
			[
				(playerList[0], matchWinnerKey, (matchWinner == 0 ? "Tie" : (matchWinner == 1 ? "Opponent" : (hasPlayerLeft ? "OppRetreat" : "Me")))),
				(playerList[1], matchWinnerKey, (matchWinner == 0 ? "Tie" : (matchWinner == -1 ? "Opponent" : (hasPlayerLeft ? "OppRetreat" : "Me"))))
			],
			publicProperties: [(phaseKey, ((int)Phase.Ended).ToString())]);
	}

	private enum Phase
	{
		Sync,
		Play,
		Result,
		Ended,
	}
}