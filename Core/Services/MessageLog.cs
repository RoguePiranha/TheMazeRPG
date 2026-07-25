using System.Collections.Generic;

namespace TheMazeRPG.Core.Services;

/// <summary>Category of a player-facing game message — drives its color in the HUD log.</summary>
public enum MessageKind
{
    System,   // floor transitions, saves, town — gray
    Combat,   // kills, guardian events — red
    Loot,     // items, gold, chests — gold
    LevelUp,  // level-ups — green
    Warning   // traps, death, danger — orange
}

public class GameMessage
{
    public string Text { get; }
    public MessageKind Kind { get; }
    /// <summary>GameState.TickCount when the message was added — the HUD uses this to fade old
    /// messages out.</summary>
    public int Tick { get; }

    public GameMessage(string text, MessageKind kind, int tick)
    {
        Text = text;
        Kind = kind;
        Tick = tick;
    }
}

/// <summary>
/// The player-facing event feed ("Found a Sword", "The Guardian stirs...") shown at the bottom
/// of the game view — SimpleRPG's message log, adapted to real-time. Owned per-GameState (not a
/// static service) so parallel headless GameStates don't interleave. Distinct from GameLog,
/// which is developer console/debug output.
/// </summary>
public class MessageLog
{
    private const int Capacity = 50;
    private readonly List<GameMessage> _messages = new();

    public IReadOnlyList<GameMessage> Messages => _messages;

    public void Add(string text, MessageKind kind, int tick)
    {
        _messages.Add(new GameMessage(text, kind, tick));
        if (_messages.Count > Capacity)
        {
            _messages.RemoveAt(0);
        }
    }

    public void Clear() => _messages.Clear();
}
