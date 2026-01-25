namespace NSnipes;

/// <summary>
/// Represents the type of snipe (A or B)
/// </summary>
public enum SnipeType
{
    TypeA,
    TypeB
}

public class Snipe(int x, int y, SnipeType type, int directionX = 0, int directionY = 0)
{
    public const char SnipeACharacter = '@';
    public const char SnipeBCharacter = '@';
    
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int PreviousX { get; set; } = x; // For clearing previous position
    public int PreviousY { get; set; } = y; // For clearing previous position
    public int PreviousDirectionX { get; set; } = 0; // For clearing previous arrow position
    public int PreviousDirectionY { get; set; } = 0; // For clearing previous arrow position
    public SnipeType Type { get; set; } = type;
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
    
    // Arrow character constants
    private const char ArrowUp = '↑';
    private const char ArrowDown = '↓';
    private const char ArrowLeft = '←';
    private const char ArrowRight = '→';
    private const char ArrowUpLeft = '↖';
    private const char ArrowUpRight = '↗';
    private const char ArrowDownLeft = '↙';
    private const char ArrowDownRight = '↘';
    private const char ArrowNone = '·';
    
    public char GetDirectionArrow()
    {
        // Return arrow character based on direction
        if (DirectionX == 0 && DirectionY == -1) return ArrowUp;
        if (DirectionX == 0 && DirectionY == 1) return ArrowDown;
        if (DirectionX == -1 && DirectionY == 0) return ArrowLeft;
        if (DirectionX == 1 && DirectionY == 0) return ArrowRight;
        if (DirectionX == -1 && DirectionY == -1) return ArrowUpLeft;
        if (DirectionX == 1 && DirectionY == -1) return ArrowUpRight;
        if (DirectionX == -1 && DirectionY == 1) return ArrowDownLeft;
        if (DirectionX == 1 && DirectionY == 1) return ArrowDownRight;
        return ArrowNone; // Default if no direction
    }
}
