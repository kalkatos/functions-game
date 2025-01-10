using Kalkatos.Network.Model;
using System;
using System.Collections.Generic;

namespace Kalkatos.Network.Registry;

public class PlayerRegistry
{
	public string PlayerId;
	public PlayerInfo Info;
	public string Region;
	public bool IsUsingAuthentication;
	public string[] Devices;
	public DateTimeOffset LastAccess;
	public DateTimeOffset FirstAccess;
	public DateTimeOffset TimeOfAuthentication;
	public UserInfo UserInfo;
}
