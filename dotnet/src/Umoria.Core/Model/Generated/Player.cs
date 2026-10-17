// Hand-translated from src/player.h's Player_t (not tool-generated).
namespace Umoria.Core.Model.Generated;

// The player character. The original's `char prev_dir` (a direction, not
// free text) stays a char; BaseHpLevels/BaseExpLevels are indexed by
// character level (40 entries, PLAYER_MAX_LEVEL); Inventory is the pack +
// equipped items (34 slots, PLAYER_INVENTORY_SIZE -- see PlayerEquipment in
// src/inventory.h for the equipped-slot indices).

// M6: changed from a positional `record struct` (M1) to a mutable class with
// zero-valued defaults, matching the C++ `Player_t py{}` global that every
// player function mutates in place -- with struct semantics, nested writes
// like `py.Misc.Level = 1` don't compile (CS1612) and copies silently diverge.
public sealed class Player
{
    public PlayerMisc Misc { get; set; } = new();
    public PlayerStats Stats { get; set; } = new();
    public PlayerFlags Flags { get; set; } = new();
    public Coord Pos { get; set; }
    public char PrevDir { get; set; } = ' ';
    public ushort[] BaseHpLevels { get; set; } = new ushort[40];
    public uint[] BaseExpLevels { get; set; } = new uint[40];
    public byte RunningTracker { get; set; }
    public bool TemporaryLightOnly { get; set; }
    public int MaxScore { get; set; }
    public PlayerPack Pack { get; set; } = new();
    public Inventory[] Inventory { get; set; } = CreateEmptyInventory();
    public short EquipmentCount { get; set; }
    public bool WeaponIsHeavy { get; set; }
    public bool CarryingLight { get; set; }

    // PLAYER_INVENTORY_SIZE (src/inventory.h) zero-initialized slots, as in
    // `Player_t{}`. Inscription is "" rather than null to match the C++
    // empty char buffer.
    private static Inventory[] CreateEmptyInventory()
    {
        var inventory = new Inventory[34];
        for (var i = 0; i < inventory.Length; i++)
        {
            inventory[i] = default(Inventory) with { Inscription = "" };
        }

        return inventory;
    }
}
