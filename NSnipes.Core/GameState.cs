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
        return 4 + ((level - 1) / 4);
    }

    public int GetSnipesPerHiveForLevel(int level)
    {
        return 10 + (level - 1);
    }

    public bool IsLevelComplete()
    {
        return HivesUndestroyed == 0 && SnipesUndestroyed == 0;
    }
}
