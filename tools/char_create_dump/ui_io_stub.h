#pragma once

#include <cstddef>
#include <string>

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
