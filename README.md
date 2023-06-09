# Functions Game Backend

Backend for a turn-based game running on a serverless architecture. The game implemented is rock-paper-scissors for the Azure Functions platform, but the code is modular enough to change the game and the service provider more easily.

![Rock-Paper-Scissors Game GIF](https://github.com/kalkatos/instruction-images/blob/main/rps_multiplayer_v0.1.22-short.gif "Rock-Paper-Scissors")

The front-end is implemented in a rock-paper-scissors game for Unity [here](https://github.com/kalkatos/rpsls) using the following parts: [C-sharp base scripts](https://github.com/kalkatos/csharpgame), with [Unity specific utility scripts](https://github.com/kalkatos/unitygame), an implementation of [Scriptable Objects Architecture](https://github.com/kalkatos/scriptable) to emit UI Signals, and a modular [network](https://github.com/kalkatos/network) framework.

<!---
For a thorough explanation of how this works encompasing all modules, please refer to my website [here](https://kalkatos.com/serverlessgame/).
-->

## How it Works

The main functionality is implemented in the class [MatchFunctions](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Functions.cs). I comment the functions in more detail below. The interaction between the client and the 'server' is by polling, so the client must ckeck on the server repeatedly to know if the state of the game has changed and treat the changes.

### Functions

Every function is called using a [Request](https://github.com/kalkatos/network-model/tree/907b9ebefd535049fa0bce644ccc9eefcfb59403/Requests) and responds with a [Response](https://github.com/kalkatos/network-model/tree/907b9ebefd535049fa0bce644ccc9eefcfb59403/Responses).

#### Log In

The first interaction between the client and server on every session. Must be called with an unique device identifier. Creates (if new) or updates a [PlayerRegistry](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Registry/PlayerRegistry.cs) to the database.

#### Get Game Settings

Each [PlayerRegistry](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Registry/PlayerRegistry.cs) has a [PlayerInfo](https://github.com/kalkatos/network-model/blob/907b9ebefd535049fa0bce644ccc9eefcfb59403/Info/PlayerInfo.cs) field, which in turn has a CustomData field with public data about that player. This function allows to send custom data to that field. 

#### Find Match

Registers this player as looking for match and tries to match them right away if enough players are available. A temporary [entry](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Registry/MatchmakingEntry.cs) will be written to the database with this player info for future players finding matches.

#### Get Match

Get info about a match, or tries to match the requester player if not matched yet. If a match is created, a [MatchRegistry](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Registry/MatchRegistry.cs) will be written to the database.

#### Leave Match

Takes measures to mark this player as retreated from the match. If only looking for match, deletes the entry from this player. If already in a match, sets this player as a "RetreatedPlayer".

#### Send Action

Sends an [ActionInfo](https://github.com/kalkatos/network-model/blob/907b9ebefd535049fa0bce644ccc9eefcfb59403/Info/ActionInfo.cs) with custom information to alter the match state. The game implemented must check and interpret the action sent.

#### Get Match State

Get the current match state if it's different from the last known state. Writes and updates a [StateRegistry](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Registry/StateRegistry.cs).

### Interfaces

#### IService [(Link)](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Services/IService.cs)

Provides methods to be implemented for a service that will make specific reads and writes to the database.

#### IGame [(Link)](https://github.com/kalkatos/functions-game/blob/main/FunctionsGame/Games/IGame.cs)

Contains methods to be implemented according to the rules of the game. It checks actions sent and modifies the match state.
