using System.Text.Json;

namespace NSnipes;

public enum GameSessionRole
{
    Host,
    Client
}

public enum GameSessionStatus
{
    NotStarted,
    WaitingForPlayers,
    Starting,
    Playing,
    Ended
}

public class GameSession
{
    public string GameId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public GameSessionRole Role { get; set; }
    public GameSessionStatus Status { get; set; } = GameSessionStatus.NotStarted;
    public int MaxPlayers { get; set; } = 1;
    public int CurrentPlayers { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartTime { get; set; }
    public List<NetworkPlayerInfo> Players { get; set; } = new List<NetworkPlayerInfo>(5); // Max 5 players
    
    public static string GenerateGameId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        // Avoid LINQ allocations - use stackalloc for small array
        Span<char> buffer = stackalloc char[6];
        var random = new Random();
        for (int i = 0; i < 6; i++)
        {
            buffer[i] = chars[random.Next(chars.Length)];
        }
        return new string(buffer);
    }
    
    public static string GeneratePlayerId()
    {
        return $"player_{DateTime.UtcNow.Ticks}_{Guid.NewGuid().ToString().Substring(0, 8)}";
    }
}

public class NetworkPlayerInfo
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Lives { get; set; } = 5;
    public int Score { get; set; } = 0;
    public bool IsAlive { get; set; } = true;
    public bool IsLocal { get; set; } = false;
}

// Legacy message types removed - now using gRPC protocol buffers from NSnipes.GrpcServer namespace

