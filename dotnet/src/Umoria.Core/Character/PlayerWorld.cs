using Umoria.Core.Dungeon;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Character;

// The slice of the original's globals (py, dg, game, monsters[],
// game.treasure) that player-side game logic touches, bundled so M7+ code
// can take one explicit context instead of five parameters. Each member is
// the same live object the rest of the port mutates; this owns nothing.
public sealed class PlayerWorld(
    Player py,
    Model.Generated.Dungeon dg,
    GameState game,
    MonsterManager monsters,
    TreasureList treasure)
{
    public Player Py { get; } = py;
    public Model.Generated.Dungeon Dg { get; } = dg;
    public GameState Game { get; } = game;
    public MonsterManager Monsters { get; } = monsters;
    public TreasureList Treasure { get; } = treasure;
}
