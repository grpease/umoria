#pragma once

#include <cstddef>
#include <string>
#include <vector>

// Scripted-input controls for ui_io_stub.cpp (see there for why it exists).
// `keys` is replayed one char per getKeyInput() call; `name` is what
// getStringInput() returns.
void uiStubSetScript(const std::string &keys, const std::string &name);

// True once every scripted key has been consumed -- the harness checks this
// after each case so a script/flow mismatch fails loudly instead of quietly
// verifying the wrong path.
bool uiStubScriptConsumed();

// How many scripted keys have been read so far. The spell-casting harness
// (M10) dumps it instead of requiring the whole script be consumed: a spell
// that fails its roll never reads its direction key.
size_t uiStubKeysConsumed();

// What getInputConfirmation() answers (default false). The player-actions
// harness (M8) flips it to cover "pick it up anyway?" prompts.
void uiStubSetConfirmAnswer(bool answer);

// What getInputConfirmationWithAbort() answers: 1 yes, 0 no (the default),
// -1 <ESCAPE>. (M11, the drop command's "Drop all ...?")
void uiStubSetConfirmWithAbortAnswer(int answer);

// Where printMessage() / printMessageNoCommandInterrupt() append every
// non-null message; nullptr (the default) logs nothing. (M11)
void uiStubSetMessageLog(std::vector<std::string> *log);

// Where putStringClearToEOL() appends every line it is given, as
// "row,col<TAB>text"; nullptr (the default) logs nothing. (M12, the score
// screen)
void uiStubSetScreenLog(std::vector<std::string> *log);

// Also log putString() lines (as "row,col=<TAB>text"), not only
// putStringClearToEOL() ones. Off by default. (M13, the store screen)
void uiStubSetScreenLogPutString(bool enabled);

// Scripted lines for getStringInput(), one per call, replacing the single
// `name` of uiStubSetScript(); a line holding just ESCAPE is <ESCAPE> (the call
// returns false and leaves the buffer alone, as the real one does when
// nothing was typed). nullptr (the default) goes back to `name`. (M13, the
// store's haggling prompts)
void uiStubSetStringScript(const std::vector<std::string> *lines);

// How many scripted lines getStringInput() has read so far. (M13)
size_t uiStubStringsConsumed();
