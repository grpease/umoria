using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// Port of the state-mutating lighting helpers from src/dungeon.cpp.
//
// dungeonMoveCharacterLight() (and its sub1MoveLight()/sub3MoveLight()
// helpers) are NOT ported here: they read Player.flags.blind,
// Player.carrying_light, Player.running_tracker, Player.temporary_light_only
// (no Player singleton exists until M6) and config::options::run_print_self
// (UI), so they're a real M6/M8 dependency, not this milestone's. They
// naturally belong with M8's player-movement port, which already lists M5
// as a prerequisite.
public static class DungeonLighting
{
    // src/dungeon.cpp:212 caveTileVisible()
    public static bool CaveTileVisible(Model.Generated.Dungeon dg, Coord coord)
    {
        var tile = dg.Floor[coord.Y, coord.X];
        return tile.PermanentLight || tile.TemporaryLight || tile.FieldMark;
    }

    // src/dungeon.cpp:358 dungeonLightRoom() -- ports the state mutation
    // only (permanent_light, field_mark, dark->light floor upgrade). The
    // original's per-tile panelPutTile(caveGetTileSymbol(location), ...)
    // call is terminal rendering built on caveGetTileSymbol(), which isn't
    // ported (see ScreenRendering's class comment), so it's omitted.
    public static void LightRoom(Model.Generated.Dungeon dg, TreasureList treasure, Coord coord)
    {
        const int heightMiddle = DungeonConstants.ScreenHeight / 2;
        const int widthMiddle = DungeonConstants.ScreenWidth / 2;

        var top = (coord.Y / heightMiddle) * heightMiddle;
        var left = (coord.X / widthMiddle) * widthMiddle;
        var bottom = top + heightMiddle - 1;
        var right = left + widthMiddle - 1;

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var tile = dg.Floor[y, x];

                if (!tile.PermaLitRoom || tile.PermanentLight)
                {
                    continue;
                }

                tile.PermanentLight = true;

                if (tile.FeatureId == TileConstants.TileDarkFloor)
                {
                    tile.FeatureId = TileConstants.TileLightFloor;
                }

                if (!tile.FieldMark && tile.TreasureId != 0)
                {
                    var categoryId = treasure[tile.TreasureId].CategoryId;
                    if (categoryId >= TreasureCategory.TvMinVisible && categoryId <= TreasureCategory.TvMaxVisible)
                    {
                        tile.FieldMark = true;
                    }
                }

                dg.Floor[y, x] = tile;
            }
        }
    }
}
