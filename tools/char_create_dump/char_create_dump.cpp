// Character-creation dump harness: verifies the .NET migration's port of
// character creation (M6, Umoria.Core.Character) against the real compiled
// C++ game.
//
// Drives the REAL characterCreate() (src/character.cpp) with scripted
// keypresses -- see ui_io_stub.cpp, which replaces src/ui_io.cpp in this
// target only -- followed by the rest of startMoria()'s new-character path
// (initializeCharacterInventory(), food, spell/mana setup), then dumps every
// deterministic field of the resulting Player_t plus objects_identified[].
//
// Deliberately NOT exercised (documented in migration-plan.md's M6 section):
//   - startMoria()'s seedsInitialize()/storeInitializeOwners() RNG draws
//     before characterCreate() -- each case calls setRandomSeed() directly
//     instead, same as golden_trace_harness. Store owners are M13's job.
//   - py.misc.date_of_birth -- wall-clock time, not deterministic; not dumped.
//   - magicInitializeItemNames() -- runs after creation, only affects item
//     flavor names (identification, M11), not Player_t.
//
// Matrix: 3 seeds x 8 races x 2 genders x every class each race allows, with
// a re-roll count (0-2) varied per case so the re-roll loop's repeated RNG
// consumption is exercised too.
//
// Usage: char_create_dump [output_file]
// With no output_file, writes to stdout.
//
// The creation + dump code itself lives in char_create_common.cpp, shared
// with the M7 status-effects harness.

#include "../../src/headers.h"

#include <cstdio>

#include "char_create_common.h"

int main(int argc, char *argv[]) {
    FILE *out = stdout;
    if (argc >= 2) {
        out = std::fopen(argv[1], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[1]);
            return 1;
        }
    }
    playerDumpSetOutput(out);

    const uint32_t seeds[] = {12345, 987654321, 31337};

    int case_index = 0;
    for (uint32_t seed : seeds) {
        for (int race_id = 0; race_id < PLAYER_MAX_RACES; race_id++) {
            int class_count = raceClassCount(race_id);

            for (int gender = 0; gender < 2; gender++) {
                bool is_male = gender == 0;

                for (int class_choice = 0; class_choice < class_count; class_choice++) {
                    int rerolls = case_index % 3;
                    case_index++;

                    if (!createScriptedCharacter(seed, race_id, is_male, class_choice, rerolls)) {
                        std::fprintf(stderr, "case %d: key script not fully consumed -- flow mismatch\n", case_index);
                        return 2;
                    }

                    std::fprintf(out, "case seed=%u race=%d male=%d class_choice=%d rerolls=%d\n", seed, race_id,
                                 is_male ? 1 : 0, class_choice, rerolls);
                    dumpPlayer();
                }
            }
        }
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
