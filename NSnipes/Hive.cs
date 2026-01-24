namespace NSnipes;

public class Hive
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsDestroyed { get; set; } = false;
    public int Hits { get; set; } = 0; // Number of times hit by bullets
    public const int HitsToDestroy = 3; // Hive destroyed after 3 hits
    public int FlashIntervalMs { get; set; } = 75; // Flash interval in milliseconds (reduced by 1/3 each hit)
    public int SnipesRemaining { get; set; }
    public int SnipesType2 { get; set; } // chr(2) snipes
    public int SnipesType3 { get; set; } // chr(3) snipes
    public DateTime LastSpawnTime { get; set; }
    public const int SpawnIntervalMs = 3000; // Spawn a snipe every 3 seconds (randomized)
    
    // Hive is a small rectangular box (2x2 characters)
    public const int Width = 2;
    public const int Height = 2;
    
    public Hive(int x, int y, int snipesPerHive, DateTime? initialSpawnTime = null)
    {
        X = x;
        Y = y;
        SnipesRemaining = snipesPerHive;
        // Split snipes evenly between type A and B
        SnipesType2 = snipesPerHive / 2;
        SnipesType3 = snipesPerHive / 2;
        // If odd number, give the extra to type A
        if (snipesPerHive % 2 == 1)
            SnipesType2++;
        LastSpawnTime = initialSpawnTime ?? DateTime.Now;
    }
    
    public bool CanSpawnSnipe()
    {
        return SnipesRemaining > 0 && !IsDestroyed;
    }
    
    public char GetNextSnipeType()
    {
        // Alternate or prefer type with more remaining
        if (SnipesType2 > 0 && SnipesType3 > 0)
        {
            // Randomly choose between the two
            return Random.Shared.Next(2) == 0 ? 'A' : 'B';
        }
        else if (SnipesType2 > 0)
        {
            return 'A';
        }
        else if (SnipesType3 > 0)
        {
            return 'B';
        }
        return 'A'; // Default
    }
    
    public void SpawnSnipe(DateTime? spawnTime = null)
    {
        if (SnipesRemaining > 0)
        {
            char type = GetNextSnipeType();
            if (type == 'A')
                SnipesType2--;
            else
                SnipesType3--;
            SnipesRemaining--;
            LastSpawnTime = spawnTime ?? DateTime.Now;
        }
    }
}

