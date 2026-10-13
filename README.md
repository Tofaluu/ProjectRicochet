# Project Ricochet

A competitive multiplayer puzzle game. Every player gets the same board: robots slide in a straight
line until they hit a wall or another robot, and the goal is to get the target robot onto the target
square in as few moves as possible. The first solution starts a timer, shorter solutions extend it,
and the shortest solution when time runs out wins the round.

Early prototype: the graphics and UI are placeholders.

## How it's built

| Folder | What it is |
|---|---|
| `SharedLogic/` | Game rules, board generator and optimal solver in plain C#, used by both the server and the Unity client |
| `BackendServer/` | ASP.NET Core SignalR server: lobbies, matches and round timers. It has the final say on every move |
| `UnityClient/` | Unity 6 game client |
| `*.Tests/` | xUnit tests for the rules, the server's lobby/match logic and the client's state |

## Running it locally

Needs the .NET 9 SDK, and Unity 6 (6000.6.5f1) for the client.

```
dotnet test RicochetGame.sln
dotnet run --project BackendServer --launch-profile http
```

Then open `UnityClient/` in Unity, open `Assets/Scenes/Scratchpad.unity`, press Play and join with
server `localhost:5022` and any lobby code.

## License

Copyright (c) 2026 Thomas Liu. All rights reserved.

The source is published so people can read it. No license is granted to copy, modify, distribute or
sell this code or the game, in whole or in part. Third-party components keep their own licenses; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Inspired by the board game *Ricochet Robots*. This project isn't affiliated with or endorsed by its
designer or publishers, and uses its own board layouts.
