using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// M9: the player's half of melee combat, from src/player.cpp
// (playerAttackMonster() and its to-hit/blows helpers,
// playerWeaponCriticalBlow(), playerSavingThrow(),
// playerGainKillExperience(), playerTestAttackHits(),
// playerDiedFromString(), playerAdjustBonusesForItem()), src/player_bash.cpp
// (playerBashAttack()) and src/player_magic.cpp (itemMagicAbilityDamage(),
// the slay/brand multipliers).
//
// PlayerMovement.AttackPosition (bump attacks, M8's deferral) and
// PlayerDoors' bashing a monster call in here.
public static class PlayerCombat
{
    // src/player.cpp:252 playerDiedFromString() -- the death label a
    // monster's attacks pass to playerTakesHit().
    public static string DiedFromString(string monsterName, uint move)
    {
        if ((move & MonsterConfig.Move.CmWin) != 0)
        {
            return $"The {monsterName}";
        }

        if (IsVowel(monsterName[0]))
        {
            return $"an {monsterName}";
        }

        return $"a {monsterName}";
    }

    // src/helpers.cpp:101 isVowel()
    private static bool IsVowel(char ch) => "aeiouAEIOU".IndexOf(ch) >= 0;

    // src/player.cpp:262 playerTestAttackHits() -- does a monster's attack
    // of this type hit the player?
    public static bool TestAttackHits(PlayerWorld w, int attackId, byte level)
    {
        var py = w.Py;
        var ac = py.Misc.Ac + py.Misc.MagicalAc;

        bool Hit(int baseToHit, int armorClass) =>
            PlayerMovement.TestBeingHit(w, baseToHit, level, 0, armorClass, PlayerConfig.ClassMiscHit);

        switch (attackId)
        {
            case 1: // Normal attack
                return Hit(60, ac);
            case 2: // Lose Strength
                return Hit(-3, ac);
            case 3: // Confusion attack
            case 4: // Fear attack
            case 5: // Fire attack
                return Hit(10, ac);
            case 6: // Acid attack
                return Hit(0, ac);
            case 7: // Cold attack
            case 8: // Lightning attack
                return Hit(10, ac);
            case 9: // Corrosion attack
                return Hit(0, ac);
            case 10: // Blindness attack
            case 11: // Paralysis attack
                return Hit(2, ac);
            case 12: // Steal Money
                return Hit(5, py.Misc.Level) && py.Misc.Au > 0;
            case 13: // Steal Object
                return Hit(2, py.Misc.Level) && py.Pack.UniqueItems > 0;
            case 14: // Poison
                return Hit(5, ac);
            case 15: // Lose dexterity
            case 16: // Lose constitution
                return Hit(0, ac);
            case 17: // Lose intelligence
            case 18: // Lose wisdom
                return Hit(2, ac);
            case 19: // Lose experience
                return Hit(5, ac);
            case 20: // Aggravate monsters
                return true;
            case 21: // Disenchant
                return Hit(20, ac);
            case 22: // Eat food
            case 23: // Eat light
                return Hit(5, ac);
            case 24: // Eat charges
                // check to make sure an object exists
                return Hit(15, ac) && py.Pack.UniqueItems > 0;
            case 99:
                return true;
            default:
                return false;
        }
    }

    // src/player.cpp:1041 playerWeaponCriticalBlow() -- critical hits,
    // Nasty way to die. -RAK-
    public static int WeaponCriticalBlow(PlayerWorld w, int weaponWeight, int plusToHit, int damage, int attackTypeId)
    {
        var py = w.Py;
        var critical = damage;

        // Weight of weapon, plusses to hit, and character level all
        // contribute to the chance of a critical
        if (RandomNumber.Next(5000) <= weaponWeight + 5 * plusToHit + DataPlayerData.ClassLevelAdj[py.Misc.ClassId][attackTypeId] * py.Misc.Level)
        {
            weaponWeight += RandomNumber.Next(650);

            if (weaponWeight < 400)
            {
                critical = 2 * damage + 5;
                PlayerCore.PrintMessage(w, "It was a good hit! (x2 damage)");
            }
            else if (weaponWeight < 700)
            {
                critical = 3 * damage + 10;
                PlayerCore.PrintMessage(w, "It was an excellent hit! (x3 damage)");
            }
            else if (weaponWeight < 900)
            {
                critical = 4 * damage + 15;
                PlayerCore.PrintMessage(w, "It was a superb hit! (x4 damage)");
            }
            else
            {
                critical = 5 * damage + 20;
                PlayerCore.PrintMessage(w, "It was a *GREAT* hit! (x5 damage)");
            }
        }

        return critical;
    }

    // src/player.cpp:1068 playerSavingThrow() -- saving throws for player
    // character. -RAK-
    public static bool SavingThrow(Player py)
    {
        var classLevelAdjustment = DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Save] * py.Misc.Level / 3;

        var saving = py.Misc.SavingThrow + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Wis) + classLevelAdjustment;

        return RandomNumber.Next(100) <= saving;
    }

    // src/player.cpp:1076 playerGainKillExperience()
    public static void GainKillExperience(Player py, in Creature creature)
    {
        var exp = creature.KillExpValue * creature.Level;

        var quotient = exp / py.Misc.Level;
        var remainder = exp % py.Misc.Level;

        remainder *= 0x10000;
        remainder /= py.Misc.Level;
        remainder += py.Misc.ExpFraction;

        if (remainder >= 0x10000)
        {
            quotient++;
            py.Misc.ExpFraction = (ushort)(remainder - 0x10000);
        }
        else
        {
            py.Misc.ExpFraction = (ushort)remainder;
        }

        py.Misc.Exp += quotient;
    }

    // src/player.cpp:387 playerAdjustBonusesForItem() -- the stat/search/
    // stealth/speed/infravision bonuses an item gives while worn (factor 1)
    // or taken off (factor -1). Reached here when the last missile is used
    // up; wielding and taking off items are M11.
    public static void AdjustBonusesForItem(PlayerWorld w, in Inventory item, int factor)
    {
        var py = w.Py;
        var amount = item.MiscUse * factor;

        if ((item.Flags & TreasureConfig.Flags.TrStats) != 0)
        {
            for (var i = 0; i < 6; i++)
            {
                if (((1u << i) & item.Flags) != 0)
                {
                    PlayerStatLogic.StatBoost(py, i, amount);
                }
            }
        }

        if ((item.Flags & TreasureConfig.Flags.TrSearch) != 0)
        {
            py.Misc.ChanceInSearch = (short)(py.Misc.ChanceInSearch + amount);
            py.Misc.Fos = (short)(py.Misc.Fos - amount);
        }

        if ((item.Flags & TreasureConfig.Flags.TrStealth) != 0)
        {
            py.Misc.StealthFactor = (short)(py.Misc.StealthFactor + amount);
        }

        if ((item.Flags & TreasureConfig.Flags.TrSpeed) != 0)
        {
            PlayerCore.ChangeSpeed(w, -amount);
        }

        if ((item.Flags & TreasureConfig.Flags.TrBlind) != 0 && factor > 0)
        {
            py.Flags.Blind += 1000;
        }

        if ((item.Flags & TreasureConfig.Flags.TrTimid) != 0 && factor > 0)
        {
            py.Flags.Afraid += 50;
        }

        if ((item.Flags & TreasureConfig.Flags.TrInfra) != 0)
        {
            py.Flags.SeeInfra = (short)(py.Flags.SeeInfra + amount);
        }
    }

    // src/player_magic.cpp:66 itemMagicAbilityDamage() -- special damage
    // due to magical abilities of object -RAK- (`creatureId` is a
    // creatures_list[] index; the original calls it monster_id.)
    public static int ItemMagicAbilityDamage(PlayerWorld w, in Inventory item, int totalDamage, int creatureId)
    {
        var isEgoWeapon = (item.Flags & TreasureConfig.Flags.TrEgoWeapon) != 0;
        var isProjectile = item.CategoryId >= TreasureCategory.TvSlingAmmo && item.CategoryId <= TreasureCategory.TvArrow;
        var isHaftedSword = item.CategoryId >= TreasureCategory.TvHafted && item.CategoryId <= TreasureCategory.TvSword;
        var isFlask = item.CategoryId == TreasureCategory.TvFlask;

        if (isEgoWeapon && (isProjectile || isHaftedSword || isFlask))
        {
            var creature = DataCreaturesData.CreaturesList[creatureId];
            var memory = w.CreatureRecall[creatureId];

            // Slay Dragon
            if ((creature.Defenses & MonsterConfig.Defense.CdDragon) != 0 && (item.Flags & TreasureConfig.Flags.TrSlayDragon) != 0)
            {
                memory.Defenses |= MonsterConfig.Defense.CdDragon;
                return totalDamage * 4;
            }

            // Slay Undead
            if ((creature.Defenses & MonsterConfig.Defense.CdUndead) != 0 && (item.Flags & TreasureConfig.Flags.TrSlayUndead) != 0)
            {
                memory.Defenses |= MonsterConfig.Defense.CdUndead;
                return totalDamage * 3;
            }

            // Slay Animal
            if ((creature.Defenses & MonsterConfig.Defense.CdAnimal) != 0 && (item.Flags & TreasureConfig.Flags.TrSlayAnimal) != 0)
            {
                memory.Defenses |= MonsterConfig.Defense.CdAnimal;
                return totalDamage * 2;
            }

            // Slay Evil
            if ((creature.Defenses & MonsterConfig.Defense.CdEvil) != 0 && (item.Flags & TreasureConfig.Flags.TrSlayEvil) != 0)
            {
                memory.Defenses |= MonsterConfig.Defense.CdEvil;
                return totalDamage * 2;
            }

            // Frost
            if ((creature.Defenses & MonsterConfig.Defense.CdFrost) != 0 && (item.Flags & TreasureConfig.Flags.TrFrostBrand) != 0)
            {
                memory.Defenses |= MonsterConfig.Defense.CdFrost;
                return totalDamage * 3 / 2;
            }

            // Fire
            if ((creature.Defenses & MonsterConfig.Defense.CdFire) != 0 && (item.Flags & TreasureConfig.Flags.TrFlameTongue) != 0)
            {
                memory.Defenses |= MonsterConfig.Defense.CdFire;
                return totalDamage * 3 / 2;
            }
        }

        return totalDamage;
    }

    // src/player.cpp:1096 playerCalculateToHitBlows()
    private static void CalculateToHitBlows(Player py, int weaponId, int weaponWeight, out int blows, out int totalToHit)
    {
        if (weaponId != TreasureCategory.TvNothing)
        {
            // Proper weapon
            blows = PlayerStatLogic.AttackBlows(py, weaponWeight, out totalToHit);
        }
        else
        {
            // Bare hands?
            blows = 2;
            totalToHit = -3;
        }

        // Fix for arrows
        if (weaponId >= TreasureCategory.TvSlingAmmo && weaponId <= TreasureCategory.TvSpike)
        {
            blows = 1;
        }

        totalToHit += py.Misc.PlussesToHit;
    }

    // src/player.cpp:1114 playerCalculateBaseToHit()
    private static int CalculateBaseToHit(Player py, bool creatureLit, int totToHit)
    {
        if (creatureLit)
        {
            return py.Misc.Bth;
        }

        // creature not lit, make it more difficult to hit
        int bth = py.Misc.Bth / 2;
        bth -= totToHit * (PlayerConfig.BthPerPlusToHitAdjust - 1);
        bth -= py.Misc.Level * DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Bth] / 2;

        return bth;
    }

    // src/player.cpp:1130 playerAttackMonster() -- player attacks a (poor,
    // defenseless) creature -RAK-
    public static void AttackMonster(PlayerWorld w, Coord coord)
    {
        var py = w.Py;
        int creatureId = w.Dg.Floor[coord.Y, coord.X].CreatureId;

        ref var monster = ref w.Monsters.Monsters[creatureId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];
        ref var item = ref py.Inventory[PlayerConfig.Equipment.Wield];

        monster.SleepCount = 0;

        // Does the player know what they're fighting?
        var name = monster.Lit ? $"the {creature.Name}" : "it";

        CalculateToHitBlows(py, item.CategoryId, item.Weight, out var blows, out var totalToHit);

        var baseToHit = CalculateBaseToHit(py, monster.Lit, totalToHit);

        // Loop for number of blows, trying to hit the critter.
        // Note: blows will always be greater than 0 at the start of the loop -MRC-
        for (var i = blows; i > 0; i--)
        {
            if (!PlayerMovement.TestBeingHit(w, baseToHit, py.Misc.Level, totalToHit, creature.Ac, PlayerConfig.ClassLevelAdj.Bth))
            {
                PlayerCore.PrintMessage(w, $"You miss {name}.");
                continue;
            }

            PlayerCore.PrintMessage(w, $"You hit {name}.");

            int damage;
            if (item.CategoryId != TreasureCategory.TvNothing)
            {
                damage = DiceRoller.Roll(item.Damage);
                damage = ItemMagicAbilityDamage(w, item, damage, monster.CreatureId);
                damage = WeaponCriticalBlow(w, item.Weight, totalToHit, damage, PlayerConfig.ClassLevelAdj.Bth);
            }
            else
            {
                // Bare hands!?
                damage = DiceRoller.Roll(new Model.Generated.Dice(1, 1));
                damage = WeaponCriticalBlow(w, 1, 0, damage, PlayerConfig.ClassLevelAdj.Bth);
            }

            damage += py.Misc.PlussesToDamage;
            if (damage < 0)
            {
                damage = 0;
            }

            if (py.Flags.ConfuseMonster)
            {
                py.Flags.ConfuseMonster = false;

                PlayerCore.PrintMessage(w, "Your hands stop glowing.");

                string msg;
                if ((creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0 || RandomNumber.Next(MonsterConstants.MonMaxLevels) < creature.Level)
                {
                    msg = $"{name} is unaffected.";
                }
                else
                {
                    msg = $"{name} appears confused.";
                    if (monster.ConfusedAmount != 0)
                    {
                        monster.ConfusedAmount += 3;
                    }
                    else
                    {
                        monster.ConfusedAmount = (byte)(2 + RandomNumber.Next(16));
                    }
                }
                PlayerCore.PrintMessage(w, msg);

                if (monster.Lit && RandomNumber.Next(4) == 1)
                {
                    w.CreatureRecall[monster.CreatureId].Defenses |= (ushort)(creature.Defenses & MonsterConfig.Defense.CdNoSleep);
                }
            }

            // See if we done it in.
            if (MonsterLife.TakeHit(w, creatureId, damage) >= 0)
            {
                PlayerCore.PrintMessage(w, $"You have slain {name}.");
                PlayerCore.DisplayCharacterExperience(w);

                return;
            }

            // Use missiles up
            if (item.CategoryId >= TreasureCategory.TvSlingAmmo && item.CategoryId <= TreasureCategory.TvSpike)
            {
                item.ItemsCount--;
                py.Pack.Weight = (short)(py.Pack.Weight - item.Weight);
                py.Flags.Status |= PlayerConfig.Status.PyStrWgt;

                if (item.ItemsCount == 0)
                {
                    py.EquipmentCount--;
                    AdjustBonusesForItem(w, item, -1);
                    item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing);
                    PlayerBonuses.RecalculateBonuses(py);
                }
            }
        }
    }

    // src/player_bash.cpp:82 playerBashAttack() -- make a bash attack on
    // someone. -CJS-
    public static void BashAttack(PlayerWorld w, Coord coord)
    {
        var py = w.Py;
        int monsterId = w.Dg.Floor[coord.Y, coord.X].CreatureId;

        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        monster.SleepCount = 0;

        // Does the player know what they're fighting?
        var name = monster.Lit ? $"the {creature.Name}" : "it";

        int baseToHit = py.Stats.Used[PlayerConfig.Attr.Str];
        baseToHit += py.Inventory[PlayerConfig.Equipment.Arm].Weight / 2;
        baseToHit += py.Misc.Weight / 10;

        if (!monster.Lit)
        {
            baseToHit /= 2;
            baseToHit -= py.Stats.Used[PlayerConfig.Attr.Dex] * (PlayerConfig.BthPerPlusToHitAdjust - 1);
            baseToHit -= py.Misc.Level * DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Bth] / 2;
        }

        if (PlayerMovement.TestBeingHit(w, baseToHit, py.Misc.Level, py.Stats.Used[PlayerConfig.Attr.Dex], creature.Ac, PlayerConfig.ClassLevelAdj.Bth))
        {
            PlayerCore.PrintMessage(w, $"You hit {name}.");

            var shield = py.Inventory[PlayerConfig.Equipment.Arm];
            var damage = DiceRoller.Roll(shield.Damage);
            damage = WeaponCriticalBlow(w, shield.Weight / 4 + py.Stats.Used[PlayerConfig.Attr.Str], 0, damage, PlayerConfig.ClassLevelAdj.Bth);
            damage += py.Misc.Weight / 60;
            damage += 3;

            if (damage < 0)
            {
                damage = 0;
            }

            // See if we done it in.
            if (MonsterLife.TakeHit(w, monsterId, damage) >= 0)
            {
                PlayerCore.PrintMessage(w, $"You have slain {name}.");
                PlayerCore.DisplayCharacterExperience(w);
            }
            else
            {
                name = char.ToUpperInvariant(name[0]) + name[1..]; // Capitalize

                // Can not stun Balrog
                int avgMaxHp;
                if ((creature.Defenses & MonsterConfig.Defense.CdMaxHp) != 0)
                {
                    avgMaxHp = DiceRoller.MaxRoll(creature.HitDie);
                }
                else
                {
                    avgMaxHp = (creature.HitDie.Count * (creature.HitDie.Sides + 1)) >> 1;
                }

                string msg;
                if (100 + RandomNumber.Next(400) + RandomNumber.Next(400) > monster.Hp + avgMaxHp)
                {
                    monster.StunnedAmount = (byte)(monster.StunnedAmount + RandomNumber.Next(3) + 1);
                    if (monster.StunnedAmount > 24)
                    {
                        monster.StunnedAmount = 24;
                    }

                    msg = $"{name} appears stunned!";
                }
                else
                {
                    msg = $"{name} ignores your bash!";
                }
                PlayerCore.PrintMessage(w, msg);
            }
        }
        else
        {
            PlayerCore.PrintMessage(w, $"You miss {name}.");
        }

        if (RandomNumber.Next(150) > py.Stats.Used[PlayerConfig.Attr.Dex])
        {
            PlayerCore.PrintMessage(w, "You are off balance.");
            py.Flags.Paralysis = (short)(1 + RandomNumber.Next(2));
        }
    }
}
