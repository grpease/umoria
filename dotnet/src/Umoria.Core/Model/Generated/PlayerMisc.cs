// Hand-translated from src/player.h's Player_t::misc (not tool-generated).
namespace Umoria.Core.Model.Generated;

// The original's fixed char buffers (name[PLAYER_NAME_SIZE], history[4][60])
// become plain strings here; nothing in this milestone depends on their
// exact byte layout.

// M6: changed from a positional `record struct` (M1) to a mutable class with
// zero-valued defaults, matching the C++ `Player_t py{}` global that every
// player function mutates in place -- with struct semantics, nested writes
// like `py.Misc.Level = 1` don't compile (CS1612) and copies silently diverge.
public sealed class PlayerMisc
{
    public string Name { get; set; } = "";
    public bool Gender { get; set; }
    public int DateOfBirth { get; set; }
    public int Au { get; set; }
    public int MaxExp { get; set; }
    public int Exp { get; set; }
    public ushort ExpFraction { get; set; }
    public ushort Age { get; set; }
    public ushort Height { get; set; }
    public ushort Weight { get; set; }
    public ushort Level { get; set; }
    public ushort MaxDungeonDepth { get; set; }
    public short ChanceInSearch { get; set; }
    public short Fos { get; set; }
    public short Bth { get; set; }
    public short BthWithBows { get; set; }
    public short Mana { get; set; }
    public short MaxHp { get; set; }
    public short PlussesToHit { get; set; }
    public short PlussesToDamage { get; set; }
    public short Ac { get; set; }
    public short MagicalAc { get; set; }
    public short DisplayToHit { get; set; }
    public short DisplayToDamage { get; set; }
    public short DisplayAc { get; set; }
    public short DisplayToAc { get; set; }
    public short Disarm { get; set; }
    public short SavingThrow { get; set; }
    public short SocialClass { get; set; }
    public short StealthFactor { get; set; }
    public byte ClassId { get; set; }
    public byte RaceId { get; set; }
    public byte HitDie { get; set; }
    public byte ExperienceFactor { get; set; }
    public short CurrentMana { get; set; }
    public ushort CurrentManaFraction { get; set; }
    public short CurrentHp { get; set; }
    public ushort CurrentHpFraction { get; set; }
    public string[] History { get; set; } = new[] { "", "", "", "" };
}
