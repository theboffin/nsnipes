namespace NSnipes;

public class Snipe(int x, int y, SnipeType type, int directionX = 0, int directionY = 0)
{
    public const char SnipeACharacter = '@';
    public const char SnipeBCharacter = '@';

    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int PreviousX { get; set; } = x;
    public int PreviousY { get; set; } = y;
    public int PreviousDirectionX { get; set; } = 0;
    public int PreviousDirectionY { get; set; } = 0;
    public SnipeType Type { get; set; } = type;
    public int DirectionX { get; set; } = directionX;
    public int DirectionY { get; set; } = directionY;
    public bool IsAlive { get; set; } = true;
    public DateTime LastMoveTime { get; set; } = DateTime.Now;
    public string SnipeId { get; set; } = $"snipe_{x}_{y}_{DateTime.UtcNow.Ticks}";
    public const int MoveIntervalMs = 200;

    public char GetDisplayChar() => '@';

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
        if (DirectionX == 0 && DirectionY == -1) return ArrowUp;
        if (DirectionX == 0 && DirectionY == 1) return ArrowDown;
        if (DirectionX == -1 && DirectionY == 0) return ArrowLeft;
        if (DirectionX == 1 && DirectionY == 0) return ArrowRight;
        if (DirectionX == -1 && DirectionY == -1) return ArrowUpLeft;
        if (DirectionX == 1 && DirectionY == -1) return ArrowUpRight;
        if (DirectionX == -1 && DirectionY == 1) return ArrowDownLeft;
        if (DirectionX == 1 && DirectionY == 1) return ArrowDownRight;
        return ArrowNone;
    }
}
