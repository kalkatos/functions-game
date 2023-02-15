namespace Kalkatos.FunctionsGame
{
	public class MatchmakingRules
	{
		// Matchmaking
		public float DelayBetweenAttempts { get; set; }
		public int MaxAttempts { get; set; }
		public int MinPlayerCount { get; set; }
		public int MaxPlayerCount { get; set; }
		public bool HasBackfill { get; set; }
		public float WaitingTimeForBackfill { get; set; }
		public bool DoBackfillWithBots { get; set; }
		public MatchmakingNoPlayerAction ActionForNoPlayers { get; set; }
	}

	/*
	{
	"DelayBetweenAttempts":3,
	"MaxAttempts":3,
	"MinPlayerCount":2,
	"MaxPlayerCount":2,
	"HasBackfill":false,
	"WaitingTimeForBackfill":5,
	"DoBackfillWithBots":false,
	"ActionForNoPlayers":1,
	}
	*/

	public enum MatchmakingNoPlayerAction
	{
		ReturnFailed,
		MatchWithBots,
	}
}