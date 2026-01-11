using System.Collections.Concurrent;
using Grpc.Core;

namespace NSnipes.GrpcServer;

public class GameRoom
{
    public string GameId { get; set; } = "";
    public string HostPlayerId { get; set; } = "";
    public string HostInitials { get; set; } = "";
    public int MaxPlayers { get; set; }
    public int StartingLevel { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartTime { get; set; }
    
    // Track connected players and their streams
    private readonly ConcurrentDictionary<string, PlayerConnection> _players = new();
    
    // Store pending join info as backup (in case main dictionary is cleared)
    private readonly ConcurrentDictionary<string, PlayerJoinInfo> _pendingJoinInfo = new();
    
    public int CurrentPlayers => _players.Count;
    public bool IsFull => CurrentPlayers >= MaxPlayers;
    public bool IsStarted => StartTime.HasValue;
    
    public IReadOnlyDictionary<string, PlayerConnection> Players => _players;
    
    public bool AddPlayer(string playerId, string initials, int playerNumber, IServerStreamWriter<GameMessage> stream)
    {
        if (IsFull || IsStarted)
            return false;
            
        var connection = new PlayerConnection
        {
            PlayerId = playerId,
            Initials = initials,
            PlayerNumber = playerNumber,
            Stream = stream
        };
        
        return _players.TryAdd(playerId, connection);
    }
    
    public bool RemovePlayer(string playerId)
    {
        return _players.TryRemove(playerId, out _);
    }
    
    public PlayerConnection? GetPlayer(string playerId)
    {
        _players.TryGetValue(playerId, out var player);
        return player;
    }
    
    public bool UpdatePlayerStream(string playerId, IServerStreamWriter<GameMessage> stream)
    {
        if (_players.TryGetValue(playerId, out var connection))
        {
            connection.Stream = stream;
            return true;
        }
        return false;
    }
    
    public void StorePendingJoinInfo(string playerId, PlayerJoinInfo joinInfo)
    {
        _pendingJoinInfo[playerId] = joinInfo;
    }
    
    public PlayerJoinInfo? GetPendingJoinInfo(string playerId)
    {
        _pendingJoinInfo.TryGetValue(playerId, out var info);
        return info;
    }
    
    public bool RemovePendingJoinInfo(string playerId)
    {
        return _pendingJoinInfo.TryRemove(playerId, out _);
    }
    
    public async Task BroadcastMessageAsync(GameMessage message, string? excludePlayerId = null)
    {
        var tasks = new List<Task>();
        
        foreach (var (playerId, connection) in _players)
        {
            if (excludePlayerId != null && playerId == excludePlayerId)
                continue;
                
            if (connection.Stream != null)
            {
                tasks.Add(connection.Stream.WriteAsync(message));
            }
        }
        
        await Task.WhenAll(tasks);
    }
    
    public List<PlayerInfo> GetPlayerList()
    {
        return _players.Values
            .Select(p => new PlayerInfo
            {
                PlayerId = p.PlayerId,
                Initials = p.Initials,
                PlayerNumber = p.PlayerNumber
            })
            .OrderBy(p => p.PlayerNumber)
            .ToList();
    }
}

public class PlayerConnection
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
    public IServerStreamWriter<GameMessage>? Stream { get; set; }
}
