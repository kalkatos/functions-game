using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kalkatos.FunctionsGame
{
	public static class MatchFunctions
	{
#if AZURE_FUNCTIONS
		private static IService service = new Azure.AzureService();
#else
		private static IService service;
#endif
		private static Random rand = new Random();
		private const string consonantsUpper = "BCDFGHJKLMNPQRSTVWXZ";
		private const string consonantsLower = "bcdfghjklmnpqrstvwxz";
		private const string vowels = "aeiouy";

		private static Dictionary<string, IGame> gameList = new Dictionary<string, IGame>()
		{
			{ "rps", new Rps.RpsGame() }
		};

		// ████████████████████████████████████████████ L O G I N ████████████████████████████████████████████

		public static async Task<LoginResponse> LogIn (LoginRequest request)
		{
			if (string.IsNullOrEmpty(request.Identifier) || string.IsNullOrEmpty(request.GameId))
				return new LoginResponse { IsError = true, Message = "Wrong parameters. Identifier and GameId must not be null." };
			PlayerRegistry playerRegistry;
			string playerId = await service.GetPlayerId(request.Identifier);
			GameRegistry gameRegistry = await service.GetGameConfig(request.GameId);
			gameList[request.GameId].SetSettings(gameRegistry);

			if (string.IsNullOrEmpty(playerId))
			{
				playerId = Guid.NewGuid().ToString();
				await service.RegisterDeviceWithId(request.Identifier, playerId);
				string newPlayerAlias = Guid.NewGuid().ToString();
				PlayerInfo newPlayerInfo = new PlayerInfo { Alias = newPlayerAlias, Nickname = GetRandomNickname_AdjectiveNoun(), CustomData = gameRegistry.DefaultPlayerCustomData };
				playerRegistry = new PlayerRegistry
				{
					PlayerId = playerId,
					Info = newPlayerInfo,
					Devices = new string[] { request.Identifier },
					Region = request.Region,
					LastAccess = DateTime.UtcNow,
					FirstAccess = DateTime.UtcNow
				};
				await service.SetPlayerRegistry(playerRegistry);
			}
			else
			{
				playerRegistry = await service.GetPlayerRegistry(playerId);
				if (!playerRegistry.Devices.Contains(request.Identifier))
				{
					playerRegistry.Devices = (string[])playerRegistry.Devices.Append(request.Identifier);
					await service.SetPlayerRegistry(playerRegistry);
				}
			}

			return new LoginResponse
			{
				IsAuthenticated = playerRegistry.IsAuthenticated,
				PlayerId = playerRegistry.PlayerId,
				MyInfo = playerRegistry.Info,
			};
		}

		public static async Task<PlayerInfoResponse> SetPlayerData (SetPlayerDataRequest request)
		{
			if (request.Data == null || request.Data.Count() == 0)
				return new PlayerInfoResponse { IsError = true, Message = "Request Data is null or empty." };
			if (string.IsNullOrEmpty(request.PlayerId))
				return new PlayerInfoResponse { IsError = true, Message = "Player ID is null or empty." };
			PlayerRegistry playerRegistry = await service.GetPlayerRegistry(request.PlayerId);
			if (playerRegistry == null)
				return new PlayerInfoResponse { IsError = true, Message = "Player not found." };
			bool hasChangedNickname = request.Data.ContainsKey("Nickname");
			string newNickname;
			if (hasChangedNickname)
			{
				newNickname = GetRandomNickname_AdjectiveNoun();
				playerRegistry.Info.Nickname = newNickname; //request.Data["Nickname"];
				request.Data.Remove("Nickname");
			}
			if (playerRegistry.Info.CustomData == null)
				playerRegistry.Info.CustomData = new Dictionary<string, string>();
			foreach (var item in request.Data)
				if (playerRegistry.Info.CustomData.ContainsKey(item.Key))
					playerRegistry.Info.CustomData[item.Key] = item.Value;
			await service.SetPlayerRegistry(playerRegistry);
			return new PlayerInfoResponse { PlayerInfo = playerRegistry.Info.Clone(), Message = "Data changed successfully!" };
		}

		public static async Task<GameDataResponse> GetGameSettings (GameDataRequest request)
		{
			if (string.IsNullOrEmpty(request.GameId) || string.IsNullOrEmpty(request.PlayerId))
				return new GameDataResponse { IsError = true, Message = "Wrong parameters." };
			PlayerRegistry playerRegistry = await service.GetPlayerRegistry(request.PlayerId);
			if (playerRegistry == null)
				return new GameDataResponse { IsError = true, Message = "Player not registered." };
			GameRegistry gameRegistry = await service.GetGameConfig(request.GameId);
			return new GameDataResponse { Settings = gameRegistry.Settings };
		}

		// ████████████████████████████████████████████ A C T I O N ████████████████████████████████████████████

		public static async Task<ActionResponse> SendAction (ActionRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new ActionResponse { IsError = true, Message = "Match id and player id may not be null." };
			if (request.Action == null || (!request.Action.HasAnyPublicChange() && !request.Action.HasAnyPrivateChange()))
				return new ActionResponse { IsError = true, Message = "Action is null or empty." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new ActionResponse { IsError = true, Message = "Problem retrieving the match." };
			if (!match.HasPlayer(request.PlayerId))
				return new ActionResponse { IsError = true, Message = "Player is not on that match." };
			if (match.IsEnded)
				return new ActionResponse { IsError = true, Message = "Match is over." };
			StateRegistry state = await service.GetState(request.MatchId);
			if (!gameList[match.GameId].IsActionAllowed(request.PlayerId, request.Action, match, state))
				return new ActionResponse { IsError = true, Message = "Action is not allowed." };

			// Action Duplicate Protection

			//List<ActionRegistry> registeredActions = await service.GetActions(request.MatchId, request.PlayerId);
			//foreach (var action in registeredActions)
			//{
			//	if (action.Hash == requestActionHash)
			//		return new ActionResponse { IsError = true, Message = "Action is already registered." };
			//}


			await service.AddAction(request.MatchId, request.PlayerId, 
				new ActionRegistry 
				{ 
					Id = Guid.NewGuid().ToString(),
					MatchId = request.MatchId, 
					PlayerId = request.PlayerId, 
					Action = request.Action, 
				});

			Logger.LogWarning($"   [{nameof(SendAction)}] \r\n>> Request = {JsonConvert.SerializeObject(request, Formatting.Indented)}");

			return new ActionResponse { Message = "Action registered successfully." };


			/*
			StateRegistry newState = null;
			for (int attempt = 0; attempt <= 5; ++attempt)
			{
				if (attempt >= 5)
					return new ActionResponse { IsError = true, Message = "Max attempts to Send Action reached." };
				StateRegistry state = await service.GetState(request.MatchId);
				if (state == null)
				{
					Logger.LogError("   [SendAction] State came out null. Retrying...");
					continue;
				}
				if (!gameList[match.GameId].IsActionAllowed(request.PlayerId, request.Action, match, state))
					return new ActionResponse { IsError = true, Message = "Action is not allowed." };
				newState = state.Clone();
				if (request.Action.PublicChanges != null)
					newState.UpsertPublicProperties(request.Action.PublicChanges);
				if (request.Action.PrivateChanges != null)
					newState.UpsertPrivateProperties(request.PlayerId, request.Action.PrivateChanges);
				if (state != null && state.Hash == newState.Hash)
				{
					Logger.LogError($"   [SendAction] state hash and newState hash are equal:\n>> State = {JsonConvert.SerializeObject(state)}\n\n>> NewState = {JsonConvert.SerializeObject(newState)}");
					return new ActionResponse { IsError = true, Message = "Action is already registered." };
				}
				if (!await service.SetState(request.MatchId, state, newState))
				{
					Logger.LogError("   [SendAction] States didn't match, retrying....");
					continue;
				}
				break;
			}
			Logger.LogWarning($"   [SendAction] State after action = {JsonConvert.SerializeObject(newState, Formatting.Indented)}");
			return new ActionResponse { AlteredState = newState.GetStateInfo(request.PlayerId), Message = "Action registered successfully." };
			*/
		}

		// ████████████████████████████████████████████ M A T C H ████████████████████████████████████████████

		public static async Task<Response> FindMatch (FindMatchRequest request)
		{
			if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.GameId))
				return new MatchResponse { IsError = true, Message = "Wrong Parameters." };
			MatchmakingEntry[] entries = await service.GetMatchmakingEntries(request.Region, null, request.PlayerId, MatchmakingStatus.Undefined);
			if (entries != null && entries.Length > 1)
				foreach (MatchmakingEntry entry in entries)
					await service.DeleteMatchmakingHistory(entry.PlayerId, entry.MatchId);
			string playerInfoSerialized = JsonConvert.SerializeObject((await service.GetPlayerRegistry(request.PlayerId)).Info);
			await service.UpsertMatchmakingEntry(
				new MatchmakingEntry
				{
					Region = request.Region,
					PlayerId = request.PlayerId,
					Status = MatchmakingStatus.Searching,
					PlayerInfoSerialized = playerInfoSerialized
				});
			await TryToMatchPlayers(request.GameId, request.Region);

			return new Response { Message = "Find match request registered successfully." };
		}

		public static async Task<MatchResponse> GetMatch (MatchRequest request)
		{
			if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region) || string.IsNullOrEmpty(request.GameId))
				return new MatchResponse { IsError = true, Message = "Wrong Parameters." };

			if (string.IsNullOrEmpty(request.MatchId))
			{
				// Try to get entries two times 
				for (int attempt = 1; attempt <= 2; attempt++)
				{
					// Get the match id of the match assigned to the player or find matches
					MatchmakingEntry[] entries = await service.GetMatchmakingEntries(null, null, request.PlayerId, MatchmakingStatus.Undefined);
					if (entries == null || entries.Length == 0)
						return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
					if (entries.Length > 1)
						Logger.LogError($"[{nameof(GetMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {JsonConvert.SerializeObject(entries)}");
					var playerEntry = entries[0];
					if (playerEntry.Status == MatchmakingStatus.FailedWithNoPlayers)
						return new MatchResponse { IsError = true, Message = $"Matchmaking failed with no players." };
					if (attempt == 1 && string.IsNullOrEmpty(playerEntry.MatchId))
						await TryToMatchPlayers(request.GameId, request.Region);
					else
						request.MatchId = playerEntry.MatchId;
				}
				if (string.IsNullOrEmpty(request.MatchId))
					return new MatchResponse { IsError = true, Message = $"Didn't find any match for player." };
			}
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new MatchResponse { IsError = true, Message = $"Match with id {request.MatchId} wasn't found." };
			if (match.IsEnded)
			{
				await DeleteMatch(match.MatchId);
				return new MatchResponse { IsError = true, Message = $"Match is over.", IsOver = true };
			}
			PlayerInfo[] playerInfos = new PlayerInfo[match.PlayerInfos.Length];
			for (int i = 0; i < match.PlayerInfos.Length; i++)
				playerInfos[i] = match.PlayerInfos[i].Clone();
			return new MatchResponse
			{
				MatchId = request.MatchId,
				Players = playerInfos,
				IsOver = match.IsEnded
			};
		}

		public static async Task<Response> LeaveMatch (MatchRequest request)
		{
			if (request == null || string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.Region))
				return new Response { IsError = true, Message = "Wrong Parameters." };

			if (string.IsNullOrEmpty(request.MatchId))
			{
				MatchmakingEntry[] entries = await service.GetMatchmakingEntries(null, null, request.PlayerId, MatchmakingStatus.Undefined);
				if (entries == null || entries.Length == 0)
					return new Response { IsError = true, Message = $"Didn't find any match for player." };
				MatchmakingEntry playerEntry = default;
				if (entries.Length > 1)
					Logger.LogError($"[{nameof(GetMatch)}] More than one entry in matchmaking found! Player = {request.PlayerId} Query = {JsonConvert.SerializeObject(entries)}");
				playerEntry = entries[0];
				if (string.IsNullOrEmpty(playerEntry.MatchId))
				{
					await service.DeleteMatchmakingHistory(request.PlayerId, null);
					return new Response { Message = $"Leave match executed by wiping matchmaking entries." };
				}
				request.MatchId = playerEntry.MatchId;
			}

			StateRegistry currentState = await service.GetState(request.MatchId);
			if (currentState == null)
				return new Response { IsError = true, Message = "Problem getting the match state." };
			StateRegistry newState = currentState.Clone();
			if (newState.HasPublicProperty("RetreatedPlayers"))
				newState.UpsertPublicProperty("RetreatedPlayers", $"{newState.GetPublic("RetreatedPlayers")}|{request.PlayerId}");
			newState.UpsertPublicProperty("RetreatedPlayers", request.PlayerId);
			await service.SetState(request.MatchId, currentState.Hash, newState);
			newState = await PrepareTurn (request.PlayerId, await service.GetMatchRegistry(request.MatchId), newState);

			Logger.LogWarning($"   [{nameof(LeaveMatch)}] \r\n>> Request :: {JsonConvert.SerializeObject(request, Formatting.Indented)}\r\n>> StateRegistry :: {JsonConvert.SerializeObject(newState, Formatting.Indented)}");

			return new Response { Message = $"Added player as retreated in {request.MatchId} successfully." };
		}

		public static async Task DeleteMatch (string matchId)
		{
			Logger.LogWarning("   [DeleteMatch] " + matchId);
			if (string.IsNullOrEmpty(matchId))
				return;
			await service.DeleteMatchmakingHistory(null, matchId);
			await service.DeleteState(matchId);
			await service.DeleteMatchRegistry(matchId);
			await service.DeleteActions(matchId);
		}

		public static async Task CheckMatch (string matchId, int lastHash)
		{
			StateRegistry state = await service.GetState(matchId);
			if (state == null)
				return;
			if ((state.TurnNumber == 0 && !HasHandshakingFromAllPlayers(state, await service.GetActions(matchId))) || state.Hash == lastHash)
				await DeleteMatch(matchId);
			else
			{
				MatchRegistry match = await service.GetMatchRegistry(matchId);
				if (match == null)
					return;
				GameRegistry gameRegistry = await service.GetGameConfig(match.GameId);
				await service.ScheduleCheckMatch(gameRegistry.RecurrentCheckMatchDelay * 1000, matchId, state.Hash);
			}
		}

		// ████████████████████████████████████████████ S T A T E ████████████████████████████████████████████

		public static async Task<StateResponse> GetMatchState (StateRequest request)
		{
			if (string.IsNullOrEmpty(request.PlayerId) || string.IsNullOrEmpty(request.MatchId))
				return new StateResponse { IsError = true, Message = "Match id and player id may not be null." };
			MatchRegistry match = await service.GetMatchRegistry(request.MatchId);
			if (match == null)
				return new StateResponse { IsError = true, Message = "Match not found." };
			if (!match.HasPlayer(request.PlayerId))
				return new StateResponse { IsError = true, Message = "Player is not on that match." };
			StateRegistry currentState = await service.GetState(request.MatchId);
			List<ActionRegistry> actions = await service.GetActions(request.MatchId);

			if (currentState == null)
				return new StateResponse { IsError = true, Message = "Get state error." };
			if (!HasHandshakingFromAllPlayers(currentState, actions))
				return new StateResponse { IsError = true, Message = "Not every player is ready." };
			currentState = await PrepareTurn(request.PlayerId, match, currentState, actions);
			StateInfo info = currentState.GetStateInfo(request.PlayerId);
			if (info.Hash == request.LastHash)
				return new StateResponse { IsError = true, Message = "Current state is the same known state." };

			Logger.LogWarning($"   [{nameof(GetMatchState)}] \r\n>> Request :: {JsonConvert.SerializeObject(request, Formatting.Indented)} \r\n>> StateRegistry :: {JsonConvert.SerializeObject(currentState, Formatting.Indented)}");

			return new StateResponse { StateInfo = info };


			/*
			switch (match.Status)
			{
				case (int)MatchStatus.AwaitingPlayers:
					//if (!HasHandshakingFromAllPlayers(currentState, actions))
					//	return new StateResponse { IsError = true, Message = "Not every player is ready." };
					currentState = await PrepareTurn(request.PlayerId, match, currentState, actions);
					if (match.Status == (int)MatchStatus.Started)
					{
						match.StartTime = DateTime.UtcNow;
						await service.SetMatchRegistry(match);
					}

					Logger.LogWarning("   [GetMatchState] StateRegistry = = " + JsonConvert.SerializeObject(currentState, Formatting.Indented));

					return new StateResponse { StateInfo = currentState.GetStateInfo(request.PlayerId) };
				case (int)MatchStatus.Started:
				case (int)MatchStatus.Ended:
					currentState = await PrepareTurn(request.PlayerId, match, currentState, actions);
					StateInfo info = currentState.GetStateInfo(request.PlayerId);
					if (info.Hash == request.LastHash)
						return new StateResponse { IsError = true, Message = "Current state is the same known state." };

					Logger.LogWarning("   [GetMatchState] StateRegistry = = " + JsonConvert.SerializeObject(currentState, Formatting.Indented));

					return new StateResponse { StateInfo = info };
			}
			return new StateResponse { IsError = true, Message = "Match is in an unknown state." };
			*/
		}

		// █████████████████████████████████████ P U B L I C █ U T I L I T Y █████████████████████████████████████

		public static string GetRandomNickname_GuestPlus6Letters ()
		{
			string result = "";
			for (int i = 0; i < 6; i++)
			{
				if (i == 0)
					result += consonantsUpper[rand.Next(0, consonantsUpper.Length)];
				else if (i % 2 == 0)
					result += consonantsLower[rand.Next(0, consonantsLower.Length)];
				else
					result += vowels[rand.Next(0, vowels.Length)];
			}
			return "Guest-" + result;
		}

		public static string GetRandomNickname_AdjectiveNoun ()
		{
			string[] adjectives = new string[] { "Absurd", "Admirable", "Adventurous", "Affable", "Agile", "Alluring", "Amber", "Ambitious", "Amethyst", "Amiable", "Amusing", "Ancient", "Apricot", "Aqua", "Aquamarine", "Artistic", "Astonishing", "Audacious", "Authentic", "Bashful", "Beautiful", "Beguiling", "Beige", "Bewildering", "Bewitching", "Blissful", "Blue", "Boisterous", "Bold", "Brave", "Breathtaking", "Bright", "Brilliant", "Bronze", "Burgundy", "Captivating", "Carefree", "Caring", "Cerulean", "Chaotic", "Charismatic", "Charming", "Cheerful", "Clever", "Cobalt", "Colorful", "Comical", "Compassionate", "Confident", "Coral", "Cozy", "Creative", "Crimson", "Curious", "Dandelion", "Daring", "Dazzling", "Delightful", "Demure", "Denim", "Determined", "Driven", "Dynamic", "Ebony", "Eccentric", "Eclectic", "Ecru", "Elegant", "Emerald", "Empathetic", "Empowered", "Enchanting", "Energetic", "Engaging", "Enigmatic", "Enthralling", "Enthusiastic", "Enticing", "Ethereal", "Exhilarating", "Exquisite", "Extraordinary", "Exuberant", "Fabulous", "Fanciful", "Fandango", "Fantastic", "Fascinating", "Fawn", "Fearless", "Flamboyant", "Flamenco", "Flawless", "Forest", "Fragrant", "Fresh", "Friendly", "Fuchsia", "Funky", "Gallant", "Generous", "Gentle", "Genuine", "Glamorous", "Gleaming", "Glorious", "Glowing", "Gold", "Gorgeous", "Graceful", "Gracious", "Grandiose", "Grateful", "Green", "Grotesque", "Happy", "Harmonious", "Heliotrope", "Hilarious", "Honest", "Honeydew", "Hopeful", "Humble", "Hypnotic", "Illuminating", "Imaginative", "Impressive", "Incandescent", "Indigo", "Ingenious", "Inspirational", "Inspiring", "Intriguing", "Intuitive", "Invigorating", "Iridescent", "Iris", "Ivory", "Jade", "Jasper", "Jazzy", "Jolly", "Jovial", "Joyful", "Jubilant", "Kaleidoscopic", "Khaki", "Kind", "Kindhearted", "Knowledgeable", "Koi", "Lavender", "Lilac", "Lively", "Loquacious", "Loving", "Loyal", "Luminous", "Luxurious", "Magnetic", "Magnificent", "Majestic", "Maroon", "Mauve", "Mellow", "Melodic", "Mesmerizing", "Modest", "Motivated", "Mysterious", "Mystical", "Natural", "Nautical", "Navy", "Noble", "Nonjudgmental", "Nostalgic", "Nurturing", "Nutty", "Odd", "Olive", "Optimistic", "Opulent", "Orchid", "Ornate", "Outstanding", "Passionate", "Patient", "Peach", "Peculiar", "Pensive", "Periwinkle", "Perplexing", "Picturesque", "Pink", "Playful", "Plucky", "Precious", "Quaint", "Quartz", "Quicksilver", "Quirky", "Quixotic", "Radiant", "Reckless", "Red", "Remarkable", "Resilient", "Resourceful", "Resplendent", "Riveting", "Rose", "Ruby", "Russet", "Saffron", "Sapphire", "Scintillating", "Selfless", "Serene", "Silky", "Sincere", "Sizzling", "Sleek", "Sparkling", "Spectacular", "Spirited", "Splendid", "Spontaneous", "Stimulating", "Striking", "Stunning", "Sunny", "Supportive", "Surreal", "Tangerine", "Tantalizing", "Teal", "Thoughtful", "Thrilling", "Tourmaline", "Tranquil", "Tremendous", "Trustworthy", "Tumultuous", "Turquoise", "Ultramarine", "Umber", "Unconventional", "Understanding", "Unforgettable", "Unique", "Unpredictable", "Unusual", "Unwavering", "Uplifting", "Vermilion", "Vibrant", "Vigorous", "Violet", "Viridian", "Virtuous", "Vivacious", "Vivid", "Wheat", "Whimsical", "White", "Wholesome", "Wise", "Witty", "Wonderful", "Xanadu", "Xenial", "Xenodochial", "Yearning", "Yellow", "Youthful", "Yummy", "Zaffre", "Zany", "Zealous", "Zestful", "Zesty" };
			string[] nouns = new string[] { "Aardvark", "Airplane", "Alligator", "Alpaca", "Anemone", "Ant", "Antelope", "Apple", "Apron", "Armadillo", "Baboon", "Backpack", "Badge", "Badger", "Bag", "Ball", "Barometer", "Bass", "Bat", "Bear", "Beaver", "Bee", "Beetle", "Belt", "Bicycle", "Bike", "Bilby", "Binoculars", "Bird", "Bison", "Blender", "Blush", "Boat", "Bobcat", "Book", "Boots", "Bowl", "Bracelet", "Briefs", "Broom", "Bucket", "Buffalo", "Bus", "Butterfly", "Calendar", "Camel", "Camera", "Candle", "Capuchin", "Capybara", "Car", "Caracal", "Carp", "Cat", "Centipede", "Chair", "Chameleon", "Cheetah", "Chicken", "Chimpanzee", "Chinchilla", "Chipmunk", "Clam", "Clock", "Cloud", "Clownfish", "Coat", "Cobra", "Coin", "Cologne", "Comb", "Compass", "Computer", "Coral", "Cougar", "Cow", "Crab", "Cricket", "Crocodile", "Cup", "Deer", "Desk", "Dingo", "Dishwasher", "Dog", "Dolphin", "Door", "Dragonfly", "Dress", "Drum", "Dryer", "Duck", "Eagle", "Earrings", "Earthworm", "Echidna", "Eel", "Elephant", "Emu", "Eyeliner", "Eyeshadow", "Falcon", "Ferret", "Firefly", "Firewood", "Fish", "Flower", "Fork", "Foundation", "Fox", "Frog", "Gazelle", "Gecko", "Gibbon", "Giraffe", "Glass", "Globe", "Glove", "Gloves", "Goat", "Goggles", "Goldfish", "Goose", "Gorilla", "Grasshopper", "Guitar", "Hairbrush", "Hairdryer", "Hamster", "Hat", "Hawk", "Headphones", "Hedgehog", "Helmet", "Hippo", "Hippopotamus", "Hornet", "Horse", "House", "Hyena", "Jackal", "Jacket", "Jellyfish", "Kangaroo", "Key", "Knife", "Koala", "Ladybug", "Lamp", "Lantern", "Leech", "Lemur", "Lion", "Lipstick", "Lizard", "Llama", "Lobster", "Lynx", "Magazine", "Manatee", "Mandrill", "Map", "Mascara", "Mask", "Meerkat", "Microphone", "Microscope", "Microwave", "Millipede", "Mirror", "Mole", "Money", "Mongoose", "Moon", "Moose", "Mop", "Moth", "Motorcycle", "Mountain", "Mouse", "Music", "Mussel", "Napkin", "Narwhal", "Necklace", "Net", "Newspaper", "Newt", "Notebook", "Numbat", "Nutria", "Ocean", "Octopus", "Opossum", "Orangutan", "Ostrich", "Otter", "Oven", "Owl", "Oyster", "Panda", "Panther", "Pants", "Paper", "Parrot", "Passport", "Peacock", "Pen", "Pencil", "Penguin", "Perfume", "Phone", "Piano", "Pig", "Pigeon", "Plate", "Platypus", "Porcupine", "Possum", "Python", "Quokka", "Quoll", "Rabbit", "Raccoon", "Racket", "Radio", "Rat", "Ray", "Razor", "Refrigerator", "Reindeer", "Rhino", "Rhinoceros", "Ring", "River", "Rocket", "Rooster", "Salamander", "Salmon", "Sandals", "Scarf", "Scorpion", "Seagull", "Seahorse", "Seal", "Serval", "Shampoo", "Shark", "Shaver", "Sheep", "Ship", "Shirt", "Shoe", "Shoes", "Shorts", "Shrew", "Shrimp", "Shuttlecock", "Skateboard", "Skates", "Skirt", "Skunk", "Slippers", "Sloth", "Slug", "Snail", "Snake", "Soap", "Socks", "Speaker", "Spider", "Sponge", "Spoon", "Squid", "Squirrel", "Star", "Starfish", "Stove", "Subway", "Subway", "Suit", "Suitcase", "Sun", "Sunglasses", "Sunscreen", "Suspenders", "Swan", "Sweater", "Swordfish", "Table", "Tablecloth", "Tapir", "Tarantula", "Tarsier", "Telescope", "Television", "Tent", "Thermometer", "Thermos", "Ticket", "Tie", "Tiger", "Tissue", "Toad", "Toaster", "Toothbrush", "Toothpaste", "Tortoise", "Towel", "Train", "Tree", "Trout", "Turkey", "Turtle", "Tuxedo", "Underwear", "Uniform", "Viper", "Wallaby", "Wallet", "Walrus", "Warthog", "Wasp", "Watch", "Weasel", "Whale", "Whistle", "Window", "Wolf", "Wolverine", "Wombat", "Yak", "Zebra" };
			return $"{adjectives[rand.Next(adjectives.Length)]}{nouns[rand.Next(nouns.Length)]}";
		}

		// ████████████████████████████████████████████ P R I V A T E ████████████████████████████████████████████

		private static async Task TryToMatchPlayers (string gameId, string region)
		{
			// Get settings for matchmaking
			GameRegistry gameRegistry = await service.GetGameConfig(gameId);
			int playerCount = gameRegistry.HasSetting("PlayerCount") && int.TryParse(gameRegistry.GetValue("PlayerCount"), out int count) ? count : 2;
			float maxWaitToMatchWithBots = gameRegistry.HasSetting("MaxWait") && float.TryParse(gameRegistry.GetValue("MaxWait"), out float wait) ? wait : 6.0f;
			string actionForNoPlayers = gameRegistry.HasSetting("ActionForNoPlayers") ? gameRegistry.GetValue("ActionForNoPlayers") : "MatchWithBots";
			// Get entries for that region
			MatchmakingEntry[] entries = await service.GetMatchmakingEntries(region, null, null, MatchmakingStatus.Undefined);

			List<MatchmakingEntry> matchCandidates = new List<MatchmakingEntry>();
			for (int i = 0; i < entries.Length; i++)
			{
				if (entries[i] == null)
					continue;
				if (entries[i].Status == MatchmakingStatus.Searching)
					matchCandidates.Add(entries[i]);
			}
			while (matchCandidates.Count >= playerCount)
			{
				List<MatchmakingEntry> range = matchCandidates.GetRange(0, playerCount);
				matchCandidates.RemoveRange(0, playerCount);
				await CreateMatch(range, false);
			}
			if (matchCandidates.Count == 0)
				return;
			DateTime entriesMaxTimestamp = matchCandidates.Max(e => e.Timestamp);
			if ((DateTime.UtcNow - entriesMaxTimestamp).TotalSeconds >= maxWaitToMatchWithBots)
			{
				if (actionForNoPlayers == "MatchWithBots")
				{
					// Match with bots
					int candidatesCount = matchCandidates.Count;
					for (int i = 0; i < playerCount - candidatesCount; i++)
					{
						// Add bot entry to the matchmaking table
						string botId = "X" + Guid.NewGuid().ToString();
						string botAlias = Guid.NewGuid().ToString();
						PlayerInfo botInfo = gameList[gameId].CreateBot(gameRegistry.BotSettings);
						matchCandidates.Add(
							new MatchmakingEntry
							{
								PlayerId = botId,
								PlayerInfoSerialized = JsonConvert.SerializeObject(botInfo),
								Region = region,
								Status = MatchmakingStatus.Searching,
								Timestamp = DateTime.UtcNow
							});
					}
					await CreateMatch(matchCandidates, true);
				}
				else
				{
					foreach (var item in matchCandidates)
					{
						item.Status = MatchmakingStatus.FailedWithNoPlayers;
						await service.UpsertMatchmakingEntry(item);
					}
				}
			}

			async Task CreateMatch (List<MatchmakingEntry> entries, bool hasBots)
			{
				string matchId = Guid.NewGuid().ToString();
				string[] ids = new string[entries.Count];
				PlayerInfo[] infos = new PlayerInfo[entries.Count];
				for (int i = 0; i < entries.Count; i++)
				{
					MatchmakingEntry entry = entries[i];
					ids[i] = entry.PlayerId;
					infos[i] = JsonConvert.DeserializeObject<PlayerInfo>(entry.PlayerInfoSerialized);
					if (entry.PlayerId[0] == 'X')
						continue;
					entry.MatchId = matchId;
					entry.Status = MatchmakingStatus.Matched;
					await service.UpsertMatchmakingEntry(entry);
				}
				MatchRegistry match = new MatchRegistry
				{
					GameId = gameId,
					MatchId = matchId,
					CreatedTime = DateTime.UtcNow,
					PlayerIds = ids,
					PlayerInfos = infos,
					Region = region,
					HasBots = hasBots
				};
				await service.SetMatchRegistry(match);
				await CreateFirstStateAndRegister(match);
				await service.ScheduleCheckMatch(gameRegistry.FirstCheckMatchDelay * 1000, matchId, 0);
			}
		}

		private static async Task<StateRegistry> CreateFirstStateAndRegister (MatchRegistry match)
		{
			StateRegistry newState = gameList[match.GameId].CreateFirstState(match);
			await service.SetState(match.MatchId, null, newState);
			return newState;
		}

		private static async Task<StateRegistry> PrepareTurn (string requesterId, MatchRegistry match, StateRegistry lastState, List<ActionRegistry> actions = null)
		{
			if (lastState == null)
				Logger.LogError("   [PrepareTurn] Last state should not be null.");
			if (actions == null)
				actions = await service.GetActions(match.MatchId);
			StateRegistry newState = gameList[match.GameId].PrepareTurn(requesterId, match, lastState, actions);
			await service.UpdateActions(match.MatchId, actions);
			if (newState.IsMatchEnded && !match.IsEnded)
			{
				match.IsEnded = true;
				await service.SetMatchRegistry(match);
				await service.DeleteMatchmakingHistory(null, match.MatchId);
				if (!await service.SetState(match.MatchId, lastState.Hash, newState))
				{
					Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
					return await PrepareTurn(requesterId, match, await service.GetState(match.MatchId), actions);
				}
				GameRegistry gameRegistry = await service.GetGameConfig(match.GameId);
				await service.ScheduleCheckMatch(gameRegistry.FinalCheckMatchDelay * 1000, match.MatchId, newState.Hash);
				return newState;
			}
			if (lastState != null && newState.Hash == lastState.Hash)
				return lastState;
			if (!await service.SetState(match.MatchId, lastState.Hash, newState))
			{
				Logger.LogError("   [PrepareTurn] States didn't match, retrying....");
				return await PrepareTurn(requesterId, match, await service.GetState(match.MatchId), actions);
			}
			return newState;
		}

		private static bool HasHandshakingFromAllPlayers (StateRegistry state, List<ActionRegistry> actions)
		{
			string[] players = state.GetPlayers();
			int count = 0;
			string playersWithHandshaking = "";
			foreach (var player in players)
				if (player[0] == 'X' 
					|| !string.IsNullOrEmpty(state.GetPrivate(player, "Handshaking")) 
					|| (actions.Find(x => x.PlayerId == player && x.Action.IsPrivateChangeEqualsIfPresent("Handshaking", "1")) != null))
				{
					count++;
					playersWithHandshaking += $"| {player}";
				}
			if (count != players.Length)
				Logger.LogError($"   [HasHandshakingFromAllPlayers] Player with handshaking = {count} = {playersWithHandshaking}\n{JsonConvert.SerializeObject(actions, Formatting.Indented)}");
			return count == players.Length;
		}
	}
}
