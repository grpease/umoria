namespace Umoria.Core.Config;

// Hand-translated subset of config::options (src/config.cpp) -- the
// user-settable options, with the original defaults. Only what ported code
// reads so far.
public sealed class GameOptions
{
    // (The original's comments on find_bound and run_print_self are swapped
    // in config.cpp; these describe what the code actually does.)

    // Stop running when the map shifts
    public bool FindBound { get; set; } = false;

    // Cut corners while running
    public bool RunCutCorners { get; set; } = true;

    // Check corners while running
    public bool RunExamineCorners { get; set; } = true;

    // Run through open doors
    public bool RunIgnoreDoors { get; set; } = false;

    // Print yourself on a run (slower)
    public bool RunPrintSelf { get; set; } = false;

    // Highlight magma and quartz veins
    public bool HighlightSeams { get; set; } = false;

    // Prompt to pick something up
    public bool PromptToPickup { get; set; } = false;
}
