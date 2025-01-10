using Kalkatos.Network.Registry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Kalkatos.Network;

internal class GoogleAuthService : IAuthService
{
	private const string AUTH_URL = "https://accounts.google.com/o/oauth2/v2/auth";
	private const string TOKEN_URL = "https://oauth2.googleapis.com/token";
	private const string ACCESS_URL = "https://www.googleapis.com/oauth2/v3/userinfo";

	public string Name => "Google";

	public string GetAuthUrl (AuthOptions options)
	{
		string callbackUrl = Environment.GetEnvironmentVariable("CALLBACK_FUNCTION");
		string clientId = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_ID");

		string url = AUTH_URL +
		$"?client_id={clientId}" +
		$"&redirect_uri={callbackUrl}" +
		"&response_type=code" +
		"&access_type=offline" +
		"&scope=email+profile" +
		$"&state={options.AuthId}";

		switch (options.AuthType)
		{
			case AuthType.First:
				url += "&prompt=consent";
				break;
			case AuthType.Returning:
				url += $"&login_hint={options.ReturningId}";
				break;
			default:
				break;
		}
		return url;
	}

	public async Task<bool> IsValid (AuthenticationEntry entry)
	{
		string accessKey = Environment.GetEnvironmentVariable("ACCESS_ENCRYPTION_KEY");
		string accessToken = EncryptionHelper.Decrypt(entry.Data["AccessToken"], accessKey);

		HttpResponseMessage response;
		using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ACCESS_URL))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			response = await Global.HttpClient.SendAsync(request);
		}
		if (!response.IsSuccessStatusCode)
		{
			Logger.LogWarning("Access token has expired. Trying to refresh it.");
			string clientId = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_ID");
			string clientSecret = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_SECRET");
			string refreshKey = Environment.GetEnvironmentVariable("REFRESH_ENCRYPTION_KEY");
			string refreshToken = EncryptionHelper.Decrypt(entry.Data["RefreshToken"], refreshKey);
			string callbackUrl = Environment.GetEnvironmentVariable("CALLBACK_FUNCTION");
			var refreshInfo = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{ "client_id" , clientId },
				{ "client_secret", clientSecret },
				{ "grant_type", "refresh_token" },
				{ "refresh_token", refreshToken },
				{ "redirect_uri", callbackUrl }
			});
			response = await Global.HttpClient.PostAsync(TOKEN_URL, refreshInfo);
			if (!response.IsSuccessStatusCode)
				return false;
			string result = await response.Content.ReadAsStringAsync();
			Dictionary<string, dynamic> dict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(result);
			string newAccessToken = dict["access_token"] as string;
			entry.Data["AccessToken"] = EncryptionHelper.Encrypt(newAccessToken, accessKey);
			Logger.LogWarning("Response fields:");
			foreach (var item in dict)
				Logger.LogWarning($"    {item.Key} : {item.Value.ToString()}");
		}
		return true;
	}

	public async Task<AuthenticationEntry> CreateEntryWithCallbackData (Dictionary<string, string> data)
	{
		AuthenticationEntry entry = new AuthenticationEntry { Provider = "Google" };
		entry.AuthTicket = data["state"];
		string code = data["code"];
		string callbackUrl = Environment.GetEnvironmentVariable("CALLBACK_FUNCTION");
		string clientId = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_ID");
		string clientSecret = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CLIENT_SECRET");
		var newContent = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			{ "client_id" , clientId },
			{ "client_secret", clientSecret },
			{ "grant_type", "authorization_code" },
			{ "redirect_uri", callbackUrl },
			{ "code", code }
		});
		Logger.LogWarning($" -----  Getting Token --------");
		var response = await Global.HttpClient.PostAsync(TOKEN_URL, newContent);
		string result = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
		{
			string msg = $"Error getting code: {JsonConvert.SerializeObject(response)}\n\n{result}";
			Logger.LogError(msg);
			entry.SetStatus(AuthStatus.Failed);
			return entry;
		}
		Dictionary<string, dynamic> dict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(result);
		string accessToken = dict["access_token"] as string;
		string idToken = dict["id_token"] as string;
		string refreshToken = "";
		if (dict.ContainsKey("refresh_token"))
			refreshToken = dict["refresh_token"] as string;
		Logger.LogWarning("Response fields:");
		foreach (var item in dict)
			Logger.LogWarning($"    {item.Key} : {item.Value.ToString()}");

		string encryptedAccessToken = EncryptionHelper.Encrypt(accessToken, Environment.GetEnvironmentVariable("ACCESS_ENCRYPTION_KEY"));
		entry.Data["AccessToken"] = encryptedAccessToken;
		if (!string.IsNullOrEmpty(refreshToken))
			entry.Data["RefreshToken"] = EncryptionHelper.Encrypt(refreshToken, Environment.GetEnvironmentVariable("REFRESH_ENCRYPTION_KEY"));

		var idTokenObj = Global.JwtHandler.ReadJsonWebToken(idToken);
		Logger.LogWarning("Id token fields:");
		entry.UserInfo.UserId = idTokenObj.Subject;
		foreach (var field in idTokenObj.Claims)
		{
			Logger.LogWarning($"    {field.Type} : {field.Value}");
			switch (field.Type)
			{
				case "email":
					entry.UserInfo.Email = field.Value;
					break;
				case "name":
					entry.UserInfo.Name = field.Value;
					break;
				case "picture":
					entry.UserInfo.Picture = field.Value;
					break;
			}
		}
		entry.SetStatus(AuthStatus.Granted);
		return entry;
	}
}
