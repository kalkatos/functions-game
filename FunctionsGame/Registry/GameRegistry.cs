using System.Collections.Generic;

namespace Kalkatos.FunctionsGame.Registry
{
    public class GameRegistry
    {
        public int FirstCheckMatchDelay = 45;
        public int RecurrentCheckMatchDelay = 30;
        public int FinalCheckMatchDelay = 30;
        public Dictionary<string, string> DefaultPlayerCustomData;
        public Dictionary<string, string> Settings;
        public Dictionary<string, string> BotSettings;

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
