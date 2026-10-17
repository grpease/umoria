using Umoria.Core.Character;
using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Tests.CharCreate;

// Real cross-check: compares Umoria.Core.Character.CharacterCreation (the M6
// port of src/character.cpp plus startMoria()'s new-character tail) against
// a text dump captured from the real, compiled C++ game via
// tools/char_create_dump/char_create_dump.cpp (built through the MSVC+vcpkg
// toolchain -- see dotnet/tests/CharCreateTraces/char_create_dump.txt for
// the checked-in baseline).
//
// The C++ side drives the real, unmodified characterCreate() with scripted
// keypresses (its ui_io.cpp is swapped for a headless stub); this side feeds
// the same answers in as CharacterChoices. Same matrix on both sides: 3
// seeds x 8 races x 2 genders x every class each race allows, with the
// re-roll count cycling 0/1/2 per case. Each case dumps every deterministic
// Player_t field (all but date_of_birth, which is wall-clock time), the full
// 34-slot inventory, any nonzero objects_identified[] entries, and the final
// RNG state -- the latter catches any extra or missing RNG draw even if it
// happened not to change a visible field.
public class CharCreateTraceTests
{
    private static readonly uint[] Seeds = [12345, 987654321, 31337];

    [Fact]
    public void CharacterCreation_MatchesCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CharCreateTraces", "char_create_dump.txt");
        var expected = File.ReadAllLines(path);

        var actual = new List<string>();
        var caseIndex = 0;

        foreach (var seed in Seeds)
        {
            for (var raceId = 0; raceId < DataPlayerData.CharacterRaces.Length; raceId++)
            {
                var classCount = CharacterCreation.AvailableClasses(raceId).Count;

                foreach (var isMale in new[] { true, false })
                {
                    for (var classChoice = 0; classChoice < classCount; classChoice++)
                    {
                        var rerolls = caseIndex % 3;
                        caseIndex++;

                        var (py, identification) = PlayerTrace.CreateCharacter(seed, raceId, isMale, classChoice, rerolls);

                        actual.Add($"case seed={seed} race={raceId} male={(isMale ? 1 : 0)} class_choice={classChoice} rerolls={rerolls}");
                        PlayerTrace.Dump(actual, py, identification);
                    }
                }
            }
        }

        PlayerTrace.AssertMatches(expected, actual, "case ");
    }

    [Fact]
    public void AvailableClasses_HumanCanChooseEveryClass()
    {
        // src/data_player.cpp: Human's classes_bit_field is 0x3F.
        Assert.Equal([0, 1, 2, 3, 4, 5], CharacterCreation.AvailableClasses(0));
    }

    [Fact]
    public void Create_RejectsClassTheRaceDoesNotOffer()
    {
        var py = new Player();
        MoriaRng.SetRandomSeed(1);

        var classCount = CharacterCreation.AvailableClasses(7).Count; // one past the last offered
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterCreation.Create(py, new CharacterChoices(7, true, classCount, "Tester")));
    }
}
