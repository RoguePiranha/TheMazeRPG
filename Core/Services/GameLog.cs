using System;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Lightweight logging shim. Verbose per-tick combat/diagnostic output is off by
/// default and only prints when MAZE_VERBOSE=1, so the shipped app isn't spammed.
/// High-level milestones (headless sim harness output) use Console.WriteLine directly.
/// </summary>
public static class GameLog
{
    public static bool Verbose { get; set; } =
        Environment.GetEnvironmentVariable("MAZE_VERBOSE") == "1";

    public static void Debug(string message)
    {
        if (Verbose)
            Console.WriteLine(message);
    }
}
