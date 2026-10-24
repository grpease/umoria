using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Character;

// The subset of src/player.cpp character creation (M6) reaches through
// playerSetAndUseStat(): playerRecalculateBonuses() and its helpers, plus
// the gender accessors. playerAdjustBonusesForItem() (wear/remove), combat,
// movement, searching and resting stay with M8/M9/M11.
public static class PlayerBonuses
{
    // src/player.cpp:33 playerIsMale()
    public static bool IsMale(Player py)
    {
        return py.Misc.Gender;
    }

    // src/player.cpp:41 playerGetGenderLabel() (M12, the character sheet)
    public static string GenderLabel(Player py)
    {
        if (IsMale(py))
        {
            return "Male";
        }

        return "Female";
    }

    // src/player.cpp:1652 playerRankTitle() (M12, the character sheet)
    public static string RankTitle(Player py)
    {
        if (py.Misc.Level < 1)
        {
            return "Babe in arms";
        }

        if (py.Misc.Level <= PlayerConfig.PlayerMaxLevel)
        {
            return Data.Generated.DataPlayerData.ClassRankTitles[py.Misc.ClassId][py.Misc.Level - 1];
        }

        if (IsMale(py))
        {
            return "**KING**";
        }

        return "**QUEEN**";
    }

    // src/player.cpp:37 playerSetGender()
    public static void SetGender(Player py, bool isMale)
    {
        py.Misc.Gender = isMale;
    }

    // src/player.cpp:13 playerResetFlags()
    private static void ResetFlags(Player py)
    {
        var f = py.Flags;
        f.SeeInvisible = false;
        f.Teleport = false;
        f.FreeAction = false;
        f.SlowDigest = false;
        f.Aggravate = false;
        f.SustainStr = false;
        f.SustainInt = false;
        f.SustainWis = false;
        f.SustainCon = false;
        f.SustainDex = false;
        f.SustainChr = false;
        f.ResistantToFire = false;
        f.ResistantToAcid = false;
        f.ResistantToCold = false;
        f.RegenerateHp = false;
        f.ResistantToLight = false;
        f.FreeFall = false;
    }

    // src/player.cpp:424 playerRecalculateBonusesFromInventory()
    private static void RecalculateBonusesFromInventory(Player py)
    {
        var m = py.Misc;

        for (var i = PlayerConfig.Equipment.Wield; i < PlayerConfig.Equipment.Light; i++)
        {
            var item = py.Inventory[i];

            if (item.CategoryId == TreasureCategory.TvNothing)
            {
                continue;
            }

            m.PlussesToHit += item.ToHit;

            // Bows can't damage. -CJS-
            if (item.CategoryId != TreasureCategory.TvBow)
            {
                m.PlussesToDamage += item.ToDamage;
            }

            m.MagicalAc += item.ToAc;
            m.Ac += item.Ac;

            if (ObjectIdentification.SpellItemIdentified(item))
            {
                m.DisplayToHit += item.ToHit;

                // Bows can't damage. -CJS-
                if (item.CategoryId != TreasureCategory.TvBow)
                {
                    m.DisplayToDamage += item.ToDamage;
                }

                m.DisplayToAc += item.ToAc;
                m.DisplayAc += item.Ac;
            }
            else if (!PlayerInventory.ItemIsCursed(item))
            {
                // Base AC values should always be visible,
                // as long as the item is not cursed.
                m.DisplayAc += item.Ac;
            }
        }
    }

    // src/player.cpp:458 playerRecalculateSustainStatsFromInventory()
    private static void RecalculateSustainStatsFromInventory(Player py)
    {
        for (var i = PlayerConfig.Equipment.Wield; i < PlayerConfig.Equipment.Light; i++)
        {
            if ((py.Inventory[i].Flags & TreasureConfig.Flags.TrSustStat) == 0)
            {
                continue;
            }

            switch (py.Inventory[i].MiscUse)
            {
                case 1: py.Flags.SustainStr = true; break;
                case 2: py.Flags.SustainInt = true; break;
                case 3: py.Flags.SustainWis = true; break;
                case 4: py.Flags.SustainCon = true; break;
                case 5: py.Flags.SustainDex = true; break;
                case 6: py.Flags.SustainChr = true; break;
            }
        }
    }

    // src/player.cpp:490 playerRecalculateBonuses() -- recalculate the
    // effect of all the stuff we use. -CJS-
    public static void RecalculateBonuses(Player py)
    {
        var m = py.Misc;
        var f = py.Flags;

        // Temporarily adjust food_digested
        if (f.SlowDigest)
        {
            f.FoodDigested++;
        }
        if (f.RegenerateHp)
        {
            f.FoodDigested -= 3;
        }

        int savedDisplayAc = m.DisplayAc;

        ResetFlags(py);

        // Real values
        m.PlussesToHit = PlayerStatLogic.ToHitAdjustment(py);
        m.PlussesToDamage = PlayerStatLogic.DamageAdjustment(py);
        m.MagicalAc = PlayerStatLogic.ArmorClassAdjustment(py);
        m.Ac = 0;

        // Display values
        m.DisplayToHit = m.PlussesToHit;
        m.DisplayToDamage = m.PlussesToDamage;
        m.DisplayAc = 0;
        m.DisplayToAc = m.MagicalAc;

        RecalculateBonusesFromInventory(py);

        m.DisplayAc += m.DisplayToAc;

        if (py.WeaponIsHeavy)
        {
            m.DisplayToHit += (short)(py.Stats.Used[PlayerConfig.Attr.Str] * 15 - py.Inventory[PlayerConfig.Equipment.Wield].Weight);
        }

        // Add in temporary spell increases
        if (f.Invulnerability > 0)
        {
            m.Ac += 100;
            m.DisplayAc += 100;
        }

        if (f.Blessed > 0)
        {
            m.Ac += 2;
            m.DisplayAc += 2;
        }

        if (f.DetectInvisible > 0)
        {
            f.SeeInvisible = true;
        }

        // can't print AC here because might be in a store
        if (savedDisplayAc != m.DisplayAc)
        {
            f.Status |= PlayerConfig.Status.PyArmor;
        }

        var itemFlags = PlayerInventory.CollectAllItemFlags(py);

        if ((itemFlags & TreasureConfig.Flags.TrSlowDigest) != 0) f.SlowDigest = true;
        if ((itemFlags & TreasureConfig.Flags.TrAggravate) != 0) f.Aggravate = true;
        if ((itemFlags & TreasureConfig.Flags.TrTeleport) != 0) f.Teleport = true;
        if ((itemFlags & TreasureConfig.Flags.TrRegen) != 0) f.RegenerateHp = true;
        if ((itemFlags & TreasureConfig.Flags.TrResFire) != 0) f.ResistantToFire = true;
        if ((itemFlags & TreasureConfig.Flags.TrResAcid) != 0) f.ResistantToAcid = true;
        if ((itemFlags & TreasureConfig.Flags.TrResCold) != 0) f.ResistantToCold = true;
        if ((itemFlags & TreasureConfig.Flags.TrFreeAct) != 0) f.FreeAction = true;
        if ((itemFlags & TreasureConfig.Flags.TrSeeInvis) != 0) f.SeeInvisible = true;
        if ((itemFlags & TreasureConfig.Flags.TrResLight) != 0) f.ResistantToLight = true;
        if ((itemFlags & TreasureConfig.Flags.TrFfall) != 0) f.FreeFall = true;

        RecalculateSustainStatsFromInventory(py);

        // Reset food_digested values
        if (f.SlowDigest)
        {
            f.FoodDigested--;
        }
        if (f.RegenerateHp)
        {
            f.FoodDigested += 3;
        }
    }
}
