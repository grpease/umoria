using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Character;

// The slice of the original's globals (py, dg, game, monsters[],
// game.treasure) that player-side game logic touches, bundled so M7+ code
// can take one explicit context instead of five parameters. Each member is
// the same live object the rest of the port mutates; this owns nothing.
//
// M8 added the object-level helpers (ObjectPlacement over the same dg +
// treasure list), the object-knowledge table (objects_identified[], needed
// for pickup stacking and chest identification), the running algorithm's
// state (static in the original) and the yes/no prompt hook.
//
// M9 added the monster memories (creature_recall[], which persist across
// levels like objects_identified[]) and routes MonsterManager's
// compactMonsters() message through PlayerCore.PrintMessage.
//
// M10 added the two remaining input hooks the casting layer reads through:
// single keypresses (spell menus, directions, genocide's monster letter) and
// the inventory item prompt.
public sealed class PlayerWorld
{
    public PlayerWorld(
        Player py,
        Model.Generated.Dungeon dg,
        GameState game,
        MonsterManager monsters,
        TreasureList treasure,
        ObjectIdentification? identification = null,
        Recall[]? creatureRecall = null)
    {
        Py = py;
        Dg = dg;
        Game = game;
        Monsters = monsters;
        Treasure = treasure;
        Objects = new ObjectPlacement(dg, treasure);
        Identification = identification ?? new ObjectIdentification();
        CreatureRecall = creatureRecall ?? Recall.CreateTable();

        Print = message => PlayerCore.PrintMessage(this, message);
        monsters.PrintMessage = Print;
    }

    public Player Py { get; }
    public Model.Generated.Dungeon Dg { get; }
    public GameState Game { get; }
    public MonsterManager Monsters { get; }
    public TreasureList Treasure { get; }

    public ObjectPlacement Objects { get; }

    // PlayerCore.PrintMessage bound to this world, for code that only takes
    // a message sink (M11)
    public Action<string> Print { get; }
    public ObjectIdentification Identification { get; }

    // src/recall.h creature_recall[] -- monster memories. -CJS- (M9)
    public Recall[] CreatureRecall { get; }

    // src/player_run.cpp's file-static find_* variables.
    public RunState Run { get; } = new();

    // src/ui_io.cpp getInputConfirmation() -- the yes/no prompt. Input, not
    // logic: the caller (UI, or a test script) supplies the answer. The
    // default answers "no", like the headless C++ stub.
    public Func<string, bool> Confirm { get; set; } = _ => false;

    // src/ui_io.cpp getCommand() -- one keypress answering a prompt; null
    // is <ESCAPE>. Input, like Confirm; read it through PlayerCore.GetCommand,
    // which keeps getKeyInput()'s game-state effect. The default escapes. (M10)
    public Func<string, char?> ReadCommand { get; set; } = _ => null;

    // src/ui_inventory.cpp:1152 inventoryGetInputForItemId() -- choose an
    // inventory slot in [start, end] for a prompt; null is <ESCAPE>. The
    // menu itself (paging between pack and equipment, the '*' list,
    // inscription digits, upper-case "Try" confirmation) is UI (M14); the
    // caller sees only the chosen slot. Read it through
    // PlayerCore.GetItemId. The default escapes. (M10)
    public Func<string, int, int, int?> SelectItem { get; set; } = (_, _, _) => null;

    // src/ui_io.cpp getInputConfirmationWithAbort() -- yes (1), no (0) or
    // <ESCAPE> (-1); the drop command's "Drop all ...?" prompt. The default
    // answers 0, like the headless C++ stub's. (M11)
    public Func<string, int> ConfirmWithAbort { get; set; } = _ => 0;

    // src/ui_io.cpp getStringInput() -- a line of text of at most
    // `maxLength` characters, trailing blanks removed; null aborts. The
    // inscribe command reads through it. (M11)
    public Func<string, int, string?> ReadString { get; set; } = (_, _) => null;

    // Every message printMessage() is given, in order -- the message line,
    // -more- prompt and history are the UI's (M14), so by default this
    // drops them; the M11 item-use trace records them to check the text.
    // Fed by PlayerCore.PrintMessage. (M11)
    public Action<string>? MessageLog { get; set; }
}

// src/player_run.cpp's static running state -CJS-
public sealed class RunState
{
    // You are in the open on at least one side.
    public bool OpenArea;

    // You have a wall on the right, and will stop if it opens
    public bool BreakRight;

    // You have a wall on the left, and will stop if it opens
    public bool BreakLeft;

    public int PrevDir;

    // Keep a record of which way we are going.
    public int Direction;
}
