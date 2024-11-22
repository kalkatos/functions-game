using System;
using System.Linq;
using System.Collections.Generic;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame.Registry
{
	public class MatchRegistry
    {
        public string GameId;
        public string MatchId;
        public string Alias;
        public string[] PlayerIds;
        public PlayerInfo[] PlayerInfos;
		public string Region;
        public bool UseLobby;
        public bool IsStarted;
        public bool IsEnded;
        public bool HasBots;
        public DateTime CreatedTime;
        public DateTime StartTime;
        public DateTime EndedTime;
        public Dictionary<string, string> CustomData;

        public bool HasPlayer (string playerId)
        {
            foreach (var player in PlayerIds)
                if (player == playerId)
                    return true;
            return false;
        }

        public void AddPlayer (PlayerRegistry player)
        {
            PlayerIds = PlayerIds.Append(player.PlayerId).ToArray();
            PlayerInfos = PlayerInfos.Append(player.Info).ToArray();
            HasBots |= player.PlayerId[0] == 'X';
        }
    }
}
