using Umoria.Core.Config;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// Port of the state-mutating lighting helpers from src/dungeon.cpp.
//
// dungeonMoveCharacterLight() (with its sub1MoveLight()/sub3MoveLight()
// helpers) and src/ui.cpp's dungeonResetView() were deferred from M5 for
// want of a Player; they're ported here as of M7, because the player
// status-effect updates (blindness, light running out, playerDisturb())
// reach them. Their panelPutTile()/drawDungeonPanel() calls are terminal
// rendering and are omitted, same as LightRoom's.
public static class DungeonLighting
{
    // src/ui.cpp:111 dungeonResetView() -- we need to reset the view of
    // things. -CJS- (M8 added the panel check: the panel shift is state, see
    // ScreenPanel; only the drawDungeonPanel() redraw is omitted.)
    public static void ResetView(Player py, Model.Generated.Dungeon dg, TreasureList treasure, GameOptions options)
    {
        // Check for new panel
        if (ScreenPanel.CoordOutsidePanel(py, dg, treasure, options, py.Pos, false))
        {
            // (drawDungeonPanel() -- rendering)
        }

        // Move the light source
        MoveCharacterLight(py, dg, treasure, options, py.Pos, py.Pos);

        // (re-read after the move: the original holds a reference to the tile)
        var tile = dg.Floor[py.Pos.Y, py.Pos.X];

        // A room of light should be lit.
        if (tile.FeatureId == TileConstants.TileLightFloor)
        {
            if (py.Flags.Blind < 1 && !tile.PermanentLight)
            {
                LightRoom(dg, treasure, py.Pos);
            }
            return;
        }

        // In doorway of light-room?
        if (tile.PermaLitRoom && py.Flags.Blind < 1)
        {
            for (var i = py.Pos.Y - 1; i <= py.Pos.Y + 1; i++)
            {
                for (var j = py.Pos.X - 1; j <= py.Pos.X + 1; j++)
                {
                    if (dg.Floor[i, j].FeatureId == TileConstants.TileLightFloor && !dg.Floor[i, j].PermanentLight)
                    {
                        LightRoom(dg, treasure, new Coord(i, j));
                    }
                }
            }
        }
    }

    // src/player_run.cpp:238 playerEndRunning() -- switch off the run flag
    // and get the light correct. -CJS- Lives here (not only in PlayerCore)
    // because ScreenPanel.CoordOutsidePanel(), below PlayerWorld, calls it.
    public static void EndRunning(Player py, Model.Generated.Dungeon dg, TreasureList treasure, GameOptions options)
    {
        if (py.RunningTracker == 0)
        {
            return;
        }

        py.RunningTracker = 0;

        MoveCharacterLight(py, dg, treasure, options, py.Pos, py.Pos);
    }

    // src/dungeon.cpp:491 dungeonMoveCharacterLight() -- package for moving
    // the character's light about the screen. Four cases : Normal, Finding,
    // Blind, and No light -RAK-
    public static void MoveCharacterLight(Player py, Model.Generated.Dungeon dg, TreasureList treasure, GameOptions options, Coord from, Coord to)
    {
        if (py.Flags.Blind > 0 || !py.CarryingLight)
        {
            Sub3MoveLight(py, dg, from);
        }
        else
        {
            Sub1MoveLight(py, dg, treasure, options, from, to);
        }
    }

    // src/dungeon.cpp:403 sub1MoveLight() -- normal movement. When
    // FIND_FLAG, light only permanent features.
    private static void Sub1MoveLight(Player py, Model.Generated.Dungeon dg, TreasureList treasure, GameOptions options, Coord from, Coord to)
    {
        if (py.TemporaryLightOnly)
        {
            // Turn off lamp light
            for (var y = from.Y - 1; y <= from.Y + 1; y++)
            {
                for (var x = from.X - 1; x <= from.X + 1; x++)
                {
                    dg.Floor[y, x].TemporaryLight = false;
                }
            }
            if (py.RunningTracker != 0 && !options.RunPrintSelf)
            {
                py.TemporaryLightOnly = false;
            }
        }
        else if (py.RunningTracker == 0 || options.RunPrintSelf)
        {
            py.TemporaryLightOnly = true;
        }

        for (var y = to.Y - 1; y <= to.Y + 1; y++)
        {
            for (var x = to.X - 1; x <= to.X + 1; x++)
            {
                ref var tile = ref dg.Floor[y, x];

                // only light up if normal movement
                if (py.TemporaryLightOnly)
                {
                    tile.TemporaryLight = true;
                }

                if (tile.FeatureId >= TileConstants.MinCaveWall)
                {
                    tile.PermanentLight = true;
                }
                else if (!tile.FieldMark && tile.TreasureId != 0)
                {
                    int tval = treasure[tile.TreasureId].CategoryId;

                    if (tval >= TreasureCategory.TvMinVisible && tval <= TreasureCategory.TvMaxVisible)
                    {
                        tile.FieldMark = true;
                    }
                }
            }
        }

        // (the original then redraws every tile from `from` to `to` --
        // rendering only)
    }

    // src/dungeon.cpp:468 sub3MoveLight() -- when blinded, move only the
    // player symbol. With no light, movement becomes involved.
    private static void Sub3MoveLight(Player py, Model.Generated.Dungeon dg, Coord from)
    {
        if (py.TemporaryLightOnly)
        {
            for (var y = from.Y - 1; y <= from.Y + 1; y++)
            {
                for (var x = from.X - 1; x <= from.X + 1; x++)
                {
                    dg.Floor[y, x].TemporaryLight = false;
                }
            }

            py.TemporaryLightOnly = false;
        }

        // (the rest only redraws `from` and the '@' at `to` -- rendering)
    }

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
