using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Dice;

// Port of src/dice.cpp.
public static class DiceRoller
{
    // Generates damage for "NdS" style dice rolls (e.g. 2d6).
    public static int Roll(Model.Generated.Dice dice)
    {
        var sum = 0;
        for (var i = 0; i < dice.Count; i++)
        {
            sum += RandomNumber.Next(dice.Sides);
        }

        return sum;
    }

    public static int MaxRoll(Model.Generated.Dice dice)
    {
        return dice.Count * dice.Sides;
    }
}
