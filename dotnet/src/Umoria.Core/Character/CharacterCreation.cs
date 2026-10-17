using System.Text;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// The answers to characterCreate()'s interactive prompts, in the order the
// original asks for them. ClassChoice is the menu position the player picks
// (the 'a'/'b'/... key), i.e. an index into AvailableClasses(RaceId) -- not
// a raw class id, since each race only offers some classes.
public sealed record CharacterChoices(
    int RaceId,
    bool IsMale,
    int ClassChoice,
    string Name,
    int Rerolls = 0);

// Port of src/character.cpp (M6), plus the new-character tail of
// startMoria() (src/game_run.cpp): initializeCharacterInventory(), food,
// and the initial spell/mana setup.
//
// The original interleaves its RNG-consuming logic with curses prompts
// (race/gender/class menus, a space-to-re-roll loop, name entry). Here the
// prompts' answers come in as CharacterChoices instead, and the logic runs
// in exactly the original's order -- including re-running stats/history/
// age-height-weight once per re-roll, since each re-roll consumes RNG.
// The display-only pieces (displayCharacterRaces, displayCharacterHistory,
// the print* calls, help screens) are left to the UI milestone (M14).
//
// Verified byte-for-byte against the real characterCreate() via
// tools/char_create_dump (see CharCreateTraceTests).
public static class CharacterCreation
{
    // src/character.cpp:474 characterCreate()
    public static void Create(Player py, CharacterChoices choices)
    {
        // characterChooseRace()
        if (choices.RaceId < 0 || choices.RaceId >= PlayerConfig.PlayerMaxRaces)
        {
            throw new ArgumentOutOfRangeException(nameof(choices), $"race id {choices.RaceId} out of range");
        }
        py.Misc.RaceId = (byte)choices.RaceId;

        // characterSetGender()
        PlayerBonuses.SetGender(py, choices.IsMale);

        // here we start a loop giving a player a choice of characters -RGM-
        for (var roll = 0; roll <= choices.Rerolls; roll++)
        {
            GenerateStatsAndRace(py);
            GetHistory(py);
            SetAgeHeightWeight(py);
        }

        GetClass(py, choices.ClassChoice);
        CalculateStartGold(py);
        py.Misc.Name = ResolveName(choices.Name);
    }

    // The new-character branch of startMoria() (src/game_run.cpp), run right
    // after characterCreate(). DateOfBirth is wall-clock time, as in the
    // original.
    public static void InitializeNewCharacter(Player py, ObjectIdentification identification)
    {
        py.Misc.DateOfBirth = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        InitializeCharacterInventory(py, identification);
        py.Flags.Food = 7500;
        py.Flags.FoodDigested = 2;

        // Spell and Mana based on class: Mage or Clerical realm.
        var spellType = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells;
        if (spellType == SpellConfig.SpellTypeMage)
        {
            PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Int);
            PlayerSpells.GainMana(py, PlayerConfig.Attr.Int);
        }
        else if (spellType == SpellConfig.SpellTypePriest)
        {
            PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Wis);
            PlayerSpells.GainMana(py, PlayerConfig.Attr.Wis);
        }

        // Set some default values -MRC-
        py.TemporaryLightOnly = false;
        py.WeaponIsHeavy = false;
        py.Pack.Heaviness = 0;
    }

    // The class ids a race may choose, in the order displayRaceClasses()
    // (src/character.cpp) lists them -- CharacterChoices.ClassChoice indexes
    // into this.
    public static IReadOnlyList<int> AvailableClasses(int raceId)
    {
        var classes = new List<int>();
        uint mask = 0x1;

        for (var i = 0; i < PlayerConfig.PlayerMaxClasses; i++)
        {
            if ((DataPlayerData.CharacterRaces[raceId].ClassesBitField & mask) != 0)
            {
                classes.Add(i);
            }
            mask <<= 1;
        }

        return classes;
    }

    // src/character.cpp:11 characterGenerateStats() -- generates character's
    // stats. -JWT-
    private static void GenerateStats(Player py)
    {
        int total;
        var dice = new int[18];

        do
        {
            total = 0;
            for (var i = 0; i < 18; i++)
            {
                // Roll 3,4,5 sided dice once each
                dice[i] = RandomNumber.Next(3 + i % 3);
                total += dice[i];
            }
        } while (total <= 42 || total >= 54);

        for (var i = 0; i < 6; i++)
        {
            py.Stats.Max[i] = (byte)(5 + dice[3 * i] + dice[3 * i + 1] + dice[3 * i + 2]);
        }
    }

    // src/character.cpp:30 decrementStat(). Byte arithmetic is deliberate:
    // the original's uint8_t wraps the same way.
    private static byte DecrementStat(short adjustment, byte currentStat)
    {
        var stat = currentStat;
        for (var i = 0; i > adjustment; i--)
        {
            if (stat > 108)
            {
                stat--;
            }
            else if (stat > 88)
            {
                stat = (byte)(stat + (-RandomNumber.Next(6) - 2));
            }
            else if (stat > 18)
            {
                stat = (byte)(stat + (-RandomNumber.Next(15) - 5));
                if (stat < 18)
                {
                    stat = 18;
                }
            }
            else if (stat > 3)
            {
                stat--;
            }
        }
        return stat;
    }

    // src/character.cpp:49 incrementStat()
    private static byte IncrementStat(short adjustment, byte currentStat)
    {
        var stat = currentStat;
        for (var i = 0; i < adjustment; i++)
        {
            if (stat < 18)
            {
                stat++;
            }
            else if (stat < 88)
            {
                stat = (byte)(stat + RandomNumber.Next(15) + 5);
            }
            else if (stat < 108)
            {
                stat = (byte)(stat + RandomNumber.Next(6) + 2);
            }
            else if (stat < 118)
            {
                stat++;
            }
        }
        return stat;
    }

    // src/character.cpp:69 createModifyPlayerStat() -- changes stats by given
    // amount, with a little randomness. -JWT-
    private static byte CreateModifyPlayerStat(byte stat, short adjustment)
    {
        return adjustment < 0 ? DecrementStat(adjustment, stat) : IncrementStat(adjustment, stat);
    }

    private static void ApplyStatAdjustments(Player py, short str, short intel, short wis, short dex, short con, short chr)
    {
        var max = py.Stats.Max;
        max[PlayerConfig.Attr.Str] = CreateModifyPlayerStat(max[PlayerConfig.Attr.Str], str);
        max[PlayerConfig.Attr.Int] = CreateModifyPlayerStat(max[PlayerConfig.Attr.Int], intel);
        max[PlayerConfig.Attr.Wis] = CreateModifyPlayerStat(max[PlayerConfig.Attr.Wis], wis);
        max[PlayerConfig.Attr.Dex] = CreateModifyPlayerStat(max[PlayerConfig.Attr.Dex], dex);
        max[PlayerConfig.Attr.Con] = CreateModifyPlayerStat(max[PlayerConfig.Attr.Con], con);
        max[PlayerConfig.Attr.Chr] = CreateModifyPlayerStat(max[PlayerConfig.Attr.Chr], chr);
    }

    private static void SetAndUseAllStats(Player py)
    {
        for (var i = 0; i < 6; i++)
        {
            py.Stats.Current[i] = py.Stats.Max[i];
            PlayerStatLogic.SetAndUseStat(py, i);
        }
    }

    // src/character.cpp:76 characterGenerateStatsAndRace()
    private static void GenerateStatsAndRace(Player py)
    {
        var race = DataPlayerData.CharacterRaces[py.Misc.RaceId];
        var m = py.Misc;

        GenerateStats(py);
        ApplyStatAdjustments(py, race.StrAdjustment, race.IntAdjustment, race.WisAdjustment,
            race.DexAdjustment, race.ConAdjustment, race.ChrAdjustment);

        m.Level = 1;

        SetAndUseAllStats(py);

        m.ChanceInSearch = race.SearchChanceBase;
        m.Bth = race.BaseToHit;
        m.BthWithBows = race.BaseToHitBows;
        m.Fos = race.Fos;
        m.StealthFactor = race.Stealth;
        m.SavingThrow = race.SavingThrowBase;
        m.HitDie = race.HitPointsBase;
        m.PlussesToDamage = PlayerStatLogic.DamageAdjustment(py);
        m.PlussesToHit = PlayerStatLogic.ToHitAdjustment(py);
        m.MagicalAc = 0;
        m.Ac = PlayerStatLogic.ArmorClassAdjustment(py);
        m.ExperienceFactor = race.ExpFactorBase;
        py.Flags.SeeInfra = race.InfraVision;
    }

    // src/character.cpp:173 characterGetHistory() -- get the racial history,
    // determines social class. -RAK-
    //
    // Assumptions (from the original):
    //   - Each race has init history beginning at (race-1)*3+1
    //   - All history parts are in ascending order
    private static void GetHistory(Player py)
    {
        var backgrounds = DataPlayerData.CharacterBackgrounds;

        var historyId = py.Misc.RaceId * 3 + 1;
        var socialClass = RandomNumber.Next(4);

        var block = new StringBuilder();

        var backgroundId = 0;

        // Get a block of history text
        do
        {
            var found = false;
            while (!found)
            {
                if (backgrounds[backgroundId].Chart == historyId)
                {
                    var testRoll = RandomNumber.Next(100);

                    while (testRoll > backgrounds[backgroundId].Roll)
                    {
                        backgroundId++;
                    }

                    var background = backgrounds[backgroundId];

                    block.Append(background.Info);
                    socialClass += background.Bonus - 50;

                    if (historyId > background.Next)
                    {
                        backgroundId = 0;
                    }

                    historyId = background.Next;
                    found = true;
                }
                else
                {
                    backgroundId++;
                }
            }
        } while (historyId >= 1);

        // playerClearHistory()
        for (var i = 0; i < py.Misc.History.Length; i++)
        {
            py.Misc.History[i] = "";
        }

        // Process block of history text for pretty output. Kept index-for-
        // index with the original's char-buffer walk; At() returns '\0' past
        // the end, like reading a C string's terminator.
        var text = block.ToString();
        char At(int i) => i >= 0 && i < text.Length ? text[i] : '\0';

        var cursorStart = 0;
        var cursorEnd = text.Length - 1;
        while (At(cursorEnd) == ' ')
        {
            cursorEnd--;
        }

        var lineNumber = 0;
        var newCursorStart = 0;

        var done = false;
        while (!done)
        {
            while (At(cursorStart) == ' ')
            {
                cursorStart++;
            }

            var currentCursorPosition = cursorEnd - cursorStart + 1;

            if (currentCursorPosition > 60)
            {
                currentCursorPosition = 60;

                while (At(cursorStart + currentCursorPosition - 1) != ' ')
                {
                    currentCursorPosition--;
                }

                newCursorStart = cursorStart + currentCursorPosition;

                while (At(cursorStart + currentCursorPosition - 1) == ' ')
                {
                    currentCursorPosition--;
                }
            }
            else
            {
                done = true;
            }

            py.Misc.History[lineNumber] = text.Substring(cursorStart, currentCursorPosition);

            lineNumber++;
            cursorStart = newCursorStart;
        }

        // Compute social class for player
        if (socialClass > 100)
        {
            socialClass = 100;
        }
        else if (socialClass < 1)
        {
            socialClass = 1;
        }

        py.Misc.SocialClass = (short)socialClass;
    }

    // src/character.cpp:290 characterSetAgeHeightWeight() -- computes
    // character's age, height, and weight. -JWT-
    private static void SetAgeHeightWeight(Player py)
    {
        var race = DataPlayerData.CharacterRaces[py.Misc.RaceId];

        py.Misc.Age = (ushort)(race.BaseAge + RandomNumber.Next(race.MaxAge));

        int heightBase, heightMod, weightBase, weightMod;
        if (PlayerBonuses.IsMale(py))
        {
            heightBase = race.MaleHeightBase;
            heightMod = race.MaleHeightMod;
            weightBase = race.MaleWeightBase;
            weightMod = race.MaleWeightMod;
        }
        else
        {
            heightBase = race.FemaleHeightBase;
            heightMod = race.FemaleHeightMod;
            weightBase = race.FemaleWeightBase;
            weightMod = race.FemaleWeightMod;
        }

        py.Misc.Height = (ushort)RandomNumber.NextNormal(heightBase, heightMod);
        py.Misc.Weight = (ushort)RandomNumber.NextNormal(weightBase, weightMod);
        py.Misc.Disarm = (short)(race.DisarmChanceBase + PlayerStatLogic.DisarmAdjustment(py));
    }

    // src/character.cpp:415 characterGetClass(), minus the menu itself.
    private static void GetClass(Player py, int classChoice)
    {
        var classList = AvailableClasses(py.Misc.RaceId);

        // Reset the class ID
        py.Misc.ClassId = 0;

        if (classChoice < 0 || classChoice >= classList.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(classChoice),
                $"class choice {classChoice} out of range; race {py.Misc.RaceId} offers {classList.Count} classes");
        }

        GenerateCharacterClass(py, (byte)classList[classChoice]);
    }

    // src/character.cpp:342 generateCharacterClass()
    private static void GenerateCharacterClass(Player py, byte classId)
    {
        var m = py.Misc;
        m.ClassId = classId;

        var klass = DataPlayerData.Classes[m.ClassId];

        // Adjust the stats for the class adjustment -RAK-
        ApplyStatAdjustments(py, klass.Strength, klass.Intelligence, klass.Wisdom,
            klass.Dexterity, klass.Constitution, klass.Charisma);

        SetAndUseAllStats(py);

        // Real values
        m.PlussesToDamage = PlayerStatLogic.DamageAdjustment(py);
        m.PlussesToHit = PlayerStatLogic.ToHitAdjustment(py);
        m.MagicalAc = PlayerStatLogic.ArmorClassAdjustment(py);
        m.Ac = 0;

        // Displayed values
        m.DisplayToDamage = m.PlussesToDamage;
        m.DisplayToHit = m.PlussesToHit;
        m.DisplayToAc = m.MagicalAc;
        m.DisplayAc = (short)(m.Ac + m.DisplayToAc);

        // now set misc stats, do this after setting stats because of
        // playerStatAdjustmentConstitution() for hit-points
        m.HitDie += klass.HitPoints;
        m.MaxHp = (short)(PlayerStatLogic.StatAdjustmentConstitution(py) + m.HitDie);
        m.CurrentHp = m.MaxHp;
        m.CurrentHpFraction = 0;

        // Initialize hit_points array.
        // Put bounds on total possible hp, only succeed
        // if it is within 1/8 of average value.
        const int maxLevel = PlayerConfig.PlayerMaxLevel;
        var minValue = (maxLevel * 3 / 8 * (m.HitDie - 1)) + maxLevel;
        var maxValue = (maxLevel * 5 / 8 * (m.HitDie - 1)) + maxLevel;
        py.BaseHpLevels[0] = m.HitDie;

        do
        {
            for (var i = 1; i < maxLevel; i++)
            {
                py.BaseHpLevels[i] = (ushort)RandomNumber.Next(m.HitDie);
                py.BaseHpLevels[i] += py.BaseHpLevels[i - 1];
            }
        } while (py.BaseHpLevels[maxLevel - 1] < minValue || py.BaseHpLevels[maxLevel - 1] > maxValue);

        m.Bth += klass.BaseToHit;
        m.BthWithBows += klass.BaseToHitWithBows; // RAK
        m.ChanceInSearch += klass.Searching;
        m.Disarm += klass.DisarmTraps;
        m.Fos += klass.Fos;
        m.StealthFactor += klass.Stealth;
        m.SavingThrow += klass.SavingThrow;
        m.ExperienceFactor += klass.ExperienceFactor;
    }

    // src/character.cpp:439 monetaryValueCalculatedFromStat()
    private static int MonetaryValueCalculatedFromStat(byte stat)
    {
        return 5 * (stat - 10);
    }

    // src/character.cpp:443 playerCalculateStartGold()
    private static void CalculateStartGold(Player py)
    {
        var max = py.Stats.Max;

        var value = MonetaryValueCalculatedFromStat(max[PlayerConfig.Attr.Str]);
        value += MonetaryValueCalculatedFromStat(max[PlayerConfig.Attr.Int]);
        value += MonetaryValueCalculatedFromStat(max[PlayerConfig.Attr.Wis]);
        value += MonetaryValueCalculatedFromStat(max[PlayerConfig.Attr.Con]);
        value += MonetaryValueCalculatedFromStat(max[PlayerConfig.Attr.Dex]);

        // Social Class adjustment
        var newGold = py.Misc.SocialClass * 6 + RandomNumber.Next(25) + 325;

        // Stat adjustment
        newGold -= value;

        // Charisma adjustment
        newGold += MonetaryValueCalculatedFromStat(max[PlayerConfig.Attr.Chr]);

        // She charmed the banker into it! -CJS-
        if (!PlayerBonuses.IsMale(py))
        {
            newGold += 50;
        }

        // Minimum
        if (newGold < 80)
        {
            newGold = 80;
        }

        py.Misc.Au = newGold;
    }

    // getCharacterName() (src/ui.cpp) semantics without the prompt: the
    // original's getStringInput() caps entry at 23 columns and strips
    // trailing blanks; an empty/aborted entry falls back to
    // getDefaultPlayerName() (the OS login name, else "X").
    private static string ResolveName(string? name)
    {
        var entered = (name ?? "").TrimEnd(' ');
        if (entered.Length > 23)
        {
            entered = entered[..23].TrimEnd(' ');
        }

        if (entered.Length > 0)
        {
            return entered;
        }

        var login = Environment.UserName;
        // PLAYER_NAME_SIZE is 27 including the terminator; GetUserName fails
        // (-> "X") if the login doesn't fit.
        return string.IsNullOrEmpty(login) || login.Length >= 27 ? "X" : login;
    }

    // src/game_run.cpp:158 initializeCharacterInventory() -- init players
    // with some belongings. -RAK-
    private static void InitializeCharacterInventory(Player py, ObjectIdentification identification)
    {
        // this is needed for bash to work right, it can't hurt anyway
        for (var i = 0; i < py.Inventory.Length; i++)
        {
            py.Inventory[i] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing);
        }

        foreach (var itemId in DataPlayerData.ClassBaseProvisions[py.Misc.ClassId])
        {
            var item = InventoryCatalog.CopyFromCatalog(itemId);

            // this makes it spellItemIdentifyAndRemoveRandomInscription and itemSetAsIdentified
            identification.ItemIdentifyAsStoreBought(ref item);

            // must set this bit to display to_hit/to_damage for stiletto
            if (item.CategoryId == TreasureCategory.TvSword)
            {
                item.Identification |= IdentificationConfig.IdShowHitDam;
            }

            PlayerInventory.CarryItem(py, identification, item);
        }

        // weird place for it, but why not?
        for (var i = 0; i < py.Flags.SpellsLearnedOrder.Length; i++)
        {
            py.Flags.SpellsLearnedOrder[i] = 99;
        }
    }
}
