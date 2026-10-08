using Xunit;

// MoriaRng mirrors the original C++ codebase's single global RNG state
// (rnd_seed is a lone static in rng.cpp) and several tests depend on
// controlling that shared state via SetRandomSeed(). Disable xUnit's
// default cross-class parallelization so those tests can't interleave.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
