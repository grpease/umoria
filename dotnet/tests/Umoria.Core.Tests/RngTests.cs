using Umoria.Core.Rng;
using Xunit;

namespace Umoria.Core.Tests;

public class RngTests
{
    // Mirrors the #ifdef TEST_RNG self-check in src/rng.cpp: setRandomSeed(0)
    // maps to internal state z[1]=1 (SetRandomSeed(0) -> (0 % (M-1)) + 1 ==
    // 1), then discard 9999 draws -- the 10001st draw must equal 1043618065.
    // This is the cheapest possible validation that the ported PMMLCG
    // produces bit-identical output to the original C++ generator.
    [Fact]
    public void SelfTest_Seed1_10001stDraw_Matches1043618065()
    {
        MoriaRng.SetRandomSeed(0);

        for (var i = 1; i < 10000; i++)
        {
            MoriaRng.Rnd();
        }

        var result = MoriaRng.Rnd();

        Assert.Equal(1043618065, result);
    }
}
