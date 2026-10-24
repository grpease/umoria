namespace Umoria.Core;

// src/helpers.cpp -- the bit-twiddling helper shared by monster spell
// selection (M9) and spell-book reading (M10), and the string helpers item
// descriptions use (M11), and the number helpers store haggling uses
// (M13).
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

    // src/helpers.cpp:26 insertNumberIntoString() -- replaces the first
    // occurrence of `fromString` in `toString` with the number (a leading
    // '+' when `showSign` and it isn't negative), cut to a vtype_t. (M13)
    public static string InsertNumberIntoString(string toString, string fromString, int number, bool showSign)
    {
        var at = toString.IndexOf(fromString, StringComparison.Ordinal);
        if (at < 0)
        {
            return toString;
        }

        var sign = number >= 0 && showSign ? "+" : "";
        return Truncate(toString[..at] + sign + number + toString[(at + fromString.Length)..], MoriaMessageSize);
    }

    // src/helpers.cpp:120 stringToNumber() -- strtol() in base 10: leading
    // white space, an optional sign, digits, and nothing after them. On
    // success `number` is set and true returned; on anything else (out of
    // range, trailing garbage, a sign with no digits) `number` is left as it
    // was. An empty string is a success with 0 (strtol() converts nothing
    // and stops at the end). Out of range is a 32-bit long's, as on the
    // MSVC build the baselines come from (a 64-bit long would accept more
    // and truncate). (M13)
    public static bool StringToNumber(string str, ref int number)
    {
        var i = 0;
        while (i < str.Length && (str[i] == ' ' || (str[i] >= '\t' && str[i] <= '\r')))
        {
            i++;
        }

        var negative = false;
        if (i < str.Length && (str[i] == '+' || str[i] == '-'))
        {
            negative = str[i] == '-';
            i++;
        }

        var digitsStart = i;
        long value = 0;
        var outOfRange = false;
        while (i < str.Length && str[i] >= '0' && str[i] <= '9')
        {
            if (!outOfRange)
            {
                value = value * 10 + (str[i] - '0');
                if (value > (long)int.MaxValue + 1)
                {
                    outOfRange = true;
                }
            }
            i++;
        }

        if (i == digitsStart)
        {
            // no conversion: strtol() points back at the start of the string
            if (str.Length != 0)
            {
                return false;
            }

            number = 0;
            return true;
        }

        if (negative)
        {
            value = -value;
        }

        if (outOfRange || value > int.MaxValue || value < int.MinValue)
        {
            return false;
        }

        // garbage at end of string
        if (i != str.Length)
        {
            return false;
        }

        number = (int)value;
        return true;
    }

    // What snprintf(buffer, size, ...) keeps of `text`: at most size - 1
    // characters. The original builds its messages in fixed vtype_t (80) and
    // obj_desc_t (160) buffers, so a long one really is cut short. (M11)
    public static string Truncate(string text, int size)
    {
        return text.Length < size ? text : text[..(size - 1)];
    }
}
