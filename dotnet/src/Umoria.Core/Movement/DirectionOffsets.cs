namespace Umoria.Core.Movement;

// Shared direction-delta table matching src/player.cpp's playerMovePosition()
// numeric-keypad convention (also mirrored internally by DungeonGenerator for
// streamer-rock placement). Exposed publicly here so the UI layer's input
// handling and the core's generation logic share one definition.
public static class DirectionOffsets
{
    public static (int DeltaY, int DeltaX) For(int direction)
    {
        return direction switch
        {
            1 => (1, -1),
            2 => (1, 0),
            3 => (1, 1),
            4 => (0, -1),
            5 => (0, 0),
            6 => (0, 1),
            7 => (-1, -1),
            8 => (-1, 0),
            9 => (-1, 1),
            _ => (0, 0),
        };
    }
}
