using Kalkatos.Network.Model;
using System;
using System.Collections.Generic;

namespace Kalkatos.Network.Registry;

public class AuthenticationEntry
{
	public string PlayerId;
	public string Provider;
	public string AuthTicket;
	public Dictionary<string, string> Data;
	public UserInfo UserInfo;
	public AuthStatus Status;
	public string StatusDescription;
	public DateTimeOffset CreationDate;

	internal void SetStatus (AuthStatus status)
	{
		Status = status;
		StatusDescription = status.ToString();
	}

	public AuthenticationEntry ()
	{
		UserInfo = new();
		Data = new();
		CreationDate = DateTime.UtcNow;
	}
}

public enum AuthStatus
{
	Unknown = 0,
	WaitingAuthentication = 1,
	Processing = 2,
	Failed = 3,
	Granted = 4,
	Concluded = 5
}
