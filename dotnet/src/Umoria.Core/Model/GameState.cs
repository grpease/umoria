using Umoria.Core.Config;

namespace Umoria.Core.Model;

// Hand-translated subset of src/game.h's Game_t (the global `game`), added
// in M7 for the fields the player status-effect updates touch. The rest
// (seeds, wizard mode, screen state, the treasure list -- the latter is
// already the TreasureList class) joins as later milestones need it.
public sealed class GameState
{
    // `true` if character has died
    public bool CharacterIsDead { get; set; }

    // Character beat the Balrog
    public bool TotalWinner { get; set; }

    // Handle teleport traps
    public bool TeleportPlayer { get; set; }

    // Player has a free turn, so do not move creatures (M8)
    public bool PlayerFreeTurn { get; set; }

    // How many times to repeat a specific command -CJS-
    public uint CommandCount { get; set; }

    // What the character died from: starvation, Bat, etc.
    public string CharacterDiedFrom { get; set; } = "";

    // config::options (src/config.cpp) -- user-settable game options.
    public GameOptions Options { get; } = new();
}
