# 18 — Audio: Godot's engine + Core sound events (PR 12 + post-milestone content)

**Rewritten 2026-08-05 under ruling #30 (final product = Godot).** The original draft's backend
spike (NAudio/SDL/OpenAL evaluation, a hand-rolled `IAudioService`) existed solely because
*Avalonia has no audio engine*. Godot ships a complete one — `AudioStreamPlayer`/`2D`, buses,
mixing, positional audio — so the spike is **obsolete**. What remains is wiring and content.
Standing rulings unchanged: **SFX first, music later; semi-retro/chiptune + atmospheric hybrid.**

## Architecture: Core emits, Godot plays

- Core already gets a transient **`SoundEvent`** stream for the stealth system (note 11 —
  footsteps, attacks, doors, chest channels, mining, with world positions and kinds). The same
  events are the audio triggers: **AI ears and player speakers share one emission model.** Until
  PR 5b lands, a minimal `GameState.SoundsThisTick` list can ship early with just the obvious
  emitters (attack fired, hit landed, chest opened, level-up, UI click) and grow into the full
  note-11 table.
- The Godot side (`GameHost` or a small `AudioDirector` node) drains the per-tick events and
  plays mapped streams: `AudioStreamPlayer2D` at the event's world position for world sounds
  (free distance attenuation + pan from the camera), plain `AudioStreamPlayer` for UI.
- Mapping is data: `Data/Config/audio.json` — event kind/id → stream path, base volume, pitch
  variance (±5% keeps repeated footsteps from droning). Assets under `Godot/Audio/` as imported
  Godot resources (OGG preferred).
- **Buses**: Master → SFX / Music / UI. The Godot client's existing `user://client-settings.json`
  already stores master volume; extend it with per-bus sliders when the Settings screen lands.
- Headless safety is automatic: Core emits plain data; the `TEST_*` suites never touch Godot.

## PR 12 (S): proof-of-pipeline

Wire ~6 SFX end-to-end (attack swing, projectile hit, chest open, level-up, UI click, footstep)
through real Core events → `audio.json` → players. Verify: audible latency fine at tick rate,
16+ simultaneous voices OK (Godot's default polyphony handles this), volume persists via
client settings, zero regression impact (Core-side events asserted in a `TEST_SOUNDEVENTS`
extension — counts and positions, no audio device involved).

## Content pass (post-milestone)

- **SFX coverage** in priority order: combat per `VisualStyle`/element (palette-matched tints in
  sound: each element gets a cast/impact identity), statuses, UI, doors/chests/mining, footsteps
  by terrain (stone/grass/water — the note 11 emission sites carry terrain), town ambience
  one-shots (smithy hammer, market murmur).
- **Music**: ambient beds per context — title, town day/night, dungeon per-theme (the
  `DungeonTheme` palettes already imply moods: Sewer drips, Library hush, Forge drones),
  guardian-arena sting. Chip-adjacent leads over atmospheric pads per the aesthetic ruling.
- **Events lead perception**: the dungeon-break horn or flood bell plays *before* the message-log
  line (note 17 integration).
- Sourcing: CC0/CC-BY packs (OpenGameArt, Kenney, freesound) + tracker originals later; keep a
  `CREDITS.md` from day one so licensing never becomes archaeology.

## Verification

PR 12: the six proof sounds audible in the Godot editor run; `TEST_SOUNDEVENTS` asserts Core-side
emission (kinds/positions/counts) headlessly; client-settings volume round-trips. Content pass:
coverage checklist against the SFX table + an hour's play without repetition fatigue (owner's
ears — this one can't be headless).
