using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Tests.ModelTests;

// M1: Monster/Inventory/Player are pure data shapes with no behavior yet --
// these tests only confirm they construct correctly and that their index
// fields (CreatureId, Id, ClassId, ...) line up with M0's real, verified
// data tables, using hand-checked values from the original C++ source
// (same discipline as the golden-trace/data-table harnesses: never trust an
// assumed value, confirm it against src/).
public class PlayerModelTests
{
    [Fact]
    public void Monster_CreatureId_IndexesCreaturesList()
    {
        // src/data_creatures.cpp row 0.
        var monster = new Monster(Hp: 4, SleepCount: 0, Speed: 11, CreatureId: 0,
            Pos: new Coord(10, 20), DistanceFromPlayer: 0, Lit: false, StunnedAmount: 0, ConfusedAmount: 0);

        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        Assert.Equal("Filthy Street Urchin", creature.Name);
        Assert.Equal(11, creature.Speed);
    }

    [Fact]
    public void Inventory_Id_IndexesGameObjects_Row367IsOpenDoor()
    {
        // src/data_treasure.cpp:445 -- `{"& open door", ..., TV_OPEN_DOOR, ...}, // 367`
        var item = new Inventory(Id: 367, SpecialNameId: 0, Inscription: "", Flags: 0, CategoryId: 0,
            Sprite: 0, MiscUse: 0, Cost: 0, SubCategoryId: 0, ItemsCount: 1, Weight: 0, ToHit: 0,
            ToDamage: 0, Ac: 0, ToAc: 0, Damage: new Umoria.Core.Model.Generated.Dice(0, 0), DepthFirstFound: 0, Identification: 0);

        var template = DataTreasureData.GameObjects[item.Id];

        Assert.Equal("& open door", template.Name);
        Assert.Equal(104, template.CategoryId); // TV_OPEN_DOOR, src/treasure.h
    }

    [Fact]
    public void Player_Misc_ClassId_IndexesClasses()
    {
        // src/data_player.cpp row 1 ("Mage").
        var player = new Player(
            Misc: new PlayerMisc(Name: "Test", Gender: true, DateOfBirth: 0, Au: 0, MaxExp: 0, Exp: 0,
                ExpFraction: 0, Age: 0, Height: 0, Weight: 0, Level: 1, MaxDungeonDepth: 0,
                ChanceInSearch: 0, Fos: 0, Bth: 0, BthWithBows: 0, Mana: 0, MaxHp: 0, PlussesToHit: 0,
                PlussesToDamage: 0, Ac: 0, MagicalAc: 0, DisplayToHit: 0, DisplayToDamage: 0, DisplayAc: 0,
                DisplayToAc: 0, Disarm: 0, SavingThrow: 0, SocialClass: 0, StealthFactor: 0, ClassId: 1,
                RaceId: 0, HitDie: 0, ExperienceFactor: 0, CurrentMana: 0, CurrentManaFraction: 0,
                CurrentHp: 0, CurrentHpFraction: 0, History: new string[4]),
            Stats: new PlayerStats(new byte[6], new byte[6], new short[6], new byte[6]),
            Flags: new PlayerFlags(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, false, false, false, false, false, false, false, false, false, false, false, false,
                false, false, false, false, false, false, 0, 0, 0, 0, new byte[32]),
            Pos: new Coord(0, 0), PrevDir: ' ', BaseHpLevels: new ushort[40], BaseExpLevels: new uint[40],
            RunningTracker: 0, TemporaryLightOnly: false, MaxScore: 0, Pack: new PlayerPack(0, 0, 0),
            Inventory: new Inventory[34], // PLAYER_INVENTORY_SIZE, src/inventory.h
            EquipmentCount: 0, WeaponIsHeavy: false, CarryingLight: false);

        var characterClass = DataPlayerData.Classes[player.Misc.ClassId];

        Assert.Equal("Mage", characterClass.Title);
        Assert.Equal(34, player.Inventory.Length);
    }
}
