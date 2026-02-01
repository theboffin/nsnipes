using Grpc.Core;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NSnipes.GrpcServer;

public class GameServiceImplementation : GameService.GameServiceBase
{
    private readonly GameRoomManager _roomManager;
    private readonly ILogger<GameServiceImplementation> _logger;
    private readonly ConcurrentDictionary<string, string> _playerToRoom = new(); // playerId -> gameId
    private readonly ConcurrentDictionary<string, PlayerJoinInfo> _pendingJoins = new(); // playerId -> join info
    
    public GameServiceImplementation(GameRoomManager roomManager, ILogger<GameServiceImplementation> logger)
    {
        _roomManager = roomManager;
        _logger = logger;
    }
    
    public override Task<CreateGameResponse> CreateGame(CreateGameRequest request, ServerCallContext context)
    {
        try
        {
            // Generate 6-character game ID
            var gameId = GenerateGameId();
            
            var room = _roomManager.CreateRoom(
                gameId,
                request.HostPlayerId,
                request.HostInitials,
                request.MaxPlayers,
                request.StartingLevel
            );
            
            _playerToRoom.TryAdd(request.HostPlayerId, gameId);
            
            // Log game creation prominently
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("🎮 GAME CREATED");
            _logger.LogInformation("   Game ID: {GameId}", gameId);
            _logger.LogInformation("   Host Player ID: {PlayerId}", request.HostPlayerId);
            _logger.LogInformation("   Host Initials: {Initials}", request.HostInitials);
            _logger.LogInformation("   Max Players: {MaxPlayers}", request.MaxPlayers);
            _logger.LogInformation("   Starting Level: {Level}", request.StartingLevel);
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            
            return Task.FromResult(new CreateGameResponse
            {
                GameId = gameId,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating game");
            return Task.FromResult(new CreateGameResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }
    
    public override Task<JoinResponse> JoinGame(JoinRequest request, ServerCallContext context)
    {
        try
        {
            var room = _roomManager.GetRoom(request.GameId);
            
            if (room == null)
            {
                return Task.FromResult(new JoinResponse
                {
                    Accepted = false,
                    ErrorMessage = "Game not found"
                });
            }
            
            if (room.IsFull)
            {
                return Task.FromResult(new JoinResponse
                {
                    Accepted = false,
                    ErrorMessage = "Game is full"
                });
            }
            
            if (room.IsStarted)
            {
                return Task.FromResult(new JoinResponse
                {
                    Accepted = false,
                    ErrorMessage = "Game has already started"
                });
            }
            
            // Assign player number (host is 1, others increment)
            int playerNumber = room.CurrentPlayers + 1;
            
            // Note: We can't add the player to the room here because we don't have the stream yet
            // The stream will be established in GameStream, and we'll add the player there
            
            _playerToRoom.TryAdd(request.PlayerId, request.GameId);
            
            // Store join info for when GameStream connects
            var joinInfo = new PlayerJoinInfo
            {
                PlayerId = request.PlayerId,
                Initials = request.Initials,
                PlayerNumber = playerNumber,
                GameId = request.GameId
            };
            
            // Store in pending joins dictionary
            bool added = _pendingJoins.TryAdd(request.PlayerId, joinInfo);
            _logger.LogInformation("Player {PlayerId} joined game {GameId}, assigned player number {PlayerNumber}. Join info stored: {Stored}. Total pending: {Count}", 
                request.PlayerId, request.GameId, playerNumber, added, _pendingJoins.Count);
            
            if (!added)
            {
                // If already exists, update it
                _pendingJoins[request.PlayerId] = joinInfo;
                _logger.LogWarning("Join info for player {PlayerId} already existed, updated it", request.PlayerId);
            }
            
            // Also store join info in the room as a backup (in case dictionary is cleared)
            // This allows GameStream to retrieve it even if _pendingJoins is empty
            room.StorePendingJoinInfo(request.PlayerId, joinInfo);
            
            return Task.FromResult(new JoinResponse
            {
                Accepted = true,
                PlayerId = request.PlayerId,
                PlayerNumber = playerNumber
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining game");
            return Task.FromResult(new JoinResponse
            {
                Accepted = false,
                ErrorMessage = ex.Message
            });
        }
    }
    
    public override async Task GameStream(
        IAsyncStreamReader<GameMessage> requestStream,
        IServerStreamWriter<GameMessage> responseStream,
        ServerCallContext context)
    {
        string? playerId = null;
        string? gameId = null;
        GameRoom? room = null;
        
        try
        {
            // Wait for first message to identify player and game
            if (!await requestStream.MoveNext())
            {
                return;
            }
            
            var firstMessage = requestStream.Current;
            playerId = firstMessage.PlayerId;
            gameId = firstMessage.GameId;
            
            _logger.LogInformation("GameStream received first message from player {PlayerId} for game {GameId}", playerId, gameId);
            
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(gameId))
            {
                _logger.LogWarning("Invalid first message: missing playerId or gameId");
                return;
            }
            
            // Get or create room connection
            room = _roomManager.GetRoom(gameId);
            if (room == null)
            {
                _logger.LogWarning("Room {GameId} not found for player {PlayerId}", gameId, playerId);
                return;
            }
            
            // Check if this is the host or a joining player
            var isHost = room.HostPlayerId == playerId;
            _logger.LogInformation("Player {PlayerId} is {Role} for game {GameId}", playerId, isHost ? "HOST" : "JOINING", gameId);
            
            if (!isHost)
            {
                // For joining players, get info from pending joins
                _logger.LogInformation("Looking for pending join info for player '{PlayerId}'. Current pending joins: {PendingJoins}", 
                    playerId, string.Join(", ", _pendingJoins.Keys.Select(k => $"'{k}'")));
                
                // Try to find join info - check main dictionary first, then room backup
                PlayerJoinInfo? joinInfo = null;
                string? matchedKey = null;
                
                // First try exact match in main dictionary
                if (_pendingJoins.TryGetValue(playerId, out joinInfo))
                {
                    matchedKey = playerId;
                    _pendingJoins.TryRemove(playerId, out _);
                    _logger.LogInformation("Found join info in main dictionary for '{PlayerId}'", playerId);
                }
                else
                {
                    // Try case-insensitive match in main dictionary
                    foreach (var kvp in _pendingJoins)
                    {
                        if (string.Equals(kvp.Key, playerId, StringComparison.OrdinalIgnoreCase))
                        {
                            joinInfo = kvp.Value;
                            matchedKey = kvp.Key;
                            _pendingJoins.TryRemove(kvp.Key, out _);
                            _logger.LogWarning("Found join info with case-insensitive match: '{StoredKey}' for '{RequestedKey}'", 
                                kvp.Key, playerId);
                            break;
                        }
                    }
                    
                    // If not found in main dictionary, try room backup
                    if (joinInfo == null)
                    {
                        joinInfo = room.GetPendingJoinInfo(playerId);
                        if (joinInfo != null)
                        {
                            room.RemovePendingJoinInfo(playerId);
                            _logger.LogInformation("Found join info in room backup for '{PlayerId}'", playerId);
                        }
                    }
                }
                
                if (joinInfo == null)
                {
                    _logger.LogWarning("No pending join info for player '{PlayerId}'. Available pending joins: {PendingJoins}", 
                        playerId, string.Join(", ", _pendingJoins.Keys.Select(k => $"'{k}'")));
                    
                    // Try to get join info from room if player was already added (retry scenario)
                    var existingPlayer = room.GetPlayer(playerId);
                    if (existingPlayer != null)
                    {
                        _logger.LogInformation("Player '{PlayerId}' already in room, reconnecting stream", playerId);
                        // Update the stream for existing player
                        room.UpdatePlayerStream(playerId, responseStream);
                    }
                    else
                    {
                        _logger.LogError("Cannot add player '{PlayerId}' - no join info and not in room", playerId);
                        return;
                    }
                }
                else
                {
                    if (matchedKey != null)
                    {
                        _logger.LogInformation("Found join info for player '{PlayerId}' (matched key: '{MatchedKey}')", 
                            playerId, matchedKey);
                    }
                    else
                    {
                        _logger.LogInformation("Found join info for player '{PlayerId}' (from room backup)", playerId);
                    }
                    
                    // Add player to room with their stream
                    if (!room.AddPlayer(playerId, joinInfo.Initials, joinInfo.PlayerNumber, responseStream))
                    {
                        _logger.LogWarning("Failed to add player '{PlayerId}' to room {GameId}", playerId, gameId);
                        return;
                    }
                    
                    // Notify other players about the new player joining
                    var joinNotification = new GameMessage
                    {
                        GameId = gameId,
                        PlayerId = playerId,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        PlayerJoin = new PlayerJoinNotification
                        {
                            PlayerId = playerId,
                            Initials = joinInfo.Initials,
                            PlayerNumber = joinInfo.PlayerNumber,
                            CurrentPlayers = room.CurrentPlayers,
                            MaxPlayers = room.MaxPlayers
                        }
                    };
                    
                    _logger.LogInformation("Broadcasting player join notification for '{PlayerId}' to {Count} players", 
                        playerId, room.CurrentPlayers - 1);
                    await room.BroadcastMessageAsync(joinNotification, playerId);
                }
            }
            else
            {
                // Host - add to room if not already added
                if (room.GetPlayer(playerId) == null)
                {
                    room.AddPlayer(playerId, room.HostInitials, 1, responseStream);
                }
            }
            
            // Process incoming messages: apply input to server simulation, or forward control messages
            var readTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in requestStream.ReadAllAsync())
                    {
                        if (room == null) break;

                        // Server-authoritative: apply player input to simulation; do not relay position/bullet
                        if (message.PlayerInput != null)
                        {
                            room.ApplyInput(message.PlayerId, message.PlayerInput.MoveDx, message.PlayerInput.MoveDy,
                                message.PlayerInput.FireDx, message.PlayerInput.FireDy);
                            continue;
                        }

                        // Client notified server that player is leaving (e.g. pressed ESC)
                        if (message.PlayerLeave != null)
                        {
                            room.RemovePlayer(playerId);
                            _playerToRoom.TryRemove(playerId, out _);
                            _logger.LogInformation("Player {PlayerId} left game {GameId} (explicit leave)", playerId, gameId);
                            if (room.CurrentPlayers == 0)
                            {
                                room.StopSimulation();
                                _roomManager.RemoveRoom(gameId);
                                _logger.LogInformation("Game {GameId} abandoned (all players left). Room removed.", gameId);
                            }
                            return; // Exit read loop; stream will close and finally will run (RemovePlayer is idempotent)
                        }

                        // Relay other messages (gameStart, playerJoin, etc.) for backward compatibility
                        await room.BroadcastMessageAsync(message, playerId);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when client disconnects normally
                    _logger.LogInformation("Stream cancelled for player {PlayerId} (normal disconnect)", playerId);
                }
                catch (System.IO.IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.ConnectionAbortedException)
                {
                    // Expected when HTTP/2 connection is aborted (client closed connection)
                    _logger.LogInformation("Connection aborted for player {PlayerId} (client disconnected)", playerId);
                }
                catch (Exception ex)
                {
                    // Only log unexpected errors
                    _logger.LogError(ex, "Unexpected error reading from stream for player {PlayerId}", playerId);
                }
            });
            
            // Wait for client to disconnect
            await readTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GameStream for player {PlayerId}", playerId);
        }
        finally
        {
            // Clean up this player's connection
            if (playerId != null && gameId != null && room != null)
            {
                room.RemovePlayer(playerId);
                _playerToRoom.TryRemove(playerId, out _);
                _logger.LogInformation("Player {PlayerId} disconnected from game {GameId}", playerId, gameId);

                // If no players left, abandon the game to free resources
                if (room.CurrentPlayers == 0)
                {
                    room.StopSimulation();
                    _roomManager.RemoveRoom(gameId);
                    _logger.LogInformation("Game {GameId} abandoned (all players left). Room removed.", gameId);
                }
            }
        }
    }
    
    private string GenerateGameId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

public class PlayerJoinInfo
{
    public string PlayerId { get; set; } = "";
    public string Initials { get; set; } = "";
    public int PlayerNumber { get; set; }
    public string GameId { get; set; } = "";
}
