namespace NSnipes;

public class Snipe
{
    public const char SnipeACharacter = '@';
    public const char SnipeBCharacter = '@';
    
    public int X { get; set; }
    public int Y { get; set; }
    public int PreviousX { get; set; } // For clearing previous position
    public int PreviousY { get; set; } // For clearing previous position
    public int PreviousDirectionX { get; set; } // For clearing previous arrow position
    public int PreviousDirectionY { get; set; } // For clearing previous arrow position
    public char Type { get; set; } // 'A' or 'B'
    public int DirectionX { get; set; } // -1, 0, or 1
    public int DirectionY { get; set; } // -1, 0, or 1
    public bool IsAlive { get; set; } = true;
    public DateTime LastMoveTime { get; set; }
    public const int MoveIntervalMs = 200; // Snipes move every 200ms
    
    public Snipe(int x, int y, char type)
    {
        X = x;
        Y = y;
        PreviousX = x;
        PreviousY = y;
        PreviousDirectionX = 0;
        PreviousDirectionY = 0;
        Type = type; // 'A' or 'B'
        LastMoveTime = DateTime.Now;
        // Initial direction will be set when snipe starts moving toward player
        DirectionX = 0;
        DirectionY = 0;
    }
    
    public char GetDisplayChar()
    {
        return '@'; // Both types use '@', differentiated by color
    }
    
    public char GetDirectionArrow()
    {
        // Return arrow character based on direction
        if (DirectionX == 0 && DirectionY == -1) return '↑'; // Up
        if (DirectionX == 0 && DirectionY == 1) return '↓'; // Down
        if (DirectionX == -1 && DirectionY == 0) return '←'; // Left
        if (DirectionX == 1 && DirectionY == 0) return '→'; // Right
        if (DirectionX == -1 && DirectionY == -1) return '↖'; // Up-Left
        if (DirectionX == 1 && DirectionY == -1) return '↗'; // Up-Right
        if (DirectionX == -1 && DirectionY == 1) return '↙'; // Down-Left
        if (DirectionX == 1 && DirectionY == 1) return '↘'; // Down-Right
        return '·'; // Default if no direction
    }
}

