namespace TheMazeRPG.Core.Models;

/// <summary>What kind of noise a sound event is — decides how hard it lands on an enemy's
/// awareness (Planning note 11 §1). Combat and Alarm alert outright; the rest only ever make a
/// listener suspicious.</summary>
public enum SoundKind
{
    Step,
    Combat,
    Door,
    Work,
    Alarm
}

/// <summary>
/// A transient noise: emitted into GameState's per-tick sound list, consumed by the awareness
/// pass the same tick, then cleared. Never rendered, never persisted. What a sound leaks is the
/// *location* of its source, not the identity — a heard enemy investigates the spot, it doesn't
/// magically know who stood there.
/// </summary>
public readonly record struct SoundEvent(float X, float Y, float Radius, SoundKind Kind, bool FromHero);
