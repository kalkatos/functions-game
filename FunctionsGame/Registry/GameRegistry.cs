using System.Collections.Generic;

namespace Kalkatos.Network.Registry;

public class GameRegistry
{
	public int CheckMatchDelay = 300;
	public int LobbyDuration = 3600;
	public int MinPlayersPerMatch = 2;
	public int MaxPlayersPerMatch = 2;
	public Dictionary<string, string> DefaultPlayerCustomData;
	public Dictionary<string, string> Settings;
	public Dictionary<string, string> BotSettings;

	public Dictionary<string, string> GetFullSettings ()
	{
		Dictionary<string, string> dict = null;
		if (Settings != null)
			dict = Settings;
		else
			dict = new();
		dict[nameof(CheckMatchDelay)] = CheckMatchDelay.ToString();
		dict[nameof(LobbyDuration)] = LobbyDuration.ToString();
		dict[nameof(MinPlayersPerMatch)] = MinPlayersPerMatch.ToString();
		dict[nameof(MaxPlayersPerMatch)] = MaxPlayersPerMatch.ToString();
		return dict;
	}

	public bool HasSetting (string key)
	{
		if (Settings == null)
			return false;
		return Settings.ContainsKey(key);
	}

	public string GetValue (string key)
	{
		if (HasSetting(key))
			return Settings[key];
		return null;
	}
}
