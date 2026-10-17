using Umoria.Core.Config;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// M8: src/player_tunnel.cpp (the T command) plus src/player.cpp's
// playerTunnelWall(), which the wall-to-mud spell (M10) will share.
// Tunnelling into a monster is a bump attack (M9; see
// PlayerMovement.AttackPosition).
public static class PlayerTunnel
{
    // Don't let the player tunnel somewhere illegal, this is necessary to
    // prevent the player from getting a free attack by trying to tunnel
    // somewhere where it has no effect.
    private static bool CanTunnel(PlayerWorld w, int treasureId, int tileId)
    {
        if (tileId < TileConstants.MinCaveWall &&
            (treasureId == 0 || (w.Treasure[treasureId].CategoryId != TreasureCategory.TvRubble && w.Treasure[treasureId].CategoryId != TreasureCategory.TvSecretDoor)))
        {
            w.Game.PlayerFreeTurn = true;

            if (treasureId == 0)
            {
                PlayerCore.PrintMessage(w, "Tunnel through what?  Empty air?!?");
            }
            else
            {
                PlayerCore.PrintMessage(w, "You can't tunnel through that.");
            }

            return false;
        }

        return true;
    }

    // Compute the digging ability of player; based on strength, and type of tool used
    private static int DiggingAbility(Player py, in Inventory weapon)
    {
        int diggingAbility = py.Stats.Used[PlayerConfig.Attr.Str];

        if ((weapon.Flags & TreasureConfig.Flags.TrTunnel) != 0)
        {
            diggingAbility += 25 + weapon.MiscUse * 50;
        }
        else
        {
            diggingAbility += DiceRoller.MaxRoll(weapon.Damage) + weapon.ToHit + weapon.ToDamage;

            // divide by two so that digging without shovel isn't too easy
            diggingAbility >>= 1;
        }

        // If this weapon is too heavy for the player to wield properly,
        // then also make it harder to dig with it.
        if (py.WeaponIsHeavy)
        {
            diggingAbility += (py.Stats.Used[PlayerConfig.Attr.Str] * 15) - weapon.Weight;

            if (diggingAbility < 0)
            {
                diggingAbility = 0;
            }
        }

        return diggingAbility;
    }

    private static void DigWall(PlayerWorld w, Coord coord, int diggingAbility, int chance, string failMessage)
    {
        if (TunnelWall(w, coord, diggingAbility, chance))
        {
            PlayerCore.PrintMessage(w, "You have finished the tunnel.");
        }
        else
        {
            PlayerCore.PrintMessageNoCommandInterrupt(w, failMessage);
        }
    }

    private static void DigRubble(PlayerWorld w, Coord coord, int diggingAbility)
    {
        if (diggingAbility > RandomNumber.Next(180))
        {
            w.Objects.DeleteObject(coord);
            PlayerCore.PrintMessage(w, "You have removed the rubble.");

            if (RandomNumber.Next(10) == 1)
            {
                w.Objects.PlaceRandomObjectAt(coord, false);

                if (DungeonLighting.CaveTileVisible(w.Dg, coord))
                {
                    PlayerCore.PrintMessage(w, "You have found something!");
                }
            }

            ScreenRendering.LiteSpot(w.Dg, coord);
        }
        else
        {
            PlayerCore.PrintMessageNoCommandInterrupt(w, "You dig in the rubble.");
        }
    }

    // Dig regular walls; Granite, magma intrusion, quartz vein
    // Don't forget the boundary walls, made of titanium (255)
    // Return `true` if a wall was dug at
    private static bool DigAtLocation(PlayerWorld w, Coord coord, byte wallType, int diggingAbility)
    {
        switch (wallType)
        {
            case TileConstants.TileGraniteWall:
                DigWall(w, coord, diggingAbility, RandomNumber.Next(1200) + 80, "You tunnel into the granite wall.");
                break;
            case TileConstants.TileMagmaWall:
                DigWall(w, coord, diggingAbility, RandomNumber.Next(600) + 10, "You tunnel into the magma intrusion.");
                break;
            case TileConstants.TileQuartzWall:
                DigWall(w, coord, diggingAbility, RandomNumber.Next(400) + 10, "You tunnel into the quartz vein.");
                break;
            case TileConstants.TileBoundaryWall:
                PlayerCore.PrintMessage(w, "This seems to be permanent rock.");
                break;
            default:
                return false;
        }
        return true;
    }

    // src/player_tunnel.cpp:132 playerTunnel() -- tunnels through rubble
    // and walls -RAK- Must take into account: secret doors, special tools
    public static void Tunnel(PlayerWorld w, int direction)
    {
        var py = w.Py;

        // Confused?                    75% random movement
        if (py.Flags.Confused > 0 && RandomNumber.Next(4) > 1)
        {
            direction = RandomNumber.Next(9);
        }

        var coord = py.Pos;
        PlayerMovement.MovePosition(w.Dg, direction, ref coord);

        var tile = w.Dg.Floor[coord.Y, coord.X];
        var item = py.Inventory[PlayerConfig.Equipment.Wield];

        if (!CanTunnel(w, tile.TreasureId, tile.FeatureId))
        {
            return;
        }

        if (tile.CreatureId > 1)
        {
            PlayerCore.ObjectBlockedByMonster(w, tile.CreatureId);
            PlayerMovement.AttackPosition(w, coord);
            return;
        }

        if (item.CategoryId != TreasureCategory.TvNothing)
        {
            var diggingAbility = DiggingAbility(py, item);

            if (!DigAtLocation(w, coord, tile.FeatureId, diggingAbility))
            {
                // Is there an object in the way?  (Rubble and secret doors)
                if (tile.TreasureId == 0)
                {
                    throw new InvalidOperationException("playerTunnel(): nothing to tunnel through (the original abort()s)");
                }

                if (w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvRubble)
                {
                    DigRubble(w, coord, diggingAbility);
                }
                else if (w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvSecretDoor)
                {
                    // Found secret door!
                    PlayerCore.PrintMessageNoCommandInterrupt(w, "You tunnel into the granite wall.");
                    PlayerMovement.Search(w, py.Pos, py.Misc.ChanceInSearch);
                }
                else
                {
                    throw new InvalidOperationException("playerTunnel(): unexpected object (the original abort()s)");
                }
            }

            return;
        }

        PlayerCore.PrintMessage(w, "You dig with your hands, making no progress.");
    }

    // src/player.cpp:1399 playerTunnelWall() -- tunneling through real
    // wall: 10, 11, 12 -RAK- Used by TUNNEL and WALL_TO_MUD
    public static bool TunnelWall(PlayerWorld w, Coord coord, int diggingAbility, int diggingChance)
    {
        var dg = w.Dg;

        if (diggingAbility <= diggingChance)
        {
            return false;
        }

        ref var tile = ref dg.Floor[coord.Y, coord.X];

        if (tile.PermaLitRoom)
        {
            // Should become a room space, check to see whether
            // it should be TILE_LIGHT_FLOOR or TILE_DARK_FLOOR.
            var found = false;

            for (var y = coord.Y - 1; y <= coord.Y + 1 && y < DungeonConstants.MaxHeight; y++)
            {
                for (var x = coord.X - 1; x <= coord.X + 1 && x < DungeonConstants.MaxWidth; x++)
                {
                    if (dg.Floor[y, x].FeatureId <= TileConstants.MaxCaveRoom)
                    {
                        tile.FeatureId = dg.Floor[y, x].FeatureId;
                        tile.PermanentLight = dg.Floor[y, x].PermanentLight;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                tile.FeatureId = TileConstants.TileCorrFloor;
                tile.PermanentLight = false;
            }
        }
        else
        {
            // should become a corridor space
            tile.FeatureId = TileConstants.TileCorrFloor;
            tile.PermanentLight = false;
        }

        tile.FieldMark = false;

        if (ScreenPanel.CoordInsidePanel(dg, coord) && (tile.TemporaryLight || tile.PermanentLight) && tile.TreasureId != 0)
        {
            PlayerCore.PrintMessage(w, "You have found something!");
        }

        ScreenRendering.LiteSpot(dg, coord);

        return true;
    }
}
