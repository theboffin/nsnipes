namespace NSnipes;

public class Snipe(int x, int y, char type, int directionX = 0, int directionY = 0)
{
    public const char SnipeACharacter = '@';
    public const char SnipeBCharacter = '@';
    
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int PreviousX { get; set; } = x; // For clearing previous position
    public int PreviousY { get; set; } = y; // For clearing previous position
    public int PreviousDirectionX { get; set; } = 0; // For clearing previous arrow position
    public int PreviousDirectionY { get; set; } = 0; // For clearing previous arrow position
    public char Type { get; set; } = type; // 'A' or 'B'
    public int DirectionX { get; set; } = directionX; // -1, 0, or 1
    public int DirectionY { get; set; } = directionY; // -1, 0, or 1
    public bool IsAlive { get; set; } = true;
    public DateTime LastMoveTime { get; set; } = DateTime.Now;
    public string SnipeId { get; set; } = $"snipe_{x}_{y}_{DateTime.UtcNow.Ticks}"; // Unique identifier for network sync
    public const int MoveIntervalMs = 200; // Snipes move every 200ms
    
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

