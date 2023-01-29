using Kalkatos.Network.Model;
using System;

namespace Kalkatos.FunctionsGame.Registry
{
	public class PlayerRegistry
    {
        public string PlayerId;
        public PlayerInfo Info;
        public string Region;
        public bool IsAuthenticated;
        public string[] Devices;
        public DateTime LastAccess;
        public DateTime FirstAccess;
        public DateTime TimeOfAuthentication;
    }
}
