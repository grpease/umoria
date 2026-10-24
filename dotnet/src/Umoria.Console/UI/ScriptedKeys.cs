using Umoria.Core.Ui;

namespace Umoria.Console.UI;

// The --script keys: typed one at a time, then end of input. Like the C++
// trace harness's shim, nothing is ever "typed ahead", so the game's
// non-blocking reads (flushInputBuffer(), the rest/run interrupt) find
// nothing and can't eat the script.
public sealed class ScriptedKeys : IKeySource
{
    private readonly Queue<int> _keys = new();

    // ~ = <ESCAPE>, $ = <RETURN>, ^x = control-x, anything else itself
    public ScriptedKeys(string script)
    {
        for (var k = 0; k < script.Length; k++)
        {
            var c = script[k];
            _keys.Enqueue(c switch
            {
                '~' => 27,
                '$' => '\r',
                '^' when k + 1 < script.Length => script[++k] & 0x1f,
                _ => c,
            });
        }
    }

    // Every key has been read.
    public bool IsDrained => _keys.Count == 0;

    public int ReadKey() => _keys.Count > 0 ? _keys.Dequeue() : -1;
}
