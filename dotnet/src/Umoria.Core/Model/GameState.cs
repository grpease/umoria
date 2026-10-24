using Umoria.Core.Config;

namespace Umoria.Core.Model;

// Hand-translated subset of src/game.h's Game_t (the global `game`), added
// in M7 for the fields the player status-effect updates touch; M12 added
// the seeds and flags the save file and scoreboard read. The rest
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

    // Seed for town generation (game.town_seed) -- dungeonGenerate()'s town
    // level reseeds the RNG with it, so it is save-file state. (M12)
    public uint TownSeed { get; set; }

    // Don't save score until character generation is finished (M12)
    public bool CharacterGenerated { get; set; }

    // Prevents save on kill after saving a character (M12)
    public bool CharacterSaved { get; set; }

    // Character is a Wizard (M12: only saveChar()'s overwrite prompt reads
    // it; wizard.cpp itself is out of the roadmap's scope)
    public bool WizardMode { get; set; }

    // Player requests to be Wizard - used during startup, when -w option
    // used. loadGame() reads it to allow resurrection. (M12)
    public bool ToBeWizard { get; set; }

    // Don't save a score for this game. -CJS- Bits: 0x1 resurrected, 0x2
    // wizard mode was used, 0x4 already on the scoreboard. (M12)
    public short NoScore { get; set; }

    // src/ui_io.cpp panic_save -- true if playing from a panic save (M12)
    public bool PanicSave { get; set; }

    // src/ui_io.cpp eof_flag -- signals an EOF/HANGUP on input. The input
    // layer that sets it is M14/M15's; svWrite() reads it. (M12)
    public int EofFlag { get; set; }

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
