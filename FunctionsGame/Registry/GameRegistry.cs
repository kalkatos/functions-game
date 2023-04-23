using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Registry
{
	public class GameRegistry
    {
        public int FirstCheckMatchDelay;
        public int RecurrentCheckMatchDelay;
        public int FinalCheckMatchDelay;
        public Dictionary<string, string> DefaultPlayerCustomData;
        public Dictionary<string, string> Settings;

        public bool HasSetting (string key)
        { 
            return Settings.ContainsKey (key); 
        }

        public string GetValue (string key)
        {
            if (HasSetting(key))
                return Settings[key];
            return null;
        }
    }
}
