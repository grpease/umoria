// Hand-translated from src/player.h's Player_t::stats (not tool-generated).
namespace Umoria.Core.Model.Generated;

// Each array is indexed by PlayerAttr (A_STR, A_INT, A_WIS, A_DEX, A_CON,
// A_CHR), 6 entries.

// M6: changed from a positional `record struct` (M1) to a mutable class with
// zero-valued defaults, matching the C++ `Player_t py{}` global that every
// player function mutates in place -- with struct semantics, nested writes
// like `py.Misc.Level = 1` don't compile (CS1612) and copies silently diverge.
public sealed class PlayerStats
{
    public byte[] Max { get; set; } = new byte[6];
    public byte[] Current { get; set; } = new byte[6];
    public short[] Modified { get; set; } = new short[6];
    public byte[] Used { get; set; } = new byte[6];
}
