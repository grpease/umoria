namespace Umoria.Core.Rng;

// Port of src/rng.cpp: a Park-Miller "minimal standard" PMMLCG (Lehmer
// generator) via Schrage's method. Deliberately NOT System.Random -- a
// different algorithm would break seed-reproducible dungeon generation.
//
// Correctness is pinned by RngTests: with seed=1, the 10001st draw must
// equal 1043618065 (the self-test baked into the original rng.cpp).
public static class MoriaRng
{
    private const int M = int.MaxValue; // 2^31 - 1
    private const int A = 16807;
    private const int Q = M / A; // 127773
    private const int R = M % A; // 2836

    private static uint _seed;

    public static uint GetRandomSeed() => _seed;

    // The state at program start (rng.cpp's zero-initialized global), before
    // seedsInitialize() sets a seed. (M15: Game.GameRun, one per run.)
    public static void ResetToProgramStart() => _seed = 0;

    public static void SetRandomSeed(uint seed)
    {
        _seed = (seed % (M - 1)) + 1;
    }

    // Returns a pseudo-random number from the set 1, 2, ..., M - 1.
    public static int Rnd()
    {
        var high = (int)(_seed / Q);
        var low = (int)(_seed % Q);
        var test = (A * low) - (R * high);

        _seed = test > 0 ? (uint)test : (uint)(test + M);

        return (int)_seed;
    }
}
