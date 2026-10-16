using Umoria.Core.Config;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Dungeon;

// Port of magicTreasureMagicalAbility (src/treasure.cpp:854) and its ~20
// helpers -- M3 per migration-plan.md. Discovered (not assumed) to be a
// hard prerequisite for dungeon object placement (M4) and confirmed also
// required by store restocking (store_inventory.cpp:354).
//
// The original takes an item_id and mutates game.treasure.list[item_id] in
// place; since the real item-instance list doesn't exist yet (M2's
// TreasureStub only tracks a bare category id for door/stair placement),
// this operates directly on a ref Inventory instead. Wiring this into a
// real treasure list is M4's job (object placement) per migration-plan.md.
//
// Defer: store-specific pricing (priceAdjust()) stays with stores (M13).
public static class TreasureMagic
{
    // src/treasure.cpp:823 missiles_counter -- a true process-global in the
    // original (not reset per level), persisted across save/load. M12 will
    // need to persist this field once save/load exists.
    public static short MissilesCounter;

    // src/treasure.cpp:854 magicTreasureMagicalAbility()
    // "Chance of treasure having magic abilities -RAK-
    //  Chance increases with each dungeon level"
    public static void MagicalAbility(ref Inventory item, int level)
    {
        var chance = TreasureConfig.ObjectBaseMagic + level;
        if (chance > TreasureConfig.ObjectMaxBaseMagic)
        {
            chance = TreasureConfig.ObjectMaxBaseMagic;
        }

        var special = chance / TreasureConfig.ObjectChanceSpecial;
        var cursed = (10 * chance) / TreasureConfig.ObjectChanceCursed;

        switch (item.CategoryId)
        {
            case TreasureCategory.TvShield:
            case TreasureCategory.TvHardArmor:
            case TreasureCategory.TvSoftArmor:
                if (ShouldBeEnchanted(chance))
                {
                    MagicalArmor(ref item, special, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedArmor(ref item, level);
                }

                break;

            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvSword:
                // always show to_hit/to_damage values if identified
                item.Identification |= IdentificationConfig.IdShowHitDam;

                if (ShouldBeEnchanted(chance))
                {
                    MagicalSword(ref item, special, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedSword(ref item, level);
                }

                break;

            case TreasureCategory.TvBow:
                item.Identification |= IdentificationConfig.IdShowHitDam;

                if (ShouldBeEnchanted(chance))
                {
                    MagicalBow(ref item, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedBow(ref item, level);
                }

                break;

            case TreasureCategory.TvDigging:
                item.Identification |= IdentificationConfig.IdShowHitDam;

                if (ShouldBeEnchanted(chance))
                {
                    if (RandomNumber.Next(3) < 3)
                    {
                        MagicalDiggingTool(ref item, level);
                    }
                    else
                    {
                        CursedDiggingTool(ref item, level);
                    }
                }

                break;

            case TreasureCategory.TvGloves:
                if (ShouldBeEnchanted(chance))
                {
                    MagicalGloves(ref item, special, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedGloves(ref item, special, level);
                }

                break;

            case TreasureCategory.TvBoots:
                if (ShouldBeEnchanted(chance))
                {
                    MagicalBoots(ref item, special, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedBoots(ref item, level);
                }

                break;

            case TreasureCategory.TvHelm:
                // give crowns a higher chance for magic
                if (item.SubCategoryId >= 6 && item.SubCategoryId <= 8)
                {
                    chance += item.Cost / 100;
                    special += special;
                }

                if (ShouldBeEnchanted(chance))
                {
                    MagicalHelms(ref item, special, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedHelms(ref item, special, level);
                }

                break;

            case TreasureCategory.TvRing:
                ProcessRings(ref item, level, cursed);
                break;

            case TreasureCategory.TvAmulet:
                ProcessAmulets(ref item, level, cursed);
                break;

            case TreasureCategory.TvLight:
                // sub_category_id should be even for store, odd for dungeon.
                // Dungeon found ones will be partially charged.
                if ((item.SubCategoryId % 2) == 1)
                {
                    item.MiscUse = (short)RandomNumber.Next(item.MiscUse);
                    item.SubCategoryId -= 1;
                }

                break;

            case TreasureCategory.TvWand:
            {
                var magicAmount = WandMagic(item.SubCategoryId);
                if (magicAmount != -1)
                {
                    item.MiscUse = (short)magicAmount;
                }

                break;
            }

            case TreasureCategory.TvStaff:
            {
                var magicAmount = StaffMagic(item.SubCategoryId);
                if (magicAmount != -1)
                {
                    item.MiscUse = (short)magicAmount;
                }

                // Change the level the item was first found on value
                if (item.SubCategoryId == 7)
                {
                    item.DepthFirstFound = 10;
                }
                else if (item.SubCategoryId == 22)
                {
                    item.DepthFirstFound = 5;
                }

                break;
            }

            case TreasureCategory.TvCloak:
                if (ShouldBeEnchanted(chance))
                {
                    MagicalCloak(ref item, special, level);
                }
                else if (ShouldBeEnchanted(cursed))
                {
                    CursedCloak(ref item, level);
                }

                break;

            case TreasureCategory.TvChest:
                MagicalChests(ref item, level);
                break;

            case TreasureCategory.TvSlingAmmo:
            case TreasureCategory.TvSpike:
            case TreasureCategory.TvBolt:
            case TreasureCategory.TvArrow:
                MagicalProjectile(ref item, special, level, chance, cursed);
                break;

            case TreasureCategory.TvFood:
                // make sure all food rations have the same level
                if (item.SubCategoryId == 90)
                {
                    item.DepthFirstFound = 0;
                }

                // give all Elvish waybread the same level
                if (item.SubCategoryId == 92)
                {
                    item.DepthFirstFound = 6;
                }

                break;

            case TreasureCategory.TvScroll1:
                if (item.SubCategoryId == 67)
                {
                    // give all identify scrolls the same level
                    item.DepthFirstFound = 1;
                }
                else if (item.SubCategoryId == 69)
                {
                    // scroll of light
                    item.DepthFirstFound = 0;
                }
                else if (item.SubCategoryId == 80)
                {
                    // scroll of trap detection
                    item.DepthFirstFound = 5;
                }
                else if (item.SubCategoryId == 81)
                {
                    // scroll of door/stair location
                    item.DepthFirstFound = 5;
                }

                break;

            case TreasureCategory.TvPotion1:
                // cure light
                if (item.SubCategoryId == 76)
                {
                    item.DepthFirstFound = 0;
                }

                break;
        }
    }

    // src/treasure.cpp:11 magicShouldBeEnchanted()
    private static bool ShouldBeEnchanted(int chance)
    {
        return RandomNumber.Next(100) <= chance;
    }

    // src/treasure.cpp:16 magicEnchantmentBonus()
    private static int EnchantmentBonus(int baseValue, int maxStandard, int level)
    {
        var standardDeviation = (TreasureConfig.LevelStdObjectAdjust * level / 100) + TreasureConfig.LevelMinObjectStd;

        // Check for level > max_standard since that may have generated an overflow.
        if (standardDeviation > maxStandard || level > maxStandard)
        {
            standardDeviation = maxStandard;
        }

        var absDistribution = Math.Abs(RandomNumber.NextNormal(0, standardDeviation));
        var bonus = (absDistribution / 10) + baseValue;

        return bonus < baseValue ? baseValue : bonus;
    }

    // src/treasure.cpp:35 magicalArmor()
    private static void MagicalArmor(ref Inventory item, int special, int level)
    {
        item.ToAc += (short)EnchantmentBonus(1, 30, level);

        if (!ShouldBeEnchanted(special))
        {
            return;
        }

        switch (RandomNumber.Next(9))
        {
            case 1:
                item.Flags |= TreasureConfig.Flags.TrResLight | TreasureConfig.Flags.TrResCold | TreasureConfig.Flags.TrResAcid |
                              TreasureConfig.Flags.TrResFire;
                item.SpecialNameId = (byte)SpecialNameIds.SnR;
                item.ToAc += 5;
                item.Cost += 2500;
                break;
            case 2: // Resist Acid
                item.Flags |= TreasureConfig.Flags.TrResAcid;
                item.SpecialNameId = (byte)SpecialNameIds.SnRa;
                item.Cost += 1000;
                break;
            case 3:
            case 4: // Resist Fire
                item.Flags |= TreasureConfig.Flags.TrResFire;
                item.SpecialNameId = (byte)SpecialNameIds.SnRf;
                item.Cost += 600;
                break;
            case 5:
            case 6: // Resist Cold
                item.Flags |= TreasureConfig.Flags.TrResCold;
                item.SpecialNameId = (byte)SpecialNameIds.SnRc;
                item.Cost += 600;
                break;
            case 7:
            case 8:
            case 9: // Resist Lightning
                item.Flags |= TreasureConfig.Flags.TrResLight;
                item.SpecialNameId = (byte)SpecialNameIds.SnRl;
                item.Cost += 500;
                break;
        }
    }

    // src/treasure.cpp:80 cursedArmor()
    private static void CursedArmor(ref Inventory item, int level)
    {
        item.ToAc -= (short)EnchantmentBonus(1, 40, level);
        item.Cost = 0;
        item.Flags |= TreasureConfig.Flags.TrCursed;
    }

    // src/treasure.cpp:86 magicalSword()
    private static void MagicalSword(ref Inventory item, int special, int level)
    {
        item.ToHit += (short)EnchantmentBonus(0, 40, level);

        // Magical damage bonus now proportional to weapon base damage
        var damageBonus = Umoria.Core.Dice.DiceRoller.MaxRoll(item.Damage);

        item.ToDamage += (short)EnchantmentBonus(0, 4 * damageBonus, damageBonus * level / 10);

        // the 3*special/2 is needed because weapons are not as common as
        // before change to treasure distribution, this helps keep same
        // number of ego weapons same as before, see also missiles
        if (!ShouldBeEnchanted(3 * special / 2))
        {
            return;
        }

        switch (RandomNumber.Next(16))
        {
            case 1: // Holy Avenger
                item.Flags |= TreasureConfig.Flags.TrSeeInvis | TreasureConfig.Flags.TrSustStat | TreasureConfig.Flags.TrSlayUndead |
                              TreasureConfig.Flags.TrSlayEvil | TreasureConfig.Flags.TrStr;
                item.ToHit += 5;
                item.ToDamage += 5;
                item.ToAc += (short)RandomNumber.Next(4);

                // the value in `misc_use` is used for strength increase
                // `misc_use` is also used for sustain stat
                item.MiscUse = (short)RandomNumber.Next(4);
                item.SpecialNameId = (byte)SpecialNameIds.SnHa;
                item.Cost += item.MiscUse * 500;
                item.Cost += 10000;
                break;
            case 2: // Defender
                item.Flags |= TreasureConfig.Flags.TrFfall | TreasureConfig.Flags.TrResLight | TreasureConfig.Flags.TrSeeInvis |
                              TreasureConfig.Flags.TrFreeAct | TreasureConfig.Flags.TrResCold | TreasureConfig.Flags.TrResAcid |
                              TreasureConfig.Flags.TrResFire | TreasureConfig.Flags.TrRegen | TreasureConfig.Flags.TrStealth;
                item.ToHit += 3;
                item.ToDamage += 3;
                item.ToAc += (short)(5 + RandomNumber.Next(5));
                item.SpecialNameId = (byte)SpecialNameIds.SnDf;

                // the value in `misc_use` is used for stealth
                item.MiscUse = (short)RandomNumber.Next(3);
                item.Cost += item.MiscUse * 500;
                item.Cost += 7500;
                break;
            case 3:
            case 4: // Slay Animal
                item.Flags |= TreasureConfig.Flags.TrSlayAnimal;
                item.ToHit += 2;
                item.ToDamage += 2;
                item.SpecialNameId = (byte)SpecialNameIds.SnSa;
                item.Cost += 3000;
                break;
            case 5:
            case 6: // Slay Dragon
                item.Flags |= TreasureConfig.Flags.TrSlayDragon;
                item.ToHit += 3;
                item.ToDamage += 3;
                item.SpecialNameId = (byte)SpecialNameIds.SnSd;
                item.Cost += 4000;
                break;
            case 7:
            case 8: // Slay Evil
                item.Flags |= TreasureConfig.Flags.TrSlayEvil;
                item.ToHit += 3;
                item.ToDamage += 3;
                item.SpecialNameId = (byte)SpecialNameIds.SnSe;
                item.Cost += 4000;
                break;
            case 9:
            case 10: // Slay Undead
                item.Flags |= TreasureConfig.Flags.TrSeeInvis | TreasureConfig.Flags.TrSlayUndead;
                item.ToHit += 3;
                item.ToDamage += 3;
                item.SpecialNameId = (byte)SpecialNameIds.SnSu;
                item.Cost += 5000;
                break;
            case 11:
            case 12:
            case 13: // Flame Tongue
                item.Flags |= TreasureConfig.Flags.TrFlameTongue;
                item.ToHit++;
                item.ToDamage += 3;
                item.SpecialNameId = (byte)SpecialNameIds.SnFt;
                item.Cost += 2000;
                break;
            case 14:
            case 15:
            case 16: // Frost Brand
                item.Flags |= TreasureConfig.Flags.TrFrostBrand;
                item.ToHit++;
                item.ToDamage++;
                item.SpecialNameId = (byte)SpecialNameIds.SnFb;
                item.Cost += 1200;
                break;
        }
    }

    // src/treasure.cpp:183 cursedSword()
    private static void CursedSword(ref Inventory item, int level)
    {
        item.ToHit -= (short)EnchantmentBonus(1, 55, level);

        // Magical damage bonus now proportional to weapon base damage
        var damageBonus = Umoria.Core.Dice.DiceRoller.MaxRoll(item.Damage);

        item.ToDamage -= (short)EnchantmentBonus(1, 11 * damageBonus / 2, damageBonus * level / 10);
        item.Flags |= TreasureConfig.Flags.TrCursed;
        item.Cost = 0;
    }

    // src/treasure.cpp:194 magicalBow()
    private static void MagicalBow(ref Inventory item, int level)
    {
        item.ToHit += (short)EnchantmentBonus(1, 30, level);

        // add damage. -CJS-
        item.ToDamage += (short)EnchantmentBonus(1, 20, level);
    }

    // src/treasure.cpp:201 cursedBow()
    private static void CursedBow(ref Inventory item, int level)
    {
        item.ToHit -= (short)EnchantmentBonus(1, 50, level);

        // add damage. -CJS-
        item.ToDamage -= (short)EnchantmentBonus(1, 30, level);

        item.Flags |= TreasureConfig.Flags.TrCursed;
        item.Cost = 0;
    }

    // src/treasure.cpp:211 magicalDiggingTool()
    private static void MagicalDiggingTool(ref Inventory item, int level)
    {
        item.MiscUse += (short)EnchantmentBonus(0, 25, level);
    }

    // src/treasure.cpp:215 cursedDiggingTool()
    private static void CursedDiggingTool(ref Inventory item, int level)
    {
        item.MiscUse = (short)-EnchantmentBonus(1, 30, level);
        item.Cost = 0;
        item.Flags |= TreasureConfig.Flags.TrCursed;
    }

    // src/treasure.cpp:221 magicalGloves()
    private static void MagicalGloves(ref Inventory item, int special, int level)
    {
        item.ToAc += (short)EnchantmentBonus(1, 20, level);

        if (!ShouldBeEnchanted(special))
        {
            return;
        }

        if (RandomNumber.Next(2) == 1)
        {
            item.Flags |= TreasureConfig.Flags.TrFreeAct;
            item.SpecialNameId = (byte)SpecialNameIds.SnFreeAction;
            item.Cost += 1000;
        }
        else
        {
            item.Identification |= IdentificationConfig.IdShowHitDam;
            item.ToHit += (short)(1 + RandomNumber.Next(3));
            item.ToDamage += (short)(1 + RandomNumber.Next(3));
            item.SpecialNameId = (byte)SpecialNameIds.SnSlaying;
            item.Cost += (item.ToHit + item.ToDamage) * 250;
        }
    }

    // src/treasure.cpp:241 cursedGloves()
    private static void CursedGloves(ref Inventory item, int special, int level)
    {
        if (ShouldBeEnchanted(special))
        {
            if (RandomNumber.Next(2) == 1)
            {
                item.Flags |= TreasureConfig.Flags.TrDex;
                item.SpecialNameId = (byte)SpecialNameIds.SnClumsiness;
            }
            else
            {
                item.Flags |= TreasureConfig.Flags.TrStr;
                item.SpecialNameId = (byte)SpecialNameIds.SnWeakness;
            }

            item.Identification |= IdentificationConfig.IdShowP1;
            item.MiscUse = (short)-EnchantmentBonus(1, 10, level);
        }

        item.ToAc -= (short)EnchantmentBonus(1, 40, level);
        item.Flags |= TreasureConfig.Flags.TrCursed;
        item.Cost = 0;
    }

    // src/treasure.cpp:259 magicalBoots()
    private static void MagicalBoots(ref Inventory item, int special, int level)
    {
        item.ToAc += (short)EnchantmentBonus(1, 20, level);

        if (!ShouldBeEnchanted(special))
        {
            return;
        }

        var magicType = RandomNumber.Next(12);

        if (magicType > 5)
        {
            item.Flags |= TreasureConfig.Flags.TrFfall;
            item.SpecialNameId = (byte)SpecialNameIds.SnSlowDescent;
            item.Cost += 250;
        }
        else if (magicType == 1)
        {
            item.Flags |= TreasureConfig.Flags.TrSpeed;
            item.SpecialNameId = (byte)SpecialNameIds.SnSpeed;
            item.Identification |= IdentificationConfig.IdShowP1;
            item.MiscUse = 1;
            item.Cost += 5000;
        }
        else
        {
            // 2 - 5
            item.Flags |= TreasureConfig.Flags.TrStealth;
            item.Identification |= IdentificationConfig.IdShowP1;
            item.MiscUse = (short)RandomNumber.Next(3);
            item.SpecialNameId = (byte)SpecialNameIds.SnStealth;
            item.Cost += 500;
        }
    }

    // src/treasure.cpp:288 cursedBoots()
    private static void CursedBoots(ref Inventory item, int level)
    {
        var magicType = RandomNumber.Next(3);

        switch (magicType)
        {
            case 1:
                item.Flags |= TreasureConfig.Flags.TrSpeed;
                item.SpecialNameId = (byte)SpecialNameIds.SnSlowness;
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = -1;
                break;
            case 2:
                item.Flags |= TreasureConfig.Flags.TrAggravate;
                item.SpecialNameId = (byte)SpecialNameIds.SnNoise;
                break;
            default:
                item.SpecialNameId = (byte)SpecialNameIds.SnGreatMass;
                item.Weight = (ushort)(item.Weight * 5);
                break;
        }

        item.Cost = 0;
        item.ToAc -= (short)EnchantmentBonus(2, 45, level);
        item.Flags |= TreasureConfig.Flags.TrCursed;
    }

    // src/treasure.cpp:313 magicalHelms()
    private static void MagicalHelms(ref Inventory item, int special, int level)
    {
        item.ToAc += (short)EnchantmentBonus(1, 20, level);

        if (!ShouldBeEnchanted(special))
        {
            return;
        }

        if (item.SubCategoryId < 6)
        {
            item.Identification |= IdentificationConfig.IdShowP1;

            var magicType = RandomNumber.Next(3);

            switch (magicType)
            {
                case 1:
                    item.MiscUse = (short)RandomNumber.Next(2);
                    item.Flags |= TreasureConfig.Flags.TrInt;
                    item.SpecialNameId = (byte)SpecialNameIds.SnIntelligence;
                    item.Cost += item.MiscUse * 500;
                    break;
                case 2:
                    item.MiscUse = (short)RandomNumber.Next(2);
                    item.Flags |= TreasureConfig.Flags.TrWis;
                    item.SpecialNameId = (byte)SpecialNameIds.SnWisdom;
                    item.Cost += item.MiscUse * 500;
                    break;
                default:
                    item.MiscUse = (short)(1 + RandomNumber.Next(4));
                    item.Flags |= TreasureConfig.Flags.TrInfra;
                    item.SpecialNameId = (byte)SpecialNameIds.SnInfravision;
                    item.Cost += item.MiscUse * 250;
                    break;
            }

            return;
        }

        switch (RandomNumber.Next(6))
        {
            case 1:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)RandomNumber.Next(3);
                item.Flags |= TreasureConfig.Flags.TrFreeAct | TreasureConfig.Flags.TrCon | TreasureConfig.Flags.TrDex | TreasureConfig.Flags.TrStr;
                item.SpecialNameId = (byte)SpecialNameIds.SnMight;
                item.Cost += 1000 + (item.MiscUse * 500);
                break;
            case 2:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)RandomNumber.Next(3);
                item.Flags |= TreasureConfig.Flags.TrChr | TreasureConfig.Flags.TrWis;
                item.SpecialNameId = (byte)SpecialNameIds.SnLordliness;
                item.Cost += 1000 + (item.MiscUse * 500);
                break;
            case 3:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)RandomNumber.Next(3);
                item.Flags |= TreasureConfig.Flags.TrResLight | TreasureConfig.Flags.TrResCold | TreasureConfig.Flags.TrResAcid |
                              TreasureConfig.Flags.TrResFire | TreasureConfig.Flags.TrInt;
                item.SpecialNameId = (byte)SpecialNameIds.SnMagi;
                item.Cost += 3000 + (item.MiscUse * 500);
                break;
            case 4:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)RandomNumber.Next(3);
                item.Flags |= TreasureConfig.Flags.TrChr;
                item.SpecialNameId = (byte)SpecialNameIds.SnBeauty;
                item.Cost += 750;
                break;
            case 5:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)(5 * (1 + RandomNumber.Next(4)));
                item.Flags |= TreasureConfig.Flags.TrSeeInvis | TreasureConfig.Flags.TrSearch;
                item.SpecialNameId = (byte)SpecialNameIds.SnSeeing;
                item.Cost += 1000 + (item.MiscUse * 100);
                break;
            case 6:
                item.Flags |= TreasureConfig.Flags.TrRegen;
                item.SpecialNameId = (byte)SpecialNameIds.SnRegeneration;
                item.Cost += 1500;
                break;
        }
    }

    // src/treasure.cpp:394 cursedHelms()
    private static void CursedHelms(ref Inventory item, int special, int level)
    {
        item.ToAc -= (short)EnchantmentBonus(1, 45, level);
        item.Flags |= TreasureConfig.Flags.TrCursed;
        item.Cost = 0;

        if (!ShouldBeEnchanted(special))
        {
            return;
        }

        switch (RandomNumber.Next(7))
        {
            case 1:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)-RandomNumber.Next(5);
                item.Flags |= TreasureConfig.Flags.TrInt;
                item.SpecialNameId = (byte)SpecialNameIds.SnStupidity;
                break;
            case 2:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)-RandomNumber.Next(5);
                item.Flags |= TreasureConfig.Flags.TrWis;
                item.SpecialNameId = (byte)SpecialNameIds.SnDullness;
                break;
            case 3:
                item.Flags |= TreasureConfig.Flags.TrBlind;
                item.SpecialNameId = (byte)SpecialNameIds.SnBlindness;
                break;
            case 4:
                item.Flags |= TreasureConfig.Flags.TrTimid;
                item.SpecialNameId = (byte)SpecialNameIds.SnTimidness;
                break;
            case 5:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)-RandomNumber.Next(5);
                item.Flags |= TreasureConfig.Flags.TrStr;
                item.SpecialNameId = (byte)SpecialNameIds.SnWeakness;
                break;
            case 6:
                item.Flags |= TreasureConfig.Flags.TrTeleport;
                item.SpecialNameId = (byte)SpecialNameIds.SnTeleportation;
                break;
            case 7:
                item.Identification |= IdentificationConfig.IdShowP1;
                item.MiscUse = (short)-RandomNumber.Next(5);
                item.Flags |= TreasureConfig.Flags.TrChr;
                item.SpecialNameId = (byte)SpecialNameIds.SnUgliness;
                break;
        }
    }

    // src/treasure.cpp:445 processRings()
    private static void ProcessRings(ref Inventory item, int level, int cursed)
    {
        switch (item.SubCategoryId)
        {
            case 0:
            case 1:
            case 2:
            case 3:
                if (ShouldBeEnchanted(cursed))
                {
                    item.MiscUse = (short)-EnchantmentBonus(1, 20, level);
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }
                else
                {
                    item.MiscUse = (short)EnchantmentBonus(1, 10, level);
                    item.Cost += item.MiscUse * 100;
                }

                break;
            case 4:
                if (ShouldBeEnchanted(cursed))
                {
                    item.MiscUse = (short)-RandomNumber.Next(3);
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }
                else
                {
                    item.MiscUse = 1;
                }

                break;
            case 5:
                item.MiscUse = (short)(5 * EnchantmentBonus(1, 20, level));
                item.Cost += item.MiscUse * 50;
                if (ShouldBeEnchanted(cursed))
                {
                    item.MiscUse = (short)-item.MiscUse;
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }

                break;
            case 19: // Increase damage
                item.ToDamage += (short)EnchantmentBonus(1, 20, level);
                item.Cost += item.ToDamage * 100;
                if (ShouldBeEnchanted(cursed))
                {
                    item.ToDamage = (short)-item.ToDamage;
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }

                break;
            case 20: // Increase To-Hit
                item.ToHit += (short)EnchantmentBonus(1, 20, level);
                item.Cost += item.ToHit * 100;
                if (ShouldBeEnchanted(cursed))
                {
                    item.ToHit = (short)-item.ToHit;
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }

                break;
            case 21: // Protection
                item.ToAc += (short)EnchantmentBonus(1, 20, level);
                item.Cost += item.ToAc * 100;
                if (ShouldBeEnchanted(cursed))
                {
                    item.ToAc = (short)-item.ToAc;
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }

                break;
            case 24:
            case 25:
            case 26:
            case 27:
            case 28:
            case 29:
                item.Identification |= IdentificationConfig.IdNoShowP1;
                break;
            case 30: // Slaying
                item.Identification |= IdentificationConfig.IdShowHitDam;
                item.ToDamage += (short)EnchantmentBonus(1, 25, level);
                item.ToHit += (short)EnchantmentBonus(1, 25, level);
                item.Cost += (item.ToHit + item.ToDamage) * 100;
                if (ShouldBeEnchanted(cursed))
                {
                    item.ToHit = (short)-item.ToHit;
                    item.ToDamage = (short)-item.ToDamage;
                    item.Flags |= TreasureConfig.Flags.TrCursed;
                    item.Cost = -item.Cost;
                }

                break;
        }
    }

    // src/treasure.cpp:530 processAmulets()
    private static void ProcessAmulets(ref Inventory item, int level, int cursed)
    {
        if (item.SubCategoryId < 2)
        {
            if (ShouldBeEnchanted(cursed))
            {
                item.MiscUse = (short)-EnchantmentBonus(1, 20, level);
                item.Flags |= TreasureConfig.Flags.TrCursed;
                item.Cost = -item.Cost;
            }
            else
            {
                item.MiscUse = (short)EnchantmentBonus(1, 10, level);
                item.Cost += item.MiscUse * 100;
            }
        }
        else if (item.SubCategoryId == 2)
        {
            item.MiscUse = (short)(5 * EnchantmentBonus(1, 25, level));
            if (ShouldBeEnchanted(cursed))
            {
                item.MiscUse = (short)-item.MiscUse;
                item.Cost = -item.Cost;
                item.Flags |= TreasureConfig.Flags.TrCursed;
            }
            else
            {
                item.Cost += 50 * item.MiscUse;
            }
        }
        else if (item.SubCategoryId == 8)
        {
            // amulet of the magi is never cursed
            item.MiscUse = (short)(5 * EnchantmentBonus(1, 25, level));
            item.Cost += 20 * item.MiscUse;
        }
    }

    // src/treasure.cpp:556 wandMagic()
    private static int WandMagic(byte id)
    {
        return id switch
        {
            0 => RandomNumber.Next(10) + 6,
            1 => RandomNumber.Next(8) + 6,
            2 => RandomNumber.Next(5) + 6,
            3 => RandomNumber.Next(8) + 6,
            4 => RandomNumber.Next(4) + 3,
            5 => RandomNumber.Next(8) + 6,
            6 or 7 => RandomNumber.Next(20) + 12,
            8 => RandomNumber.Next(10) + 6,
            9 => RandomNumber.Next(12) + 6,
            10 => RandomNumber.Next(10) + 12,
            11 => RandomNumber.Next(3) + 3,
            12 => RandomNumber.Next(8) + 6,
            13 => RandomNumber.Next(10) + 6,
            14 or 15 => RandomNumber.Next(5) + 3,
            16 => RandomNumber.Next(5) + 6,
            17 => RandomNumber.Next(5) + 4,
            18 => RandomNumber.Next(8) + 4,
            19 => RandomNumber.Next(6) + 2,
            20 => RandomNumber.Next(4) + 2,
            21 => RandomNumber.Next(8) + 6,
            22 => RandomNumber.Next(5) + 2,
            23 => RandomNumber.Next(12) + 12,
            _ => -1,
        };
    }

    // src/treasure.cpp:609 staffMagic()
    private static int StaffMagic(byte id)
    {
        return id switch
        {
            0 => RandomNumber.Next(20) + 12,
            1 => RandomNumber.Next(8) + 6,
            2 => RandomNumber.Next(5) + 6,
            3 => RandomNumber.Next(20) + 12,
            4 => RandomNumber.Next(15) + 6,
            5 => RandomNumber.Next(4) + 5,
            6 => RandomNumber.Next(5) + 3,
            7 or 8 => RandomNumber.Next(3) + 1,
            9 => RandomNumber.Next(5) + 6,
            10 => RandomNumber.Next(10) + 12,
            11 or 12 or 13 => RandomNumber.Next(5) + 6,
            14 => RandomNumber.Next(10) + 12,
            15 => RandomNumber.Next(3) + 4,
            16 or 17 => RandomNumber.Next(5) + 6,
            18 => RandomNumber.Next(3) + 4,
            19 => RandomNumber.Next(10) + 12,
            20 or 21 => RandomNumber.Next(3) + 4,
            22 => RandomNumber.Next(10) + 6,
            _ => -1,
        };
    }

    // src/treasure.cpp:657 magicalCloak()
    private static void MagicalCloak(ref Inventory item, int special, int level)
    {
        if (!ShouldBeEnchanted(special))
        {
            item.ToAc += (short)EnchantmentBonus(1, 20, level);
            return;
        }

        if (RandomNumber.Next(2) == 1)
        {
            item.SpecialNameId = (byte)SpecialNameIds.SnProtection;
            item.ToAc += (short)EnchantmentBonus(2, 40, level);
            item.Cost += 250;
            return;
        }

        item.ToAc += (short)EnchantmentBonus(1, 20, level);
        item.Identification |= IdentificationConfig.IdShowP1;
        item.MiscUse = (short)RandomNumber.Next(3);
        item.Flags |= TreasureConfig.Flags.TrStealth;
        item.SpecialNameId = (byte)SpecialNameIds.SnStealth;
        item.Cost += 500;
    }

    // src/treasure.cpp:678 cursedCloak()
    private static void CursedCloak(ref Inventory item, int level)
    {
        var magicType = RandomNumber.Next(3);

        switch (magicType)
        {
            case 1:
                item.Flags |= TreasureConfig.Flags.TrAggravate;
                item.SpecialNameId = (byte)SpecialNameIds.SnIrritation;
                item.ToAc -= (short)EnchantmentBonus(1, 10, level);
                item.Identification |= IdentificationConfig.IdShowHitDam;
                item.ToHit -= (short)EnchantmentBonus(1, 10, level);
                item.ToDamage -= (short)EnchantmentBonus(1, 10, level);
                item.Cost = 0;
                break;
            case 2:
                item.SpecialNameId = (byte)SpecialNameIds.SnVulnerability;
                item.ToAc -= (short)EnchantmentBonus(10, 100, level + 50);
                item.Cost = 0;
                break;
            default:
                item.SpecialNameId = (byte)SpecialNameIds.SnEnveloping;
                item.ToAc -= (short)EnchantmentBonus(1, 10, level);
                item.Identification |= IdentificationConfig.IdShowHitDam;
                item.ToHit -= (short)EnchantmentBonus(2, 40, level + 10);
                item.ToDamage -= (short)EnchantmentBonus(2, 40, level + 10);
                item.Cost = 0;
                break;
        }

        item.Flags |= TreasureConfig.Flags.TrCursed;
    }

    // src/treasure.cpp:709 magicalChests()
    private static void MagicalChests(ref Inventory item, int level)
    {
        var magicType = RandomNumber.Next(level + 4);

        switch (magicType)
        {
            case 1:
                item.Flags = 0;
                item.SpecialNameId = (byte)SpecialNameIds.SnEmpty;
                break;
            case 2:
                item.Flags |= TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnLocked;
                break;
            case 3:
            case 4:
                item.Flags |= TreasureConfig.Chests.ChLoseStr | TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnPoisonNeedle;
                break;
            case 5:
            case 6:
                item.Flags |= TreasureConfig.Chests.ChPoison | TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnPoisonNeedle;
                break;
            case 7:
            case 8:
            case 9:
                item.Flags |= TreasureConfig.Chests.ChParalysed | TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnGasTrap;
                break;
            case 10:
            case 11:
                item.Flags |= TreasureConfig.Chests.ChExplode | TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnExplosionDevice;
                break;
            case 12:
            case 13:
            case 14:
                item.Flags |= TreasureConfig.Chests.ChSummon | TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnSummoningRunes;
                break;
            case 15:
            case 16:
            case 17:
                item.Flags |= TreasureConfig.Chests.ChParalysed | TreasureConfig.Chests.ChPoison | TreasureConfig.Chests.ChLoseStr |
                              TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnMultipleTraps;
                break;
            default:
                item.Flags |= TreasureConfig.Chests.ChSummon | TreasureConfig.Chests.ChExplode | TreasureConfig.Chests.ChLocked;
                item.SpecialNameId = (byte)SpecialNameIds.SnMultipleTraps;
                break;
        }
    }

    // src/treasure.cpp:762 magicalProjectileAdjustment()
    private static void MagicalProjectileAdjustment(ref Inventory item, int special, int level)
    {
        item.ToHit += (short)EnchantmentBonus(1, 35, level);
        item.ToDamage += (short)EnchantmentBonus(1, 35, level);

        // see comment for weapons
        if (!ShouldBeEnchanted(3 * special / 2))
        {
            return;
        }

        switch (RandomNumber.Next(10))
        {
            case 1:
            case 2:
            case 3:
                item.SpecialNameId = (byte)SpecialNameIds.SnSlaying;
                item.ToHit += 5;
                item.ToDamage += 5;
                item.Cost += 20;
                break;
            case 4:
            case 5:
                item.Flags |= TreasureConfig.Flags.TrFlameTongue;
                item.ToHit += 2;
                item.ToDamage += 4;
                item.SpecialNameId = (byte)SpecialNameIds.SnFire;
                item.Cost += 25;
                break;
            case 6:
            case 7:
                item.Flags |= TreasureConfig.Flags.TrSlayEvil;
                item.ToHit += 3;
                item.ToDamage += 3;
                item.SpecialNameId = (byte)SpecialNameIds.SnSlayEvil;
                item.Cost += 25;
                break;
            case 8:
            case 9:
                item.Flags |= TreasureConfig.Flags.TrSlayAnimal;
                item.ToHit += 2;
                item.ToDamage += 2;
                item.SpecialNameId = (byte)SpecialNameIds.SnSlayAnimal;
                item.Cost += 30;
                break;
            case 10:
                item.Flags |= TreasureConfig.Flags.TrSlayDragon;
                item.ToHit += 3;
                item.ToDamage += 3;
                item.SpecialNameId = (byte)SpecialNameIds.SnDragonSlaying;
                item.Cost += 35;
                break;
        }
    }

    // src/treasure.cpp:814 cursedProjectileAdjustment()
    private static void CursedProjectileAdjustment(ref Inventory item, int level)
    {
        item.ToHit -= (short)EnchantmentBonus(5, 55, level);
        item.ToDamage -= (short)EnchantmentBonus(5, 55, level);
        item.Flags |= TreasureConfig.Flags.TrCursed;
        item.Cost = 0;
    }

    // src/treasure.cpp:825 magicalProjectile()
    private static void MagicalProjectile(ref Inventory item, int special, int level, int chance, int cursed)
    {
        if (item.CategoryId == TreasureCategory.TvSlingAmmo || item.CategoryId == TreasureCategory.TvBolt || item.CategoryId == TreasureCategory.TvArrow)
        {
            // always show to_hit/to_damage values if identified
            item.Identification |= IdentificationConfig.IdShowHitDam;

            if (ShouldBeEnchanted(chance))
            {
                MagicalProjectileAdjustment(ref item, special, level);
            }
            else if (ShouldBeEnchanted(cursed))
            {
                CursedProjectileAdjustment(ref item, level);
            }
        }

        item.ItemsCount = 0;

        for (var i = 0; i < 7; i++)
        {
            item.ItemsCount += (byte)RandomNumber.Next(6);
        }

        if (MissilesCounter == short.MaxValue)
        {
            MissilesCounter = (short)(-short.MaxValue - 1);
        }
        else
        {
            MissilesCounter++;
        }

        item.MiscUse = MissilesCounter;
    }
}
