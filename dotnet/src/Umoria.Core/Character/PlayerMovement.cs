using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// M8: the player's step, from src/player_move.cpp (playerMove(),
// playerRandomMovement(), carry()) plus the src/player.cpp helpers it
// reaches: playerMovePosition(), playerNoLight(), playerTeleport(),
// playerSearch(), playerTestBeingHit() and playerAttackPosition().
// Stepping onto a trap is PlayerTraps.StepsOnTrap; the running algorithm is
// PlayerRunning.
//
// Not ported here:
//   - Bump attacks (playerAttackMonster(), monster combat): M9.
//     AttackPosition() keeps the "too afraid" refusal and throws
//     NotSupportedException otherwise.
//   - Message and prompt text that needs itemDescription() for a real item
//     (pickup, "You have found <trap>"): M11. Those messages still zero
//     game.command_count via PlayerCore.PrintMessage; only their text is
//     missing.
public static class PlayerMovement
{
    // src/player.cpp:49 playerMovePosition() -- given direction "dir",
    // returns new row, column location -RAK-
    public static bool MovePosition(Model.Generated.Dungeon dg, int dir, ref Coord coord)
    {
        Coord newCoord;
        if (dir >= 1 && dir <= 9)
        {
            var (dy, dx) = Movement.DirectionOffsets.For(dir);
            newCoord = new Coord(coord.Y + dy, coord.X + dx);
        }
        else
        {
            newCoord = new Coord(0, 0);
        }

        var canMove = false;

        if (newCoord.Y >= 0 && newCoord.Y < dg.Height && newCoord.X >= 0 && newCoord.X < dg.Width)
        {
            coord = newCoord;
            canMove = true;
        }

        return canMove;
    }

    // src/player.cpp:141 playerNoLight() -- returns true if player has no
    // light -RAK-
    public static bool NoLight(PlayerWorld w)
    {
        var tile = w.Dg.Floor[w.Py.Pos.Y, w.Py.Pos.X];
        return !tile.TemporaryLight && !tile.PermanentLight;
    }

    // src/player.cpp:106 playerTeleport() -- teleport the player to a new
    // location -RAK-
    public static void Teleport(PlayerWorld w, int newDistance)
    {
        var py = w.Py;
        var dg = w.Dg;

        Coord location;

        do
        {
            var y = RandomNumber.Next(dg.Height) - 1;
            var x = RandomNumber.Next(dg.Width) - 1;

            while (CoordHelpers.DistanceBetween(new Coord(y, x), py.Pos) > newDistance)
            {
                y += (py.Pos.Y - y) / 2;
                x += (py.Pos.X - x) / 2;
            }

            location = new Coord(y, x);
        } while (dg.Floor[location.Y, location.X].FeatureId >= TileConstants.MinClosedSpace || dg.Floor[location.Y, location.X].CreatureId >= 2);

        w.Monsters.MoveCreatureRecord(py.Pos, location);

        for (var y = py.Pos.Y - 1; y <= py.Pos.Y + 1; y++)
        {
            for (var x = py.Pos.X - 1; x <= py.Pos.X + 1; x++)
            {
                dg.Floor[y, x].TemporaryLight = false;
                ScreenRendering.LiteSpot(dg, new Coord(y, x));
            }
        }

        ScreenRendering.LiteSpot(dg, py.Pos);

        py.Pos = location;

        DungeonLighting.ResetView(py, dg, w.Treasure, w.Game.Options);
        PlayerCore.UpdateMonstersWithoutMoving(w);

        w.Game.TeleportPlayer = false;
    }

    // src/player.cpp:665 playerSearch() -- searches for hidden things. -RAK-
    public static void Search(PlayerWorld w, Coord coord, int chance)
    {
        var py = w.Py;
        var dg = w.Dg;

        if (py.Flags.Confused > 0)
        {
            chance = chance / 10;
        }

        if (py.Flags.Blind > 0 || NoLight(w))
        {
            chance = chance / 10;
        }

        if (py.Flags.Image > 0)
        {
            chance = chance / 10;
        }

        for (var y = coord.Y - 1; y <= coord.Y + 1; y++)
        {
            for (var x = coord.X - 1; x <= coord.X + 1; x++)
            {
                // always coordInBounds() here
                if (RandomNumber.Next(100) >= chance)
                {
                    continue;
                }

                var treasureId = dg.Floor[y, x].TreasureId;
                if (treasureId == 0)
                {
                    continue;
                }

                // Search for hidden objects

                ref var item = ref w.Treasure[treasureId];
                var spot = new Coord(y, x);

                if (item.CategoryId == TreasureCategory.TvInvisTrap)
                {
                    // Trap on floor?
                    PlayerCore.PrintMessage(w, "You have found " + ItemDescription.Describe(item));

                    w.Objects.TrapChangeVisibility(spot);
                    PlayerCore.EndRunning(w);
                }
                else if (item.CategoryId == TreasureCategory.TvSecretDoor)
                {
                    // Secret door?
                    PlayerCore.PrintMessage(w, "You have found a secret door.");

                    w.Objects.TrapChangeVisibility(spot);
                    PlayerCore.EndRunning(w);
                }
                else if (item.CategoryId == TreasureCategory.TvChest)
                {
                    // Chest is trapped?

                    // mask out the treasure bits
                    if ((item.Flags & TreasureConfig.Chests.ChTrapped) > 1)
                    {
                        if (!ObjectIdentification.SpellItemIdentified(item))
                        {
                            w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
                            PlayerCore.PrintMessage(w, "You have discovered a trap on the chest!");
                        }
                        else
                        {
                            PlayerCore.PrintMessage(w, "The chest is trapped!");
                        }
                    }
                }
            }
        }
    }

    // src/player.cpp:628 playerTestBeingHit() -- attacker's level and
    // plusses, defender's AC -RAK-
    public static bool TestBeingHit(PlayerWorld w, int baseToHit, int level, int plusToHit, int armorClass, int attackTypeId)
    {
        PlayerCore.Disturb(w, 1, 0);

        // `plusToHit` could be less than 0 if player wielding weapon too heavy for them
        var hitChance = baseToHit + plusToHit * PlayerConfig.BthPerPlusToHitAdjust +
                        (level * DataPlayerData.ClassLevelAdj[w.Py.Misc.ClassId][attackTypeId]);

        // always miss 1 out of 20, always hit 1 out of 20
        var die = RandomNumber.Next(20);

        // normal hit
        return die != 1 && (die == 20 || (hitChance > 0 && RandomNumber.Next(hitChance) > armorClass));
    }

    // src/player.cpp:1444 playerAttackPosition() -- let the player attack
    // the creature
    public static void AttackPosition(PlayerWorld w, Coord coord)
    {
        // Is a Coward?
        if (w.Py.Flags.Afraid > 0)
        {
            PlayerCore.PrintMessage(w, "You are too afraid!");
            return;
        }

        throw new NotSupportedException("playerAttackMonster() (melee combat) is M9, not yet ported.");
    }

    // src/player_move.cpp:332 playerRandomMovement()
    private static bool RandomMovement(PlayerWorld w, int dir)
    {
        // Never random if sitting
        if (dir == 5)
        {
            return false;
        }

        // 75% random movement -- rolled even when not confused
        var playerRandomMove = RandomNumber.Next(4) > 1;

        var playerIsConfused = w.Py.Flags.Confused > 0;

        return playerIsConfused && playerRandomMove;
    }

    private static bool IsTrapOrStoreDoor(int categoryId)
    {
        return categoryId == TreasureCategory.TvInvisTrap || categoryId == TreasureCategory.TvVisTrap || categoryId == TreasureCategory.TvStoreDoor;
    }

    // src/player_move.cpp:349 carry() -- player is on an object. Many things
    // can happen based on the TVAL of the object. Traps are set off, money
    // and most objects are picked up. Some objects, such as open doors, just
    // sit there. -RAK-
    private static void Carry(PlayerWorld w, Coord coord, bool pickup)
    {
        var py = w.Py;
        var treasureId = w.Dg.Floor[coord.Y, coord.X].TreasureId;
        var item = w.Treasure[treasureId];

        int tileFlags = item.CategoryId;

        if (tileFlags > TreasureCategory.TvMaxPickUp)
        {
            if (IsTrapOrStoreDoor(tileFlags))
            {
                // OOPS!
                PlayerTraps.StepsOnTrap(w, coord);
            }
            return;
        }

        PlayerCore.EndRunning(w);

        // There's GOLD in them thar hills!
        if (tileFlags == TreasureCategory.TvGold)
        {
            py.Misc.Au += item.Cost;

            var msg = $"You have found {item.Cost} gold pieces worth of {ItemDescription.Describe(item)}";

            // (printCharacterGoldValue() -- display)
            w.Objects.DeleteObject(coord);

            PlayerCore.PrintMessage(w, msg);

            return;
        }

        // Too many objects?
        if (PlayerInventory.CanCarryItemCount(py, w.Identification, item))
        {
            // Okay, pick it up
            // (prompt text: itemDescription() is M11, so the item name is
            // missing from it)
            if (pickup && w.Game.Options.PromptToPickup)
            {
                pickup = w.Confirm("Pick up the item?");
            }

            // Check to see if it will change the players speed.
            if (pickup && !PlayerInventory.CanCarryItem(py, item))
            {
                pickup = w.Confirm("Exceed your weight limit to pick up the item?");
            }

            // Attempt to pick up an object.
            if (pickup)
            {
                var locn = PlayerInventory.CarryItem(py, w.Identification, item);

                PlayerCore.PrintMessage(w, $"You have the item ({(char)(locn + 'a')})");
                w.Objects.DeleteObject(coord);
            }
        }
        else
        {
            PlayerCore.PrintMessage(w, "You can't carry the item");
        }
    }

    // src/player_move.cpp:417 playerMove() -- moves player from one space
    // to another. -RAK-
    //
    // The original holds `tile` as a reference into the floor and reads it
    // again after carry()/traps have changed it, so this re-reads
    // dg.Floor[coord] at every use rather than caching a copy.
    public static void Move(PlayerWorld w, int direction, bool doPickup)
    {
        var py = w.Py;
        var dg = w.Dg;

        if (RandomMovement(w, direction))
        {
            direction = RandomNumber.Next(9);
            PlayerCore.EndRunning(w);
        }

        var coord = py.Pos;

        // Legal move?
        if (!MovePosition(dg, direction, ref coord))
        {
            return;
        }

        var creatureId = dg.Floor[coord.Y, coord.X].CreatureId;
        var monsterLit = w.Monsters.Monsters[creatureId].Lit;

        // if there is no creature, or an unlit creature in the walls then...
        // disallow attacks against unlit creatures in walls because moving into
        // a wall is a free turn normally, hence don't give player free turns
        // attacking each wall in an attempt to locate the invisible creature,
        // instead force player to tunnel into walls which always takes a turn
        if (creatureId < 2 || (!monsterLit && dg.Floor[coord.Y, coord.X].FeatureId >= TileConstants.MinClosedSpace))
        {
            // Open floor spot
            if (dg.Floor[coord.Y, coord.X].FeatureId <= TileConstants.MaxOpenSpace)
            {
                // Make final assignments of char coords
                var oldCoord = py.Pos;

                py.Pos = coord;

                // Move character record (-1)
                w.Monsters.MoveCreatureRecord(oldCoord, py.Pos);

                // Check for new panel
                if (ScreenPanel.CoordOutsidePanel(py, dg, w.Treasure, w.Game.Options, py.Pos, false))
                {
                    // (drawDungeonPanel() -- rendering)
                }

                // Check to see if they should stop
                if (py.RunningTracker != 0)
                {
                    PlayerRunning.AreaAffect(w, direction, py.Pos);
                }

                // Check to see if they've noticed something
                // fos may be negative if have good rings of searching
                if (py.Misc.Fos <= 1 || RandomNumber.Next(py.Misc.Fos) == 1 || (py.Flags.Status & PlayerConfig.Status.PySearch) != 0)
                {
                    Search(w, py.Pos, py.Misc.ChanceInSearch);
                }

                var tile = dg.Floor[coord.Y, coord.X];
                if (tile.FeatureId == TileConstants.TileLightFloor)
                {
                    // A room of light should be lit.

                    if (!tile.PermanentLight && py.Flags.Blind == 0)
                    {
                        DungeonLighting.LightRoom(dg, w.Treasure, py.Pos);
                    }
                }
                else if (tile.PermaLitRoom && py.Flags.Blind < 1)
                {
                    // In doorway of light-room?

                    for (var row = py.Pos.Y - 1; row <= py.Pos.Y + 1; row++)
                    {
                        for (var col = py.Pos.X - 1; col <= py.Pos.X + 1; col++)
                        {
                            if (dg.Floor[row, col].FeatureId == TileConstants.TileLightFloor && !dg.Floor[row, col].PermanentLight)
                            {
                                DungeonLighting.LightRoom(dg, w.Treasure, new Coord(row, col));
                            }
                        }
                    }
                }

                // Move the light source
                DungeonLighting.MoveCharacterLight(py, dg, w.Treasure, w.Game.Options, oldCoord, py.Pos);

                // An object is beneath them.
                if (dg.Floor[coord.Y, coord.X].TreasureId != 0)
                {
                    Carry(w, py.Pos, doPickup);

                    // if stepped on falling rock trap, and space contains
                    // rubble, then step back into a clear area
                    if (w.Treasure[dg.Floor[coord.Y, coord.X].TreasureId].CategoryId == TreasureCategory.TvRubble)
                    {
                        w.Monsters.MoveCreatureRecord(py.Pos, oldCoord);
                        DungeonLighting.MoveCharacterLight(py, dg, w.Treasure, w.Game.Options, py.Pos, oldCoord);

                        py.Pos = oldCoord;

                        // check to see if we have stepped back onto another trap, if so, set it off
                        var id = dg.Floor[py.Pos.Y, py.Pos.X].TreasureId;
                        if (id != 0 && IsTrapOrStoreDoor(w.Treasure[id].CategoryId))
                        {
                            PlayerTraps.StepsOnTrap(w, py.Pos);
                        }
                    }
                }
            }
            else
            {
                // Can't move onto floor space

                var treasureId = dg.Floor[coord.Y, coord.X].TreasureId;
                if (py.RunningTracker == 0 && treasureId != 0)
                {
                    if (w.Treasure[treasureId].CategoryId == TreasureCategory.TvRubble)
                    {
                        PlayerCore.PrintMessage(w, "There is rubble blocking your way.");
                    }
                    else if (w.Treasure[treasureId].CategoryId == TreasureCategory.TvClosedDoor)
                    {
                        PlayerCore.PrintMessage(w, "There is a closed door blocking your way.");
                    }
                }
                else
                {
                    PlayerCore.EndRunning(w);
                }
                w.Game.PlayerFreeTurn = true;
            }
        }
        else
        {
            // Attacking a creature!

            int oldFindFlag = py.RunningTracker;

            PlayerCore.EndRunning(w);

            // if player can see monster, and was in find mode, then nothing
            if (w.Monsters.Monsters[creatureId].Lit && oldFindFlag != 0)
            {
                // did not do anything this turn
                w.Game.PlayerFreeTurn = true;
            }
            else
            {
                AttackPosition(w, coord);
            }
        }
    }
}
