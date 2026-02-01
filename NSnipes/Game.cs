using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NSnipes.GrpcServer;
using DrawingAttribute = Terminal.Gui.Drawing.Attribute;
using GameMessage = NSnipes.GrpcServer.GameMessage;
using PlayerPositionUpdate = NSnipes.GrpcServer.PlayerPositionUpdate;
using BulletUpdate = NSnipes.GrpcServer.BulletUpdate;

namespace NSnipes;

public class Game : Window, Terminal.Gui.App.IRunnable
{
    private readonly IApplication _app;
    
    // IRunnable implementation
    public void Run()
    {
        // Window already handles its own lifecycle
        // This method is called by app.Run()
    }
    private readonly Map _map;
    private readonly Player _player;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private bool _mapDrawn = false;
    private readonly List<Bullet> _bullets = new List<Bullet>(MaxBullets);
    private readonly List<Hive> _hives = new List<Hive>(MaxHives);
    private readonly List<Snipe> _snipes = new List<Snipe>(MaxSnipes);
    private readonly GameState _gameState = new GameState();
    /// <summary>Protects game state collections when updated from network (HandleGameStateSnapshot) while UI thread iterates them in draw methods.</summary>
    private readonly object _gameStateLock = new object();
    
    // Game configuration constants
    private const int MaxBullets = 10;
    private const int MaxHives = 15; // Max ~15 hives at higher levels
    private const int MaxSnipes = 100; // Can have many active snipes
    private const double BulletSpeed = 1.0; // Bullets move 1.0 cell per update (10ms) to ensure proper wall collision
    private const int StatusBarHeight = 2; // First 2 rows reserved for status information
    
    // Player constants
    private const int PlayerWidth = 2; // Player is 2 columns wide [X, X+1]
    private const int PlayerHeight = 3; // Player is 3 rows tall [Y, Y+1, Y+2]
    
    // Score constants
    private const int SnipeKillScore = 25;
    private const int HiveBaseScore = 500;
    private const int SnipePerHiveScore = 25; // Score per unreleased snipe when hive is destroyed
    
    // Timer intervals (milliseconds)
    private const int IntroAnimationIntervalMs = 16; // ~60fps for intro screen animation
    private const int PlayerAnimationIntervalMs = 40; // More responsive movement
    private const int BulletUpdateIntervalMs = 10; // Smooth bullet movement
    private const int HiveAnimationIntervalMs = 75; // Slower color change for better performance
    private const int PositionPublishPeriodicIntervalMs = 200; // Periodic position publish even when not moving
    private const int SnipeUpdateIntervalMs = 200; // Snipe spawning and movement
    private const int StreamConnectionDelayMs = 300; // Delay to ensure stream is fully connected
    private const int GameStatePublishDelayMs = 200; // Delay to ensure all players' streams are active
    private const int SubscriptionDelayMs = 100; // Small delay to ensure subscriptions are active
    
    // Multiplayer constants
    private const int MaxNetworkPlayers = 10; // Max 5 players, 10 for safety
    private const int MaxDirections = 8; // Max 8 directions (cardinal + diagonal)
    private const int MultiplayerWaitTimeSeconds = 60; // Wait time for players to join

    // Performance optimization: Track previous positions to avoid unnecessary redraws
    private int _previousPlayerViewportX = -1;
    private int _previousPlayerViewportY = -1;
    // Cached map viewport - null indicates cache is invalid/dirty
    // Using null for invalidation is simple and effective (no need for IsDirty flag)
    private string[]? _cachedMapViewport = null;
    private DateTime _cachedDateTime = DateTime.MinValue;
    
    // Frame rate tracking using circular buffer for better performance
    private DateTime _lastFrameTime = DateTime.Now;
    private double _currentFPS = 0.0;
    private readonly double[] _fpsHistory = new double[FpsHistorySize];
    private int _fpsHistoryIndex = 0;
    private int _fpsHistoryCount = 0; // Track how many entries are valid
    private const int FpsHistorySize = 10; // Average over last 10 frames
    
    // Cached DateTime for current frame/update cycle to avoid excessive DateTime.Now calls
    private DateTime _currentFrameTime = DateTime.Now;

    // Intro screen
    private IntroScreen _introScreen;
    private GameConfig _config;
    
    // Multiplayer
    private GrpcGameClient? _grpcClient;
    private GameSession? _gameSession;
    private Dictionary<string, PlayerNetwork> _networkPlayers = new Dictionary<string, PlayerNetwork>(MaxNetworkPlayers);
    private bool _isMultiplayer = false;
    /// <summary>True after we've received at least one GameStateSnapshot that set our position (avoids broken map for joining player).</summary>
    private bool _hasReceivedMultiplayerSnapshot = false;
    private int _positionSequence = 0; // Sequence number for position updates
    private DateTime _lastPositionPublish = DateTime.Now;
    private const int PositionPublishThrottleMs = 20; // Publish position every 20ms when moved for smoother updates

    // Key state tracking for smooth movement
    private Dictionary<string, DateTime> _pressedKeys = new Dictionary<string, DateTime>(MaxDirections);
    private const int KeyRepeatThresholdMs = 60; // Consider key released if not seen in 60ms (reduced for faster response)

    public Game(IApplication app)
    {
        _app = app;
        // Load configuration (initials)
        _config = GameConfig.Load();

        _map = new Map();
        var (x, y) = FindRandomValidPosition();
        _player = new Player(x, y);
        _player.Initials = _config.Initials; // Use loaded initials

        // Initialize game state and hives
        InitializeHives();

        // Initialize intro screen
        _introScreen = new IntroScreen(_config, _gameState);
        _introScreen.OnStartGame += (level) =>
        {
            // Level is already set in game state by IntroScreen
            ResetGame(); // Reset all game state for a new game (preserves level)
        };
        _introScreen.OnRespawnComplete += () =>
        {
            // When respawn clearing effect completes, ensure map and status bar are redrawn
            _mapDrawn = false; // Force redraw of map (which will also redraw status bar)
        };
        _introScreen.OnExit += () =>
        {
            _app.RequestStop();
        };
        _introScreen.OnInitialsChanged += (initials) =>
        {
            _player.Initials = initials;
        };
        _introScreen.OnReturnToIntro += () =>
        {
            // Reset game state when returning to intro screen
            _mapDrawn = false;
            _pressedKeys.Clear(); // Clear any lingering pressed keys
            // Disconnect from multiplayer if connected
            if (_grpcClient != null)
            {
                _grpcClient.Dispose();
                _grpcClient = null;
            }
            _gameSession = null;
            _isMultiplayer = false;
            _hasReceivedMultiplayerSnapshot = false;
            _networkPlayers.Clear();
        };
        _introScreen.OnStartMultiplayerGame += async (maxPlayers) =>
        {
            await StartMultiplayerGame(maxPlayers);
        };
        _introScreen.OnJoinGame += async (gameId) =>
        {
            await JoinGame(gameId);
        };
        _introScreen.SetMapCharCallback((x, y) => GetMapCharAtPosition(x, y));
        
        // Add IntroScreen as a child view so it can render
        Add(_introScreen);
        
        _introScreen.Show();
        
        // Remove window border and title
        Title = ""; // Empty title to hide it
        
        // Set Border thickness to zero to remove the border completely
        // Thickness constructor takes (top, right, bottom, left)
        if (Border != null)
        {
            Border.Thickness = new Thickness(0, 0, 0, 0);
        }
        
        // Make window fill entire screen
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // Prevent default Escape key behavior (we handle it ourselves)
        CanFocus = true;

        // Handle keyboard input using IApplication.Keyboard.KeyDown
        _app.Keyboard.KeyDown += (sender, key) =>
        {
            // Handle CTRL-C for global application exit
            // Use Key API: check if key is CTRL-C (Ctrl+C)
            if (key.IsCtrl && key == Key.C)
            {
                // Exit application
                _app.RequestStop();
                return;
            }
            
            // Handle Escape key for returning to menu (not exit)
            // Use Key.Esc for proper Escape key detection
            if (key == Key.Esc)
            {
                if (!_introScreen.IsActive)
                {
                    // Return to intro screen from game
                    _introScreen.Show();
                    _introScreen.SetNeedsDraw();
                    return;
                }
                // If intro screen is active, pass Escape to HandleKeyDown so it can reach IntroScreen
                // This allows Escape to work in input screens (level select, player count, etc.)
            }
            
            // For other keys, handle them inline
            HandleKeyDown(sender, key);
        };

        // Also handle at Window level as backup
        KeyDown += HandleWindowKeyDown;
        
        // Note: Application.SizeChanging doesn't exist in v2
        // Size changes will be detected in the timers via dimension checks

        // Timer for intro screen animation and clearing effects
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(IntroAnimationIntervalMs), () =>
        {
            if (_introScreen.IsActive || _introScreen.IsClearingScreen || _introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
            {
                _introScreen.SetNeedsDraw();
            }
            // Note: Game drawing is triggered by other timers when needed (movement, bullets, etc.)
            return true;
        });

        // Timer for player animation, movement, and initial map draw
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(PlayerAnimationIntervalMs), () =>
        {
            // Cache DateTime.Now at start of update cycle
            _currentFrameTime = DateTime.Now;
            
            if (!_introScreen.IsActive && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey && !_mapDrawn)
            {
                _mapDrawn = true;
                SetNeedsDraw();
            }
            else if (!_introScreen.IsActive && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                // Check if window dimensions have changed (e.g., from resize)
                if (true)
                {
                    int currentWidth = Frame.Width;
                    int currentHeight = Frame.Height;
                    int frameWidth = currentWidth;
                    int frameHeight = currentHeight - StatusBarHeight;
                    
                    // If dimensions changed, invalidate cache and redraw everything
                    if (frameWidth != _lastFrameWidth || frameHeight != _lastFrameHeight)
                    {
                        _cachedMapViewport = null;
                        SetNeedsDraw();
                        return true;
                    }
                }
                
                // Process continuous movement based on pressed keys
                bool playerMoved = ProcessPlayerMovement();
                if (playerMoved)
                {
                    // Player moved - trigger redraw
                    SetNeedsDraw();
                }
                else
                {
                    // Player didn't move - just redraw player animation
                    DrawPlayer();
                }
            }
            return true;
        });

        // Separate timer for bullet updates
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(BulletUpdateIntervalMs), () =>
        {
            // Cache DateTime.Now at start of update cycle
            _currentFrameTime = DateTime.Now;
            
            if (_mapDrawn && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey && !_introScreen.IsActive)
            {
                // Check if window dimensions have changed (e.g., from resize) - check frequently for responsive resize
                if (true)
                {
                    int currentWidth = Frame.Width;
                    int currentHeight = Frame.Height;
                    int frameWidth = currentWidth;
                    int frameHeight = currentHeight - StatusBarHeight;
                    
                    // If dimensions changed, invalidate cache and redraw everything
                    if (frameWidth != _lastFrameWidth || frameHeight != _lastFrameHeight)
                    {
                        _cachedMapViewport = null;
                        SetNeedsDraw();
                        return true;
                    }
                }
                
                // Server-authoritative: in multiplayer server runs simulation and sends state
                if (!_isMultiplayer)
                    UpdateBullets();
                SetNeedsDraw();
            }
            return true;
        });

        // Separate timer for hive animation
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(HiveAnimationIntervalMs), () =>
        {
            if (_mapDrawn && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                SetNeedsDraw(); // Trigger redraw for hives and status bar
            }
            return true;
        });
        
        // Periodic position update timer for multiplayer (publish position even if not moving)
        // This ensures other players can see this player even when stationary
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(PositionPublishPeriodicIntervalMs), () =>
        {
            // Cache DateTime.Now at start of update cycle
            _currentFrameTime = DateTime.Now;
            
            if (_isMultiplayer && _gameSession != null && _gameSession.Status == GameSessionStatus.Playing &&
                _grpcClient != null && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver)
            {
                // Server-authoritative: send current move input so server can update simulation
                var (moveDx, moveDy) = CalculateMovementDirection();
                _ = _grpcClient.SendInputAsync(_gameSession.GameId, _gameSession.PlayerId, moveDx, moveDy, 0, 0);
            }
            return true;
        });

        // Timer for snipe spawning and movement - only host runs this
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(SnipeUpdateIntervalMs), () =>
        {
            // Cache DateTime.Now at start of update cycle
            _currentFrameTime = DateTime.Now;
            
            if (_mapDrawn && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                // Server-authoritative: server runs simulation and sends state; no local snipe/hive updates in multiplayer
                if (!_isMultiplayer)
                {
                    SpawnSnipes();
                    UpdateSnipes();
                }
                SetNeedsDraw();
            }
            return true;
        });
    }

    private void HandleWindowKeyDown(object? sender, Key key)
    {
        // Handle CTRL-C for global application exit
        // Use Key API: check if key is CTRL-C (Ctrl+C)
        if (key.IsCtrl && key == Key.C)
        {
            // Exit application
            _app.RequestStop();
            return;
        }
        
        // Handle Escape key for returning to menu (not exit)
        // Use Key.Esc for proper Escape key detection
        if (key == Key.Esc)
        {
            if (!_introScreen.IsActive)
            {
                // Return to intro screen from game
                _introScreen.Show();
                return;
            }
            // If intro screen is active, let it handle Escape (for canceling input, etc.)
            // Don't return here - let it fall through to HandleKeyDown so IntroScreen can process it
        }

        // For other keys, let them process normally
    }

    protected override bool OnDrawingContent(DrawContext? dc)
    {
        if (dc == null || !IsInitialized)
            return false;

        // Only draw game content when intro screen is not active
        if (!_introScreen.IsActive && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
        {
            DrawMapAndPlayer();
        }

        return base.OnDrawingContent(dc);
    }

    private void HandleKeyDown(object? sender, Key key)
    {
        // Handle intro screen key press (including game over and Escape key)
        // This must be called BEFORE checking if Escape should be skipped
        // so that Escape can be processed by input handlers
        if (_introScreen.HandleKey(key))
        {
            return; // Intro screen handled the key
        }

        // Escape is handled at Application level for non-intro-screen cases
        // Use Key.Esc for proper Escape key detection
        if (key == Key.Esc)
        {
            return;
        }

        // Don't process game keys if intro screen is active or game is over
        if (_introScreen.IsActive || _introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
        {
            return;
        }

        if (!IsInitialized)
            return;

        // Track movement keys for continuous movement
        // Normalize key names so they match what ProcessPlayerMovement expects
        bool movementKeyPressed = false;
        string? normalizedKey = null;
        
        // Check for movement keys (arrow keys and numeric keypad)
        // Use Key API for cursor keys
        if (key == Key.CursorUp)
        {
            normalizedKey = "Up";
            movementKeyPressed = true;
        }
        else if (key == Key.CursorDown)
        {
            normalizedKey = "Down";
            movementKeyPressed = true;
        }
        else if (key == Key.CursorLeft)
        {
            normalizedKey = "Left";
            movementKeyPressed = true;
        }
        else if (key == Key.CursorRight)
        {
            normalizedKey = "Right";
            movementKeyPressed = true;
        }
        else
        {
            // Check for numeric keypad keys (1-9)
            // Use Key API for digit keys
            if (key == Key.D1) { normalizedKey = "1"; movementKeyPressed = true; }
            else if (key == Key.D2) { normalizedKey = "2"; movementKeyPressed = true; }
            else if (key == Key.D3) { normalizedKey = "3"; movementKeyPressed = true; }
            else if (key == Key.D4) { normalizedKey = "4"; movementKeyPressed = true; }
            else if (key == Key.D5) { normalizedKey = "5"; movementKeyPressed = true; }
            else if (key == Key.D6) { normalizedKey = "6"; movementKeyPressed = true; }
            else if (key == Key.D7) { normalizedKey = "7"; movementKeyPressed = true; }
            else if (key == Key.D8) { normalizedKey = "8"; movementKeyPressed = true; }
            else if (key == Key.D9) { normalizedKey = "9"; movementKeyPressed = true; }
        }
        
        if (movementKeyPressed && normalizedKey != null)
        {
            // Update key state - mark this key as currently pressed with current time
            _pressedKeys[normalizedKey] = _currentFrameTime;
            
            // If a movement key was just pressed, immediately process movement
            // This provides instant response when changing directions
            if (!_introScreen.IsActive && !_introScreen.IsClearingScreen && 
                !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                // Process movement immediately for instant response
                if (ProcessPlayerMovement())
                {
                    // Player moved - redraw to show the movement immediately
                    DrawMapAndPlayer();
                }
            }
            
            // Movement key handled - don't process as bullet firing key
            return;
        }

        // Handle bullet firing (q, w, e, a, d, z, x, c)
        // Player is PlayerWidth columns wide [X, X+1] and PlayerHeight rows tall [Y, Y+1, Y+2]
        // In multiplayer, always send fire input; server enforces MaxBullets. In single-player, check local count.
        if (_isMultiplayer || _bullets.Count < MaxBullets)
        {
            double startX = 0;
            double startY = 0;
            double velX = 0;
            double velY = 0;
            bool shouldFire = false;

            // Use Key API to check for firing keys
            // Remove modifiers to compare base key (case-insensitive)
            Key keyNoModifiers = key.NoShift.NoCtrl.NoAlt;
            
            // Check for firing keys (Q, W, E, A, D, Z, X, C) - case-insensitive
            if (keyNoModifiers == Key.Q || keyNoModifiers == Key.Q.WithShift)
            {
                // Diagonal left/up - fire from top-left corner
                startX = _player.X;
                startY = _player.Y;
                velX = -BulletSpeed;
                velY = -BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.W || keyNoModifiers == Key.W.WithShift)
            {
                // Up - fire from top center
                startX = _player.X + 0.5; // Center of player width
                startY = _player.Y;
                velY = -BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.E || keyNoModifiers == Key.E.WithShift)
            {
                // Diagonal right/up - fire from top-right corner
                startX = _player.X + (PlayerWidth - 1); // Right column
                startY = _player.Y;
                velX = BulletSpeed;
                velY = -BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.A || keyNoModifiers == Key.A.WithShift)
            {
                // Left - fire from left center
                startX = _player.X;
                startY = _player.Y + 1.0;
                velX = -BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.D || keyNoModifiers == Key.D.WithShift)
            {
                // Right - fire from right center
                startX = _player.X + (PlayerWidth - 1); // Right column
                startY = _player.Y + 1.0; // Middle row
                velX = BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.Z || keyNoModifiers == Key.Z.WithShift)
            {
                // Diagonal left/down - fire from bottom-left corner
                startX = _player.X;
                startY = _player.Y + (PlayerHeight - 1); // Bottom row
                velX = -BulletSpeed;
                velY = BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.X || keyNoModifiers == Key.X.WithShift)
            {
                // Down - fire from bottom center
                startX = _player.X + 0.5; // Center of player width
                startY = _player.Y + (PlayerHeight - 1); // Bottom row
                velY = BulletSpeed;
                shouldFire = true;
            }
            else if (keyNoModifiers == Key.C || keyNoModifiers == Key.C.WithShift)
            {
                // Diagonal right/down - fire from bottom-right corner
                startX = _player.X + (PlayerWidth - 1); // Right column
                startY = _player.Y + (PlayerHeight - 1); // Bottom row
                velX = BulletSpeed;
                velY = BulletSpeed;
                shouldFire = true;
            }

            if (shouldFire && (velX != 0 || velY != 0))
            {
                // Server-authoritative: in multiplayer send fire input only; server adds bullet and sends state
                if (_isMultiplayer && _gameSession != null && _grpcClient != null)
                {
                    int fireDx = velX > 0 ? 1 : (velX < 0 ? -1 : 0);
                    int fireDy = velY > 0 ? 1 : (velY < 0 ? -1 : 0);
                    _ = _grpcClient.SendInputAsync(_gameSession.GameId, _gameSession.PlayerId, 0, 0, fireDx, fireDy);
                    return;
                }

                string? playerId = _gameSession?.PlayerId;
                var bullet = new Bullet(startX, startY, velX, velY, playerId: playerId, createdAt: _currentFrameTime);
                _bullets.Add(bullet);
                
                // Redraw to show the new bullet
                if (_mapDrawn)
                {
                    DrawFrame();
                }
            }
        }
    }

    /// <summary>
    /// Cleans up old key presses that haven't been seen recently (keys are considered released)
    /// </summary>
    private void CleanupOldKeyPresses()
    {
        var keysToRemove = new List<string>(_pressedKeys.Count); // Pre-allocate with expected size
        foreach (var kvp in _pressedKeys)
        {
            // Remove keys that haven't been refreshed recently
            // This detects when a key is released (no more key repeat events)
            if ((_currentFrameTime - kvp.Value).TotalMilliseconds > KeyRepeatThresholdMs)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _pressedKeys.Remove(key);
        }
    }

    /// <summary>
    /// Calculates movement direction based on currently pressed keys
    /// </summary>
    private (int deltaX, int deltaY) CalculateMovementDirection()
    {
        int deltaX = 0;
        int deltaY = 0;

        // Check for cardinal directions (arrow keys and keypad)
        bool upPressed = _pressedKeys.ContainsKey("Up") || _pressedKeys.ContainsKey("8");
        bool downPressed = _pressedKeys.ContainsKey("Down") || _pressedKeys.ContainsKey("2");
        bool leftPressed = _pressedKeys.ContainsKey("Left") || _pressedKeys.ContainsKey("4");
        bool rightPressed = _pressedKeys.ContainsKey("Right") || _pressedKeys.ContainsKey("6");

        // Check for diagonal keypad keys
        bool upLeftPressed = _pressedKeys.ContainsKey("7");
        bool upRightPressed = _pressedKeys.ContainsKey("9");
        bool downLeftPressed = _pressedKeys.ContainsKey("1");
        bool downRightPressed = _pressedKeys.ContainsKey("3");

        // Handle diagonal keypad keys (they take priority)
        if (upLeftPressed)
        {
            deltaX = -1;
            deltaY = -1;
        }
        else if (upRightPressed)
        {
            deltaX = 1;
            deltaY = -1;
        }
        else if (downLeftPressed)
        {
            deltaX = -1;
            deltaY = 1;
        }
        else if (downRightPressed)
        {
            deltaX = 1;
            deltaY = 1;
        }
        else
        {
            // Handle cardinal directions (can combine for diagonal movement)
            if (upPressed) deltaY = -1;
            if (downPressed) deltaY = 1;
            if (leftPressed) deltaX = -1;
            if (rightPressed) deltaX = 1;
        }

        return (deltaX, deltaY);
    }

    /// <summary>
    /// Checks if the player can move to the given world position using map walkability (same logic as server).
    /// Ensures client collision matches server and is independent of viewport alignment.
    /// </summary>
    private bool CanMoveToWorld(int newWorldX, int newWorldY)
    {
        newWorldX = _map.WrapX(newWorldX);
        newWorldY = _map.WrapY(newWorldY);

        // Check every cell the player would occupy (2 wide x PlayerHeight tall), using wrapped coords so edge wrap works
        for (int dy = 0; dy < PlayerHeight; dy++)
        for (int dx = 0; dx < PlayerWidth; dx++)
        {
            int cx = _map.WrapX(newWorldX + dx);
            int cy = _map.WrapY(newWorldY + dy);
            if (!_map.IsWalkable(cx, cy))
                return false;
        }

        // Check player-to-player collision in multiplayer (cell-based, wrapped, same as server)
        if (_isMultiplayer && _gameSession != null)
        {
            List<PlayerNetwork> playersCopy;
            lock (_gameStateLock) { playersCopy = new List<PlayerNetwork>(_networkPlayers.Values); }
            foreach (var networkPlayer in playersCopy)
            {
                if (networkPlayer.PlayerId == _gameSession.PlayerId)
                    continue;
                for (int dy = 0; dy < PlayerHeight; dy++)
                for (int dx = 0; dx < PlayerWidth; dx++)
                {
                    int cx = _map.WrapX(newWorldX + dx);
                    int cy = _map.WrapY(newWorldY + dy);
                    for (int ody = 0; ody < PlayerHeight; ody++)
                    for (int odx = 0; odx < PlayerWidth; odx++)
                    {
                        if (_map.WrapX(networkPlayer.X + odx) == cx && _map.WrapY(networkPlayer.Y + ody) == cy)
                            return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Executes player movement and handles map wrapping
    /// </summary>
    private void ExecutePlayerMove(int deltaX, int deltaY)
    {
        _player.X += deltaX;
        _player.Y += deltaY;

        // Wrap to valid range [0, MapWidth-1] x [0, MapHeight-1] (e.g. x=-1 wraps to right edge)
        _player.X = _map.WrapX(_player.X);
        _player.Y = _map.WrapY(_player.Y);

        // Invalidate cached map since player moved
        _cachedMapViewport = null;

        // Server-authoritative: send move input so server can update simulation
        if (_isMultiplayer && _gameSession != null && _grpcClient != null)
        {
            _ = _grpcClient.SendInputAsync(_gameSession.GameId, _gameSession.PlayerId, deltaX, deltaY, 0, 0);
        }
    }

    private bool ProcessPlayerMovement()
    {
        if (!IsInitialized || _introScreen.IsClearingScreen || _introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
            return false;

        CleanupOldKeyPresses();
        if (_pressedKeys.Count == 0)
            return false;

        var (deltaX, deltaY) = CalculateMovementDirection();
        if (deltaX == 0 && deltaY == 0)
            return false;

        int newWorldX = _player.X + deltaX;
        int newWorldY = _player.Y + deltaY;

        if (CanMoveToWorld(newWorldX, newWorldY))
        {
            ExecutePlayerMove(deltaX, deltaY);
            return true;
        }

        return false;
    }

    private void DrawMapAndPlayer()
    {
        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;

        // Ensure we have valid dimensions (need at least status bar + some map)
        if (currentWidth < 3 || currentHeight < StatusBarHeight + 3)
            return;

        int frameWidth = currentWidth;
        int frameHeight = currentHeight - StatusBarHeight; // Account for status bar

        // Joining player: don't draw map until we've received at least one snapshot with our position (avoids broken/misaligned map and invisible walls)
        if (_isMultiplayer && !_hasReceivedMultiplayerSnapshot)
        {
            _lastFrameWidth = frameWidth;
            _lastFrameHeight = frameHeight;
            DrawStatusBar();
            SetAttribute(new DrawingAttribute(Color.Yellow, Color.Black));
            int msgRow = StatusBarHeight + (frameHeight / 2) - 1;
            if (msgRow >= StatusBarHeight && msgRow < currentHeight)
            {
                Move(0, msgRow);
                string msg = " Syncing game state... ";
                if (msg.Length < frameWidth)
                    msg = msg.PadRight(frameWidth);
                else
                    msg = msg.Substring(0, frameWidth);
                this.AddString(msg);
            }
            SetAttribute(new DrawingAttribute(Color.White, Color.Black));
            return;
        }

        // Use a single captured position for the whole draw to avoid torn reads when snapshot updates _player mid-draw
        int centerX = _player.X;
        int centerY = _player.Y;

        // Check if dimensions changed - if so, we need to clear and redraw everything
        bool dimensionsChanged = (frameWidth != _lastFrameWidth || frameHeight != _lastFrameHeight);
        
        _lastFrameWidth = frameWidth;
        _lastFrameHeight = frameHeight;
        
        // If dimensions changed, clear the entire game area first (especially important if window got smaller)
        if (dimensionsChanged)
        {
            SetAttribute(new DrawingAttribute(Color.Black, Color.Black));
            // Clear from status bar to bottom of screen
            for (int r = StatusBarHeight; r < currentHeight; r++)
            {
                Move(0, r);
                this.AddString(new string(' ', currentWidth));
            }
        }

        var map = _map.GetMap(frameWidth, frameHeight, centerX, centerY);

        // Cache map viewport for reuse in other drawing functions
        _cachedMapViewport = map;

        // Draw status bar first
        DrawStatusBar();

        SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));

        // draw the maze - start at row StatusBarHeight (after status bar)
        for (int r = 0; r < frameHeight; r++)
        {
            Move(0, r + StatusBarHeight);
            this.AddString(map[r]);
        }

        DrawPlayer();
        DrawRemotePlayers(); // Draw remote players in multiplayer
        DrawBullets();
        DrawHives();
        DrawSnipes();
        _mapDrawn = true; // Mark that map has been drawn

        // Update previous player viewport position
        _previousPlayerViewportX = frameWidth / 2;
        _previousPlayerViewportY = frameHeight / 2;
    }

    private void DrawFrame()
    {
        // Don't draw if in intro screen or game over
        if (_introScreen.IsActive || _introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
            return;
            
        // Track frame rate
        UpdateFrameRate();

        DrawPlayerWithClearing();
        DrawRemotePlayersWithClearing(); // Draw remote players with clearing for smooth movement
        DrawBullets();
        // Hives and snipes are drawn on their own timers for better performance
    }
    
    private void UpdateFrameRate()
    {
        double elapsedMs = (_currentFrameTime - _lastFrameTime).TotalMilliseconds;
        
        if (elapsedMs > 0)
        {
            // Calculate FPS for this frame
            double frameFPS = 1000.0 / elapsedMs;
            
            // Add to circular buffer
            _fpsHistory[_fpsHistoryIndex] = frameFPS;
            _fpsHistoryIndex = (_fpsHistoryIndex + 1) % FpsHistorySize;
            if (_fpsHistoryCount < FpsHistorySize)
                _fpsHistoryCount++;
            
            // Calculate average FPS using circular buffer
            if (_fpsHistoryCount > 0)
            {
                double sum = 0.0;
                for (int i = 0; i < _fpsHistoryCount; i++)
                {
                    sum += _fpsHistory[i];
                }
                _currentFPS = sum / _fpsHistoryCount;
            }
        }
        
        _lastFrameTime = _currentFrameTime;
    }

    private void DrawPlayerWithClearing()
    {
        // Clear previous player position before drawing new position
        if (_previousPlayerViewportX >= 0 && _previousPlayerViewportY >= 0 && _cachedMapViewport != null)
        {
            int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : Frame.Width;
            int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (Frame.Height - StatusBarHeight);

            if (_previousPlayerViewportX < frameWidth && _previousPlayerViewportY < frameHeight &&
                _previousPlayerViewportY >= 0 && _previousPlayerViewportY < _cachedMapViewport.Length &&
                _previousPlayerViewportX >= 0 && _previousPlayerViewportX < _cachedMapViewport[_previousPlayerViewportY].Length)
            {
                SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
                // Clear all cells of player (PlayerWidth x PlayerHeight)
                for (int row = 0; row < PlayerHeight; row++)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        int clearX = _previousPlayerViewportX + col;
                        int clearY = _previousPlayerViewportY + row;
                        if (clearX < frameWidth && clearY < frameHeight &&
                            clearY >= 0 && clearY < _cachedMapViewport.Length &&
                            clearX >= 0 && clearX < _cachedMapViewport[clearY].Length)
                        {
                            char mapChar = _cachedMapViewport[clearY][clearX];
                            Move(clearX, clearY + StatusBarHeight);
                            AddRune(mapChar);
                        }
                    }
                }
            }
        }

        // Draw player at new position
        DrawPlayer();

        // Update previous viewport position
        int frameWidth2 = _lastFrameWidth != 0 ? _lastFrameWidth : Frame.Width;
        int frameHeight2 = _lastFrameHeight != 0 ? _lastFrameHeight : (Frame.Height - StatusBarHeight);
        _previousPlayerViewportX = frameWidth2 / 2;
        _previousPlayerViewportY = frameHeight2 / 2;
    }

    /// <summary>
    /// Clears a bullet at the specified viewport position
    /// </summary>
    private void ClearBulletAtPosition(int viewportX, int viewportY, int frameWidth, int frameHeight, in string[] map)
    {
        if (viewportX >= 0 && viewportX < frameWidth &&
            viewportY >= 0 && viewportY < frameHeight &&
            map != null && viewportY >= 0 && viewportY < map.Length &&
            viewportX >= 0 && viewportX < map[viewportY].Length)
        {
            SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
            Move(viewportX, viewportY + StatusBarHeight);
            AddRune(map[viewportY][viewportX]);
            SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        }
    }

    /// <summary>
    /// Clears bullet at current position and previous position if different
    /// </summary>
    private void ClearBulletAndPreviousPosition(Bullet bullet, int bulletWorldX, int bulletWorldY,
                                                int frameWidth, int frameHeight, int mapOffsetX, int mapOffsetY, in string[] map)
    {
        // Clear bullet at collision point
        int viewportX = bulletWorldX - mapOffsetX;
        int viewportY = bulletWorldY - mapOffsetY;
        ClearBulletAtPosition(viewportX, viewportY, frameWidth, frameHeight, map);

        // Also clear bullet's previous position if different
        int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
        int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
        prevBulletWorldX = _map.WrapX(prevBulletWorldX);
        prevBulletWorldY = _map.WrapY(prevBulletWorldY);

        if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
        {
            int prevViewportX = prevBulletWorldX - mapOffsetX;
            int prevViewportY = prevBulletWorldY - mapOffsetY;
            ClearBulletAtPosition(prevViewportX, prevViewportY, frameWidth, frameHeight, map);
        }
    }

    /// <summary>
    /// Handles bullet-snipe collision - returns true if bullet was removed
    /// </summary>
    private bool HandleBulletSnipeCollision(Bullet bullet, int bulletWorldX, int bulletWorldY,
                                           int frameWidth, int frameHeight, int mapOffsetX, int mapOffsetY, string bulletId)
    {
        for (int j = _snipes.Count - 1; j >= 0; j--)
        {
            var snipe = _snipes[j];
            if (!snipe.IsAlive)
                continue;

            int snipeWorldX = _map.WrapX(snipe.X);
            int snipeWorldY = _map.WrapY(snipe.Y);

            // Check bullet at snipe position
            bool hitSnipe = (bulletWorldX == snipeWorldX && bulletWorldY == snipeWorldY);
            
            // Check bullet at arrow position
            if (!hitSnipe)
            {
                int arrowWorldX = snipeWorldX + (snipe.DirectionX < 0 ? -1 : 1);
                arrowWorldX = _map.WrapX(arrowWorldX);
                hitSnipe = (bulletWorldX == arrowWorldX && bulletWorldY == snipeWorldY);
            }

            if (hitSnipe)
            {
                // Bullet hit snipe - clear both bullet and snipe
                snipe.IsAlive = false;

                // Get fresh map to ensure we have correct character for clearing
                var freshMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

                // Clear snipe first (both '@' and arrow) - uses world coordinates
                ClearSnipePosition(snipe);

                // Clear bullet at collision point and previous position
                ClearBulletAndPreviousPosition(bullet, bulletWorldX, bulletWorldY, 
                                              frameWidth, frameHeight, mapOffsetX, mapOffsetY, freshMap);

                // Invalidate cached map since we're removing entities
                _cachedMapViewport = null;

                // Remove from lists AFTER clearing - use bullet ID to find and remove
                _snipes.RemoveAt(j);
                
                // Find and remove bullet by ID (more robust than using index)
                int bulletIndexToRemove = -1;
                for (int idx = 0; idx < _bullets.Count; idx++)
                {
                    if (_bullets[idx]?.BulletId == bulletId)
                    {
                        bulletIndexToRemove = idx;
                        break;
                    }
                }
                if (bulletIndexToRemove >= 0)
                {
                    _bullets.RemoveAt(bulletIndexToRemove);
                }
                _gameState.SnipesUndestroyed--;
                _gameState.Score += SnipeKillScore;
                _player.Score += SnipeKillScore;
                
                // Check for level completion (host only in multiplayer)
                if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
                {
                    CheckLevelComplete();
                }
                
                return true; // Bullet was removed
            }
        }
        return false; // Bullet not removed
    }

    /// <summary>
    /// Handles bullet-hive collision - returns true if bullet was removed
    /// </summary>
    private bool HandleBulletHiveCollision(Bullet bullet, int bulletWorldX, int bulletWorldY,
                                          int frameWidth, int frameHeight, int mapOffsetX, int mapOffsetY, string bulletId)
    {
        foreach (var hive in _hives)
        {
            if (hive.IsDestroyed)
                continue;

            // Check if bullet is within hive bounds (2x2 area)
            // Hive occupies: [X, X+1] columns, [Y, Y+1] rows
            int hiveWorldX = _map.WrapX(hive.X);
            int hiveWorldY = _map.WrapY(hive.Y);
            int hiveWorldX2 = _map.WrapX(hiveWorldX + 1);
            int hiveWorldY2 = _map.WrapY(hiveWorldY + 1);

            // Check if bullet is within the 2x2 hive area
            bool inHiveX = (bulletWorldX == hiveWorldX || bulletWorldX == hiveWorldX2);
            bool inHiveY = (bulletWorldY == hiveWorldY || bulletWorldY == hiveWorldY2);

            if (inHiveX && inHiveY)
            {
                // Bullet hit hive
                hive.Hits++;

                // Reduce flash rate by 1/3 (for this hive only)
                hive.FlashIntervalMs = (int)(hive.FlashIntervalMs * 2.0 / 3.0);
                if (hive.FlashIntervalMs < 10) hive.FlashIntervalMs = 10; // Minimum 10ms

                // Get fresh map to ensure we have correct character for clearing
                var freshMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

                // Clear bullet at collision point and previous position
                ClearBulletAndPreviousPosition(bullet, bulletWorldX, bulletWorldY, 
                                              frameWidth, frameHeight, mapOffsetX, mapOffsetY, freshMap);

                // Invalidate cached map since we're removing a bullet
                _cachedMapViewport = null;

                // Publish bullet hit in multiplayer (host only)
                if (_isMultiplayer && _gameSession != null && _grpcClient != null && 
                    _gameSession.Role == GameSessionRole.Host && bullet.PlayerId == _gameSession.PlayerId)
                {
                    PublishBulletUpdate(bullet, "hit", "hive", $"hive_{hive.X}_{hive.Y}");
                }
                
                // Find and remove bullet by ID (more robust than using index)
                int bulletIndexToRemove = -1;
                for (int idx = 0; idx < _bullets.Count; idx++)
                {
                    if (_bullets[idx]?.BulletId == bulletId)
                    {
                        bulletIndexToRemove = idx;
                        break;
                    }
                }
                if (bulletIndexToRemove >= 0)
                {
                    _bullets.RemoveAt(bulletIndexToRemove);
                }

                // Check if hive is destroyed (3 hits)
                if (hive.Hits >= Hive.HitsToDestroy)
                {
                    hive.IsDestroyed = true;
                    _gameState.HivesUndestroyed--;

                    // Clear hive from screen immediately
                    ClearHivePosition(hive);

                    // Kill all unreleased snipes from this hive
                    int unreleasedSnipes = hive.SnipesRemaining;

                    // Add score: base score for hive + score per unreleased snipe
                    int hiveScore = HiveBaseScore + (unreleasedSnipes * SnipePerHiveScore);
                    _gameState.Score += hiveScore;
                    _player.Score += hiveScore;

                    // Update total snipes count (unreleased snipes are now gone)
                    _gameState.SnipesUndestroyed -= unreleasedSnipes;
                    _gameState.TotalSnipes -= unreleasedSnipes;
                    
                    // Check for level completion (host only in multiplayer)
                    if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
                    {
                        CheckLevelComplete();
                    }
                }

                return true; // Bullet was removed
            }
        }
        return false; // Bullet not removed
    }

    /// <summary>
    /// Handles bullet expiration - removes expired bullets and clears them from screen
    /// </summary>
    private bool HandleBulletExpiration(Bullet bullet, int frameWidth, int frameHeight, int mapOffsetX, int mapOffsetY, in string[] map)
    {
        double ageSeconds = (_currentFrameTime - bullet.CreatedAt).TotalSeconds;
        if (ageSeconds >= Bullet.LifetimeSeconds)
        {
            // Clear the expired bullet from screen before removing
            int viewportX = (int)Math.Round(bullet.X) - mapOffsetX;
            int viewportY = (int)Math.Round(bullet.Y) - mapOffsetY;
            ClearBulletAtPosition(viewportX, viewportY, frameWidth, frameHeight, map);

            // Publish bullet expired in multiplayer
            if (_isMultiplayer && _gameSession != null && _grpcClient != null && bullet.PlayerId == _gameSession.PlayerId)
            {
                PublishBulletUpdate(bullet, "expired");
            }
            
            return true; // Bullet expired
        }
        return false; // Bullet still alive
    }

    /// <summary>
    /// Handles bullet wall collision and bouncing
    /// </summary>
    private void HandleBulletWallCollision(Bullet bullet, double prevX, double prevY)
    {
        // Check for wall collision using world map coordinates
        int bulletMapX = (int)Math.Round(bullet.X);
        int bulletMapY = (int)Math.Round(bullet.Y);

        // Wrap coordinates to map bounds
        bulletMapX = _map.WrapX(bulletMapX);
        bulletMapY = _map.WrapY(bulletMapY);

        // Check if bullet hit a wall
        if (_map.IsValidCoordinate(bulletMapX, bulletMapY))
        {
            char cell = _map.FullMap[bulletMapY][bulletMapX];
            if (cell != ' ')
            {
                // Hit a wall - determine wall type and bounce accordingly
                // Horizontal walls: ═, ─, ╦, ╩, ╬ (reverse Y)
                // Vertical walls: ║, │, ╣, ╠ (reverse X)
                // Corners: ╗, ╝, ╚, ╔ (determine based on approach direction)

                bool isHorizontalWall = cell == '═' || cell == '─' || cell == '╦' || cell == '╩' || cell == '╬';
                bool isVerticalWall = cell == '║' || cell == '│' || cell == '╣' || cell == '╠';

                if (isHorizontalWall)
                {
                    // Hit a horizontal wall - reverse Y direction
                    bullet.BounceY();
                }
                else if (isVerticalWall)
                {
                    // Hit a vertical wall - reverse X direction
                    bullet.BounceX();
                }
                else
                {
                    // Corner or other wall character - determine based on approach direction
                    // If moving more horizontally, likely hit vertical surface, reverse X
                    // If moving more vertically, likely hit horizontal surface, reverse Y
                    if (Math.Abs(bullet.VelocityX) > Math.Abs(bullet.VelocityY))
                    {
                        bullet.BounceX();
                    }
                    else if (Math.Abs(bullet.VelocityY) > Math.Abs(bullet.VelocityX))
                    {
                        bullet.BounceY();
                    }
                    else
                    {
                        // Equal diagonal - reverse both
                        bullet.BounceX();
                        bullet.BounceY();
                    }
                }

                // Move bullet back to previous position to avoid getting stuck
                bullet.X = prevX;
                bullet.Y = prevY;
            }
        }
    }

    private void UpdateBullets()
    {
        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
        int mapOffsetX = _player.X - (frameWidth / 2);
        int mapOffsetY = _player.Y - (frameHeight / 2);

        // Use bullet IDs instead of indices to avoid index invalidation issues
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            // Defensive check: ensure index is valid
            if (i < 0 || i >= _bullets.Count)
                break;
                
            var bullet = _bullets[i];
            if (bullet == null)
                continue;
                
            // Capture bullet ID for safe lookup after potential removals
            string bulletId = bullet.BulletId;

            // Check if bullet has expired
            if (HandleBulletExpiration(bullet, frameWidth, frameHeight, mapOffsetX, mapOffsetY, map))
            {
                // Re-find bullet by ID to ensure it still exists before removing
                int bulletIndexToRemove = -1;
                for (int idx = 0; idx < _bullets.Count; idx++)
                {
                    if (_bullets[idx]?.BulletId == bulletId)
                    {
                        bulletIndexToRemove = idx;
                        break;
                    }
                }
                if (bulletIndexToRemove >= 0)
                {
                    _bullets.RemoveAt(bulletIndexToRemove);
                }
                continue;
            }

            // Store previous position
            double prevX = bullet.X;
            double prevY = bullet.Y;

            // Update bullet position (moves every 10ms when this is called)
            bullet.Update();
            
            // Publish bullet update in multiplayer (for local bullets only)
            if (_isMultiplayer && _gameSession != null && _grpcClient != null && bullet.PlayerId == _gameSession.PlayerId)
            {
                PublishBulletUpdate(bullet, "updated");
            }

            // Check for wall collision
            HandleBulletWallCollision(bullet, prevX, prevY);

            // Check for bullet-snipe collision
            int bulletWorldX = (int)Math.Round(bullet.X);
            int bulletWorldY = (int)Math.Round(bullet.Y);
            bulletWorldX = _map.WrapX(bulletWorldX);
            bulletWorldY = _map.WrapY(bulletWorldY);

            bool bulletRemoved = HandleBulletSnipeCollision(bullet, bulletWorldX, bulletWorldY, 
                                                           frameWidth, frameHeight, mapOffsetX, mapOffsetY, bulletId);

            // Check for bullet-hive collision (only if bullet still exists)
            if (!bulletRemoved)
            {
                // Re-find bullet by ID before checking hive collision
                Bullet? bulletToCheck = null;
                for (int idx = 0; idx < _bullets.Count; idx++)
                {
                    if (_bullets[idx]?.BulletId == bulletId)
                    {
                        bulletToCheck = _bullets[idx];
                        break;
                    }
                }
                if (bulletToCheck != null)
                {
                    bulletRemoved = HandleBulletHiveCollision(bulletToCheck, bulletWorldX, bulletWorldY, 
                                                             frameWidth, frameHeight, mapOffsetX, mapOffsetY, bulletId);
                }
            }
            
            // Check for bullet-to-player collision (host only, for all bullets)
            if (!bulletRemoved && _isMultiplayer && _gameSession != null && _gameSession.Role == GameSessionRole.Host)
            {
                // Re-find bullet by ID before checking player collision
                Bullet? bulletToCheck = null;
                for (int idx = 0; idx < _bullets.Count; idx++)
                {
                    if (_bullets[idx]?.BulletId == bulletId)
                    {
                        bulletToCheck = _bullets[idx];
                        break;
                    }
                }
                if (bulletToCheck != null)
                {
                    CheckBulletPlayerCollision(bulletToCheck, frameWidth, frameHeight, mapOffsetX, mapOffsetY);
                }
            }
        }
    }
    
    private void CheckBulletPlayerCollision(Bullet bullet, int frameWidth, int frameHeight, int mapOffsetX, int mapOffsetY)
    {
        if (bullet == null)
            return;
        
        if (!_isMultiplayer)
            return;
        
        if (_gameSession == null)
            return;
        
        if (_grpcClient == null)
            return;
        
        int bulletWorldX = (int)Math.Round(bullet.X);
        int bulletWorldY = (int)Math.Round(bullet.Y);
        bulletWorldX = _map.WrapX(bulletWorldX);
        bulletWorldY = _map.WrapY(bulletWorldY);
        
        // Check against all network players (including local) - snapshot to avoid collection modified during iteration
        List<PlayerNetwork> playersCopy;
        lock (_gameStateLock) { playersCopy = new List<PlayerNetwork>(_networkPlayers.Values); }
        foreach (var networkPlayer in playersCopy)
        {
            // Skip if bullet belongs to this player
            if (bullet.PlayerId == networkPlayer.PlayerId)
                continue;
            
            int playerWorldX = _map.WrapX(networkPlayer.X);
            int playerWorldY = _map.WrapY(networkPlayer.Y);
            
            // Check if bullet is within player's area (PlayerWidth x PlayerHeight)
            if (bulletWorldX >= playerWorldX && bulletWorldX <= playerWorldX + (PlayerWidth - 1) &&
                bulletWorldY >= playerWorldY && bulletWorldY <= playerWorldY + (PlayerHeight - 1))
            {
                // Bullet hit player
                networkPlayer.Lives--;
                networkPlayer.IsAlive = networkPlayer.Lives > 0;
                
                // Get fresh map to ensure we have correct character for clearing
                var freshMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
                
                // Clear bullet at collision point
                int viewportX = bulletWorldX - mapOffsetX;
                int viewportY = bulletWorldY - mapOffsetY;
                if (viewportX >= 0 && viewportX < frameWidth &&
                    viewportY >= 0 && viewportY < frameHeight &&
                    freshMap != null && viewportY >= 0 && viewportY < freshMap.Length &&
                    viewportX >= 0 && viewportX < freshMap[viewportY].Length)
                {
                    SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
                    Move(viewportX, viewportY + StatusBarHeight);
                    AddRune(freshMap[viewportY][viewportX]);
                    SetAttribute(new DrawingAttribute(Color.White, Color.Black));
                }
                
                // Also clear bullet's previous position if different
                    int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
                    int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
                    prevBulletWorldX = _map.WrapX(prevBulletWorldX);
                    prevBulletWorldY = _map.WrapY(prevBulletWorldY);

                    if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
                {
                    int prevViewportX = prevBulletWorldX - mapOffsetX;
                    int prevViewportY = prevBulletWorldY - mapOffsetY;
                    if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                        prevViewportY >= 0 && prevViewportY < frameHeight &&
                        freshMap != null && prevViewportY >= 0 && prevViewportY < freshMap.Length &&
                        prevViewportX >= 0 && prevViewportX < freshMap[prevViewportY].Length)
                    {
                        SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
                        Move(prevViewportX, prevViewportY + StatusBarHeight);
                        AddRune(freshMap[prevViewportY][prevViewportX]);
                        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
                    }
                }
                
                // Invalidate cached map since we're removing a bullet
                _cachedMapViewport = null;
                
                // Publish bullet hit
                // Note: Cannot use 'in' parameter in lambda, so bullet is passed by value
                string bulletId = bullet.BulletId;
                PublishBulletUpdate(bullet, "hit", "player", networkPlayer.PlayerId);
                
                // Remove bullet by finding index first (more efficient than RemoveAll)
                int bulletIndexToRemove = -1;
                for (int idx = 0; idx < _bullets.Count; idx++)
                {
                    if (_bullets[idx].BulletId == bulletId)
                    {
                        bulletIndexToRemove = idx;
                        break;
                    }
                }
                if (bulletIndexToRemove >= 0)
                {
                    _bullets.RemoveAt(bulletIndexToRemove);
                }
                
                if (networkPlayer.IsLocal)
                {
                    // Local player hit - handle respawn
                    _player.Lives = networkPlayer.Lives;
                    _player.IsAlive = networkPlayer.IsAlive;
                    
                    if (_player.Lives > 0)
                    {
                        // Respawn at random position
                        var (x, y) = FindRandomValidPositionForMultiplayer();
                        _player.X = x;
                        _player.Y = y;
                        networkPlayer.X = x;
                        networkPlayer.Y = y;
                        networkPlayer.PreviousX = x; // Reset previous position
                        networkPlayer.PreviousY = y;
                        _cachedMapViewport = null;
                        
                        // Immediately publish new position to other players (bypass throttling)
                        if (_isMultiplayer && _gameSession != null && _grpcClient != null)
                        {
                            _positionSequence++;
                            // Use the standard position publish method
                            PublishPlayerPosition();
                        }
                        
                        _introScreen.StartClearingEffect($"{_player.Lives} Lives Left");
                    }
                    else
                    {
                        // Game over for this player
                        _player.IsAlive = false;
                        
                        // Check if all players are dead (game over for everyone)
                        CheckGameOver();
                    }
                }
                
                break; // Only one player can be hit per bullet
            }
        }
    }

    private void DrawBullets()
    {
        

        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Use cached map viewport if available, otherwise get new one
        var map = _cachedMapViewport;
        if (map == null || map.Length != frameHeight)
        {
            map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
            _cachedMapViewport = map;
        }

        // Get map viewport to convert world coordinates to viewport coordinates
        // Map.GetMap centers on (_player.X, _player.Y), so:
        // viewport center = (frameWidth/2, frameHeight/2) corresponds to (_player.X, _player.Y)
        // Offset by StatusBarHeight when drawing
        int mapOffsetX = _player.X - (frameWidth / 2);
        int mapOffsetY = _player.Y - (frameHeight / 2);

        // Snapshot bullets so we don't iterate while HandleGameStateSnapshot modifies the list (multiplayer)
        List<Bullet> bulletsCopy;
        lock (_gameStateLock) { bulletsCopy = new List<Bullet>(_bullets); }

        // First, clear previous bullet positions by drawing the map character there
        SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
        foreach (var bullet in bulletsCopy)
        {
            // Convert previous world coordinates to viewport coordinates
            int prevViewportX = (int)Math.Round(bullet.PreviousX) - mapOffsetX;
            int prevViewportY = (int)Math.Round(bullet.PreviousY) - mapOffsetY;

            // Only clear if within viewport and different from current position
            if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                prevViewportY >= 0 && prevViewportY < frameHeight)
            {
                int currentViewportX = (int)Math.Round(bullet.X) - mapOffsetX;
                int currentViewportY = (int)Math.Round(bullet.Y) - mapOffsetY;

                // Only clear if position actually changed
                if (prevViewportX != currentViewportX || prevViewportY != currentViewportY)
                {
                    // Get the map character at the previous position
                    if (map != null && prevViewportY >= 0 && prevViewportY < map.Length &&
                        prevViewportX >= 0 && prevViewportX < map[prevViewportY].Length)
                    {
                        char mapChar = map[prevViewportY][prevViewportX];
                        Move(prevViewportX, prevViewportY + StatusBarHeight);
                        AddRune(mapChar);
                    }
                }
            }
        }

        // Use cached frame time instead of calling DateTime.Now
        _cachedDateTime = _currentFrameTime;

        // Flash between bright red and red based on time
        bool isBright = (_cachedDateTime.Millisecond / 250) % 2 == 0;
        var bulletColor = isBright ? Color.BrightRed : Color.Red;
        SetAttribute(new DrawingAttribute(bulletColor, Color.Black));

        // Now draw bullets at their current positions
        foreach (var bullet in bulletsCopy)
        {
            // Convert world coordinates to viewport coordinates
            int viewportX = (int)Math.Round(bullet.X) - mapOffsetX;
            int viewportY = (int)Math.Round(bullet.Y) - mapOffsetY;

            // Only draw if within viewport (offset by StatusBarHeight)
            if (viewportX >= 0 && viewportX < frameWidth &&
                viewportY >= 0 && viewportY < frameHeight)
            {
                Move(viewportX, viewportY + StatusBarHeight);
                AddRune('*');
            }
        }

        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
    }

    private void DrawHives()
    {
        List<Hive> hivesCopy;
        lock (_gameStateLock) { hivesCopy = new List<Hive>(_hives); }

        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Get map viewport to convert world coordinates to viewport coordinates
        int mapOffsetX = _player.X - (frameWidth / 2);
        int mapOffsetY = _player.Y - (frameHeight / 2);

        // Use cached frame time instead of calling DateTime.Now
        _cachedDateTime = _currentFrameTime;

        long totalMs = _cachedDateTime.Ticks / TimeSpan.TicksPerMillisecond;

        // Pre-calculate viewport bounds for early exit optimization
        int minViewportX = -2; // Hive is 2 wide, so allow 2 cells outside for partial visibility
        int maxViewportX = frameWidth + 1;
        int minViewportY = -2;
        int maxViewportY = frameHeight + 1;

        foreach (var hive in hivesCopy)
        {
            if (hive.IsDestroyed)
                continue;

            // Each hive has its own flash interval (reduced by 1/3 each hit)
            // Flash between cyan and green based on time
            bool isCyan = (totalMs / hive.FlashIntervalMs) % 2 == 0;
            var hiveColor = isCyan ? Color.Cyan : Color.Green;
            SetAttribute(new DrawingAttribute(hiveColor, Color.Black));

            // Calculate viewport coordinates for the hive
            // The viewport is centered on the player at (frameWidth/2, frameHeight/2)
            // Map.GetMap centers on (_player.X, _player.Y), so we need to calculate
            // the relative position of the hive from the player, accounting for wrapping

            // Calculate the difference in world coordinates
            int deltaX = hive.X - _player.X;
            int deltaY = hive.Y - _player.Y;

            // Handle wrapping: find the shortest path (accounting for wrap)
            _map.WrapDeltaX(ref deltaX);
            _map.WrapDeltaY(ref deltaY);

            // Convert to viewport coordinates (viewport center is at frameWidth/2, frameHeight/2)
            int hiveViewportX = (frameWidth / 2) + deltaX;
            int hiveViewportY = (frameHeight / 2) + deltaY;

            // Check if any part of the 2x2 hive is visible in viewport
            if (hiveViewportX + 1 < minViewportX || hiveViewportX > maxViewportX ||
                hiveViewportY + 1 < minViewportY || hiveViewportY > maxViewportY)
            {
                continue; // Hive is completely outside viewport, skip drawing
            }

            // Hive is a 2x2 box with corner characters: ╔ ╗ ╚ ╝
            // Only draw corners that are within the viewport bounds (offset by StatusBarHeight)
            // Top-left corner
            if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
                hiveViewportY >= 0 && hiveViewportY < frameHeight)
            {
                Move(hiveViewportX, hiveViewportY + StatusBarHeight);
                AddRune('╔');
            }

            // Top-right corner
            int topRightX = hiveViewportX + 1;
            if (topRightX >= 0 && topRightX < frameWidth &&
                hiveViewportY >= 0 && hiveViewportY < frameHeight)
            {
                Move(topRightX, hiveViewportY + StatusBarHeight);
                AddRune('╗');
            }

            // Bottom-left corner
            int bottomLeftY = hiveViewportY + 1;
            if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
                bottomLeftY >= 0 && bottomLeftY < frameHeight)
            {
                Move(hiveViewportX, bottomLeftY + StatusBarHeight);
                AddRune('╚');
            }

            // Bottom-right corner
            if (topRightX >= 0 && topRightX < frameWidth &&
                bottomLeftY >= 0 && bottomLeftY < frameHeight)
            {
                Move(topRightX, bottomLeftY + StatusBarHeight);
                AddRune('╝');
            }
        }

        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
    }

    private void SpawnSnipes()
    {
        foreach (var hive in _hives)
        {
            if (!hive.CanSpawnSnipe())
                continue;

            // Random chance to spawn (roughly every 3 seconds, but randomized)
            int timeSinceLastSpawn = (int)(_currentFrameTime - hive.LastSpawnTime).TotalMilliseconds;
            const int HiveSpawnRandomizationMs = 1000; // Randomize spawn time by ±1 second
            if (timeSinceLastSpawn >= Hive.SpawnIntervalMs + Random.Shared.Next(-HiveSpawnRandomizationMs, HiveSpawnRandomizationMs))
            {
                // Spawn snipe at hive position (center of 2x2 hive)
                int snipeX = hive.X + 1; // Center of hive
                int snipeY = hive.Y + 1;
                SnipeType snipeType = hive.GetNextSnipeType();

                var snipe = new Snipe(snipeX, snipeY, snipeType);

                // Give snipe a random initial direction
                int[] directions = [-1, 0, 1];
                snipe.DirectionX = directions[Random.Shared.Next(3)];
                snipe.DirectionY = directions[Random.Shared.Next(3)];

                // Ensure snipe has some direction (not both 0)
                if (snipe.DirectionX == 0 && snipe.DirectionY == 0)
                {
                    if (Random.Shared.Next(2) == 0)
                        snipe.DirectionX = Random.Shared.Next(2) == 0 ? -1 : 1;
                    else
                        snipe.DirectionY = Random.Shared.Next(2) == 0 ? -1 : 1;
                }

                _snipes.Add(snipe);
                hive.SpawnSnipe(_currentFrameTime);
                // Note: SnipesUndestroyed doesn't change when spawning - the snipe just moves from "in hive" to "spawned"
                // It only decreases when a snipe is killed
                
                // Publish snipe spawn in multiplayer (host only)
                if (_isMultiplayer && _gameSession != null && _gameSession.Role == GameSessionRole.Host && _grpcClient != null)
                {
                    PublishSnipeSpawn(snipe);
                }
            }
        }
    }
    
    private void PublishSnipeSpawn(Snipe snipe)
    {
        if (snipe == null)
            return;
        
        if (!_isMultiplayer)
            return;
        
        if (_gameSession == null)
            return;
        
        if (_gameSession.Role != GameSessionRole.Host)
            return;
        
        if (_grpcClient == null)
            return;
        
        var gameMessage = CreateBaseGameMessage();
        if (gameMessage == null)
            return;
        
        gameMessage.Snipes = new SnipeUpdates();
        gameMessage.Snipes.Updates.Add(new SnipeUpdateInfo
        {
            SnipeId = snipe.SnipeId,
            Action = "spawned",
            X = snipe.X,
            Y = snipe.Y,
            DirectionX = snipe.DirectionX,
            DirectionY = snipe.DirectionY,
            Type = snipe.Type == SnipeType.TypeA ? "A" : "B"
        });
        SendGameMessage(gameMessage);
    }
    
    /// <summary>
    /// Parses a string snipe type from gRPC message to SnipeType enum
    /// </summary>
    private static SnipeType ParseSnipeType(string? typeString)
    {
        if (string.IsNullOrEmpty(typeString))
            return SnipeType.TypeA; // Default
        
        return typeString.Trim().ToUpperInvariant() switch
        {
            "A" or "TYPEA" => SnipeType.TypeA,
            "B" or "TYPEB" => SnipeType.TypeB,
            _ => SnipeType.TypeA // Default fallback
        };
    }
    
    private void PublishSnipeUpdates()
    {
        if (!_isMultiplayer)
            return;
        
        if (_gameSession == null)
            return;
        
        if (_gameSession.Role != GameSessionRole.Host)
            return;
        
        // Publish all current snipe positions (periodic update)
        // IMPORTANT: All coordinates must be WORLD/MAP coordinates, not viewport
        // Count alive snipes first to pre-allocate list capacity
        int aliveCount = 0;
        foreach (var s in _snipes)
        {
            if (s != null && s.IsAlive) aliveCount++;
        }
        
        if (aliveCount > 0)
        {
            var gameMessage = CreateBaseGameMessage();
            if (gameMessage == null)
                return;
            
            gameMessage.Snipes = new SnipeUpdates();
            // Pre-allocate capacity for snipe updates
            gameMessage.Snipes.Updates.Capacity = aliveCount;
            
            foreach (var s in _snipes)
            {
                if (!s.IsAlive) continue;
                
                gameMessage.Snipes.Updates.Add(new SnipeUpdateInfo
                {
                    SnipeId = s.SnipeId,
                    Action = "moved",
                    X = s.X,  // World coordinate (map space)
                    Y = s.Y,  // World coordinate (map space)
                    DirectionX = s.DirectionX,
                    DirectionY = s.DirectionY,
                    Type = s.Type.ToString()
                });
            }
            SendGameMessage(gameMessage);
        }
    }

    private bool IsSnipePositionValid(int x, int y, int dirX, int dirY)
    {
        // Check if snipe position (x, y) is valid (not a wall)
        int wrappedX = _map.WrapX(x);
        int wrappedY = _map.WrapY(y);

        if (!_map.IsValidCoordinate(wrappedX, wrappedY))
            return false;

        if (_map.FullMap[wrappedY][wrappedX] != ' ')
            return false;

        // Check if arrow position is also valid
        // Arrow position depends on direction:
        // Moving left (dirX < 0): arrow is at (x - 1, y)
        // Moving right or other: arrow is at (x + 1, y)
        int arrowX = dirX < 0 ? x - 1 : x + 1;
        int arrowY = y;

        int wrappedArrowX = _map.WrapX(arrowX);
        int wrappedArrowY = _map.WrapY(arrowY);

        if (!_map.IsValidCoordinate(wrappedArrowX, wrappedArrowY))
            return false;

        if (_map.FullMap[wrappedArrowY][wrappedArrowX] != ' ')
            return false;

        return true;
    }

    private bool CheckSnipeSnipeCollision(Snipe snipe1, Snipe snipe2)
    {
        // Wrap coordinates for comparison
        int x1 = _map.WrapX(snipe1.X);
        int y1 = _map.WrapY(snipe1.Y);
        int x2 = _map.WrapX(snipe2.X);
        int y2 = _map.WrapY(snipe2.Y);

        // Arrow position depends on direction:
        // Moving left (DirectionX < 0): arrow is at (x - 1, y)
        // Moving right or other: arrow is at (x + 1, y)
        int arrow1X = snipe1.DirectionX < 0 ? _map.WrapX(x1 - 1) : _map.WrapX(x1 + 1);
        int arrow2X = snipe2.DirectionX < 0 ? _map.WrapX(x2 - 1) : _map.WrapX(x2 + 1);

        // Check if snipe1's position overlaps with snipe2's position or arrow
        if ((x1 == x2 && y1 == y2) || (x1 == arrow2X && y1 == y2))
            return true;

        // Check if snipe1's arrow overlaps with snipe2's position or arrow
        if ((arrow1X == x2 && y1 == y2) || (arrow1X == arrow2X && y1 == y2))
            return true;

        return false;
    }

    /// <summary>
    /// Calculates the preferred direction for a snipe to move toward the player
    /// </summary>
    private (int preferredDirX, int preferredDirY) CalculatePreferredDirection(Snipe snipe)
    {
        int deltaX = _player.X - snipe.X;
        int deltaY = _player.Y - snipe.Y;

        // Handle map wrapping - find shortest path
        _map.WrapDeltaX(ref deltaX);
        _map.WrapDeltaY(ref deltaY);

        int preferredDirX = 0;
        int preferredDirY = 0;

        if (Math.Abs(deltaX) > Math.Abs(deltaY))
        {
            // Move horizontally first
            preferredDirX = deltaX > 0 ? 1 : (deltaX < 0 ? -1 : 0);
            if (preferredDirX == 0 && deltaY != 0)
                preferredDirY = deltaY > 0 ? 1 : -1;
        }
        else
        {
            // Move vertically first
            preferredDirY = deltaY > 0 ? 1 : (deltaY < 0 ? -1 : 0);
            if (preferredDirY == 0 && deltaX != 0)
                preferredDirX = deltaX > 0 ? 1 : -1;
        }

        return (preferredDirX, preferredDirY);
    }

    /// <summary>
    /// Gets all valid directions a snipe can move
    /// </summary>
    private List<(int dx, int dy)> GetValidDirections(Snipe snipe)
    {
        return GetValidDirectionsFromSnapshot(snipe.X, snipe.Y);
    }

    /// <summary>
    /// Thread-safe version that uses position snapshot instead of snipe object
    /// </summary>
    private List<(int dx, int dy)> GetValidDirectionsFromSnapshot(int snipeX, int snipeY)
    {
        List<(int dx, int dy)> possibleDirections = new List<(int, int)>(8); // Max 8 directions

        // Try all 8 possible directions (including diagonals)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue; // Skip no movement

                int testX = snipeX + dx;
                int testY = snipeY + dy;

                if (IsSnipePositionValid(testX, testY, dx, dy))
                {
                    possibleDirections.Add((dx, dy));
                }
            }
        }

        return possibleDirections;
    }

    /// <summary>
    /// Handles collisions between a snipe and other snipes
    /// </summary>
    private void HandleSnipeSnipeCollisions(Snipe snipe, int snipeIndex)
    {
        for (int j = 0; j < _snipes.Count; j++)
        {
            if (snipeIndex == j || !_snipes[j].IsAlive)
                continue;

            var otherSnipe = _snipes[j];
            if (CheckSnipeSnipeCollision(snipe, otherSnipe))
            {
                // Snipes collided - bounce (reverse direction)
                snipe.DirectionX = -snipe.DirectionX;
                snipe.DirectionY = -snipe.DirectionY;
                otherSnipe.DirectionX = -otherSnipe.DirectionX;
                otherSnipe.DirectionY = -otherSnipe.DirectionY;

                // Move snipes back to previous positions to avoid overlap
                snipe.X = snipe.PreviousX;
                snipe.Y = snipe.PreviousY;
                otherSnipe.X = otherSnipe.PreviousX;
                otherSnipe.Y = otherSnipe.PreviousY;

                // Wrap coordinates
                snipe.X = _map.WrapX(snipe.X);
                snipe.Y = _map.WrapY(snipe.Y);
                otherSnipe.X = _map.WrapX(otherSnipe.X);
                otherSnipe.Y = _map.WrapY(otherSnipe.Y);

                break; // Only handle one collision per update
            }
        }
    }

    /// <summary>
    /// Calculates the direction a snipe should move based on heat radius and valid directions
    /// </summary>
    private (int dx, int dy) CalculateSnipeDirection(Snipe snipe, List<(int dx, int dy)> possibleDirections, 
                                                       (int preferredDirX, int preferredDirY) preferredDir, double heatFactor)
    {
        return CalculateSnipeDirectionFromSnapshot(snipe.DirectionX, snipe.DirectionY, possibleDirections, preferredDir, heatFactor);
    }

    /// <summary>
    /// Thread-safe version that uses direction snapshot instead of snipe object
    /// </summary>
    private (int dx, int dy) CalculateSnipeDirectionFromSnapshot(int currentDirX, int currentDirY, List<(int dx, int dy)> possibleDirections, 
                                                       (int preferredDirX, int preferredDirY) preferredDir, double heatFactor)
    {
        bool currentDirectionValid = possibleDirections.Contains((currentDirX, currentDirY));

        if (currentDirectionValid && heatFactor < 0.3)
        {
            // Current direction is valid and player is far - wander through maze
            // Occasionally change direction to explore (20% chance)
            if (Random.Shared.Next(100) < 20)
            {
                // Choose a random valid direction to explore
                return possibleDirections[Random.Shared.Next(possibleDirections.Count)];
            }
            else
            {
                // Continue in current direction
                return (currentDirX, currentDirY);
            }
        }
        else if (heatFactor > 0.3 && (preferredDir.preferredDirX != 0 || preferredDir.preferredDirY != 0))
        {
            // Player is close (heat radius) - prefer moving toward player
            bool preferredValid = possibleDirections.Contains((preferredDir.preferredDirX, preferredDir.preferredDirY));

            if (preferredValid)
            {
                // Prefer moving toward player, but allow continuing current direction if it's also toward player
                if (currentDirectionValid && currentDirX == preferredDir.preferredDirX && currentDirY == preferredDir.preferredDirY)
                {
                    // Current direction is toward player - continue
                    return (currentDirX, currentDirY);
                }
                else
                {
                    // Change direction to move toward player
                    return (preferredDir.preferredDirX, preferredDir.preferredDirY);
                }
            }
            else if (currentDirectionValid)
            {
                // Preferred direction not valid, but current direction is - continue
                return (currentDirX, currentDirY);
            }
            else
            {
                // Hit a wall and player is close - randomly choose from valid directions
                return possibleDirections[Random.Shared.Next(possibleDirections.Count)];
            }
        }
        else
        {
            // Current direction hit a wall (not valid) and player is far - randomly choose new direction
            return possibleDirections[Random.Shared.Next(possibleDirections.Count)];
        }
    }

    private void UpdateSnipes()
    {
        // Phase 1: Remove dead snipes (sequential - modifies collection)
        for (int i = _snipes.Count - 1; i >= 0; i--)
        {
            var snipe = _snipes[i];

            if (!snipe.IsAlive)
            {
                // Clear snipe from screen before removing
                ClearSnipePosition(snipe);
                _snipes.RemoveAt(i);
                _gameState.SnipesUndestroyed--;
                
                // Check for level completion (host only in multiplayer)
                if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
                {
                    CheckLevelComplete();
                }
            }
        }

        // Phase 2: Collect snipes that need to move (read-only filtering)
        // Store indices only - we'll access snipes by index when needed
        var snipesToUpdate = new List<int>(_snipes.Count);
        for (int i = 0; i < _snipes.Count; i++)
        {
            var snipe = _snipes[i];
            if (!snipe.IsAlive)
                continue;

            // Check if it's time to move
            int timeSinceLastMove = (int)(_currentFrameTime - snipe.LastMoveTime).TotalMilliseconds;
            if (timeSinceLastMove >= Snipe.MoveIntervalMs)
            {
                snipesToUpdate.Add(i);
            }
        }

        if (snipesToUpdate.Count == 0)
            return;

        // Phase 3: Parallelize AI calculations (read-only operations)
        // Cache player position and current frame time for thread safety
        int playerX = _player.X;
        int playerY = _player.Y;
        DateTime frameTime = _currentFrameTime;
        const int maxHeatRadius = 20;

        // Capture snipe state snapshots before parallel execution to ensure thread safety
        // Store snipe IDs instead of indices to avoid index invalidation issues
        var snipeSnapshots = new (int x, int y, int dirX, int dirY)[snipesToUpdate.Count];
        var snipeIds = new string[snipesToUpdate.Count];
        for (int i = 0; i < snipesToUpdate.Count; i++)
        {
            int index = snipesToUpdate[i];
            
            // Defensive check: verify index is still valid
            if (index < 0 || index >= _snipes.Count)
            {
                ErrorLogger.LogWarning($"Invalid snipe index {index} captured in Phase 2 (snipes count: {_snipes.Count})");
                snipeIds[i] = ""; // Mark as invalid
                continue;
            }
            
            var snipe = _snipes[index];
            if (snipe == null)
            {
                ErrorLogger.LogWarning($"Null snipe at index {index}");
                snipeIds[i] = ""; // Mark as invalid
                continue;
            }
            
            snipeSnapshots[i] = (snipe.X, snipe.Y, snipe.DirectionX, snipe.DirectionY);
            snipeIds[i] = snipe.SnipeId;
        }

        var movementDecisions = new (string snipeId, int newX, int newY, int dirX, int dirY, bool canMove)[snipesToUpdate.Count];
        
        // Cache map dimensions for thread safety (read-only properties)
        int mapWidth = _map.MapWidth;
        int mapHeight = _map.MapHeight;
        
        // Only parallelize if there are enough snipes to benefit (threshold: 5+ snipes)
        // For small numbers, overhead of parallelization may not be worth it
        if (snipesToUpdate.Count < 5)
        {
            // Sequential processing for small numbers
            for (int i = 0; i < snipesToUpdate.Count; i++)
            {
                string snipeId = snipeIds[i];
                
                // Skip invalid entries
                if (string.IsNullOrEmpty(snipeId))
                    continue;
                    
                var snapshot = snipeSnapshots[i];

                // Calculate distance to player for heat radius system (using snapshot)
                int deltaX = playerX - snapshot.x;
                int deltaY = playerY - snapshot.y;

                // Handle map wrapping - find shortest path (read-only map operations)
                int wrappedDeltaX = deltaX;
                int wrappedDeltaY = deltaY;
                if (wrappedDeltaX > mapWidth / 2)
                    wrappedDeltaX -= mapWidth;
                else if (wrappedDeltaX < -mapWidth / 2)
                    wrappedDeltaX += mapWidth;

                if (wrappedDeltaY > mapHeight / 2)
                    wrappedDeltaY -= mapHeight;
                else if (wrappedDeltaY < -mapHeight / 2)
                    wrappedDeltaY += mapHeight;

                // Calculate distance (Manhattan distance for simplicity)
                int distanceToPlayer = Math.Abs(wrappedDeltaX) + Math.Abs(wrappedDeltaY);

                // Heat radius: closer = more attracted, further = less attracted
                double heatFactor = Math.Max(0, 1.0 - (distanceToPlayer / (double)maxHeatRadius));

                // Determine preferred direction (toward player) - using snapshot, not snipe object
                var preferredDir = CalculatePreferredDirectionParallel(snapshot.x, snapshot.y, playerX, playerY, wrappedDeltaX, wrappedDeltaY);

                // Get all possible valid directions (using snapshot for position)
                var possibleDirections = GetValidDirectionsFromSnapshot(snapshot.x, snapshot.y);

                if (possibleDirections.Count == 0)
                {
                    // Can't move in any direction - stay in place but keep trying
                    movementDecisions[i] = (snipeId, snapshot.x, snapshot.y, snapshot.dirX, snapshot.dirY, false);
                    continue;
                }

                // Determine direction choice based on rules (using snapshot for current direction)
                var chosenDirection = CalculateSnipeDirectionFromSnapshot(snapshot.dirX, snapshot.dirY, possibleDirections, preferredDir, heatFactor);

                // Calculate new position (don't apply yet - that happens sequentially)
                int newSnipeX = snapshot.x + chosenDirection.dx;
                int newSnipeY = snapshot.y + chosenDirection.dy;
                movementDecisions[i] = (snipeId, newSnipeX, newSnipeY, chosenDirection.dx, chosenDirection.dy, true);
            }
        }
        else
        {
            // Parallel processing for larger numbers
            try
            {
                Parallel.For(0, snipesToUpdate.Count, i =>
                {
                    string snipeId = snipeIds[i];
                    
                    // Skip invalid entries
                    if (string.IsNullOrEmpty(snipeId))
                        return;
                        
                    var snapshot = snipeSnapshots[i];

            // Calculate distance to player for heat radius system (using snapshot)
            int deltaX = playerX - snapshot.x;
            int deltaY = playerY - snapshot.y;

            // Handle map wrapping - find shortest path (read-only map operations)
            int wrappedDeltaX = deltaX;
            int wrappedDeltaY = deltaY;
            if (wrappedDeltaX > mapWidth / 2)
                wrappedDeltaX -= mapWidth;
            else if (wrappedDeltaX < -mapWidth / 2)
                wrappedDeltaX += mapWidth;

            if (wrappedDeltaY > mapHeight / 2)
                wrappedDeltaY -= mapHeight;
            else if (wrappedDeltaY < -mapHeight / 2)
                wrappedDeltaY += mapHeight;

            // Calculate distance (Manhattan distance for simplicity)
            int distanceToPlayer = Math.Abs(wrappedDeltaX) + Math.Abs(wrappedDeltaY);

            // Heat radius: closer = more attracted, further = less attracted
            double heatFactor = Math.Max(0, 1.0 - (distanceToPlayer / (double)maxHeatRadius));

            // Determine preferred direction (toward player) - using snapshot, not snipe object
            var preferredDir = CalculatePreferredDirectionParallel(snapshot.x, snapshot.y, playerX, playerY, wrappedDeltaX, wrappedDeltaY);

            // Get all possible valid directions (using snapshot for position)
            var possibleDirections = GetValidDirectionsFromSnapshot(snapshot.x, snapshot.y);

            if (possibleDirections.Count == 0)
            {
                // Can't move in any direction - stay in place but keep trying
                movementDecisions[i] = (snipeId, snapshot.x, snapshot.y, snapshot.dirX, snapshot.dirY, false);
                return;
            }

            // Determine direction choice based on rules (using snapshot for current direction)
            var chosenDirection = CalculateSnipeDirectionFromSnapshot(snapshot.dirX, snapshot.dirY, possibleDirections, preferredDir, heatFactor);

                    // Calculate new position (don't apply yet - that happens sequentially)
                    int newSnipeX = snapshot.x + chosenDirection.dx;
                    int newSnipeY = snapshot.y + chosenDirection.dy;
                    movementDecisions[i] = (snipeId, newSnipeX, newSnipeY, chosenDirection.dx, chosenDirection.dy, true);
                });
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError("Parallel snipe AI calculation failed, falling back to sequential", ex);
                // If parallel execution fails, fall back to sequential processing
                // This prevents crashes but may indicate a thread safety issue
                for (int i = 0; i < snipesToUpdate.Count; i++)
                {
                    string snipeId = snipeIds[i];
                    
                    // Skip invalid entries
                    if (string.IsNullOrEmpty(snipeId))
                        continue;
                        
                    var snapshot = snipeSnapshots[i];

                    int deltaX = playerX - snapshot.x;
                    int deltaY = playerY - snapshot.y;

                    int wrappedDeltaX = deltaX;
                    int wrappedDeltaY = deltaY;
                    if (wrappedDeltaX > mapWidth / 2)
                        wrappedDeltaX -= mapWidth;
                    else if (wrappedDeltaX < -mapWidth / 2)
                        wrappedDeltaX += mapWidth;

                    if (wrappedDeltaY > mapHeight / 2)
                        wrappedDeltaY -= mapHeight;
                    else if (wrappedDeltaY < -mapHeight / 2)
                        wrappedDeltaY += mapHeight;

                    int distanceToPlayer = Math.Abs(wrappedDeltaX) + Math.Abs(wrappedDeltaY);
                    double heatFactor = Math.Max(0, 1.0 - (distanceToPlayer / (double)maxHeatRadius));

                    var preferredDir = CalculatePreferredDirectionParallel(snapshot.x, snapshot.y, playerX, playerY, wrappedDeltaX, wrappedDeltaY);
                    var possibleDirections = GetValidDirectionsFromSnapshot(snapshot.x, snapshot.y);

                    if (possibleDirections.Count == 0)
                    {
                        movementDecisions[i] = (snipeId, snapshot.x, snapshot.y, snapshot.dirX, snapshot.dirY, false);
                        continue;
                    }

                    var chosenDirection = CalculateSnipeDirectionFromSnapshot(snapshot.dirX, snapshot.dirY, possibleDirections, preferredDir, heatFactor);
                    int newSnipeX = snapshot.x + chosenDirection.dx;
                    int newSnipeY = snapshot.y + chosenDirection.dy;
                    movementDecisions[i] = (snipeId, newSnipeX, newSnipeY, chosenDirection.dx, chosenDirection.dy, true);
                }
            }
        }

        // Phase 4: Sequentially apply movements and handle collisions (modifies shared state)
        // Use snipe IDs instead of indices to avoid index invalidation issues
        for (int i = 0; i < movementDecisions.Length; i++)
        {
            var decision = movementDecisions[i];
            
            // Skip invalid entries
            if (string.IsNullOrEmpty(decision.snipeId))
                continue;
            
            // Find snipe by ID instead of using index (more robust)
            Snipe? snipe = null;
            int snipeIndex = -1;
            for (int j = 0; j < _snipes.Count; j++)
            {
                if (_snipes[j].SnipeId == decision.snipeId)
                {
                    snipe = _snipes[j];
                    snipeIndex = j;
                    break;
                }
            }
            
            // Defensive check: verify snipe was found
            if (snipe == null)
            {
                // Snipe was removed between capture and application, skip
                continue;
            }
            
            // Verify snipe is still alive
            if (!snipe.IsAlive)
            {
                // Snipe died between capture and application, skip
                continue;
            }
            
            if (!decision.canMove)
            {
                // Update LastMoveTime even if can't move
                try
                {
                    snipe.LastMoveTime = frameTime;
                }
                catch (Exception ex)
                {
                    ErrorLogger.LogError($"Failed to update LastMoveTime for snipe {decision.snipeId}", ex);
                    continue;
                }
                continue;
            }

            // Apply movement
            snipe.X = _map.WrapX(decision.newX);
            snipe.Y = _map.WrapY(decision.newY);
            snipe.DirectionX = decision.dirX;
            snipe.DirectionY = decision.dirY;
            snipe.LastMoveTime = frameTime;

            // Verify snipe still exists before collision checks (re-find by ID to be safe)
            snipe = null;
            snipeIndex = -1;
            for (int j = 0; j < _snipes.Count; j++)
            {
                if (_snipes[j].SnipeId == decision.snipeId)
                {
                    snipe = _snipes[j];
                    snipeIndex = j;
                    break;
                }
            }
            
            if (snipe == null || !snipe.IsAlive)
            {
                // Snipe was removed or died, skip collision checks
                continue;
            }
            
            // Check for collision with other snipes
            HandleSnipeSnipeCollisions(snipe, snipeIndex);

            // Verify snipe still exists after snipe-snipe collision check
            snipe = null;
            snipeIndex = -1;
            for (int j = 0; j < _snipes.Count; j++)
            {
                if (_snipes[j].SnipeId == decision.snipeId)
                {
                    snipe = _snipes[j];
                    snipeIndex = j;
                    break;
                }
            }
            
            if (snipe == null || !snipe.IsAlive)
            {
                continue;
            }

            // Check collision with bullets (snipe moving into bullet)
            HandleSnipeBulletCollision(snipe, snipeIndex);

            // Verify snipe still exists after bullet collision check
            snipe = null;
            for (int j = 0; j < _snipes.Count; j++)
            {
                if (_snipes[j].SnipeId == decision.snipeId)
                {
                    snipe = _snipes[j];
                    break;
                }
            }
            
            if (snipe == null || !snipe.IsAlive)
            {
                continue;
            }

            // Check collision with player (only if snipe is still alive and game is not over)
            if (snipe.IsAlive && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                HandleSnipePlayerCollision(snipe);
            }
        }
    }

    /// <summary>
    /// Thread-safe version of CalculatePreferredDirection that uses pre-calculated deltas and position snapshots
    /// </summary>
    private (int preferredDirX, int preferredDirY) CalculatePreferredDirectionParallel(int snipeX, int snipeY, int playerX, int playerY, int deltaX, int deltaY)
    {
        int preferredDirX = 0;
        int preferredDirY = 0;

        if (Math.Abs(deltaX) > Math.Abs(deltaY))
        {
            // Move horizontally first
            preferredDirX = deltaX > 0 ? 1 : (deltaX < 0 ? -1 : 0);
            if (preferredDirX == 0 && deltaY != 0)
                preferredDirY = deltaY > 0 ? 1 : -1;
        }
        else
        {
            // Move vertically first
            preferredDirY = deltaY > 0 ? 1 : (deltaY < 0 ? -1 : 0);
            if (preferredDirY == 0 && deltaX != 0)
                preferredDirX = deltaX > 0 ? 1 : -1;
        }

        return (preferredDirX, preferredDirY);
    }

    /// <summary>
    /// Handles collision between a snipe and bullets
    /// </summary>
    private void HandleSnipeBulletCollision(Snipe snipe, int snipeIndex)
    {
        int snipeWorldX = _map.WrapX(snipe.X);
        int snipeWorldY = _map.WrapY(snipe.Y);

        for (int k = _bullets.Count - 1; k >= 0; k--)
        {
            var bullet = _bullets[k];
            int bulletWorldX = (int)Math.Round(bullet.X);
            int bulletWorldY = (int)Math.Round(bullet.Y);
            bulletWorldX = _map.WrapX(bulletWorldX);
            bulletWorldY = _map.WrapY(bulletWorldY);

            // Check if snipe is at bullet position
            if (snipeWorldX == bulletWorldX && snipeWorldY == bulletWorldY)
            {
                // Snipe moved into bullet - clear both bullet and snipe
                snipe.IsAlive = false;

                // Get fresh map to ensure we have correct character for clearing
                int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : Frame.Width;
                int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (Frame.Height - StatusBarHeight);
                var bulletMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
                int mapOffsetX = _player.X - (frameWidth / 2);
                int mapOffsetY = _player.Y - (frameHeight / 2);

                // Clear snipe first (both '@' and arrow) - uses world coordinates
                ClearSnipePosition(snipe);

                // Clear bullet at collision point and previous position
                ClearBulletAndPreviousPosition(bullet, bulletWorldX, bulletWorldY, 
                                              frameWidth, frameHeight, mapOffsetX, mapOffsetY, bulletMap);

                // Invalidate cached map since we're removing entities
                _cachedMapViewport = null;

                // Remove from lists AFTER clearing
                _snipes.RemoveAt(snipeIndex);
                _bullets.RemoveAt(k);
                _gameState.SnipesUndestroyed--;
                _gameState.Score += SnipeKillScore;
                _player.Score += SnipeKillScore;
                
                // Check for level completion (host only in multiplayer)
                if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
                {
                    CheckLevelComplete();
                }
                
                return; // Snipe removed, exit
            }
        }
    }

    /// <summary>
    /// Handles collision between a snipe and the player
    /// </summary>
    private void HandleSnipePlayerCollision(Snipe snipe)
    {
        if (CheckSnipePlayerCollision(snipe))
        {
            // Snipe explodes, player loses a life
            snipe.IsAlive = false;
            _player.Lives--;
            
            if (_player.Lives > 0)
            {
                // Respawn player at random position with clearing effect
                var (x, y) = FindRandomValidPosition();
                _player.X = x;
                _player.Y = y;
                // Invalidate cached map viewport since player moved
                _cachedMapViewport = null;
                // Trigger clearing effect with lives message
                _introScreen.StartClearingEffect($"{_player.Lives} Lives Left");
            }
            else
            {
                // Game over for this player
                _player.IsAlive = false;
                
                // Check if all players are dead (game over for everyone)
                CheckGameOver();
            }
        }
    }

    private bool CheckSnipePlayerCollision(Snipe snipe)
    {
        // Check if snipe position overlaps with any part of the player
        // Player occupies: [X, X+PlayerWidth-1] columns, [Y, Y+PlayerHeight-1] rows
        return snipe.X >= _player.X && snipe.X <= _player.X + (PlayerWidth - 1) &&
               snipe.Y >= _player.Y && snipe.Y <= _player.Y + (PlayerHeight - 1);
    }

    /// <summary>
    /// Builds a set of positions that snipes previously occupied (for clearing)
    /// </summary>
    private HashSet<(int x, int y)> BuildPreviousSnipePositions()
    {
        List<Snipe> snipesCopy;
        lock (_gameStateLock) { snipesCopy = new List<Snipe>(_snipes); }
        HashSet<(int x, int y)> positionsToClear = new HashSet<(int, int)>(snipesCopy.Count * 2); // Each snipe has 2 positions (@ + arrow)

        foreach (var snipe in snipesCopy)
        {
            if (!snipe.IsAlive)
                continue;

            // Get previous world coordinates (wrapped)
            int prevWorldX = _map.WrapX(snipe.PreviousX);
            int prevWorldY = _map.WrapY(snipe.PreviousY);

            // Determine where '@' and arrow were based on previous direction
            // Drawing logic: DirectionX < 0: arrow at center, '@' to right
            //                 DirectionX >= 0: '@' at center, arrow to right
            int prevCharWorldX, prevArrowWorldX;
            if (snipe.PreviousDirectionX < 0)
            {
                // Moving left: arrow was at snipe position, '@' was one cell to the right
                prevArrowWorldX = prevWorldX;
                prevCharWorldX = _map.WrapX(prevWorldX + 1);
            }
            else
            {
                // Moving right, up, down, or diagonal (DirectionX >= 0): '@' at snipe position, arrow to the right
                prevCharWorldX = prevWorldX;
                prevArrowWorldX = _map.WrapX(prevWorldX + 1);
            }

            // Always add both positions to clear list
            positionsToClear.Add((prevCharWorldX, prevWorldY));
            positionsToClear.Add((prevArrowWorldX, prevWorldY));
        }

        return positionsToClear;
    }

    /// <summary>
    /// Removes current snipe positions from the positions to clear set
    /// </summary>
    private void RemoveCurrentSnipePositions(HashSet<(int x, int y)> positionsToClear)
    {
        List<Snipe> snipesCopy;
        lock (_gameStateLock) { snipesCopy = new List<Snipe>(_snipes); }
        foreach (var snipe in snipesCopy)
        {
            if (!snipe.IsAlive)
                continue;

            int snipeWorldX = _map.WrapX(snipe.X);
            int snipeWorldY = _map.WrapY(snipe.Y);

            // Calculate current '@' and arrow positions based on current direction
            // Drawing logic: DirectionX < 0: arrow at center, '@' to right
            //                 DirectionX >= 0: '@' at center, arrow to right
            int charWorldX, arrowWorldX;
            if (snipe.DirectionX < 0)
            {
                // Moving left: arrow at snipe position, '@' one cell to the right
                arrowWorldX = snipeWorldX;
                charWorldX = _map.WrapX(snipeWorldX + 1);
            }
            else
            {
                // Moving right, up, down, or diagonal (DirectionX >= 0): '@' at snipe position, arrow to the right
                charWorldX = snipeWorldX;
                arrowWorldX = _map.WrapX(snipeWorldX + 1);
            }
            positionsToClear.Remove((charWorldX, snipeWorldY));
            positionsToClear.Remove((arrowWorldX, snipeWorldY));
        }
    }

    /// <summary>
    /// Clears snipe positions that are no longer occupied
    /// </summary>
    private void ClearSnipePreviousPositions(HashSet<(int x, int y)> positionsToClear, int frameWidth, int frameHeight)
    {
        SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
        foreach (var (worldX, worldY) in positionsToClear)
        {
            // Calculate viewport coordinates
            int deltaX = worldX - _player.X;
            int deltaY = worldY - _player.Y;

            // Handle wrapping
            _map.WrapDeltaX(ref deltaX);
            _map.WrapDeltaY(ref deltaY);

            int viewportX = (frameWidth / 2) + deltaX;
            int viewportY = (frameHeight / 2) + deltaY;

            if (viewportX >= 0 && viewportX < frameWidth &&
                viewportY >= 0 && viewportY < frameHeight &&
                _map.IsValidCoordinate(worldX, worldY))
            {
                char mapChar = _map.FullMap[worldY][worldX];
                Move(viewportX, viewportY + StatusBarHeight);
                AddRune(mapChar);
            }
        }
    }

    /// <summary>
    /// Draws a single snipe at its current position
    /// </summary>
    private void DrawSnipeAtPosition(Snipe snipe, int frameWidth, int frameHeight)
    {
        // Calculate viewport coordinates
        int deltaX = snipe.X - _player.X;
        int deltaY = snipe.Y - _player.Y;

        // Handle wrapping
        _map.WrapDeltaX(ref deltaX);
        _map.WrapDeltaY(ref deltaY);

        int viewportX = (frameWidth / 2) + deltaX;
        int viewportY = (frameHeight / 2) + deltaY;

        // Only draw if within viewport
        if (viewportX >= 0 && viewportX < frameWidth &&
            viewportY >= 0 && viewportY < frameHeight)
        {
            // Set color based on snipe type: TypeA = magenta, TypeB = green
            var snipeColor = snipe.Type == SnipeType.TypeA ? Color.Magenta : Color.Green;
            SetAttribute(new DrawingAttribute(snipeColor, Color.Black));

            // Draw order depends on direction:
            // Moving left: arrow first, then character
            // Moving right or other: character first, then arrow
            if (snipe.DirectionX < 0)
            {
                // Moving left - draw arrow first, then character
                Move(viewportX, viewportY + StatusBarHeight);
                AddRune(snipe.GetDirectionArrow());

                if (viewportX + 1 < frameWidth)
                {
                    Move(viewportX + 1, viewportY + StatusBarHeight);
                    AddRune(snipe.GetDisplayChar());
                }
            }
            else
            {
                // Moving right or other directions - draw character first, then arrow
                Move(viewportX, viewportY + StatusBarHeight);
                AddRune(snipe.GetDisplayChar());

                if (viewportX + 1 < frameWidth)
                {
                    Move(viewportX + 1, viewportY + StatusBarHeight);
                    AddRune(snipe.GetDirectionArrow());
                }
            }
        }
    }

    /// <summary>
    /// Updates PreviousX/PreviousY for all snipes to match what was actually drawn
    /// </summary>
    private void UpdateSnipePreviousPositions(List<Snipe> snipes)
    {
        foreach (var snipe in snipes)
        {
            if (!snipe.IsAlive)
                continue;

            // Update previous position to current position (what was just drawn)
            snipe.PreviousX = snipe.X;
            snipe.PreviousY = snipe.Y;
            snipe.PreviousDirectionX = snipe.DirectionX;
            snipe.PreviousDirectionY = snipe.DirectionY;
        }
    }

    private void DrawSnipes()
    {
        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Use cached map viewport if available, otherwise get new one
        var map = _cachedMapViewport;
        if (map == null || map.Length != frameHeight)
        {
            map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
            _cachedMapViewport = map;
        }

        // Step 1: Build a list of all positions that snipes PREVIOUSLY occupied
        var positionsToClear = BuildPreviousSnipePositions();

        // Step 2: Remove current positions from clear list (don't clear positions that are still occupied)
        RemoveCurrentSnipePositions(positionsToClear);

        // Step 3: Clear all positions that remain in positionsToClear (no longer occupied)
        ClearSnipePreviousPositions(positionsToClear, frameWidth, frameHeight);

        // Step 4: Draw snipes at their new positions (snapshot to avoid collection modified during iteration)
        List<Snipe> snipesCopy;
        lock (_gameStateLock) { snipesCopy = new List<Snipe>(_snipes); }
        foreach (var snipe in snipesCopy)
        {
            if (!snipe.IsAlive)
                continue;

            DrawSnipeAtPosition(snipe, frameWidth, frameHeight);
        }

        SetAttribute(new DrawingAttribute(Color.White, Color.Black));

        // CRITICAL: Update PreviousX/PreviousY to match what was actually drawn
        UpdateSnipePreviousPositions(snipesCopy);
    }

    // Callback for IntroScreen to get map character at position during clearing effect
    private char GetMapCharAtPosition(int x, int y)
    {
        if (_map == null)
            return ' ';

        int frameWidth = Frame.Width;
        int frameHeight = Frame.Height - StatusBarHeight;

        // Get map viewport
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        if (y >= 0 && y < frameHeight && x >= 0 && x < frameWidth && map != null && y < map.Length && x < map[y].Length)
        {
            return map[y][x];
        }

        return ' ';
    }

    private void DrawMapAndPlayerAtPosition(int x, int y)
    {
        if (false || _map == null)
            return;

        // Calculate which part of the map should be at this position
        int frameWidth = Frame.Width;
        int frameHeight = Frame.Height - StatusBarHeight;

        if (y < StatusBarHeight)
        {
            // Status bar area - just draw a space (status bar will be drawn separately)
            SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
            Move(x, y);
            AddRune(' ');
            return;
        }

        // Get map viewport
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        int mapY = y - StatusBarHeight;

        // Check if player should be drawn here first (player is on top)
        int playerCenterX = frameWidth / 2;
        int playerCenterY = frameHeight / 2;
        int playerTopLeftX = playerCenterX;
        int playerTopLeftY = playerCenterY + StatusBarHeight;

        if (x >= playerTopLeftX && x < playerTopLeftX + 2 &&
            y >= playerTopLeftY && y < playerTopLeftY + 3)
        {
            DrawPlayerAtPosition(x, y, playerTopLeftX, playerTopLeftY);
            return;
        }

        // Draw map character
        if (mapY >= 0 && mapY < frameHeight && x >= 0 && x < frameWidth && map != null && mapY < map.Length && x < map[mapY].Length)
        {
            SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
            Move(x, y);
            AddRune(map[mapY][x]);
        }
    }

    private void DrawPlayerAtPosition(int x, int y, int playerTopLeftX, int playerTopLeftY)
    {
        

        int relX = x - playerTopLeftX;
        int relY = y - playerTopLeftY;

        SetAttribute(new DrawingAttribute(Color.BrightYellow, Color.Black));
        Move(x, y);

        // Player is 2x3: "BD" on first row, "BD" on second row, "BD" on third row
        if (relX == 0 && relY == 0)
            AddRune('B');
        else if (relX == 1 && relY == 0)
            AddRune('D');
        else if (relX == 0 && relY == 1)
            AddRune('B');
        else if (relX == 1 && relY == 1)
            AddRune('D');
        else if (relX == 0 && relY == 2)
            AddRune('B');
        else if (relX == 1 && relY == 2)
            AddRune('D');
    }

    private void ClearSnipePosition(Snipe snipe)
    {
        

        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Calculate viewport coordinates
        int deltaX = snipe.X - _player.X;
        int deltaY = snipe.Y - _player.Y;

        // Handle wrapping
        _map.WrapDeltaX(ref deltaX);
        _map.WrapDeltaY(ref deltaY);

        int viewportX = (frameWidth / 2) + deltaX;
        int viewportY = (frameHeight / 2) + deltaY;

        if (viewportX >= 0 && viewportX < frameWidth &&
            viewportY >= 0 && viewportY < frameHeight)
        {
            SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));

            // Clear '@' character and arrow based on direction
            // When moving left (DirectionX < 0): arrow is at viewportX, '@' is at viewportX + 1
            // When moving right or other: '@' is at viewportX, arrow is at viewportX + 1
            int charViewportX; // Where the '@' character is
            int arrowViewportX; // Where the arrow is

            if (snipe.DirectionX < 0)
            {
                // Moving left: arrow first, then '@'
                arrowViewportX = viewportX;
                charViewportX = viewportX + 1;
            }
            else
            {
                // Moving right or other: '@' first, then arrow
                charViewportX = viewportX;
                arrowViewportX = viewportX + 1;
            }

            // Clear '@' character position
            if (charViewportX >= 0 && charViewportX < frameWidth)
            {
                char mapChar = _map.FullMap[_map.WrapY(snipe.Y)][_map.WrapX(snipe.X)];
                Move(charViewportX, viewportY + StatusBarHeight);
                AddRune(mapChar);
            }

            // Clear arrow position if within viewport
            if (arrowViewportX >= 0 && arrowViewportX < frameWidth)
            {
                // Calculate world coordinates for arrow position
                int arrowWorldX = snipe.X + (snipe.DirectionX < 0 ? -1 : 1);
                int arrowWorldY = snipe.Y;

                // Wrap coordinates
                arrowWorldX = _map.WrapX(arrowWorldX);
                arrowWorldY = _map.WrapY(arrowWorldY);

                if (_map.IsValidCoordinate(arrowWorldX, arrowWorldY))
                {
                    char arrowMapChar = _map.FullMap[arrowWorldY][arrowWorldX];
                    Move(arrowViewportX, viewportY + StatusBarHeight);
                    AddRune(arrowMapChar);
                }
            }

            SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        }
    }

    private void ClearHivePosition(Hive hive)
    {
        

        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Calculate viewport coordinates for the hive
        int deltaX = hive.X - _player.X;
        int deltaY = hive.Y - _player.Y;

        // Handle wrapping
        _map.WrapDeltaX(ref deltaX);
        _map.WrapDeltaY(ref deltaY);

        int hiveViewportX = (frameWidth / 2) + deltaX;
        int hiveViewportY = (frameHeight / 2) + deltaY;

        // Clear all 4 corners of the 2x2 hive
        SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));

        // Top-left corner
        if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
            hiveViewportY >= 0 && hiveViewportY < frameHeight)
        {
            int worldX = _map.WrapX(hive.X);
            int worldY = _map.WrapY(hive.Y);
            char mapChar = _map.FullMap[worldY][worldX];
            Move(hiveViewportX, hiveViewportY + StatusBarHeight);
            AddRune(mapChar);
        }

        // Top-right corner
        int topRightX = hiveViewportX + 1;
        if (topRightX >= 0 && topRightX < frameWidth &&
            hiveViewportY >= 0 && hiveViewportY < frameHeight)
        {
            int worldX = _map.WrapX(hive.X + 1);
            int worldY = _map.WrapY(hive.Y);
            char mapChar = _map.FullMap[worldY][worldX];
            Move(topRightX, hiveViewportY + StatusBarHeight);
            AddRune(mapChar);
        }

        // Bottom-left corner
        int bottomLeftY = hiveViewportY + 1;
        if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
            bottomLeftY >= 0 && bottomLeftY < frameHeight)
        {
            int worldX = _map.WrapX(hive.X);
            int worldY = _map.WrapY(hive.Y + 1);
            char mapChar = _map.FullMap[worldY][worldX];
            Move(hiveViewportX, bottomLeftY + StatusBarHeight);
            AddRune(mapChar);
        }

        // Bottom-right corner
        if (topRightX >= 0 && topRightX < frameWidth &&
            bottomLeftY >= 0 && bottomLeftY < frameHeight)
        {
            int worldX = _map.WrapX(hive.X + 1);
            int worldY = _map.WrapY(hive.Y + 1);
            char mapChar = _map.FullMap[worldY][worldX];
            Move(topRightX, bottomLeftY + StatusBarHeight);
            AddRune(mapChar);
        }

        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
    }

    private (int x, int y) FindRandomValidPosition()
    {
        const int MAX_ATTEMPTS = 1000; // Prevent infinite loop

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            // Pick a random position on the map
            // Player is PlayerWidth columns wide, so X must be at least (PlayerWidth-1) from the right edge
            // Player is PlayerHeight rows tall, so Y must be at least (PlayerHeight-1) from the bottom edge
            int x = Random.Shared.Next(0, _map.MapWidth - (PlayerWidth - 1));
            int y = Random.Shared.Next(0, _map.MapHeight - (PlayerHeight - 1));

            // Check if all cells (PlayerWidth x PlayerHeight) at this position are walkable
            if (IsPositionValid(x, y))
            {
                return (x, y);
            }
        }

        // Fallback: if we can't find a random position, try systematically
        for (int y = 0; y < _map.MapHeight - 2; y++)
        {
            for (int x = 0; x < _map.MapWidth - 1; x++)
            {
                if (IsPositionValid(x, y))
                {
                    return (x, y);
                }
            }
        }

        // Last resort: return a default position (shouldn't happen on a valid map)
        return (1, 1);
    }

    private bool IsPositionValid(int x, int y)
    {
        // Check if all cells (PlayerWidth columns x PlayerHeight rows) starting at (x, y) are walkable
        // Player occupies: columns [x, x+PlayerWidth-1], rows [y, y+PlayerHeight-1]

        // Bounds check
        if (x < 0 || x + (PlayerWidth - 1) >= _map.MapWidth || y < 0 || y + (PlayerHeight - 1) >= _map.MapHeight)
            return false;

        // Check all cells are spaces (walkable)
        for (int row = y; row <= y + (PlayerHeight - 1); row++)
        {
            for (int col = x; col <= x + (PlayerWidth - 1); col++)
            {
                if (_map.FullMap[row][col] != ' ')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void ResetGame()
    {
        // For multiplayer, player positions are already set in StartMultiplayerGameSession()
        // Only reset player position for single-player games
        if (!_isMultiplayer)
        {
            // Reset player position and state
            var (x, y) = FindRandomValidPosition();
            _player.X = x;
            _player.Y = y;
        }
        else
        {
            // For multiplayer, use position from network player if available
            if (_gameSession != null && _networkPlayers.TryGetValue(_gameSession.PlayerId, out var localNetworkPlayer))
            {
                _player.X = localNetworkPlayer.X;
                _player.Y = localNetworkPlayer.Y;
            }
        }
        
        _player.Lives = Player.InitialLives;
        _player.Score = Player.InitialScore;
        _player.IsAlive = true;
        _player.Initials = _config.Initials; // Ensure initials are current
        
        // Reset game state (preserve level if it was set by user)
        // Level is set by IntroScreen before calling ResetGame, so we preserve it
        int currentLevel = _gameState.Level;
        if (currentLevel < 1)
        {
            currentLevel = 1; // Only reset to 1 if level wasn't set
        }
        _gameState.Level = currentLevel;
        _gameState.Score = Player.InitialScore;
        _gameState.TotalHives = 0;
        _gameState.HivesUndestroyed = 0;
        _gameState.TotalSnipes = 0;
        _gameState.SnipesUndestroyed = 0;
        
        // Clear all game entities
        _bullets.Clear();
        _hives.Clear();
        _snipes.Clear();
        
        // Only host initializes hives - clients will receive them via gRPC
        if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
        {
            InitializeHives();
            
            // Publish hive positions to clients
            if (_isMultiplayer && _gameSession != null && _gameSession.Role == GameSessionRole.Host)
            {
                PublishGameStateSnapshot();
            }
        }
        
        // Reset drawing state
        _mapDrawn = false;
        _pressedKeys.Clear(); // Clear any lingering pressed keys
        
        // Reset cached values
        _cachedMapViewport = null;
        _cachedDateTime = DateTime.MinValue;
        _previousPlayerViewportX = -1;
        _previousPlayerViewportY = -1;
    }

    private void InitializeHives()
    {
        _hives.Clear();
        _snipes.Clear();
        int hiveCount = _gameState.GetHiveCountForLevel(_gameState.Level);
        int snipesPerHive = _gameState.GetSnipesPerHiveForLevel(_gameState.Level);
        _gameState.TotalHives = hiveCount;
        _gameState.HivesUndestroyed = hiveCount;
        _gameState.TotalSnipes = hiveCount * snipesPerHive;
        // SnipesUndestroyed = all snipes in hives (they haven't spawned yet, but they exist)
        // This will decrease as snipes spawn (they move from "in hive" to "spawned")
        // and as spawned snipes are killed
        _gameState.SnipesUndestroyed = _gameState.TotalSnipes;

        for (int i = 0; i < hiveCount; i++)
        {
            var (x, y) = FindRandomValidHivePosition();
            _hives.Add(new Hive(x, y, snipesPerHive, _currentFrameTime));
        }
    }

    private void CheckLevelComplete()
    {
        // Only check if level is complete (host only in multiplayer)
        if (_isMultiplayer && (_gameSession == null || _gameSession.Role != GameSessionRole.Host))
            return;
            
        if (_gameState.IsLevelComplete())
        {
            // Level complete! Advance to next level
            StartNextLevel();
        }
    }
    
    private void CheckGameOver()
    {
        // Check if all players have lost all lives
        bool allPlayersDead = true;
        List<PlayerScoreInfo> playerScores = new List<PlayerScoreInfo>(_networkPlayers.Count + 1); // Local player + network players
        
        if (_isMultiplayer && _gameSession != null)
        {
            // Check all network players
            foreach (var networkPlayer in _networkPlayers.Values)
            {
                playerScores.Add(new PlayerScoreInfo
                {
                    Initials = networkPlayer.Initials,
                    Score = networkPlayer.Score
                });
                
                if (networkPlayer.IsAlive && networkPlayer.Lives > 0)
                {
                    allPlayersDead = false;
                }
            }
        }
        else
        {
            // Single player
            playerScores.Add(new PlayerScoreInfo
            {
                Initials = _player.Initials,
                Score = _player.Score
            });
            
            if (_player.IsAlive && _player.Lives > 0)
            {
                allPlayersDead = false;
            }
        }
        
        if (allPlayersDead)
        {
            // Game over - show game over screen with player scores
            _introScreen.ShowGameOver(playerScores);
        }
    }
    
    private void StartNextLevel()
    {
        // Advance to next level
        _gameState.Level++;
        
        // Calculate level info for display
        int hiveCount = _gameState.GetHiveCountForLevel(_gameState.Level);
        int snipesPerHive = _gameState.GetSnipesPerHiveForLevel(_gameState.Level);
        int totalSnipes = hiveCount * snipesPerHive;
        
        // Show level start message
        string levelMessage = $"LEVEL {_gameState.Level} - {hiveCount} HIVES with {totalSnipes} SNIPES";
        
        // Reset player positions for all players (random positions)
        if (_isMultiplayer && _gameSession != null)
        {
            // Reset all network players to random positions
            foreach (var networkPlayer in _networkPlayers.Values)
            {
                var (x, y) = FindRandomValidPositionForMultiplayer();
                networkPlayer.X = x;
                networkPlayer.Y = y;
                networkPlayer.PreviousX = x;
                networkPlayer.PreviousY = y;
                
                // Update local player position if this is the local player
                if (networkPlayer.IsLocal)
                {
                    _player.X = x;
                    _player.Y = y;
                }
            }
        }
        else
        {
            // Single player - reset position
            var (x, y) = FindRandomValidPosition();
            _player.X = x;
            _player.Y = y;
        }
        
        // Clear all game entities
        _bullets.Clear();
        _hives.Clear();
        _snipes.Clear();
        
        // Initialize new level (host only in multiplayer)
        if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
        {
            InitializeHives();
            
            // Publish game state snapshot in multiplayer
            if (_isMultiplayer && _gameSession != null && _gameSession.Role == GameSessionRole.Host && _grpcClient != null)
            {
                // Small delay to ensure subscriptions are active
                _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(SubscriptionDelayMs), () =>
                {
                    PublishGameStateSnapshot();
                    return false; // One-time
                });
            }
        }
        
        // Show clearing effect with level message
        _introScreen.StartClearingEffect(levelMessage, isStartingNewGame: false);
        _mapDrawn = false;
        _pressedKeys.Clear();
        
        // Reset cached values
        _cachedMapViewport = null;
        _cachedDateTime = DateTime.MinValue;
        _previousPlayerViewportX = -1;
        _previousPlayerViewportY = -1;
    }

    private (int x, int y) FindRandomValidHivePosition()
    {
        const int MAX_ATTEMPTS = 1000;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            // Hive is 2x2, so we need space for that
            int x = Random.Shared.Next(0, _map.MapWidth - 1); // -1 because we need 2 columns
            int y = Random.Shared.Next(0, _map.MapHeight - 1); // -1 because we need 2 rows

            // Check if the 2x2 area is valid and doesn't overlap with player or existing hives
            if (IsHivePositionValid(x, y))
            {
                return (x, y);
            }
        }

        // Fallback: try systematically
        for (int y = 0; y < _map.MapHeight - 1; y++)
        {
            for (int x = 0; x < _map.MapWidth - 1; x++)
            {
                if (IsHivePositionValid(x, y))
                {
                    return (x, y);
                }
            }
        }

        // Last resort
        return (1, 1);
    }

    private bool IsHivePositionValid(int x, int y)
    {
        // Check if all 4 cells (2x2) starting at (x, y) are walkable
        // Hive occupies: columns [x, x+1], rows [y, y+1]

        // Bounds check
        if (x < 0 || x + 1 >= _map.MapWidth || y < 0 || y + 1 >= _map.MapHeight)
            return false;

        // Check all 4 cells are spaces (walkable)
        for (int row = y; row <= y + 1; row++)
        {
            for (int col = x; col <= x + 1; col++)
            {
                if (_map.FullMap[row][col] != ' ')
                {
                    return false;
                }
            }
        }

        // Check that hive doesn't overlap with player
        // Player occupies: columns [player.X, player.X+PlayerWidth-1], rows [player.Y, player.Y+PlayerHeight-1]
        if (x >= _player.X - 1 && x <= _player.X + PlayerWidth && y >= _player.Y - 1 && y <= _player.Y + PlayerHeight)
        {
            return false;
        }

        // Check that hive doesn't overlap with existing hives
        foreach (var existingHive in _hives)
        {
            if (x >= existingHive.X - 1 && x <= existingHive.X + 1 &&
                y >= existingHive.Y - 1 && y <= existingHive.Y + 1)
            {
                return false;
            }
        }

        return true;
    }

    private void DrawStatusBar()
    {
        // In Terminal.Gui v2, OnDrawingContent clears the view each time,
        // so we must redraw the status bar every frame (no caching optimization)
        int currentFPS = (int)Math.Round(_currentFPS);
        
        int currentWidth = Frame.Width;

        // Set status bar color: white text on blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));

        // Draw status bar with hive shapes
        Move(0, 0);

        // Draw hive indicator (small box shape) with fixed color (cyan - first hive color)
        // Using fixed color to reduce status bar updates
        SetAttribute(new DrawingAttribute(Color.Cyan, Color.Blue));
        this.AddString("╔╗"); // Top corners of hive

        // Reset to status bar color and position
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        string hivesText = $" {_gameState.HivesUndestroyed}/{_gameState.TotalHives}  ";
        this.AddString(hivesText);

        // Draw snipes count
        string snipesText = $"Snipes: {_gameState.SnipesUndestroyed}/{_gameState.TotalSnipes}  ";
        this.AddString(snipesText);

        // Draw lives
        string livesText = $"Lives: {_player.Lives}  ";
        this.AddString(livesText);

        // Draw level
        string levelText = $"Level: {_gameState.Level}  ";
        this.AddString(levelText);

        // Draw score
        string scoreText = $"Score: {_gameState.Score}  ";
        this.AddString(scoreText);

        // Draw FPS (currentFPS already calculated at top of method)
        string fpsText = $"FPS: {currentFPS}";
        this.AddString(fpsText);

        // Calculate current cursor position
        int currentPos = 2 + hivesText.Length + snipesText.Length + livesText.Length + 
                        levelText.Length + scoreText.Length + fpsText.Length;
        
        // Clear rest of first row
        if (currentPos < currentWidth)
        {
            this.AddString(new string(' ', currentWidth - currentPos));
        }

        // Draw second row with bottom of hive
        Move(0, 1);
        SetAttribute(new DrawingAttribute(Color.Cyan, Color.Blue));
        this.AddString("╚╝");
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        this.AddString(new string(' ', currentWidth - 2));

        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
    }

    private void DrawPlayer()
    {
        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;

        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // draw the player
        // _player.X, _player.Y represents top-left corner of player
        // Map.GetMap centers viewport on (_player.X, _player.Y)
        // So top-left in viewport is at (frameWidth/2, frameHeight/2)
        // Offset by StatusBarHeight to account for status bar
        int topLeftCol = frameWidth / 2;
        int topLeftRow = (frameHeight / 2) + StatusBarHeight;

        // Use cached frame time instead of calling DateTime.Now
        _cachedDateTime = _currentFrameTime;

        var eyes = _cachedDateTime.Millisecond < 500 ? "ÔÔ" : "OO";
        var mouth = _cachedDateTime.Millisecond < 500 ? "◄►" : "◂▸";

        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        Move(topLeftCol, topLeftRow);
        AddStr(eyes);
        Move(topLeftCol, topLeftRow + 1);
        AddStr(mouth);
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        Move(topLeftCol, topLeftRow + 2);
        AddStr(_player.Initials);
    }
    
    private void DrawRemotePlayers()
    {
        if (!_isMultiplayer || _gameSession == null || false)
            return;
        
        List<PlayerNetwork> playersCopy;
        lock (_gameStateLock) { playersCopy = new List<PlayerNetwork>(_networkPlayers.Values); }
        
        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
        
        foreach (var networkPlayer in playersCopy)
        {
            if (networkPlayer.IsLocal)
                continue; // Skip local player (already drawn)
            
            // Calculate delta between remote player and local player world positions, handling wrapping
            int deltaX = networkPlayer.X - _player.X;
            int deltaY = networkPlayer.Y - _player.Y;
            
            // Adjust delta for map wrapping to find shortest path
            _map.WrapDeltaX(ref deltaX);
            _map.WrapDeltaY(ref deltaY);
            
            // Convert to viewport coordinates
            int viewportX = frameWidth / 2 + deltaX;
            int viewportY = frameHeight / 2 + deltaY;
            
            // Only draw if within viewport (PlayerWidth x PlayerHeight player area)
            if (viewportX + PlayerWidth > 0 && viewportX < frameWidth &&
                viewportY + PlayerHeight > 0 && viewportY < frameHeight)
            {
                // Draw remote player (different color to distinguish from local)
                SetAttribute(new DrawingAttribute(Color.Yellow, Color.Black));
                
                // Draw eyes (same as local player but different color)
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY >= 0 && viewportY < frameHeight)
                {
                    var eyes = _currentFrameTime.Millisecond < 500 ? "ÔÔ" : "OO";
                    Move(viewportX, viewportY + StatusBarHeight);
                    this.AddString(eyes);
                }
                
                // Draw mouth
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 1 >= 0 && viewportY + 1 < frameHeight)
                {
                    var mouth = _currentFrameTime.Millisecond < 500 ? "◄►" : "◂▸";
                    Move(viewportX, viewportY + 1 + StatusBarHeight);
                    this.AddString(mouth);
                }
                
                // Draw initials
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 2 >= 0 && viewportY + 2 < frameHeight)
                {
                    Move(viewportX, viewportY + 2 + StatusBarHeight);
                    this.AddString(networkPlayer.Initials);
                }
                
                // Track where we drew this player (viewport coordinates)
                networkPlayer.LastDrawnViewportX = viewportX;
                networkPlayer.LastDrawnViewportY = viewportY;
            }
            else
            {
                // Player is off-screen, mark as not drawn
                networkPlayer.LastDrawnViewportX = -1;
                networkPlayer.LastDrawnViewportY = -1;
            }
        }
        
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
    }
    
    private void DrawRemotePlayersWithClearing()
    {
        if (!_isMultiplayer || _gameSession == null || false)
            return;
        
        List<PlayerNetwork> playersCopy;
        lock (_gameStateLock) { playersCopy = new List<PlayerNetwork>(_networkPlayers.Values); }
        
        int currentWidth = Frame.Width;
        int currentHeight = Frame.Height;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
        
        // Get map viewport for clearing
        var map = _cachedMapViewport;
        if (map == null || map.Length != frameHeight)
        {
            map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
        }
        
        foreach (var networkPlayer in playersCopy)
        {
            if (networkPlayer.IsLocal)
                continue; // Skip local player (already drawn)
            
            // Calculate delta between remote player and local player world positions, handling wrapping
            int deltaX = networkPlayer.X - _player.X;
            int deltaY = networkPlayer.Y - _player.Y;
            
            // Adjust delta for map wrapping to find shortest path
            _map.WrapDeltaX(ref deltaX);
            _map.WrapDeltaY(ref deltaY);
            
            // Always clear previous position before drawing new one (to prevent artifacts)
            // Check if we need to clear (position changed)
            if (networkPlayer.PreviousX != networkPlayer.X || networkPlayer.PreviousY != networkPlayer.Y)
            {
                int prevDeltaX = networkPlayer.PreviousX - _player.X;
                int prevDeltaY = networkPlayer.PreviousY - _player.Y;
                
                // Adjust for map wrapping
                if (prevDeltaX > _map.MapWidth / 2) prevDeltaX -= _map.MapWidth;
                else if (prevDeltaX < -_map.MapWidth / 2) prevDeltaX += _map.MapWidth;
                
                if (prevDeltaY > _map.MapHeight / 2) prevDeltaY -= _map.MapHeight;
                else if (prevDeltaY < -_map.MapHeight / 2) prevDeltaY += _map.MapHeight;
                
                int prevViewportX = frameWidth / 2 + prevDeltaX;
                int prevViewportY = frameHeight / 2 + prevDeltaY;
                
                // Clear previous position (PlayerWidth x PlayerHeight area) - but only if it's different from current
                int currentViewportX = frameWidth / 2 + deltaX;
                int currentViewportY = frameHeight / 2 + deltaY;
                
                if ((prevViewportX != currentViewportX || prevViewportY != currentViewportY) &&
                    prevViewportX + 2 > 0 && prevViewportX < frameWidth &&
                    prevViewportY + 3 > 0 && prevViewportY < frameHeight &&
                    map != null)
                {
                    SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 2; col++)
                        {
                            int clearX = prevViewportX + col;
                            int clearY = prevViewportY + row;
                            if (clearX >= 0 && clearX < frameWidth &&
                                clearY >= 0 && clearY < frameHeight &&
                                clearY < map.Length &&
                                clearX < map[clearY].Length)
                            {
                                char mapChar = map[clearY][clearX];
                                Move(clearX, clearY + StatusBarHeight);
                                AddRune(mapChar);
                            }
                        }
                    }
                }
            }
            
            // Convert to viewport coordinates
            int viewportX = frameWidth / 2 + deltaX;
            int viewportY = frameHeight / 2 + deltaY;
            
            // Only draw if within viewport (PlayerWidth x PlayerHeight player area)
            if (viewportX + PlayerWidth > 0 && viewportX < frameWidth &&
                viewportY + PlayerHeight > 0 && viewportY < frameHeight)
            {
                // Draw remote player (different color to distinguish from local)
                SetAttribute(new DrawingAttribute(Color.Yellow, Color.Black));
                
                // Draw eyes (same as local player but different color)
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY >= 0 && viewportY < frameHeight)
                {
                    var eyes = _currentFrameTime.Millisecond < 500 ? "ÔÔ" : "OO";
                    Move(viewportX, viewportY + StatusBarHeight);
                    this.AddString(eyes);
                }
                
                // Draw mouth
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 1 >= 0 && viewportY + 1 < frameHeight)
                {
                    var mouth = _currentFrameTime.Millisecond < 500 ? "◄►" : "◂▸";
                    Move(viewportX, viewportY + 1 + StatusBarHeight);
                    this.AddString(mouth);
                }
                
                // Draw initials
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 2 >= 0 && viewportY + 2 < frameHeight)
                {
                    Move(viewportX, viewportY + 2 + StatusBarHeight);
                    this.AddString(networkPlayer.Initials);
                }
                
                // Track where we drew this player (viewport coordinates) for proper clearing next frame
                networkPlayer.LastDrawnViewportX = viewportX;
                networkPlayer.LastDrawnViewportY = viewportY;
            }
            else
            {
                // Player is off-screen, mark as not drawn
                networkPlayer.LastDrawnViewportX = -1;
                networkPlayer.LastDrawnViewportY = -1;
            }
        }
        
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
    }
    
    private async Task StartMultiplayerGame(int maxPlayers)
    {
        // Single player mode - no network, just start the game locally
        if (maxPlayers == 1)
        {
            // Ensure multiplayer is disabled
            _isMultiplayer = false;
            _hasReceivedMultiplayerSnapshot = false;
            _gameSession = null;
            _grpcClient = null;
            _networkPlayers.Clear();
            
            // Reset game state and start
            ResetGame();
            _introScreen.StartGame();
            return;
        }
        
        // Multiplayer mode (2+ players) - use gRPC
        // Show "Connecting..." screen immediately so user knows something is happening
        _introScreen.ShowWaitingForPlayers("Connecting...", maxPlayers, isHost: true);
        
        try
        {
            // Create gRPC client
            _grpcClient = new GrpcGameClient();
            _grpcClient.OnGameMessageReceived += HandleGrpcMessage;
            _grpcClient.OnConnected += () =>
            {
                // Connection successful
            };
            _grpcClient.OnConnectionError += (error) =>
            {
                // Handle connection error - show message to user
                // For now, just log or handle silently
            };
            
            // Connect to gRPC server using configured address
            string serverUrl = _config.GetServerUrl();
            bool connected = await _grpcClient.ConnectAsync(serverUrl);
            if (!connected)
            {
                // Failed to connect - return to menu
                _introScreen.Show();
                return;
            }
            
            // Generate player ID first
            var playerId = GameSession.GeneratePlayerId();
            
            // Create game on server
            string gameId;
            try
            {
                gameId = await _grpcClient.CreateGameAsync(
                    playerId,
                    _player.Initials,
                    maxPlayers,
                    _gameState.Level
                );
                
                // Validate game ID was returned
                if (string.IsNullOrEmpty(gameId))
                {
                    // Failed to get game ID - show error and return to menu
                    _introScreen.ShowWaitingForPlayers("ERROR: No game ID returned", maxPlayers, isHost: true);
                    // Wait a moment so user can see the error
                    await Task.Delay(2000);
                    _introScreen.Show();
                    return;
                }
                
                // Update waiting screen with actual game ID immediately
                _introScreen.ShowWaitingForPlayers(gameId, maxPlayers, isHost: true);
            }
            catch (InvalidOperationException ex)
            {
                // Failed to create game (already logged via OnConnectionError) - show error message
                _introScreen.ShowWaitingForPlayers($"ERROR: {ex.Message}", maxPlayers, isHost: true);
                // Wait a moment so user can see the error
                await Task.Delay(2000);
                _introScreen.Show();
                return;
            }
            catch (Exception ex)
            {
                // Unexpected error - show error message
                _introScreen.ShowWaitingForPlayers($"ERROR: {ex.Message}", maxPlayers, isHost: true);
                // Wait a moment so user can see the error
                await Task.Delay(2000);
                _introScreen.Show();
                return;
            }
            
            // Create game session
            _gameSession = new GameSession
            {
                GameId = gameId,
                PlayerId = playerId,
                Role = GameSessionRole.Host,
                Status = GameSessionStatus.WaitingForPlayers,
                MaxPlayers = maxPlayers,
                CurrentPlayers = 1,
                CreatedAt = DateTime.UtcNow
            };
            
            _isMultiplayer = true;
            
            // Add host as first player
            var hostPlayer = new NetworkPlayerInfo
            {
                PlayerId = _gameSession.PlayerId,
                Initials = _player.Initials,
                PlayerNumber = 1
            };
            _gameSession.Players.Add(hostPlayer);
            
            // Start game stream
            bool streamStarted = await _grpcClient.StartGameStreamAsync(_gameSession.GameId, _gameSession.PlayerId);
            if (!streamStarted)
            {
                _introScreen.Show();
                return;
            }
            
            // Ensure waiting screen shows the actual game ID (in case it wasn't updated earlier)
            _introScreen.ShowWaitingForPlayers(_gameSession.GameId, maxPlayers, isHost: true);
            
            // Start timer to publish player count updates
            _app.TimedEvents?.Add(TimeSpan.FromSeconds(1), () =>
            {
                if (_gameSession != null && _gameSession.Status == GameSessionStatus.WaitingForPlayers)
                {
                    PublishPlayerCountUpdate();
                    return true;
                }
                return false; // Stop timer when game starts
            });
            
            // Server starts game when 60s or max players; host and clients receive GameStartMessage from server
        }
        catch (Exception)
        {
            // Handle error - return to menu
            _introScreen.Show();
        }
    }
    
    private async Task JoinGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            _introScreen.Show();
            return;
        }
        
        try
        {
            // Create gRPC client
            _grpcClient = new GrpcGameClient();
            _grpcClient.OnGameMessageReceived += HandleGrpcMessage;
            
            // Connect to gRPC server using configured address
            string serverUrl = _config.GetServerUrl();
            bool connected = await _grpcClient.ConnectAsync(serverUrl);
            if (!connected)
            {
                _introScreen.Show();
                return;
            }
            
            // Generate player ID
            var playerId = GameSession.GeneratePlayerId();
            
            // Join game on server
            JoinResponse joinResponse;
            try
            {
                joinResponse = await _grpcClient.JoinGameAsync(gameId.ToUpper(), playerId, _player.Initials);
            }
            catch (InvalidOperationException)
            {
                // Failed to join game (already logged via OnConnectionError) - return to menu
                _introScreen.Show();
                return;
            }
            catch (ArgumentException)
            {
                // Invalid arguments (already logged via OnConnectionError) - return to menu
                _introScreen.Show();
                return;
            }
            catch (Exception)
            {
                // Unexpected error - return to menu
                // Note: GrpcGameClient should have logged this via OnConnectionError
                _introScreen.Show();
                return;
            }
            
            if (joinResponse == null)
            {
                _introScreen.Show();
                return;
            }
            
            if (!joinResponse.Accepted)
            {
                // Join rejected - return to menu
                _introScreen.Show();
                return;
            }
            
            // Create game session
            _gameSession = new GameSession
            {
                GameId = gameId.ToUpper(),
                PlayerId = playerId,
                Role = GameSessionRole.Client,
                Status = GameSessionStatus.WaitingForPlayers
            };
            
            _isMultiplayer = true;
            
            // Start game stream - this must happen before we can receive player join notifications
            bool streamStarted = await _grpcClient.StartGameStreamAsync(_gameSession.GameId, _gameSession.PlayerId);
            if (!streamStarted)
            {
                _introScreen.Show();
                return;
            }
            
            // If server provided initial state, apply it
            if (joinResponse.InitialState != null)
            {
                HandleGameStateSnapshot(joinResponse.InitialState);
            }
            
            // Show waiting screen (maxPlayers will be updated when we receive PlayerJoinNotification or PlayerCountUpdate)
            // For now, use a default value - it will be updated when we receive the first player count message
            _introScreen.ShowWaitingForPlayers(_gameSession.GameId, 2, isHost: false);
        }
        catch (Exception)
        {
            // Unexpected error - return to menu
            _introScreen.Show();
        }
    }
    
    private void HandleGrpcMessage(GameMessage message)
    {
        if (message == null)
            return;
        
        try
        {
            if (_gameSession == null)
                return;
            
            if (string.IsNullOrWhiteSpace(message.GameId) || message.GameId != _gameSession.GameId)
                return;
            
            // Ignore messages from self
            if (!string.IsNullOrWhiteSpace(message.PlayerId) && message.PlayerId == _gameSession.PlayerId)
                return;
            
            // Handle player join notifications
            if (message.PlayerJoin != null)
            {
                _introScreen.UpdatePlayerJoin(message.PlayerJoin.Initials);
                
                // Update game session - avoid LINQ Any()
                bool playerExists = false;
                foreach (var p in _gameSession.Players)
                {
                    if (p.PlayerId == message.PlayerJoin.PlayerId)
                    {
                        playerExists = true;
                        break;
                    }
                }
                if (!playerExists)
                {
                    _gameSession.Players.Add(new NetworkPlayerInfo
                    {
                        PlayerId = message.PlayerJoin.PlayerId,
                        Initials = message.PlayerJoin.Initials,
                        PlayerNumber = message.PlayerJoin.PlayerNumber
                    });
                }
                
                // Update player counts
                _gameSession.CurrentPlayers = message.PlayerJoin.CurrentPlayers;
                _gameSession.MaxPlayers = message.PlayerJoin.MaxPlayers;
                
                // If host, send game state to the newly joined player (whether game has started or not)
                // This ensures the new player gets hives, snipes, and player positions
                if (_gameSession.Role == GameSessionRole.Host)
                {
                    // Send game state snapshot to the new player so they can see hives and other players
                    // Use a small delay to ensure the player's stream is fully connected
                    _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(StreamConnectionDelayMs), () =>
                    {
                        // Always send game state snapshot when a player joins
                        // This includes hives (if game has started) and all player positions
                        PublishGameStateSnapshot();
                        
                        // Also send host's current position explicitly
                        if (_gameSession.Status == GameSessionStatus.Playing)
                        {
                            PublishPlayerPosition();
                        }
                        return false; // One-time
                    });
                }
                
                // Server starts game when full or 60s; host and clients all wait for GameStartMessage from server
            }
            // Handle player count updates
            else if (message.PlayerCount != null)
            {
                _introScreen.UpdatePlayerCount(
                    message.PlayerCount.CurrentPlayers, 
                    message.PlayerCount.MaxPlayers, 
                    message.PlayerCount.TimeRemaining
                );
                
                // Update game session with player counts
                if (_gameSession != null)
                {
                    _gameSession.CurrentPlayers = message.PlayerCount.CurrentPlayers;
                    _gameSession.MaxPlayers = message.PlayerCount.MaxPlayers;
                    
                    // Update player list from the message
                    if (message.PlayerCount.Players != null)
                    {
                        foreach (var playerInfo in message.PlayerCount.Players)
                        {
                            // Avoid LINQ Any() - iterate directly
                            bool playerExists = false;
                            foreach (var p in _gameSession.Players)
                            {
                                if (p.PlayerId == playerInfo.PlayerId)
                                {
                                    playerExists = true;
                                    break;
                                }
                            }
                            if (!playerExists)
                            {
                                _gameSession.Players.Add(new NetworkPlayerInfo
                                {
                                    PlayerId = playerInfo.PlayerId,
                                    Initials = playerInfo.Initials,
                                    PlayerNumber = playerInfo.PlayerNumber
                                });
                            }
                        }
                    }
                }
            }
            // Handle game start
            else if (message.GameStart != null)
            {
                _gameSession.Status = GameSessionStatus.Starting;
                _hasReceivedMultiplayerSnapshot = false; // Joiner must wait for first snapshot before drawing map

                // Initialize network players
                foreach (var playerId in message.GameStart.PlayerIds)
                {
                    // Avoid LINQ FirstOrDefault - iterate directly
                    NetworkPlayerInfo? playerInfo = null;
                    foreach (var p in _gameSession.Players)
                    {
                        if (p.PlayerId == playerId)
                        {
                            playerInfo = p;
                            break;
                        }
                    }
                    if (playerInfo != null)
                    {
                        var networkPlayer = new PlayerNetwork(
                            playerInfo.PlayerId,
                            playerInfo.Initials,
                            playerInfo.PlayerNumber,
                            isLocal: playerInfo.PlayerId == _gameSession.PlayerId
                        );
                        
                        _networkPlayers[playerInfo.PlayerId] = networkPlayer;
                        
                        if (networkPlayer.IsLocal)
                        {
                            _player.Initials = playerInfo.Initials;
                        }
                    }
                }
                
                _introScreen.StartGame();
                
                // For clients, publish initial position after game starts so host can see them
                if (_gameSession.Role == GameSessionRole.Client)
                {
                    _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(GameStatePublishDelayMs), () =>
                    {
                        PublishPlayerPosition();
                        return false; // One-time
                    });
                }
            }
            // Handle game state snapshot (clients receive from host)
            else if (message.State != null)
            {
                HandleGameStateSnapshot(message.State);
            }
            // Handle snipe updates (clients receive from host)
            else if (message.Snipes != null)
            {
                HandleSnipeUpdatesGrpc(message.Snipes);
            }
            // Handle hive updates (clients receive from host)
            else if (message.Hives != null)
            {
                HandleHiveUpdatesGrpc(message.Hives);
            }
            // Handle player position updates
            else if (message.Position != null)
            {
                // IMPORTANT: message.Position.X and Y are WORLD/MAP coordinates (not viewport)
                if (_networkPlayers.TryGetValue(message.PlayerId, out var networkPlayer))
                {
                    networkPlayer.UpdatePosition(message.Position.X, message.Position.Y, (int)message.Position.Sequence);
                }
                else
                {
                    // New player - try to find their info from game session first (avoid LINQ FirstOrDefault)
                    NetworkPlayerInfo? sessionPlayerInfo = null;
                    foreach (var p in _gameSession.Players)
                    {
                        if (p.PlayerId == message.PlayerId)
                        {
                            sessionPlayerInfo = p;
                            break;
                        }
                    }
                    var isLocalPlayer = message.PlayerId == _gameSession.PlayerId;
                    
                    // Create network player even if not in game session (position updates can arrive before game state)
                    var newNetworkPlayer = new PlayerNetwork(
                        message.PlayerId,
                        sessionPlayerInfo?.Initials ?? "??",  // Use "??" if not in session yet
                        sessionPlayerInfo?.PlayerNumber ?? 0,  // Use 0 if not in session yet
                        isLocal: isLocalPlayer
                    );
                    newNetworkPlayer.PreviousX = message.Position.X;
                    newNetworkPlayer.PreviousY = message.Position.Y;
                    newNetworkPlayer.UpdatePosition(message.Position.X, message.Position.Y, (int)message.Position.Sequence);
                    _networkPlayers[message.PlayerId] = newNetworkPlayer;
                    
                    // Also add to game session if not already there (for consistency)
                    if (sessionPlayerInfo == null && _gameSession != null)
                    {
                        _gameSession.Players.Add(new NetworkPlayerInfo
                        {
                            PlayerId = message.PlayerId,
                            Initials = "??",
                            PlayerNumber = 0
                        });
                    }
                    
                    if (newNetworkPlayer.IsLocal)
                    {
                        _player.X = message.Position.X;
                        _player.Y = message.Position.Y;
                        if (sessionPlayerInfo != null)
                        {
                            _player.Initials = sessionPlayerInfo.Initials;
                        }
                    }
                }
            }
            // Handle bullet updates
            else if (message.Bullet != null)
            {
                HandleBulletMessageGrpc(message.Bullet, message.PlayerId);
            }
            // Handle player respawn
            else if (message.Respawn != null)
            {
                if (_networkPlayers.TryGetValue(message.PlayerId, out var networkPlayer))
                {
                    networkPlayer.UpdatePosition(message.Respawn.X, message.Respawn.Y, 0);
                }
            }
            // Handle game over
            else if (message.GameOver != null)
            {
                // Game over is handled elsewhere, but we can process it here if needed
            }
        }
        catch (Exception)
        {
            // Errors in message handling are non-critical - gRPC client already logs connection errors
            // Silently handle to prevent game from crashing due to malformed messages
        }
    }
    
    private void HandleGameStateSnapshot(GameStateSnapshot snapshot)
    {
        if (snapshot == null)
            return;
        
        // Client receives game state snapshot from host
        try
        {
            lock (_gameStateLock)
            {
                // Update game state
                _gameState.Level = snapshot.Level;
                
                // Update hives from host
                _hives.Clear();
                // Calculate snipes per hive from level (needed for Hive constructor)
                int snipesPerHive = _gameState.GetSnipesPerHiveForLevel(snapshot.Level);
                if (snapshot.Hives != null)
                {
                    foreach (var hiveState in snapshot.Hives)
                    {
                        if (hiveState == null)
                            continue;
                        
                        var hive = new Hive(hiveState.X, hiveState.Y, snipesPerHive, _currentFrameTime)
                        {
                            Hits = hiveState.Hits,
                            IsDestroyed = hiveState.IsDestroyed,
                            SnipesRemaining = hiveState.SnipesRemaining,
                            FlashIntervalMs = hiveState.FlashIntervalMs
                        };
                        _hives.Add(hive);
                    }
                }
                _gameState.TotalHives = snapshot.Hives?.Count ?? 0;
                // Count undestroyed hives manually to avoid LINQ allocation
                int undestroyedCount = 0;
                if (snapshot.Hives != null)
                {
                    foreach (var h in snapshot.Hives)
                    {
                        if (h != null && !h.IsDestroyed)
                            undestroyedCount++;
                    }
                }
                _gameState.HivesUndestroyed = undestroyedCount;
                
                // Update snipes from host
                // IMPORTANT: snipeState.X and snipeState.Y are WORLD/MAP coordinates (0 to MapWidth/MapHeight)
                _snipes.Clear();
                if (snapshot.Snipes != null)
                {
                    foreach (var snipeState in snapshot.Snipes)
                    {
                        if (snipeState == null)
                            continue;
                        
                        if (snipeState.IsAlive)
                        {
                            // Convert string type from gRPC to enum
                            SnipeType snipeType = ParseSnipeType(snipeState.Type);
                            var snipe = new Snipe(snipeState.X, snipeState.Y, snipeType)  // World coordinates
                            {
                                DirectionX = snipeState.DirectionX,
                                DirectionY = snipeState.DirectionY,
                                IsAlive = snipeState.IsAlive
                            };
                            _snipes.Add(snipe);
                        }
                    }
                }
                
                // Count alive snipes manually to avoid LINQ allocation
                int aliveCount = 0;
                if (snapshot.Snipes != null)
                {
                    foreach (var s in snapshot.Snipes)
                    {
                        if (s != null && s.IsAlive)
                            aliveCount++;
                    }
                }
                _gameState.SnipesUndestroyed = aliveCount;

                // Update bullets from server state (server-authoritative)
                _bullets.Clear();
                if (snapshot.Bullets != null)
                {
                    foreach (var b in snapshot.Bullets)
                    {
                        if (b == null) continue;
                        _bullets.Add(new Bullet(b.X, b.Y, b.VelocityX, b.VelocityY, b.BulletId, b.PlayerId, _currentFrameTime));
                    }
                }

                // Update player states
                // IMPORTANT: playerState.X and playerState.Y are WORLD/MAP coordinates (0 to MapWidth/MapHeight)
                if (snapshot.Players != null)
                {
                    foreach (var playerState in snapshot.Players)
                    {
                        if (playerState == null)
                            continue;
                        
                        if (_networkPlayers.TryGetValue(playerState.PlayerId, out var networkPlayer))
                        {
                            // Store world coordinates - conversion to viewport happens when drawing
                            // Update previous position to avoid artifacts
                            networkPlayer.PreviousX = networkPlayer.X;
                            networkPlayer.PreviousY = networkPlayer.Y;
                            networkPlayer.X = playerState.X;  // World coordinate
                            networkPlayer.Y = playerState.Y;  // World coordinate
                            networkPlayer.Lives = playerState.Lives;
                            networkPlayer.Score = playerState.Score;
                            networkPlayer.IsAlive = playerState.IsAlive;
                            // Update initials from game state (in case they were "??" before)
                            if (!string.IsNullOrEmpty(playerState.Initials))
                            {
                                networkPlayer.Initials = playerState.Initials;
                            }
                            
                        if (networkPlayer.IsLocal)
                        {
                            _player.X = playerState.X;  // World coordinate
                            _player.Y = playerState.Y;  // World coordinate
                            _player.Lives = playerState.Lives;
                            _player.Score = playerState.Score;
                            _player.IsAlive = playerState.IsAlive;
                            // Update local player initials too
                            if (!string.IsNullOrEmpty(playerState.Initials))
                            {
                                _player.Initials = playerState.Initials;
                            }
                            _hasReceivedMultiplayerSnapshot = true; // Safe to draw map with server position
                            _cachedMapViewport = null; // Force map redraw
                        }
                        }
                        else
                        {
                            // New player not in our network players list - create them (avoid LINQ FirstOrDefault)
                            NetworkPlayerInfo? playerInfo = null;
                            if (_gameSession != null)
                            {
                                foreach (var p in _gameSession.Players)
                                {
                                    if (p.PlayerId == playerState.PlayerId)
                                    {
                                        playerInfo = p;
                                        break;
                                    }
                                }
                            }
                            var isLocalPlayer = playerState.PlayerId == _gameSession?.PlayerId;
                            
                            // Create network player even if not in game session (shouldn't happen, but be safe)
                            var newNetworkPlayer = new PlayerNetwork(
                                playerState.PlayerId,
                                !string.IsNullOrEmpty(playerState.Initials) ? playerState.Initials : (playerInfo?.Initials ?? "??"),
                                playerInfo?.PlayerNumber ?? 0,
                                isLocal: isLocalPlayer
                            );
                            newNetworkPlayer.PreviousX = playerState.X;
                            newNetworkPlayer.PreviousY = playerState.Y;
                            newNetworkPlayer.X = playerState.X;
                            newNetworkPlayer.Y = playerState.Y;
                            newNetworkPlayer.Lives = playerState.Lives;
                            newNetworkPlayer.Score = playerState.Score;
                            newNetworkPlayer.IsAlive = playerState.IsAlive;
                            _networkPlayers[playerState.PlayerId] = newNetworkPlayer;
                            
                            // Also add to game session if not already there
                            if (playerInfo == null && _gameSession != null)
                            {
                                _gameSession.Players.Add(new NetworkPlayerInfo
                                {
                                    PlayerId = playerState.PlayerId,
                                    Initials = !string.IsNullOrEmpty(playerState.Initials) ? playerState.Initials : "??",
                                    PlayerNumber = 0
                                });
                            }
                            
                            if (newNetworkPlayer.IsLocal)
                            {
                                _player.X = playerState.X;
                                _player.Y = playerState.Y;
                                _player.Lives = playerState.Lives;
                                _player.Score = playerState.Score;
                                _player.IsAlive = playerState.IsAlive;
                                if (!string.IsNullOrEmpty(playerState.Initials))
                                {
                                    _player.Initials = playerState.Initials;
                                }
                                _hasReceivedMultiplayerSnapshot = true;
                                _cachedMapViewport = null;
                            }
                        }
                    }
                }
            
                // Force map redraw to show hives and players
                _cachedMapViewport = null;
                _mapDrawn = false;
            }
        }
        catch (Exception)
        {
            // Errors in state snapshot handling are non-critical - game can continue with partial state
            // Silently handle to prevent game from crashing due to malformed snapshots
        }
    }
    
    private void HandleSnipeUpdatesGrpc(SnipeUpdates updates)
    {
        if (updates == null)
            return;
        
        if (updates.Updates == null)
            return;
        
        // Convert gRPC snipe updates to internal format
        foreach (var update in updates.Updates)
        {
            if (update == null)
                continue;
            
            if (string.IsNullOrWhiteSpace(update.Action))
                continue;
            
            if (update.Action == "spawned")
            {
                // Create new snipe
                // Convert string type from gRPC to enum
                SnipeType snipeType = ParseSnipeType(update.Type);
                var snipe = new Snipe(update.X, update.Y, snipeType, update.DirectionX, update.DirectionY);
                snipe.SnipeId = update.SnipeId;
                _snipes.Add(snipe);
            }
            else if (update.Action == "moved")
            {
                // Avoid LINQ FirstOrDefault - iterate directly
                Snipe? snipe = null;
                foreach (var s in _snipes)
                {
                    if (s.SnipeId == update.SnipeId)
                    {
                        snipe = s;
                        break;
                    }
                }
                if (snipe != null)
                {
                    snipe.X = update.X;
                    snipe.Y = update.Y;
                    snipe.DirectionX = update.DirectionX;
                    snipe.DirectionY = update.DirectionY;
                }
            }
            else if (update.Action == "died")
            {
                // Remove snipe by finding index first (more efficient than RemoveAll)
                int snipeIndexToRemove = -1;
                for (int idx = 0; idx < _snipes.Count; idx++)
                {
                    if (_snipes[idx].SnipeId == update.SnipeId)
                    {
                        snipeIndexToRemove = idx;
                        break;
                    }
                }
                if (snipeIndexToRemove >= 0)
                {
                    _snipes.RemoveAt(snipeIndexToRemove);
                    _gameState.SnipesUndestroyed--;
                }
            }
        }
    }
    
    private void HandleHiveUpdatesGrpc(HiveUpdates updates)
    {
        if (updates == null)
            return;
        
        if (updates.Updates == null)
            return;
        
        // Convert gRPC hive updates to internal format
        foreach (var update in updates.Updates)
        {
            if (update == null)
                continue;
            
            if (string.IsNullOrWhiteSpace(update.Action))
                continue;
            
            if (update.Action == "spawned")
            {
                // Hives are spawned at game start, so this might not be needed
            }
            else if (update.Action == "hit")
            {
                // Avoid LINQ FirstOrDefault - iterate directly
                Hive? hive = null;
                foreach (var h in _hives)
                {
                    if ($"hive_{h.X}_{h.Y}" == update.HiveId)
                    {
                        hive = h;
                        break;
                    }
                }
                if (hive != null)
                {
                    hive.Hits = update.Hits;
                    hive.FlashIntervalMs = update.FlashIntervalMs;
                }
            }
            else if (update.Action == "destroyed")
            {
                // Remove hive by finding index first (more efficient than RemoveAll)
                int hiveIndexToRemove = -1;
                for (int idx = 0; idx < _hives.Count; idx++)
                {
                    if ($"hive_{_hives[idx].X}_{_hives[idx].Y}" == update.HiveId)
                    {
                        hiveIndexToRemove = idx;
                        break;
                    }
                }
                if (hiveIndexToRemove >= 0)
                {
                    _hives.RemoveAt(hiveIndexToRemove);
                    _gameState.HivesUndestroyed--;
                }
            }
        }
    }
    
    private void HandleBulletMessageGrpc(BulletUpdate bulletMsg, string playerId)
    {
        if (bulletMsg == null)
            return;
        
        if (string.IsNullOrWhiteSpace(bulletMsg.Action))
            return;
        
        if (bulletMsg.Action == "fired")
        {
            // Add remote bullet to our list
            var bullet = new Bullet(bulletMsg.X, bulletMsg.Y, bulletMsg.VelocityX, bulletMsg.VelocityY, bulletMsg.BulletId, playerId, _currentFrameTime);
            _bullets.Add(bullet);
        }
        else if (bulletMsg.Action == "updated")
        {
            // Update existing bullet - avoid LINQ FirstOrDefault
            Bullet? bullet = null;
            foreach (var b in _bullets)
            {
                if (b.BulletId == bulletMsg.BulletId)
                {
                    bullet = b;
                    break;
                }
            }
            if (bullet != null)
            {
                bullet.X = bulletMsg.X;
                bullet.Y = bulletMsg.Y;
                bullet.VelocityX = bulletMsg.VelocityX;
                bullet.VelocityY = bulletMsg.VelocityY;
            }
        }
        else if (bulletMsg.Action == "expired" || bulletMsg.Action == "hit")
        {
            // Find and clear the bullet before removing it - capture index for efficient removal
            Bullet? bullet = null;
            int bulletIndexToRemove = -1;
            for (int idx = 0; idx < _bullets.Count; idx++)
            {
                if (_bullets[idx].BulletId == bulletMsg.BulletId)
                {
                    bullet = _bullets[idx];
                    bulletIndexToRemove = idx;
                    break;
                }
            }
            if (bullet != null)
            {
                // Get viewport information to clear the bullet
                int currentWidth = Frame.Width;
                int currentHeight = Frame.Height;
                int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
                int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
                
                int mapOffsetX = _player.X - (frameWidth / 2);
                int mapOffsetY = _player.Y - (frameHeight / 2);
                
                // Get fresh map for clearing
                var freshMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
                
                // Clear bullet at current position
                int bulletWorldX = (int)Math.Round(bullet.X);
                int bulletWorldY = (int)Math.Round(bullet.Y);
                bulletWorldX = _map.WrapX(bulletWorldX);
                bulletWorldY = _map.WrapY(bulletWorldY);
                
                int viewportX = bulletWorldX - mapOffsetX;
                int viewportY = bulletWorldY - mapOffsetY;
                if (viewportX >= 0 && viewportX < frameWidth &&
                    viewportY >= 0 && viewportY < frameHeight &&
                    freshMap != null && viewportY >= 0 && viewportY < freshMap.Length &&
                    viewportX >= 0 && viewportX < freshMap[viewportY].Length)
                {
                    SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
                    Move(viewportX, viewportY + StatusBarHeight);
                    AddRune(freshMap[viewportY][viewportX]);
                    SetAttribute(new DrawingAttribute(Color.White, Color.Black));
                }
                
                // Also clear bullet's previous position if different
                    int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
                    int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
                    prevBulletWorldX = _map.WrapX(prevBulletWorldX);
                    prevBulletWorldY = _map.WrapY(prevBulletWorldY);

                    if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
                {
                    int prevViewportX = prevBulletWorldX - mapOffsetX;
                    int prevViewportY = prevBulletWorldY - mapOffsetY;
                    if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                        prevViewportY >= 0 && prevViewportY < frameHeight &&
                        freshMap != null && prevViewportY >= 0 && prevViewportY < freshMap.Length &&
                        prevViewportX >= 0 && prevViewportX < freshMap[prevViewportY].Length)
                    {
                        SetAttribute(new DrawingAttribute(Color.Blue, Color.Black));
                        Move(prevViewportX, prevViewportY + StatusBarHeight);
                        AddRune(freshMap[prevViewportY][prevViewportX]);
                        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
                    }
                }
                
                // Invalidate cached map
                _cachedMapViewport = null;
            }
            
            // Remove bullet by index (more efficient than RemoveAll)
            if (bulletIndexToRemove >= 0)
            {
                _bullets.RemoveAt(bulletIndexToRemove);
            }
        }
    }
    
    // Legacy methods removed - using gRPC HandleBulletMessageGrpc now
    
    /// <summary>
    /// Creates a base GameMessage with common fields (GameId, PlayerId, Timestamp)
    /// Returns null if game session or gRPC client is not available (caller should handle null)
    /// </summary>
    private GameMessage? CreateBaseGameMessage(string? playerId = null)
    {
        if (_gameSession == null)
            return null;
        
        if (_grpcClient == null)
            return null;
        
        if (string.IsNullOrWhiteSpace(_gameSession.GameId))
            return null;
        
        if (string.IsNullOrWhiteSpace(_gameSession.PlayerId) && string.IsNullOrWhiteSpace(playerId))
            return null;
        
        return new GameMessage
        {
            GameId = _gameSession.GameId,
            PlayerId = playerId ?? _gameSession.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
    
    /// <summary>
    /// Sends a GameMessage using fire-and-forget pattern
    /// Silently handles errors (fire-and-forget pattern for non-critical messages)
    /// </summary>
    private void SendGameMessage(GameMessage? gameMessage)
    {
        if (gameMessage == null)
            return;
        
        if (_grpcClient == null)
            return;
        
        // Fire-and-forget: don't await, errors are handled by GrpcGameClient
        _ = _grpcClient.SendGameMessageAsync(gameMessage);
    }
    
    private void PublishPlayerPosition()
    {
        if (_gameSession == null || _grpcClient == null || !_isMultiplayer)
            return;
        
        // Throttle position updates to avoid flooding, but allow more frequent updates
        // Reduce throttling to 20ms for smoother movement
        // Note: The periodic timer will ensure position is sent even if player isn't moving
        if ((_currentFrameTime - _lastPositionPublish).TotalMilliseconds < PositionPublishThrottleMs)
            return;
        
        _positionSequence++;
        // IMPORTANT: All coordinates in gRPC messages must be WORLD/MAP coordinates, not viewport coordinates
        // _player.X and _player.Y are world coordinates (0 to MapWidth/MapHeight)
        var gameMessage = CreateBaseGameMessage();
        if (gameMessage == null)
            return;
        
        gameMessage.Position = new PlayerPositionUpdate
        {
            X = _player.X,  // World coordinate (map space)
            Y = _player.Y,  // World coordinate (map space)
            Sequence = _positionSequence
        };
        
        // Use fire-and-forget for position updates
        SendGameMessage(gameMessage);
        _lastPositionPublish = _currentFrameTime;
    }
    
    private void PublishBulletUpdate(Bullet bullet, string action, string? hitType = null, string? hitTargetId = null)
    {
        if (bullet == null)
            return;
        
        if (string.IsNullOrWhiteSpace(action))
            return;
        
        if (!_isMultiplayer)
            return;
        
        if (_gameSession == null)
            return;
        
        if (_grpcClient == null)
            return;
        
        var gameMessage = CreateBaseGameMessage(bullet.PlayerId);
        if (gameMessage == null)
            return;
        
        gameMessage.Bullet = new BulletUpdate
        {
            BulletId = bullet.BulletId,
            X = bullet.X,
            Y = bullet.Y,
            VelocityX = bullet.VelocityX,
            VelocityY = bullet.VelocityY,
            Action = action,
            HitType = hitType ?? "",
            HitTargetId = hitTargetId ?? ""
        };
        
        SendGameMessage(gameMessage);
    }
    
    private void PublishPlayerCountUpdate()
    {
        if (!_isMultiplayer)
            return;
        
        if (_gameSession == null)
            return;
        
        if (_gameSession.Role != GameSessionRole.Host)
            return;
        
        var gameMessage = CreateBaseGameMessage();
        if (gameMessage == null)
            return;
        
        int elapsed = (int)(_currentFrameTime - _gameSession.CreatedAt).TotalSeconds;
        int timeRemaining = Math.Max(0, MultiplayerWaitTimeSeconds - elapsed);
        
        gameMessage.PlayerCount = new PlayerCountUpdate
        {
            CurrentPlayers = _gameSession.CurrentPlayers,
            MaxPlayers = _gameSession.MaxPlayers,
            TimeRemaining = timeRemaining
        };
        foreach (var p in _gameSession.Players)
        {
            gameMessage.PlayerCount.Players.Add(new PlayerInfo
            {
                PlayerId = p.PlayerId,
                Initials = p.Initials,
                PlayerNumber = p.PlayerNumber
            });
        }
        SendGameMessage(gameMessage);
    }
    
    private async void StartMultiplayerGameSession()
    {
        if (!_isMultiplayer)
            return;
        
        if (_gameSession == null)
            return;
        
        if (_gameSession.Role != GameSessionRole.Host)
            return;
        
        _gameSession.Status = GameSessionStatus.Starting;
        _gameSession.StartTime = _currentFrameTime;
        
        // Initialize network players from game session
        foreach (var playerInfo in _gameSession.Players)
        {
            var networkPlayer = new PlayerNetwork(
                playerInfo.PlayerId,
                playerInfo.Initials,
                playerInfo.PlayerNumber,
                isLocal: playerInfo.PlayerId == _gameSession.PlayerId
            );
            
            // Spawn players at valid positions (no overlap)
            var (x, y) = FindRandomValidPositionForMultiplayer();
            networkPlayer.X = x;
            networkPlayer.Y = y;
            
            if (networkPlayer.IsLocal)
            {
                _player.X = x;
                _player.Y = y;
            }
            
            _networkPlayers[playerInfo.PlayerId] = networkPlayer;
        }
        
        // Publish game start message (gRPC doesn't use subscriptions - messages come through the stream)
        if (_grpcClient == null)
            return;
        
        var gameMessage = CreateBaseGameMessage();
        if (gameMessage == null)
            return;
        
        gameMessage.GameStart = new GameStartMessage
        {
            Level = _gameState.Level
        };
        // Avoid LINQ Select allocation - iterate directly
        gameMessage.GameStart.PlayerIds.Capacity = _gameSession.Players.Count;
        foreach (var player in _gameSession.Players)
        {
            gameMessage.GameStart.PlayerIds.Add(player.PlayerId);
        }
        await _grpcClient.SendGameMessageAsync(gameMessage);
        
        // Start the game
        _gameSession.Status = GameSessionStatus.Playing;
        
        // Reset game state (this will preserve network player positions in multiplayer)
        ResetGame();
        
        // Ensure network player positions match local player position (for host)
        // This is important because ResetGame() might have updated _player position
        if (_gameSession.Role == GameSessionRole.Host)
        {
            if (_networkPlayers.TryGetValue(_gameSession.PlayerId, out var localNetworkPlayer))
            {
                // Update network player to match local player (in case ResetGame changed it)
                localNetworkPlayer.X = _player.X;
                localNetworkPlayer.Y = _player.Y;
                localNetworkPlayer.PreviousX = _player.X;
                localNetworkPlayer.PreviousY = _player.Y;
            }
        }
        
        // Publish game state snapshot (hives, initial positions) for clients
        if (_gameSession.Role == GameSessionRole.Host)
        {
            // Small delay to ensure all players' streams are active, then publish game state
            _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(GameStatePublishDelayMs), () =>
            {
                PublishGameStateSnapshot();
                PublishPlayerPosition(); // Also publish host's initial position
                return false; // One-time
            });
        }
        
        _introScreen.StartGame();
    }
    
    private (int x, int y) FindRandomValidPositionForMultiplayer()
    {
        const int MAX_ATTEMPTS = 1000;
        
        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            int x = Random.Shared.Next(0, _map.MapWidth - 1);
            int y = Random.Shared.Next(0, _map.MapHeight - 2);
            
            // Check if position is valid (not on walls, not on hives, not on other players)
            if (IsPositionValid(x, y) && !IsPositionOverlappingPlayers(x, y))
            {
                return (x, y);
            }
        }
        
        // Fallback: systematic search
        for (int y = 0; y < _map.MapHeight - 2; y++)
        {
            for (int x = 0; x < _map.MapWidth - 1; x++)
            {
                if (IsPositionValid(x, y) && !IsPositionOverlappingPlayers(x, y))
                {
                    return (x, y);
                }
            }
        }
        
        // Last resort
        return (1, 1);
    }
    
    private bool IsPositionOverlappingPlayers(int x, int y)
    {
            // Check if position overlaps with any existing network player
            foreach (var networkPlayer in _networkPlayers.Values)
            {
                // Player occupies: [X, X+PlayerWidth-1] columns, [Y, Y+PlayerHeight-1] rows
                if (!(x + PlayerWidth <= networkPlayer.X || x >= networkPlayer.X + PlayerWidth ||
                      y + PlayerHeight <= networkPlayer.Y || y >= networkPlayer.Y + PlayerHeight))
            {
                return true; // Overlaps
            }
        }
        return false;
    }
    
    private void PublishBulletFired(Bullet bullet)
    {
        if (_gameSession == null || _grpcClient == null)
            return;
        
        // Use the standard bullet update method
        PublishBulletUpdate(bullet, "fired");
    }
    
    // Legacy methods removed - using gRPC HandleSnipeUpdatesGrpc and HandleHiveUpdatesGrpc now
    
    // Legacy method removed - using HandleSnipeUpdatesGrpc now
    
    // Legacy method removed - using HandleHiveUpdatesGrpc now
    
    private void PublishGameStateSnapshot()
    {
        if (_gameSession == null)
            return;
        
        if (_gameSession.Role != GameSessionRole.Host)
            return;
        
        // IMPORTANT: All coordinates in gRPC messages must be WORLD/MAP coordinates, not viewport coordinates
        // All X/Y values here are world coordinates (0 to MapWidth/MapHeight)
        // Each client will convert these to viewport coordinates based on their own viewport dimensions
        var gameMessage = CreateBaseGameMessage();
        if (gameMessage == null)
            return;
        
        gameMessage.State = new GameStateSnapshot
        {
            Level = _gameState.Level,
            Status = "playing",
            Sequence = 0
        };
        // Add players - include all players from game session, not just network players
        // This ensures newly joined players are included even if they're not in _networkPlayers yet
        foreach (var sessionPlayer in _gameSession.Players)
        {
            // Try to get network player data if available
            if (_networkPlayers.TryGetValue(sessionPlayer.PlayerId, out var np))
            {
                gameMessage.State.Players.Add(new PlayerStateInfo
                {
                    PlayerId = np.PlayerId,
                    Initials = np.Initials,
                    X = np.X,  // World coordinate (map space)
                    Y = np.Y,  // World coordinate (map space)
                    Lives = np.Lives,
                    Score = np.Score,
                    IsAlive = np.IsAlive
                });
            }
            else if (sessionPlayer.PlayerId == _gameSession.PlayerId)
            {
                // Host player - use local player data (host might not be in _networkPlayers yet)
                gameMessage.State.Players.Add(new PlayerStateInfo
                {
                    PlayerId = _gameSession.PlayerId,
                    Initials = _player.Initials,
                    X = _player.X,  // World coordinate (map space)
                    Y = _player.Y,  // World coordinate (map space)
                    Lives = _player.Lives,
                    Score = _player.Score,
                    IsAlive = _player.IsAlive
                });
            }
            else
            {
                // New player not yet in network players - use default values
                gameMessage.State.Players.Add(new PlayerStateInfo
                {
                    PlayerId = sessionPlayer.PlayerId,
                    Initials = sessionPlayer.Initials,
                    X = 0,  // Will be updated when they send position
                    Y = 0,
                    Lives = Player.InitialLives,
                    Score = Player.InitialScore,
                    IsAlive = true
                });
            }
        }
        // Add hives
        foreach (var h in _hives)
        {
            gameMessage.State.Hives.Add(new HiveStateInfo
            {
                HiveId = $"hive_{h.X}_{h.Y}",
                X = h.X,  // World coordinate (map space)
                Y = h.Y,  // World coordinate (map space)
                Hits = h.Hits,
                IsDestroyed = h.IsDestroyed,
                SnipesRemaining = h.SnipesRemaining,
                FlashIntervalMs = h.FlashIntervalMs
            });
        }
        // Add snipes
        foreach (var s in _snipes)
        {
            gameMessage.State.Snipes.Add(new SnipeStateInfo
            {
                SnipeId = s.SnipeId,
                X = s.X,  // World coordinate (map space)
                Y = s.Y,  // World coordinate (map space)
                Type = s.Type == SnipeType.TypeA ? "A" : "B",
                DirectionX = s.DirectionX,
                DirectionY = s.DirectionY,
                IsAlive = s.IsAlive
            });
        }
        SendGameMessage(gameMessage);
    }

}






