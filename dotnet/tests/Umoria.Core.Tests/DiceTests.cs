using Xunit;

namespace Umoria.Core.Tests;

public class DiceTests
{
    [Fact]
    public void MaxRoll_IsCountTimesSides()
    {
        var dice = new Umoria.Core.Model.Generated.Dice(3, 6);

        Assert.Equal(18, Umoria.Core.Dice.DiceRoller.MaxRoll(dice));
    }

    [Fact]
    public void Roll_IsWithinCountAndMaxRollInclusive()
    {
        Umoria.Core.Rng.MoriaRng.SetRandomSeed(42);
        var dice = new Umoria.Core.Model.Generated.Dice(2, 6);

        for (var i = 0; i < 1000; i++)
        {
            var result = Umoria.Core.Dice.DiceRoller.Roll(dice);
            Assert.InRange(result, dice.Count, Umoria.Core.Dice.DiceRoller.MaxRoll(dice));
        }
    }
}
