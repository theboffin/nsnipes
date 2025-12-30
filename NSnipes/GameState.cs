namespace NSnipes;

public class GameState
{
    public int Level { get; set; } = 1;
    public int Score { get; set; } = 0;
    public int TotalHives { get; set; } = 0;
    public int HivesUndestroyed { get; set; } = 0;
    public int TotalSnipes { get; set; } = 0;
    public int SnipesUndestroyed { get; set; } = 0;
    
    public int GetHiveCountForLevel(int level)
    {
        // Level 1 = 5 hives, then +1 hive every 5 levels
        // Level 1-5: 5 hives
        // Level 6-10: 6 hives
        // Level 11-15: 7 hives
        // etc.
        return 5 + ((level - 1) / 5);
    }
}

