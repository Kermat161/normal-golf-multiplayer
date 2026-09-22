# Architecture

How the mod plugs into Normal Golf Game, and why each piece looks the way it does.

Target: **Normal Golf Game** (Steam app `3510740`), game build *"Build 9:16AM September 21"* (Steam build `25426758`),
Unity `6000.3.11f1`, **Mono** scripting backend. BepInEx 5.4.23.5, LiteNetLib 2.1.4, plugin targets netstandard2.1.

## Layout

```
src/
  Plugin.cs                  BepInEx entry; creates one persistent GameObject holding the components below
  ModConfig.cs               BepInEx config (name, colour, ports, keys, visuals)
  Net/Protocol.cs            Wire format: message ids, PlayerInfo, PlayerState (~45 bytes), ScoreCard, events
  Net/NetSession.cs          LiteNetLib listen-server: handshake, host relay, disconnect reasons
  Game/LocalPlayer.cs        Reads the local pose and ball out of the game each tick; "Go to" teleport
  Game/ScoreTracker.cs       Mirrors the local Front Nine round (the game's LMUGC system) and broadcasts it
  Game/GamePatches.cs        Harmony hooks (see below)
  Remote/SnapshotBuffer.cs   Per-player interpolation buffer with clock-offset estimation
  Remote/RemotePlayerView.cs Primitive-built golfer avatar, animation, remote ball + trail + labels
  Remote/RemoteWorld.cs      Owns remote players across scene loads; per-camera label billboarding
  Remote/Visuals.cs          Materials/meshes/fonts/sounds borrowed from the game's own assets
  UI/MultiplayerUI.cs        IMGUI menu (F8), scoreboard (F9), HUD, toasts, chat (T)
  DevTools.cs                Test flags and a command file for driving running instances
  SaveSandbox.cs             -ngmp-profile save redirection for side-by-side test instances
  Diagnostics.cs             Scene/camera/layer dump, used while reverse engineering
```

## Networking

A **listen server**: the host is player 1 and relays every client's messages to the other clients, so clients only
ever talk to the host. Transport is UDP via LiteNetLib.

* **Handshake inside the connection request.** The client puts magic, protocol version, password and its player info
  into the LiteNetLib connect request, so the host can accept or reject with a readable reason before a peer exists.
* **State**: `Msg.State`, unreliable, 20 Hz, ~45 bytes — position, yaw, pitch, flags (in-world, golfing, crouched,
  ball visible/trail/moving, Play Nine), club, ball position and a ball "epoch". Each carries a sequence number and
  the sender's session clock.
* **Events**: `Shot`, `Holed`, `Chat`, `PlayerInfo`, `Score` are reliable-ordered.
* **Interpolation**: receivers render each player ~100 ms in the past (`SnapshotBuffer`), estimating the clock offset
  from the fastest recent packets, so jitter and a lost packet don't produce stutter. Teleports would otherwise be
  interpolated as a slide across the map, so the ball carries an **epoch** that bumps on every reset, and large
  jumps snap instead of lerping.
* Bump `Protocol.Version` whenever the wire format changes; mismatched peers are refused with a clear message.

## Hooks into the game

Patches are applied **individually** in `GamePatches.Apply`, so a game update that renames one method disables that
one feature with a warning in the log instead of stopping the whole mod from loading.

| Target | Why |
|---|---|
| `HitManager.ResetBall` (postfix) | Bumps the ball epoch: a reset teleports the ball, and receivers must snap |
| `HitSequeneceManager.HitSequence` (prefix) | The coroutine every real swing starts — broadcasts the shot |
| `HitSequeneceManager.ShowBallInHoleFeedback` (prefix) | Every hole-out path reports through it |
| `LMUGC.StartChallenge` (postfix) | The red button at the first tee: a new Front Nine round |
| `LMUGC.CompleteHole` (prefix) | Records each hole score, including ticket-skipped holes |
| `SaveManager.AddScoreToCard` (postfix) | End of a Play Nine round, while the final card is still intact |
| `ClubSwing.Update`, `ClubSwingFPS.Update`, `HitSequeneceManager.RotateCamera` (prefix) | Skipped while our UI owns the mouse |

## Things the game does that shaped the design

* **There is no player model.** The hands and the golfer are FMV video clips rendered to textures, so a remote player
  had to be built from scratch: `RemotePlayerView` assembles a small golfer out of primitives and clones the ball's
  URP/Lit material (cloning a material the game already renders guarantees the shader variant exists in the build).
* **Pose comes from two different places.** Walking uses the ECM2 `Character` (`MoveAndHitController.m_fpsCharacter`).
  Golfing disables that object entirely and positions `m_golferHolder` instead, with the ball at the holder's local
  +X (0.79), so a golfing avatar stands at the holder and faces holder yaw + 90°.
* **Several cameras render the world each frame.** Avatars live on the `IgnoreForRTEXCams` layer, which the
  first-person camera, the golfer (FMV) camera and the ball-chase camera all draw, but the small swing/spin/club
  panel cameras do not. Name tags are re-oriented per camera in `RenderPipelineManager.beginCameraRendering`.
* **Scoring is the game's own.** A hole is `Ball.m_currentShot + 1`, and a retake after water does `m_currentShot++`,
  so penalties are already included; nothing is recomputed. The catch: `LMUGC.CompleteNormalGolfRound` **clears**
  `RunSave.lmugcScore` immediately after the ninth hole, so scores are captured as they are recorded, and the final
  card is taken at `SaveManager.AddScoreToCard` while it still exists. A round already in progress is imported from
  the save on load, so the mod can be enabled mid-round.
* **Input is one `PlayerInput`**, deactivated while the menu or chat is open. The swing panel additionally reads the
  *legacy* mouse API, which that can't block, so `ClubSwing.Update` is skipped during that time — otherwise clicking
  a button in the menu could start a swing.
* **Saves**: `SaveSandbox` can redirect every `SaveManager` path into a profile folder. That exists purely so two
  copies can run on one PC during testing without touching real saves.

## Ideas for later

* Spectate: follow another player's ball with your chase camera
* Show players on the in-game map
* Batch every player's snapshot into one packet per tick for large sessions
* Shared round mode: one card, alternating turns
