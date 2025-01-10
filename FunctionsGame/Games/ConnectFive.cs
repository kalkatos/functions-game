using Kalkatos.Network.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kalkatos.Network.ConnectFive;

public class ConnectFive : IGame
{
	private const string PLAYER_ORDER = "GBPY";

	private const string TURN_PHASE_KEY = "TurnPhase";
	private const string TURN_START_TIME_KEY = "TurnStartTime";
	private const string TURN_END_TIME_KEY = "TurnEndTime";
	private const string IS_ENDED_KEY = "IsEnded";
	private const string PLAYERS_ON_KEY = "PlayersOn";
	private const string CURRENT_PLAYER_KEY = "CurrentPlayer";
	private const string CURRENT_MOVE_KEY = "CurrentMove";
	private const string PIECE_AMOUNT_KEY = "PieceAmount_";
	private const string PIECE_AMOUNT_G_KEY = "PieceAmount_G";
	private const string PIECE_AMOUNT_B_KEY = "PieceAmount_B";
	private const string PIECE_AMOUNT_P_KEY = "PieceAmount_P";
	private const string PIECE_AMOUNT_Y_KEY = "PieceAmount_Y";
	private const string GRID_KEY = "Grid";
	private const string MY_COLOR_KEY = "MyColor";
	private const string MY_MOVE_KEY = "MyMove";
	private const string TURN_DURATION_KEY = "TurnDuration";
	private const string OBJECTIVE_PIECE_COUNT_KEY = "ObjectivePieceCount";
	private const string STARTING_PIECE_COUNT_KEY = "PieceAmount";
	private const string MAX_WAIT_FOR_SYNC_KEY = "MaxWaitForSync";

	private Random rand = Global.Random;
	private GameRegistry settings;
	private int turnDuration = 30;
	private int objectivePieceCount = 5;
	private int piecesPerPlayer = 15;
	private int maxWaitForSync = 20;

	private static char[] separators = [',', '_'];

	private enum TurnPhase { GameStart = 0, Play = 1, Result = 2, Ended = 3 }

	public string Name => "ConnectFive";
	public GameRegistry Settings => settings;

	public PlayerInfo CreateBot (Dictionary<string, string> settings)
	{
		return null;
	}

	public void SetSettings (GameRegistry settings)
	{
		if (settings.Settings?.TryGetValue(TURN_DURATION_KEY, out string turnDurationStr) ?? false)
			turnDuration = int.Parse(turnDurationStr);
		if (settings.Settings?.TryGetValue(OBJECTIVE_PIECE_COUNT_KEY, out string objPieceCountStr) ?? false)
			objectivePieceCount = int.Parse(objPieceCountStr);
		if (settings.Settings?.TryGetValue(STARTING_PIECE_COUNT_KEY, out string pieceAmountStr) ?? false)
			piecesPerPlayer = int.Parse(pieceAmountStr);
		if (settings.Settings?.TryGetValue(MAX_WAIT_FOR_SYNC_KEY, out string maxWaitForSyncStr) ?? false)
			maxWaitForSync = int.Parse(maxWaitForSyncStr);
		this.settings = settings;
	}

	public StateRegistry CreateFirstState (MatchRegistry match)
	{
		StateRegistry newState = new StateRegistry(match.PlayerIds);
		int playerAmount = Math.Min(4, match.PlayerIds.Length);
		string playersOn = PLAYER_ORDER.Substring(0, playerAmount);
		newState.UpsertProperties(
			publicProperties:
			[
				(PLAYERS_ON_KEY, playersOn),
				(IS_ENDED_KEY,  ""),
				(TURN_START_TIME_KEY,  DateTimeOffset.MaxValue.ToString("u")),
				(TURN_END_TIME_KEY, DateTimeOffset.MaxValue.ToString("u")),
				(TURN_PHASE_KEY, ((int)TurnPhase.GameStart).ToString()),
				(CURRENT_PLAYER_KEY, playersOn[rand.Next(0, playersOn.Length)].ToString()),
				(CURRENT_MOVE_KEY, ""),
				(GRID_KEY, ""),
				(PIECE_AMOUNT_G_KEY, piecesPerPlayer.ToString()),
				(PIECE_AMOUNT_B_KEY, piecesPerPlayer.ToString()),
				(PIECE_AMOUNT_P_KEY, piecesPerPlayer.ToString()),
				(PIECE_AMOUNT_Y_KEY, piecesPerPlayer.ToString())
			]);
		for (int i = 0; i < match.PlayerIds.Length; i++)
		{
			string id = match.PlayerIds[i];
			string color = playersOn[i].ToString();
			newState.UpsertPrivateProperties(
				(id, MY_COLOR_KEY, color));
		}
		return newState;
	}

	public bool IsActionAllowed (string playerId, ActionInfo action, MatchRegistry match, StateRegistry state)
	{
		// Move will come with one of the following formats:
		//      1 - When a player puts a new piece:      "0,1_G"          <grid_position>_<player_color>
		//      2 - When a player moves a piece:          "0,1_G_3,-1"    <grid_position_placed>_<player_color>_<grid_position_removed>
		if (!action.OnlyHasThesePrivateChanges(MY_MOVE_KEY))
		{
			Logger.LogError("Action is not allowed because contains changes different than allowed move");
			return false;
		}
		string color = state.GetPrivate(playerId, MY_COLOR_KEY);
		string currentPlayer = state.GetPublic(CURRENT_PLAYER_KEY);
		if (color != currentPlayer)
		{
			Logger.LogError($"Action not allowed because player {color} is not allowed to send move. Current {currentPlayer}");
			return false;
		}
		string move = action.PrivateChanges[MY_MOVE_KEY];
		Cell? parsedMove = ToCell(move);
		if (!parsedMove.HasValue)
		{
			Logger.LogError($"Action not allowed because move is not in correct format: {move}");
			return false;
		}
		string playersOn = state.GetPublic(PLAYERS_ON_KEY);
		if (!playersOn.Contains(parsedMove.Value.Color))
		{
			Logger.LogError($"Action not allowed because players on {playersOn} does not include {parsedMove.Value.Color}");
			return false;
		}
		string pieceAmountKey = $"{PIECE_AMOUNT_KEY}{color}";
		int pieceAmount = int.Parse(state.GetPublic(pieceAmountKey));
		if (parsedMove.Value.HasOld ^ pieceAmount <= 0)
		{
			Logger.LogError($"Action not allowed because either is moving while still have pieces OR does not have pieces but is not moving");
			return false;
		}
		string grid = state.GetPublic(GRID_KEY);
		if (!IsCorrectGridPosition(parsedMove.Value, grid))
			return false;
		if (parsedMove.Value.HasOld)
		{
			if (parsedMove.Value.X == parsedMove.Value.OldX && parsedMove.Value.Y == parsedMove.Value.OldY)
			{
				Logger.LogError($"Action not allowed because new and old positions are equal");
				return false;
			}
			if (!grid.Contains($"|{parsedMove.Value.OldX},{parsedMove.Value.OldY}_{parsedMove.Value.Color}|"))
			{
				Logger.LogError($"Action not allowed because old position is inconsistent");
				return false;
			}
			if (IsMoveLeavingIsolatedPieces(parsedMove.Value, grid))
			{
				Logger.LogError($"Action not allowed because move is leaving isolated pieces");
				return false;
			}
		}
		return true;

		bool IsCorrectGridPosition (Cell cell, string grid)
		{
			string moveInGrid = $"|{cell.X},{cell.Y}_";
			if (grid.Contains(moveInGrid))
			{
				Logger.LogError($"Action not allowed because grid already has piece in position {cell.X},{cell.Y}");
				return false;
			}
			bool isFirstMove = string.IsNullOrEmpty(grid);
			if (isFirstMove)
			{
				if (cell.X == 0 && cell.Y == 0)
					return true;
				Logger.LogError($"Action not allowed because first move is not 0,0");
				return false;
			}
			int adjCount = CountAdjacentPieces(cell.X, cell.Y, grid);
			if (adjCount == 0)
			{
				Logger.LogError($"Action not allowed because there is no piece adjacent to position {cell.X},{cell.Y}");
				return false;
			}
			if (cell.HasOld)
			{
				if (adjCount == 1 && AreAdjacentPositions(cell.X, cell.Y, cell.OldX, cell.OldY))
				{
					Logger.LogError($"Action not allowed because piece in new position will be left isolated");
					return false;
				}
			}
			return true;
		}

		bool IsMoveLeavingIsolatedPieces (Cell cell, string grid)
		{
			(int x, int y)[] oldPosAdjacencies = new[]
			{
				(cell.OldX - 1, cell.OldY),
				(cell.OldX, cell.OldY + 1),
				(cell.OldX + 1, cell.OldY),
				(cell.OldX, cell.OldY - 1)
			};
			foreach (var adj in oldPosAdjacencies)
			{
				if (!grid.Contains($"|{adj.x},{adj.y}_"))
					continue;
				if (CountAdjacentPieces(adj.x, adj.y, grid) <= 1 && !AreAdjacentPositions(adj.x, adj.y, cell.X, cell.Y))
					return true;
			}
			return false;

		}

		int CountAdjacentPieces (int x, int y, string grid)
		{
			string[] adjacentPos = new[]
			{
				$"|{x - 1},{y}_",
				$"|{x},{y + 1}_",
				$"|{x + 1},{y}_",
				$"|{x},{y - 1}_"
			};
			return adjacentPos.Count(grid.Contains);
		}

		bool AreAdjacentPositions (int x1, int y1, int x2, int y2)
		{
			return (Math.Abs(x1 - x2) == 1 ^ Math.Abs(y1 - y2) == 1) && (x1 == x2 ^ y1 == y2);
		}
	}

	public StateRegistry PrepareTurn (string playerId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions)
	{
		StateRegistry newState = lastState.Clone();
		string currentPlayer = newState.GetPublic(CURRENT_PLAYER_KEY);
		TurnPhase turnPhase = (TurnPhase)int.Parse(newState.GetPublic(TURN_PHASE_KEY));

		DateTimeOffset turnStartTime = newState.GetTimeFromPublic(TURN_START_TIME_KEY).Value;
		DateTimeOffset now = DateTimeOffset.UtcNow;
		if (turnStartTime < now
			&& turnPhase == TurnPhase.Play
			&& (now - turnStartTime).TotalSeconds > turnDuration + 5)
		{
			newState.IsMatchEnded = true;
			newState.UpsertPublicProperty(IS_ENDED_KEY, $"TimeOut:{currentPlayer}");
			LogError($"State Changed:     TIME OUT\n{JsonConvert.SerializeObject(newState, Formatting.Indented)}");
			return newState;
		}

		if (newState.HasAnyPrivatePropertyWithValue(Global.RETREATED_KEY, "1"))
		{
			var players = newState.GetPlayers();
			foreach (var id in players)
			{
				if (newState.TryGetPrivate(id, Global.RETREATED_KEY, out string value) && value == "1")
				{
					newState.IsMatchEnded = true;
					newState.UpsertPublicProperty(IS_ENDED_KEY, $"Retreated:{newState.GetPrivate(id, MY_COLOR_KEY)}");
					LogError($"State Changed:     RETREAT\n{JsonConvert.SerializeObject(newState, Formatting.Indented)}");
					return newState;
				}
			}
		}

		string playersOn = newState.GetPublic(PLAYERS_ON_KEY);
		string playerColor = newState.GetPrivate(playerId, MY_COLOR_KEY);
		switch (turnPhase)
		{
			case TurnPhase.GameStart:
				if (newState.IsSyncdForAllPlayers())
				{
					newState.UpsertProperties(
						publicProperties:
						[
							(TURN_PHASE_KEY, ((int)TurnPhase.Play).ToString()),
							(TURN_START_TIME_KEY, DateTimeOffset.UtcNow.ToString("u"))
						],
						clearSync: true);
					LogError($"State Changed:     GAME START\n{JsonConvert.SerializeObject(newState, Formatting.Indented)}");
					return newState;
				}
				else if (!newState.IsPlayerSyncd(playerId))
				{
					LogError($"Player {playerId} syncd for turn {newState.TurnNumber}");
					newState.Sync(playerId);
				}
				break;
			case TurnPhase.Play:
				if (playerColor == currentPlayer
					&& actions != null
					&& actions.Count > 0)
				{
					foreach (var action in actions)
					{
						if (action.PlayerId != playerId || action.IsProcessed)
							continue;
						ActionInfo info = action.Action;
						if (!info.HasPrivateChange(MY_MOVE_KEY))
							continue;
						string move = info.PrivateChanges[MY_MOVE_KEY];
						string currentGrid = lastState.GetPublic(GRID_KEY);
						Cell cell = ToCell(move).Value;
						string placedPos = $"{cell.X},{cell.Y}_{cell.Color}";
						if (currentGrid.Contains($"|{placedPos}|"))
						{
							action.IsProcessed = true;
							continue;
						}
						string pieceAmountKey = $"{PIECE_AMOUNT_KEY}{playerColor}";
						int pieceAmount = int.Parse(newState.GetPublic(pieceAmountKey));
						if (cell.HasOld)
							currentGrid = currentGrid.Replace($"|{cell.OldX},{cell.OldY}_{cell.Color}", "");
						else
							pieceAmount--;
						string newGrid = string.IsNullOrEmpty(currentGrid) ?
							$"|{placedPos}|"
							: $"{currentGrid}{placedPos}|";
						string endedState = GetEndedStateFromGrid(newGrid);
						bool isEnded = !string.IsNullOrEmpty(endedState);
						string nextState;
						if (isEnded)
							nextState = ((int)TurnPhase.Ended).ToString();
						else
							nextState = ((int)TurnPhase.Result).ToString();

						newState.IsMatchEnded = isEnded;
						newState.UpsertProperties(
							publicProperties:
							[
								(GRID_KEY, newGrid),
								(IS_ENDED_KEY, endedState),
								(CURRENT_MOVE_KEY, move),
								(TURN_PHASE_KEY, nextState),
								(TURN_END_TIME_KEY, DateTimeOffset.UtcNow.ToString("u")),
								(pieceAmountKey, pieceAmount.ToString())
							],
							clearSync: true);
						action.IsProcessed = true;
						LogError($"State Changed:     PLAYER MOVE\n{JsonConvert.SerializeObject(newState, Formatting.Indented)}");
						break;
					}
				}
				break;
			case TurnPhase.Result:
				if (newState.IsSyncdForAllPlayers())
				{
					newState.TurnNumber++;
					int currentIndex = playersOn.IndexOf(currentPlayer);
					string nextPlayer = playersOn[(currentIndex + 1) % playersOn.Length].ToString();
					newState.UpsertProperties(
						publicProperties:
						[
						(CURRENT_PLAYER_KEY, nextPlayer),
						(TURN_START_TIME_KEY, DateTimeOffset.UtcNow.ToString("u")),
						(TURN_END_TIME_KEY, DateTimeOffset.MaxValue.ToString("u")),
						(CURRENT_MOVE_KEY, ""),
						(TURN_PHASE_KEY, ((int)TurnPhase.Play).ToString())
						],
						clearSync: true);
					LogError($"State Changed:     END TURN\n{JsonConvert.SerializeObject(newState, Formatting.Indented)}");
				}
				else
				{
					if (!newState.IsPlayerSyncd(playerId))
					{
						LogError($"Player {playerId} syncd for turn {newState.TurnNumber}");
						newState.Sync(playerId);
					}
					else if ((DateTimeOffset.UtcNow - DateTimeOffset.Parse(newState.GetPublic(TURN_END_TIME_KEY))).Seconds >= maxWaitForSync)
					{
						string[] unsyncdPlayers = newState.GetUnsyncdPlayers();
						if (unsyncdPlayers != null)
						{
							foreach (var player in unsyncdPlayers)
								newState.UpsertPrivateProperties((player, Global.RETREATED_KEY, "1"));
							LogError($"State Changed:     PLAYER FAILED SYNC\n{JsonConvert.SerializeObject(newState, Formatting.Indented)}");
						}
					}
				}
				break;
			default:
				break;
		}
		return newState;
	}

	private string GetEndedStateFromGrid (string grid)
	{
		if (string.IsNullOrEmpty(grid))
			return "";
		string[] gridCells = grid.Split('|', StringSplitOptions.RemoveEmptyEntries);
		string winningLine = "";
		foreach (var cellStr in gridCells)
		{
			Cell cell = ToCell(cellStr).Value;
			if (HasLine(cell, 1, 0)  // Horizontal
				|| HasLine(cell, 0, 1)  // Vertical
				|| HasLine(cell, 1, 1)  // Diagonal Right
				|| HasLine(cell, 1, -1))  // Diagonal Left
				return winningLine;
		}
		return "";

		bool HasLine (Cell cell, int xInc, int yInc)
		{
			bool hasLine =
				CountSameColorPiecesInDirection(cell, xInc, yInc, 0) +
				CountSameColorPiecesInDirection(cell, xInc * -1, yInc * -1, 0)
				>= objectivePieceCount - 1;
			if (hasLine)
				winningLine += $"{cell.X},{cell.Y}_{cell.Color}|";
			else
				winningLine = "";
			return hasLine;
		}

		int CountSameColorPiecesInDirection (Cell cell, int xInc, int yInc, int count)
		{
			Cell nextCell = new Cell(cell.X + xInc, cell.Y + yInc, cell.Color);
			if (grid.Contains($"|{nextCell.X},{nextCell.Y}_{nextCell.Color}"))
			{
				if (string.IsNullOrEmpty(winningLine))
					winningLine = $"|{nextCell.X},{nextCell.Y}_{nextCell.Color}|";
				else
					winningLine += $"{nextCell.X},{nextCell.Y}_{nextCell.Color}|";
				return CountSameColorPiecesInDirection(nextCell, xInc, yInc, count + 1);
			}
			return count;
		}
	}

	private Cell? ToCell (string cellStr)
	{
		string[] split = cellStr.Split(separators, StringSplitOptions.RemoveEmptyEntries);
		if (split.Length != 3 && split.Length != 5)
			return null;
		if (!int.TryParse(split[0], out int x) || !int.TryParse(split[1], out int y))
			return null;
		int oldX = -1000, oldY = -1000;
		bool hasOld = false;
		if (split.Length == 5)
		{
			hasOld = true;
			if (!int.TryParse(split[3], out oldX) || !int.TryParse(split[4], out oldY))
				return null;
		}
		if (split[2].Length != 1 || !PLAYER_ORDER.Contains(split[2]))
			return null;
		return new Cell(x, y, split[2], hasOld, oldX, oldY);
	}

	private void LogError (string message)
	{
		Logger.LogError($"[OK Play - PrepareTurn] {message}");
	}

	private struct Cell
	{
		public int X;
		public int Y;
		public string Color;
		public bool HasOld;
		public int OldX;
		public int OldY;

		public Cell (int x, int y, string color)
		{
			X = x;
			Y = y;
			Color = color;
		}

		public Cell (int x, int y, string color, bool hasOld, int oldX, int oldY)
		{
			X = x;
			Y = y;
			Color = color;
			HasOld = hasOld;
			OldX = oldX;
			OldY = oldY;
		}
	}
}
