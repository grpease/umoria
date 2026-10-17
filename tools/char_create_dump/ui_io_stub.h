#pragma once

#include <string>

// Scripted-input controls for ui_io_stub.cpp (see there for why it exists).
// `keys` is replayed one char per getKeyInput() call; `name` is what
// getStringInput() returns.
void uiStubSetScript(const std::string &keys, const std::string &name);

// True once every scripted key has been consumed -- the harness checks this
// after each case so a script/flow mismatch fails loudly instead of quietly
// verifying the wrong path.
bool uiStubScriptConsumed();
