using System.Collections.Concurrent;
using Grpc.Core;
using NSnipes;

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

    private GameSimulation? _simulation;
    private CancellationTokenSource? _tickCts;
    private readonly object _simLock = new object();
    private const int TickIntervalMs = 50;

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

    public void ApplyInput(string playerId, int moveDx, int moveDy, int fireDx, int fireDy)
    {
        lock (_simLock)
        {
            _simulation?.ApplyInput(playerId, moveDx, moveDy, fireDx, fireDy);
        }
    }

    public void StartGameSimulation(ILogger? logger)
    {
        lock (_simLock)
        {
            if (StartTime.HasValue) return;
            StartTime = DateTime.UtcNow;

            var playerList = GetPlayerList();
            var playerInfos = playerList
                .Select(p => (p.PlayerId, p.Initials))
                .ToList();

            _simulation = new GameSimulation();
            _simulation.StartGame(StartingLevel, playerInfos);

            _tickCts = new CancellationTokenSource();
            var token = _tickCts.Token;
            var room = this;
            _ = Task.Run(async () =>
            {
                try
                {
                    var gameStart = new GameMessage
                    {
                        GameId = room.GameId,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        GameStart = new GameStartMessage
                        {
                            Level = room.StartingLevel
                        }
                    };
                    gameStart.GameStart.PlayerIds.AddRange(playerList.Select(p => p.PlayerId));
                    await room.BroadcastMessageAsync(gameStart);

                    while (!token.IsCancellationRequested && _simulation?.Status == "playing")
                    {
                        GameMessage? msg = null;
                        lock (_simLock)
                        {
                            _simulation?.Tick(DateTime.UtcNow);
                            if (_simulation == null) break;
                            var snapshot = BuildStateSnapshot();
                            if (snapshot != null)
                                msg = new GameMessage
                                {
                                    GameId = room.GameId,
                                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    State = snapshot
                                };
                        }
                        if (msg != null)
                            await room.BroadcastMessageAsync(msg);
                        await Task.Delay(TickIntervalMs, token);
                    }

                    if (_simulation?.Status == "ended")
                    {
                        var scores = _simulation.Players
                            .OrderByDescending(p => p.Score)
                            .Select((p, i) => new PlayerScoreInfo
                            {
                                PlayerId = p.PlayerId ?? "",
                                Initials = p.Initials,
                                Score = p.Score,
                                Rank = i + 1
                            })
                            .ToList();
                        var gameOver = new GameMessage
                        {
                            GameId = room.GameId,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            GameOver = new GameOverMessage()
                        };
                        gameOver.GameOver.FinalScores.AddRange(scores);
                        await room.BroadcastMessageAsync(gameOver);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Tick loop error in room {GameId}", room.GameId);
                }
            }, token);
        }
    }

    private GameStateSnapshot? BuildStateSnapshot()
    {
        if (_simulation == null) return null;
        var s = _simulation;
        var snapshot = new GameStateSnapshot
        {
            Level = s.State.Level,
            Status = s.Status
        };
        foreach (var p in s.Players)
        {
            snapshot.Players.Add(new PlayerStateInfo
            {
                PlayerId = p.PlayerId ?? "",
                Initials = p.Initials,
                X = p.X,
                Y = p.Y,
                Lives = p.Lives,
                Score = p.Score,
                IsAlive = p.IsAlive
            });
        }
        foreach (var h in s.Hives)
        {
            snapshot.Hives.Add(new HiveStateInfo
            {
                HiveId = $"hive_{h.X}_{h.Y}",
                X = h.X,
                Y = h.Y,
                Hits = h.Hits,
                IsDestroyed = h.IsDestroyed,
                SnipesRemaining = h.SnipesRemaining,
                FlashIntervalMs = h.FlashIntervalMs
            });
        }
        foreach (var sn in s.Snipes)
        {
            snapshot.Snipes.Add(new SnipeStateInfo
            {
                SnipeId = sn.SnipeId,
                X = sn.X,
                Y = sn.Y,
                Type = sn.Type == SnipeType.TypeA ? "A" : "B",
                DirectionX = sn.DirectionX,
                DirectionY = sn.DirectionY,
                IsAlive = sn.IsAlive
            });
        }
        foreach (var b in s.Bullets)
        {
            snapshot.Bullets.Add(new BulletStateInfo
            {
                BulletId = b.BulletId,
                X = b.X,
                Y = b.Y,
                VelocityX = b.VelocityX,
                VelocityY = b.VelocityY,
                PlayerId = b.PlayerId ?? ""
            });
        }
        return snapshot;
    }

    public void StopSimulation()
    {
        _tickCts?.Cancel();
    }
}

public class PlayerConnection
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
    public IServerStreamWriter<GameMessage>? Stream { get; set; }
}
