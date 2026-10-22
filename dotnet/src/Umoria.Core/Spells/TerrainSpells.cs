using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M10: src/spells.cpp's terrain and object spells -- the ones that rewrite
// tiles or the treasure list: light line / starlite (blue light hurts
// light-sensitive monsters), stone to mud, build wall, door and trap
// destruction/disarming in a line or adjacent, surround with traps/doors,
// the destruction spell, glyph of warding and create food. Deferred from
// M9 because only some of them hurt monsters, and only as a side effect.
//
// stone-to-mud's "The <object> turns into mud." uses the bare catalog name
// until itemDescription() exists (M11), like M9's armour messages; it still
// zeroes game.command_count.
public static class TerrainSpells
{
    // src/spells.cpp:587 spellLightLineTouchesMonster() -- update monster
    // when light line spell touches it.
    private static void LightLineTouchesMonster(PlayerWorld w, int monsterId)
    {
        // light up and draw monster
        MonsterAi.UpdateVisibility(w, monsterId);

        var monster = w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

        if ((creature.Defenses & MonsterConfig.Defense.CdLight) != 0)
        {
            if (monster.Lit)
            {
                w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdLight;
            }

            if (MonsterLife.TakeHit(w, monsterId, DiceRoller.Roll(new Model.Generated.Dice(2, 8))) >= 0)
            {
                MonsterLife.PrintActionText(w, name, "shrivels away in the light!");
                PlayerCore.DisplayCharacterExperience(w);
            }
            else
            {
                MonsterLife.PrintActionText(w, name, "cringes from the light!");
            }
        }
    }

    // src/spells.cpp:611 spellLightLine() -- leave a line of light in given
    // dir, blue light can sometimes hurt creatures. -RAK-
    public static void LightLine(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var finished = false;

        while (!finished)
        {
            ref var tile = ref dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                PlayerMovement.MovePosition(dg, direction, ref coord);
                finished = true;
                continue; // we're done here, break out of the loop
            }

            if (!tile.PermanentLight && !tile.TemporaryLight)
            {
                // set permanent_light so that dungeonLiteSpot will work
                tile.PermanentLight = true;

                if (tile.FeatureId == TileConstants.TileLightFloor)
                {
                    if (ScreenPanel.CoordInsidePanel(dg, coord))
                    {
                        DungeonLighting.LightRoom(dg, w.Treasure, coord);
                    }
                }
                else
                {
                    ScreenRendering.LiteSpot(dg, coord);
                }
            }

            // set permanent_light in case temporary_light was true above
            tile.PermanentLight = true;

            if (tile.CreatureId > 1)
            {
                LightLineTouchesMonster(w, tile.CreatureId);
            }

            // move must be at end because want to light up current tmp_coord
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;
        }
    }

    // src/spells.cpp:657 spellStarlite() -- light line in all directions
    // -RAK-
    public static void Starlite(PlayerWorld w, Coord coord)
    {
        if (w.Py.Flags.Blind < 1)
        {
            PlayerCore.PrintMessage(w, "The end of the staff bursts into a blue shimmering light.");
        }

        for (var dir = 1; dir <= 9; dir++)
        {
            if (dir != 5)
            {
                LightLine(w, coord, dir);
            }
        }
    }

    // destroy traps on chest and unlock (shared by the three disarm spells)
    private static void UnlockChest(PlayerWorld w, ref Inventory item)
    {
        item.Flags &= ~(TreasureConfig.Chests.ChTrapped | TreasureConfig.Chests.ChLocked);
        item.SpecialNameId = (byte)SpecialNameIds.SnUnlocked;
    }

    // src/spells.cpp:670 spellDisarmAllInDirection() -- disarms all
    // traps/chests in a given direction -RAK-
    public static bool DisarmAllInDirection(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var disarmed = false;

        Coord tileCoord;

        do
        {
            tileCoord = coord;
            ref var tile = ref dg.Floor[coord.Y, coord.X];

            // note, must continue up to and including the first non open space,
            // because secret doors have feature_id greater than MAX_OPEN_SPACE
            if (tile.TreasureId != 0)
            {
                ref var item = ref w.Treasure[tile.TreasureId];

                if (item.CategoryId == TreasureCategory.TvInvisTrap || item.CategoryId == TreasureCategory.TvVisTrap)
                {
                    if (w.Objects.DeleteObject(coord))
                    {
                        disarmed = true;
                    }
                }
                else if (item.CategoryId == TreasureCategory.TvClosedDoor)
                {
                    // Locked or jammed doors become merely closed.
                    item.MiscUse = 0;
                }
                else if (item.CategoryId == TreasureCategory.TvSecretDoor)
                {
                    tile.FieldMark = true;
                    w.Objects.TrapChangeVisibility(coord);
                    disarmed = true;
                }
                else if (item.CategoryId == TreasureCategory.TvChest && item.Flags != 0)
                {
                    disarmed = true;
                    PlayerCore.PrintMessage(w, "Click!");

                    UnlockChest(w, ref item);

                    w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
                }
            }

            // move must be at end because want to light up current spot
            PlayerMovement.MovePosition(dg, direction, ref coord);

            distance++;
        } while (distance <= TreasureConfig.ObjectBoltsMaxRange && dg.Floor[tileCoord.Y, tileCoord.X].FeatureId <= TileConstants.MaxOpenSpace);

        return disarmed;
    }

    // src/spells.cpp:1381 spellWallToMud() -- turn stone to mud, delete
    // wall. -RAK-
    public static bool WallToMud(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var turned = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            ref var tile = ref dg.Floor[coord.Y, coord.X];

            // note, this ray can move through walls as it turns them to mud
            if (distance == TreasureConfig.ObjectBoltsMaxRange)
            {
                finished = true;
            }

            if (tile.FeatureId >= TileConstants.MinCaveWall && tile.FeatureId != TileConstants.TileBoundaryWall)
            {
                finished = true;

                PlayerTunnel.TunnelWall(w, coord, 1, 0);

                if (DungeonLighting.CaveTileVisible(dg, coord))
                {
                    turned = true;
                    PlayerCore.PrintMessage(w, "The wall turns into mud.");
                }
            }
            else if (tile.TreasureId != 0 && tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;

                if (ScreenPanel.CoordInsidePanel(dg, coord) && DungeonLighting.CaveTileVisible(dg, coord))
                {
                    turned = true;

                    var description = DataTreasureData.GameObjects[w.Treasure[tile.TreasureId].Id].Name;
                    PlayerCore.PrintMessage(w, $"The {description} turns into mud.");
                }

                if (w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvRubble)
                {
                    w.Objects.DeleteObject(coord);
                    if (RandomNumber.Next(10) == 1)
                    {
                        w.Objects.PlaceRandomObjectAt(coord, false);
                        if (DungeonLighting.CaveTileVisible(dg, coord))
                        {
                            PlayerCore.PrintMessage(w, "You have found something!");
                        }
                    }
                    ScreenRendering.LiteSpot(dg, coord);
                }
                else
                {
                    w.Objects.DeleteObject(coord);
                }
            }

            if (tile.CreatureId > 1)
            {
                var monster = w.Monsters.Monsters[tile.CreatureId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                if ((creature.Defenses & MonsterConfig.Defense.CdStone) != 0)
                {
                    var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                    // Should get these messages even if the monster is not visible.
                    var creatureId = MonsterLife.TakeHit(w, tile.CreatureId, 100);
                    if (creatureId >= 0)
                    {
                        w.CreatureRecall[creatureId].Defenses |= MonsterConfig.Defense.CdStone;
                        MonsterLife.PrintActionText(w, name, "dissolves!");
                        PlayerCore.DisplayCharacterExperience(w); // print msg before calling prt_exp
                    }
                    else
                    {
                        w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdStone;
                        MonsterLife.PrintActionText(w, name, "grunts in pain!");
                    }
                    finished = true;
                }
            }
        }

        return turned;
    }

    // src/spells.cpp:1460 spellDestroyDoorsTrapsInDirection() -- destroy
    // all traps and doors in a given direction -RAK-
    //
    // NOTE: the loop condition is `distance <= range || open space` in the
    // original (not `&&` as in its siblings), so the ray runs on past the
    // bolt range until it reaches a closed space. Ported as written.
    public static bool DestroyDoorsTrapsInDirection(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var destroyed = false;
        var distance = 0;

        do
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            var tile = dg.Floor[coord.Y, coord.X];

            // must move into first closed spot, as it might be a secret door
            if (tile.TreasureId != 0)
            {
                ref var item = ref w.Treasure[tile.TreasureId];

                if (item.CategoryId == TreasureCategory.TvInvisTrap || item.CategoryId == TreasureCategory.TvClosedDoor ||
                    item.CategoryId == TreasureCategory.TvVisTrap || item.CategoryId == TreasureCategory.TvOpenDoor ||
                    item.CategoryId == TreasureCategory.TvSecretDoor)
                {
                    if (w.Objects.DeleteObject(coord))
                    {
                        destroyed = true;
                        PlayerCore.PrintMessage(w, "There is a bright flash of light!");
                    }
                }
                else if (item.CategoryId == TreasureCategory.TvChest && item.Flags != 0)
                {
                    destroyed = true;
                    PlayerCore.PrintMessage(w, "Click!");

                    UnlockChest(w, ref item);

                    w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
                }
            }
        } while (distance <= TreasureConfig.ObjectBoltsMaxRange || dg.Floor[coord.Y, coord.X].FeatureId <= TileConstants.MaxOpenSpace);

        return destroyed;
    }

    // src/spells.cpp:1542 spellBuildWall() -- create a wall. -RAK-
    public static bool BuildWall(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var built = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            ref var tile = ref dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue; // we're done here, break out of the loop
            }

            if (tile.TreasureId != 0)
            {
                w.Objects.DeleteObject(coord);
            }

            if (tile.CreatureId > 1)
            {
                finished = true;

                ref var monster = ref w.Monsters.Monsters[tile.CreatureId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                if ((creature.Movement & MonsterConfig.Move.CmPhase) == 0)
                {
                    // monster does not move, can't escape the wall
                    int damage;
                    if ((creature.Movement & MonsterConfig.Move.CmAttackOnly) != 0)
                    {
                        // this will kill everything
                        damage = 3000;
                    }
                    else
                    {
                        damage = DiceRoller.Roll(new Model.Generated.Dice(4, 8));
                    }

                    var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                    MonsterLife.PrintActionText(w, name, "wails out in pain!");

                    if (MonsterLife.TakeHit(w, tile.CreatureId, damage) >= 0)
                    {
                        MonsterLife.PrintActionText(w, name, "is embedded in the rock.");
                        PlayerCore.DisplayCharacterExperience(w);
                    }
                }
                else if (creature.Sprite == (byte)'E' || creature.Sprite == (byte)'X')
                {
                    // must be an earth elemental, an earth spirit,
                    // or a Xorn to increase its hit points
                    monster.Hp = (short)(monster.Hp + DiceRoller.Roll(new Model.Generated.Dice(4, 8)));
                }
            }

            tile.FeatureId = TileConstants.TileMagmaWall;
            tile.FieldMark = false;

            // Permanently light this wall if it is lit by player's lamp.
            tile.PermanentLight = tile.TemporaryLight || tile.PermanentLight;
            ScreenRendering.LiteSpot(dg, coord);

            built = true;
        }

        return built;
    }

    // src/spells.cpp:453 spellSurroundPlayerWithTraps() -- surround the
    // fool with traps (chuckle) -RAK-
    public static bool SurroundPlayerWithTraps(PlayerWorld w)
    {
        var dg = w.Dg;
        var py = w.Py;

        for (var y = py.Pos.Y - 1; y <= py.Pos.Y + 1; y++)
        {
            for (var x = py.Pos.X - 1; x <= py.Pos.X + 1; x++)
            {
                // Don't put a trap under the player, since this can lead to
                // strange situations, e.g. falling through a trap door while
                // trying to rest, setting off a falling rock trap and ending
                // up under the rock.
                if (y == py.Pos.Y && x == py.Pos.X)
                {
                    continue;
                }

                var coord = new Coord(y, x);
                ref var tile = ref dg.Floor[y, x];

                if (tile.FeatureId <= TileConstants.MaxCaveFloor)
                {
                    if (tile.TreasureId != 0)
                    {
                        w.Objects.DeleteObject(coord);
                    }

                    w.Objects.SetTrap(coord, RandomNumber.Next(DungeonGenerationConfig.Objects.MaxTraps) - 1);

                    // don't let player gain exp from the newly created traps
                    w.Treasure[tile.TreasureId].MiscUse = 0;

                    // open pits are immediately visible, so call dungeonLiteSpot
                    ScreenRendering.LiteSpot(dg, coord);
                }
            }
        }

        // traps are always placed, so just return true
        return true;
    }

    // src/spells.cpp:489 spellSurroundPlayerWithDoors() -- surround the
    // player with doors. -RAK-
    public static bool SurroundPlayerWithDoors(PlayerWorld w)
    {
        var dg = w.Dg;
        var py = w.Py;
        var created = false;

        for (var y = py.Pos.Y - 1; y <= py.Pos.Y + 1; y++)
        {
            for (var x = py.Pos.X - 1; x <= py.Pos.X + 1; x++)
            {
                // Don't put a door under the player!
                if (y == py.Pos.Y && x == py.Pos.X)
                {
                    continue;
                }

                var coord = new Coord(y, x);
                ref var tile = ref dg.Floor[y, x];

                if (tile.FeatureId <= TileConstants.MaxCaveFloor)
                {
                    if (tile.TreasureId != 0)
                    {
                        w.Objects.DeleteObject(coord);
                    }

                    var freeId = w.Treasure.Popt();
                    tile.FeatureId = TileConstants.TileBlockedFloor;
                    tile.TreasureId = (byte)freeId;

                    w.Treasure[freeId] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjClosedDoor);
                    ScreenRendering.LiteSpot(dg, coord);

                    created = true;
                }
            }
        }

        return created;
    }

    // src/spells.cpp:524 spellDestroyAdjacentDoorsTraps() -- destroys any
    // adjacent door(s)/trap(s) -RAK-
    public static bool DestroyAdjacentDoorsTraps(PlayerWorld w)
    {
        var dg = w.Dg;
        var py = w.Py;
        var destroyed = false;

        for (var y = py.Pos.Y - 1; y <= py.Pos.Y + 1; y++)
        {
            for (var x = py.Pos.X - 1; x <= py.Pos.X + 1; x++)
            {
                var coord = new Coord(y, x);
                var tile = dg.Floor[y, x];

                if (tile.TreasureId == 0)
                {
                    continue;
                }

                ref var item = ref w.Treasure[tile.TreasureId];

                if ((item.CategoryId >= TreasureCategory.TvInvisTrap && item.CategoryId <= TreasureCategory.TvClosedDoor && item.CategoryId != TreasureCategory.TvRubble) ||
                    item.CategoryId == TreasureCategory.TvSecretDoor)
                {
                    if (w.Objects.DeleteObject(coord))
                    {
                        destroyed = true;
                    }
                }
                else if (item.CategoryId == TreasureCategory.TvChest && item.Flags != 0)
                {
                    UnlockChest(w, ref item);

                    destroyed = true;

                    PlayerCore.PrintMessage(w, "You have disarmed the chest.");
                    w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
                }
            }
        }

        return destroyed;
    }

    // src/spells.cpp:2215 replaceSpot()
    private static void ReplaceSpot(PlayerWorld w, Coord coord, int typ)
    {
        ref var tile = ref w.Dg.Floor[coord.Y, coord.X];

        switch (typ)
        {
            case 1:
            case 2:
            case 3:
                tile.FeatureId = TileConstants.TileCorrFloor;
                break;
            case 4:
            case 7:
            case 10:
                tile.FeatureId = TileConstants.TileGraniteWall;
                break;
            case 5:
            case 8:
            case 11:
                tile.FeatureId = TileConstants.TileMagmaWall;
                break;
            case 6:
            case 9:
            case 12:
                tile.FeatureId = TileConstants.TileQuartzWall;
                break;
        }

        tile.PermanentLight = false;
        tile.FieldMark = false;
        tile.PermaLitRoom = false; // this is no longer part of a room

        if (tile.TreasureId != 0)
        {
            w.Objects.DeleteObject(coord);
        }

        if (tile.CreatureId > 1)
        {
            w.Monsters.DeleteMonster(tile.CreatureId);
        }
    }

    // src/spells.cpp:2260 spellDestroyArea() -- the spell of destruction.
    // -RAK-
    // NOTE:
    //   Winning creatures that are deleted will be considered as teleporting to another level.
    //   This will NOT win the game.
    public static void DestroyArea(PlayerWorld w, Coord coord)
    {
        var dg = w.Dg;

        if (dg.CurrentLevel > 0)
        {
            for (var y = coord.Y - 15; y <= coord.Y + 15; y++)
            {
                for (var x = coord.X - 15; x <= coord.X + 15; x++)
                {
                    var spot = new Coord(y, x);

                    if (CoordHelpers.InBounds(dg, spot) && dg.Floor[y, x].FeatureId != TileConstants.TileBoundaryWall)
                    {
                        var distance = CoordHelpers.DistanceBetween(spot, coord);

                        // clear player's spot, but don't put wall there
                        if (distance == 0)
                        {
                            ReplaceSpot(w, spot, 1);
                        }
                        else if (distance < 13)
                        {
                            ReplaceSpot(w, spot, RandomNumber.Next(6));
                        }
                        else if (distance < 16)
                        {
                            ReplaceSpot(w, spot, RandomNumber.Next(9));
                        }
                    }
                }
            }
        }

        PlayerCore.PrintMessage(w, "There is a searing blast of light!");
        w.Py.Flags.Blind = (short)(w.Py.Flags.Blind + 10 + RandomNumber.Next(10));
    }

    // src/spells.cpp:2097 spellWardingGlyph() -- leave a glyph of warding.
    // Creatures will not pass over! -RAK-
    public static void WardingGlyph(PlayerWorld w)
    {
        var pos = w.Py.Pos;

        if (w.Dg.Floor[pos.Y, pos.X].TreasureId == 0)
        {
            var freeId = w.Treasure.Popt();
            w.Dg.Floor[pos.Y, pos.X].TreasureId = (byte)freeId;
            w.Treasure[freeId] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjScareMon);
        }
    }

    // src/spells.cpp:2011 spellCreateFood() -- create some high quality mush
    // for the player. -RAK-
    public static void CreateFood(PlayerWorld w)
    {
        // Note: must take reference to this location as dungeonPlaceRandomObjectAt()
        // below, changes the tile values.
        ref var tile = ref w.Dg.Floor[w.Py.Pos.Y, w.Py.Pos.X];

        // take no action here, don't want to destroy object under player
        if (tile.TreasureId != 0)
        {
            // set player_free_turn so that scroll/spell points won't be used
            w.Game.PlayerFreeTurn = true;

            PlayerCore.PrintMessage(w, "There is already an object under you.");

            return;
        }

        w.Objects.PlaceRandomObjectAt(w.Py.Pos, false);
        w.Treasure[tile.TreasureId] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjMush);
    }
}
