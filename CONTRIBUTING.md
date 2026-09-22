# Contributing

Forks, issues and pull requests are all welcome. The code is public domain, so you never need permission to take
it somewhere else.

## Reporting a bug

The useful things to include:

* What each player was doing (hosting or joining, walking or golfing, story or Play Nine).
* `Normal\BepInEx\LogOutput.log` from everyone involved, ideally.
* Your mod version (shown in the F8 menu) and whether everyone was on the same one.
* For connection problems: LAN or internet, and whether the port is forwarded.

## Building

See [the README](README.md#building-from-source). In short: .NET 8 SDK, BepInEx 5 installed in the game,
then `.\build.ps1`. Close the game first — it locks the plugin DLL.

`docs/TESTING.md` explains how to run two or three copies of the game on one PC and drive them from a command
file, which is how this was developed. It's much faster than finding a second person for every change.

## Things worth knowing before you change something

* **`docs/ARCHITECTURE.md`** explains the parts of the game that shaped the design — no player model, two
  different sources for the player's pose, several cameras rendering each frame, and where scores really live.
* **Bump `Protocol.Version`** in `src/Net/Protocol.cs` whenever the wire format changes. Mismatched players then
  get a clear "update the mod" message instead of strange behaviour.
* **Keep hooks defensive.** Patches are applied one at a time so a game update degrades one feature rather than
  breaking the mod; please keep new hooks in that style, and keep reads of game state inside try/catch where the
  object might not exist yet.
* **Don't change other players' games.** Remote avatars and balls are visual only, with no colliders, and the mod
  never writes to saves. Keeping that true is what makes it safe to install.
* Style: match what's there — the code is plain C# with comments that explain *why*, not what.

## A note on the AI authorship

This mod was written by Claude (Anthropic's Claude Opus 5); see the README. That has no bearing on contributions:
review it like any other code, change whatever you like, and use AI or not as you prefer. If you do submit
AI-assisted changes, please say so in the PR and make sure you've actually run them in-game.
