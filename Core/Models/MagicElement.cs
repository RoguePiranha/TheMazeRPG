namespace TheMazeRPG.Core.Models;

/// <summary>
/// The elemental flavor of a magic attack, used to give spells distinct default colors (and,
/// later, resistances/status effects). None = not elemental / use the visual's own default color.
/// </summary>
public enum MagicElement
{
    None,
    Mana,      // pale blue — generic arcane energy
    Arcane,    // violet
    Fire,      // reddish-orange
    Ice,       // pale cyan-white
    Poison,    // neon green
    Water,     // blue
    Lightning, // yellow
    Life,      // living green
    Death,     // dark / necrotic
    Light,     // white-gold (the perception element; distinct from divine Holy)
    Shadow,    // dark purple
    Earth,     // brown
    Air,       // off-white / cloud
    Sonic,     // light blue (bardic sound)
    Void,      // matte absence, indigo rim
    // Holy is a divine *state* per the magic-system doc, not a source; kept here as a legacy
    // color tint for existing holy attacks and treated as non-affinity. See MagicElements.
    Holy       // golden
}
