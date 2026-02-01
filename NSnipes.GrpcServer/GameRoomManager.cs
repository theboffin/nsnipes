using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NSnipes.GrpcServer;

public class GameRoomManager
{
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly Timer _cleanupTimer;
    private readonly Timer _startCheckTimer;
    private readonly ILogger<GameRoomManager>? _logger;

    public GameRoomManager(ILogger<GameRoomManager> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupOldRooms, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        _startCheckTimer = new Timer(CheckRoomsToStart, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void CheckRoomsToStart(object? state)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.IsStarted) continue;
            if (!room.IsFull && (DateTime.UtcNow - room.CreatedAt).TotalSeconds < 60) continue;
            try
            {
                room.StartGameSimulation(_logger);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start game simulation for room {GameId}", room.GameId);
            }
        }
    }
    
    public GameRoom CreateRoom(string gameId, string hostPlayerId, string hostInitials, int maxPlayers, int startingLevel)
    {
        var room = new GameRoom
        {
            GameId = gameId,
            HostPlayerId = hostPlayerId,
            HostInitials = hostInitials,
            MaxPlayers = maxPlayers,
            StartingLevel = startingLevel
        };
        
        _rooms.TryAdd(gameId, room);
        return room;
    }
    
    public GameRoom? GetRoom(string gameId)
    {
        _rooms.TryGetValue(gameId, out var room);
        return room;
    }
    
    public bool RemoveRoom(string gameId)
    {
        return _rooms.TryRemove(gameId, out _);
    }
    
    private void CleanupOldRooms(object? state)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1); // Remove rooms older than 1 hour
        
        var roomsToRemove = _rooms.Values
            .Where(r => r.CreatedAt < cutoff && !r.IsStarted)
            .Select(r => r.GameId)
            .ToList();
            
        foreach (var gameId in roomsToRemove)
        {
            _rooms.TryRemove(gameId, out _);
        }
    }
}
