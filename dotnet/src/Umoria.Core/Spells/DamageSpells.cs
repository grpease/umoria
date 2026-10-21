using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M9: src/spells.cpp's damage-spell primitives -- the slice of spells.cpp
// that is genuinely coupled to monster.cpp in both directions (monster
// breath calls spellBreath(); every one of these kills through
// monsterTakeHit()/monsterDeath()):
//   spellFireBolt(), spellFireBall(), spellBreath() and their
//   spellGetAreaAffectFlags(); spellChangeMonsterHitPoints(),
//   spellDrainLifeFromMonster(), spellDispelCreature() and
//   spellEarthquake().
// Their callers (wands, staffs, scrolls, the mage/priest casting layer) are
// M10/M11; the harness drives them directly with a direction.
//
// The bolt/ball animation (panelPutTile('*'), putQIO()) is display only and
// omitted; dungeonLiteSpot() is the ScreenRendering.LiteSpot no-op.
//
// Deferred to M10 with the rest of spells.cpp: the monster-status spells
// (sleep/confuse/slow/speed, polymorph, clone, teleport-other, genocide,
// turn undead) and the terrain/light spells that happen to hurt monsters
// as a side effect (spellLightLine(), spellWallToMud(), spellBuildWall(),
// spellDestroyArea()).
public static class DamageSpells
{
    // src/spells.cpp:716 spellGetAreaAffectFlags() -- return flags for
    // given type area affect -RAK-
    private static void GetAreaAffectFlags(PlayerWorld w, MagicSpellFlags spellType, out uint weaponType, out int harmType, out Func<Inventory, bool> destroy)
    {
        switch (spellType)
        {
            case MagicSpellFlags.MagicMissile:
                weaponType = 0;
                harmType = 0;
                destroy = InventoryDamage.SetNull;
                break;
            case MagicSpellFlags.Lightning:
                weaponType = MonsterConfig.Spells.CsBrLight;
                harmType = MonsterConfig.Defense.CdLight;
                destroy = InventoryDamage.SetLightningDestroyableItems;
                break;
            case MagicSpellFlags.PoisonGas:
                weaponType = MonsterConfig.Spells.CsBrGas;
                harmType = MonsterConfig.Defense.CdPoison;
                destroy = InventoryDamage.SetNull;
                break;
            case MagicSpellFlags.Acid:
                weaponType = MonsterConfig.Spells.CsBrAcid;
                harmType = MonsterConfig.Defense.CdAcid;
                destroy = InventoryDamage.SetAcidDestroyableItems;
                break;
            case MagicSpellFlags.Frost:
                weaponType = MonsterConfig.Spells.CsBrFrost;
                harmType = MonsterConfig.Defense.CdFrost;
                destroy = InventoryDamage.SetFrostDestroyableItems;
                break;
            case MagicSpellFlags.Fire:
                weaponType = MonsterConfig.Spells.CsBrFire;
                harmType = MonsterConfig.Defense.CdFire;
                destroy = InventoryDamage.SetFireDestroyableItems;
                break;
            case MagicSpellFlags.HolyOrb:
                weaponType = 0;
                harmType = MonsterConfig.Defense.CdEvil;
                destroy = InventoryDamage.SetNull;
                break;
            default:
                // (the original leaves its out-parameters unset here)
                PlayerCore.PrintMessage(w, "ERROR in spellGetAreaAffectFlags()\n");
                weaponType = 0;
                harmType = 0;
                destroy = InventoryDamage.SetNull;
                break;
        }
    }

    // src/spells.cpp:758 printBoltStrikesMonsterMessage()
    private static void PrintBoltStrikesMonsterMessage(PlayerWorld w, in Creature creature, string boltName, bool isLit)
    {
        var monsterName = isLit ? "the " + creature.Name : "it";
        PlayerCore.PrintMessage(w, "The " + boltName + " strikes " + monsterName + ".");
    }

    // src/spells.cpp:770 spellFireBoltTouchesMonster() -- light up, draw,
    // and check for monster damage when Fire Bolt touches it.
    private static void FireBoltTouchesMonster(PlayerWorld w, ref Tile tile, int damage, int harmType, uint weaponId, string boltName)
    {
        int monsterId = tile.CreatureId;
        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        // light up monster and draw monster, temporarily set
        // permanent_light so that `monsterUpdateVisibility()` will work
        var savedLitStatus = tile.PermanentLight;
        tile.PermanentLight = true;
        MonsterAi.UpdateVisibility(w, monsterId);
        tile.PermanentLight = savedLitStatus;

        PrintBoltStrikesMonsterMessage(w, creature, boltName, monster.Lit);

        if ((harmType & creature.Defenses) != 0)
        {
            damage *= 2;
            if (monster.Lit)
            {
                w.CreatureRecall[monster.CreatureId].Defenses |= (ushort)harmType;
            }
        }
        else if ((weaponId & creature.Spells) != 0)
        {
            damage /= 4;
            if (monster.Lit)
            {
                w.CreatureRecall[monster.CreatureId].Spells |= weaponId;
            }
        }

        var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

        if (MonsterLife.TakeHit(w, monsterId, damage) >= 0)
        {
            MonsterLife.PrintActionText(w, name, "dies in a fit of agony.");
            PlayerCore.DisplayCharacterExperience(w);
        }
        else if (damage > 0)
        {
            MonsterLife.PrintActionText(w, name, "screams in agony.");
        }
    }

    // src/spells.cpp:809 spellFireBolt() -- shoot a bolt in a given
    // direction -RAK-
    public static void FireBolt(PlayerWorld w, Coord coord, int direction, int damageHp, MagicSpellFlags spellType, string spellName)
    {
        GetAreaAffectFlags(w, spellType, out var weaponType, out var harmType, out _);

        var distance = 0;
        var finished = false;

        while (!finished)
        {
            var oldCoord = coord;
            PlayerMovement.MovePosition(w.Dg, direction, ref coord);

            distance++;

            ref var tile = ref w.Dg.Floor[coord.Y, coord.X];

            ScreenRendering.LiteSpot(w.Dg, oldCoord);

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue; // we're done here, break out of the loop
            }

            if (tile.CreatureId > 1)
            {
                finished = true;
                FireBoltTouchesMonster(w, ref tile, damageHp, harmType, weaponType, spellName);
            }

            // (else: panelPutTile('*') to show the bolt -- display)
        }
    }

    // src/spells.cpp:849 spellFireBall() -- shoot a ball in a given
    // direction. Note that balls have an area affect. -RAK-
    public static void FireBall(PlayerWorld w, Coord coord, int direction, int damageHp, MagicSpellFlags spellType, string spellName)
    {
        var dg = w.Dg;
        var totalHits = 0;
        var totalKills = 0;
        const int maxDistance = 2;

        GetAreaAffectFlags(w, spellType, out var weaponType, out var harmType, out var destroy);

        var distance = 0;
        var finished = false;

        while (!finished)
        {
            var oldCoord = coord;
            PlayerMovement.MovePosition(dg, direction, ref coord);

            distance++;

            ScreenRendering.LiteSpot(dg, oldCoord);

            if (distance > TreasureConfig.ObjectBoltsMaxRange)
            {
                finished = true;
                continue;
            }

            var hitTile = dg.Floor[coord.Y, coord.X];

            if (hitTile.FeatureId >= TileConstants.MinClosedSpace || hitTile.CreatureId > 1)
            {
                finished = true;

                if (hitTile.FeatureId >= TileConstants.MinClosedSpace)
                {
                    coord = oldCoord;
                }

                // The ball hits and explodes.

                // The explosion.
                for (var row = coord.Y - maxDistance; row <= coord.Y + maxDistance; row++)
                {
                    for (var col = coord.X - maxDistance; col <= coord.X + maxDistance; col++)
                    {
                        var spot = new Coord(row, col);

                        if (CoordHelpers.InBounds(dg, spot) && CoordHelpers.DistanceBetween(coord, spot) <= maxDistance && Los.Check(dg, coord, spot))
                        {
                            ref var tile = ref dg.Floor[spot.Y, spot.X];

                            if (tile.TreasureId != 0 && destroy(w.Treasure[tile.TreasureId]))
                            {
                                w.Objects.DeleteObject(spot);
                            }

                            if (tile.FeatureId <= TileConstants.MaxOpenSpace)
                            {
                                if (tile.CreatureId > 1)
                                {
                                    int monsterId = tile.CreatureId;
                                    ref var monster = ref w.Monsters.Monsters[monsterId];
                                    var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                                    // lite up creature if visible, temp set permanent_light so that monsterUpdateVisibility works
                                    var savedLitStatus = tile.PermanentLight;
                                    tile.PermanentLight = true;
                                    MonsterAi.UpdateVisibility(w, monsterId);

                                    totalHits++;
                                    var damage = damageHp;

                                    if ((harmType & creature.Defenses) != 0)
                                    {
                                        damage *= 2;
                                        if (monster.Lit)
                                        {
                                            w.CreatureRecall[monster.CreatureId].Defenses |= (ushort)harmType;
                                        }
                                    }
                                    else if ((weaponType & creature.Spells) != 0)
                                    {
                                        damage /= 4;
                                        if (monster.Lit)
                                        {
                                            w.CreatureRecall[monster.CreatureId].Spells |= weaponType;
                                        }
                                    }

                                    damage /= CoordHelpers.DistanceBetween(spot, coord) + 1;

                                    if (MonsterLife.TakeHit(w, monsterId, damage) >= 0)
                                    {
                                        totalKills++;
                                    }
                                    tile.PermanentLight = savedLitStatus;
                                }

                                // (else: panelPutTile('*') -- display)
                            }
                        }
                    }
                }

                // (show ball of whatever: putQIO() and the dungeonLiteSpot()
                // redraw of the blast area -- display)

                if (totalHits == 1)
                {
                    PlayerCore.PrintMessage(w, "The " + spellName + " envelops a creature!");
                }
                else if (totalHits > 1)
                {
                    PlayerCore.PrintMessage(w, "The " + spellName + " envelops several creatures!");
                }

                if (totalKills == 1)
                {
                    PlayerCore.PrintMessage(w, "There is a scream of agony!");
                }
                else if (totalKills > 1)
                {
                    PlayerCore.PrintMessage(w, "There are several screams of agony!");
                }

                // (the original tests `total_kills >= 0`: always)
                PlayerCore.DisplayCharacterExperience(w);
                // End ball hitting.
            }

            // (else: panelPutTile('*') to show the bolt -- display)
        }
    }

    // src/spells.cpp:985 spellBreath() -- breath weapon works like a
    // spellFireBall(), but affects the player. Note the area affect. -RAK-
    public static void Breath(PlayerWorld w, Coord coord, int monsterId, int damageHp, MagicSpellFlags spellType, string spellName)
    {
        var dg = w.Dg;
        const int maxDistance = 2;

        GetAreaAffectFlags(w, spellType, out var weaponType, out var harmType, out var destroy);

        for (var y = coord.Y - 2; y <= coord.Y + 2; y++)
        {
            for (var x = coord.X - 2; x <= coord.X + 2; x++)
            {
                var location = new Coord(y, x);

                if (!CoordHelpers.InBounds(dg, location) || CoordHelpers.DistanceBetween(coord, location) > maxDistance || !Los.Check(dg, coord, location))
                {
                    continue;
                }

                ref var tile = ref dg.Floor[location.Y, location.X];

                if (tile.TreasureId != 0 && destroy(w.Treasure[tile.TreasureId]))
                {
                    w.Objects.DeleteObject(location);
                }

                if (tile.FeatureId > TileConstants.MaxOpenSpace)
                {
                    continue;
                }

                // (panelPutTile('*') unless PY_BLIND -- display)

                if (tile.CreatureId > 1)
                {
                    int hitId = tile.CreatureId;
                    ref var monster = ref w.Monsters.Monsters[hitId];
                    var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                    var damage = damageHp;

                    if ((harmType & creature.Defenses) != 0)
                    {
                        damage *= 2;
                    }
                    else if ((weaponType & creature.Spells) != 0)
                    {
                        damage /= 4;
                    }

                    damage /= CoordHelpers.DistanceBetween(location, coord) + 1;

                    // can not call monsterTakeHit here, since player does not
                    // get experience for kill
                    monster.Hp = (short)(monster.Hp - damage);
                    monster.SleepCount = 0;

                    if (monster.Hp < 0)
                    {
                        var treasureId = MonsterLife.Death(w, monster.Pos, creature.Movement);

                        if (monster.Lit)
                        {
                            var memory = w.CreatureRecall[monster.CreatureId];
                            var tmp = (memory.Movement & MonsterConfig.Move.CmTreasure) >> MonsterConfig.Move.CmTrShift;
                            if (tmp > ((treasureId & MonsterConfig.Move.CmTreasure) >> MonsterConfig.Move.CmTrShift))
                            {
                                treasureId = (treasureId & ~MonsterConfig.Move.CmTreasure) | (tmp << MonsterConfig.Move.CmTrShift);
                            }
                            memory.Movement = treasureId | (memory.Movement & ~MonsterConfig.Move.CmTreasure);
                        }

                        // It ate an already processed monster. Handle normally.
                        if (monsterId < hitId)
                        {
                            w.Monsters.DeleteMonster(hitId);
                        }
                        else
                        {
                            // If it eats this monster, an already processed monster
                            // will take its place, causing all kinds of havoc.
                            // Delay the kill a bit.
                            w.Monsters.RemoveMonsterFromLevel(hitId);
                        }
                    }
                }
                else if (tile.CreatureId == 1)
                {
                    var damage = damageHp / (CoordHelpers.DistanceBetween(location, coord) + 1);

                    // let's do at least one point of damage
                    // prevents randomNumber(0) problem with damagePoisonedGas, also
                    if (damage == 0)
                    {
                        damage = 1;
                    }

                    switch (spellType)
                    {
                        case MagicSpellFlags.Lightning:
                            InventoryDamage.DamageLightningBolt(w, damage, spellName);
                            break;
                        case MagicSpellFlags.PoisonGas:
                            InventoryDamage.DamagePoisonedGas(w, damage, spellName);
                            break;
                        case MagicSpellFlags.Acid:
                            InventoryDamage.DamageAcid(w, damage, spellName);
                            break;
                        case MagicSpellFlags.Frost:
                            InventoryDamage.DamageCold(w, damage, spellName);
                            break;
                        case MagicSpellFlags.Fire:
                            InventoryDamage.DamageFire(w, damage, spellName);
                            break;
                    }
                }
            }
        }

        // (show the ball of gas: putQIO() and the dungeonLiteSpot() redraw
        // -- display)
    }

    // The shared shape of spellChangeMonsterHitPoints() and
    // spellDrainLifeFromMonster(): follow a bolt path to the first monster.
    private static int FindMonsterInDirection(PlayerWorld w, ref Coord coord, int direction)
    {
        var distance = 0;

        while (true)
        {
            PlayerMovement.MovePosition(w.Dg, direction, ref coord);
            distance++;

            var tile = w.Dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                return 0;
            }

            if (tile.CreatureId > 1)
            {
                return tile.CreatureId;
            }
        }
    }

    // src/spells.cpp:1149 spellChangeMonsterHitPoints()
    public static bool ChangeMonsterHitPoints(PlayerWorld w, Coord coord, int direction, int damageHp)
    {
        var monsterId = FindMonsterInDirection(w, ref coord, direction);
        if (monsterId == 0)
        {
            return false;
        }

        var monster = w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

        if (MonsterLife.TakeHit(w, monsterId, damageHp) >= 0)
        {
            MonsterLife.PrintActionText(w, name, "dies in a fit of agony.");
            PlayerCore.DisplayCharacterExperience(w);
        }
        else if (damageHp > 0)
        {
            MonsterLife.PrintActionText(w, name, "screams in agony.");
        }

        return true;
    }

    // src/spells.cpp:1188 spellDrainLifeFromMonster() -- drains life; note
    // it must be living. -RAK-
    public static bool DrainLifeFromMonster(PlayerWorld w, Coord coord, int direction)
    {
        var monsterId = FindMonsterInDirection(w, ref coord, direction);
        if (monsterId == 0)
        {
            return false;
        }

        var monster = w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        if ((creature.Defenses & MonsterConfig.Defense.CdUndead) != 0)
        {
            w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdUndead;
            return false;
        }

        var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

        if (MonsterLife.TakeHit(w, monsterId, 75) >= 0)
        {
            MonsterLife.PrintActionText(w, name, "dies in a fit of agony.");
            PlayerCore.DisplayCharacterExperience(w);
        }
        else
        {
            MonsterLife.PrintActionText(w, name, "screams in agony.");
        }

        return true;
    }

    // src/spells.cpp:2032 spellDispelCreature() -- attempts to destroy a
    // type of creature. Success depends on the creatures level VS. the
    // player's level -RAK-
    public static bool DispelCreature(PlayerWorld w, int creatureDefense, int damage)
    {
        var dispelled = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            var monster = w.Monsters.Monsters[id];

            if (monster.DistanceFromPlayer <= MonsterConfig.MonMaxSight && (creatureDefense & DataCreaturesData.CreaturesList[monster.CreatureId].Defenses) != 0 &&
                Los.Check(w.Dg, w.Py.Pos, monster.Pos))
            {
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                w.CreatureRecall[monster.CreatureId].Defenses |= (ushort)creatureDefense;

                dispelled = true;

                var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                var hit = MonsterLife.TakeHit(w, id, RandomNumber.Next(damage));

                // Should get these messages even if the monster is not visible.
                MonsterLife.PrintActionText(w, name, hit >= 0 ? "dissolves!" : "shudders.");

                if (hit >= 0)
                {
                    PlayerCore.DisplayCharacterExperience(w);
                }
            }
        }

        return dispelled;
    }

    // src/spells.cpp:1940 earthquakeHitsMonster()
    private static void EarthquakeHitsMonster(PlayerWorld w, int monsterId)
    {
        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        if ((creature.Movement & MonsterConfig.Move.CmPhase) == 0)
        {
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

            if (MonsterLife.TakeHit(w, monsterId, damage) >= 0)
            {
                MonsterLife.PrintActionText(w, name, "is embedded in the rock.");
                PlayerCore.DisplayCharacterExperience(w);
            }
        }
        else if (creature.Sprite == (byte)'E' || creature.Sprite == (byte)'X')
        {
            // must be an earth elemental or an earth spirit, or a
            // Xorn increase its hit points
            monster.Hp = (short)(monster.Hp + DiceRoller.Roll(new Model.Generated.Dice(4, 8)));
        }
    }

    // src/spells.cpp:1971 spellEarthquake() -- this is a fun one. In a given
    // block, pick some walls and turn them into open spots. Pick some open
    // spots and turn them into walls. An "Earthquake" effect. -RAK-
    public static void Earthquake(PlayerWorld w)
    {
        var dg = w.Dg;
        var py = w.Py;

        for (var y = py.Pos.Y - 8; y <= py.Pos.Y + 8; y++)
        {
            for (var x = py.Pos.X - 8; x <= py.Pos.X + 8; x++)
            {
                var coord = new Coord(y, x);

                if ((y != py.Pos.Y || x != py.Pos.X) && CoordHelpers.InBounds(dg, coord) && RandomNumber.Next(8) == 1)
                {
                    ref var tile = ref dg.Floor[y, x];

                    if (tile.TreasureId != 0)
                    {
                        w.Objects.DeleteObject(coord);
                    }

                    if (tile.CreatureId > 1)
                    {
                        EarthquakeHitsMonster(w, tile.CreatureId);
                    }

                    if (tile.FeatureId >= TileConstants.MinCaveWall && tile.FeatureId != TileConstants.TileBoundaryWall)
                    {
                        tile.FeatureId = TileConstants.TileCorrFloor;
                        tile.PermanentLight = false;
                        tile.FieldMark = false;
                    }
                    else if (tile.FeatureId <= TileConstants.MaxCaveFloor)
                    {
                        var tmp = RandomNumber.Next(10);

                        if (tmp < 6)
                        {
                            tile.FeatureId = TileConstants.TileQuartzWall;
                        }
                        else if (tmp < 9)
                        {
                            tile.FeatureId = TileConstants.TileMagmaWall;
                        }
                        else
                        {
                            tile.FeatureId = TileConstants.TileGraniteWall;
                        }

                        tile.FieldMark = false;
                    }
                    ScreenRendering.LiteSpot(dg, coord);
                }
            }
        }
    }
}
