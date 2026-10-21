using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// M8: opening, closing and bashing doors and chests -- src/player.cpp
// (playerOpenClosedObject(), playerCloseDoor()) and src/player_bash.cpp.
// Each command takes the direction getDirectionWithMemory() returned (see
// PlayerCore.RememberDirection).
//
// M9 filled in the two M8 deferrals: opening a chest (openClosedChest()
// drops the contents through monsterDeath()) and bashing a monster
// (PlayerCombat.BashAttack).
public static class PlayerDoors
{
    // src/player.cpp:1229 playerLockPickingSkill()
    private static short LockPickingSkill(Player py)
    {
        var skill = py.Misc.Disarm;

        skill += (short)(2 * PlayerStatLogic.DisarmAdjustment(py));
        skill += (short)PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int);
        skill += (short)(DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Disarm] * py.Misc.Level / 3);

        return skill;
    }

    // src/player.cpp:1239 openClosedDoor()
    private static void OpenClosedDoor(PlayerWorld w, Coord coord)
    {
        var py = w.Py;
        var treasureId = w.Dg.Floor[coord.Y, coord.X].TreasureId;
        ref var item = ref w.Treasure[treasureId];

        if (item.MiscUse > 0)
        {
            // It's locked.

            if (py.Flags.Confused > 0)
            {
                PlayerCore.PrintMessage(w, "You are too confused to pick the lock.");
            }
            else if (LockPickingSkill(py) - item.MiscUse > RandomNumber.Next(100))
            {
                PlayerCore.PrintMessage(w, "You have picked the lock.");
                py.Misc.Exp++;
                PlayerCore.DisplayCharacterExperience(w);
                item.MiscUse = 0;
            }
            else
            {
                PlayerCore.PrintMessageNoCommandInterrupt(w, "You failed to pick the lock.");
            }
        }
        else if (item.MiscUse < 0)
        {
            // It's stuck

            PlayerCore.PrintMessage(w, "It appears to be stuck.");
        }

        if (item.MiscUse == 0)
        {
            item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjOpenDoor);
            w.Dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileCorrFloor;
            ScreenRendering.LiteSpot(w.Dg, coord);
            w.Game.CommandCount = 0;
        }
    }

    // src/player.cpp:1270 openClosedChest() (M9)
    private static void OpenClosedChest(PlayerWorld w, Coord coord)
    {
        var py = w.Py;
        var treasureId = w.Dg.Floor[coord.Y, coord.X].TreasureId;
        ref var item = ref w.Treasure[treasureId];

        var success = false;

        if ((item.Flags & TreasureConfig.Chests.ChLocked) != 0)
        {
            if (py.Flags.Confused > 0)
            {
                PlayerCore.PrintMessage(w, "You are too confused to pick the lock.");
            }
            else if (LockPickingSkill(py) - item.DepthFirstFound > RandomNumber.Next(100))
            {
                PlayerCore.PrintMessage(w, "You have picked the lock.");

                py.Misc.Exp += item.DepthFirstFound;
                PlayerCore.DisplayCharacterExperience(w);

                success = true;
            }
            else
            {
                PlayerCore.PrintMessageNoCommandInterrupt(w, "You failed to pick the lock.");
            }
        }
        else
        {
            success = true;
        }

        if (success)
        {
            item.Flags &= ~TreasureConfig.Chests.ChLocked;
            item.SpecialNameId = (byte)SpecialNameIds.SnEmpty;
            w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
            item.Cost = 0;
        }

        // Was chest still trapped?
        if ((item.Flags & TreasureConfig.Chests.ChLocked) != 0)
        {
            return;
        }

        // Oh, yes it was...   (Snicker)
        PlayerTraps.ChestTrap(w, coord);

        // (the chest may have exploded: re-read the tile)
        var chestId = w.Dg.Floor[coord.Y, coord.X].TreasureId;
        if (chestId != 0)
        {
            ref var chest = ref w.Treasure[chestId];

            // Chest treasure is allocated as if a creature had been killed.
            // clear the cursed chest/monster win flag, so that people
            // can not win by opening a cursed chest
            chest.Flags &= ~TreasureConfig.Flags.TrCursed;

            Creatures.MonsterLife.Death(w, coord, w.Treasure[chestId].Flags);

            w.Treasure[w.Dg.Floor[coord.Y, coord.X].TreasureId].Flags = 0;
        }
    }

    // src/player.cpp:1321 playerOpenClosedObject() -- opens a closed door
    // or closed chest. -RAK-
    public static void OpenClosedObject(PlayerWorld w, int dir)
    {
        var coord = w.Py.Pos;
        PlayerMovement.MovePosition(w.Dg, dir, ref coord);

        var noObject = false;

        var tile = w.Dg.Floor[coord.Y, coord.X];
        var item = w.Treasure[tile.TreasureId];

        if (tile.CreatureId > 1 && tile.TreasureId != 0 && (item.CategoryId == TreasureCategory.TvClosedDoor || item.CategoryId == TreasureCategory.TvChest))
        {
            PlayerCore.ObjectBlockedByMonster(w, tile.CreatureId);
        }
        else if (tile.TreasureId != 0)
        {
            if (item.CategoryId == TreasureCategory.TvClosedDoor)
            {
                OpenClosedDoor(w, coord);
            }
            else if (item.CategoryId == TreasureCategory.TvChest)
            {
                OpenClosedChest(w, coord);
            }
            else
            {
                noObject = true;
            }
        }
        else
        {
            noObject = true;
        }

        if (noObject)
        {
            w.Game.PlayerFreeTurn = true;
            PlayerCore.PrintMessage(w, "I do not see anything you can open there.");
        }
    }

    // src/player.cpp:1356 playerCloseDoor() -- closes an open door. -RAK-
    public static void CloseDoor(PlayerWorld w, int dir)
    {
        var coord = w.Py.Pos;
        PlayerMovement.MovePosition(w.Dg, dir, ref coord);

        ref var tile = ref w.Dg.Floor[coord.Y, coord.X];
        ref var item = ref w.Treasure[tile.TreasureId];

        var noObject = false;

        if (tile.TreasureId != 0)
        {
            if (item.CategoryId == TreasureCategory.TvOpenDoor)
            {
                if (tile.CreatureId == 0)
                {
                    if (item.MiscUse == 0)
                    {
                        item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjClosedDoor);
                        tile.FeatureId = TileConstants.TileBlockedFloor;
                        ScreenRendering.LiteSpot(w.Dg, coord);
                    }
                    else
                    {
                        PlayerCore.PrintMessage(w, "The door appears to be broken.");
                    }
                }
                else
                {
                    PlayerCore.ObjectBlockedByMonster(w, tile.CreatureId);
                }
            }
            else
            {
                noObject = true;
            }
        }
        else
        {
            noObject = true;
        }

        if (noObject)
        {
            w.Game.PlayerFreeTurn = true;
            PlayerCore.PrintMessage(w, "I do not see anything you can close there.");
        }
    }

    // src/game.cpp:224 getRandomDirection()
    private static int GetRandomDirection()
    {
        int dir;

        do
        {
            dir = RandomNumber.Next(9);
        } while (dir == 5);

        return dir;
    }

    // src/player_bash.cpp:36 playerBash() -- bash open a door or chest -RAK-
    // Note: Affected by strength and weight of character. (See the
    // original's comment for the door misc_use conventions: positive
    // locked, negative stuck, positive on an open door = broken.)
    public static void Bash(PlayerWorld w, int dir)
    {
        var py = w.Py;

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are confused.");
            dir = GetRandomDirection();
        }

        var coord = py.Pos;
        PlayerMovement.MovePosition(w.Dg, dir, ref coord);

        var tile = w.Dg.Floor[coord.Y, coord.X];

        if (tile.CreatureId > 1)
        {
            BashPosition(w, coord);
            return;
        }

        if (tile.TreasureId != 0)
        {
            var category = w.Treasure[tile.TreasureId].CategoryId;

            if (category == TreasureCategory.TvClosedDoor)
            {
                BashClosedDoor(w, coord, dir, tile.TreasureId);
            }
            else if (category == TreasureCategory.TvChest)
            {
                BashClosedChest(w, tile.TreasureId);
            }
            else
            {
                // Can't give free turn, or else player could try
                // directions until they find the invisible creature
                PlayerCore.PrintMessage(w, "You bash it, but nothing interesting happens.");
            }
            return;
        }

        if (tile.FeatureId < TileConstants.MinCaveWall)
        {
            PlayerCore.PrintMessage(w, "You bash at empty space.");
            return;
        }

        // same message for wall as for secret door
        PlayerCore.PrintMessage(w, "You bash it, but nothing interesting happens.");
    }

    // src/player_bash.cpp:153 playerBashPosition()
    private static void BashPosition(PlayerWorld w, Coord coord)
    {
        // Is a Coward?
        if (w.Py.Flags.Afraid > 0)
        {
            PlayerCore.PrintMessage(w, "You are afraid!");
            return;
        }

        PlayerCombat.BashAttack(w, coord);
    }

    // src/player_bash.cpp:163 playerBashClosedDoor()
    private static void BashClosedDoor(PlayerWorld w, Coord coord, int dir, int treasureId)
    {
        var py = w.Py;
        ref var item = ref w.Treasure[treasureId];

        PlayerCore.PrintMessageNoCommandInterrupt(w, "You smash into the door!");

        var chance = py.Stats.Used[PlayerConfig.Attr.Str] + py.Misc.Weight / 2;

        // Use (roughly) similar method as for monsters.
        var absMiscUse = Math.Abs((int)item.MiscUse);
        if (RandomNumber.Next(chance * (20 + absMiscUse)) < 10 * (chance - absMiscUse))
        {
            PlayerCore.PrintMessage(w, "The door crashes open!");

            item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjOpenDoor);

            // 50% chance of breaking door
            item.MiscUse = (short)(1 - RandomNumber.Next(2));

            w.Dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileCorrFloor;

            if (py.Flags.Confused == 0)
            {
                PlayerMovement.Move(w, dir, false);
            }
            else
            {
                ScreenRendering.LiteSpot(w.Dg, coord);
            }

            return;
        }

        if (RandomNumber.Next(150) > py.Stats.Used[PlayerConfig.Attr.Dex])
        {
            PlayerCore.PrintMessage(w, "You are off-balance.");
            py.Flags.Paralysis = (short)(1 + RandomNumber.Next(2));
            return;
        }

        if (w.Game.CommandCount == 0)
        {
            PlayerCore.PrintMessage(w, "The door holds firm.");
        }
    }

    // src/player_bash.cpp:205 playerBashClosedChest()
    private static void BashClosedChest(PlayerWorld w, int treasureId)
    {
        ref var item = ref w.Treasure[treasureId];

        if (RandomNumber.Next(10) == 1)
        {
            PlayerCore.PrintMessage(w, "You have destroyed the chest.");
            PlayerCore.PrintMessage(w, "and its contents!");

            item.Id = DungeonGenerationConfig.Objects.ObjRuinedChest;
            item.Flags = 0;

            return;
        }

        if ((item.Flags & TreasureConfig.Chests.ChLocked) != 0 && RandomNumber.Next(10) == 1)
        {
            PlayerCore.PrintMessage(w, "The lock breaks open!");

            item.Flags &= ~TreasureConfig.Chests.ChLocked;

            return;
        }

        PlayerCore.PrintMessageNoCommandInterrupt(w, "The chest holds firm.");
    }
}
