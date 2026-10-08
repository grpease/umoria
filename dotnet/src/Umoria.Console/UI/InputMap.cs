using Terminal.Gui;

namespace Umoria.Console.UI;

// Maps numeric-keypad-style direction keys to the same 1-9 direction
// convention used by src/player.cpp's playerMovePosition() (and already
// mirrored in Umoria.Core.Dungeon.DungeonGenerator.PlayerMovePosition):
// 7 8 9
// 4 5 6
// 1 2 3
public static class InputMap
{
    public static int? DirectionFor(Key key)
    {
        return key.KeyCode switch
        {
            KeyCode.D1 => 1,
            KeyCode.D2 or KeyCode.CursorDown => 2,
            KeyCode.D3 => 3,
            KeyCode.D4 or KeyCode.CursorLeft => 4,
            KeyCode.D6 or KeyCode.CursorRight => 6,
            KeyCode.D7 => 7,
            KeyCode.D8 or KeyCode.CursorUp => 8,
            KeyCode.D9 => 9,
            // vi-style keys, matching the original's roguelike-keys option
            KeyCode.H => 4,
            KeyCode.J => 2,
            KeyCode.K => 8,
            KeyCode.L => 6,
            KeyCode.Y => 7,
            KeyCode.U => 9,
            KeyCode.B => 1,
            KeyCode.N => 3,
            _ => null,
        };
    }

    public static bool IsQuit(Key key)
    {
        return key.KeyCode is KeyCode.Q or KeyCode.Esc;
    }
}
