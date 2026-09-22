# Testing

The mod ships with the tooling that was used to build it: you can run two or three copies of the game on one PC
and drive them from the outside, which is how every feature here was checked.

## Two copies on one PC

1. Put a `steam_appid.txt` containing `3510740` next to `Normal Golf Game.exe`, so the exe starts directly instead
   of relaunching through Steam. (Delete it afterwards if you like; it changes nothing else.)
2. Launch copies with separate save sandboxes. Each profile copies your real saves once and never writes to them:

```powershell
$exe = "D:\SteamLibrary\steamapps\common\Normal Golf Game\Normal\Normal Golf Game.exe"
& $exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -ngmp-profile hostA   -ngmp-name Alice -ngmp-loopback -ngmp-host 7777 -ngmp-autoplay nine
& $exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -ngmp-profile clientB -ngmp-name Bob   -ngmp-join 127.0.0.1:7777        -ngmp-autoplay nine
```

Two things to know: Unity remembers the test window size, so set your resolution back in the game's settings
afterwards; and with `steam_appid.txt` present Steam Cloud will sync the save folder around those launches
(contents stay yours, timestamps change).

### Flags

| Flag | |
|---|---|
| `-ngmp-profile NAME` | Redirect all saves into `ngmp_profiles\NAME`, and use a separate config file |
| `-ngmp-name NAME` | Player name for this copy |
| `-ngmp-host PORT` / `-ngmp-join IP:PORT` | Host or join as soon as the main menu loads |
| `-ngmp-loopback` | Host on 127.0.0.1 only: no firewall prompt |
| `-ngmp-autoplay story\|nine` | Start a game from the main menu automatically |
| `-ngmp-quit SECONDS` | Quit this long after the course loads |

## Driving a running copy

Write lines into `Normal\BepInEx\ngmp_cmd_<profile>.txt`; the game runs them within half a second, deletes the file,
and logs the result to `BepInEx\LogOutput.log` (`.log.1` for the second copy, `.log.2` for the third).

```powershell
"startnine"          > "...\Normal\BepInEx\ngmp_cmd_hostA.txt"
"hole 1 3"           > "...\Normal\BepInEx\ngmp_cmd_hostA.txt"
"screenshot mytest"  > "...\Normal\BepInEx\ngmp_cmd_hostA.txt"   # saved under BepInEx\ngmp_debug
```

| Verb | |
|---|---|
| `state` | Local pose/ball/flags plus every remote player's, and cursor/input state |
| `scores` | Local and remote Front Nine cards, and the course pars |
| `buf ID` | Snapshot buffer for a player: contents, clock offset, render time |
| `dump` | Full scene report: layers, every camera with culling mask, player/ball hierarchies, shaders, fonts |
| `screenshot NAME` | Writes `BepInEx\ngmp_debug\<profile>_NAME.png` |
| `tp X Y Z [YAW]`, `tpnear ID DIST`, `walkto X Z [SPEED]`, `yaw DEG`, `goto ID` | Move around |
| `golf`, `walk`, `hit POWER`, `ball N`, `retake`, `holed PAR` | Drive gameplay |
| `startnine`, `hole N SCORE` | Front Nine round control |
| `host [PORT]`, `join IP:PORT`, `leave`, `password X`, `chat TEXT` | Session control |
| `menu [off]`, `scoreboard [off]`, `tomenu`, `play story\|nine`, `timescale N`, `bindings`, `quit` | Misc |

## Cleaning up after testing

Test profiles live in `%USERPROFILE%\AppData\LocalLow\Luke Muscat\Normal Golf Game\ngmp_profiles\`, with per-profile
config files in `BepInEx\config\normalgolf.multiplayer.<profile>.cfg`, screenshots and dumps in `BepInEx\ngmp_debug\`.
All of it is safe to delete.
