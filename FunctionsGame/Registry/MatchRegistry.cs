using System;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame.Registry
{
	public class MatchRegistry
    {
        public string MatchId;
        public string[] PlayerIds;
        public PlayerInfo[] PlayerInfos;
		public string Region;
        public int Status;
        public bool HasBots;
        public DateTime CreatedTime;
        public DateTime StartTime;
        public DateTime EndedTime;

        public bool HasPlayer (string playerId)
        {
            foreach (var player in PlayerIds)
                if (player == playerId)
                    return true;
            return false;
        }
    }

    public enum MatchStatus
    {
        AwaitingPlayers,
        WaitingToStart,
        Started,
        FailedToStart,
        Ended
    }
}
