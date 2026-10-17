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
public sealed class PlayerWorld(
    Player py,
    Model.Generated.Dungeon dg,
    GameState game,
    MonsterManager monsters,
    TreasureList treasure,
    ObjectIdentification? identification = null)
{
    public Player Py { get; } = py;
    public Model.Generated.Dungeon Dg { get; } = dg;
    public GameState Game { get; } = game;
    public MonsterManager Monsters { get; } = monsters;
    public TreasureList Treasure { get; } = treasure;

    public ObjectPlacement Objects { get; } = new(dg, treasure);
    public ObjectIdentification Identification { get; } = identification ?? new ObjectIdentification();

    // src/player_run.cpp's file-static find_* variables.
    public RunState Run { get; } = new();

    // src/ui_io.cpp getInputConfirmation() -- the yes/no prompt. Input, not
    // logic: the caller (UI, or a test script) supplies the answer. The
    // default answers "no", like the headless C++ stub.
    public Func<string, bool> Confirm { get; set; } = _ => false;
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
