using Grpc.Net.Client;
using Grpc.Core;
using NSnipes.GrpcServer;

namespace NSnipes;

public class GrpcGameClient : IDisposable
{
    private GrpcChannel? _channel;
    private GameService.GameServiceClient? _client;
    private AsyncDuplexStreamingCall<GameMessage, GameMessage>? _stream;
    private Task? _receiveTask;
    private bool _isConnected = false;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // Events for game message handling
    public event Action<string, string>? OnMessageReceived; // messageType, jsonPayload (for compatibility)
    public event Action<GameMessage>? OnGameMessageReceived; // Direct gRPC message
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<string>? OnConnectionError;
    
    // Server configuration
    private const string DefaultServer = "http://localhost:5000";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);
    
    public bool IsConnected => _isConnected && _stream != null;
    
    public async Task<bool> ConnectAsync(string? server = null)
    {
        try
        {
            var serverUrl = server ?? DefaultServer;
            
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                OnConnectionError?.Invoke("Cannot connect: Server URL is required");
                return false;
            }
            
            // Configure HTTP/2 support for unencrypted connections (http://)
            // This is required for gRPC over plain HTTP
            // Must be set before creating any HttpClient or GrpcChannel
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            
            // Create channel - it will use HTTP/2 automatically when the switch is set
            _channel = GrpcChannel.ForAddress(serverUrl);
            _client = new GameService.GameServiceClient(_channel);
            
            _isConnected = true;
            OnConnected?.Invoke();
            
            return true;
        }
        catch (UriFormatException ex)
        {
            OnConnectionError?.Invoke($"Connection error: Invalid server URL format - {ex.Message}");
            return false;
        }
        catch (Grpc.Core.RpcException ex)
        {
            OnConnectionError?.Invoke($"Connection gRPC error: {ex.Status.Detail}");
            return false;
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Connection error: {ex.Message}");
            return false;
        }
    }
    
    public async Task DisconnectAsync()
    {
        _isConnected = false;
        
        if (_stream != null)
        {
            await _stream.RequestStream.CompleteAsync();
            _stream.Dispose();
            _stream = null;
        }
        
        _cancellationTokenSource.Cancel();
        
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch { }
            _receiveTask = null;
        }
        
        _channel?.Dispose();
        _channel = null;
        
        OnDisconnected?.Invoke();
    }
    
    public async Task<string> CreateGameAsync(string hostPlayerId, string hostInitials, int maxPlayers, int startingLevel)
    {
        if (_client == null || !_isConnected)
        {
            var error = "Cannot create game: Not connected to server";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error);
        }
        
        try
        {
            var request = new CreateGameRequest
            {
                HostPlayerId = hostPlayerId,
                HostInitials = hostInitials,
                MaxPlayers = maxPlayers,
                StartingLevel = startingLevel
            };
            
            var response = await _client.CreateGameAsync(request);
            
            if (!response.Success)
            {
                var error = $"Create game failed: {response.ErrorMessage}";
                OnConnectionError?.Invoke(error);
                throw new InvalidOperationException(error);
            }
            
            return response.GameId;
        }
        catch (InvalidOperationException)
        {
            // Re-throw InvalidOperationException (already logged)
            throw;
        }
        catch (Grpc.Core.RpcException ex)
        {
            var error = $"Create game gRPC error: {ex.Status.Detail}";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error, ex);
        }
        catch (Exception ex)
        {
            var error = $"Create game error: {ex.Message}";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error, ex);
        }
    }
    
    public async Task<JoinResponse> JoinGameAsync(string gameId, string playerId, string initials)
    {
        if (_client == null || !_isConnected)
        {
            var error = "Cannot join game: Not connected to server";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error);
        }
        
        if (string.IsNullOrWhiteSpace(gameId))
        {
            var error = "Cannot join game: Game ID is required";
            OnConnectionError?.Invoke(error);
            throw new ArgumentException(error, nameof(gameId));
        }
        
        if (string.IsNullOrWhiteSpace(playerId))
        {
            var error = "Cannot join game: Player ID is required";
            OnConnectionError?.Invoke(error);
            throw new ArgumentException(error, nameof(playerId));
        }
        
        try
        {
            var request = new JoinRequest
            {
                GameId = gameId,
                PlayerId = playerId,
                Initials = initials
            };
            
            using var cts = new CancellationTokenSource(OperationTimeout);
            return await _client.JoinGameAsync(request, cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Re-throw InvalidOperationException (already logged)
            throw;
        }
        catch (ArgumentException)
        {
            // Re-throw ArgumentException (already logged)
            throw;
        }
        catch (OperationCanceledException)
        {
            var error = "Join game timed out";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error);
        }
        catch (Grpc.Core.RpcException ex)
        {
            var error = $"Join game gRPC error: {ex.Status.Detail}";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error, ex);
        }
        catch (Exception ex)
        {
            var error = $"Join game error: {ex.Message}";
            OnConnectionError?.Invoke(error);
            throw new InvalidOperationException(error, ex);
        }
    }
    
    public async Task<bool> StartGameStreamAsync(string gameId, string playerId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            OnConnectionError?.Invoke("Cannot start stream: Game ID is required");
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(playerId))
        {
            OnConnectionError?.Invoke("Cannot start stream: Player ID is required");
            return false;
        }
        
        if (_client == null || !_isConnected)
        {
            OnConnectionError?.Invoke("Cannot start stream: Not connected to server");
            return false;
        }
        
        try
        {
            _stream = _client.GameStream();
            
            // Start receiving messages
            _receiveTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in _stream.ResponseStream.ReadAllAsync(_cancellationTokenSource.Token))
                    {
                        // Ignore messages from self
                        if (message.PlayerId == playerId)
                            continue;
                        
                        OnGameMessageReceived?.Invoke(message);
                        
                        // For compatibility with existing code, also fire OnMessageReceived
                        // Convert message to JSON-like format
                        var messageType = GetMessageType(message);
                        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(message);
                        OnMessageReceived?.Invoke(messageType, jsonPayload);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when disconnecting - don't log as error
                }
                catch (Grpc.Core.RpcException ex)
                {
                    OnConnectionError?.Invoke($"Stream receive gRPC error: {ex.Status.Detail}");
                }
                catch (Exception ex)
                {
                    OnConnectionError?.Invoke($"Stream receive error: {ex.Message}");
                }
            });
            
            // Send initial message to establish connection
            var initialMessage = new GameMessage
            {
                GameId = gameId,
                PlayerId = playerId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            await _stream.RequestStream.WriteAsync(initialMessage);
            
            return true;
        }
        catch (Grpc.Core.RpcException ex)
        {
            OnConnectionError?.Invoke($"Start stream gRPC error: {ex.Status.Detail}");
            return false;
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Start stream error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> SendGameMessageAsync(GameMessage message)
    {
        if (message == null)
        {
            OnConnectionError?.Invoke("Cannot send message: Message is null");
            return false;
        }
        
        if (_stream == null || !_isConnected)
        {
            OnConnectionError?.Invoke("Cannot send message: Not connected to server");
            return false;
        }
        
        try
        {
            await _stream.RequestStream.WriteAsync(message);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting - don't log as error
            return false;
        }
        catch (Grpc.Core.RpcException ex)
        {
            OnConnectionError?.Invoke($"Send message gRPC error: {ex.Status.Detail}");
            return false;
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Send message error: {ex.Message}");
            return false;
        }
    }
    
    private string GetMessageType(GameMessage message)
    {
        // Convert gRPC message to topic-like string for compatibility
        if (message.Position != null) return "position";
        if (message.Bullet != null) return "bullet";
        if (message.State != null) return "state";
        if (message.Snipes != null) return "snipes";
        if (message.Hives != null) return "hives";
        if (message.GameStart != null) return "gameStart";
        if (message.GameOver != null) return "gameOver";
        if (message.PlayerJoin != null) return "playerJoin";
        if (message.PlayerCount != null) return "playerCount";
        if (message.Respawn != null) return "respawn";
        return "unknown";
    }
    
    public void Dispose()
    {
        DisconnectAsync().Wait(1000);
        _cancellationTokenSource.Dispose();
    }
}
