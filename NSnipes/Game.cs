using Terminal.Gui;

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
    
    // Intro screen state
    private bool _inIntroScreen = true;
    private bool _bannerScrolling = true;
    private bool _showMenu = false;
    private bool _clearingScreen = false;
    private DateTime _bannerStartTime;
    private int _bannerScrollPosition = 0;
    private int _clearingRectSize = 0;
    private DateTime _clearingStartTime;
    private string _clearingMessage = "";
    private bool _gameOver = false;
    private bool _waitingForGameOverKey = false;
    
    // Menu state
    private int _selectedMenuIndex = 0;
    private readonly string[] _menuItems = { "Start a New Game", "Join an Existing Game", "Initials", "Exit" };
    private bool _enteringInitials = false;
    private string _initialsInput = "";
    private GameConfig _config;
    
    // NSNIPES banner definition (7 rows tall, each letter is 7 characters wide)
    private static readonly string[] BannerN = new[]
    {
        "█     █",
        "██    █",
        "█ █   █",
        "█  █  █",
        "█   █ █",
        "█    ██",
        "█     █"
    };
    
    private static readonly string[] BannerS = new[]
    {
        " █████ ",
        "█      ",
        "█      ",
        " █████ ",
        "      █",
        "      █",
        " █████ "
    };
    
    private static readonly string[] BannerI = new[]
    {
        "███████",
        "   █   ",
        "   █   ",
        "   █   ",
        "   █   ",
        "   █   ",
        "███████"
    };
    
    private static readonly string[] BannerP = new[]
    {
        "██████ ",
        "█     █",
        "█     █",
        "██████ ",
        "█      ",
        "█      ",
        "█      "
    };
    
    private static readonly string[] BannerE = new[]
    {
        "███████",
        "█      ",
        "█      ",
        "██████ ",
        "█      ",
        "█      ",
        "███████"
    };

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
                if (_inIntroScreen)
                {
                    // Exit application from intro screen
                    Application.RequestStop();
                }
                else
                {
                    // Return to intro screen from game
                    _inIntroScreen = true;
                    _bannerScrolling = false;
                    _showMenu = true;
                    _clearingScreen = false;
                    _bannerStartTime = DateTime.Now;
                    // Calculate centered banner position - only if Driver is available
                    if (Application.Driver != null)
                    {
                        int width = Application.Driver.Cols;
                        int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
                        _bannerScrollPosition = (width - bannerWidth) / 2;
                        // Force redraw of intro screen immediately
                        DrawIntroScreen();
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
            if (!_inIntroScreen)
            {
                DrawMapAndPlayer();
            }
        };

        // Start intro screen
        _bannerStartTime = DateTime.Now;
        
        // Timer for intro screen animation and clearing effects (16ms for ~60fps)
        Application.AddTimeout(TimeSpan.FromMilliseconds(16), () =>
        {
            if (Application.Driver != null)
            {
                if (_inIntroScreen)
                {
                    DrawIntroScreen();
                }
                else if (_clearingScreen && !_gameOver)
                {
                    // Draw clearing effect during gameplay (e.g., when player loses a life)
                    int width = Application.Driver.Cols;
                    int height = Application.Driver.Rows;
                    DrawClearingEffect(width, height);
                }
            }
            return true;
        });

        // Timer for player animation and initial map draw (100ms)
        Application.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            if (!_inIntroScreen && !_clearingScreen && !_mapDrawn)
            {
                DrawMapAndPlayer();
                _mapDrawn = true;
            }
            else if (!_inIntroScreen && !_clearingScreen)
            {
                DrawPlayer();
            }
            return true;
        });

        // Separate timer for bullet updates (10ms for smooth movement)
        Application.AddTimeout(TimeSpan.FromMilliseconds(10), () =>
        {
            if (_mapDrawn && !_clearingScreen)
            {
                UpdateBullets();
                DrawFrame();
            }
            return true;
        });

        // Separate timer for hive animation (75ms for slower color change and better performance)
        Application.AddTimeout(TimeSpan.FromMilliseconds(75), () =>
        {
            if (_mapDrawn && !_clearingScreen)
            {
                DrawHives();
                DrawStatusBar(); // Update status bar periodically
            }
            return true;
        });

        // Timer for snipe spawning and movement (200ms)
        Application.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
        {
            if (_mapDrawn && !_clearingScreen)
            {
                SpawnSnipes();
                UpdateSnipes();
                DrawSnipes();
            }
            return true;
        });
    }

    private void HandleWindowKeyDown(object? sender, dynamic e)
    {
        // Handle Escape key at Window level to prevent default close behavior
        if (e.KeyCode == KeyCode.Esc)
        {
            if (_inIntroScreen)
            {
                // Exit application from intro screen
                Application.RequestStop();
            }
            else
            {
                // Return to intro screen from game
                _inIntroScreen = true;
                _bannerScrolling = false;
                _showMenu = true;
                _clearingScreen = false;
                _bannerStartTime = DateTime.Now;
                // Calculate centered banner position - only if Driver is available
                if (Application.Driver != null)
                {
                    int width = Application.Driver.Cols;
                    int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
                    _bannerScrollPosition = (width - bannerWidth) / 2;
                }
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
        
        // Handle game over key press
        if (_waitingForGameOverKey)
        {
            // Any key press returns to intro screen
            _gameOver = false;
            _waitingForGameOverKey = false;
            _inIntroScreen = true;
            _bannerScrolling = false;
            _showMenu = true;
            _clearingScreen = false;
            _selectedMenuIndex = 0;
            _enteringInitials = false;
            _bannerStartTime = DateTime.Now;
            if (Application.Driver != null)
            {
                int width = Application.Driver.Cols;
                int bannerWidth = 7 * 7 + 6 * 2;
                _bannerScrollPosition = (width - bannerWidth) / 2;
                DrawIntroScreen();
            }
            return;
        }
        
        // Handle intro screen key press
        if (_inIntroScreen)
        {
            HandleIntroScreenKey(e);
            return; // Don't process game keys during intro
        }
        
        // Don't process game keys if game is over or waiting for game over key
        if (_gameOver || _waitingForGameOverKey)
        {
            return;
        }
        
        if (Application.Driver == null)
            return;
            
        int currentWidth = Application.Driver.Cols;
        int currentHeight = Application.Driver.Rows;
        int frameWidth = currentWidth;
        int frameHeight = currentHeight;

        // Get map viewport centered on player position
        // _player.X, _player.Y represents the top-left corner of the player in world coordinates
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        // Calculate top-left corner of player in viewport coordinates
        // Map.GetMap centers the viewport on (_player.X, _player.Y)
        // So top-left corner in viewport is at the center of the viewport
        int topLeftCol = frameWidth / 2;
        int topLeftRow = frameHeight / 2;

        // Player is 2 columns wide, 3 rows tall
        // Player currently occupies: columns [topLeftCol, topLeftCol+1], rows [topLeftRow, topLeftRow+1, topLeftRow+2]
        // That's 6 cells total: (topLeftCol, topLeftRow), (topLeftCol+1, topLeftRow), 
        //                        (topLeftCol, topLeftRow+1), (topLeftCol+1, topLeftRow+1),
        //                        (topLeftCol, topLeftRow+2), (topLeftCol+1, topLeftRow+2)

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
            // Check all 6 cells: 2 columns x 3 rows
            return IsWalkable(newTopLeftRow, newTopLeftCol) &&
                   IsWalkable(newTopLeftRow, newTopLeftCol + 1) &&
                   IsWalkable(newTopLeftRow + 1, newTopLeftCol) &&
                   IsWalkable(newTopLeftRow + 1, newTopLeftCol + 1) &&
                   IsWalkable(newTopLeftRow + 2, newTopLeftCol) &&
                   IsWalkable(newTopLeftRow + 2, newTopLeftCol + 1);
        }

        // Handle movement - check KeyCode for both arrow keys and numeric keypad
        bool moved = false;

        switch (e.KeyCode)
        {
            case KeyCode.D8: // Numeric keypad 8 (Up)
            case KeyCode.CursorUp:
                // Moving up: new position is (topLeftCol, topLeftRow - 1)
                if (CanMoveTo(topLeftCol, topLeftRow - 1))
                {
                    _player.Y--;
                    if (_player.Y < 0)
                        _player.Y = _map.MapHeight;
                    moved = true;
                }
                break;
            case KeyCode.D2: // Numeric keypad 2 (Down)
            case KeyCode.CursorDown:
                // Moving down: new position is (topLeftCol, topLeftRow + 1)
                if (CanMoveTo(topLeftCol, topLeftRow + 1))
                {
                    _player.Y++;
                    if (_player.Y > _map.MapHeight)
                        _player.Y = 0;
                    moved = true;
                }
                break;
            case KeyCode.D4: // Numeric keypad 4 (Left)
            case KeyCode.CursorLeft:
                // Moving left: new position is (topLeftCol - 1, topLeftRow)
                if (CanMoveTo(topLeftCol - 1, topLeftRow))
                {
                    _player.X--;
                    if (_player.X < 0)
                        _player.X = _map.MapWidth;
                    moved = true;
                }
                break;
            case KeyCode.D6: // Numeric keypad 6 (Right)
            case KeyCode.CursorRight:
                // Moving right: new position is (topLeftCol + 1, topLeftRow)
                if (CanMoveTo(topLeftCol + 1, topLeftRow))
                {
                    _player.X++;
                    if (_player.X > _map.MapWidth)
                        _player.X = 0;
                    moved = true;
                }
                break;
            case KeyCode.D7: // Numeric keypad 7 (Up-Left diagonal)
                // Moving up-left: new position is (topLeftCol - 1, topLeftRow - 1)
                if (CanMoveTo(topLeftCol - 1, topLeftRow - 1))
                {
                    _player.X--;
                    _player.Y--;
                    if (_player.X < 0)
                        _player.X = _map.MapWidth;
                    if (_player.Y < 0)
                        _player.Y = _map.MapHeight;
                    moved = true;
                }
                break;
            case KeyCode.D9: // Numeric keypad 9 (Up-Right diagonal)
                // Moving up-right: new position is (topLeftCol + 1, topLeftRow - 1)
                if (CanMoveTo(topLeftCol + 1, topLeftRow - 1))
                {
                    _player.X++;
                    _player.Y--;
                    if (_player.X > _map.MapWidth)
                        _player.X = 0;
                    if (_player.Y < 0)
                        _player.Y = _map.MapHeight;
                    moved = true;
                }
                break;
            case KeyCode.D1: // Numeric keypad 1 (Down-Left diagonal)
                // Moving down-left: new position is (topLeftCol - 1, topLeftRow + 1)
                if (CanMoveTo(topLeftCol - 1, topLeftRow + 1))
                {
                    _player.X--;
                    _player.Y++;
                    if (_player.X < 0)
                        _player.X = _map.MapWidth;
                    if (_player.Y > _map.MapHeight)
                        _player.Y = 0;
                    moved = true;
                }
                break;
            case KeyCode.D3: // Numeric keypad 3 (Down-Right diagonal)
                // Moving down-right: new position is (topLeftCol + 1, topLeftRow + 1)
                if (CanMoveTo(topLeftCol + 1, topLeftRow + 1))
                {
                    _player.X++;
                    _player.Y++;
                    if (_player.X > _map.MapWidth)
                        _player.X = 0;
                    if (_player.Y > _map.MapHeight)
                        _player.Y = 0;
                    moved = true;
                }
                break;
        }

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
                _bullets.Add(new Bullet(startX, startY, velX, velY));
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
        DrawPlayerWithClearing();
        DrawBullets();
        // Hives and snipes are drawn on their own timers for better performance
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
                
                _bullets.RemoveAt(i);
                continue;
            }
            
            // Store previous position
            double prevX = bullet.X;
            double prevY = bullet.Y;
            
            // Update bullet position (moves every 10ms when this is called)
            bullet.Update();

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
            }
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

            // Check collision with player (only if snipe is still alive)
            if (!snipe.IsAlive)
                goto nextSnipe;
                
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
                    // Trigger clearing effect with lives message
                    _clearingScreen = true;
                    _clearingStartTime = DateTime.Now;
                    _clearingRectSize = 0;
                    _clearingMessage = $"{_player.Lives} Lives Left";
                    _gameOver = false;
                    _waitingForGameOverKey = false;
                }
                else
                {
                    // Game over
                    _player.IsAlive = false;
                    _gameOver = true;
                    _clearingScreen = true;
                    _clearingStartTime = DateTime.Now;
                    _clearingRectSize = 0;
                    _clearingMessage = "GAME OVER";
                    _waitingForGameOverKey = false;
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

    private void DrawIntroScreen()
    {
        if (Application.Driver == null)
            return;
            
        int width = Application.Driver.Cols;
        int height = Application.Driver.Rows;
        
        if (_waitingForGameOverKey)
        {
            // Draw game over screen
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Application.Driver.Move(x, y);
                    Application.Driver.AddRune('*');
                }
            }
            // Draw GAME OVER message
            if (!string.IsNullOrEmpty(_clearingMessage))
            {
                int messageX = (width - _clearingMessage.Length) / 2;
                int messageY = height / 2;
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                Application.Driver.Move(messageX, messageY);
                Application.Driver.AddStr(_clearingMessage);
            }
            return;
        }
        
        if (_clearingScreen)
        {
            DrawClearingEffect(width, height);
            return;
        }
        
        // Fill screen with blue background
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Application.Driver.Move(0, y);
            Application.Driver.AddStr(new string(' ', width));
        }
        
        if (_bannerScrolling)
        {
            // Animate banner scrolling in from left
            double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
            int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
            int targetX = (width - bannerWidth) / 2; // Center position
            int startX = -bannerWidth; // Start completely off screen to the left
            
            if (elapsedSeconds >= 2.0)
            {
                // Animation complete, center the banner
                _bannerScrollPosition = targetX;
                _bannerScrolling = false;
                _showMenu = true;
            }
            else
            {
                // Calculate scroll position (ease-in-out)
                double progress = elapsedSeconds / 2.0;
                // Simple ease-in-out: smooth start and end
                progress = progress * progress * (3.0 - 2.0 * progress);
                // Interpolate from startX (off-screen left) to targetX (centered)
                _bannerScrollPosition = (int)(startX + (targetX - startX) * progress);
            }
            
            DrawBanner(_bannerScrollPosition, height);
        }
        else
        {
            // Banner is centered, draw it and show press key message
            int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
            int bannerX = (width - bannerWidth) / 2;
            DrawBanner(bannerX, height);
            
            if (_showMenu)
            {
                DrawMenu(width, height);
            }
        }
    }
    
    private void HandleIntroScreenKey(dynamic e)
    {
        if (_enteringInitials)
        {
            HandleInitialsInput(e);
            return;
        }
        
        if (!_showMenu || _clearingScreen)
            return;
        
        // Handle menu navigation
        switch (e.KeyCode)
        {
            case KeyCode.CursorUp:
            case KeyCode.D8: // Numeric keypad 8
                _selectedMenuIndex = (_selectedMenuIndex - 1 + _menuItems.Length) % _menuItems.Length;
                break;
                
            case KeyCode.CursorDown:
            case KeyCode.D2: // Numeric keypad 2
                _selectedMenuIndex = (_selectedMenuIndex + 1) % _menuItems.Length;
                break;
                
            case KeyCode.Enter:
                HandleMenuSelection();
                break;
                
            case KeyCode.S:
                _selectedMenuIndex = 0;
                HandleMenuSelection();
                break;
                
            case KeyCode.J:
                _selectedMenuIndex = 1;
                HandleMenuSelection();
                break;
                
            case KeyCode.I:
                _selectedMenuIndex = 2;
                HandleMenuSelection();
                break;
                
            case KeyCode.E:
            case KeyCode.X:
                _selectedMenuIndex = 3;
                HandleMenuSelection();
                break;
        }
    }
    
    private void HandleMenuSelection()
    {
        switch (_selectedMenuIndex)
        {
            case 0: // Start a New Game
                _clearingScreen = true;
                _clearingStartTime = DateTime.Now;
                _clearingRectSize = 0;
                _clearingMessage = $"Level {_gameState.Level}";
                _gameOver = false;
                _waitingForGameOverKey = false;
                break;
                
            case 1: // Join an Existing Game
                // Do nothing for now
                break;
                
            case 2: // Initials
                _enteringInitials = true;
                _initialsInput = "";
                break;
                
            case 3: // Exit
                Application.RequestStop();
                break;
        }
    }
    
    private void HandleInitialsInput(dynamic e)
    {
        // Handle backspace
        if (e.KeyCode == KeyCode.Backspace)
        {
            if (_initialsInput.Length > 0)
            {
                _initialsInput = _initialsInput.Substring(0, _initialsInput.Length - 1);
            }
            return;
        }
        
        // Handle Escape to cancel
        if (e.KeyCode == KeyCode.Esc)
        {
            _enteringInitials = false;
            _initialsInput = "";
            return;
        }
        
        // Get character from key
        char? ch = GetCharFromKey(e);
        if (ch == null)
            return;
        
        // Validate character (A-Z, 0-9)
        if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
        {
            // Convert to uppercase
            char upperChar = char.ToUpper(ch.Value);
            
            if (_initialsInput.Length < 2)
            {
                _initialsInput += upperChar;
                
                // If we have 2 characters, save and exit input mode
                if (_initialsInput.Length == 2)
                {
                    _config.Initials = _initialsInput;
                    _config.Save();
                    _player.Initials = _initialsInput;
                    _enteringInitials = false;
                    // Redraw menu immediately to show initials in cyan
                    if (Application.Driver != null && _inIntroScreen && _showMenu)
                    {
                        DrawIntroScreen();
                    }
                }
            }
        }
    }
    
    private char? GetCharFromKey(dynamic e)
    {
        // Try to get character from KeyEvent
        try
        {
            if (e.KeyEvent != null && e.KeyEvent.Key != null)
            {
                int keyValue = (int)e.KeyEvent.Key;
                if (keyValue >= 32 && keyValue <= 126) // Printable ASCII
                {
                    return (char)keyValue;
                }
            }
        }
        catch
        {
            // Fall through to alternative method
        }
        
        // Alternative: check KeyCode for letter keys
        try
        {
            if (e.KeyCode >= KeyCode.A && e.KeyCode <= KeyCode.Z)
            {
                return (char)('A' + ((int)e.KeyCode - (int)KeyCode.A));
            }
            if (e.KeyCode >= KeyCode.D0 && e.KeyCode <= KeyCode.D9)
            {
                return (char)('0' + ((int)e.KeyCode - (int)KeyCode.D0));
            }
        }
        catch
        {
        }
        
        return null;
    }
    
    private void DrawMenu(int width, int height)
    {
        if (Application.Driver == null)
            return;
        
        // Calculate menu position (centered below banner, with clear separation)
        int bannerEndY = height / 4 + 9; // Banner ends at 1/4 + 9 rows
        int menuBoxHeight = _menuItems.Length + 3; // Items + title + borders + gap
        int menuStartY = bannerEndY + 5; // 5 rows gap after banner
        
        // Calculate box dimensions
        int boxWidth = 40; // Fixed width for menu box
        int boxX = (width - boxWidth) / 2; // Center horizontally
        int boxY = menuStartY;
        int padding = 2; // Padding from box borders
        
        // Draw box border (using single-line characters)
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        
        // Top border with title
        Application.Driver.Move(boxX, boxY);
        Application.Driver.AddRune('┌');
        // Draw horizontal line with title
        string title = " Options ";
        int titleStartX = boxX + (boxWidth - title.Length) / 2;
        for (int x = boxX + 1; x < boxX + boxWidth - 1; x++)
        {
            Application.Driver.Move(x, boxY);
            if (x >= titleStartX && x < titleStartX + title.Length)
            {
                Application.Driver.AddRune(title[x - titleStartX]);
            }
            else
            {
                Application.Driver.AddRune('─');
            }
        }
        Application.Driver.Move(boxX + boxWidth - 1, boxY);
        Application.Driver.AddRune('┐');
        
        // Calculate actual menu items (with gap between Initials and Exit)
        // 4 items + 1 gap = 5 rows for items, plus 1 for top border = 6 total rows
        int menuItemCount = _menuItems.Length + 1; // +1 for gap
        
        // Draw menu items
        int itemIndex = 0;
        for (int i = 0; i < _menuItems.Length; i++)
        {
            string menuText = _menuItems[i];
            
            // Special handling for Initials option
            string initialsPart = "";
            if (i == 2) // Initials option
            {
                if (_enteringInitials)
                {
                    // Show input field with caret
                    menuText = "Initials ";
                    if (_initialsInput.Length == 0)
                    {
                        initialsPart = "▊_"; // Caret at first position
                    }
                    else if (_initialsInput.Length == 1)
                    {
                        initialsPart = _initialsInput + "▊"; // Caret at second position
                    }
                    else
                    {
                        initialsPart = _initialsInput; // Both characters entered, no caret
                    }
                }
                else
                {
                    // Show current initials in Cyan
                    menuText = "Initials ";
                    initialsPart = _config.Initials;
                }
            }
            
            int menuX = boxX + padding;
            int menuY = boxY + 1 + itemIndex; // +1 for top border
            
            // Set colors based on selection
            if (i == _selectedMenuIndex)
            {
                // Selected: white background, blue text - fill entire box width
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Blue, Color.White));
                // Fill the entire line width (minus borders)
                for (int x = boxX + 1; x < boxX + boxWidth - 1; x++)
                {
                    Application.Driver.Move(x, menuY);
                    Application.Driver.AddRune(' ');
                }
            }
            else
            {
                // Not selected: white text, blue background
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
            }
            
            // Draw menu text character by character (left-justified with padding)
            // Highlight first letter in yellow
            Application.Driver.Move(menuX, menuY);
            bool firstLetterDrawn = false;
            foreach (char c in menuText)
            {
                if (!firstLetterDrawn && char.IsLetter(c))
                {
                    // First letter - draw in yellow
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Yellow, i == _selectedMenuIndex ? Color.White : Color.Blue));
                    Application.Driver.AddRune(c);
                    firstLetterDrawn = true;
                    // Reset to menu color
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(i == _selectedMenuIndex ? Color.Blue : Color.White, i == _selectedMenuIndex ? Color.White : Color.Blue));
                }
                else
                {
                    // Regular character - use current menu color
                    Application.Driver.AddRune(c);
                }
            }
            
            // Draw initials part with special color if needed
            if (i == 2)
            {
                if (_enteringInitials)
                {
                    // Draw initials input with purple for typed letters
                    foreach (char c in initialsPart)
                    {
                        if (c == '▊')
                        {
                            // Caret - use current selection color
                            Application.Driver.AddRune(c);
                        }
                        else if (c == '_')
                        {
                            // Placeholder - use current selection color
                            Application.Driver.AddRune(c);
                        }
                        else
                        {
                            // Typed letter - use purple
                            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Magenta, i == _selectedMenuIndex ? Color.White : Color.Blue));
                            Application.Driver.AddRune(c);
                            // Reset to menu color
                            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(i == _selectedMenuIndex ? Color.Blue : Color.White, i == _selectedMenuIndex ? Color.White : Color.Blue));
                        }
                    }
                }
                else
                {
                    // Draw initials in Cyan
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Cyan, i == _selectedMenuIndex ? Color.White : Color.Blue));
                    Application.Driver.AddStr(initialsPart);
                    // Reset to menu color
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(i == _selectedMenuIndex ? Color.Blue : Color.White, i == _selectedMenuIndex ? Color.White : Color.Blue));
                }
            }
            
            itemIndex++;
            
            // Add gap after Initials option (index 2)
            if (i == 2)
            {
                itemIndex++; // Skip a row for gap
            }
        }
        
        // Left and right borders for each menu item row
        for (int row = 1; row <= menuItemCount; row++)
        {
            int y = boxY + row;
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
            Application.Driver.Move(boxX, y);
            Application.Driver.AddRune('│');
            Application.Driver.Move(boxX + boxWidth - 1, y);
            Application.Driver.AddRune('│');
        }
        
        // Bottom border (after all menu items)
        int bottomY = boxY + menuItemCount + 1;
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        Application.Driver.Move(boxX, bottomY);
        Application.Driver.AddRune('└');
        for (int x = boxX + 1; x < boxX + boxWidth - 1; x++)
        {
            Application.Driver.Move(x, bottomY);
            Application.Driver.AddRune('─');
        }
        Application.Driver.Move(boxX + boxWidth - 1, bottomY);
        Application.Driver.AddRune('┘');
    }
    
    private void DrawBanner(int startX, int screenHeight)
    {
        if (Application.Driver == null)
            return;
            
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        
        // Banner is 7 rows tall, with 1 blank row above and below
        // Position banner higher up (about 1/3 from top) to leave room for menu below
        int bannerStartY = screenHeight / 4; // 1/4 from top instead of center
        
        // Draw blank row above
        if (bannerStartY > 0)
        {
            for (int y = 0; y < bannerStartY; y++)
            {
                // Already filled with blue background
            }
        }
        
        // Draw each letter of NSNIPES with 2-column gaps between letters
        string[][] letters = { BannerN, BannerS, BannerN, BannerI, BannerP, BannerE, BannerS };
        
        for (int letterIndex = 0; letterIndex < letters.Length; letterIndex++)
        {
            string[] letter = letters[letterIndex];
            // Each letter is 7 columns wide, with 2 columns gap after each (except last)
            // Position = startX + (letterIndex * (7 + 2))
            int letterX = startX + (letterIndex * 9); // 7 for letter + 2 for gap
            
            for (int row = 0; row < 7; row++)
            {
                int y = bannerStartY + 1 + row; // +1 for blank row above
                if (y >= 0 && y < screenHeight)
                {
                    for (int col = 0; col < 7; col++)
                    {
                        int x = letterX + col;
                        if (x >= 0 && x < Application.Driver.Cols)
                        {
                            Application.Driver.Move(x, y);
                            Application.Driver.AddRune(letter[row][col]);
                        }
                    }
                }
            }
        }
    }
    
    private void DrawClearingEffect(int width, int height)
    {
        if (Application.Driver == null)
            return;
            
        // Calculate message area size first (if message exists)
        int messageAreaWidth = 0;
        int messageAreaHeight = 3; // 1 row for message + 1 above + 1 below
        int messageX = 0;
        int messageY = height / 2;
        string messageWithSpacing = "";
        
        if (!string.IsNullOrEmpty(_clearingMessage))
        {
            // Add spacing: 2 spaces before and after the message
            messageWithSpacing = "  " + _clearingMessage + "  ";
            messageAreaWidth = messageWithSpacing.Length + 4; // Extra padding on sides
            messageX = (width - messageWithSpacing.Length) / 2;
        }
        
        // Calculate starting size from message area (diagonal distance from center to message area edge)
        int centerX = width / 2;
        int centerY = height / 2;
        int messageAreaStartSize = 0;
        if (messageAreaWidth > 0)
        {
            // Calculate distance from center to the farthest corner of message area
            int messageAreaHalfWidth = messageAreaWidth / 2;
            int messageAreaHalfHeight = messageAreaHeight / 2;
            int maxDistFromCenter = (int)Math.Sqrt(
                messageAreaHalfWidth * messageAreaHalfWidth + 
                messageAreaHalfHeight * messageAreaHalfHeight
            );
            messageAreaStartSize = maxDistFromCenter + 2; // Add small buffer
        }
        
        // Calculate clearing rectangle size based on elapsed time
        // Effect should complete in about 1 second
        double elapsedSeconds = (DateTime.Now - _clearingStartTime).TotalSeconds;
        double progress = Math.Min(1.0, elapsedSeconds / 1.0);
        
        // Calculate rectangle size (grows from message area size to full screen)
        // Use diagonal distance to ensure rectangle covers entire screen
        int maxSize = (int)Math.Sqrt(width * width + height * height) / 2 + 10;
        int newRectSize = messageAreaStartSize + (int)((maxSize - messageAreaStartSize) * progress);
        
        // Draw status bar once at the top
        DrawStatusBar();
        
        // Draw expanding rectangle and reveal map underneath
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
        
        for (int y = StatusBarHeight; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Check if this position is in the message area (protect it from asterisks)
                bool inMessageArea = false;
                if (messageAreaWidth > 0)
                {
                    int messageAreaLeft = messageX - 2; // Extra padding
                    int messageAreaRight = messageX + messageWithSpacing.Length + 2;
                    int messageAreaTop = messageY - 1;
                    int messageAreaBottom = messageY + 1;
                    if (x >= messageAreaLeft && x < messageAreaRight &&
                        y >= messageAreaTop && y <= messageAreaBottom)
                    {
                        inMessageArea = true;
                    }
                }
                
                // Calculate distance from center
                int dx = x - centerX;
                int dy = (y - StatusBarHeight) - (height - StatusBarHeight) / 2;
                int distance = (int)Math.Sqrt(dx * dx + dy * dy);
                
                if (distance <= newRectSize && !inMessageArea)
                {
                    // Inside rectangle but not in message area - draw '*'
                    Application.Driver.Move(x, y);
                    Application.Driver.AddRune('*');
                }
                else if (!inMessageArea)
                {
                    // Outside rectangle and not in message area - draw map/player
                    DrawMapAndPlayerAtPosition(x, y);
                }
                // If in message area, skip drawing here (will draw message below)
            }
        }
        
        _clearingRectSize = newRectSize;
        
        // Draw message centered on screen (if provided) with spacing around it
        if (!string.IsNullOrEmpty(_clearingMessage) && !string.IsNullOrEmpty(messageWithSpacing))
        {
            // Draw blank rows above and below the message (spaces, not asterisks)
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
            
            // Clear the area around the message (above, message row, below) with spaces
            for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
            {
                int y = messageY + rowOffset;
                if (y >= StatusBarHeight && y < height)
                {
                    // Clear a wider area to ensure message stands out
                    int clearWidth = messageWithSpacing.Length + 4; // Extra padding
                    int clearX = (width - clearWidth) / 2;
                    for (int x = clearX; x < clearX + clearWidth && x < width; x++)
                    {
                        Application.Driver.Move(x, y);
                        Application.Driver.AddRune(' '); // Use spaces, not asterisks
                    }
                }
            }
            
            // Draw the message on top
            Application.Driver.Move(messageX, messageY);
            Application.Driver.AddStr(messageWithSpacing);
        }
        
        // When rectangle covers entire screen, transition to game or wait for key (game over)
        if (newRectSize >= maxSize)
        {
            if (_gameOver)
            {
                // Game over - wait for key press
                _waitingForGameOverKey = true;
                _clearingScreen = false; // Stop the animation, but keep showing the screen
                // Draw final screen with GAME OVER message
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                for (int y = StatusBarHeight; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Application.Driver.Move(x, y);
                        Application.Driver.AddRune('*');
                    }
                }
                // Draw GAME OVER message
                if (!string.IsNullOrEmpty(_clearingMessage))
                {
                    string gameOverMessageWithSpacing = "  " + _clearingMessage + "  ";
                    int gameOverMessageX = (width - gameOverMessageWithSpacing.Length) / 2;
                    int gameOverMessageY = height / 2;
                    
                    // Clear area around message
                    for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
                    {
                        int y = gameOverMessageY + rowOffset;
                        if (y >= StatusBarHeight && y < height)
                        {
                            int clearWidth = gameOverMessageWithSpacing.Length + 4;
                            int clearX = (width - clearWidth) / 2;
                            for (int x = clearX; x < clearX + clearWidth && x < width; x++)
                            {
                                Application.Driver.Move(x, y);
                                Application.Driver.AddRune(' ');
                            }
                        }
                    }
                    
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                    Application.Driver.Move(gameOverMessageX, gameOverMessageY);
                    Application.Driver.AddStr(gameOverMessageWithSpacing);
                }
            }
            else
            {
                // Normal game start or respawn
                if (_inIntroScreen)
                {
                    _inIntroScreen = false;
                }
                _clearingScreen = false;
                // Clear screen by filling with spaces and start game
                Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Black));
                for (int y = 0; y < height; y++)
                {
                    Application.Driver.Move(0, y);
                    Application.Driver.AddStr(new string(' ', width));
                }
                _mapDrawn = false; // Force redraw of map
            }
        }
    }
    
    // Reusable clearing effect method (for later use)
    public void StartClearingEffect()
    {
        _clearingScreen = true;
        _clearingStartTime = DateTime.Now;
        _clearingRectSize = 0;
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
        if (_cachedHivesUndestroyed == _gameState.HivesUndestroyed &&
            _cachedSnipesUndestroyed == _gameState.SnipesUndestroyed &&
            _cachedLives == _player.Lives &&
            _cachedLevel == _gameState.Level &&
            _cachedScore == _gameState.Score)
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
        Application.Driver.AddStr($"Score: {_gameState.Score}");
        
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

}
