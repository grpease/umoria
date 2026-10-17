// Headless replacement for src/ui_io.cpp, linked ONLY into the
// char_create_dump, status_effects_dump and player_actions_dump harnesses
// (see their .cpp files and CMakeLists.txt).
//
// Why this exists: characterCreate() (src/character.cpp) interleaves its
// RNG-consuming logic with curses prompts (race/gender/class/re-roll keys,
// name entry), and putString() calls abort() when curses isn't initialized
// -- so unlike every earlier harness, this one can't simply avoid calling
// initscr(). Rather than adding a test-only copy of characterCreate()'s
// orchestration to the game (which could silently drift from the real one),
// this file swaps out the terminal layer underneath it: every output call is
// a no-op, and getKeyInput()/getStringInput() replay a scripted sequence of
// keypresses. That way the harness drives the REAL, unmodified
// characterCreate() end to end.
//
// None of the replaced functions consume RNG. The game state they mutate is
// the message history and terminal bookkeeping (which no harness reads),
// plus game.command_count -- printMessage() zeroes it for every non-null
// message (cancelling a repeated command), which the stub reproduces since
// the status-effects harness (M7) dumps it. getKeyInput() also zeroes it;
// only character creation reads keys, and it never sets a count.

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

#include "ui_io_stub.h"

int eof_flag = 0;
bool panic_save = false;

static std::string scripted_keys;
static size_t scripted_key_pos = 0;
static std::string scripted_name;

void uiStubSetScript(const std::string &keys, const std::string &name) {
    scripted_keys = keys;
    scripted_key_pos = 0;
    scripted_name = name;
}

bool uiStubScriptConsumed() {
    return scripted_key_pos == scripted_keys.size();
}

bool terminalInitialize() {
    return true;
}

void terminalRestore() {}

void terminalSaveScreen() {}

void terminalRestoreScreen() {}

ssize_t terminalBellSound() {
    return 0;
}

void putQIO() {}

void flushInputBuffer() {}

void clearScreen() {}

void clearToBottom(int /*row*/) {}

void moveCursor(Coord_t /*coord*/) {}

void addChar(char /*ch*/, Coord_t /*coord*/) {}

void putString(const char * /*out_str*/, Coord_t /*coord*/) {}

void putStringClearToEOL(const std::string & /*str*/, Coord_t /*coord*/) {}

void eraseLine(Coord_t /*coord*/) {}

void panelMoveCursor(Coord_t /*coord*/) {}

void panelPutTile(char /*ch*/, Coord_t /*coord*/) {}

void messageLinePrintMessage(std::string /*message*/) {}

void messageLineClear() {}

void printMessage(const char *msg) {
    // Mirrors the real printMessage()'s one game-state effect.
    if (msg != nullptr) {
        game.command_count = 0;
    }
}

// The real one saves/restores game.command_count around printMessage(), so
// its net effect on game state is nil.
void printMessageNoCommandInterrupt(const std::string & /*msg*/) {}

char getKeyInput() {
    if (scripted_key_pos >= scripted_keys.size()) {
        std::fprintf(stderr, "ui_io_stub: getKeyInput() called with the key script exhausted\n");
        std::exit(2);
    }
    return scripted_keys[scripted_key_pos++];
}

bool getCommand(const std::string & /*prompt*/, char &command) {
    command = getKeyInput();
    return command != ESCAPE;
}

bool getTileCharacter(const std::string &prompt, char &command) {
    return getCommand(prompt, command);
}

bool getMenuItemId(const std::string &prompt, char &command) {
    return getCommand(prompt, command);
}

// Mirrors the real getStringInput()'s only side effect on game state:
// copying the entered text (trailing blanks removed) into in_str.
bool getStringInput(char *in_str, Coord_t /*coord*/, int slen) {
    std::string name = scripted_name;
    while (!name.empty() && name.back() == ' ') {
        name.pop_back();
    }
    if ((int) name.size() > slen) {
        name.resize((size_t) slen);
    }
    (void) std::strcpy(in_str, name.c_str());
    return true;
}

static bool confirm_answer = false;

void uiStubSetConfirmAnswer(bool answer) {
    confirm_answer = answer;
}

bool getInputConfirmation(const std::string & /*prompt*/) {
    return confirm_answer;
}

int getInputConfirmationWithAbort(int /*column*/, const std::string & /*prompt*/) {
    return 0;
}

void waitForContinueKey(int /*line_number*/) {}

bool checkForNonBlockingKeyPress(int /*microseconds*/) {
    return false;
}

void getDefaultPlayerName(char *buffer) {
    (void) std::strcpy(buffer, "X");
}

#ifndef _WIN32
FILE *tfopen(const char *file, const char *mode) {
    return std::fopen(file, mode);
}

int topen(const char * /*file*/, int /*flags*/, int /*mode*/) {
    return -1;
}

bool tilde(const char *file, char *expanded) {
    (void) std::strcpy(expanded, file);
    return true;
}
#endif

bool checkFilePermissions() {
    return true;
}
