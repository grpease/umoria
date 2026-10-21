using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Creatures;

// M9: a monster taking damage and dying -- src/monster.cpp's
// monsterTakeHit(), monsterDeath() (the treasure a kill drops, also how an
// opened chest spills its contents) and monsterSleep(), plus the two small
// message helpers the damage spells share.
public static class MonsterLife
{
    // src/monster.cpp:1499 monsterNameDescription()
    public static string NameDescription(string realName, bool isLit) => isLit ? "The " + realName : "It";

    // src/monster.cpp:1495 printMonsterActionText()
    public static void PrintActionText(PlayerWorld w, string name, string action) => PlayerCore.PrintMessage(w, name + " " + action);

    // src/monster.cpp:1353 monsterTakeHit() -- decreases monsters hit
    // points and deletes monster if needed. (Picking on my babies.) -RAK-
    // Returns the dead monster's creature id, or -1 if it survived.
    public static int TakeHit(PlayerWorld w, int monsterId, int damage)
    {
        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        monster.SleepCount = 0;
        monster.Hp = (short)(monster.Hp - damage);

        if (monster.Hp >= 0)
        {
            return -1;
        }

        var treasureFlags = Death(w, monster.Pos, creature.Movement);

        var memory = w.CreatureRecall[monster.CreatureId];

        if ((w.Py.Flags.Blind < 1 && monster.Lit) || (creature.Movement & MonsterConfig.Move.CmWin) != 0)
        {
            var tmp = (memory.Movement & MonsterConfig.Move.CmTreasure) >> MonsterConfig.Move.CmTrShift;

            if (tmp > ((treasureFlags & MonsterConfig.Move.CmTreasure) >> MonsterConfig.Move.CmTrShift))
            {
                treasureFlags = (treasureFlags & ~MonsterConfig.Move.CmTreasure) | (tmp << MonsterConfig.Move.CmTrShift);
            }

            memory.Movement = (memory.Movement & ~MonsterConfig.Move.CmTreasure) | treasureFlags;

            if (memory.Kills < short.MaxValue)
            {
                memory.Kills++;
            }
        }

        PlayerCombat.GainKillExperience(w.Py, creature);

        // can't call displayCharacterExperience() here, as that would result in "new level"
        // message appearing before "monster dies" message.
        int mTakeHit = monster.CreatureId;

        // in case this is called from within updateMonsters(), this is a horrible
        // hack, the monsters/updateMonsters() code needs to be rewritten.
        if (w.Monsters.HackMonptr < monsterId)
        {
            w.Monsters.DeleteMonster(monsterId);
        }
        else
        {
            w.Monsters.RemoveMonsterFromLevel(monsterId);
        }

        return mTakeHit;
    }

    // src/monster.cpp:1399 monsterDeathItemDropType()
    private static int DeathItemDropType(uint flags)
    {
        var obj = (flags & MonsterConfig.Move.CmCarryObj) != 0 ? 1 : 0;

        if ((flags & MonsterConfig.Move.CmCarryGold) != 0)
        {
            obj += 2;
        }

        if ((flags & MonsterConfig.Move.CmSmallObj) != 0)
        {
            obj += 4;
        }

        return obj;
    }

    // src/monster.cpp:1419 monsterDeathItemDropCount()
    private static int DeathItemDropCount(uint flags)
    {
        var count = 0;

        if ((flags & MonsterConfig.Move.Cm60Random) != 0 && RandomNumber.Next(100) < 60)
        {
            count++;
        }

        if ((flags & MonsterConfig.Move.Cm90Random) != 0 && RandomNumber.Next(100) < 90)
        {
            count++;
        }

        if ((flags & MonsterConfig.Move.Cm1D2Obj) != 0)
        {
            count += RandomNumber.Next(2);
        }

        if ((flags & MonsterConfig.Move.Cm2D2Obj) != 0)
        {
            count += DiceRoller.Roll(new Model.Generated.Dice(2, 2));
        }

        if ((flags & MonsterConfig.Move.Cm4D2Obj) != 0)
        {
            count += DiceRoller.Roll(new Model.Generated.Dice(4, 2));
        }

        return count;
    }

    // src/monster.cpp:1451 monsterDeath() -- allocates objects upon a
    // creatures death -RAK- Oh well, another creature bites the dust.
    // Reward the victor based on flags set in the main creature record.
    //
    // Returns a mask of bits from the given flags which indicates what the
    // monster is seen to have dropped. This may be added to monster memory.
    public static uint Death(PlayerWorld w, Coord coord, uint flags)
    {
        var itemType = DeathItemDropType(flags);
        var itemCount = DeathItemDropCount(flags);

        uint droppedItemId = 0;

        if (itemCount > 0)
        {
            droppedItemId = (uint)w.Objects.SummonObject(coord, itemCount, itemType);
        }

        // maybe the player died in mid-turn
        if ((flags & MonsterConfig.Move.CmWin) != 0 && !w.Game.CharacterIsDead)
        {
            w.Game.TotalWinner = true;

            // (printCharacterWinner() -- display)

            PlayerCore.PrintMessage(w, "*** CONGRATULATIONS *** You have won the game.");
            PlayerCore.PrintMessage(w, "You cannot save this game, but you may retire when ready.");
        }

        if (droppedItemId == 0)
        {
            return 0;
        }

        uint returnFlags = 0;

        if ((droppedItemId & 255) != 0)
        {
            returnFlags |= MonsterConfig.Move.CmCarryObj;

            if ((itemType & 0x04) != 0)
            {
                returnFlags |= MonsterConfig.Move.CmSmallObj;
            }
        }

        if (droppedItemId >= 256)
        {
            returnFlags |= MonsterConfig.Move.CmCarryGold;
        }

        var numberOfItems = (int)(droppedItemId % 256 + droppedItemId / 256);
        numberOfItems <<= MonsterConfig.Move.CmTrShift;

        return returnFlags | (uint)numberOfItems;
    }

    // src/monster.cpp:1507 monsterSleep() -- sleep creatures adjacent to
    // player -RAK- (the "sleep I" spell, scroll and prayer: their casting
    // layers are M10/M11)
    public static bool Sleep(PlayerWorld w, Coord coord)
    {
        var asleep = false;

        for (var y = coord.Y - 1; y <= coord.Y + 1 && y < DungeonConstants.MaxHeight; y++)
        {
            for (var x = coord.X - 1; x <= coord.X + 1 && x < DungeonConstants.MaxWidth; x++)
            {
                int monsterId = w.Dg.Floor[y, x].CreatureId;

                if (monsterId <= 1)
                {
                    continue;
                }

                ref var monster = ref w.Monsters.Monsters[monsterId];
                var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

                var name = NameDescription(creature.Name, monster.Lit);

                if (RandomNumber.Next(MonsterConstants.MonMaxLevels) < creature.Level || (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                {
                    if (monster.Lit && (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
                    {
                        w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdNoSleep;
                    }

                    PrintActionText(w, name, "is unaffected.");
                }
                else
                {
                    monster.SleepCount = 500;
                    asleep = true;

                    PrintActionText(w, name, "falls asleep.");
                }
            }
        }

        return asleep;
    }
}
