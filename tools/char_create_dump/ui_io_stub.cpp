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
// the status-effects harness (M7) dumps it. getKeyInput() also zeroes it
// ("just to be safe -CJS-"), and so does getInputConfirmation(), which reads
// its answer through getKeyInput(); the stub reproduces both (M10 -- before
// that the comment claimed it but the code didn't, which no earlier harness
// could notice: none of them ever has a nonzero count at a prompt).
//
// M12 added an opt-in log of putStringClearToEOL() lines (uiStubSetScreenLog)
// for the score screen's table.
//
// M13 added, for the store, an opt-in putString() log (on the same screen
// log) and a scripted list of getStringInput() lines, <ESCAPE> included --
// a haggle reads several offers in one command.
//
// M11 added an opt-in message log (uiStubSetMessageLog) so the item-use
// harness can compare message text, a scripted answer for
// getInputConfirmationWithAbort(), and the command_count zeroing of that
// prompt and getStringInput() (both read through getKeyInput() in the real
// game).

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "ui_io_stub.h"

int eof_flag = 0;
bool panic_save = false;

static std::string scripted_keys;
static size_t scripted_key_pos = 0;
static std::string scripted_name;
static std::vector<std::string> *message_log = nullptr;

void uiStubSetMessageLog(std::vector<std::string> *log) {
    message_log = log;
}

static void logMessage(const char *msg) {
    if (message_log != nullptr && msg != nullptr) {
        message_log->push_back(msg);
    }
}

void uiStubSetScript(const std::string &keys, const std::string &name) {
    scripted_keys = keys;
    scripted_key_pos = 0;
    scripted_name = name;
}

bool uiStubScriptConsumed() {
    return scripted_key_pos == scripted_keys.size();
}

size_t uiStubKeysConsumed() {
    return scripted_key_pos;
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

static std::vector<std::string> *screen_log = nullptr;
static bool screen_log_put_string = false;

void uiStubSetScreenLogPutString(bool enabled) {
    screen_log_put_string = enabled;
}

void putString(const char *out_str, Coord_t coord) {
    if (screen_log != nullptr && screen_log_put_string) {
        screen_log->push_back(std::to_string(coord.y) + "," + std::to_string(coord.x) + "=\t" + out_str);
    }
}

void uiStubSetScreenLog(std::vector<std::string> *log) {
    screen_log = log;
}

void putStringClearToEOL(const std::string &str, Coord_t coord) {
    if (screen_log != nullptr) {
        screen_log->push_back(std::to_string(coord.y) + "," + std::to_string(coord.x) + "\t" + str);
    }
}

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
    logMessage(msg);
}

// The real one saves/restores game.command_count around printMessage(), so
// its net effect on game state is nil.
void printMessageNoCommandInterrupt(const std::string &msg) {
    logMessage(msg.c_str());
}

char getKeyInput() {
    if (scripted_key_pos >= scripted_keys.size()) {
        std::fprintf(stderr, "ui_io_stub: getKeyInput() called with the key script exhausted\n");
        std::exit(2);
    }
    game.command_count = 0; // Just to be safe -CJS-
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

static const std::vector<std::string> *string_script = nullptr;
static size_t string_script_pos = 0;

void uiStubSetStringScript(const std::vector<std::string> *lines) {
    string_script = lines;
    string_script_pos = 0;
}

size_t uiStubStringsConsumed() {
    return string_script_pos;
}

// Mirrors the real getStringInput()'s only side effect on game state:
// copying the entered text (trailing blanks removed) into in_str.
bool getStringInput(char *in_str, Coord_t /*coord*/, int slen) {
    game.command_count = 0; // via getKeyInput()
    std::string name = scripted_name;
    if (string_script != nullptr) {
        if (string_script_pos >= string_script->size()) {
            std::fprintf(stderr, "ui_io_stub: getStringInput() called with the string script exhausted\n");
            std::exit(2);
        }
        name = (*string_script)[string_script_pos++];
        if (name == std::string(1, (char) ESCAPE)) {
            return false;
        }
    }
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
    game.command_count = 0; // via getKeyInput()
    return confirm_answer;
}

static int confirm_with_abort_answer = 0;

void uiStubSetConfirmWithAbortAnswer(int answer) {
    confirm_with_abort_answer = answer;
}

int getInputConfirmationWithAbort(int /*column*/, const std::string & /*prompt*/) {
    game.command_count = 0; // via getKeyInput()
    return confirm_with_abort_answer;
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
