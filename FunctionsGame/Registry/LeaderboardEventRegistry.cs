using System.Collections.Generic;

namespace FunctionsGame.Registry
{
    public class LeaderboardEventRegistry
    {
        public string GameId;
        public string PlayerId;
        public string PlayerName;
        public string Key;
        public double Value;
        public Dictionary<string, string> Data;
    }
}
