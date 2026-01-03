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
    public List<NetworkPlayerInfo> Players { get; set; } = new List<NetworkPlayerInfo>();
    
    // Events
    public event Action<NetworkPlayerInfo>? OnPlayerJoined;
    public event Action<string>? OnPlayerLeft; // playerId
    public event Action? OnGameStart;
    public event Action? OnGameEnd;
    
    public static string GenerateGameId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
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

// MQTT Message DTOs
public class GameInfoMessage
{
    public string GameId { get; set; } = "";
    public string HostPlayerId { get; set; } = "";
    public string HostInitials { get; set; } = "";
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }
    public string Status { get; set; } = "waiting";
    public int Level { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
}

public class JoinRequestMessage
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class JoinResponseMessage
{
    public bool Accepted { get; set; }
    public string? PlayerId { get; set; }
    public int? PlayerNumber { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PlayerJoinNotificationMessage
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PlayerCountUpdateMessage
{
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();
    public int TimeRemaining { get; set; } // seconds
    public DateTime Timestamp { get; set; }
}

public class PlayerInfo
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
}

public class GameStartMessage
{
    public string GameId { get; set; } = "";
    public int Level { get; set; } = 1;
    public List<string> Players { get; set; } = new List<string>();
    public DateTime StartTime { get; set; }
}

public class PlayerPositionUpdateMessage
{
    public string PlayerId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public DateTime Timestamp { get; set; }
    public int Sequence { get; set; }
}

public class BulletFiredMessage
{
    public string BulletId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Action { get; set; } = "fired"; // "fired", "updated", "expired", "hit"
}

public class BulletUpdateMessage
{
    public string BulletId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = "updated"; // "updated", "expired", "hit"
    public string? HitType { get; set; } // "snipe", "hive", "player", "wall"
    public string? HitTargetId { get; set; } // ID of hit target (if applicable)
}

public class GameStateSnapshotMessage
{
    public string GameId { get; set; } = "";
    public int Level { get; set; } = 1;
    public string Status { get; set; } = "playing";
    public List<PlayerStateInfo> Players { get; set; } = new List<PlayerStateInfo>();
    public List<HiveStateInfo> Hives { get; set; } = new List<HiveStateInfo>();
    public List<SnipeStateInfo> Snipes { get; set; } = new List<SnipeStateInfo>();
    public DateTime Timestamp { get; set; }
    public int Sequence { get; set; }
}

public class PlayerStateInfo
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Lives { get; set; } = 5;
    public int Score { get; set; } = 0;
    public bool IsAlive { get; set; } = true;
}

public class HiveStateInfo
{
    public string HiveId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Hits { get; set; } = 0;
    public bool IsDestroyed { get; set; } = false;
    public int SnipesRemaining { get; set; } = 20;
    public int FlashIntervalMs { get; set; } = 75;
}

public class SnipeStateInfo
{
    public string SnipeId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public char Type { get; set; } = 'A';
    public int DirectionX { get; set; } = 0;
    public int DirectionY { get; set; } = 0;
    public bool IsAlive { get; set; } = true;
}

public class SnipeUpdatesMessage
{
    public List<SnipeUpdateInfo> Updates { get; set; } = new List<SnipeUpdateInfo>();
}

public class SnipeUpdateInfo
{
    public string SnipeId { get; set; } = "";
    public string Action { get; set; } = "moved"; // "spawned", "moved", "died"
    public int X { get; set; }
    public int Y { get; set; }
    public int DirectionX { get; set; } = 0;
    public int DirectionY { get; set; } = 0;
    public char? Type { get; set; } // Only for spawned
    public DateTime Timestamp { get; set; }
}

public class HiveUpdatesMessage
{
    public List<HiveUpdateInfo> Updates { get; set; } = new List<HiveUpdateInfo>();
}

public class HiveUpdateInfo
{
    public string HiveId { get; set; } = "";
    public string Action { get; set; } = "hit"; // "spawned", "hit", "destroyed"
    public int Hits { get; set; } = 0;
    public int FlashIntervalMs { get; set; } = 75;
    public DateTime Timestamp { get; set; }
}

