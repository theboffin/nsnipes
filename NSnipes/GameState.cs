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
        // Level 1 = 4 hives, then +1 hive every 4 levels
        // Level 1-4: 4 hives
        // Level 5-8: 5 hives
        // Level 9-12: 6 hives
        // etc.
        return 4 + ((level - 1) / 4);
    }
    
    public int GetSnipesPerHiveForLevel(int level)
    {
        // Level 1 = 10 snipes per hive, then +1 snipe per level
        // Level 1: 10 snipes per hive
        // Level 2: 11 snipes per hive
        // Level 3: 12 snipes per hive
        // etc.
        return 10 + (level - 1);
    }
    
    public bool IsLevelComplete()
    {
        // Level is complete when all hives and all snipes are destroyed
        return HivesUndestroyed == 0 && SnipesUndestroyed == 0;
    }
}

