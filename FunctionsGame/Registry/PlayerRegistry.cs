using System;

namespace Kalkatos.FunctionsGame.Registry
{
	internal class PlayerRegistry
    {
        public string PlayerId;
        public string PlayerAlias;
        public string Nickname;
        public string Region;
        public bool IsAuthenticated;
        public string[] Devices;
        public DateTime LastAccess;
        public DateTime FirstAccess;
        public DateTime TimeOfAuthentication;
    }
}
