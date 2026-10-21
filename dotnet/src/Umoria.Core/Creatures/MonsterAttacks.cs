using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;

namespace Umoria.Core.Creatures;

// M9: a monster's melee attack on the player -- src/monster.cpp's
// monsterAttackPlayer(), executeAttackOnPlayer() (all 24 attack types),
// monsterConfuseOnAttack() and monsterPrintAttackDescription().
//
// The attack descriptions are display text, but every one goes through
// printMessage() (zeroing game.command_count), and the insult (attack 19)
// rolls the RNG to pick its text, so both halves are kept.
public static class MonsterAttacks
{
    // src/monster.cpp:256 monsterPrintAttackDescription()
    private static void PrintAttackDescription(PlayerWorld w, string name, int attackId)
    {
        switch (attackId)
        {
            case 1:
                PlayerCore.PrintMessage(w, name + "hits you.");
                break;
            case 2:
                PlayerCore.PrintMessage(w, name + "bites you.");
                break;
            case 3:
                PlayerCore.PrintMessage(w, name + "claws you.");
                break;
            case 4:
                PlayerCore.PrintMessage(w, name + "stings you.");
                break;
            case 5:
                PlayerCore.PrintMessage(w, name + "touches you.");
                break;
            case 7:
                PlayerCore.PrintMessage(w, name + "gazes at you.");
                break;
            case 8:
                PlayerCore.PrintMessage(w, name + "breathes on you.");
                break;
            case 9:
                PlayerCore.PrintMessage(w, name + "spits on you.");
                break;
            case 10:
                PlayerCore.PrintMessage(w, name + "makes a horrible wail.");
                break;
            case 12:
                PlayerCore.PrintMessage(w, name + "crawls on you.");
                break;
            case 13:
                PlayerCore.PrintMessage(w, name + "releases a cloud of spores.");
                break;
            case 14:
                PlayerCore.PrintMessage(w, name + "begs you for money.");
                break;
            case 15:
                PlayerCore.PrintMessage(w, "You've been slimed!");
                break;
            case 16:
                PlayerCore.PrintMessage(w, name + "crushes you.");
                break;
            case 17:
                PlayerCore.PrintMessage(w, name + "tramples you.");
                break;
            case 18:
                PlayerCore.PrintMessage(w, name + "drools on you.");
                break;
            case 19:
                var insult = RandomNumber.Next(9) switch
                {
                    1 => "insults you!",
                    2 => "insults your mother!",
                    3 => "gives you the finger!",
                    4 => "humiliates you!",
                    5 => "wets on your leg!",
                    6 => "defiles you!",
                    7 => "dances around you!",
                    8 => "makes obscene gestures!",
                    _ => "moons you!!!",
                };
                PlayerCore.PrintMessage(w, name + insult);
                break;
            case 99:
                PlayerCore.PrintMessage(w, name + "is repelled.");
                break;
        }
    }

    // src/monster.cpp:357 monsterConfuseOnAttack()
    private static void ConfuseOnAttack(PlayerWorld w, in Creature creature, ref Monster monster, int attackType, string monsterName, bool visible)
    {
        var py = w.Py;

        if (py.Flags.ConfuseMonster && attackType != 99)
        {
            PlayerCore.PrintMessage(w, "Your hands stop glowing.");
            py.Flags.ConfuseMonster = false;

            string msg;

            if (RandomNumber.Next(MonsterConstants.MonMaxLevels) < creature.Level || (creature.Defenses & MonsterConfig.Defense.CdNoSleep) != 0)
            {
                msg = monsterName + "is unaffected.";
            }
            else
            {
                msg = monsterName + "appears confused.";
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

            if (visible && !w.Game.CharacterIsDead && RandomNumber.Next(4) == 1)
            {
                w.CreatureRecall[monster.CreatureId].Defenses |= (ushort)(creature.Defenses & MonsterConfig.Defense.CdNoSleep);
            }
        }
    }

    // src/monster.cpp:384 monsterAttackPlayer() -- make an attack on the
    // player (chuckle.) -RAK-
    public static void AttackPlayer(PlayerWorld w, int monsterId)
    {
        // don't beat a dead body!
        if (w.Game.CharacterIsDead)
        {
            return;
        }

        var py = w.Py;
        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];
        var memory = w.CreatureRecall[monster.CreatureId];

        var name = monster.Lit ? $"The {creature.Name} " : "It ";

        var deathDescription = PlayerCombat.DiedFromString(creature.Name, creature.Movement);

        var attackCounter = 0;
        foreach (var damageTypeId in creature.Damage)
        {
            if (damageTypeId == 0 || w.Game.CharacterIsDead)
            {
                break;
            }

            var attackType = DataCreaturesData.MonsterAttacks[damageTypeId].TypeId;
            var attackDesc = DataCreaturesData.MonsterAttacks[damageTypeId].DescriptionId;
            var dice = DataCreaturesData.MonsterAttacks[damageTypeId].Dice;

            if (py.Flags.ProtectEvil > 0 && (creature.Defenses & MonsterConfig.Defense.CdEvil) != 0 && py.Misc.Level + 1 > creature.Level)
            {
                if (monster.Lit)
                {
                    memory.Defenses |= MonsterConfig.Defense.CdEvil;
                }
                attackType = 99;
                attackDesc = 99;
            }

            if (PlayerCombat.TestAttackHits(w, attackType, creature.Level))
            {
                PlayerCore.Disturb(w, 1, 0);

                PrintAttackDescription(w, name, attackDesc);

                // always fail to notice attack if creature invisible, set notice
                // and visible here since creature may be visible when attacking
                // and then teleport afterwards (becoming effectively invisible)
                var notice = true;
                var visible = true;
                if (!monster.Lit)
                {
                    visible = false;
                    notice = false;
                }

                var damage = DiceRoller.Roll(dice);
                var hp = monster.Hp;
                notice = ExecuteAttackOnPlayer(w, creature.Level, ref hp, monsterId, attackType, damage, deathDescription, notice);
                monster.Hp = hp;

                // Moved here from monsterMove, so that monster only confused if it
                // actually hits. A monster that has been repelled has not hit
                // the player, so it should not be confused.
                ConfuseOnAttack(w, creature, ref monster, attackDesc, name, visible);

                // increase number of attacks if notice true, or if visible and
                // had previously noticed the attack (in which case all this does
                // is help player learn damage), note that in the second case do
                // not increase attacks if creature repelled (no damage done)
                if ((notice || (visible && memory.Attacks[attackCounter] != 0 && attackType != 99)) && memory.Attacks[attackCounter] < byte.MaxValue)
                {
                    memory.Attacks[attackCounter]++;
                }

                if (w.Game.CharacterIsDead && memory.Deaths < short.MaxValue)
                {
                    memory.Deaths++;
                }
            }
            else
            {
                if ((attackDesc >= 1 && attackDesc <= 3) || attackDesc == 6)
                {
                    PlayerCore.Disturb(w, 1, 0);
                    PlayerCore.PrintMessage(w, name + "misses you.");
                }
            }

            if (attackCounter < MonsterConstants.MonMaxAttacks - 1)
            {
                attackCounter++;
            }
            else
            {
                break;
            }
        }
    }

    // src/monster.cpp:1541 executeAttackOnPlayer()
    private static bool ExecuteAttackOnPlayer(PlayerWorld w, byte creatureLevel, ref short monsterHp, int monsterId, int attackType, int damage, string deathDescription, bool noticed)
    {
        var py = w.Py;
        var f = py.Flags;

        switch (attackType)
        {
            case 1: // Normal attack
                // round half-way case down
                damage -= (py.Misc.Ac + py.Misc.MagicalAc) * damage / 200;
                PlayerCore.TakesHit(w, damage, deathDescription);
                break;
            case 2: // Lose Strength
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (f.SustainStr)
                {
                    PlayerCore.PrintMessage(w, "You feel weaker for a moment, but it passes.");
                }
                else if (RandomNumber.Next(2) == 1)
                {
                    PlayerCore.PrintMessage(w, "You feel weaker.");
                    PlayerStatLogic.StatRandomDecrease(py, PlayerConfig.Attr.Str);
                }
                else
                {
                    noticed = false;
                }
                break;
            case 3: // Confusion attack
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (RandomNumber.Next(2) == 1)
                {
                    if (f.Confused < 1)
                    {
                        PlayerCore.PrintMessage(w, "You feel confused.");
                        f.Confused = (short)(f.Confused + RandomNumber.Next(creatureLevel));
                    }
                    else
                    {
                        noticed = false;
                    }
                    f.Confused += 3;
                }
                else
                {
                    noticed = false;
                }
                break;
            case 4: // Fear attack
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (PlayerCombat.SavingThrow(py))
                {
                    PlayerCore.PrintMessage(w, "You resist the effects!");
                }
                else if (f.Afraid < 1)
                {
                    PlayerCore.PrintMessage(w, "You are suddenly afraid!");
                    f.Afraid = (short)(f.Afraid + 3 + RandomNumber.Next(creatureLevel));
                }
                else
                {
                    f.Afraid += 3;
                    noticed = false;
                }
                break;
            case 5: // Fire attack
                PlayerCore.PrintMessage(w, "You are enveloped in flames!");
                InventoryDamage.DamageFire(w, damage, deathDescription);
                break;
            case 6: // Acid attack
                PlayerCore.PrintMessage(w, "You are covered in acid!");
                InventoryDamage.DamageAcid(w, damage, deathDescription);
                break;
            case 7: // Cold attack
                PlayerCore.PrintMessage(w, "You are covered with frost!");
                InventoryDamage.DamageCold(w, damage, deathDescription);
                break;
            case 8: // Lightning attack
                PlayerCore.PrintMessage(w, "Lightning strikes you!");
                InventoryDamage.DamageLightningBolt(w, damage, deathDescription);
                break;
            case 9: // Corrosion attack
                PlayerCore.PrintMessage(w, "A stinging red gas swirls about you.");
                InventoryDamage.DamageCorrodingGas(w, deathDescription);
                PlayerCore.TakesHit(w, damage, deathDescription);
                break;
            case 10: // Blindness attack
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (f.Blind < 1)
                {
                    f.Blind = (short)(f.Blind + 10 + RandomNumber.Next(creatureLevel));
                    PlayerCore.PrintMessage(w, "Your eyes begin to sting.");
                }
                else
                {
                    f.Blind += 5;
                    noticed = false;
                }
                break;
            case 11: // Paralysis attack
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (PlayerCombat.SavingThrow(py))
                {
                    PlayerCore.PrintMessage(w, "You resist the effects!");
                }
                else if (f.Paralysis < 1)
                {
                    if (f.FreeAction)
                    {
                        PlayerCore.PrintMessage(w, "You are unaffected.");
                    }
                    else
                    {
                        f.Paralysis = (short)(RandomNumber.Next(creatureLevel) + 3);
                        PlayerCore.PrintMessage(w, "You are paralyzed.");
                    }
                }
                else
                {
                    noticed = false;
                }
                break;
            case 12: // Steal Money
                if (f.Paralysis < 1 && RandomNumber.Next(124) < py.Stats.Used[PlayerConfig.Attr.Dex])
                {
                    PlayerCore.PrintMessage(w, "You quickly protect your money pouch!");
                }
                else
                {
                    var gold = py.Misc.Au / 10 + RandomNumber.Next(25);
                    if (gold > py.Misc.Au)
                    {
                        py.Misc.Au = 0;
                    }
                    else
                    {
                        py.Misc.Au -= gold;
                    }
                    PlayerCore.PrintMessage(w, "Your purse feels lighter.");
                    // (printCharacterGoldValue() -- display)
                }
                if (RandomNumber.Next(2) == 1)
                {
                    PlayerCore.PrintMessage(w, "There is a puff of smoke!");
                    SpellEffects.TeleportAwayMonster(w, monsterId, MonsterConfig.MonMaxSight);
                }
                break;
            case 13: // Steal Object
                if (f.Paralysis < 1 && RandomNumber.Next(124) < py.Stats.Used[PlayerConfig.Attr.Dex])
                {
                    PlayerCore.PrintMessage(w, "You grab hold of your backpack!");
                }
                else
                {
                    InventoryDamage.DestroyItem(py, RandomNumber.Next(py.Pack.UniqueItems) - 1);
                    PlayerCore.PrintMessage(w, "Your backpack feels lighter.");
                }
                if (RandomNumber.Next(2) == 1)
                {
                    PlayerCore.PrintMessage(w, "There is a puff of smoke!");
                    SpellEffects.TeleportAwayMonster(w, monsterId, MonsterConfig.MonMaxSight);
                }
                break;
            case 14: // Poison
                PlayerCore.TakesHit(w, damage, deathDescription);
                PlayerCore.PrintMessage(w, "You feel very sick.");
                f.Poisoned = (short)(f.Poisoned + RandomNumber.Next(creatureLevel) + 5);
                break;
            case 15: // Lose dexterity
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (f.SustainDex)
                {
                    PlayerCore.PrintMessage(w, "You feel clumsy for a moment, but it passes.");
                }
                else
                {
                    PlayerCore.PrintMessage(w, "You feel more clumsy.");
                    PlayerStatLogic.StatRandomDecrease(py, PlayerConfig.Attr.Dex);
                }
                break;
            case 16: // Lose constitution
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (f.SustainCon)
                {
                    PlayerCore.PrintMessage(w, "Your body resists the effects of the disease.");
                }
                else
                {
                    PlayerCore.PrintMessage(w, "Your health is damaged!");
                    PlayerStatLogic.StatRandomDecrease(py, PlayerConfig.Attr.Con);
                }
                break;
            case 17: // Lose intelligence
                PlayerCore.TakesHit(w, damage, deathDescription);
                PlayerCore.PrintMessage(w, "You have trouble thinking clearly.");
                if (f.SustainInt)
                {
                    PlayerCore.PrintMessage(w, "But your mind quickly clears.");
                }
                else
                {
                    PlayerStatLogic.StatRandomDecrease(py, PlayerConfig.Attr.Int);
                }
                break;
            case 18: // Lose wisdom
                PlayerCore.TakesHit(w, damage, deathDescription);
                if (f.SustainWis)
                {
                    PlayerCore.PrintMessage(w, "Your wisdom is sustained.");
                }
                else
                {
                    PlayerCore.PrintMessage(w, "Your wisdom is drained.");
                    PlayerStatLogic.StatRandomDecrease(py, PlayerConfig.Attr.Wis);
                }
                break;
            case 19: // Lose experience
                PlayerCore.PrintMessage(w, "You feel your life draining away!");
                SpellEffects.LoseExp(w, damage + py.Misc.Exp / 100 * MonsterConfig.MonPlayerExpDrainedPerHit);
                break;
            case 20: // Aggravate monster
                SpellEffects.AggravateMonsters(w, 20);
                break;
            case 21: // Disenchant
                if (InventoryDamage.ExecuteDisenchantAttack(py))
                {
                    PlayerCore.PrintMessage(w, "There is a static feeling in the air.");
                    PlayerBonuses.RecalculateBonuses(py);
                }
                else
                {
                    noticed = false;
                }
                break;
            case 22: // Eat food
                if (InventoryDamage.FindRange(py, TreasureCategory.TvFood, TreasureCategory.TvNever, out var itemPosStart, out _))
                {
                    InventoryDamage.DestroyItem(py, itemPosStart);
                    PlayerCore.PrintMessage(w, "It got at your rations!");
                }
                else
                {
                    noticed = false;
                }
                break;
            case 23: // Eat light
                noticed = InventoryDamage.DiminishLightAttack(w, noticed);
                break;
            case 24: // Eat charges
                noticed = InventoryDamage.DiminishChargesAttack(w, creatureLevel, ref monsterHp, noticed);
                break;
            default:
                // (includes 99, a repelled attack)
                noticed = false;
                break;
        }

        return noticed;
    }
}
