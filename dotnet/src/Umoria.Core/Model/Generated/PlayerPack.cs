// Hand-translated from src/player.h's Player_t::pack (not tool-generated).
namespace Umoria.Core.Model.Generated;

// M6: changed from a positional `record struct` (M1) to a mutable class with
// zero-valued defaults, matching the C++ `Player_t py{}` global that every
// player function mutates in place -- with struct semantics, nested writes
// like `py.Misc.Level = 1` don't compile (CS1612) and copies silently diverge.
public sealed class PlayerPack
{
    public short UniqueItems { get; set; }
    public short Weight { get; set; }
    public short Heaviness { get; set; }
}
