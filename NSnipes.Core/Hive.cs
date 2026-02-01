namespace NSnipes;

public class Hive(int x, int y, int snipesPerHive, DateTime? initialSpawnTime = null)
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public bool IsDestroyed { get; set; } = false;
    public int Hits { get; set; } = 0;
    public const int HitsToDestroy = 3;
    public int FlashIntervalMs { get; set; } = 75;
    public int SnipesRemaining { get; set; } = snipesPerHive;
    public int SnipesType2 { get; set; } = snipesPerHive / 2 + (snipesPerHive % 2 == 1 ? 1 : 0);
    public int SnipesType3 { get; set; } = snipesPerHive / 2;
    public DateTime LastSpawnTime { get; set; } = initialSpawnTime ?? DateTime.Now;
    public const int SpawnIntervalMs = 3000;
    public const int Width = 2;
    public const int Height = 2;

    public bool CanSpawnSnipe() => SnipesRemaining > 0 && !IsDestroyed;

    public SnipeType GetNextSnipeType()
    {
        if (SnipesType2 > 0 && SnipesType3 > 0)
            return Random.Shared.Next(2) == 0 ? SnipeType.TypeA : SnipeType.TypeB;
        if (SnipesType2 > 0) return SnipeType.TypeA;
        if (SnipesType3 > 0) return SnipeType.TypeB;
        return SnipeType.TypeA;
    }

    public void SpawnSnipe(DateTime? spawnTime = null)
    {
        if (SnipesRemaining > 0)
        {
            SnipeType type = GetNextSnipeType();
            if (type == SnipeType.TypeA) SnipesType2--;
            else SnipesType3--;
            SnipesRemaining--;
            LastSpawnTime = spawnTime ?? DateTime.Now;
        }
    }
}
