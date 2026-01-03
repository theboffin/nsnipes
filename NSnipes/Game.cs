using Terminal.Gui;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NSnipes;

public class Game : Window
{
    private readonly Map _map;
    private readonly Player _player;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private bool _mapDrawn = false;
    private readonly List<Bullet> _bullets = new List<Bullet>();
    private readonly List<Hive> _hives = new List<Hive>();
    private readonly List<Snipe> _snipes = new List<Snipe>();
    private readonly GameState _gameState = new GameState();
    private const int MaxBullets = 10;
    private const double BulletSpeed = 1.0; // Bullets move 1.0 cell per update (10ms) to ensure proper wall collision
    private const int StatusBarHeight = 2; // First 2 rows reserved for status information
    private Random _random = new Random();

    // Performance optimization: Track previous positions to avoid unnecessary redraws
    private int _previousPlayerCellX = -1;
    private int _previousPlayerCellY = -1;
    private int _previousPlayerViewportX = -1;
    private int _previousPlayerViewportY = -1;
    private string[]? _cachedMapViewport = null;
    private DateTime _cachedDateTime = DateTime.MinValue;
    private int _cachedHivesUndestroyed = -1;
    private int _cachedSnipesUndestroyed = -1;
    private int _cachedLives = -1;
    private int _cachedLevel = -1;
    private int _cachedScore = -1;
    
    // Frame rate tracking
    private DateTime _lastFrameTime = DateTime.Now;
    private double _currentFPS = 0.0;
    private readonly Queue<double> _fpsHistory = new Queue<double>();
    private const int FpsHistorySize = 10; // Average over last 10 frames
    private int _cachedFPS = -1;

    // Intro screen
    private IntroScreen _introScreen;
    private GameConfig _config;
    
    // Multiplayer
    private MqttGameClient? _mqttClient;
    private GameSession? _gameSession;
    private Dictionary<string, PlayerNetwork> _networkPlayers = new Dictionary<string, PlayerNetwork>();
    private bool _isMultiplayer = false;
    private int _positionSequence = 0; // Sequence number for position updates
    private DateTime _lastPositionPublish = DateTime.Now;
    private const int PositionPublishIntervalMs = 20; // Publish position every 20ms when moved for smoother updates

    // Key state tracking for smooth movement
    private Dictionary<KeyCode, DateTime> _pressedKeys = new Dictionary<KeyCode, DateTime>();
    private const int KeyRepeatThresholdMs = 150; // Consider key released if not seen in 150ms

    public Game()
    {
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
            ResetGame(); // Reset all game state for a new game
        };
        _introScreen.OnRespawnComplete += () =>
        {
            // When respawn clearing effect completes, ensure map and status bar are redrawn
            _mapDrawn = false; // Force redraw of map (which will also redraw status bar)
        };
        _introScreen.OnExit += () =>
        {
            Application.RequestStop();
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
            if (_mqttClient != null)
            {
                _mqttClient.Dispose();
                _mqttClient = null;
            }
            _gameSession = null;
            _isMultiplayer = false;
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
        _introScreen.Show();

        Title = "NSnipes";

        // Make window fill entire screen
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // Prevent default Escape key behavior (we handle it ourselves)
        Modal = false;
        CanFocus = true;

        // Remove border if possible
        if (Border != null)
        {
            Border.BorderStyle = LineStyle.None;
        }

        ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Disabled = new Terminal.Gui.Attribute(Color.Blue, Color.Black),
        };

        // Handle Escape at Application level FIRST to prevent default close behavior
        // We need to handle this before Terminal.Gui's default Escape handler closes the window
        Application.KeyDown += (sender, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                // Handle Escape before any default behavior
                if (_introScreen.IsActive)
                {
                    // Exit application from intro screen
                    Application.RequestStop();
                }
                else
                {
                    // Return to intro screen from game
                    _introScreen.Show();
                    if (Application.Driver != null)
                    {
                        _introScreen.Show();
                    }
                }
                // The event is handled, but Terminal.Gui might still process it
                // We need to prevent the Window from closing
                return;
            }
            // For other keys, handle them inline
            HandleKeyDown(sender, e);
        };

        // Also handle at Window level as backup
        KeyDown += HandleWindowKeyDown;

        Application.SizeChanging += (s, e) =>
        {
            if (!_introScreen.IsActive)
            {
                DrawMapAndPlayer();
            }
        };

        // Timer for intro screen animation and clearing effects (16ms for ~60fps)
        Application.AddTimeout(TimeSpan.FromMilliseconds(16), () =>
        {
            if (Application.Driver != null)
            {
                if (_introScreen.IsActive || _introScreen.IsClearingScreen || _introScreen.IsWaitingForGameOverKey)
                {
                    _introScreen.Draw();
                }
            }
            return true;
        });

        // Timer for player animation, movement, and initial map draw (40ms for more responsive movement)
        Application.AddTimeout(TimeSpan.FromMilliseconds(40), () =>
        {
            if (!_introScreen.IsActive && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey && !_mapDrawn)
            {
                DrawMapAndPlayer();
                _mapDrawn = true;
            }
            else if (!_introScreen.IsActive && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                // Process continuous movement based on pressed keys
                bool playerMoved = ProcessPlayerMovement();
                if (playerMoved)
                {
                    // Player moved - redraw map and player
                    DrawMapAndPlayer();
                }
                else
                {
                    // Player didn't move - just redraw player animation
                    DrawPlayer();
                }
            }
            return true;
        });

        // Separate timer for bullet updates (10ms for smooth movement)
        Application.AddTimeout(TimeSpan.FromMilliseconds(10), () =>
        {
            if (_mapDrawn && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey && !_introScreen.IsActive)
            {
                UpdateBullets();
                DrawFrame();
            }
            return true;
        });

        // Separate timer for hive animation (75ms for slower color change and better performance)
        Application.AddTimeout(TimeSpan.FromMilliseconds(75), () =>
        {
            if (_mapDrawn && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                DrawHives();
                DrawStatusBar(); // Update status bar periodically
            }
            return true;
        });

        // Timer for snipe spawning and movement (200ms) - only host runs this
        Application.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
        {
            if (_mapDrawn && !_introScreen.IsClearingScreen && !_introScreen.IsGameOver && !_introScreen.IsWaitingForGameOverKey)
            {
                // Only host spawns and updates snipes - clients receive updates via MQTT
                if (!_isMultiplayer || (_gameSession != null && _gameSession.Role == GameSessionRole.Host))
                {
                    SpawnSnipes();
                    UpdateSnipes();
                    PublishSnipeUpdates(); // Publish snipe state to clients
                }
                DrawSnipes(); // All players draw snipes
            }
            return true;
        });
    }

    private void HandleWindowKeyDown(object? sender, dynamic e)
    {
        // Handle Escape key at Window level to prevent default close behavior
        if (e.KeyCode == KeyCode.Esc)
        {
            if (_introScreen.IsActive)
            {
                // Exit application from intro screen
                Application.RequestStop();
            }
            else
            {
                // Return to intro screen from game
                _introScreen.Show();
            }
            // Don't process further - this prevents Window's default Escape handling
            return;
        }

        // For other keys, let them process normally
    }

    private void HandleKeyDown(object? sender, dynamic e)
    {
        // Escape is handled at Application level, so skip it here
        if (e.KeyCode == KeyCode.Esc)
        {
            return;
        }

        // Handle intro screen key press (including game over)
        if (_introScreen.HandleKey(e))
        {
            return; // Intro screen handled the key
        }

        // Don't process game keys if intro screen is active or game is over
        if (_introScreen.IsActive || _introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
        {
            return;
        }

        if (Application.Driver == null)
            return;

        // Track movement keys for continuous movement
        // Update key state when movement keys are pressed
        switch (e.KeyCode)
        {
            case KeyCode.D8: // Numeric keypad 8 (Up)
            case KeyCode.CursorUp:
            case KeyCode.D2: // Numeric keypad 2 (Down)
            case KeyCode.CursorDown:
            case KeyCode.D4: // Numeric keypad 4 (Left)
            case KeyCode.CursorLeft:
            case KeyCode.D6: // Numeric keypad 6 (Right)
            case KeyCode.CursorRight:
            case KeyCode.D7: // Numeric keypad 7 (Up-Left diagonal)
            case KeyCode.D9: // Numeric keypad 9 (Up-Right diagonal)
            case KeyCode.D1: // Numeric keypad 1 (Down-Left diagonal)
            case KeyCode.D3: // Numeric keypad 3 (Down-Right diagonal)
                // Update key state - mark this key as currently pressed
                _pressedKeys[e.KeyCode] = DateTime.Now;
                break;
        }

        // Movement will be handled by ProcessPlayerMovement() in the timer
        bool moved = false;

        // Handle bullet firing (q, w, e, a, d, z, x, c)
        // Player is 2 columns wide [X, X+1] and 3 rows tall [Y, Y+1, Y+2]
        if (_bullets.Count < MaxBullets)
        {
            double startX = 0;
            double startY = 0;
            double velX = 0;
            double velY = 0;

            switch (e.KeyCode)
            {
                case KeyCode.Q: // Diagonal left/up - fire from top-left corner
                    startX = _player.X;
                    startY = _player.Y;
                    velX = -BulletSpeed;
                    velY = -BulletSpeed;
                    break;
                case KeyCode.W: // Up - fire from top center
                    startX = _player.X + 0.5;
                    startY = _player.Y;
                    velY = -BulletSpeed;
                    break;
                case KeyCode.E: // Diagonal right/up - fire from top-right corner
                    startX = _player.X + 1.0;
                    startY = _player.Y;
                    velX = BulletSpeed;
                    velY = -BulletSpeed;
                    break;
                case KeyCode.A: // Left - fire from left center
                    startX = _player.X;
                    startY = _player.Y + 1.0;
                    velX = -BulletSpeed;
                    break;
                case KeyCode.D: // Right - fire from right center
                    startX = _player.X + 1.0;
                    startY = _player.Y + 1.0;
                    velX = BulletSpeed;
                    break;
                case KeyCode.Z: // Diagonal left/down - fire from bottom-left corner
                    startX = _player.X;
                    startY = _player.Y + 2.0;
                    velX = -BulletSpeed;
                    velY = BulletSpeed;
                    break;
                case KeyCode.X: // Down - fire from bottom center
                    startX = _player.X + 0.5;
                    startY = _player.Y + 2.0;
                    velY = BulletSpeed;
                    break;
                case KeyCode.C: // Diagonal right/down - fire from bottom-right corner
                    startX = _player.X + 1.0;
                    startY = _player.Y + 2.0;
                    velX = BulletSpeed;
                    velY = BulletSpeed;
                    break;
            }

            if (velX != 0 || velY != 0)
            {
                string? playerId = _gameSession?.PlayerId;
                var bullet = new Bullet(startX, startY, velX, velY, playerId: playerId);
                _bullets.Add(bullet);
                
                // Publish bullet fired in multiplayer
                if (_isMultiplayer && _gameSession != null && _mqttClient != null)
                {
                    PublishBulletFired(bullet);
                }
                
                // Redraw to show the new bullet
                if (_mapDrawn)
                {
                    DrawFrame();
                }
            }
        }

        // Only redraw if movement occurred
        if (moved)
        {
            // Performance optimization: Only redraw map if player moved to a different cell
            int currentCellX = _player.X;
            int currentCellY = _player.Y;

            if (_previousPlayerCellX != currentCellX || _previousPlayerCellY != currentCellY || !_mapDrawn)
            {
                // Player moved to a different cell - invalidate cache and redraw entire map
                _cachedMapViewport = null;
                DrawMapAndPlayer();
                _previousPlayerCellX = currentCellX;
                _previousPlayerCellY = currentCellY;
            }
            else
            {
                // Player is still in same cell - just update player and dynamic elements
                DrawPlayerWithClearing();
                DrawBullets();
            }
        }
    }

    private bool ProcessPlayerMovement()
    {
        if (Application.Driver == null || _introScreen.IsClearingScreen || _introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
            return false;

        // Clean up old key presses (keys not seen recently are considered released)
        DateTime now = DateTime.Now;
        var keysToRemove = new List<KeyCode>();
        foreach (var kvp in _pressedKeys)
        {
            if ((now - kvp.Value).TotalMilliseconds > KeyRepeatThresholdMs)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            _pressedKeys.Remove(key);
        }

        // If no keys are pressed, don't move
        if (_pressedKeys.Count == 0)
            return false;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = currentWidth;
        int frameHeight = currentHeight;

        // Get map viewport centered on player position
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        // Calculate top-left corner of player in viewport coordinates
        int topLeftCol = frameWidth / 2;
        int topLeftRow = frameHeight / 2;

        // Helper function to check if a cell is walkable (space)
        bool IsWalkable(int row, int col)
        {
            if (row < 0 || row >= frameHeight || col < 0 || col >= frameWidth)
                return false;
            return map?[row][col] == ' ';
        }

        // Helper function to check if all 6 cells the player will occupy are walkable
        bool CanMoveTo(int newTopLeftCol, int newTopLeftRow)
        {
            // Check walls first
            if (!IsWalkable(newTopLeftRow, newTopLeftCol) ||
                !IsWalkable(newTopLeftRow, newTopLeftCol + 1) ||
                !IsWalkable(newTopLeftRow + 1, newTopLeftCol) ||
                !IsWalkable(newTopLeftRow + 1, newTopLeftCol + 1) ||
                !IsWalkable(newTopLeftRow + 2, newTopLeftCol) ||
                !IsWalkable(newTopLeftRow + 2, newTopLeftCol + 1))
            {
                return false;
            }
            
            // Check player-to-player collision in multiplayer
            if (_isMultiplayer && _gameSession != null)
            {
                // Calculate new world position from viewport delta
                int viewportDeltaX = newTopLeftCol - topLeftCol;
                int viewportDeltaY = newTopLeftRow - topLeftRow;
                int newWorldX = _player.X + viewportDeltaX;
                int newWorldY = _player.Y + viewportDeltaY;
                
                // Handle map wrapping for collision check
                newWorldX = (newWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                newWorldY = (newWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                
                // Check against all other players (local and remote)
                foreach (var networkPlayer in _networkPlayers.Values)
                {
                    if (networkPlayer.PlayerId == _gameSession.PlayerId)
                        continue; // Skip self
                    
                    // Get network player world position (wrapped)
                    int npWorldX = (networkPlayer.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    int npWorldY = (networkPlayer.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                    
                    // Check if new position overlaps with this player (2x3 area)
                    // Player occupies: [X, X+1] columns, [Y, Y+1, Y+2] rows
                    if (!(newWorldX + 2 <= npWorldX || newWorldX >= npWorldX + 2 ||
                          newWorldY + 3 <= npWorldY || newWorldY >= npWorldY + 3))
                    {
                        return false; // Overlaps with another player
                    }
                }
            }
            
            return true;
        }

        // Determine movement direction based on currently pressed keys
        int deltaX = 0;
        int deltaY = 0;

        // Check for cardinal directions (arrow keys and keypad)
        bool upPressed = _pressedKeys.ContainsKey(KeyCode.CursorUp) || _pressedKeys.ContainsKey(KeyCode.D8);
        bool downPressed = _pressedKeys.ContainsKey(KeyCode.CursorDown) || _pressedKeys.ContainsKey(KeyCode.D2);
        bool leftPressed = _pressedKeys.ContainsKey(KeyCode.CursorLeft) || _pressedKeys.ContainsKey(KeyCode.D4);
        bool rightPressed = _pressedKeys.ContainsKey(KeyCode.CursorRight) || _pressedKeys.ContainsKey(KeyCode.D6);

        // Check for diagonal keypad keys
        bool upLeftPressed = _pressedKeys.ContainsKey(KeyCode.D7);
        bool upRightPressed = _pressedKeys.ContainsKey(KeyCode.D9);
        bool downLeftPressed = _pressedKeys.ContainsKey(KeyCode.D1);
        bool downRightPressed = _pressedKeys.ContainsKey(KeyCode.D3);

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

        // Try to move if there's a direction
        if (deltaX != 0 || deltaY != 0)
        {
            int newTopLeftCol = topLeftCol + deltaX;
            int newTopLeftRow = topLeftRow + deltaY;

            if (CanMoveTo(newTopLeftCol, newTopLeftRow))
            {
                _player.X += deltaX;
                _player.Y += deltaY;

                // Handle map wrapping
                if (_player.X < 0)
                    _player.X = _map.MapWidth;
                else if (_player.X > _map.MapWidth)
                    _player.X = 0;

                if (_player.Y < 0)
                    _player.Y = _map.MapHeight;
                else if (_player.Y > _map.MapHeight)
                    _player.Y = 0;

                // Invalidate cached map since player moved
                _cachedMapViewport = null;
                
                // Publish position update in multiplayer
                if (_isMultiplayer && _gameSession != null && _mqttClient != null)
                {
                    PublishPlayerPosition();
                }
                
                return true; // Player moved
            }
        }

        return false; // Player didn't move
    }

    private void DrawMapAndPlayer()
    {
        if (Application.Driver == null)
            return;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;

        // Ensure we have valid dimensions (need at least status bar + some map)
        if (currentWidth < 3 || currentHeight < StatusBarHeight + 3)
            return;

        int frameWidth = currentWidth;
        int frameHeight = currentHeight - StatusBarHeight; // Account for status bar

        _lastFrameWidth = frameWidth;
        _lastFrameHeight = frameHeight;

        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        // Cache map viewport for reuse in other drawing functions
        _cachedMapViewport = map;

        // Draw status bar first
        DrawStatusBar();

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));

        // draw the maze - start at row StatusBarHeight (after status bar)
        for (int r = 0; r < frameHeight; r++)
        {
            Application.Driver.Move(0, r + StatusBarHeight);
            Application.Driver.AddStr(map[r]);
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
        DateTime now = DateTime.Now;
        double elapsedMs = (now - _lastFrameTime).TotalMilliseconds;
        
        if (elapsedMs > 0)
        {
            // Calculate FPS for this frame
            double frameFPS = 1000.0 / elapsedMs;
            
            // Add to history
            _fpsHistory.Enqueue(frameFPS);
            if (_fpsHistory.Count > FpsHistorySize)
            {
                _fpsHistory.Dequeue();
            }
            
            // Calculate average FPS
            if (_fpsHistory.Count > 0)
            {
                _currentFPS = _fpsHistory.Average();
            }
        }
        
        _lastFrameTime = now;
    }

    private void DrawPlayerWithClearing()
    {
        // Clear previous player position before drawing new position
        if (_previousPlayerViewportX >= 0 && _previousPlayerViewportY >= 0 && _cachedMapViewport != null)
        {
            int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : Application.Driver!.Cols;
            int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (Application.Driver!.Rows - StatusBarHeight);

            if (_previousPlayerViewportX < frameWidth && _previousPlayerViewportY < frameHeight &&
                _previousPlayerViewportY >= 0 && _previousPlayerViewportY < _cachedMapViewport.Length &&
                _previousPlayerViewportX >= 0 && _previousPlayerViewportX < _cachedMapViewport[_previousPlayerViewportY].Length)
            {
                Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                // Clear all 6 cells of player (2x3)
                for (int row = 0; row < 3; row++)
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
                            Application.Driver.Move(clearX, clearY + StatusBarHeight);
                            Application.Driver.AddRune(mapChar);
                        }
                    }
                }
            }
        }

        // Draw player at new position
        DrawPlayer();

        // Update previous viewport position
        int frameWidth2 = _lastFrameWidth != 0 ? _lastFrameWidth : Application.Driver!.Cols;
        int frameHeight2 = _lastFrameHeight != 0 ? _lastFrameHeight : (Application.Driver!.Rows - StatusBarHeight);
        _previousPlayerViewportX = frameWidth2 / 2;
        _previousPlayerViewportY = frameHeight2 / 2;
    }

    private void UpdateBullets()
    {
        int currentWidth = Application.Driver!.Cols;
        int currentHeight = Application.Driver!.Rows;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
        int mapOffsetX = _player.X - (frameWidth / 2);
        int mapOffsetY = _player.Y - (frameHeight / 2);

        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var bullet = _bullets[i];

            // Check if bullet has expired (older than 2 seconds)
            double ageSeconds = (DateTime.Now - bullet.CreatedAt).TotalSeconds;
            if (ageSeconds >= Bullet.LifetimeSeconds)
            {
                // Clear the expired bullet from screen before removing
                int viewportX = (int)Math.Round(bullet.X) - mapOffsetX;
                int viewportY = (int)Math.Round(bullet.Y) - mapOffsetY;

                if (viewportX >= 0 && viewportX < frameWidth &&
                    viewportY >= 0 && viewportY < frameHeight &&
                    map != null && viewportY >= 0 && viewportY < map.Length &&
                    viewportX >= 0 && viewportX < map[viewportY].Length)
                {
                    Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                    Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddRune(map[viewportY][viewportX]);
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                }

                // Publish bullet expired in multiplayer
                if (_isMultiplayer && _gameSession != null && _mqttClient != null && bullet.PlayerId == _gameSession.PlayerId)
                {
                    PublishBulletUpdate(bullet, "expired");
                }
                
                _bullets.RemoveAt(i);
                continue;
            }

            // Store previous position
            double prevX = bullet.X;
            double prevY = bullet.Y;

            // Update bullet position (moves every 10ms when this is called)
            bullet.Update();
            
            // Publish bullet update in multiplayer (for local bullets only)
            if (_isMultiplayer && _gameSession != null && _mqttClient != null && bullet.PlayerId == _gameSession.PlayerId)
            {
                PublishBulletUpdate(bullet, "updated");
            }

            // Check for wall collision using world map coordinates
            int bulletMapX = (int)Math.Round(bullet.X);
            int bulletMapY = (int)Math.Round(bullet.Y);

            // Wrap coordinates to map bounds
            bulletMapX = (bulletMapX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            bulletMapY = (bulletMapY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

            // Check if bullet hit a wall
            if (bulletMapY >= 0 && bulletMapY < _map.MapHeight &&
                bulletMapX >= 0 && bulletMapX < _map.MapWidth)
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

            // Check for bullet-snipe collision
            int bulletWorldX = (int)Math.Round(bullet.X);
            int bulletWorldY = (int)Math.Round(bullet.Y);
            bulletWorldX = (bulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            bulletWorldY = (bulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

            bool bulletRemoved = false;

            for (int j = _snipes.Count - 1; j >= 0; j--)
            {
                var snipe = _snipes[j];
                if (!snipe.IsAlive)
                    continue;

                // Check if bullet is at snipe position or arrow position
                int snipeWorldX = (snipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                int snipeWorldY = (snipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                // Check bullet at snipe position
                if (bulletWorldX == snipeWorldX && bulletWorldY == snipeWorldY)
                {
                    // Bullet hit snipe - clear both bullet and snipe
                    snipe.IsAlive = false;

                    // Get fresh map to ensure we have correct character for clearing
                    var freshMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

                    // Clear snipe first (both '@' and arrow) - uses world coordinates
                    ClearSnipePosition(snipe);

                    // Clear bullet at collision point (use bullet's current position)
                    int viewportX = bulletWorldX - mapOffsetX;
                    int viewportY = bulletWorldY - mapOffsetY;
                    if (viewportX >= 0 && viewportX < frameWidth &&
                        viewportY >= 0 && viewportY < frameHeight &&
                        freshMap != null && viewportY >= 0 && viewportY < freshMap.Length &&
                        viewportX >= 0 && viewportX < freshMap[viewportY].Length)
                    {
                        Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(freshMap[viewportY][viewportX]);
                        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                    }

                    // Also clear bullet's previous position if different
                    int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
                    int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
                    prevBulletWorldX = (prevBulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    prevBulletWorldY = (prevBulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                    if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
                    {
                        int prevViewportX = prevBulletWorldX - mapOffsetX;
                        int prevViewportY = prevBulletWorldY - mapOffsetY;
                        if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                            prevViewportY >= 0 && prevViewportY < frameHeight &&
                            freshMap != null && prevViewportY >= 0 && prevViewportY < freshMap.Length &&
                            prevViewportX >= 0 && prevViewportX < freshMap[prevViewportY].Length)
                        {
                            Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                            Application.Driver.Move(prevViewportX, prevViewportY + StatusBarHeight);
                            Application.Driver.AddRune(freshMap[prevViewportY][prevViewportX]);
                            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                        }
                    }

                    // Invalidate cached map since we're removing entities
                    _cachedMapViewport = null;

                    // Remove from lists AFTER clearing
                    _snipes.RemoveAt(j);
                    _bullets.RemoveAt(i);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += 25;
                    _player.Score += 25;
                    bulletRemoved = true;
                    break; // Bullet is removed, exit snipe loop
                }

                // Check bullet at arrow position
                int arrowWorldX = snipeWorldX + (snipe.DirectionX < 0 ? -1 : 1);
                arrowWorldX = (arrowWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                if (bulletWorldX == arrowWorldX && bulletWorldY == snipeWorldY)
                {
                    // Bullet hit snipe arrow - clear both bullet and snipe
                    snipe.IsAlive = false;

                    // Get fresh map to ensure we have correct character for clearing
                    var freshMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

                    // Clear snipe first (both '@' and arrow) - uses world coordinates
                    ClearSnipePosition(snipe);

                    // Clear bullet at collision point
                    int viewportX = bulletWorldX - mapOffsetX;
                    int viewportY = bulletWorldY - mapOffsetY;
                    if (viewportX >= 0 && viewportX < frameWidth &&
                        viewportY >= 0 && viewportY < frameHeight &&
                        freshMap != null && viewportY >= 0 && viewportY < freshMap.Length &&
                        viewportX >= 0 && viewportX < freshMap[viewportY].Length)
                    {
                        Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(freshMap[viewportY][viewportX]);
                        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                    }

                    // Also clear bullet's previous position if different
                    int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
                    int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
                    prevBulletWorldX = (prevBulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    prevBulletWorldY = (prevBulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                    if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
                    {
                        int prevViewportX = prevBulletWorldX - mapOffsetX;
                        int prevViewportY = prevBulletWorldY - mapOffsetY;
                        if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                            prevViewportY >= 0 && prevViewportY < frameHeight &&
                            freshMap != null && prevViewportY >= 0 && prevViewportY < freshMap.Length &&
                            prevViewportX >= 0 && prevViewportX < freshMap[prevViewportY].Length)
                        {
                            Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                            Application.Driver.Move(prevViewportX, prevViewportY + StatusBarHeight);
                            Application.Driver.AddRune(freshMap[prevViewportY][prevViewportX]);
                            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                        }
                    }

                    // Invalidate cached map since we're removing entities
                    _cachedMapViewport = null;

                    // Remove from lists AFTER clearing
                    _snipes.RemoveAt(j);
                    _bullets.RemoveAt(i);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += 25;
                    _player.Score += 25;
                    bulletRemoved = true;
                    break; // Bullet is removed, exit snipe loop
                }
            }

            // Check for bullet-hive collision (only if bullet still exists)
            if (!bulletRemoved)
            {
                bulletWorldX = (int)Math.Round(bullet.X);
                bulletWorldY = (int)Math.Round(bullet.Y);
                bulletWorldX = (bulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                bulletWorldY = (bulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                foreach (var hive in _hives)
                {
                    if (hive.IsDestroyed)
                        continue;

                    // Check if bullet is within hive bounds (2x2 area)
                    // Hive occupies: [X, X+1] columns, [Y, Y+1] rows
                    int hiveWorldX = (hive.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    int hiveWorldY = (hive.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                    int hiveWorldX2 = (hiveWorldX + 1) % _map.MapWidth;
                    int hiveWorldY2 = (hiveWorldY + 1) % _map.MapHeight;

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

                        // Clear bullet at collision point
                        int viewportX = bulletWorldX - mapOffsetX;
                        int viewportY = bulletWorldY - mapOffsetY;
                        if (viewportX >= 0 && viewportX < frameWidth &&
                            viewportY >= 0 && viewportY < frameHeight &&
                            freshMap != null && viewportY >= 0 && viewportY < freshMap.Length &&
                            viewportX >= 0 && viewportX < freshMap[viewportY].Length)
                        {
                            Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                            Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                            Application.Driver.AddRune(freshMap[viewportY][viewportX]);
                            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                        }

                        // Also clear bullet's previous position if different
                        int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
                        int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
                        prevBulletWorldX = (prevBulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                        prevBulletWorldY = (prevBulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                        if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
                        {
                            int prevViewportX = prevBulletWorldX - mapOffsetX;
                            int prevViewportY = prevBulletWorldY - mapOffsetY;
                            if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                                prevViewportY >= 0 && prevViewportY < frameHeight &&
                                freshMap != null && prevViewportY >= 0 && prevViewportY < freshMap.Length &&
                                prevViewportX >= 0 && prevViewportX < freshMap[prevViewportY].Length)
                            {
                                Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                                Application.Driver.Move(prevViewportX, prevViewportY + StatusBarHeight);
                                Application.Driver.AddRune(freshMap[prevViewportY][prevViewportX]);
                                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                            }
                        }

                        // Invalidate cached map since we're removing a bullet
                        _cachedMapViewport = null;

                        // Publish bullet hit in multiplayer (host only)
                        if (_isMultiplayer && _gameSession != null && _mqttClient != null && 
                            _gameSession.Role == GameSessionRole.Host && bullet.PlayerId == _gameSession.PlayerId)
                        {
                            PublishBulletUpdate(bullet, "hit", "hive", $"hive_{hive.X}_{hive.Y}");
                        }
                        
                        _bullets.RemoveAt(i);
                        bulletRemoved = true;

                        // Check if hive is destroyed (3 hits)
                        if (hive.Hits >= Hive.HitsToDestroy)
                        {
                            hive.IsDestroyed = true;
                            _gameState.HivesUndestroyed--;

                            // Clear hive from screen immediately
                            ClearHivePosition(hive);

                            // Kill all unreleased snipes from this hive
                            int unreleasedSnipes = hive.SnipesRemaining;

                            // Add score: 500 for hive + 25 per unreleased snipe
                            int hiveScore = 500 + (unreleasedSnipes * 25);
                            _gameState.Score += hiveScore;
                            _player.Score += hiveScore;

                            // Update total snipes count (unreleased snipes are now gone)
                            _gameState.SnipesUndestroyed -= unreleasedSnipes;
                            _gameState.TotalSnipes -= unreleasedSnipes;
                        }

                        break; // Bullet is removed, exit hive loop
                    }
                }
            }
            
            // Check for bullet-to-player collision (host only, for all bullets)
            if (_isMultiplayer && _gameSession != null && _gameSession.Role == GameSessionRole.Host)
            {
                CheckBulletPlayerCollision(bullet, frameWidth, frameHeight, mapOffsetX, mapOffsetY);
            }
        }
    }
    
    private void CheckBulletPlayerCollision(Bullet bullet, int frameWidth, int frameHeight, int mapOffsetX, int mapOffsetY)
    {
        if (_gameSession == null || _mqttClient == null)
            return;
        
        int bulletWorldX = (int)Math.Round(bullet.X);
        int bulletWorldY = (int)Math.Round(bullet.Y);
        bulletWorldX = (bulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
        bulletWorldY = (bulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
        
        // Check against all network players (including local)
        foreach (var networkPlayer in _networkPlayers.Values)
        {
            // Skip if bullet belongs to this player
            if (bullet.PlayerId == networkPlayer.PlayerId)
                continue;
            
            int playerWorldX = (networkPlayer.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int playerWorldY = (networkPlayer.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
            
            // Check if bullet is within player's 2x3 area
            if (bulletWorldX >= playerWorldX && bulletWorldX <= playerWorldX + 1 &&
                bulletWorldY >= playerWorldY && bulletWorldY <= playerWorldY + 2)
            {
                // Bullet hit player
                networkPlayer.Lives--;
                networkPlayer.IsAlive = networkPlayer.Lives > 0;
                
                // Publish bullet hit
                PublishBulletUpdate(bullet, "hit", "player", networkPlayer.PlayerId);
                
                // Remove bullet
                _bullets.RemoveAll(b => b.BulletId == bullet.BulletId);
                
                if (networkPlayer.IsLocal)
                {
                    // Local player hit - handle respawn
                    _player.Lives = networkPlayer.Lives;
                    _player.IsAlive = networkPlayer.IsAlive;
                    _cachedLives = -1; // Force status bar update
                    
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
                        if (_isMultiplayer && _gameSession != null && _mqttClient != null)
                        {
                            _positionSequence++;
                            var posUpdate = new PlayerPositionUpdateMessage
                            {
                                PlayerId = _gameSession.PlayerId,
                                X = _player.X,  // World coordinate
                                Y = _player.Y,  // World coordinate
                                Timestamp = DateTime.UtcNow,
                                Sequence = _positionSequence
                            };
                            _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/player/{_gameSession.PlayerId}/position", posUpdate);
                            _lastPositionPublish = DateTime.Now;
                        }
                        
                        _introScreen.StartClearingEffect($"{_player.Lives} Lives Left");
                    }
                    else
                    {
                        // Game over for this player
                        _player.IsAlive = false;
                        _introScreen.ShowGameOver("GAME OVER");
                    }
                }
                
                break; // Only one player can be hit per bullet
            }
        }
    }

    private void DrawBullets()
    {
        if (Application.Driver == null)
            return;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
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

        // First, clear previous bullet positions by drawing the map character there
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
        foreach (var bullet in _bullets)
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
                        Application.Driver.Move(prevViewportX, prevViewportY + StatusBarHeight);
                        Application.Driver.AddRune(mapChar);
                    }
                }
            }
        }

        // Cache DateTime to avoid multiple system calls
        if ((DateTime.Now - _cachedDateTime).TotalMilliseconds > 10)
        {
            _cachedDateTime = DateTime.Now;
        }

        // Flash between bright red and red based on time
        bool isBright = (_cachedDateTime.Millisecond / 250) % 2 == 0;
        var bulletColor = isBright ? Color.BrightRed : Color.Red;
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(bulletColor, Color.Black));

        // Now draw bullets at their current positions
        foreach (var bullet in _bullets)
        {
            // Convert world coordinates to viewport coordinates
            int viewportX = (int)Math.Round(bullet.X) - mapOffsetX;
            int viewportY = (int)Math.Round(bullet.Y) - mapOffsetY;

            // Only draw if within viewport (offset by StatusBarHeight)
            if (viewportX >= 0 && viewportX < frameWidth &&
                viewportY >= 0 && viewportY < frameHeight)
            {
                Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                Application.Driver.AddRune('*');
            }
        }

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
    }

    private void DrawHives()
    {
        if (Application.Driver == null)
            return;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Get map viewport to convert world coordinates to viewport coordinates
        int mapOffsetX = _player.X - (frameWidth / 2);
        int mapOffsetY = _player.Y - (frameHeight / 2);

        // Cache DateTime to avoid multiple system calls
        if ((DateTime.Now - _cachedDateTime).TotalMilliseconds > 10)
        {
            _cachedDateTime = DateTime.Now;
        }

        long totalMs = _cachedDateTime.Ticks / TimeSpan.TicksPerMillisecond;

        // Pre-calculate viewport bounds for early exit optimization
        int minViewportX = -2; // Hive is 2 wide, so allow 2 cells outside for partial visibility
        int maxViewportX = frameWidth + 1;
        int minViewportY = -2;
        int maxViewportY = frameHeight + 1;

        foreach (var hive in _hives)
        {
            if (hive.IsDestroyed)
                continue;

            // Each hive has its own flash interval (reduced by 1/3 each hit)
            // Flash between cyan and green based on time
            bool isCyan = (totalMs / hive.FlashIntervalMs) % 2 == 0;
            var hiveColor = isCyan ? Color.Cyan : Color.Green;
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(hiveColor, Color.Black));

            // Calculate viewport coordinates for the hive
            // The viewport is centered on the player at (frameWidth/2, frameHeight/2)
            // Map.GetMap centers on (_player.X, _player.Y), so we need to calculate
            // the relative position of the hive from the player, accounting for wrapping

            // Calculate the difference in world coordinates
            int deltaX = hive.X - _player.X;
            int deltaY = hive.Y - _player.Y;

            // Handle wrapping: find the shortest path (accounting for wrap)
            // If the difference is more than half the map size, wrap around
            if (deltaX > _map.MapWidth / 2)
                deltaX -= _map.MapWidth;
            else if (deltaX < -_map.MapWidth / 2)
                deltaX += _map.MapWidth;

            if (deltaY > _map.MapHeight / 2)
                deltaY -= _map.MapHeight;
            else if (deltaY < -_map.MapHeight / 2)
                deltaY += _map.MapHeight;

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
                Application.Driver.Move(hiveViewportX, hiveViewportY + StatusBarHeight);
                Application.Driver.AddRune('╔');
            }

            // Top-right corner
            int topRightX = hiveViewportX + 1;
            if (topRightX >= 0 && topRightX < frameWidth &&
                hiveViewportY >= 0 && hiveViewportY < frameHeight)
            {
                Application.Driver.Move(topRightX, hiveViewportY + StatusBarHeight);
                Application.Driver.AddRune('╗');
            }

            // Bottom-left corner
            int bottomLeftY = hiveViewportY + 1;
            if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
                bottomLeftY >= 0 && bottomLeftY < frameHeight)
            {
                Application.Driver.Move(hiveViewportX, bottomLeftY + StatusBarHeight);
                Application.Driver.AddRune('╚');
            }

            // Bottom-right corner
            if (topRightX >= 0 && topRightX < frameWidth &&
                bottomLeftY >= 0 && bottomLeftY < frameHeight)
            {
                Application.Driver.Move(topRightX, bottomLeftY + StatusBarHeight);
                Application.Driver.AddRune('╝');
            }
        }

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
    }

    private void SpawnSnipes()
    {
        foreach (var hive in _hives)
        {
            if (!hive.CanSpawnSnipe())
                continue;

            // Random chance to spawn (roughly every 3 seconds, but randomized)
            int timeSinceLastSpawn = (int)(DateTime.Now - hive.LastSpawnTime).TotalMilliseconds;
            if (timeSinceLastSpawn >= Hive.SpawnIntervalMs + _random.Next(-1000, 1000))
            {
                // Spawn snipe at hive position (center of 2x2 hive)
                int snipeX = hive.X + 1; // Center of hive
                int snipeY = hive.Y + 1;
                char snipeType = hive.GetNextSnipeType();

                var snipe = new Snipe(snipeX, snipeY, snipeType);

                // Give snipe a random initial direction
                int[] directions = new int[] { -1, 0, 1 };
                snipe.DirectionX = directions[_random.Next(3)];
                snipe.DirectionY = directions[_random.Next(3)];

                // Ensure snipe has some direction (not both 0)
                if (snipe.DirectionX == 0 && snipe.DirectionY == 0)
                {
                    if (_random.Next(2) == 0)
                        snipe.DirectionX = _random.Next(2) == 0 ? -1 : 1;
                    else
                        snipe.DirectionY = _random.Next(2) == 0 ? -1 : 1;
                }

                _snipes.Add(snipe);
                hive.SpawnSnipe();
                _gameState.SnipesUndestroyed++;
                
                // Publish snipe spawn in multiplayer (host only)
                if (_isMultiplayer && _gameSession != null && _gameSession.Role == GameSessionRole.Host && _mqttClient != null)
                {
                    PublishSnipeSpawn(snipe);
                }
            }
        }
    }
    
    private void PublishSnipeSpawn(Snipe snipe)
    {
        if (_gameSession == null || _mqttClient == null)
            return;
        
        var update = new SnipeUpdateInfo
        {
            SnipeId = $"snipe_{snipe.X}_{snipe.Y}_{DateTime.UtcNow.Ticks}",
            Action = "spawned",
            X = snipe.X,
            Y = snipe.Y,
            DirectionX = snipe.DirectionX,
            DirectionY = snipe.DirectionY,
            Type = snipe.Type,
            Timestamp = DateTime.UtcNow
        };
        
        var updates = new SnipeUpdatesMessage
        {
            Updates = new List<SnipeUpdateInfo> { update }
        };
        
        _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/snipes", updates);
    }
    
    private void PublishSnipeUpdates()
    {
        if (_gameSession == null || _mqttClient == null || _gameSession.Role != GameSessionRole.Host)
            return;
        
        // Publish all current snipe positions (periodic update)
        // IMPORTANT: All coordinates must be WORLD/MAP coordinates, not viewport
        var updates = new SnipeUpdatesMessage
        {
            Updates = _snipes.Where(s => s.IsAlive).Select(s => new SnipeUpdateInfo
            {
                SnipeId = $"snipe_{s.X}_{s.Y}_{s.LastMoveTime.Ticks}",
                Action = "moved",
                X = s.X,  // World coordinate (map space)
                Y = s.Y,  // World coordinate (map space)
                DirectionX = s.DirectionX,
                DirectionY = s.DirectionY,
                Timestamp = DateTime.UtcNow
            }).ToList()
        };
        
        if (updates.Updates.Count > 0)
        {
            _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/snipes", updates);
        }
    }

    private bool IsSnipePositionValid(int x, int y, int dirX, int dirY)
    {
        // Check if snipe position (x, y) is valid (not a wall)
        int wrappedX = (x % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
        int wrappedY = (y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

        if (wrappedY < 0 || wrappedY >= _map.MapHeight || wrappedX < 0 || wrappedX >= _map.MapWidth)
            return false;

        if (_map.FullMap[wrappedY][wrappedX] != ' ')
            return false;

        // Check if arrow position is also valid
        // Arrow position depends on direction:
        // Moving left (dirX < 0): arrow is at (x - 1, y)
        // Moving right or other: arrow is at (x + 1, y)
        int arrowX = dirX < 0 ? x - 1 : x + 1;
        int arrowY = y;

        int wrappedArrowX = (arrowX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
        int wrappedArrowY = (arrowY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

        if (wrappedArrowY < 0 || wrappedArrowY >= _map.MapHeight || wrappedArrowX < 0 || wrappedArrowX >= _map.MapWidth)
            return false;

        if (_map.FullMap[wrappedArrowY][wrappedArrowX] != ' ')
            return false;

        return true;
    }

    private bool CheckSnipeSnipeCollision(Snipe snipe1, Snipe snipe2)
    {
        // Wrap coordinates for comparison
        int x1 = (snipe1.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
        int y1 = (snipe1.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
        int x2 = (snipe2.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
        int y2 = (snipe2.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

        // Arrow position depends on direction:
        // Moving left (DirectionX < 0): arrow is at (x - 1, y)
        // Moving right or other: arrow is at (x + 1, y)
        int arrow1X = snipe1.DirectionX < 0 ? (x1 - 1 + _map.MapWidth) % _map.MapWidth : (x1 + 1) % _map.MapWidth;
        int arrow2X = snipe2.DirectionX < 0 ? (x2 - 1 + _map.MapWidth) % _map.MapWidth : (x2 + 1) % _map.MapWidth;

        // Check if snipe1's position overlaps with snipe2's position or arrow
        if ((x1 == x2 && y1 == y2) || (x1 == arrow2X && y1 == y2))
            return true;

        // Check if snipe1's arrow overlaps with snipe2's position or arrow
        if ((arrow1X == x2 && y1 == y2) || (arrow1X == arrow2X && y1 == y2))
            return true;

        return false;
    }

    private void UpdateSnipes()
    {
        for (int i = _snipes.Count - 1; i >= 0; i--)
        {
            var snipe = _snipes[i];

            if (!snipe.IsAlive)
            {
                // Clear snipe from screen before removing
                ClearSnipePosition(snipe);
                _snipes.RemoveAt(i);
                _gameState.SnipesUndestroyed--;
                continue;
            }

            // Check if it's time to move
            int timeSinceLastMove = (int)(DateTime.Now - snipe.LastMoveTime).TotalMilliseconds;
            if (timeSinceLastMove < Snipe.MoveIntervalMs)
                continue;

            // Calculate distance to player for heat radius system
            int deltaX = _player.X - snipe.X;
            int deltaY = _player.Y - snipe.Y;

            // Handle map wrapping - find shortest path
            if (deltaX > _map.MapWidth / 2)
                deltaX -= _map.MapWidth;
            else if (deltaX < -_map.MapWidth / 2)
                deltaX += _map.MapWidth;

            if (deltaY > _map.MapHeight / 2)
                deltaY -= _map.MapHeight;
            else if (deltaY < -_map.MapHeight / 2)
                deltaY += _map.MapHeight;

            // Calculate distance (Manhattan distance for simplicity)
            int distanceToPlayer = Math.Abs(deltaX) + Math.Abs(deltaY);

            // Heat radius: closer = more attracted, further = less attracted
            // Use a maximum radius (e.g., 20 cells) - beyond this, movement is mostly random
            const int maxHeatRadius = 20;
            double heatFactor = Math.Max(0, 1.0 - (distanceToPlayer / (double)maxHeatRadius));
            // heatFactor: 1.0 when at player, 0.0 when at maxHeatRadius or beyond

            // Determine preferred direction (toward player)
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

            // Note: PreviousX/PreviousY are updated at the end of DrawSnipes()
            // to match what was actually drawn. We don't update them here.

            // Get all possible valid directions
            List<(int dx, int dy)> possibleDirections = new List<(int, int)>();

            // Try all 8 possible directions (including diagonals)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue; // Skip no movement

                    int testX = snipe.X + dx;
                    int testY = snipe.Y + dy;

                    if (IsSnipePositionValid(testX, testY, dx, dy))
                    {
                        possibleDirections.Add((dx, dy));
                    }
                }
            }

            if (possibleDirections.Count == 0)
            {
                // Can't move in any direction - stay in place but keep trying
                snipe.LastMoveTime = DateTime.Now;
                continue;
            }

            // Determine direction choice based on rules:
            // 1. Try to continue in current direction if valid (unless player is close)
            // 2. If current direction hits wall, choose new direction
            // 3. If player is close (heat radius), prefer moving toward player
            (int dx, int dy) chosenDirection;
            bool currentDirectionValid = possibleDirections.Contains((snipe.DirectionX, snipe.DirectionY));

            if (currentDirectionValid && heatFactor < 0.3)
            {
                // Current direction is valid and player is far - wander through maze
                // Occasionally change direction to explore (20% chance)
                if (_random.Next(100) < 20)
                {
                    // Choose a random valid direction to explore
                    chosenDirection = possibleDirections[_random.Next(possibleDirections.Count)];
                }
                else
                {
                    // Continue in current direction
                    chosenDirection = (snipe.DirectionX, snipe.DirectionY);
                }
            }
            else if (heatFactor > 0.3 && (preferredDirX != 0 || preferredDirY != 0))
            {
                // Player is close (heat radius) - prefer moving toward player
                bool preferredValid = possibleDirections.Contains((preferredDirX, preferredDirY));

                if (preferredValid)
                {
                    // Prefer moving toward player, but allow continuing current direction if it's also toward player
                    if (currentDirectionValid && snipe.DirectionX == preferredDirX && snipe.DirectionY == preferredDirY)
                    {
                        // Current direction is toward player - continue
                        chosenDirection = (snipe.DirectionX, snipe.DirectionY);
                    }
                    else
                    {
                        // Change direction to move toward player
                        chosenDirection = (preferredDirX, preferredDirY);
                    }
                }
                else if (currentDirectionValid)
                {
                    // Preferred direction not valid, but current direction is - continue
                    chosenDirection = (snipe.DirectionX, snipe.DirectionY);
                }
                else
                {
                    // Hit a wall and player is close - randomly choose from valid directions
                    chosenDirection = possibleDirections[_random.Next(possibleDirections.Count)];
                }
            }
            else
            {
                // Current direction hit a wall (not valid) and player is far - randomly choose new direction
                chosenDirection = possibleDirections[_random.Next(possibleDirections.Count)];
            }

            // Move snipe
            int newSnipeX = snipe.X + chosenDirection.dx;
            int newSnipeY = snipe.Y + chosenDirection.dy;
            snipe.X = (newSnipeX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            snipe.Y = (newSnipeY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
            snipe.DirectionX = chosenDirection.dx;
            snipe.DirectionY = chosenDirection.dy;
            snipe.LastMoveTime = DateTime.Now;

            // Check for collision with other snipes
            for (int j = 0; j < _snipes.Count; j++)
            {
                if (i == j || !_snipes[j].IsAlive)
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
                    snipe.X = (snipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    snipe.Y = (snipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                    otherSnipe.X = (otherSnipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    otherSnipe.Y = (otherSnipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                    break; // Only handle one collision per update
                }
            }

            // Check collision with bullets (snipe moving into bullet)
            int snipeWorldX = (snipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int snipeWorldY = (snipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

            for (int k = _bullets.Count - 1; k >= 0; k--)
            {
                var bullet = _bullets[k];
                int bulletWorldX = (int)Math.Round(bullet.X);
                int bulletWorldY = (int)Math.Round(bullet.Y);
                bulletWorldX = (bulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                bulletWorldY = (bulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                // Check if snipe is at bullet position
                if (snipeWorldX == bulletWorldX && snipeWorldY == bulletWorldY)
                {
                    // Snipe moved into bullet - clear both bullet and snipe
                    snipe.IsAlive = false;

                    // Get fresh map to ensure we have correct character for clearing
                    int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : Application.Driver!.Cols;
                    int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (Application.Driver!.Rows - StatusBarHeight);
                    var bulletMap = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
                    int mapOffsetX = _player.X - (frameWidth / 2);
                    int mapOffsetY = _player.Y - (frameHeight / 2);
                    int viewportX = bulletWorldX - mapOffsetX;
                    int viewportY = bulletWorldY - mapOffsetY;
                    if (viewportX >= 0 && viewportX < frameWidth &&
                        viewportY >= 0 && viewportY < frameHeight &&
                        bulletMap != null && viewportY >= 0 && viewportY < bulletMap.Length &&
                        viewportX >= 0 && viewportX < bulletMap[viewportY].Length)
                    {
                        Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(bulletMap[viewportY][viewportX]);
                        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                    }

                    // Clear snipe first (both '@' and arrow) - uses world coordinates
                    ClearSnipePosition(snipe);

                    // Clear bullet at collision point
                    if (viewportX >= 0 && viewportX < frameWidth &&
                        viewportY >= 0 && viewportY < frameHeight &&
                        bulletMap != null && viewportY >= 0 && viewportY < bulletMap.Length &&
                        viewportX >= 0 && viewportX < bulletMap[viewportY].Length)
                    {
                        Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(bulletMap[viewportY][viewportX]);
                        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                    }

                    // Also clear bullet's previous position if different
                    int prevBulletWorldX = (int)Math.Round(bullet.PreviousX);
                    int prevBulletWorldY = (int)Math.Round(bullet.PreviousY);
                    prevBulletWorldX = (prevBulletWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    prevBulletWorldY = (prevBulletWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                    if (prevBulletWorldX != bulletWorldX || prevBulletWorldY != bulletWorldY)
                    {
                        int prevViewportX = prevBulletWorldX - mapOffsetX;
                        int prevViewportY = prevBulletWorldY - mapOffsetY;
                        if (prevViewportX >= 0 && prevViewportX < frameWidth &&
                            prevViewportY >= 0 && prevViewportY < frameHeight &&
                            bulletMap != null && prevViewportY >= 0 && prevViewportY < bulletMap.Length &&
                            prevViewportX >= 0 && prevViewportX < bulletMap[prevViewportY].Length)
                        {
                            Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
                            Application.Driver.Move(prevViewportX, prevViewportY + StatusBarHeight);
                            Application.Driver.AddRune(bulletMap[prevViewportY][prevViewportX]);
                            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                        }
                    }

                    // Invalidate cached map since we're removing entities
                    _cachedMapViewport = null;

                    // Remove from lists AFTER clearing
                    _snipes.RemoveAt(i);
                    _bullets.RemoveAt(k);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += 25;
                    _player.Score += 25;
                    // Snipe is removed, continue to next snipe
                    goto nextSnipe;
                }
            }

            // Check collision with player (only if snipe is still alive and game is not over)
            if (!snipe.IsAlive)
                goto nextSnipe;

            // Don't check collision if game is over
            if (_introScreen.IsGameOver || _introScreen.IsWaitingForGameOverKey)
                goto nextSnipe;

            if (CheckSnipePlayerCollision(snipe))
            {
                // Snipe explodes, player loses a life
                snipe.IsAlive = false;
                _player.Lives--;
                
                // Invalidate cached lives to ensure status bar updates
                _cachedLives = -1;

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
                    // Game over
                    _player.IsAlive = false;
                    _introScreen.ShowGameOver("GAME OVER");
                }
            }

        nextSnipe:; // Label for continue after snipe removal
        }
    }

    private bool CheckSnipePlayerCollision(Snipe snipe)
    {
        // Check if snipe position overlaps with any part of the player (2x3)
        // Player occupies: [X, X+1] columns, [Y, Y+1, Y+2] rows
        return snipe.X >= _player.X && snipe.X <= _player.X + 1 &&
               snipe.Y >= _player.Y && snipe.Y <= _player.Y + 2;
    }

    private void DrawSnipes()
    {
        if (Application.Driver == null)
            return;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
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
        // This includes both '@' character positions and arrow positions from the last frame
        // We ALWAYS add previous positions, even if snipe hasn't moved (direction might have changed)
        HashSet<(int x, int y)> positionsToClear = new HashSet<(int, int)>();

        foreach (var snipe in _snipes)
        {
            if (!snipe.IsAlive)
                continue;

            // Get previous world coordinates (wrapped)
            int prevWorldX = (snipe.PreviousX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int prevWorldY = (snipe.PreviousY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

            // Determine where '@' and arrow were based on previous direction
            // Drawing logic: DirectionX < 0: arrow at center, '@' to right
            //                 DirectionX >= 0: '@' at center, arrow to right
            int prevCharWorldX, prevArrowWorldX;
            if (snipe.PreviousDirectionX < 0)
            {
                // Moving left: arrow was at snipe position, '@' was one cell to the right
                prevArrowWorldX = prevWorldX;
                prevCharWorldX = (prevWorldX + 1) % _map.MapWidth;
            }
            else
            {
                // Moving right, up, down, or diagonal (DirectionX >= 0): '@' at snipe position, arrow to the right
                prevCharWorldX = prevWorldX;
                prevArrowWorldX = (prevWorldX + 1) % _map.MapWidth;
            }

            // Always add both positions to clear list
            positionsToClear.Add((prevCharWorldX, prevWorldY));
            positionsToClear.Add((prevArrowWorldX, prevWorldY));
        }

        // Step 2: Build a set of all positions that snipes CURRENTLY occupy
        // Remove these from the positionsToClear set (don't clear positions that are still occupied)
        foreach (var snipe in _snipes)
        {
            if (!snipe.IsAlive)
                continue;

            int snipeWorldX = (snipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int snipeWorldY = (snipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

            // Calculate current '@' and arrow positions based on current direction
            // Drawing logic: DirectionX < 0: arrow at center, '@' to right
            //                 DirectionX >= 0: '@' at center, arrow to right
            int charWorldX, arrowWorldX;
            if (snipe.DirectionX < 0)
            {
                // Moving left: arrow at snipe position, '@' one cell to the right
                arrowWorldX = snipeWorldX;
                charWorldX = (snipeWorldX + 1) % _map.MapWidth;
            }
            else
            {
                // Moving right, up, down, or diagonal (DirectionX >= 0): '@' at snipe position, arrow to the right
                charWorldX = snipeWorldX;
                arrowWorldX = (snipeWorldX + 1) % _map.MapWidth;
            }
            positionsToClear.Remove((charWorldX, snipeWorldY));
            positionsToClear.Remove((arrowWorldX, snipeWorldY));
        }

        // Step 3: Clear all positions that remain in positionsToClear (no longer occupied)
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
        foreach (var (worldX, worldY) in positionsToClear)
        {
            // Calculate viewport coordinates
            int deltaX = worldX - _player.X;
            int deltaY = worldY - _player.Y;

            // Handle wrapping
            if (deltaX > _map.MapWidth / 2)
                deltaX -= _map.MapWidth;
            else if (deltaX < -_map.MapWidth / 2)
                deltaX += _map.MapWidth;

            if (deltaY > _map.MapHeight / 2)
                deltaY -= _map.MapHeight;
            else if (deltaY < -_map.MapHeight / 2)
                deltaY += _map.MapHeight;

            int viewportX = (frameWidth / 2) + deltaX;
            int viewportY = (frameHeight / 2) + deltaY;

            if (viewportX >= 0 && viewportX < frameWidth &&
                viewportY >= 0 && viewportY < frameHeight &&
                worldY >= 0 && worldY < _map.MapHeight &&
                worldX >= 0 && worldX < _map.MapWidth)
            {
                char mapChar = _map.FullMap[worldY][worldX];
                Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                Application.Driver.AddRune(mapChar);
            }
        }

        // Step 4: Draw snipes at their new positions
        foreach (var snipe in _snipes)
        {
            if (!snipe.IsAlive)
                continue;

            // Calculate viewport coordinates (same logic as hives)
            int deltaX = snipe.X - _player.X;
            int deltaY = snipe.Y - _player.Y;

            // Handle wrapping
            if (deltaX > _map.MapWidth / 2)
                deltaX -= _map.MapWidth;
            else if (deltaX < -_map.MapWidth / 2)
                deltaX += _map.MapWidth;

            if (deltaY > _map.MapHeight / 2)
                deltaY -= _map.MapHeight;
            else if (deltaY < -_map.MapHeight / 2)
                deltaY += _map.MapHeight;

            int viewportX = (frameWidth / 2) + deltaX;
            int viewportY = (frameHeight / 2) + deltaY;

            // Only draw if within viewport
            if (viewportX >= 0 && viewportX < frameWidth &&
                viewportY >= 0 && viewportY < frameHeight)
            {
                // Set color based on snipe type: 'A' = magenta, 'B' = green
                var snipeColor = snipe.Type == 'A' ? Color.Magenta : Color.Green;
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(snipeColor, Color.Black));

                // Draw order depends on direction:
                // Moving left: arrow first, then character
                // Moving right or other: character first, then arrow
                if (snipe.DirectionX < 0)
                {
                    // Moving left - draw arrow first, then character
                    Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddRune(snipe.GetDirectionArrow());

                    if (viewportX + 1 < frameWidth)
                    {
                        Application.Driver.Move(viewportX + 1, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(snipe.GetDisplayChar());
                    }
                }
                else
                {
                    // Moving right or other directions - draw character first, then arrow
                    Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddRune(snipe.GetDisplayChar());

                    if (viewportX + 1 < frameWidth)
                    {
                        Application.Driver.Move(viewportX + 1, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(snipe.GetDirectionArrow());
                    }
                }
            }
        }

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));

        // CRITICAL: Update PreviousX/PreviousY to match what was actually drawn
        // This ensures that on the next frame, we clear the correct positions
        foreach (var snipe in _snipes)
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

    // Callback for IntroScreen to get map character at position during clearing effect
    private char GetMapCharAtPosition(int x, int y)
    {
        if (_map == null)
            return ' ';

        int frameWidth = Application.Driver?.Cols ?? 80;
        int frameHeight = (Application.Driver?.Rows ?? 24) - StatusBarHeight;

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
        if (Application.Driver == null || _map == null)
            return;

        // Calculate which part of the map should be at this position
        int frameWidth = Application.Driver.Cols;
        int frameHeight = Application.Driver.Rows - StatusBarHeight;

        if (y < StatusBarHeight)
        {
            // Status bar area - just draw a space (status bar will be drawn separately)
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
            Application.Driver.Move(x, y);
            Application.Driver.AddRune(' ');
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
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
            Application.Driver.Move(x, y);
            Application.Driver.AddRune(map[mapY][x]);
        }
    }

    private void DrawPlayerAtPosition(int x, int y, int playerTopLeftX, int playerTopLeftY)
    {
        if (Application.Driver == null)
            return;

        int relX = x - playerTopLeftX;
        int relY = y - playerTopLeftY;

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black));
        Application.Driver.Move(x, y);

        // Player is 2x3: "BD" on first row, "BD" on second row, "BD" on third row
        if (relX == 0 && relY == 0)
            Application.Driver.AddRune('B');
        else if (relX == 1 && relY == 0)
            Application.Driver.AddRune('D');
        else if (relX == 0 && relY == 1)
            Application.Driver.AddRune('B');
        else if (relX == 1 && relY == 1)
            Application.Driver.AddRune('D');
        else if (relX == 0 && relY == 2)
            Application.Driver.AddRune('B');
        else if (relX == 1 && relY == 2)
            Application.Driver.AddRune('D');
    }

    private void ClearSnipePosition(Snipe snipe)
    {
        if (Application.Driver == null) return;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Calculate viewport coordinates
        int deltaX = snipe.X - _player.X;
        int deltaY = snipe.Y - _player.Y;

        // Handle wrapping
        if (deltaX > _map.MapWidth / 2)
            deltaX -= _map.MapWidth;
        else if (deltaX < -_map.MapWidth / 2)
            deltaX += _map.MapWidth;

        if (deltaY > _map.MapHeight / 2)
            deltaY -= _map.MapHeight;
        else if (deltaY < -_map.MapHeight / 2)
            deltaY += _map.MapHeight;

        int viewportX = (frameWidth / 2) + deltaX;
        int viewportY = (frameHeight / 2) + deltaY;

        if (viewportX >= 0 && viewportX < frameWidth &&
            viewportY >= 0 && viewportY < frameHeight)
        {
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));

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
                char mapChar = _map.FullMap[(snipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight]
                    [(snipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth];
                Application.Driver.Move(charViewportX, viewportY + StatusBarHeight);
                Application.Driver.AddRune(mapChar);
            }

            // Clear arrow position if within viewport
            if (arrowViewportX >= 0 && arrowViewportX < frameWidth)
            {
                // Calculate world coordinates for arrow position
                int arrowWorldX = snipe.X + (snipe.DirectionX < 0 ? -1 : 1);
                int arrowWorldY = snipe.Y;

                // Wrap coordinates
                arrowWorldX = (arrowWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                arrowWorldY = (arrowWorldY % _map.MapHeight + _map.MapHeight) % _map.MapHeight;

                if (arrowWorldY >= 0 && arrowWorldY < _map.MapHeight &&
                    arrowWorldX >= 0 && arrowWorldX < _map.MapWidth)
                {
                    char arrowMapChar = _map.FullMap[arrowWorldY][arrowWorldX];
                    Application.Driver.Move(arrowViewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddRune(arrowMapChar);
                }
            }

            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        }
    }

    private void ClearHivePosition(Hive hive)
    {
        if (Application.Driver == null) return;

        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // Calculate viewport coordinates for the hive
        int deltaX = hive.X - _player.X;
        int deltaY = hive.Y - _player.Y;

        // Handle wrapping
        if (deltaX > _map.MapWidth / 2)
            deltaX -= _map.MapWidth;
        else if (deltaX < -_map.MapWidth / 2)
            deltaX += _map.MapWidth;

        if (deltaY > _map.MapHeight / 2)
            deltaY -= _map.MapHeight;
        else if (deltaY < -_map.MapHeight / 2)
            deltaY += _map.MapHeight;

        int hiveViewportX = (frameWidth / 2) + deltaX;
        int hiveViewportY = (frameHeight / 2) + deltaY;

        // Clear all 4 corners of the 2x2 hive
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));

        // Top-left corner
        if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
            hiveViewportY >= 0 && hiveViewportY < frameHeight)
        {
            int worldX = (hive.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int worldY = (hive.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
            char mapChar = _map.FullMap[worldY][worldX];
            Application.Driver.Move(hiveViewportX, hiveViewportY + StatusBarHeight);
            Application.Driver.AddRune(mapChar);
        }

        // Top-right corner
        int topRightX = hiveViewportX + 1;
        if (topRightX >= 0 && topRightX < frameWidth &&
            hiveViewportY >= 0 && hiveViewportY < frameHeight)
        {
            int worldX = ((hive.X + 1) % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int worldY = (hive.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
            char mapChar = _map.FullMap[worldY][worldX];
            Application.Driver.Move(topRightX, hiveViewportY + StatusBarHeight);
            Application.Driver.AddRune(mapChar);
        }

        // Bottom-left corner
        int bottomLeftY = hiveViewportY + 1;
        if (hiveViewportX >= 0 && hiveViewportX < frameWidth &&
            bottomLeftY >= 0 && bottomLeftY < frameHeight)
        {
            int worldX = (hive.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int worldY = ((hive.Y + 1) % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
            char mapChar = _map.FullMap[worldY][worldX];
            Application.Driver.Move(hiveViewportX, bottomLeftY + StatusBarHeight);
            Application.Driver.AddRune(mapChar);
        }

        // Bottom-right corner
        if (topRightX >= 0 && topRightX < frameWidth &&
            bottomLeftY >= 0 && bottomLeftY < frameHeight)
        {
            int worldX = ((hive.X + 1) % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
            int worldY = ((hive.Y + 1) % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
            char mapChar = _map.FullMap[worldY][worldX];
            Application.Driver.Move(topRightX, bottomLeftY + StatusBarHeight);
            Application.Driver.AddRune(mapChar);
        }

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
    }

    private (int x, int y) FindRandomValidPosition()
    {
        Random random = new Random();
        const int MAX_ATTEMPTS = 1000; // Prevent infinite loop

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            // Pick a random position on the map
            // Player is 2 columns wide, so X must be at least 1 from the right edge
            // Player is 3 rows tall, so Y must be at least 2 from the bottom edge
            int x = random.Next(0, _map.MapWidth - 1); // -1 because we need 2 columns
            int y = random.Next(0, _map.MapHeight - 2); // -2 because we need 3 rows

            // Check if all 6 cells (2x3) at this position are walkable
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
        // Check if all 6 cells (2 columns x 3 rows) starting at (x, y) are walkable
        // Player occupies: columns [x, x+1], rows [y, y+1, y+2]

        // Bounds check
        if (x < 0 || x + 1 >= _map.MapWidth || y < 0 || y + 2 >= _map.MapHeight)
            return false;

        // Check all 6 cells are spaces (walkable)
        for (int row = y; row <= y + 2; row++)
        {
            for (int col = x; col <= x + 1; col++)
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
        
        _player.Lives = 5;
        _player.Score = 0;
        _player.IsAlive = true;
        _player.Initials = _config.Initials; // Ensure initials are current
        
        // Reset game state
        _gameState.Level = 1;
        _gameState.Score = 0;
        _gameState.TotalHives = 0;
        _gameState.HivesUndestroyed = 0;
        _gameState.TotalSnipes = 0;
        _gameState.SnipesUndestroyed = 0;
        
        // Clear all game entities
        _bullets.Clear();
        _hives.Clear();
        _snipes.Clear();
        
        // Only host initializes hives - clients will receive them via MQTT
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
        _cachedHivesUndestroyed = -1;
        _cachedSnipesUndestroyed = -1;
        _cachedLives = -1;
        _cachedLevel = -1;
        _cachedScore = -1;
        _previousPlayerCellX = -1;
        _previousPlayerCellY = -1;
        _previousPlayerViewportX = -1;
        _previousPlayerViewportY = -1;
    }

    private void InitializeHives()
    {
        _hives.Clear();
        _snipes.Clear();
        int hiveCount = _gameState.GetHiveCountForLevel(_gameState.Level);
        _gameState.TotalHives = hiveCount;
        _gameState.HivesUndestroyed = hiveCount;
        _gameState.TotalSnipes = hiveCount * Hive.SnipesPerHive;
        _gameState.SnipesUndestroyed = _gameState.TotalSnipes;

        for (int i = 0; i < hiveCount; i++)
        {
            var (x, y) = FindRandomValidHivePosition();
            _hives.Add(new Hive(x, y));
        }
    }

    private (int x, int y) FindRandomValidHivePosition()
    {
        Random random = new Random();
        const int MAX_ATTEMPTS = 1000;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            // Hive is 2x2, so we need space for that
            int x = random.Next(0, _map.MapWidth - 1); // -1 because we need 2 columns
            int y = random.Next(0, _map.MapHeight - 1); // -1 because we need 2 rows

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

        // Check that hive doesn't overlap with player (player is 2x3)
        // Player occupies: columns [player.X, player.X+1], rows [player.Y, player.Y+1, player.Y+2]
        if (x >= _player.X - 1 && x <= _player.X + 1 && y >= _player.Y - 1 && y <= _player.Y + 2)
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
        if (Application.Driver == null)
            return;

        // Performance optimization: Only redraw if values changed
        int currentFPS = (int)Math.Round(_currentFPS);
        if (_cachedHivesUndestroyed == _gameState.HivesUndestroyed &&
            _cachedSnipesUndestroyed == _gameState.SnipesUndestroyed &&
            _cachedLives == _player.Lives &&
            _cachedLevel == _gameState.Level &&
            _cachedScore == _gameState.Score &&
            _cachedFPS == currentFPS)
        {
            return; // No changes, skip redraw
        }

        int currentWidth = Application.Driver.Cols;

        // Set status bar color: white text on blue background
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));

        // Draw status bar with hive shapes
        Application.Driver.Move(0, 0);

        // Draw hive indicator (small box shape) with fixed color (cyan - first hive color)
        // Using fixed color to reduce status bar updates
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Cyan, Color.Blue));
        Application.Driver.AddStr("╔╗"); // Top corners of hive

        // Reset to status bar color and position
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        Application.Driver.AddStr($" {_gameState.HivesUndestroyed}/{_gameState.TotalHives}  ");

        // Draw snipes count
        Application.Driver.AddStr($"Snipes: {_gameState.SnipesUndestroyed}/{_gameState.TotalSnipes}  ");

        // Draw lives
        Application.Driver.AddStr($"Lives: {_player.Lives}  ");

        // Draw level
        Application.Driver.AddStr($"Level: {_gameState.Level}  ");

        // Draw score
        Application.Driver.AddStr($"Score: {_gameState.Score}  ");

        // Draw FPS (currentFPS already calculated at top of method)
        Application.Driver.AddStr($"FPS: {currentFPS}");

        // Clear rest of first row
        int currentPos = Application.Driver.Col;
        if (currentPos < currentWidth)
        {
            Application.Driver.AddStr(new string(' ', currentWidth - currentPos));
        }

        // Draw second row with bottom of hive
        Application.Driver.Move(0, 1);
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Cyan, Color.Blue));
        Application.Driver.AddStr("╚╝");
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        Application.Driver.AddStr(new string(' ', currentWidth - 2));

        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));

        // Cache current values
        _cachedHivesUndestroyed = _gameState.HivesUndestroyed;
        _cachedSnipesUndestroyed = _gameState.SnipesUndestroyed;
        _cachedLives = _player.Lives;
        _cachedLevel = _gameState.Level;
        _cachedScore = _gameState.Score;
        _cachedFPS = currentFPS;
    }

    private void DrawPlayer()
    {
        int currentWidth = Application.Driver!.Cols;
        int currentHeight = Application.Driver!.Rows;

        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);

        // draw the player
        // _player.X, _player.Y represents top-left corner of player
        // Map.GetMap centers viewport on (_player.X, _player.Y)
        // So top-left in viewport is at (frameWidth/2, frameHeight/2)
        // Offset by StatusBarHeight to account for status bar
        int topLeftCol = frameWidth / 2;
        int topLeftRow = (frameHeight / 2) + StatusBarHeight;

        // Cache DateTime to avoid multiple system calls
        if ((DateTime.Now - _cachedDateTime).TotalMilliseconds > 10)
        {
            _cachedDateTime = DateTime.Now;
        }

        var eyes = _cachedDateTime.Millisecond < 500 ? "ÔÔ" : "OO";
        var mouth = _cachedDateTime.Millisecond < 500 ? "◄►" : "◂▸";

        Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        Application.Driver!.Move(topLeftCol, topLeftRow);
        Application.Driver!.AddStr(eyes);
        Application.Driver!.Move(topLeftCol, topLeftRow + 1);
        Application.Driver!.AddStr(mouth);
        Application.Driver!.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        Application.Driver!.Move(topLeftCol, topLeftRow + 2);
        Application.Driver!.AddStr(_player.Initials);
    }
    
    private void DrawRemotePlayers()
    {
        if (!_isMultiplayer || _gameSession == null || Application.Driver == null)
            return;
        
        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
        
        foreach (var networkPlayer in _networkPlayers.Values)
        {
            if (networkPlayer.IsLocal)
                continue; // Skip local player (already drawn)
            
            // Calculate delta between remote player and local player world positions, handling wrapping
            int deltaX = networkPlayer.X - _player.X;
            int deltaY = networkPlayer.Y - _player.Y;
            
            // Adjust delta for map wrapping to find shortest path
            if (deltaX > _map.MapWidth / 2) deltaX -= _map.MapWidth;
            else if (deltaX < -_map.MapWidth / 2) deltaX += _map.MapWidth;
            
            if (deltaY > _map.MapHeight / 2) deltaY -= _map.MapHeight;
            else if (deltaY < -_map.MapHeight / 2) deltaY += _map.MapHeight;
            
            // Convert to viewport coordinates
            int viewportX = frameWidth / 2 + deltaX;
            int viewportY = frameHeight / 2 + deltaY;
            
            // Only draw if within viewport (2x3 player area)
            if (viewportX + 2 > 0 && viewportX < frameWidth &&
                viewportY + 3 > 0 && viewportY < frameHeight)
            {
                // Draw remote player (different color to distinguish from local)
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Yellow, Color.Black));
                
                // Draw eyes (same as local player but different color)
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY >= 0 && viewportY < frameHeight)
                {
                    var eyes = DateTime.Now.Millisecond < 500 ? "ÔÔ" : "OO";
                    Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddStr(eyes);
                }
                
                // Draw mouth
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 1 >= 0 && viewportY + 1 < frameHeight)
                {
                    var mouth = DateTime.Now.Millisecond < 500 ? "◄►" : "◂▸";
                    Application.Driver.Move(viewportX, viewportY + 1 + StatusBarHeight);
                    Application.Driver.AddStr(mouth);
                }
                
                // Draw initials
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 2 >= 0 && viewportY + 2 < frameHeight)
                {
                    Application.Driver.Move(viewportX, viewportY + 2 + StatusBarHeight);
                    Application.Driver.AddStr(networkPlayer.Initials);
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
        
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
    }
    
    private void DrawRemotePlayersWithClearing()
    {
        if (!_isMultiplayer || _gameSession == null || Application.Driver == null)
            return;
        
        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : (currentHeight - StatusBarHeight);
        
        // Get map viewport for clearing
        var map = _cachedMapViewport;
        if (map == null || map.Length != frameHeight)
        {
            map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);
        }
        
        foreach (var networkPlayer in _networkPlayers.Values)
        {
            if (networkPlayer.IsLocal)
                continue; // Skip local player (already drawn)
            
            // Calculate delta between remote player and local player world positions, handling wrapping
            int deltaX = networkPlayer.X - _player.X;
            int deltaY = networkPlayer.Y - _player.Y;
            
            // Adjust delta for map wrapping to find shortest path
            if (deltaX > _map.MapWidth / 2) deltaX -= _map.MapWidth;
            else if (deltaX < -_map.MapWidth / 2) deltaX += _map.MapWidth;
            
            if (deltaY > _map.MapHeight / 2) deltaY -= _map.MapHeight;
            else if (deltaY < -_map.MapHeight / 2) deltaY += _map.MapHeight;
            
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
                
                // Clear previous position (2x3 area) - but only if it's different from current
                int currentViewportX = frameWidth / 2 + deltaX;
                int currentViewportY = frameHeight / 2 + deltaY;
                
                if ((prevViewportX != currentViewportX || prevViewportY != currentViewportY) &&
                    prevViewportX + 2 > 0 && prevViewportX < frameWidth &&
                    prevViewportY + 3 > 0 && prevViewportY < frameHeight &&
                    map != null)
                {
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.Black));
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
                                Application.Driver.Move(clearX, clearY + StatusBarHeight);
                                Application.Driver.AddRune(mapChar);
                            }
                        }
                    }
                }
            }
            
            // Convert to viewport coordinates
            int viewportX = frameWidth / 2 + deltaX;
            int viewportY = frameHeight / 2 + deltaY;
            
            // Only draw if within viewport (2x3 player area)
            if (viewportX + 2 > 0 && viewportX < frameWidth &&
                viewportY + 3 > 0 && viewportY < frameHeight)
            {
                // Draw remote player (different color to distinguish from local)
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Yellow, Color.Black));
                
                // Draw eyes (same as local player but different color)
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY >= 0 && viewportY < frameHeight)
                {
                    var eyes = DateTime.Now.Millisecond < 500 ? "ÔÔ" : "OO";
                    Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddStr(eyes);
                }
                
                // Draw mouth
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 1 >= 0 && viewportY + 1 < frameHeight)
                {
                    var mouth = DateTime.Now.Millisecond < 500 ? "◄►" : "◂▸";
                    Application.Driver.Move(viewportX, viewportY + 1 + StatusBarHeight);
                    Application.Driver.AddStr(mouth);
                }
                
                // Draw initials
                if (viewportX >= 0 && viewportX + 1 < frameWidth && viewportY + 2 >= 0 && viewportY + 2 < frameHeight)
                {
                    Application.Driver.Move(viewportX, viewportY + 2 + StatusBarHeight);
                    Application.Driver.AddStr(networkPlayer.Initials);
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
        
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
    }
    
    private async Task StartMultiplayerGame(int maxPlayers)
    {
        try
        {
            // Create MQTT client
            _mqttClient = new MqttGameClient();
            _mqttClient.OnMessageReceived += HandleMqttMessage;
            _mqttClient.OnConnected += () =>
            {
                // Connection successful
            };
            _mqttClient.OnConnectionError += (error) =>
            {
                // Handle connection error - show message to user
                // For now, just log or handle silently
            };
            
            // Connect to broker
            bool connected = await _mqttClient.ConnectAsync();
            if (!connected)
            {
                // Failed to connect - return to menu
                _introScreen.Show();
                return;
            }
            
            // Create game session
            _gameSession = new GameSession
            {
                GameId = GameSession.GenerateGameId(),
                PlayerId = GameSession.GeneratePlayerId(),
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
            
            // Publish game to active games list
            var gameInfo = new GameInfoMessage
            {
                GameId = _gameSession.GameId,
                HostPlayerId = _gameSession.PlayerId,
                HostInitials = _player.Initials,
                MaxPlayers = maxPlayers,
                CurrentPlayers = 1,
                Status = "waiting",
                Level = _gameState.Level,
                CreatedAt = _gameSession.CreatedAt
            };
            
            await _mqttClient.PublishJsonAsync($"nsnipes/games/active", gameInfo, retain: true);
            await _mqttClient.PublishJsonAsync($"nsnipes/games/{_gameSession.GameId}/info", gameInfo, retain: true);
            
            // Subscribe to join requests
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/join");
            
            // Show waiting screen
            _introScreen.ShowWaitingForPlayers(_gameSession.GameId, maxPlayers, isHost: true);
            
            // Start timer to publish player count updates
            Application.AddTimeout(TimeSpan.FromSeconds(1), () =>
            {
                if (_gameSession != null && _gameSession.Status == GameSessionStatus.WaitingForPlayers)
                {
                    PublishPlayerCountUpdate();
                    return true;
                }
                return false; // Stop timer when game starts
            });
            
            // Start timer to check if we should start game (60 seconds or max players)
            Application.AddTimeout(TimeSpan.FromSeconds(60), () =>
            {
                if (_gameSession != null && _gameSession.Status == GameSessionStatus.WaitingForPlayers)
                {
                    StartMultiplayerGameSession();
                }
                return false; // One-time timer
            });
        }
        catch (Exception)
        {
            // Handle error - return to menu
            _introScreen.Show();
        }
    }
    
    private async Task JoinGame(string gameId)
    {
        try
        {
            // Create MQTT client
            _mqttClient = new MqttGameClient();
            _mqttClient.OnMessageReceived += HandleMqttMessage;
            
            // Connect to broker
            bool connected = await _mqttClient.ConnectAsync();
            if (!connected)
            {
                _introScreen.Show();
                return;
            }
            
            // Create game session
            _gameSession = new GameSession
            {
                GameId = gameId.ToUpper(),
                PlayerId = GameSession.GeneratePlayerId(),
                Role = GameSessionRole.Client,
                Status = GameSessionStatus.WaitingForPlayers
            };
            
            _isMultiplayer = true;
            
            // Subscribe to game info and join responses
            await _mqttClient.SubscribeAsync($"nsnipes/games/{_gameSession.GameId}/info");
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/join");
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/players/joined");
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/players/count");
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/start");
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/state"); // Game state snapshot
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/snipes"); // Snipe updates
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/hives"); // Hive updates
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/player/+/position"); // Player positions
            await _mqttClient.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/player/+/bullet"); // Bullet updates
            
            // Publish join request
            var joinRequest = new JoinRequestMessage
            {
                PlayerId = _gameSession.PlayerId,
                Initials = _player.Initials,
                Timestamp = DateTime.UtcNow
            };
            
            await _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/join", joinRequest);
            
            // Show waiting screen
            _introScreen.ShowWaitingForPlayers(_gameSession.GameId, 1, isHost: false);
        }
        catch (Exception)
        {
            _introScreen.Show();
        }
    }
    
    private void HandleMqttMessage(string topic, string payload)
    {
        try
        {
            if (_gameSession == null)
                return;
            
            // Handle join requests (host only)
            if (topic == $"nsnipes/game/{_gameSession.GameId}/join" && _gameSession.Role == GameSessionRole.Host)
            {
                var joinRequest = JsonSerializer.Deserialize<JoinRequestMessage>(payload);
                if (joinRequest != null && _gameSession.Status == GameSessionStatus.WaitingForPlayers)
                {
                    // Check if player already joined
                    if (_gameSession.Players.Any(p => p.PlayerId == joinRequest.PlayerId))
                        return;
                    
                    // Check if we have room
                    if (_gameSession.CurrentPlayers >= _gameSession.MaxPlayers)
                    {
                        // Send rejection
                        var rejectResponse = new JoinResponseMessage
                        {
                            Accepted = false,
                            ErrorMessage = "Game is full"
                        };
                        _mqttClient?.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/join", rejectResponse);
                        return;
                    }
                    
                    // Accept player
                    _gameSession.CurrentPlayers++;
                    int playerNumber = _gameSession.CurrentPlayers;
                    
                    var playerInfo = new NetworkPlayerInfo
                    {
                        PlayerId = joinRequest.PlayerId,
                        Initials = joinRequest.Initials,
                        PlayerNumber = playerNumber
                    };
                    _gameSession.Players.Add(playerInfo);
                    
                    // Send acceptance
                    var acceptResponse = new JoinResponseMessage
                    {
                        Accepted = true,
                        PlayerId = joinRequest.PlayerId,
                        PlayerNumber = playerNumber
                    };
                    _mqttClient?.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/join", acceptResponse, retain: true);
                    
                    // Publish join notification
                    var joinNotification = new PlayerJoinNotificationMessage
                    {
                        PlayerId = joinRequest.PlayerId,
                        Initials = joinRequest.Initials,
                        PlayerNumber = playerNumber,
                        CurrentPlayers = _gameSession.CurrentPlayers,
                        MaxPlayers = _gameSession.MaxPlayers,
                        Timestamp = DateTime.UtcNow
                    };
                    _mqttClient?.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/players/joined", joinNotification);
                    
                    // Update UI
                    _introScreen.UpdatePlayerJoin(joinRequest.Initials);
                    
                    // Check if we should start (max players reached)
                    if (_gameSession.CurrentPlayers >= _gameSession.MaxPlayers)
                    {
                        StartMultiplayerGameSession();
                    }
                }
            }
            // Handle join response (client only)
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/join" && _gameSession.Role == GameSessionRole.Client)
            {
                var response = JsonSerializer.Deserialize<JoinResponseMessage>(payload);
                if (response != null && response.Accepted && response.PlayerId == _gameSession.PlayerId)
                {
                    // Join accepted - wait for game start
                }
            }
            // Handle player join notifications
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/players/joined")
            {
                var notification = JsonSerializer.Deserialize<PlayerJoinNotificationMessage>(payload);
                if (notification != null)
                {
                    _introScreen.UpdatePlayerJoin(notification.Initials);
                }
            }
            // Handle player count updates
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/players/count")
            {
                var update = JsonSerializer.Deserialize<PlayerCountUpdateMessage>(payload);
                if (update != null)
                {
                    _introScreen.UpdatePlayerCount(update.CurrentPlayers, update.MaxPlayers, update.TimeRemaining);
                }
            }
            // Handle game start
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/start")
            {
                var startMessage = JsonSerializer.Deserialize<GameStartMessage>(payload);
                if (startMessage != null)
                {
                    _gameSession.Status = GameSessionStatus.Starting;
                    
                    // Initialize network players from game session
                    foreach (var playerId in startMessage.Players)
                    {
                        var playerInfo = _gameSession.Players.FirstOrDefault(p => p.PlayerId == playerId);
                        if (playerInfo != null)
                        {
                            var networkPlayer = new PlayerNetwork(
                                playerInfo.PlayerId,
                                playerInfo.Initials,
                                playerInfo.PlayerNumber,
                                isLocal: playerInfo.PlayerId == _gameSession.PlayerId
                            );
                            
                            // Initial position will be set from game state or spawn message
                            _networkPlayers[playerInfo.PlayerId] = networkPlayer;
                            
                            if (networkPlayer.IsLocal)
                            {
                                // Ensure local player initials match
                                _player.Initials = playerInfo.Initials;
                                // Local player position will be set from game state
                            }
                        }
                    }
                    
                    _introScreen.StartGame();
                }
            }
            // Handle game state updates (clients receive from host)
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/state")
            {
                HandleGameStateUpdate(payload);
            }
            // Handle snipe updates (clients receive from host)
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/snipes")
            {
                HandleSnipeUpdates(payload);
            }
            // Handle hive updates (clients receive from host)
            else if (topic == $"nsnipes/game/{_gameSession.GameId}/hives")
            {
                HandleHiveUpdates(payload);
            }
            // Handle player position updates
            else if (topic.StartsWith($"nsnipes/game/{_gameSession.GameId}/player/") && topic.EndsWith("/position"))
            {
                var posUpdate = JsonSerializer.Deserialize<PlayerPositionUpdateMessage>(payload);
                // IMPORTANT: posUpdate.X and posUpdate.Y are WORLD/MAP coordinates (not viewport)
                // Update position for ALL players (including host), not just remote ones
                if (posUpdate != null)
                {
                    // Update remote player position (or host position if we're a client)
                    if (_networkPlayers.TryGetValue(posUpdate.PlayerId, out var networkPlayer))
                    {
                        // Store world coordinates - conversion to viewport happens in DrawRemotePlayers()
                        networkPlayer.UpdatePosition(posUpdate.X, posUpdate.Y, posUpdate.Sequence);
                    }
                    else
                    {
                        // New player - find their info from game session
                        var sessionPlayerInfo = _gameSession.Players.FirstOrDefault(p => p.PlayerId == posUpdate.PlayerId);
                        if (sessionPlayerInfo != null)
                        {
                            var isLocalPlayer = posUpdate.PlayerId == _gameSession.PlayerId;
                            var newNetworkPlayer = new PlayerNetwork(
                                posUpdate.PlayerId,
                                sessionPlayerInfo.Initials,
                                sessionPlayerInfo.PlayerNumber,
                                isLocal: isLocalPlayer
                            );
                            // Set initial previous position to current position to avoid clearing artifacts on first draw
                            newNetworkPlayer.PreviousX = posUpdate.X;
                            newNetworkPlayer.PreviousY = posUpdate.Y;
                            newNetworkPlayer.UpdatePosition(posUpdate.X, posUpdate.Y, posUpdate.Sequence);
                            _networkPlayers[posUpdate.PlayerId] = newNetworkPlayer;
                            
                            // If this is actually the local player, update _player position and initials
                            if (newNetworkPlayer.IsLocal)
                            {
                                _player.X = posUpdate.X;
                                _player.Y = posUpdate.Y;
                                _player.Initials = sessionPlayerInfo.Initials;
                            }
                        }
                        else
                        {
                            // Player not in session yet - might be host, create with default info
                            // This should be rare and will be updated from game state snapshot
                            var newNetworkPlayer = new PlayerNetwork(
                                posUpdate.PlayerId,
                                "??", // Unknown initials - will be updated from game state
                                0, // Unknown number
                                isLocal: posUpdate.PlayerId == _gameSession.PlayerId  // Check if this is actually the local player
                            );
                            newNetworkPlayer.PreviousX = posUpdate.X;
                            newNetworkPlayer.PreviousY = posUpdate.Y;
                            newNetworkPlayer.UpdatePosition(posUpdate.X, posUpdate.Y, posUpdate.Sequence);
                            _networkPlayers[posUpdate.PlayerId] = newNetworkPlayer;
                            
                            // If this is actually the local player, update _player position
                            if (newNetworkPlayer.IsLocal)
                            {
                                _player.X = posUpdate.X;
                                _player.Y = posUpdate.Y;
                            }
                        }
                    }
                }
            }
            // Handle bullet fired/updates
            else if (topic.StartsWith($"nsnipes/game/{_gameSession.GameId}/player/") && topic.EndsWith("/bullet"))
            {
                var bulletMsg = JsonSerializer.Deserialize<BulletUpdateMessage>(payload);
                if (bulletMsg != null)
                {
                    HandleBulletMessage(bulletMsg);
                }
            }
        }
        catch (Exception)
        {
            // Handle error silently or log
        }
    }
    
    private void HandleBulletMessage(BulletUpdateMessage msg)
    {
        if (msg.Action == "fired")
        {
            // Add remote bullet to our list
            var bullet = new Bullet(msg.X, msg.Y, msg.VelocityX, msg.VelocityY, msg.BulletId, msg.PlayerId);
            _bullets.Add(bullet);
        }
        else if (msg.Action == "updated")
        {
            // Update existing bullet
            var bullet = _bullets.FirstOrDefault(b => b.BulletId == msg.BulletId);
            if (bullet != null)
            {
                bullet.X = msg.X;
                bullet.Y = msg.Y;
                bullet.VelocityX = msg.VelocityX;
                bullet.VelocityY = msg.VelocityY;
            }
        }
        else if (msg.Action == "expired" || msg.Action == "hit")
        {
            // Remove bullet
            _bullets.RemoveAll(b => b.BulletId == msg.BulletId);
        }
    }
    
    private void PublishPlayerPosition()
    {
        if (_gameSession == null || _mqttClient == null || !_isMultiplayer)
            return;
        
        // Throttle position updates to avoid flooding, but allow more frequent updates
        // Reduce throttling to 20ms for smoother movement
        if ((DateTime.Now - _lastPositionPublish).TotalMilliseconds < 20)
            return;
        
        _positionSequence++;
        // IMPORTANT: All coordinates in MQTT messages must be WORLD/MAP coordinates, not viewport coordinates
        // _player.X and _player.Y are world coordinates (0 to MapWidth/MapHeight)
        var posUpdate = new PlayerPositionUpdateMessage
        {
            PlayerId = _gameSession.PlayerId,
            X = _player.X,  // World coordinate (map space)
            Y = _player.Y,  // World coordinate (map space)
            Timestamp = DateTime.UtcNow,
            Sequence = _positionSequence
        };
        
        // Use fire-and-forget for position updates (QoS 0) for lower latency
        _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/player/{_gameSession.PlayerId}/position", posUpdate);
        _lastPositionPublish = DateTime.Now;
    }
    
    private void PublishBulletUpdate(Bullet bullet, string action, string? hitType = null, string? hitTargetId = null)
    {
        if (_gameSession == null || _mqttClient == null)
            return;
        
        var bulletMsg = new BulletUpdateMessage
        {
            BulletId = bullet.BulletId,
            PlayerId = bullet.PlayerId,
            X = bullet.X,
            Y = bullet.Y,
            VelocityX = bullet.VelocityX,
            VelocityY = bullet.VelocityY,
            Timestamp = DateTime.UtcNow,
            Action = action,
            HitType = hitType,
            HitTargetId = hitTargetId
        };
        
        _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/player/{_gameSession.PlayerId}/bullet", bulletMsg);
    }
    
    private void PublishPlayerCountUpdate()
    {
        if (_gameSession == null || _mqttClient == null || _gameSession.Role != GameSessionRole.Host)
            return;
        
        int elapsed = (int)(DateTime.UtcNow - _gameSession.CreatedAt).TotalSeconds;
        int timeRemaining = Math.Max(0, 60 - elapsed);
        
        var update = new PlayerCountUpdateMessage
        {
            CurrentPlayers = _gameSession.CurrentPlayers,
            MaxPlayers = _gameSession.MaxPlayers,
            Players = _gameSession.Players.Select(p => new PlayerInfo
            {
                PlayerId = p.PlayerId,
                Initials = p.Initials,
                PlayerNumber = p.PlayerNumber
            }).ToList(),
            TimeRemaining = timeRemaining,
            Timestamp = DateTime.UtcNow
        };
        
        _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/players/count", update);
    }
    
    private async void StartMultiplayerGameSession()
    {
        if (_gameSession == null || _gameSession.Role != GameSessionRole.Host)
            return;
        
        _gameSession.Status = GameSessionStatus.Starting;
        _gameSession.StartTime = DateTime.UtcNow;
        
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
        
        // Subscribe to all player position updates (wildcard)
        await _mqttClient?.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/player/+/position")!;
        await _mqttClient?.SubscribeAsync($"nsnipes/game/{_gameSession.GameId}/player/+/bullet")!;
        
        // Publish game start message
        var startMessage = new GameStartMessage
        {
            GameId = _gameSession.GameId,
            Level = _gameState.Level,
            Players = _gameSession.Players.Select(p => p.PlayerId).ToList(),
            StartTime = _gameSession.StartTime.Value
        };
        
        await _mqttClient?.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/start", startMessage)!;
        
        // Remove from active games
        await _mqttClient?.PublishAsync($"nsnipes/games/active", "", retain: true)!; // Clear retained message
        
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
            // Small delay to ensure subscriptions are active, then publish game state
            Application.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
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
        Random random = new Random();
        const int MAX_ATTEMPTS = 1000;
        
        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            int x = random.Next(0, _map.MapWidth - 1);
            int y = random.Next(0, _map.MapHeight - 2);
            
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
        // Check if position overlaps with any existing network player (2x3 area)
        foreach (var networkPlayer in _networkPlayers.Values)
        {
            // Player occupies: [X, X+1] columns, [Y, Y+1, Y+2] rows
            if (!(x + 2 <= networkPlayer.X || x >= networkPlayer.X + 2 ||
                  y + 3 <= networkPlayer.Y || y >= networkPlayer.Y + 3))
            {
                return true; // Overlaps
            }
        }
        return false;
    }
    
    private void PublishBulletFired(Bullet bullet)
    {
        if (_gameSession == null || _mqttClient == null)
            return;
        
        var bulletMsg = new BulletUpdateMessage
        {
            BulletId = bullet.BulletId,
            PlayerId = bullet.PlayerId,
            X = bullet.X,
            Y = bullet.Y,
            VelocityX = bullet.VelocityX,
            VelocityY = bullet.VelocityY,
            Timestamp = bullet.CreatedAt,
            Action = "fired"
        };
        
        _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/player/{_gameSession.PlayerId}/bullet", bulletMsg);
    }
    
    private void HandleGameStateUpdate(string payload)
    {
        // Client receives game state snapshot from host
        try
        {
            var state = JsonSerializer.Deserialize<GameStateSnapshotMessage>(payload);
            if (state != null)
            {
                // Update game state
                _gameState.Level = state.Level;
                
                // Update hives from host
                _hives.Clear();
                foreach (var hiveState in state.Hives)
                {
                    var hive = new Hive(hiveState.X, hiveState.Y)
                    {
                        Hits = hiveState.Hits,
                        IsDestroyed = hiveState.IsDestroyed,
                        SnipesRemaining = hiveState.SnipesRemaining,
                        FlashIntervalMs = hiveState.FlashIntervalMs
                    };
                    _hives.Add(hive);
                }
                _gameState.TotalHives = state.Hives.Count;
                _gameState.HivesUndestroyed = state.Hives.Count(h => !h.IsDestroyed);
                
                // Update snipes from host
                // IMPORTANT: snipeState.X and snipeState.Y are WORLD/MAP coordinates (0 to MapWidth/MapHeight)
                _snipes.Clear();
                foreach (var snipeState in state.Snipes)
                {
                    if (snipeState.IsAlive)
                    {
                        var snipe = new Snipe(snipeState.X, snipeState.Y, snipeState.Type)  // World coordinates
                        {
                            DirectionX = snipeState.DirectionX,
                            DirectionY = snipeState.DirectionY,
                            IsAlive = snipeState.IsAlive
                        };
                        _snipes.Add(snipe);
                    }
                }
                _gameState.TotalSnipes = state.Snipes.Count;
                _gameState.SnipesUndestroyed = state.Snipes.Count(s => s.IsAlive);
                
                // Update player states
                // IMPORTANT: playerState.X and playerState.Y are WORLD/MAP coordinates (0 to MapWidth/MapHeight)
                foreach (var playerState in state.Players)
                {
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
                            _cachedMapViewport = null; // Force map redraw
                        }
                    }
                    else
                    {
                        // New player not in our network players list - create them
                        var playerInfo = _gameSession.Players.FirstOrDefault(p => p.PlayerId == playerState.PlayerId);
                        if (playerInfo != null)
                        {
                            var newNetworkPlayer = new PlayerNetwork(
                                playerState.PlayerId,
                                !string.IsNullOrEmpty(playerState.Initials) ? playerState.Initials : playerInfo.Initials, // Use state initials if available, otherwise session initials
                                playerInfo.PlayerNumber,
                                isLocal: playerState.PlayerId == _gameSession.PlayerId
                            );
                            newNetworkPlayer.PreviousX = playerState.X;
                            newNetworkPlayer.PreviousY = playerState.Y;
                            newNetworkPlayer.X = playerState.X;
                            newNetworkPlayer.Y = playerState.Y;
                            newNetworkPlayer.Lives = playerState.Lives;
                            newNetworkPlayer.Score = playerState.Score;
                            newNetworkPlayer.IsAlive = playerState.IsAlive;
                            _networkPlayers[playerState.PlayerId] = newNetworkPlayer;
                            
                            if (newNetworkPlayer.IsLocal)
                            {
                                _player.X = playerState.X;
                                _player.Y = playerState.Y;
                                _player.Lives = playerState.Lives;
                                _player.Score = playerState.Score;
                                _player.IsAlive = playerState.IsAlive;
                                // Update local player initials from state
                                if (!string.IsNullOrEmpty(playerState.Initials))
                                {
                                    _player.Initials = playerState.Initials;
                                }
                                _cachedMapViewport = null;
                            }
                        }
                    }
                }
                
                // Force redraw
                _mapDrawn = false;
            }
        }
        catch
        {
            // Handle error silently
        }
    }
    
    private void HandleSnipeUpdates(string payload)
    {
        // Client receives snipe updates from host
        try
        {
            var updates = JsonSerializer.Deserialize<SnipeUpdatesMessage>(payload);
            if (updates != null)
            {
                foreach (var update in updates.Updates)
                {
                    if (update.Action == "spawned")
                    {
                        // Spawn new snipe
                        var snipe = new Snipe(update.X, update.Y, update.Type ?? 'A')
                        {
                            DirectionX = update.DirectionX,
                            DirectionY = update.DirectionY
                        };
                        _snipes.Add(snipe);
                        _gameState.SnipesUndestroyed++;
                    }
                    else if (update.Action == "moved")
                    {
                        // Update existing snipe position (find by approximate position)
                        var snipe = _snipes.FirstOrDefault(s => 
                            Math.Abs(s.X - update.X) <= 1 && Math.Abs(s.Y - update.Y) <= 1 && s.IsAlive);
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
                        // Remove snipe
                        var snipe = _snipes.FirstOrDefault(s => 
                            Math.Abs(s.X - update.X) <= 1 && Math.Abs(s.Y - update.Y) <= 1 && s.IsAlive);
                        if (snipe != null)
                        {
                            snipe.IsAlive = false;
                            _snipes.Remove(snipe);
                            _gameState.SnipesUndestroyed--;
                        }
                    }
                }
            }
        }
        catch
        {
            // Handle error silently
        }
    }
    
    private void HandleHiveUpdates(string payload)
    {
        // Client receives hive updates from host
        try
        {
            var updates = JsonSerializer.Deserialize<HiveUpdatesMessage>(payload);
            if (updates != null)
            {
                foreach (var update in updates.Updates)
                {
                    // Find hive by position (hive ID is based on position)
                    var hive = _hives.FirstOrDefault(h => 
                        Math.Abs(h.X - int.Parse(update.HiveId.Split('_')[1])) <= 1 &&
                        Math.Abs(h.Y - int.Parse(update.HiveId.Split('_')[2])) <= 1);
                    
                    if (hive != null)
                    {
                        if (update.Action == "hit")
                        {
                            hive.Hits = update.Hits;
                            hive.FlashIntervalMs = update.FlashIntervalMs;
                        }
                        else if (update.Action == "destroyed")
                        {
                            hive.IsDestroyed = true;
                            _gameState.HivesUndestroyed--;
                        }
                    }
                }
            }
        }
        catch
        {
            // Handle error silently
        }
    }
    
    private void PublishGameStateSnapshot()
    {
        if (_gameSession == null || _mqttClient == null || _gameSession.Role != GameSessionRole.Host)
            return;
        
        // IMPORTANT: All coordinates in MQTT messages must be WORLD/MAP coordinates, not viewport coordinates
        // All X/Y values here are world coordinates (0 to MapWidth/MapHeight)
        // Each client will convert these to viewport coordinates based on their own viewport dimensions
        var snapshot = new GameStateSnapshotMessage
        {
            GameId = _gameSession.GameId,
            Level = _gameState.Level,
            Status = "playing",
            Players = _networkPlayers.Values.Select(np => new PlayerStateInfo
            {
                PlayerId = np.PlayerId,
                Initials = np.Initials,
                X = np.X,  // World coordinate (map space)
                Y = np.Y,  // World coordinate (map space)
                Lives = np.Lives,
                Score = np.Score,
                IsAlive = np.IsAlive
            }).ToList(),
            Hives = _hives.Select(h => new HiveStateInfo
            {
                HiveId = $"hive_{h.X}_{h.Y}",
                X = h.X,  // World coordinate (map space)
                Y = h.Y,  // World coordinate (map space)
                Hits = h.Hits,
                IsDestroyed = h.IsDestroyed,
                SnipesRemaining = h.SnipesRemaining,
                FlashIntervalMs = h.FlashIntervalMs
            }).ToList(),
            Snipes = _snipes.Select(s => new SnipeStateInfo
            {
                SnipeId = $"snipe_{s.X}_{s.Y}",
                X = s.X,  // World coordinate (map space)
                Y = s.Y,  // World coordinate (map space)
                Type = s.Type,
                DirectionX = s.DirectionX,
                DirectionY = s.DirectionY,
                IsAlive = s.IsAlive
            }).ToList(),
            Timestamp = DateTime.UtcNow,
            Sequence = 0
        };
        
        _ = _mqttClient.PublishJsonAsync($"nsnipes/game/{_gameSession.GameId}/state", snapshot);
    }

}
