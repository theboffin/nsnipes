namespace NSnipes;

public class Player
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Score { get; set; }
    public int Lives { get; set; }
    public bool IsAlive { get; set; } = true;
    public string Initials { get; set; } = "BD";
    
    public const int InitialLives = 5;
    public const int InitialScore = 0;
    
    public Player(int x, int y)
    {
        X = x;
        Y = y;
        Score = InitialScore;
        Lives = InitialLives;
    }
}
