namespace NSnipes;

/// <summary>
/// Represents a player in a network game (either local or remote)
/// </summary>
public class PlayerNetwork
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
    public bool IsLocal { get; set; } = false;
    
    // Position (world coordinates)
    public int X { get; set; }
    public int Y { get; set; }
    public int PreviousX { get; set; }
    public int PreviousY { get; set; }
    
    // Viewport position tracking (where we last drew this player on screen)
    public int LastDrawnViewportX { get; set; } = -1;
    public int LastDrawnViewportY { get; set; } = -1;
    
    // Game state
    public int Lives { get; set; } = 5;
    public int Score { get; set; } = 0;
    public bool IsAlive { get; set; } = true;
    
    // Network sync
    public DateTime LastPositionUpdate { get; set; }
    public int PositionSequence { get; set; } = 0;
    
    public PlayerNetwork(string playerId, string initials, int playerNumber, bool isLocal)
    {
        PlayerId = playerId;
        Initials = initials;
        PlayerNumber = playerNumber;
        IsLocal = isLocal;
        LastPositionUpdate = DateTime.Now;
    }
    
    public void UpdatePosition(int x, int y, int sequence)
    {
        // Only update previous position if current position is different
        // This prevents artifacts when position updates arrive but position hasn't changed
        if (X != x || Y != y)
        {
            PreviousX = X;
            PreviousY = Y;
        }
        // If this is the first update (X and Y are 0), set previous to current to avoid clearing artifacts
        else if (X == 0 && Y == 0 && PreviousX == 0 && PreviousY == 0)
        {
            PreviousX = x;
            PreviousY = y;
        }
        
        X = x;
        Y = y;
        PositionSequence = sequence;
        LastPositionUpdate = DateTime.Now;
    }
}

