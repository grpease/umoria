using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Character;

// M8: src/player_run.cpp, the running algorithm -CJS- (see the original's
// long header comment for how corridors, open areas, corners and doorways
// are told apart). Its file-static find_* variables live on
// PlayerWorld.Run. playerEndRunning() is PlayerCore.EndRunning (M7).
//
// Running decides where to go from what the player can SEE, by asking for
// the glyph each square would be drawn with -- TileSymbol.Get(), the port of
// caveGetTileSymbol().
public static class PlayerRunning
{
    // The cycle lists the directions in anticlockwise order, for over two
    // complete cycles. The chome array maps a direction on to its position
    // in the cycle. -CJS-
    private static readonly int[] Cycle = { 1, 2, 3, 6, 9, 8, 7, 4, 1, 2, 3, 6, 9, 8, 7, 4, 1 };
    private static readonly int[] Chome = { -1, 8, 9, 10, 7, -1, 11, 6, 5, 4 };

    // Do we see a wall? Used in running. -CJS-
    private static bool CanSeeDungeonWall(PlayerWorld w, int dir, Coord coord)
    {
        // check to see if movement there possible
        if (!PlayerMovement.MovePosition(w.Dg, dir, ref coord))
        {
            return true;
        }

        var c = TileSymbol.Get(w, coord);

        return c == '#' || c == '%';
    }

    // Do we see anything? Used in running. -CJS-
    private static bool SeeNothing(PlayerWorld w, int dir, Coord coord)
    {
        // check to see if movement there possible
        return PlayerMovement.MovePosition(w.Dg, dir, ref coord) && TileSymbol.Get(w, coord) == ' ';
    }

    private static void FindRunningBreak(PlayerWorld w, int dir, Coord coord)
    {
        var run = w.Run;
        var pos = w.Py.Pos;

        var deepLeft = false;
        var deepRight = false;
        var shortLeft = false;
        var shortRight = false;

        var cycleIndex = Chome[dir];

        if (CanSeeDungeonWall(w, Cycle[cycleIndex + 1], pos))
        {
            run.BreakLeft = true;
            shortLeft = true;
        }
        else if (CanSeeDungeonWall(w, Cycle[cycleIndex + 1], coord))
        {
            run.BreakLeft = true;
            deepLeft = true;
        }

        if (CanSeeDungeonWall(w, Cycle[cycleIndex - 1], pos))
        {
            run.BreakRight = true;
            shortRight = true;
        }
        else if (CanSeeDungeonWall(w, Cycle[cycleIndex - 1], coord))
        {
            run.BreakRight = true;
            deepRight = true;
        }

        if (run.BreakLeft && run.BreakRight)
        {
            run.OpenArea = false;

            // a hack to allow angled corridor entry
            if ((dir & 1) != 0)
            {
                if (deepLeft && !deepRight)
                {
                    run.PrevDir = Cycle[cycleIndex - 1];
                }
                else if (deepRight && !deepLeft)
                {
                    run.PrevDir = Cycle[cycleIndex + 1];
                }
            }
            else if (CanSeeDungeonWall(w, Cycle[cycleIndex], coord))
            {
                // else if there is a wall two spaces ahead and seem to be in a
                // corridor, then force a turn into the side corridor, must
                // be moving straight into a corridor here

                if (shortLeft && !shortRight)
                {
                    run.PrevDir = Cycle[cycleIndex - 2];
                }
                else if (shortRight && !shortLeft)
                {
                    run.PrevDir = Cycle[cycleIndex + 2];
                }
            }
        }
        else
        {
            run.OpenArea = true;
        }
    }

    // src/player_run.cpp:186 playerFindInitialize()
    public static void FindInitialize(PlayerWorld w, int direction)
    {
        var py = w.Py;
        var run = w.Run;
        var coord = py.Pos;

        if (!PlayerMovement.MovePosition(w.Dg, direction, ref coord))
        {
            py.RunningTracker = 0;
        }
        else
        {
            py.RunningTracker = 1;

            run.Direction = direction;
            run.PrevDir = direction;

            run.BreakRight = false;
            run.BreakLeft = false;

            if (py.Flags.Blind < 1)
            {
                FindRunningBreak(w, direction, coord);
            }
        }

        // We must erase the player symbol '@' here, because sub3_move_light()
        // does not erase the previous location of the player when in find mode
        // and when `run_print_self` is false. -- The glyph lookup draws game
        // RNG while hallucinating, so it is the original's (not the screen's
        // cosmetic one).
        if (!py.TemporaryLightOnly && !w.Game.Options.RunPrintSelf)
        {
            ScreenRendering.PutTile(w.Dg, TileSymbol.Get(w, py.Pos), py.Pos);
        }

        PlayerMovement.Move(w, direction, true);

        if (py.RunningTracker == 0)
        {
            w.Game.CommandCount = 0;
        }
    }

    // src/player_run.cpp:224 playerRunAndFind()
    public static void RunAndFind(PlayerWorld w)
    {
        var py = w.Py;
        var tracker = py.RunningTracker;

        py.RunningTracker++;

        // prevent infinite loops in find mode, will stop after moving 100 times
        if (tracker > 100)
        {
            PlayerCore.PrintMessage(w, "You stop running to catch your breath.");
            PlayerCore.EndRunning(w);
            return;
        }

        PlayerMovement.Move(w, w.Run.Direction, true);
    }

    private static bool AreaAffectStopLookingAtSquares(PlayerWorld w, int i, int dir, int newDir, Coord coord, ref int checkDir, ref int dirA, ref int dirB)
    {
        var run = w.Run;
        var tile = w.Dg.Floor[coord.Y, coord.X];

        // Default: Square unseen. Treat as open.
        var invisible = true;

        if (w.Py.CarryingLight || tile.TemporaryLight || tile.PermanentLight || tile.FieldMark)
        {
            if (tile.TreasureId != 0)
            {
                int tileId = w.Treasure[tile.TreasureId].CategoryId;

                if (tileId != TreasureCategory.TvInvisTrap && tileId != TreasureCategory.TvSecretDoor &&
                    (tileId != TreasureCategory.TvOpenDoor || !w.Game.Options.RunIgnoreDoors))
                {
                    PlayerCore.EndRunning(w);
                    return true;
                }
            }

            // Also Creatures
            // The monster should be visible since monsterUpdateVisibility() checks
            // for the special case of being in find mode
            if (tile.CreatureId > 1 && w.Monsters.Monsters[tile.CreatureId].Lit)
            {
                PlayerCore.EndRunning(w);
                return true;
            }

            invisible = false;
        }

        if (tile.FeatureId <= TileConstants.MaxOpenSpace || invisible)
        {
            if (run.OpenArea)
            {
                // Have we found a break?
                if (i < 0)
                {
                    if (run.BreakRight)
                    {
                        PlayerCore.EndRunning(w);
                        return true;
                    }
                }
                else if (i > 0)
                {
                    if (run.BreakLeft)
                    {
                        PlayerCore.EndRunning(w);
                        return true;
                    }
                }
            }
            else if (dirA == 0)
            {
                // The first new direction.
                dirA = newDir;
            }
            else if (dirB != 0)
            {
                // Three new directions. STOP.
                PlayerCore.EndRunning(w);
                return true;
            }
            else if (dirA != Cycle[Chome[dir] + i - 1])
            {
                // If not adjacent to prev, STOP
                PlayerCore.EndRunning(w);
                return true;
            }
            else
            {
                // Two adjacent choices. Make dir_b the diagonal, and
                // remember the other diagonal adjacent to the first option.
                if ((newDir & 1) == 1)
                {
                    checkDir = Cycle[Chome[dir] + i - 2];
                    dirB = newDir;
                }
                else
                {
                    checkDir = Cycle[Chome[dir] + i + 1];
                    dirB = dirA;
                    dirA = newDir;
                }
            }
        }
        else if (run.OpenArea)
        {
            // We see an obstacle. In open area, STOP if on a side previously open.
            if (i < 0)
            {
                if (run.BreakLeft)
                {
                    PlayerCore.EndRunning(w);
                    return true;
                }
                run.BreakRight = true;
            }
            else if (i > 0)
            {
                if (run.BreakRight)
                {
                    PlayerCore.EndRunning(w);
                    return true;
                }
                run.BreakLeft = true;
            }
        }

        return false;
    }

    // src/player_run.cpp:331 playerAreaAffect() -- determine the next
    // direction for a run, or if we should stop. -CJS-
    public static void AreaAffect(PlayerWorld w, int direction, Coord coord)
    {
        var run = w.Run;
        var options = w.Game.Options;

        if (w.Py.Flags.Blind >= 1)
        {
            return;
        }

        var checkDir = 0;
        var dirA = 0;
        var dirB = 0;

        direction = run.PrevDir;

        var max = (direction & 1) + 1;

        // Look at every newly adjacent square.
        for (var i = -max; i <= max; i++)
        {
            var newDir = Cycle[Chome[direction] + i];

            var spot = coord;

            // Objects player can see (Including doors?) cause a stop.
            // (the original ignores the "stopped" result and keeps looking)
            if (PlayerMovement.MovePosition(w.Dg, newDir, ref spot))
            {
                AreaAffectStopLookingAtSquares(w, i, direction, newDir, spot, ref checkDir, ref dirA, ref dirB);
            }
        }

        if (run.OpenArea)
        {
            return;
        }

        // choose a direction.

        if (dirB == 0 || (options.RunExamineCorners && !options.RunCutCorners))
        {
            // There is only one option, or if two, then we always examine
            // potential corners and never cur known corners, so you step
            // into the straight option.
            if (dirA != 0)
            {
                run.Direction = dirA;
            }

            run.PrevDir = dirB == 0 ? dirA : dirB;

            return;
        }

        // Two options!

        var location = coord;
        PlayerMovement.MovePosition(w.Dg, dirA, ref location);

        if (!CanSeeDungeonWall(w, dirA, location) || !CanSeeDungeonWall(w, checkDir, location))
        {
            // Don't see that it is closed off.  This could be a
            // potential corner or an intersection.
            if (options.RunExamineCorners && SeeNothing(w, dirA, location) && SeeNothing(w, dirB, location))
            {
                // Can not see anything ahead and in the direction we are
                // turning, assume that it is a potential corner.
                run.Direction = dirA;
                run.PrevDir = dirB;
            }
            else
            {
                // STOP: we are next to an intersection or a room
                PlayerCore.EndRunning(w);
            }
        }
        else if (options.RunCutCorners)
        {
            // This corner is seen to be enclosed; we cut the corner.
            run.Direction = dirB;
            run.PrevDir = dirB;
        }
        else
        {
            // This corner is seen to be enclosed, and we deliberately
            // go the long way.
            run.Direction = dirA;
            run.PrevDir = dirB;
        }
    }
}
