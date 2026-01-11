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
    
    public bool IsConnected => _isConnected && _stream != null;
    
    public async Task<bool> ConnectAsync(string? server = null)
    {
        try
        {
            var serverUrl = server ?? DefaultServer;
            
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
            throw new InvalidOperationException("Not connected");
        
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
                throw new Exception(response.ErrorMessage);
            }
            
            return response.GameId;
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Create game error: {ex.Message}");
            throw;
        }
    }
    
    public async Task<JoinResponse> JoinGameAsync(string gameId, string playerId, string initials)
    {
        if (_client == null || !_isConnected)
            throw new InvalidOperationException("Not connected");
        
        try
        {
            var request = new JoinRequest
            {
                GameId = gameId,
                PlayerId = playerId,
                Initials = initials
            };
            
            return await _client.JoinGameAsync(request);
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Join game error: {ex.Message}");
            throw;
        }
    }
    
    public async Task<bool> StartGameStreamAsync(string gameId, string playerId)
    {
        if (_client == null || !_isConnected)
            return false;
        
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
                    // Expected when disconnecting
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
        catch (Exception ex)
        {
            OnConnectionError?.Invoke($"Start stream error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> SendGameMessageAsync(GameMessage message)
    {
        if (_stream == null || !_isConnected)
            return false;
        
        try
        {
            await _stream.RequestStream.WriteAsync(message);
            return true;
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
