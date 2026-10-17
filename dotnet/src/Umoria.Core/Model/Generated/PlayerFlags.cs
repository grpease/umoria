// Hand-translated from src/player.h's Player_t::flags (not tool-generated).
namespace Umoria.Core.Model.Generated;

// M6: changed from a positional `record struct` (M1) to a mutable class with
// zero-valued defaults, matching the C++ `Player_t py{}` global that every
// player function mutates in place -- with struct semantics, nested writes
// like `py.Misc.Level = 1` don't compile (CS1612) and copies silently diverge.
public sealed class PlayerFlags
{
    public uint Status { get; set; }
    public short Rest { get; set; }
    public short Blind { get; set; }
    public short Paralysis { get; set; }
    public short Confused { get; set; }
    public short Food { get; set; }
    public short FoodDigested { get; set; }
    public short Protection { get; set; }
    public short Speed { get; set; }
    public short Fast { get; set; }
    public short Slow { get; set; }
    public short Afraid { get; set; }
    public short Poisoned { get; set; }
    public short Image { get; set; }
    public short ProtectEvil { get; set; }
    public short Invulnerability { get; set; }
    public short Heroism { get; set; }
    public short SuperHeroism { get; set; }
    public short Blessed { get; set; }
    public short HeatResistance { get; set; }
    public short ColdResistance { get; set; }
    public short DetectInvisible { get; set; }
    public short WordOfRecall { get; set; }
    public short SeeInfra { get; set; }
    public short TimedInfra { get; set; }
    public bool SeeInvisible { get; set; }
    public bool Teleport { get; set; }
    public bool FreeAction { get; set; }
    public bool SlowDigest { get; set; }
    public bool Aggravate { get; set; }
    public bool ResistantToFire { get; set; }
    public bool ResistantToCold { get; set; }
    public bool ResistantToAcid { get; set; }
    public bool RegenerateHp { get; set; }
    public bool ResistantToLight { get; set; }
    public bool FreeFall { get; set; }
    public bool SustainStr { get; set; }
    public bool SustainInt { get; set; }
    public bool SustainWis { get; set; }
    public bool SustainCon { get; set; }
    public bool SustainDex { get; set; }
    public bool SustainChr { get; set; }
    public bool ConfuseMonster { get; set; }
    public byte NewSpellsToLearn { get; set; }
    public uint SpellsLearnt { get; set; }
    public uint SpellsWorked { get; set; }
    public uint SpellsForgotten { get; set; }
    public byte[] SpellsLearnedOrder { get; set; } = new byte[32];
}
