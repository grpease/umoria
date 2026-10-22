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

    // Used in counted commands: repeat the last direction (M10 reads it in
    // getDirectionWithMemory(); M15's command loop sets it)
    public bool UseLastDirection { get; set; }

    // What the character died from: starvation, Bat, etc.
    public string CharacterDiedFrom { get; set; } = "";

    // config::options (src/config.cpp) -- user-settable game options.
    public GameOptions Options { get; } = new();

    // Seed for initializing magic items (Potions, Wands, Staves, Scrolls,
    // etc.) -- ObjectIdentification.MagicInitializeItemNames (M11)
    public uint MagicSeed { get; set; }

    // Track inventory commands -CJS- -- the command an inventory menu was
    // left in, which the main loop (M15) re-enters next turn (M11)
    public char DoingInventoryCommand { get; set; }

    // game.screen -- which inventory screen is up, and the wear/wield range
    // of the pack. Kept because the inventory commands branch on it
    // (Items.InventoryCommands); its left/bottom drawing positions are the
    // UI's (M14). (M11)
    public InventoryScreen Screen { get; } = new();

    // src/ui.cpp screen_has_changed -- set when something redraws over an
    // open inventory menu (the real terminal layer and monster visibility
    // changes), which makes the next re-entered inventory command ask
    // "Continuing with inventory command?". Nothing in the port sets it yet:
    // that is UI (M14). (M11)
    public bool ScreenHasChanged { get; set; }
}

// src/game.h enum class Screen -- inventory command screen states (M11)
public enum Screen
{
    Blank = 0,
    Equipment,
    Inventory,
    Wear,
    Help,
    Wrong,
}

// src/game.h Game_t::screen, less the drawing positions (M11)
public sealed class InventoryScreen
{
    public Screen CurrentScreenId { get; set; } = Screen.Blank;
    public int WearLowId { get; set; }
    public int WearHighId { get; set; }
}
