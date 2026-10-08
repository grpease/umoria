using Umoria.Core.Data.Generated;

namespace Umoria.Core.Rng;

// Port of randomNumber()/randomNumberNormalDistribution() from src/game.cpp.
// They live here, not in MoriaRng, to mirror the original file boundary
// (the C++ functions are defined in game.cpp, not rng.cpp) while keeping
// both RNG-dependent pieces together on the C# side.
public static class RandomNumber
{
    private const int NormalTableSd = 64;
    private const int ShortMax = short.MaxValue; // 32767

    // Generates a random integer x where 1 <= x <= max.
    public static int Next(int max)
    {
        return (MoriaRng.Rnd() % max) + 1;
    }

    // Generates a random integer of normal distribution around mean, using
    // normal_table (SD=64) for a fast approximation instead of a true
    // transcendental normal distribution.
    public static int NextNormal(int mean, int standard)
    {
        var tmp = Next(ShortMax);

        // off scale: assign a random value between 4 and 5 times SD
        if (tmp == ShortMax)
        {
            var scaleOffset = (4 * standard) + Next(standard);

            // one half are negative
            if (Next(2) == 1)
            {
                scaleOffset = -scaleOffset;
            }

            return mean + scaleOffset;
        }

        // binary search normal_table to get the index that matches tmp;
        // this takes up to 8 iterations.
        var table = DataTables.NormalTable;
        var low = 0;
        var index = table.Length >> 1;
        var high = table.Length;

        while (true)
        {
            if (table[index] == tmp || high == low + 1)
            {
                break;
            }

            if (table[index] > tmp)
            {
                high = index;
                index = low + ((index - low) >> 1);
            }
            else
            {
                low = index;
                index += (high - index) >> 1;
            }
        }

        // might end up one below target, check that here
        if (table[index] < tmp)
        {
            index += 1;
        }

        // normal_table is based on SD of 64, so adjust the index value
        // here, rounding the half-way case up.
        var offset = ((standard * index) + (NormalTableSd >> 1)) / NormalTableSd;

        // one half should be negative
        if (Next(2) == 1)
        {
            offset = -offset;
        }

        return mean + offset;
    }
}
