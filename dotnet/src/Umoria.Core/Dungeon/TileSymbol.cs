using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Dungeon;

// src/dungeon.cpp:171 caveGetTileSymbol() -- returns symbol for given row,
// column -RAK-
//
// Ported in M8 because it is not only rendering: the running algorithm
// (player_run.cpp's playerCanSeeDungeonWall()/playerSeeNothing()) decides
// where to go by looking at the glyph the player would see, so it is game
// logic there.
//
// It also draws game RNG while the player hallucinates. Every caller in this
// port so far is that running logic, where the original draws exactly the
// same numbers. The rendering callers (drawDungeonPanel(), panelPutTile() in
// the light code) are still omitted, and they draw RNG too during
// hallucination -- the open M14 decision in migration-plan.md (M7's Defer
// list).
public static class TileSymbol
{
    public static char Get(PlayerWorld w, Coord coord)
    {
        var py = w.Py;
        var tile = w.Dg.Floor[coord.Y, coord.X];

        if (tile.CreatureId == 1 && (py.RunningTracker == 0 || w.Game.Options.RunPrintSelf))
        {
            return '@';
        }

        if ((py.Flags.Status & PlayerConfig.Status.PyBlind) != 0)
        {
            return ' ';
        }

        if (py.Flags.Image > 0 && RandomNumber.Next(12) == 1)
        {
            return (char)(byte)(RandomNumber.Next(95) + 31);
        }

        if (tile.CreatureId > 1 && w.Monsters.Monsters[tile.CreatureId].Lit)
        {
            return (char)DataCreaturesData.CreaturesList[w.Monsters.Monsters[tile.CreatureId].CreatureId].Sprite;
        }

        if (!tile.PermanentLight && !tile.TemporaryLight && !tile.FieldMark)
        {
            return ' ';
        }

        if (tile.TreasureId != 0 && w.Treasure[tile.TreasureId].CategoryId != TreasureCategory.TvInvisTrap)
        {
            return (char)w.Treasure[tile.TreasureId].Sprite;
        }

        if (tile.FeatureId <= TileConstants.MaxCaveFloor)
        {
            return '.';
        }

        if (tile.FeatureId == TileConstants.TileGraniteWall || tile.FeatureId == TileConstants.TileBoundaryWall || !w.Game.Options.HighlightSeams)
        {
            return '#';
        }

        // Originally set highlight bit, but that is not portable,
        // now use the percent sign instead.
        return '%';
    }
}
