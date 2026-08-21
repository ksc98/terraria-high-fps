# terraria-high-fps

A tModLoader mod that renders Terraria above 60 FPS. The world still simulates at exactly 60 updates
per second, so nothing about gameplay speed changes — only how many distinct frames you see.

Built against **Terraria 1.4.5.7** on a development build of tModLoader. It will not load on tModLoader
stable, which is still on 1.4.4.9. See [Compatibility](#compatibility).

## What it does

Vanilla draws once per world update, so a 240Hz display shows the same 60 distinct positions. This mod:

- **Interpolates motion.** Every entity's position and rotation is snapshotted at the end of each update.
  During the draw, entities are placed between the last two ticks according to how far into the current
  tick the frame is, then restored immediately after, so no game logic ever sees an interpolated value.
- **Guards against teleports.** Anything that moved more than a configurable distance in one tick is drawn
  where it actually is, rather than smeared across the gap.
- **Throttles draw-time particles.** Vanilla spawns dust from *drawing* code — torch flames, tile entity
  dust — which would otherwise scale with the frame rate. Extra frames drop those spawns.
- **Polls the mouse every frame.** The cursor is otherwise sampled once per update, so it moves in 60Hz
  steps regardless of frame rate. This also shaves input latency, since the next update reads a fresher
  position.
- **Forces Frame Skip off while in a world.** The "Subtle" mode busy-waits in `Main.EndDraw` until a whole
  update's worth of time has passed. The menu keeps whatever you had set.

Everything is toggleable per category in the mod config, which is also how you bisect a visual artifact.

## Known gaps

- **Multiplayer is minimal.** On a client, only locally simulated entities are interpolated. Remote ones
  move in jumps as net updates land and vanilla already smooths those via `Entity.netOffset`;
  interpolating on top fights that correction and reads as teleporting.
- The player's aim uses a handful of discrete body sprite frames, so the arm still snaps between poses.
  There is no in-between sprite to draw.
- Not tested against: whip animations, tile animation rates, cloud parallax, item placement previews.

## Compatibility

The mod calls four things that only exist in Terraria 1.4.5: `WorldItem`, `Main.TARGET_FRAME_TIME`,
`Main.improvedSubtleFrameSkip`, and `Main.DoUpdateInWorld`'s current signature. Everything else is stable
tModLoader API. Hooks are `ModSystem.PostUpdateEverything` and the `Main.OnPreDraw`/`OnPostDraw` events
rather than detours on private methods, so a tModLoader release for 1.4.5 should need a rebuild and
nothing more.

A 1.4.4.9 build would need: `Item` in place of `WorldItem`, a stopwatch instead of the frame-time
constant, and no frame-skip override.

## Building

```
<tModLoader>/LaunchUtils/ScriptCaller.sh -build <path to this directory>
```

Close the game first. The packaging step silently no-ops if the game is holding `Mods/SmoothFrames.tmod`
open, while still printing "Compilation finished with 0 errors".

## Credit

The idea, and the name of the Motion Interpolation option, come from
[High FPS Support](https://steamcommunity.com/sharedfiles/filedetails/?id=3119712528) by Stellar, which
does this for 1.4.4 and handles far more edge cases. None of its code was read or used; this is a
from-scratch implementation against 1.4.5.7.
