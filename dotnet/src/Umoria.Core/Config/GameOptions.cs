namespace Umoria.Core.Config;

// Hand-translated subset of config::options (src/config.cpp) -- the
// user-settable options, with the original defaults. Only what ported code
// reads so far.
public sealed class GameOptions
{
    // Stop running when the map shifts
    public bool RunPrintSelf { get; set; } = false;
}
