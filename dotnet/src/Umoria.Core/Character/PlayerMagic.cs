using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// src/player_magic.cpp's timed-effect helpers -- the cures, protection from
// evil, bless and detect invisible -- reached from the mage/priest casting
// layer (M10) and later from potions/scrolls (M11). itemMagicAbilityDamage(),
// the file's other function, is PlayerCombat's (M9).
public static class PlayerMagic
{
    // src/player_magic.cpp:11 playerCureConfusion() -- cure players
    // confusion -RAK-
    public static bool CureConfusion(Player py)
    {
        if (py.Flags.Confused > 1)
        {
            py.Flags.Confused = 1;
            return true;
        }
        return false;
    }

    // src/player_magic.cpp:20 playerCureBlindness() -- cure players
    // blindness -RAK-
    public static bool CureBlindness(Player py)
    {
        if (py.Flags.Blind > 1)
        {
            py.Flags.Blind = 1;
            return true;
        }
        return false;
    }

    // src/player_magic.cpp:29 playerCurePoison() -- cure poisoning -RAK-
    public static bool CurePoison(Player py)
    {
        if (py.Flags.Poisoned > 1)
        {
            py.Flags.Poisoned = 1;
            return true;
        }
        return false;
    }

    // src/player_magic.cpp:38 playerRemoveFear() -- cure the players fear
    // -RAK-
    public static bool RemoveFear(Player py)
    {
        if (py.Flags.Afraid > 1)
        {
            py.Flags.Afraid = 1;
            return true;
        }
        return false;
    }

    // src/player_magic.cpp:47 playerProtectEvil() -- evil creatures don't
    // like this. -RAK-
    public static bool ProtectEvil(Player py)
    {
        var isProtected = py.Flags.ProtectEvil == 0;

        py.Flags.ProtectEvil = (short)(py.Flags.ProtectEvil + RandomNumber.Next(25) + 3 * py.Misc.Level);

        return isProtected;
    }

    // src/player_magic.cpp:56 playerBless() -- bless -RAK-
    public static void Bless(Player py, int adjustment)
    {
        py.Flags.Blessed = (short)(py.Flags.Blessed + adjustment);
    }

    // src/player_magic.cpp:61 playerDetectInvisible() -- detect invisible
    // for period of time -RAK-
    public static void DetectInvisible(Player py, int adjustment)
    {
        py.Flags.DetectInvisible = (short)(py.Flags.DetectInvisible + adjustment);
    }
}
