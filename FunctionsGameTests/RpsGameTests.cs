using Kalkatos.FunctionsGame.Rps;
using Kalkatos.FunctionsGame.Registry;
using Kalkatos.Network.Model;
using Newtonsoft.Json;


RpsGame game = new RpsGame();

MatchRegistry match = new MatchRegistry
{
	MatchId = "Match1",
	PlayerIds = new string[] { "Player1", "Player2" },
	Region = "US",
	HasBots = false,
	Status = (int)MatchStatus.AwaitingPlayers,
	CreatedTime = DateTime.UtcNow,
};

StateRegistry state = game.PrepareTurn("Player1", match, null);

Console.WriteLine(game.IsActionAllowed("Player1", new ActionInfo
{
	PrivateChanges = new Dictionary<string, string> { { "Handshaking", "1" } }
}, match, state));

Random rand = new Random();

string[] moves = new string[] {"ROCK", "PAPER", "SCISSORS"};

Console.WriteLine("Press Key");

int executions = 1;
while (true)
{
	var key = Console.ReadKey();
	if (key.Key == ConsoleKey.Escape)
		break;
	if (key.Key == ConsoleKey.V)
	{
		Console.WriteLine("Trying to send action: " + game.IsActionAllowed("Player1", new ActionInfo
		{
			PrivateChanges = new Dictionary<string, string> { { "MyMove", "ROCK" } }
		}, match, state));
	}
	state = game.PrepareTurn("Player1", match, state);
	Console.WriteLine($"Execution: {executions} | Time: {DateTime.UtcNow}\n");
	Console.WriteLine(JsonConvert.SerializeObject(state, Formatting.Indented));
	Console.WriteLine("------------------------");
	executions++;
	if (state.GetPublic("Phase") == "1" && state.GetPrivate("Player1", "MyMove") == "")
		state.UpsertPrivateProperties(("Player1", "MyMove", moves[rand.Next(0, 3)]), ("Player2", "MyMove", moves[rand.Next(0, 3)]));
}

