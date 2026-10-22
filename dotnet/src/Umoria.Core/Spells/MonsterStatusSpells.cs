using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M10: src/spells.cpp's monster-status spells -- speed/slow, confuse and
// sleep a monster (a bolt-like ray), polymorph, clone, teleport-other, and
// their all-in-sight forms (speed/sleep all, mass polymorph, mass genocide,
// genocide, turn undead). Deferred from M9, which ported only the damage
// primitives these sit beside; monsterSleep() (the adjacent "sleep I") is
// MonsterLife.Sleep.
//
// The ray spells are deliberately not folded into one helper: they stop on
// different conditions (polymorph keeps going past a monster that resisted,
// clone returns from inside the loop), so each keeps the original's own
// loop.
public static class MonsterStatusSpells
{
    // src/spells.cpp:1232 spellSpeedMonster() -- increase or decrease a
    // creatures speed -RAK-
    // NOTE: cannot slow a winning creature (BALROG)
    public static bool SpeedMonster(PlayerWorld w, Coord coord, int direction, int speed)
    {
        var dg = w.Dg;
        var distance = 0;
        var changed = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            var tile = dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue;
            }

            if (tile.CreatureId > 1)
            {
                finished = true;

                ref var monster = ref w.Monsters.Monsters[tile.CreatureId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                if (speed > 0)
                {
                    monster.Speed = (short)(monster.Speed + speed);
                    monster.SleepCount = 0;

                    changed = true;

                    MonsterLife.PrintActionText(w, name, "starts moving faster.");
                }
                else if (RandomNumber.Next(MonsterConstants.MonMaxLevels) > creature.Level)
                {
                    monster.Speed = (short)(monster.Speed + speed);
                    monster.SleepCount = 0;

                    changed = true;

                    MonsterLife.PrintActionText(w, name, "starts moving slower.");
                }
                else
                {
                    monster.SleepCount = 0;

                    MonsterLife.PrintActionText(w, name, "is unaffected.");
                }
            }
        }

        return changed;
    }

    // src/spells.cpp:1282 spellConfuseMonster() -- confuse a creature -RAK-
    public static bool ConfuseMonster(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var confused = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            var tile = dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue;
            }

            if (tile.CreatureId > 1)
            {
                finished = true;

                ref var monster = ref w.Monsters.Monsters[tile.CreatureId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                if (RandomNumber.Next(MonsterConstants.MonMaxLevels) < creature.Level || (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                {
                    if (monster.Lit && (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                    {
                        w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdNoSleep;
                    }

                    // Monsters which resisted the attack should wake up.
                    // Monsters with innate resistance ignore the attack.
                    if ((creature.Defenses & MonsterConfig.Defense.CdNoSleep) == 0)
                    {
                        monster.SleepCount = 0;
                    }

                    MonsterLife.PrintActionText(w, name, "is unaffected.");
                }
                else
                {
                    if (monster.ConfusedAmount != 0)
                    {
                        monster.ConfusedAmount += 3;
                    }
                    else
                    {
                        monster.ConfusedAmount = (byte)(2 + RandomNumber.Next(16));
                    }
                    monster.SleepCount = 0;

                    confused = true;

                    MonsterLife.PrintActionText(w, name, "appears confused.");
                }
            }
        }

        return confused;
    }

    // src/spells.cpp:1337 spellSleepMonster() -- sleep a creature. -RAK-
    public static bool SleepMonster(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var asleep = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            var tile = dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue;
            }

            if (tile.CreatureId > 1)
            {
                finished = true;

                ref var monster = ref w.Monsters.Monsters[tile.CreatureId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                if (RandomNumber.Next(MonsterConstants.MonMaxLevels) < creature.Level || (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                {
                    if (monster.Lit && (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                    {
                        w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdNoSleep;
                    }

                    MonsterLife.PrintActionText(w, name, "is unaffected.");
                }
                else
                {
                    monster.SleepCount = 500;

                    asleep = true;

                    MonsterLife.PrintActionText(w, name, "falls asleep.");
                }
            }
        }

        return asleep;
    }

    // monsterPlaceNew()'s creature pick for the polymorph spells:
    // randomNumber(monster_levels[MON_MAX_LEVELS] - monster_levels[0]) - 1 + monster_levels[0]
    private static int RandomPolymorphCreatureId(PlayerWorld w)
    {
        var levels = w.Monsters.MonsterLevels;
        return RandomNumber.Next(levels[MonsterConstants.MonMaxLevels] - levels[0]) - 1 + levels[0];
    }

    // src/spells.cpp:1499 spellPolymorphMonster() -- polymorph a monster
    // -RAK-
    // NOTE: cannot polymorph a winning creature (BALROG)
    public static bool PolymorphMonster(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var morphed = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            ref var tile = ref dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue;
            }

            if (tile.CreatureId > 1)
            {
                var monster = w.Monsters.Monsters[tile.CreatureId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                if (RandomNumber.Next(MonsterConstants.MonMaxLevels) > creature.Level)
                {
                    finished = true;

                    w.Monsters.DeleteMonster(tile.CreatureId);

                    // Place_monster() should always return true here.
                    morphed = w.Monsters.PlaceNew(coord, RandomPolymorphCreatureId(w), false, w.Py.Pos, w.Py.Flags.Speed);

                    // don't test tile.field_mark here, only permanent_light/temporary_light
                    // (a no-op in the original too: morphed is already true)
                    if (morphed && ScreenPanel.CoordInsidePanel(dg, coord) && (tile.TemporaryLight || tile.PermanentLight))
                    {
                        morphed = true;
                    }
                }
                else
                {
                    var name = MonsterLife.NameDescription(creature.Name, monster.Lit);
                    MonsterLife.PrintActionText(w, name, "is unaffected.");
                }
            }
        }

        return morphed;
    }

    // src/spells.cpp:1607 spellCloneMonster() -- replicate a creature -RAK-
    public static bool CloneMonster(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            var tile = dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
            }
            else if (tile.CreatureId > 1)
            {
                w.Monsters.Monsters[tile.CreatureId].SleepCount = 0;

                // monptr of 0 is safe here, since can't reach here from creatures
                return MonsterAi.Multiply(w, coord, w.Monsters.Monsters[tile.CreatureId].CreatureId, 0);
            }
        }

        return false;
    }

    // src/spells.cpp:1703 spellTeleportAwayMonsterInDirection() -- teleport
    // all creatures in a given direction away -RAK-
    public static bool TeleportAwayMonsterInDirection(PlayerWorld w, Coord coord, int direction)
    {
        var dg = w.Dg;
        var distance = 0;
        var teleported = false;
        var finished = false;

        while (!finished)
        {
            PlayerMovement.MovePosition(dg, direction, ref coord);
            distance++;

            var tile = dg.Floor[coord.Y, coord.X];

            if (distance > TreasureConfig.ObjectBoltsMaxRange || tile.FeatureId >= TileConstants.MinClosedSpace)
            {
                finished = true;
                continue;
            }

            if (tile.CreatureId > 1)
            {
                // wake it up
                w.Monsters.Monsters[tile.CreatureId].SleepCount = 0;

                SpellEffects.TeleportAwayMonster(w, tile.CreatureId, MonsterConfig.MonMaxSight);

                teleported = true;
            }
        }

        return teleported;
    }

    // src/spells.cpp:1734 spellMassGenocide() -- delete all creatures within
    // max_sight distance -RAK-
    // NOTE : Winning creatures cannot be killed by genocide.
    public static bool MassGenocide(PlayerWorld w)
    {
        var killed = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            var monster = w.Monsters.Monsters[id];
            var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

            if (monster.DistanceFromPlayer <= MonsterConfig.MonMaxSight && (creature.Movement & MonsterConfig.Move.CmWin) == 0)
            {
                killed = true;
                w.Monsters.DeleteMonster(id);
            }
        }

        return killed;
    }

    // src/spells.cpp:1753 spellGenocide() -- delete all creatures of a given
    // type from level. -RAK- This does not keep creatures of type from
    // appearing later.
    // NOTE : Winning creatures can not be killed by genocide.
    public static bool Genocide(PlayerWorld w)
    {
        if (!PlayerCore.GetCommand(w, "Which type of creature do you wish exterminated?", out var creatureChar))
        {
            return false;
        }

        var killed = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            var monster = w.Monsters.Monsters[id];
            var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

            if (creatureChar == creature.Sprite)
            {
                if ((creature.Movement & MonsterConfig.Move.CmWin) == 0)
                {
                    killed = true;
                    w.Monsters.DeleteMonster(id);
                }
                else
                {
                    // genocide is a powerful spell, so we will let the player
                    // know the names of the creatures they did not destroy,
                    // this message makes no sense otherwise
                    PlayerCore.PrintMessage(w, "The " + creature.Name + " is unaffected.");
                }
            }
        }

        return killed;
    }

    // src/spells.cpp:1783 spellSpeedAllMonsters() -- change speed of any
    // creature . -RAK-
    // NOTE: cannot slow a winning creature (BALROG)
    public static bool SpeedAllMonsters(PlayerWorld w, int speed)
    {
        var speedy = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            ref var monster = ref w.Monsters.Monsters[id];
            var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

            var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

            if (monster.DistanceFromPlayer > MonsterConfig.MonMaxSight || !Los.Check(w.Dg, w.Py.Pos, monster.Pos))
            {
                continue; // do nothing
            }

            if (speed > 0)
            {
                monster.Speed = (short)(monster.Speed + speed);
                monster.SleepCount = 0;

                if (monster.Lit)
                {
                    speedy = true;
                    MonsterLife.PrintActionText(w, name, "starts moving faster.");
                }
            }
            else if (RandomNumber.Next(MonsterConstants.MonMaxLevels) > creature.Level)
            {
                monster.Speed = (short)(monster.Speed + speed);
                monster.SleepCount = 0;

                if (monster.Lit)
                {
                    speedy = true;
                    MonsterLife.PrintActionText(w, name, "starts moving slower.");
                }
            }
            else if (monster.Lit)
            {
                monster.SleepCount = 0;
                MonsterLife.PrintActionText(w, name, "is unaffected.");
            }
        }

        return speedy;
    }

    // src/spells.cpp:1822 spellSleepAllMonsters() -- sleep any creature .
    // -RAK-
    public static bool SleepAllMonsters(PlayerWorld w)
    {
        var asleep = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            ref var monster = ref w.Monsters.Monsters[id];
            var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

            var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

            if (monster.DistanceFromPlayer > MonsterConfig.MonMaxSight || !Los.Check(w.Dg, w.Py.Pos, monster.Pos))
            {
                continue; // do nothing
            }

            if (RandomNumber.Next(MonsterConstants.MonMaxLevels) < creature.Level || (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
            {
                if (monster.Lit)
                {
                    if ((creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                    {
                        w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdNoSleep;
                    }
                    MonsterLife.PrintActionText(w, name, "is unaffected.");
                }
            }
            else
            {
                monster.SleepCount = 500;
                if (monster.Lit)
                {
                    asleep = true;
                    MonsterLife.PrintActionText(w, name, "falls asleep.");
                }
            }
        }

        return asleep;
    }

    // src/spells.cpp:1856 spellMassPolymorph() -- polymorph any creature
    // that player can see. -RAK-
    // NOTE: cannot polymorph a winning creature (BALROG)
    public static bool MassPolymorph(PlayerWorld w)
    {
        var morphed = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            var monster = w.Monsters.Monsters[id];

            if (monster.DistanceFromPlayer <= MonsterConfig.MonMaxSight)
            {
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                if ((creature.Movement & MonsterConfig.Move.CmWin) == 0)
                {
                    var coord = monster.Pos;
                    w.Monsters.DeleteMonster(id);

                    // Place_monster() should always return true here.
                    morphed = w.Monsters.PlaceNew(coord, RandomPolymorphCreatureId(w), false, w.Py.Pos, w.Py.Flags.Speed);
                }
            }
        }

        return morphed;
    }

    // src/spells.cpp:2067 spellTurnUndead() -- attempt to turn (confuse)
    // undead creatures. -RAK-
    public static bool TurnUndead(PlayerWorld w)
    {
        var turned = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            ref var monster = ref w.Monsters.Monsters[id];
            var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

            if (monster.DistanceFromPlayer <= MonsterConfig.MonMaxSight && (creature.Defenses & MonsterConfig.Defense.CdUndead) != 0 &&
                Los.Check(w.Dg, w.Py.Pos, monster.Pos))
            {
                var name = MonsterLife.NameDescription(creature.Name, monster.Lit);

                if (w.Py.Misc.Level + 1 > creature.Level || RandomNumber.Next(5) == 1)
                {
                    if (monster.Lit)
                    {
                        w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdUndead;

                        turned = true;

                        MonsterLife.PrintActionText(w, name, "runs frantically!");
                    }

                    monster.ConfusedAmount = (byte)w.Py.Misc.Level;
                }
                else if (monster.Lit)
                {
                    MonsterLife.PrintActionText(w, name, "is unaffected.");
                }
            }
        }

        return turned;
    }
}
