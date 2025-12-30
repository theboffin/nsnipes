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

    public Game()
    {
        _map = new Map();
        var (x, y) = FindRandomValidPosition();
        _player = new Player(x, y);
        
        // Initialize game state and hives
        InitializeHives();
        
        Title = "NSnipes";

        // Make window fill entire screen
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

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

        Application.KeyDown += HandleKeyDown;
        Application.SizeChanging += (s, e) =>
        {
            DrawMapAndPlayer();
        };

        // Timer for player animation and initial map draw (100ms)
        Application.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            if (!_mapDrawn)
            {
                DrawMapAndPlayer();
                _mapDrawn = true;
            }
            else
            {
                DrawPlayer();
            }
            return true;
        });

        // Separate timer for bullet updates (10ms for smooth movement)
        Application.AddTimeout(TimeSpan.FromMilliseconds(10), () =>
        {
            if (_mapDrawn)
            {
                UpdateBullets();
        DrawFrame();
            }
            return true;
        });

        // Separate timer for hive animation (75ms for slower color change and better performance)
        Application.AddTimeout(TimeSpan.FromMilliseconds(75), () =>
        {
            if (_mapDrawn)
            {
                DrawHives();
                DrawStatusBar(); // Update status bar periodically
            }
            return true;
        });

        // Timer for snipe spawning and movement (200ms)
        Application.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
        {
            if (_mapDrawn)
            {
                SpawnSnipes();
                UpdateSnipes();
                DrawSnipes();
            }
            return true;
        });
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        int currentWidth = Application.Driver!.Cols;
        int currentHeight = Application.Driver!.Rows;
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
            DrawMapAndPlayer();
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

        // Draw status bar first
        DrawStatusBar();

        Application.Driver.SetAttribute(ColorScheme!.Disabled);

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
    }

    private void DrawFrame()
    {
        DrawPlayer();
        DrawBullets();
        // Hives and snipes are drawn on their own timers for better performance
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
                    Application.Driver!.SetAttribute(ColorScheme!.Disabled);
                    Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                    Application.Driver.AddRune(map[viewportY][viewportX]);
                    Application.Driver.SetAttribute(ColorScheme!.Normal);
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
                    // Bullet hit snipe
                    snipe.IsAlive = false;
                    ClearSnipePosition(snipe);
                    _snipes.RemoveAt(j);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += 25;
                    _player.Score += 25;
                    
                    // Remove bullet
                    int viewportX = bulletWorldX - mapOffsetX;
                    int viewportY = bulletWorldY - mapOffsetY;
                    if (viewportX >= 0 && viewportX < frameWidth && 
                        viewportY >= 0 && viewportY < frameHeight &&
                        map != null && viewportY >= 0 && viewportY < map.Length &&
                        viewportX >= 0 && viewportX < map[viewportY].Length)
                    {
                        Application.Driver!.SetAttribute(ColorScheme!.Disabled);
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(map[viewportY][viewportX]);
                        Application.Driver.SetAttribute(ColorScheme!.Normal);
                    }
                    _bullets.RemoveAt(i);
                    bulletRemoved = true;
                    break; // Bullet is removed, exit snipe loop
                }
                
                // Check bullet at arrow position
                int arrowWorldX = snipeWorldX + (snipe.DirectionX < 0 ? -1 : 1);
                arrowWorldX = (arrowWorldX % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                if (bulletWorldX == arrowWorldX && bulletWorldY == snipeWorldY)
                {
                    // Bullet hit snipe arrow
                    snipe.IsAlive = false;
                    ClearSnipePosition(snipe);
                    _snipes.RemoveAt(j);
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += 25;
                    _player.Score += 25;
                    
                    // Remove bullet
                    int viewportX = bulletWorldX - mapOffsetX;
                    int viewportY = bulletWorldY - mapOffsetY;
                    if (viewportX >= 0 && viewportX < frameWidth && 
                        viewportY >= 0 && viewportY < frameHeight &&
                        map != null && viewportY >= 0 && viewportY < map.Length &&
                        viewportX >= 0 && viewportX < map[viewportY].Length)
                    {
                        Application.Driver!.SetAttribute(ColorScheme!.Disabled);
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(map[viewportY][viewportX]);
                        Application.Driver.SetAttribute(ColorScheme!.Normal);
                    }
                    _bullets.RemoveAt(i);
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
                        
                        // Remove bullet
                        int viewportX = bulletWorldX - mapOffsetX;
                        int viewportY = bulletWorldY - mapOffsetY;
                        if (viewportX >= 0 && viewportX < frameWidth && 
                            viewportY >= 0 && viewportY < frameHeight &&
                            map != null && viewportY >= 0 && viewportY < map.Length &&
                            viewportX >= 0 && viewportX < map[viewportY].Length)
                        {
                            Application.Driver!.SetAttribute(ColorScheme!.Disabled);
                            Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                            Application.Driver.AddRune(map[viewportY][viewportX]);
                            Application.Driver.SetAttribute(ColorScheme!.Normal);
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

        // Get current map viewport for clearing previous positions
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        // Get map viewport to convert world coordinates to viewport coordinates
        // Map.GetMap centers on (_player.X, _player.Y), so:
        // viewport center = (frameWidth/2, frameHeight/2) corresponds to (_player.X, _player.Y)
        // Offset by StatusBarHeight when drawing
        int mapOffsetX = _player.X - (frameWidth / 2);
        int mapOffsetY = _player.Y - (frameHeight / 2);

        // First, clear previous bullet positions by drawing the map character there
        Application.Driver.SetAttribute(ColorScheme!.Disabled);
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

        // Flash between bright red and red based on time
        bool isBright = (DateTime.Now.Millisecond / 250) % 2 == 0;
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

        Application.Driver.SetAttribute(ColorScheme!.Normal);
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

        // Flash between cyan and green based on time (changes every 75ms)
        // Use total milliseconds to get smooth 75ms cycle
        long totalMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        bool isCyan = (totalMs / 75) % 2 == 0;
        var hiveColor = isCyan ? Color.Cyan : Color.Green;
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(hiveColor, Color.Black));

        // Pre-calculate viewport bounds for early exit optimization
        int minViewportX = -2; // Hive is 2 wide, so allow 2 cells outside for partial visibility
        int maxViewportX = frameWidth + 1;
        int minViewportY = -2;
        int maxViewportY = frameHeight + 1;

        foreach (var hive in _hives)
        {
            if (hive.IsDestroyed)
                continue;

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

        Application.Driver.SetAttribute(ColorScheme!.Normal);
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

            // Store previous position and direction before moving
            snipe.PreviousX = snipe.X;
            snipe.PreviousY = snipe.Y;
            snipe.PreviousDirectionX = snipe.DirectionX;
            snipe.PreviousDirectionY = snipe.DirectionY;

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
                    // Snipe moved into bullet
                    snipe.IsAlive = false;
                    ClearSnipePosition(snipe);
                    _snipes.RemoveAt(i); // Remove snipe from list
                    _gameState.SnipesUndestroyed--;
                    _gameState.Score += 25;
                    _player.Score += 25;
                    
                    // Remove bullet
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
                        Application.Driver!.SetAttribute(ColorScheme!.Disabled);
                        Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
                        Application.Driver.AddRune(bulletMap[viewportY][viewportX]);
                        Application.Driver.SetAttribute(ColorScheme!.Normal);
                    }
                    _bullets.RemoveAt(k);
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
                    // Respawn player at random position
                    var (x, y) = FindRandomValidPosition();
                    _player.X = x;
                    _player.Y = y;
                    // Redraw map when player respawns
                    DrawMapAndPlayer();
                }
                else
                {
                    // Game over
                    _player.IsAlive = false;
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

        // Get map viewport for clearing previous positions
        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        // First, clear previous snipe positions
        Application.Driver.SetAttribute(ColorScheme!.Disabled);
        foreach (var snipe in _snipes)
        {
            if (!snipe.IsAlive)
                continue;

            // Always clear previous position (even if position didn't change, direction might have)
            // This ensures '@' characters and arrows are properly cleared
            // Also clear if direction changed even if position is the same
            bool positionChanged = snipe.X != snipe.PreviousX || snipe.Y != snipe.PreviousY;
            bool directionChanged = snipe.DirectionX != snipe.PreviousDirectionX || snipe.DirectionY != snipe.PreviousDirectionY;
            
            if (positionChanged || directionChanged)
            {
                // Calculate previous viewport coordinates
                int prevDeltaX = snipe.PreviousX - _player.X;
                int prevDeltaY = snipe.PreviousY - _player.Y;

                // Handle wrapping
                if (prevDeltaX > _map.MapWidth / 2)
                    prevDeltaX -= _map.MapWidth;
                else if (prevDeltaX < -_map.MapWidth / 2)
                    prevDeltaX += _map.MapWidth;

                if (prevDeltaY > _map.MapHeight / 2)
                    prevDeltaY -= _map.MapHeight;
                else if (prevDeltaY < -_map.MapHeight / 2)
                    prevDeltaY += _map.MapHeight;

                int prevViewportX = (frameWidth / 2) + prevDeltaX;
                int prevViewportY = (frameHeight / 2) + prevDeltaY;

                // Clear previous position if within viewport
                // The '@' character position depends on previous direction:
                // Moving left (PreviousDirectionX < 0): arrow at prevViewportX, '@' at prevViewportX + 1
                // Moving right or other: '@' at prevViewportX, arrow at prevViewportX + 1
                
                // Clear both positions (character and arrow) to ensure nothing is left behind
                // Calculate world coordinates for both positions
                
                // Position 1: The viewport center position (prevViewportX)
                // This is where '@' is when moving right, or arrow when moving left
                if (prevViewportX >= 0 && prevViewportX < frameWidth && 
                    prevViewportY >= 0 && prevViewportY < frameHeight)
                {
                    int worldX1 = snipe.PreviousX;
                    int worldY1 = snipe.PreviousY;
                    worldX1 = (worldX1 % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    worldY1 = (worldY1 % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                    
                    if (worldY1 >= 0 && worldY1 < _map.MapHeight && worldX1 >= 0 && worldX1 < _map.MapWidth)
                    {
                        char mapChar1 = _map.FullMap[worldY1][worldX1];
                        Application.Driver.Move(prevViewportX, prevViewportY + StatusBarHeight);
                        Application.Driver.AddRune(mapChar1);
                    }
                }
                
                // Position 2: One cell to the right (prevViewportX + 1)
                // This is where arrow is when moving right, or '@' when moving left
                if (prevViewportX + 1 >= 0 && prevViewportX + 1 < frameWidth && 
                    prevViewportY >= 0 && prevViewportY < frameHeight)
                {
                    int worldX2 = snipe.PreviousX + 1;
                    int worldY2 = snipe.PreviousY;
                    worldX2 = (worldX2 % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    worldY2 = (worldY2 % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                    
                    if (worldY2 >= 0 && worldY2 < _map.MapHeight && worldX2 >= 0 && worldX2 < _map.MapWidth)
                    {
                        char mapChar2 = _map.FullMap[worldY2][worldX2];
                        Application.Driver.Move(prevViewportX + 1, prevViewportY + StatusBarHeight);
                        Application.Driver.AddRune(mapChar2);
                    }
                }
                
                // Position 3: One cell to the left (prevViewportX - 1) - for left-moving arrows
                if (prevViewportX - 1 >= 0 && prevViewportX - 1 < frameWidth && 
                    prevViewportY >= 0 && prevViewportY < frameHeight && snipe.PreviousDirectionX < 0)
                {
                    int worldX3 = snipe.PreviousX - 1;
                    int worldY3 = snipe.PreviousY;
                    worldX3 = (worldX3 % _map.MapWidth + _map.MapWidth) % _map.MapWidth;
                    worldY3 = (worldY3 % _map.MapHeight + _map.MapHeight) % _map.MapHeight;
                    
                    if (worldY3 >= 0 && worldY3 < _map.MapHeight && worldX3 >= 0 && worldX3 < _map.MapWidth)
                    {
                        char mapChar3 = _map.FullMap[worldY3][worldX3];
                        Application.Driver.Move(prevViewportX - 1, prevViewportY + StatusBarHeight);
                        Application.Driver.AddRune(mapChar3);
                    }
                }
            }
        }

        // Now draw snipes at their current positions
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

        Application.Driver.SetAttribute(ColorScheme!.Normal);
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
            Application.Driver.SetAttribute(ColorScheme!.Disabled);
            
            // Restore map character at snipe position
            char mapChar = _map.FullMap[(snipe.Y % _map.MapHeight + _map.MapHeight) % _map.MapHeight]
                [(snipe.X % _map.MapWidth + _map.MapWidth) % _map.MapWidth];
            Application.Driver.Move(viewportX, viewportY + StatusBarHeight);
            Application.Driver.AddRune(mapChar);
            
            // Clear arrow based on direction
            // Arrow position depends on direction:
            // Moving left (DirectionX < 0): arrow is at (viewportX - 1, viewportY)
            // Moving right or other: arrow is at (viewportX + 1, viewportY)
            int arrowViewportX;
            if (snipe.DirectionX < 0)
            {
                // Arrow is on the left
                arrowViewportX = viewportX - 1;
            }
            else
            {
                // Arrow is on the right
                arrowViewportX = viewportX + 1;
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
            
            Application.Driver.SetAttribute(ColorScheme!.Normal);
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
        Application.Driver.SetAttribute(ColorScheme!.Disabled);
        
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
        
        Application.Driver.SetAttribute(ColorScheme!.Normal);
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
        
        Application.Driver.SetAttribute(ColorScheme!.Normal);
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

        var eyes = DateTime.Now.Millisecond < 500 ? "ÔÔ" : "OO";
        var mouth = DateTime.Now.Millisecond < 500 ? "◄►" : "◂▸";

        Application.Driver!.SetAttribute(ColorScheme!.Focus);
        Application.Driver!.Move(topLeftCol, topLeftRow);
        Application.Driver!.AddStr(eyes);
        Application.Driver!.Move(topLeftCol, topLeftRow + 1);
        Application.Driver!.AddStr(mouth);
        Application.Driver!.SetAttribute(ColorScheme!.Normal);
        Application.Driver!.Move(topLeftCol, topLeftRow + 2);
        Application.Driver!.AddStr(_player.Initials);
    }

}
