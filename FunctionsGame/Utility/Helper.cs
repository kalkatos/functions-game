using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kalkatos.Network;

internal static class Helper
{
	internal static Random rand = Global.Random;

	private const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXZ";
	private const string consonantsUpper = "BCDFGHJKLMNPQRSTVWXZ";
	private const string consonantsLower = "bcdfghjklmnpqrstvwxz";
	private const string vowels = "aeiouy";

	private static JsonSerializerSettings serializationSettings = new() { MissingMemberHandling = MissingMemberHandling.Error };

	internal static string ReadBytes (Stream stream, Encoding encoding)
	{
		byte[] bytes = new byte[stream.Length];
		int numBytesToRead = (int)stream.Length;
		int numBytesRead = 0;
		while (numBytesToRead > 0)
		{
			// Read may return anything from 0 to numBytesToRead.
			int n = stream.Read(bytes, numBytesRead, numBytesToRead);

			// Break when the end of the file is reached.
			if (n == 0)
				break;
			numBytesRead += n;
			numBytesToRead -= n;
		}
		return encoding.GetString(bytes);
	}

	internal static int GetHash (params Dictionary<string, string>[] dicts)
	{
		unchecked
		{
			int hash = 23;
			foreach (var dict in dicts)
			{
				foreach (var item in dict)
				{
					foreach (char c in item.Key)
						hash = hash * 31 + c;
					foreach (char c in item.Value)
						hash = hash * 31 + c;
				}
			}
			return hash;
		}
	}

	public static void DismemberData (ref Dictionary<string, string> dict, string key, string data, bool isFirstLevel = true)
	{
		if (isFirstLevel)
			dict.Add("Value", data);
		if (TryParseAsDict(data, out Dictionary<string, dynamic> dataDict) && dataDict != null)
		{
			if (!isFirstLevel)
				dict.Add(key, data);
			foreach (var item in dataDict)
			{
				string subKey = item.Key.Contains('-') ? item.Key.Substring(0, item.Key.IndexOf('-')) : item.Key;
				if (string.IsNullOrEmpty(key) || isFirstLevel)
					DismemberData(ref dict, subKey, item.Value?.ToString(), false);
				else
				{
					if (item.Key.Contains('-'))

						DismemberData(ref dict, $"{key}_{subKey}", item.Value?.ToString(), false);
				}
			}
		}
		else if (!isFirstLevel)
			dict.Add(key, data);

		bool TryParseAsDict (string data, out Dictionary<string, dynamic> result)
		{
			try
			{
				result = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(data, serializationSettings);
				return true;
			}
			catch (Exception)
			{
				result = null;
				return false;
			}
		}
	}

	public static string GetRandomNickname_AdjectiveNoun ()
	{
		string[] adjectives = new string[] { "Absurd", "Admirable", "Adventurous", "Affable", "Agile", "Alluring", "Amber", "Ambitious", "Amethyst", "Amiable", "Amusing", "Ancient", "Apricot", "Aqua", "Aquamarine", "Artistic", "Astonishing", "Audacious", "Authentic", "Bashful", "Beautiful", "Beguiling", "Beige", "Bewildering", "Bewitching", "Blissful", "Blue", "Boisterous", "Bold", "Brave", "Breathtaking", "Bright", "Brilliant", "Bronze", "Burgundy", "Captivating", "Carefree", "Caring", "Cerulean", "Chaotic", "Charismatic", "Charming", "Cheerful", "Clever", "Cobalt", "Colorful", "Comical", "Compassionate", "Confident", "Coral", "Cozy", "Creative", "Crimson", "Curious", "Dandelion", "Daring", "Dazzling", "Delightful", "Demure", "Denim", "Determined", "Driven", "Dynamic", "Ebony", "Eccentric", "Eclectic", "Ecru", "Elegant", "Emerald", "Empathetic", "Empowered", "Enchanting", "Energetic", "Engaging", "Enigmatic", "Enthralling", "Enthusiastic", "Enticing", "Ethereal", "Exhilarating", "Exquisite", "Extraordinary", "Exuberant", "Fabulous", "Fanciful", "Fandango", "Fantastic", "Fascinating", "Fawn", "Fearless", "Flamboyant", "Flamenco", "Flawless", "Forest", "Fragrant", "Fresh", "Friendly", "Fuchsia", "Funky", "Gallant", "Generous", "Gentle", "Genuine", "Glamorous", "Gleaming", "Glorious", "Glowing", "Gold", "Gorgeous", "Graceful", "Gracious", "Grandiose", "Grateful", "Green", "Grotesque", "Happy", "Harmonious", "Heliotrope", "Hilarious", "Honest", "Honeydew", "Hopeful", "Humble", "Hypnotic", "Illuminating", "Imaginative", "Impressive", "Incandescent", "Indigo", "Ingenious", "Inspirational", "Inspiring", "Intriguing", "Intuitive", "Invigorating", "Iridescent", "Iris", "Ivory", "Jade", "Jasper", "Jazzy", "Jolly", "Jovial", "Joyful", "Jubilant", "Kaleidoscopic", "Khaki", "Kind", "Kindhearted", "Knowledgeable", "Koi", "Lavender", "Lilac", "Lively", "Loquacious", "Loving", "Loyal", "Luminous", "Luxurious", "Magnetic", "Magnificent", "Majestic", "Maroon", "Mauve", "Mellow", "Melodic", "Mesmerizing", "Modest", "Motivated", "Mysterious", "Mystical", "Natural", "Nautical", "Navy", "Noble", "Nonjudgmental", "Nostalgic", "Nurturing", "Nutty", "Odd", "Olive", "Optimistic", "Opulent", "Orchid", "Ornate", "Outstanding", "Passionate", "Patient", "Peach", "Peculiar", "Pensive", "Periwinkle", "Perplexing", "Picturesque", "Pink", "Playful", "Plucky", "Precious", "Quaint", "Quartz", "Quicksilver", "Quirky", "Quixotic", "Radiant", "Reckless", "Red", "Remarkable", "Resilient", "Resourceful", "Resplendent", "Riveting", "Rose", "Ruby", "Russet", "Saffron", "Sapphire", "Scintillating", "Selfless", "Serene", "Silky", "Sincere", "Sizzling", "Sleek", "Sparkling", "Spectacular", "Spirited", "Splendid", "Spontaneous", "Stimulating", "Striking", "Stunning", "Sunny", "Supportive", "Surreal", "Tangerine", "Tantalizing", "Teal", "Thoughtful", "Thrilling", "Tourmaline", "Tranquil", "Tremendous", "Trustworthy", "Tumultuous", "Turquoise", "Ultramarine", "Umber", "Unconventional", "Understanding", "Unforgettable", "Unique", "Unpredictable", "Unusual", "Unwavering", "Uplifting", "Vermilion", "Vibrant", "Vigorous", "Violet", "Viridian", "Virtuous", "Vivacious", "Vivid", "Wheat", "Whimsical", "White", "Wholesome", "Wise", "Witty", "Wonderful", "Xanadu", "Xenial", "Xenodochial", "Yearning", "Yellow", "Youthful", "Yummy", "Zaffre", "Zany", "Zealous", "Zestful", "Zesty" };
		string[] nouns = new string[] { "Aardvark", "Airplane", "Alligator", "Alpaca", "Anemone", "Ant", "Antelope", "Apple", "Apron", "Armadillo", "Baboon", "Backpack", "Badge", "Badger", "Bag", "Ball", "Barometer", "Bass", "Bat", "Bear", "Beaver", "Bee", "Beetle", "Belt", "Bicycle", "Bike", "Bilby", "Binoculars", "Bird", "Bison", "Blender", "Blush", "Boat", "Bobcat", "Book", "Boots", "Bowl", "Bracelet", "Briefs", "Broom", "Bucket", "Buffalo", "Bus", "Butterfly", "Calendar", "Camel", "Camera", "Candle", "Capuchin", "Capybara", "Car", "Caracal", "Carp", "Cat", "Centipede", "Chair", "Chameleon", "Cheetah", "Chicken", "Chimpanzee", "Chinchilla", "Chipmunk", "Clam", "Clock", "Cloud", "Clownfish", "Coat", "Cobra", "Coin", "Cologne", "Comb", "Compass", "Computer", "Coral", "Cougar", "Cow", "Crab", "Cricket", "Crocodile", "Cup", "Deer", "Desk", "Dingo", "Dishwasher", "Dog", "Dolphin", "Door", "Dragonfly", "Dress", "Drum", "Dryer", "Duck", "Eagle", "Earrings", "Earthworm", "Echidna", "Eel", "Elephant", "Emu", "Eyeliner", "Eyeshadow", "Falcon", "Ferret", "Firefly", "Firewood", "Fish", "Flower", "Fork", "Foundation", "Fox", "Frog", "Gazelle", "Gecko", "Gibbon", "Giraffe", "Glass", "Globe", "Glove", "Gloves", "Goat", "Goggles", "Goldfish", "Goose", "Gorilla", "Grasshopper", "Guitar", "Hairbrush", "Hairdryer", "Hamster", "Hat", "Hawk", "Headphones", "Hedgehog", "Helmet", "Hippo", "Hippopotamus", "Hornet", "Horse", "House", "Hyena", "Jackal", "Jacket", "Jellyfish", "Kangaroo", "Key", "Knife", "Koala", "Ladybug", "Lamp", "Lantern", "Leech", "Lemur", "Lion", "Lipstick", "Lizard", "Llama", "Lobster", "Lynx", "Magazine", "Manatee", "Mandrill", "Map", "Mascara", "Mask", "Meerkat", "Microphone", "Microscope", "Microwave", "Millipede", "Mirror", "Mole", "Money", "Mongoose", "Moon", "Moose", "Mop", "Moth", "Motorcycle", "Mountain", "Mouse", "Music", "Mussel", "Napkin", "Narwhal", "Necklace", "Net", "Newspaper", "Newt", "Notebook", "Numbat", "Nutria", "Ocean", "Octopus", "Opossum", "Orangutan", "Ostrich", "Otter", "Oven", "Owl", "Oyster", "Panda", "Panther", "Pants", "Paper", "Parrot", "Passport", "Peacock", "Pen", "Pencil", "Penguin", "Perfume", "Phone", "Piano", "Pig", "Pigeon", "Plate", "Platypus", "Porcupine", "Possum", "Python", "Quokka", "Quoll", "Rabbit", "Raccoon", "Racket", "Radio", "Rat", "Ray", "Razor", "Refrigerator", "Reindeer", "Rhino", "Rhinoceros", "Ring", "River", "Rocket", "Rooster", "Salamander", "Salmon", "Sandals", "Scarf", "Scorpion", "Seagull", "Seahorse", "Seal", "Serval", "Shampoo", "Shark", "Shaver", "Sheep", "Ship", "Shirt", "Shoe", "Shoes", "Shorts", "Shrew", "Shrimp", "Shuttlecock", "Skateboard", "Skates", "Skirt", "Skunk", "Slippers", "Sloth", "Slug", "Snail", "Snake", "Soap", "Socks", "Speaker", "Spider", "Sponge", "Spoon", "Squid", "Squirrel", "Star", "Starfish", "Stove", "Subway", "Subway", "Suit", "Suitcase", "Sun", "Sunglasses", "Sunscreen", "Suspenders", "Swan", "Sweater", "Swordfish", "Table", "Tablecloth", "Tapir", "Tarantula", "Tarsier", "Telescope", "Television", "Tent", "Thermometer", "Thermos", "Ticket", "Tie", "Tiger", "Tissue", "Toad", "Toaster", "Toothbrush", "Toothpaste", "Tortoise", "Towel", "Train", "Tree", "Trout", "Turkey", "Turtle", "Tuxedo", "Underwear", "Uniform", "Viper", "Wallaby", "Wallet", "Walrus", "Warthog", "Wasp", "Watch", "Weasel", "Whale", "Whistle", "Window", "Wolf", "Wolverine", "Wombat", "Yak", "Zebra" };
		return $"{adjectives[rand.Next(adjectives.Length)]}{nouns[rand.Next(nouns.Length)]}";
	}

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

	public static string GetRandomMatchAlias (int length, bool useLetters = true)
	{
		string result = "";
		for (int i = 0; i < length; i++)
		{
			if (useLetters)
				result += letters[rand.Next(0, letters.Length)];
			else
				result += rand.Next(0, 10).ToString();
		}
		return result;
	}
}