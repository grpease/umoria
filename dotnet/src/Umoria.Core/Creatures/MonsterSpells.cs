using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;

namespace Umoria.Core.Creatures;

// M9: monsters casting spells -- src/monster.cpp's monsterCastSpell(),
// monsterCanCastSpells() and monsterExecuteCastingOfSpell(): teleports,
// wounds, hold/blind/confuse/fear/slow, summoning, draining mana and the
// five breath weapons (spellBreath(), Spells.DamageSpells -- the
// confirmed monster.cpp -> spells.cpp coupling the plan built M9 around).
public static class MonsterSpells
{
    // src/monster.cpp:674 monsterCanCastSpells()
    private static bool CanCastSpells(PlayerWorld w, ref Monster monster, uint spells)
    {
        // 1 in x chance of casting spell
        if (RandomNumber.Next((int)(spells & MonsterConfig.Spells.CsFreq)) != 1)
        {
            return false;
        }

        // Must be within certain range
        var withinRange = monster.DistanceFromPlayer <= MonsterConfig.MonMaxSpellCastDistance;

        // Must have unobstructed Line-Of-Sight
        var unobstructed = Los.Check(w.Dg, w.Py.Pos, monster.Pos);

        return withinRange && unobstructed;
    }

    private static void ResistOr(PlayerWorld w, Action effect)
    {
        if (PlayerCombat.SavingThrow(w.Py))
        {
            PlayerCore.PrintMessage(w, "You resist the effects of the spell.");
        }
        else
        {
            effect();
        }
    }

    // src/monster.cpp:689 monsterExecuteCastingOfSpell()
    public static void ExecuteCastingOfSpell(PlayerWorld w, ref Monster monster, int monsterId, int spellId, byte level, string monsterName, string deathDescription)
    {
        var py = w.Py;
        var f = py.Flags;

        // Cast the spell.
        switch (spellId)
        {
            case 5: // Teleport Short
                SpellEffects.TeleportAwayMonster(w, monsterId, 5);
                break;
            case 6: // Teleport Long
                SpellEffects.TeleportAwayMonster(w, monsterId, MonsterConfig.MonMaxSight);
                break;
            case 7: // Teleport To
                SpellEffects.TeleportPlayerTo(w, monster.Pos);
                break;
            case 8: // Light Wound
                ResistOr(w, () => PlayerCore.TakesHit(w, DiceRoller.Roll(new Model.Generated.Dice(3, 8)), deathDescription));
                break;
            case 9: // Serious Wound
                ResistOr(w, () => PlayerCore.TakesHit(w, DiceRoller.Roll(new Model.Generated.Dice(8, 8)), deathDescription));
                break;
            case 10: // Hold Person
                if (f.FreeAction)
                {
                    PlayerCore.PrintMessage(w, "You are unaffected.");
                }
                else
                {
                    ResistOr(w, () =>
                    {
                        if (f.Paralysis > 0)
                        {
                            f.Paralysis += 2;
                        }
                        else
                        {
                            f.Paralysis = (short)(RandomNumber.Next(5) + 4);
                        }
                    });
                }
                break;
            case 11: // Cause Blindness
                ResistOr(w, () =>
                {
                    if (f.Blind > 0)
                    {
                        f.Blind += 6;
                    }
                    else
                    {
                        f.Blind = (short)(f.Blind + 12 + RandomNumber.Next(3));
                    }
                });
                break;
            case 12: // Cause Confuse
                ResistOr(w, () =>
                {
                    if (f.Confused > 0)
                    {
                        f.Confused += 2;
                    }
                    else
                    {
                        f.Confused = (short)(RandomNumber.Next(5) + 3);
                    }
                });
                break;
            case 13: // Cause Fear
                ResistOr(w, () =>
                {
                    if (f.Afraid > 0)
                    {
                        f.Afraid += 2;
                    }
                    else
                    {
                        f.Afraid = (short)(RandomNumber.Next(5) + 3);
                    }
                });
                break;
            case 14: // Summon Monster
            {
                PlayerCore.PrintMessage(w, monsterName + "magically summons a monster!");
                var coord = py.Pos;

                // in case compact_monster() is called,it needs monster_id
                w.Monsters.HackMonptr = monsterId;
                w.Monsters.Summon(ref coord, false, py.Pos, f.Speed);
                w.Monsters.HackMonptr = -1;
                MonsterAi.UpdateVisibility(w, w.Dg.Floor[coord.Y, coord.X].CreatureId);
                break;
            }
            case 15: // Summon Undead
            {
                PlayerCore.PrintMessage(w, monsterName + "magically summons an undead!");
                var coord = py.Pos;

                // in case compact_monster() is called,it needs monster_id
                w.Monsters.HackMonptr = monsterId;
                w.Monsters.SummonUndead(ref coord, py.Pos, f.Speed);
                w.Monsters.HackMonptr = -1;
                MonsterAi.UpdateVisibility(w, w.Dg.Floor[coord.Y, coord.X].CreatureId);
                break;
            }
            case 16: // Slow Person
                if (f.FreeAction)
                {
                    PlayerCore.PrintMessage(w, "You are unaffected.");
                }
                else
                {
                    ResistOr(w, () =>
                    {
                        if (f.Slow > 0)
                        {
                            f.Slow += 2;
                        }
                        else
                        {
                            f.Slow = (short)(RandomNumber.Next(5) + 3);
                        }
                    });
                }
                break;
            case 17: // Drain Mana
                if (py.Misc.CurrentMana > 0)
                {
                    PlayerCore.Disturb(w, 1, 0);

                    PlayerCore.PrintMessage(w, monsterName + "draws psychic energy from you!");

                    if (monster.Lit)
                    {
                        PlayerCore.PrintMessage(w, monsterName + "appears healthier.");
                    }

                    var num = (RandomNumber.Next(level) >> 1) + 1;
                    if (num > py.Misc.CurrentMana)
                    {
                        num = py.Misc.CurrentMana;
                        py.Misc.CurrentMana = 0;
                        py.Misc.CurrentManaFraction = 0;
                    }
                    else
                    {
                        py.Misc.CurrentMana = (short)(py.Misc.CurrentMana - num);
                    }
                    // (printCharacterCurrentMana() -- display)
                    monster.Hp = (short)(monster.Hp + 6 * num);
                }
                break;
            case 20: // Breath Light
                PlayerCore.PrintMessage(w, monsterName + "breathes lightning.");
                DamageSpells.Breath(w, py.Pos, monsterId, monster.Hp / 4, MagicSpellFlags.Lightning, deathDescription);
                break;
            case 21: // Breath Gas
                PlayerCore.PrintMessage(w, monsterName + "breathes gas.");
                DamageSpells.Breath(w, py.Pos, monsterId, monster.Hp / 3, MagicSpellFlags.PoisonGas, deathDescription);
                break;
            case 22: // Breath Acid
                PlayerCore.PrintMessage(w, monsterName + "breathes acid.");
                DamageSpells.Breath(w, py.Pos, monsterId, monster.Hp / 3, MagicSpellFlags.Acid, deathDescription);
                break;
            case 23: // Breath Frost
                PlayerCore.PrintMessage(w, monsterName + "breathes frost.");
                DamageSpells.Breath(w, py.Pos, monsterId, monster.Hp / 3, MagicSpellFlags.Frost, deathDescription);
                break;
            case 24: // Breath Fire
                PlayerCore.PrintMessage(w, monsterName + "breathes fire.");
                DamageSpells.Breath(w, py.Pos, monsterId, monster.Hp / 3, MagicSpellFlags.Fire, deathDescription);
                break;
            default:
                PlayerCore.PrintMessage(w, monsterName + "cast unknown spell.");
                break;
        }
    }

    // src/monster.cpp:849 monsterCastSpell() -- creatures can cast spells
    // too. (Dragon Breath) -RAK- Returns true (took_turn) if creature casts
    // a spell.
    public static bool CastSpell(PlayerWorld w, int monsterId)
    {
        if (w.Game.CharacterIsDead)
        {
            return false;
        }

        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        if (!CanCastSpells(w, ref monster, creature.Spells))
        {
            return false;
        }

        // Creature is going to cast a spell

        // Check to see if monster should be lit.
        MonsterAi.UpdateVisibility(w, monsterId);

        // Describe the attack
        var name = monster.Lit ? $"The {creature.Name} " : "It ";

        var deathDescription = PlayerCombat.DiedFromString(creature.Name, creature.Movement);

        // Extract all possible spells into spell_choice
        var spellChoice = new int[30];
        var spellFlags = creature.Spells & ~MonsterConfig.Spells.CsFreq;

        var id = 0;
        while (spellFlags != 0)
        {
            spellChoice[id] = GetAndClearFirstBit(ref spellFlags);
            id++;
        }

        // Choose a spell to cast
        var thrownSpell = spellChoice[RandomNumber.Next(id) - 1];
        thrownSpell++;

        // all except spellTeleportAwayMonster() and drain mana spells always disturb
        if (thrownSpell > 6 && thrownSpell != 17)
        {
            PlayerCore.Disturb(w, 1, 0);
        }

        // save some code/data space here, with a small time penalty
        if ((thrownSpell < 14 && thrownSpell > 6) || thrownSpell == 16)
        {
            PlayerCore.PrintMessage(w, name + "casts a spell.");
        }

        ExecuteCastingOfSpell(w, ref monster, monsterId, thrownSpell, creature.Level, name, deathDescription);

        if (monster.Lit)
        {
            var memory = w.CreatureRecall[monster.CreatureId];

            memory.Spells |= 1u << (thrownSpell - 1);
            if ((memory.Spells & MonsterConfig.Spells.CsFreq) != MonsterConfig.Spells.CsFreq)
            {
                memory.Spells++;
            }
            if (w.Game.CharacterIsDead && memory.Deaths < short.MaxValue)
            {
                memory.Deaths++;
            }
        }

        return true;
    }

    // src/helpers.cpp:10 getAndClearFirstBit()
    private static int GetAndClearFirstBit(ref uint flag)
    {
        uint mask = 0x1;

        for (var i = 0; i < 32; i++)
        {
            if ((flag & mask) != 0)
            {
                flag &= ~mask;
                return i;
            }
            mask <<= 1;
        }

        // no one bits found
        return -1;
    }
}
