namespace Umoria.Core.Ui;

// src/ui.cpp messages[] / last_message_id -- the saved message history
// -CJS-, which ^P pages back through. It is game state in the sense that
// the save file carries it (svWrite()), so it lives on the PlayerWorld
// rather than in the terminal layer; printMessage() (GameUi.PrintMessage)
// is what fills it. (M14)
public sealed class MessageHistory
{
    // src/ui.h MESSAGE_HISTORY_SIZE
    public const int Size = 22;

    // Each entry is a vtype_t: at most MORIA_MESSAGE_SIZE - 1 characters.
    public string[] Messages { get; } = Enumerable.Repeat("", Size).ToArray();

    // Index of last message held in saved messages array
    public short LastMessageId { get; set; }
}
