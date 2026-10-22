namespace Umoria.Core;

// src/helpers.cpp -- the bit-twiddling helper shared by monster spell
// selection (M9) and spell-book reading (M10), and the string helpers item
// descriptions use (M11).
public static class Helpers
{
    // src/types.h MORIA_MESSAGE_SIZE / MORIA_OBJ_DESC_SIZE (M11)
    public const int MoriaMessageSize = 80;
    public const int MoriaObjDescSize = 160;

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

    // src/helpers.cpp:63 insertStringIntoString() -- replaces the first
    // occurrence of `fromString` in `toString` with `strToInsert` (null
    // removes it). (M11)
    public static string InsertStringIntoString(string toString, string fromString, string? strToInsert)
    {
        var at = toString.IndexOf(fromString, StringComparison.Ordinal);
        if (at < 0)
        {
            return toString;
        }

        return toString[..at] + strToInsert + toString[(at + fromString.Length)..];
    }

    // src/helpers.cpp:101 isVowel() (M11)
    public static bool IsVowel(char ch)
    {
        switch (ch)
        {
            case 'a':
            case 'e':
            case 'i':
            case 'o':
            case 'u':
            case 'A':
            case 'E':
            case 'I':
            case 'O':
            case 'U':
                return true;
            default:
                return false;
        }
    }

    // What snprintf(buffer, size, ...) keeps of `text`: at most size - 1
    // characters. The original builds its messages in fixed vtype_t (80) and
    // obj_desc_t (160) buffers, so a long one really is cut short. (M11)
    public static string Truncate(string text, int size)
    {
        return text.Length < size ? text : text[..(size - 1)];
    }
}
