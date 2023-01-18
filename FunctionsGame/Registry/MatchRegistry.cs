using System;

namespace Kalkatos.FunctionsGame.Registry
{
    internal class MatchRegistry
    {
        public string MatchId;
        public string[] Players;
        public bool IsEnded;
        public DateTime CreatedTime;
        public DateTime LastUpdatedTime;
        public DateTime EndedTime;
    }
}
