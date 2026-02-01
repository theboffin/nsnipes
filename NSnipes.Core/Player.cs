namespace NSnipes;

public class Player(int x, int y)
{
    /// <summary>Optional player ID for multiplayer (null for single-player local player).</summary>
    public string? PlayerId { get; set; }

    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int Score { get; set; } = InitialScore;
    public int Lives { get; set; } = InitialLives;
    public bool IsAlive { get; set; } = true;
    public string Initials { get; set; } = "BD";

    public const int InitialLives = 5;
    public const int InitialScore = 0;
    public const int Width = 2;
    public const int Height = 3;
}
