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
        // Player is a mutable class since M6 (it was a positional record
        // struct in M1); a fresh instance mirrors the C++ `Player_t{}`.
        var player = new Player();
        player.Misc.ClassId = 1;

        var characterClass = DataPlayerData.Classes[player.Misc.ClassId];

        Assert.Equal("Mage", characterClass.Title);
        Assert.Equal(34, player.Inventory.Length);
    }
}
