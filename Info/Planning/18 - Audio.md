# 18 — Audio: backend spike now, content pass after (PR 12 + post-milestone)

**Owner rulings 2026-08-05:** backend spike early per recommendation (the "spike" = a small throwaway-tolerant PR whose only job is picking and proving the audio library, because that's the platform risk — Avalonia has no built-in audio, and discovering the chosen library stutters under Skia render load is a *now* problem, not a ship-week problem). SFX first, music later. **Aesthetic: semi-retro/chiptune + atmospheric hybrid** — chiptune-leaning SFX to match the pixel look, atmospheric/ambient beds for music.

## PR 12 — the spike (S)

1. **Evaluate** (Windows-first, cross-platform-aware): **NAudio** (simple, Windows-native, battle-tested — likely winner for now), SDL2-CS mixer, OpenTK/OpenAL. Criteria: latency good enough for combat SFX, N simultaneous voices (~16), no GC-pressure per play, coexists with the Avalonia/Skia render loop.
2. **Thin interface so the choice is swappable** — nothing outside this service may touch the library:

```csharp
public interface IAudioService
{
    void PlaySfx(string id, float volume = 1f, float pan = 0f);
    void PlayMusic(string track, float fadeSeconds = 1f);   // loops; null = fade out
    void StopAll();
    float SfxVolume { get; set; }    // settings-menu hooks (the pause menu's dead
    float MusicVolume { get; set; }  // Settings button finally gets a first resident)
}
```

3. **Prove the pipeline with ~6 placeholder SFX** wired at real call sites: attack swing, projectile hit, chest open, level-up, UI click, footstep. Assets under `Assets/Audio/` (embedded like the font), `Data/Config/audio.json` maps id → file + base volume + random pitch-variance (±5% keeps repeated footsteps from droning).
4. Headless safety: `IAudioService` no-ops when the audio device is absent or under `TEST_*` runs — the 14-suite regression must never depend on a sound card.

## Content pass (post-milestone)

- **SFX coverage** in priority order: combat (per `VisualStyle` — arrow, sword arc, each element's cast/impact tinted like the palette), statuses, UI, doors/chests/mining, footsteps by terrain (stone/grass/water — the note 11 noise events are the trigger points, so the player's ears and the AI's "ears" share emission sites), town ambience one-shots (smithy hammer, market murmur).
- **Music**: ambient beds per context — title, town day, town night, dungeon (per-theme variants eventually: forest floor birdsong-with-pads, scorched-arena low drones), guardian arena sting. Hybrid palette per the ruling: chip-adjacent leads over atmospheric pads.
- **Events**: sound leads perception — the dungeon-break horn or the flood bell plays *before* the message log line (offscreen events get audible arrival, note 17).
- Sourcing: CC0/CC-BY packs (OpenGameArt, Kenney, freesound) + tracker-made originals later; keep a `CREDITS.md` from day one so licensing never becomes archaeology.

## Verification

Spike: perceived latency check (SFX within ~50 ms of trigger), 16-voice stress under a busy combat scene, zero regression-suite impact, volume settings persist. Content pass: coverage checklist against the SFX table + an hour's play without audible repetition fatigue (owner's ears — this one can't be headless).
