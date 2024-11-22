using System;

namespace Kalkatos.FunctionsGame.Registry
{
	public class MatchmakingEntry
    {
        public string Region;
        public string PlayerId;
		public string PlayerInfoSerialized;
		public string MatchId;
		public string Alias;
		public bool UseLobby;
        public MatchmakingStatus Status;
		public DateTime Timestamp;
	}

	public enum MatchmakingStatus
	{
		Undefined = -1,
		Searching = 0,
		Matched = 1,
		Backfilling = 2,
		Failed = 3,
		FailedWithNoPlayers = 4,
		Canceled = 5,
		InLobby = 6,
	}
}
