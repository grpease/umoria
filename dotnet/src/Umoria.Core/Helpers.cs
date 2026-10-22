namespace Umoria.Core;

// src/helpers.cpp -- the bit-twiddling helper shared by monster spell
// selection (M9) and spell-book reading (M10).
public static class Helpers
{
    // src/helpers.cpp:10 getAndClearFirstBit() -- returns position of first
    // set bit and clears that bit -RAK-
    public static int GetAndClearFirstBit(ref uint flag)
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
