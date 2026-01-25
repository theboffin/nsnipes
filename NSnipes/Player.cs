namespace NSnipes;

public class Player(int x, int y)
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int Score { get; set; } = InitialScore;
    public int Lives { get; set; } = InitialLives;
    public bool IsAlive { get; set; } = true;
    public string Initials { get; set; } = "BD";
    
    public const int InitialLives = 5;
    public const int InitialScore = 0;
}
