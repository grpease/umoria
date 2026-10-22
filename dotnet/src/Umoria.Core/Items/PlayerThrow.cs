using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Items;

// src/player_throw.cpp -- throwing (and firing) objects (M11). The flight
// redraws the missile on every lit square (panelPutTile()/putQIO()); that is
// display, so only the dungeonLiteSpot() calls are kept, as elsewhere.
public static class PlayerThrow
{
    // src/player_throw.cpp:10 inventoryThrow()
    private static Inventory InventoryThrow(Player py, int itemId)
    {
        ref var item = ref py.Inventory[itemId];

        var treasure = item;

        if (item.ItemsCount > 1)
        {
            treasure.ItemsCount = 1;
            item.ItemsCount--;
            py.Pack.Weight = (short)(py.Pack.Weight - item.Weight);
            py.Flags.Status |= PlayerConfig.Status.PyStrWgt;
        }
        else
        {
            InventoryDamage.DestroyItem(py, itemId);
        }

        return treasure;
    }

    // src/player_throw.cpp:26 weaponMissileFacts() -- obtain the hit and
    // damage bonuses and the maximum distance for a thrown missile.
    private static void WeaponMissileFacts(Player py, in Inventory item, out int baseToHit, out int plusToHit, out int damage, out int distance)
    {
        int weight = item.Weight;
        if (weight < 1)
        {
            weight = 1;
        }

        var weapon = py.Inventory[PlayerConfig.Equipment.Wield];

        // Throwing objects
        damage = DiceRoller.Roll(item.Damage) + item.ToDamage;
        baseToHit = py.Misc.BthWithBows * 75 / 100;
        plusToHit = py.Misc.PlussesToHit + item.ToHit;

        // Add this back later if the correct throwing device. -CJS-
        if (weapon.CategoryId != TreasureCategory.TvNothing)
        {
            plusToHit -= weapon.ToHit;
        }

        distance = ((py.Stats.Used[PlayerConfig.Attr.Str] + 20) * 10) / weight;
        if (distance > 10)
        {
            distance = 10;
        }

        // multiply damage bonuses instead of adding, when have proper
        // missile/weapon combo, this makes them much more useful

        // Using Bows, slings, or crossbows?
        if (weapon.CategoryId != TreasureCategory.TvBow)
        {
            return;
        }

        // misc_use: the launcher; the ammunition it takes; the multiplier;
        // the range
        (byte Ammo, int Multiplier, int Range) launcher = weapon.MiscUse switch
        {
            1 => (TreasureCategory.TvSlingAmmo, 2, 20), // Sling and ammo
            2 => (TreasureCategory.TvArrow, 2, 25), // Short Bow and Arrow
            3 => (TreasureCategory.TvArrow, 3, 30), // Long Bow and Arrow
            4 => (TreasureCategory.TvArrow, 4, 35), // Composite Bow and Arrow
            5 => (TreasureCategory.TvBolt, 3, 25), // Light Crossbow and Bolt
            6 => (TreasureCategory.TvBolt, 4, 35), // Heavy Crossbow and Bolt
            _ => (0, 0, 0),
        };

        if (launcher.Multiplier != 0 && item.CategoryId == launcher.Ammo)
        {
            baseToHit = py.Misc.BthWithBows;
            plusToHit += 2 * weapon.ToHit;
            damage += weapon.ToDamage;
            damage *= launcher.Multiplier;
            distance = launcher.Range;
        }
    }

    // src/player_throw.cpp:116 inventoryDropOrThrowItem()
    private static void DropOrThrowItem(PlayerWorld w, Coord coord, in Inventory item)
    {
        var dg = w.Dg;
        var position = coord;

        var flag = false;

        if (RandomNumber.Next(10) > 1)
        {
            for (var k = 0; !flag && k <= 9;)
            {
                if (CoordHelpers.InBounds(dg, position))
                {
                    if (dg.Floor[position.Y, position.X].FeatureId <= TileConstants.MaxOpenSpace && dg.Floor[position.Y, position.X].TreasureId == 0)
                    {
                        flag = true;
                    }
                }

                if (!flag)
                {
                    position = new Coord(coord.Y + RandomNumber.Next(3) - 2, coord.X + RandomNumber.Next(3) - 2);
                    k++;
                }
            }
        }

        if (flag)
        {
            var curPos = w.Treasure.Popt();
            dg.Floor[position.Y, position.X].TreasureId = (byte)curPos;
            w.Treasure[curPos] = item;
            ScreenRendering.LiteSpot(dg, position);
        }
        else
        {
            var description = ItemDescription.Describe(w.Identification, item, false);
            PlayerCore.PrintMessage(w, Helpers.Truncate($"The {description} disappears.", Helpers.MoriaObjDescSize));
        }
    }

    // src/player_throw.cpp:156 playerThrowItem() -- throw an object across
    // the dungeon. -RAK-
    // Note: Flasks of oil do fire damage
    // Note: Extra damage and chance of hitting when missiles are used
    // with correct weapon. i.e. wield bow and throw arrow.
    public static void ThrowItem(PlayerWorld w)
    {
        var py = w.Py;
        var dg = w.Dg;

        if (py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "But you are not carrying anything.");
            w.Game.PlayerFreeTurn = true;
            return;
        }

        if (!PlayerCore.GetItemId(w, "Fire/Throw which one?", 0, py.Pack.UniqueItems - 1, out var itemId))
        {
            return;
        }

        if (!PlayerCore.GetDirectionWithMemory(w, null, out var dir))
        {
            return;
        }

        ItemDescription.TypeRemainingCountDescription(w, itemId);

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are confused.");
            dir = PlayerCore.GetRandomDirection();
        }

        var thrownItem = InventoryThrow(py, itemId);

        WeaponMissileFacts(py, thrownItem, out var tbth, out var tpth, out var tdam, out var tdis);

        var currentDistance = 0;

        var coord = py.Pos;
        var oldCoord = py.Pos;

        var flag = false;

        while (!flag)
        {
            PlayerMovement.MovePosition(dg, dir, ref coord);
            currentDistance++;
            ScreenRendering.LiteSpot(dg, oldCoord);

            if (currentDistance > tdis)
            {
                flag = true;
            }

            var tile = dg.Floor[coord.Y, coord.X];

            if (tile.FeatureId <= TileConstants.MaxOpenSpace && !flag)
            {
                if (tile.CreatureId > 1)
                {
                    flag = true;

                    var monster = w.Monsters.Monsters[tile.CreatureId];

                    tbth -= currentDistance;

                    // if monster not lit, make it much more difficult to hit, subtract
                    // off most bonuses, and reduce bth_with_bows depending on distance.
                    if (!monster.Lit)
                    {
                        tbth /= currentDistance + 2;
                        tbth -= py.Misc.Level * DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Bthb] / 2;
                        tbth -= tpth * (PlayerConfig.BthPerPlusToHitAdjust - 1);
                    }

                    if (PlayerMovement.TestBeingHit(w, tbth, py.Misc.Level, tpth, DataCreaturesData.CreaturesList[monster.CreatureId].Ac, PlayerConfig.ClassLevelAdj.Bthb))
                    {
                        int damage = monster.CreatureId;

                        var description = ItemDescription.Describe(w.Identification, thrownItem, false);

                        // Does the player know what they're fighting?
                        bool visible;
                        string msg;
                        if (!monster.Lit)
                        {
                            msg = $"You hear a cry as the {description} finds a mark.";
                            visible = false;
                        }
                        else
                        {
                            msg = $"The {description} hits the {DataCreaturesData.CreaturesList[damage].Name}.";
                            visible = true;
                        }
                        PlayerCore.PrintMessage(w, Helpers.Truncate(msg, Helpers.MoriaObjDescSize));

                        tdam = PlayerCombat.ItemMagicAbilityDamage(w, thrownItem, tdam, damage);
                        tdam = PlayerCombat.WeaponCriticalBlow(w, thrownItem.Weight, tpth, tdam, PlayerConfig.ClassLevelAdj.Bthb);

                        if (tdam < 0)
                        {
                            tdam = 0;
                        }

                        damage = MonsterLife.TakeHit(w, tile.CreatureId, tdam);

                        if (damage >= 0)
                        {
                            if (!visible)
                            {
                                PlayerCore.PrintMessage(w, "You have killed something!");
                            }
                            else
                            {
                                PlayerCore.PrintMessage(w, Helpers.Truncate($"You have killed the {DataCreaturesData.CreaturesList[damage].Name}.", Helpers.MoriaObjDescSize));
                            }
                            PlayerCore.DisplayCharacterExperience(w);
                        }
                    }
                    else
                    {
                        DropOrThrowItem(w, oldCoord, thrownItem);
                    }
                }

                // (else: the missile is drawn in flight if the square is lit
                // -- display only)
            }
            else
            {
                flag = true;
                DropOrThrowItem(w, oldCoord, thrownItem);
            }

            oldCoord = coord;
        }
    }
}
