using System.Collections.Concurrent;
using Umoria.Core.Ui;

namespace Umoria.Console.UI;

// Keys typed in the Terminal.Gui window, handed to the game thread. The
// game reads them the way the original reads curses' getch(): blocking,
// one at a time, from deep inside its commands (Umoria.Core.Ui.Terminal).
// Closing the queue is end of input (-1), which the game treats as the
// original treats EOF.
public sealed class KeyQueue : IKeySource
{
    private readonly BlockingCollection<int> _keys = new();

    public void Add(char key)
    {
        if (!_keys.IsAddingCompleted)
        {
            _keys.Add(key);
        }
    }

    public void Close() => _keys.CompleteAdding();

    // Every key typed so far has been read.
    public bool IsDrained => _keys.Count == 0;

    public int ReadKey()
    {
        try
        {
            return _keys.Take();
        }
        catch (InvalidOperationException)
        {
            // completed and empty
            return -1;
        }
    }
}
