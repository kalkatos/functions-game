using System;
using Kalkatos.Network.Model;

namespace Kalkatos.FunctionsGame.Registry
{
	public class MatchRegistry
    {
        public string MatchId;
        public string[] PlayerAliases;
        public string[] PlayerIds;
        public string Region;
        public bool IsEnded;
        public bool HasBots;
        public DateTime CreatedTime;
        public DateTime LastUpdatedTime;
        public DateTime EndedTime;
    }
}
