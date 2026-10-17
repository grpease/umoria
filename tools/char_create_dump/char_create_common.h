#pragma once

#include <cstdint>
#include <cstdio>

// Shared by char_create_dump (M6) and status_effects_dump (M7); see
// char_create_common.cpp.

// Where dumpPlayer() writes (stdout by default).
void playerDumpSetOutput(FILE *file);

// Dumps every deterministic Player_t field, the 34-slot inventory, nonzero
// objects_identified[] entries and the RNG state, one "name<TAB>value" line
// each -- the format dotnet/tests mirrors.
void dumpPlayer();

// How many classes `race_id` may choose from (classes_bit_field popcount).
int raceClassCount(int race_id);

// Resets the player, drives the real characterCreate() with scripted
// keypresses for these answers, then runs startMoria()'s new-character tail.
// Returns false if the key script wasn't fully consumed (flow mismatch).
bool createScriptedCharacter(uint32_t seed, int race_id, bool is_male, int class_choice, int rerolls);
