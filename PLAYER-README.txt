NORMAL GOLF MULTIPLAYER  (fan mod, not affiliated with the game's developer)
=========================================================================

See your friends walking around the course, watch them swing, and follow their
golf balls in real time. Connect directly by IP address and port.

INSTALL
-------
1. Find the game folder: Steam > right-click Normal Golf Game > Manage >
   Browse local files. Open the "Normal" folder inside it, the one that
   contains "Normal Golf Game.exe".
2. Extract everything from this zip into that folder, so winhttp.dll sits
   right next to "Normal Golf Game.exe".
3. Start the game from Steam as usual.

Everyone who plays together needs the same version of the mod.

PLAYING TOGETHER
----------------
Press F8 in the game (main menu or on the course) to open the Multiplayer menu.

  Host: pick a port (default 7777), optionally a password, and click Host game.
        Give your friends your IP address and the port.
  Join: type the host's IP address and port, then click Join. If the host
        set a password, type it into the Password box first.

In a session:
  T      open the chat box (Enter sends, Esc cancels)
  F8     player list, ping, distance, the Front Nine scoreboard, and a "Go to"
         button that teleports you next to another player (while walking)
  F9     show/hide the scoreboard as an overlay while you play

FRONT NINE SCOREBOARD
---------------------
In Play Nine, everyone's card is shown side by side: hole, par, then a row per
player. Scores come from the game's own counters, so water and retake penalties
are included exactly as the game counts them. The hole a player is on shows
their strokes so far in grey. A row resets when that player presses the red
button to start a new round, and stops updating once they finish the ninth hole.
Everyone keeps playing their own round at their own pace.

HOSTING OVER THE INTERNET
-------------------------
Same house or LAN: use the host's LAN IP (shown in the F8 menu).
Over the internet, choose one:
  * Port forwarding: on the host's router, forward UDP port 7777 (or your
    chosen port) to the host PC. Friends then join with the host's public IP
    (search "what is my ip").
  * A virtual LAN such as Tailscale, ZeroTier or Radmin VPN. Everyone joins
    the same network and connects using the host's VPN address. No router
    setup needed.
The first time you host, Windows may ask whether Normal Golf Game can use
the network. Click Allow, otherwise nobody can reach you.

WHAT'S SHARED
-------------
Player positions (walking, crouching, looking around), golf stance and swings,
each player's ball (flight, trail, where it lands, and the ball skin they
picked), hole-outs, and chat. Everyone keeps their own save, progress and
money. Balls and players don't collide with each other.

UNINSTALL
---------
Delete winhttp.dll from the game's "Normal" folder (this disables all mods),
or delete the BepInEx folder, winhttp.dll, doorstop_config.ini,
.doorstop_version and changelog.txt to remove everything.

SETTINGS
--------
BepInEx\config\normalgolf.multiplayer.cfg (created after the first launch):
name, colour, keys, name tags, remote sounds, max players, and more.
