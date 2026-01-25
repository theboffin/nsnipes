using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using System.Runtime.InteropServices;
using Grpc.Net.Client;
using Grpc.Core;
using NSnipes.GrpcServer;
using DrawingAttribute = Terminal.Gui.Drawing.Attribute;

namespace NSnipes;

public class IntroScreen : View
{
    // Events for communication with Game
    public event Action<int>? OnStartGame; // Level - called when starting a new game from menu
    public event Action<int>? OnStartMultiplayerGame; // MaxPlayers - called when starting a multiplayer game
    public event Action<string>? OnJoinGame; // GameId - called when joining an existing game
    public event Action? OnRespawnComplete; // Called when respawn clearing effect completes
    public event Action? OnExit;
    public event Action<string>? OnInitialsChanged; // New initials
    public event Action? OnReturnToIntro; // When returning to intro screen (e.g., from game over)
    
    // State
    private bool _isActive = true;
    private bool _bannerScrolling = true;
    private bool _showMenu = false;
    private bool _clearingScreen = false;
    private bool _isStartingNewGame = false; // Track if clearing effect is for starting a new game vs respawn
    private int _lastDrawnMenuIndex = -1; // Track last drawn menu index to detect changes
    
    private DateTime _bannerStartTime;
    private int _bannerScrollPosition = 0;
    private int _clearingRectSize = 0;
    private DateTime _clearingStartTime;
    private string _clearingMessage = "";
    
    // Intro player animation state
    private int _introPlayerX = -5; // Start off-screen to the left
    private int _introPlayerY = 0; // Will be set based on banner position
    private int _introPlayerPrevX = -5; // Previous X position for clearing
    
    // Game over screen
    private GameOverScreen _gameOverScreen;
    
    // Menu state
    private int _selectedMenuIndex = 0;
    private readonly string[] _menuItems = ["Start a New Game", "Join an Existing Game", "Initials", "Configure Server", "Exit"];
    private bool _enteringInitials = false;
    private string _initialsInput = "";
    
    // Level selection state
    private bool _enteringStartingLevel = false;
    private string _startingLevelInput = "1";
    private int _selectedStartingLevel = 1;
    
    // Multiplayer state
    private bool _enteringPlayerCount = false;
    private string _playerCountInput = "1";
    private bool _enteringGameId = false;
    private string _gameIdInput = "";
    private bool _waitingForPlayers = false;
    private string _currentGameId = "";
    private int _currentPlayerCount = 0;
    private int _maxPlayers = 1;
    private const int MultiplayerWaitTimeSeconds = 60;
    private int _timeRemaining = MultiplayerWaitTimeSeconds;
    private List<string> _joinedPlayers = new List<string>(5); // Max 5 players
    private DateTime _joinWaitStartTime = DateTime.Now;
    
    // Server configuration state
    private bool _enteringServerConfig = false;
    private string _serverAddressInput = "";
    private string _serverPortInput = "";
    private bool _editingServerAddress = true; // true = editing address, false = editing port
    private bool? _serverStatus = null; // null = unknown, true = online, false = offline
    private DateTime _lastServerCheck = DateTime.MinValue;
    private const int ServerCheckIntervalSeconds = 5; // Check server status every 5 seconds
    private Task? _serverStatusCheckTask = null;
    private CancellationTokenSource _serverStatusCheckCancellation = new CancellationTokenSource();
    
    // Demo animation state
    private bool _demoActive = false;
    private List<DemoPlayer> _demoPlayers = new List<DemoPlayer>(2); // Pre-allocated for 2 players
    private List<Snipe> _demoSnipes = new List<Snipe>(10); // Pre-allocated capacity
    private List<Bullet> _demoBullets = new List<Bullet>(20); // Pre-allocated capacity
    private Dictionary<string, int> _snipeDirectionPersistence = new Dictionary<string, int>(); // Track snipe direction persistence by SnipeId
    private Dictionary<string, (int count, DateTime lastBurstTime)> _playerBurstFire = new Dictionary<string, (int, DateTime)>(); // Track burst fire state by playerId
    private DateTime _demoUpdateTimer = DateTime.Now;
    private DateTime _demoSpawnTimer = DateTime.Now;
    private (int x, int y, int width, int height)? _cachedMenuBounds = null;
    private (int x, int y, int width, int height)? _cachedLogoBounds = null;
    private DateTime _cachedFrameTime = DateTime.Now;
    private Random _demoRandom = new Random();
    
    // Demo constants
    private const int MaxDemoSnipes = 25;
    private const int MaxDemoBulletsPerPlayer = 10;
    private const int DemoUpdateIntervalMs = 150;
    private const double DemoSnipeBulletSpeed = 8.0; // Reduced from 15.0 for slower, more visible bullets
    private const int BurstFireChancePercent = 15; // 15% chance to fire a burst
    private const int BurstFireCount = 3; // Number of bullets in a burst
    private const int BurstFireIntervalMs = 100; // Time between bullets in a burst
    private const int SnipeSpawnIntervalMs = 2000;
    private const double PlayerAvoidanceRadius = 5.0;
    private const double PlayerSnipeHomingRadius = 8.0;
    private const double CollisionRadiusSquared = 1.0; // 1.0 squared for collision detection
    private const double BulletSnipeCollisionRadiusSquared = 0.5; // Smaller radius for bullet-snipe collisions
    private const int SnipeMinDirectionPersistence = 5; // Minimum moves before changing direction
    private const int SnipeMaxDirectionPersistence = 12; // Maximum moves before changing direction
    private const int PlayerPauseChancePercent = 15; // 15% chance player pauses instead of moving
    
    // Dependencies
    private GameConfig _config;
    private GameState _gameState;
    private Func<int, int, char>? _getMapCharAtPosition; // Callback to get map character during clearing effect
    
    // NSNIPES banner definition (7 rows tall, each letter is 7 characters wide)
    private static readonly string[] BannerN = [
        "█     █",
        "██    █",
        "█ █   █",
        "█  █  █",
        "█   █ █",
        "█    ██",
        "█     █"
    ];
    
    private static readonly string[] BannerS = [
        " █████ ",
        "█      ",
        "█      ",
        " █████ ",
        "      █",
        "      █",
        " █████ "
    ];
    
    private static readonly string[] BannerI = [
        "███████",
        "   █   ",
        "   █   ",
        "   █   ",
        "   █   ",
        "   █   ",
        "███████"
    ];
    
    private static readonly string[] BannerP = [
        "██████ ",
        "█     █",
        "█     █",
        "██████ ",
        "█      ",
        "█      ",
        "█      "
    ];
    
    private static readonly string[] BannerE = [
        "███████",
        "█      ",
        "█      ",
        "██████ ",
        "█      ",
        "█      ",
        "███████"
    ];
    
    public IntroScreen(GameConfig config, GameState gameState)
    {
        _config = config;
        _gameState = gameState;
        _bannerStartTime = DateTime.Now;
        _gameOverScreen = new GameOverScreen();
        _gameOverScreen.OnReturnToIntro += () =>
        {
            Show();
            OnReturnToIntro?.Invoke();
        };
        
        // Initialize View properties
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        Visible = true; // Start visible
        
        // Add GameOverScreen as child view
        Add(_gameOverScreen);
    }
    
    public bool IsActive => _isActive;
    public bool IsClearingScreen => _clearingScreen;
    public bool IsGameOver => _gameOverScreen.IsActive;
    public bool IsWaitingForGameOverKey => _gameOverScreen.IsWaitingForEnter;
    
    public void SetMapCharCallback(Func<int, int, char> callback)
    {
        _getMapCharAtPosition = callback;
    }
    
    public void Show()
    {
        _isActive = true;
        _bannerScrolling = true;
        _showMenu = false;
        _clearingScreen = false;
        _demoActive = false; // Reset demo state
        _gameOverScreen.Hide();
        _selectedMenuIndex = 0;
        _enteringInitials = false;
        _enteringStartingLevel = false;
        _enteringPlayerCount = false;
        Visible = true;
        SetNeedsDraw();
        _startingLevelInput = "1";
        _selectedStartingLevel = 1;
        _bannerStartTime = DateTime.Now;
        _introPlayerX = -5; // Reset player position to off-screen left
        _introPlayerPrevX = -5; // Reset previous position
        _introPlayerY = 0; // Will be calculated during animation
        
        // Clear demo entities
        _demoPlayers.Clear();
        _demoSnipes.Clear();
        _demoBullets.Clear();
        _snipeDirectionPersistence.Clear();
        _playerBurstFire.Clear();
        
        // Invalidate cached bounds
        _cachedMenuBounds = null;
        _cachedLogoBounds = null;
        
        // Clear the screen before showing intro
        if (IsInitialized)
        {
            int width = Frame.Width;
            int height = Frame.Height;
            int bannerWidth = 7 * 7 + 6 * 2;
            _bannerScrollPosition = -bannerWidth; // Start off-screen
            
            // Clear entire screen with blue background
            SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
            for (int y = 0; y < height; y++)
            {
                Move(0, y);
                this.AddString(new string(' ', width));
            }
        }
    }
    
    public void StartClearingEffect(string message, bool isStartingNewGame = false)
    {
        _clearingScreen = true;
        _clearingStartTime = DateTime.Now;
        _clearingRectSize = 0;
        _clearingMessage = message;
        _gameOverScreen.Hide();
        _isStartingNewGame = isStartingNewGame; // Track if this is for starting a new game
        _isActive = true; // Activate intro screen for clearing effect
        Visible = true; // Make visible for clearing effect
        SetNeedsDraw();
    }
    
    public void ShowGameOver(List<PlayerScoreInfo> playerScores)
    {
        // Make IntroScreen visible so GameOverScreen (child view) can be rendered
        Visible = true;
        _gameOverScreen.Show(playerScores);
        SetNeedsDraw();
    }
    
    protected override bool OnDrawingContent(DrawContext? dc)
    {
        if (dc == null || !IsInitialized)
            return false;
            
        int width = Frame.Width;
        int height = Frame.Height;
        
        // Draw game over screen if active (it will draw itself as a child view)
        if (_gameOverScreen.IsActive)
        {
            _gameOverScreen.SetNeedsDraw();
            return true;
        }
        
        if (_clearingScreen)
        {
            DrawClearingEffect(width, height);
            return true;
        }
        
        if (_waitingForPlayers)
        {
            DrawWaitingForPlayers(width, height);
            return true;
        }
        
        if (_enteringStartingLevel)
        {
            DrawStartingLevelInput(width, height);
            return true;
        }
        
        if (_enteringPlayerCount)
        {
            DrawPlayerCountInput(width, height);
            return true;
        }
        
        if (_enteringGameId)
        {
            DrawGameIdInput(width, height);
            return true;
        }
        
        if (_enteringServerConfig)
        {
            DrawServerConfigInput(width, height);
            return true;
        }
        
        // Fill screen with blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            this.AddString(new string(' ', width));
        }
        
        // Show menu immediately (not blocked by animation)
        _showMenu = true;
        
        // Check if we're in the intro animation phase (banner scrolling or demo transition)
        double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
        int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
        int targetX = (width - bannerWidth) / 2; // Center position
        int startX = -bannerWidth; // Start completely off screen to the left
        
        // Animation phase: banner scrolling (0-2s), then transition to demo mode
        if (elapsedSeconds < 2.0)
        {
            // Animate banner scrolling in from left (first 2 seconds)
            double progress = elapsedSeconds / 2.0;
            // Simple ease-in-out: smooth start and end
            progress = progress * progress * (3.0 - 2.0 * progress);
            // Interpolate from startX (off-screen left) to targetX (centered)
            int bannerX = (int)(startX + (targetX - startX) * progress);
            _bannerScrollPosition = bannerX;
            
            // Calculate player positions - two players flanking the banner
            int bannerStartY = height / 4; // Banner Y position
            int playerY = bannerStartY + 1 + 3; // Position players at middle of banner (row 3 of 7)
            
            // Player 1 (BD, white) on right side of banner
            int bannerRightEdge = bannerX + bannerWidth;
            int player1X = bannerRightEdge + 5;
            
            // Player 2 (NP, yellow) on left side of banner
            int player2X = bannerX - 7;
            
            // Initialize demo players if not already done
            if (_demoPlayers.Count == 0)
            {
                _demoPlayers.Add(new DemoPlayer("BD", Terminal.Gui.Drawing.Color.White, "demo_player_1")
                {
                    X = player1X,
                    Y = playerY,
                    PreviousX = player1X,
                    PreviousY = playerY
                });
                _demoPlayers.Add(new DemoPlayer("NP", Terminal.Gui.Drawing.Color.Yellow, "demo_player_2")
                {
                    X = player2X,
                    Y = playerY,
                    PreviousX = player2X,
                    PreviousY = playerY
                });
            }
            else
            {
                // Update player positions during animation
                _demoPlayers[0].X = player1X;
                _demoPlayers[0].Y = playerY;
                _demoPlayers[1].X = player2X;
                _demoPlayers[1].Y = playerY;
            }
            
            // Draw banner first
            DrawBanner(bannerX, height);
            
            // Draw demo players
            DrawDemoPlayers(width, height);
        }
        else
        {
            // Banner animation complete (after 2 seconds) - transition to demo mode
            int bannerX = targetX;
            _bannerScrollPosition = bannerX;
            
            if (_bannerScrolling)
            {
                _bannerScrolling = false;
                // Initialize demo mode
                if (!_demoActive)
                {
                    InitializeDemoMode(width, height);
                }
            }
            
            // Draw banner
            DrawBanner(bannerX, height);
            
            // Update and draw demo if active
            if (_demoActive && !_enteringInitials && !_enteringStartingLevel && 
                !_enteringPlayerCount && !_enteringGameId && !_enteringServerConfig)
            {
                UpdateDemo(width, height);
                DrawDemoPlayers(width, height);
                DrawDemoSnipes(width, height);
                DrawDemoBullets(width, height);
            }
            
            // Always draw menu (it's always visible)
            DrawMenu(width, height);
            _lastDrawnMenuIndex = _selectedMenuIndex; // Track what was drawn
        }
        
        return true;
    }
    
    public bool HandleKey(Key key)
    {
        // Handle game over key press - this must be checked first
        if (_gameOverScreen.IsActive)
        {
            return _gameOverScreen.HandleKey(key);
        }
        
        // Handle intro screen key press
        if (_isActive && !_clearingScreen)
        {
            HandleIntroScreenKey(key);
            return true;
        }
        
        // If we're in clearing screen, don't handle keys (clearing effect is in progress)
        if (_clearingScreen)
        {
            return false;
        }
        
        return false;
    }
    
    private void HandleIntroScreenKey(Key key)
    {
        var keyStr = key.ToString();
        
        if (_enteringInitials)
        {
            HandleInitialsInput(key);
            return;
        }
        
        if (_enteringStartingLevel)
        {
            HandleStartingLevelInput(key);
            return;
        }
        
        if (_enteringPlayerCount)
        {
            HandlePlayerCountInput(key);
            return;
        }
        
        if (_enteringGameId)
        {
            HandleGameIdInput(key);
            return;
        }
        
        if (_enteringServerConfig)
        {
            HandleServerConfigInput(key);
            return;
        }
        
        if (_waitingForPlayers)
        {
            // During wait, allow Escape to cancel
            if (keyStr.Contains("Esc") || keyStr.Contains("Escape"))
            {
                _waitingForPlayers = false;
                _enteringPlayerCount = false;
                _enteringGameId = false;
                _showMenu = true;
            }
            return;
        }
        
        if (!_showMenu || _clearingScreen)
            return;
        
        // Handle menu navigation
        if (keyStr.Contains("Up") || keyStr.Contains("8"))
        {
            _selectedMenuIndex = (_selectedMenuIndex - 1 + _menuItems.Length) % _menuItems.Length;
        }
        else if (keyStr.Contains("Down") || keyStr.Contains("2"))
        {
            _selectedMenuIndex = (_selectedMenuIndex + 1) % _menuItems.Length;
        }
        else if (keyStr.Contains("Enter"))
        {
            HandleMenuSelection();
        }
        else
        {
            char keyChar = keyStr.Length > 0 ? char.ToUpper(keyStr[0]) : '\0';
            switch (keyChar)
            {
                case 'S':
                    _selectedMenuIndex = 0;
                    HandleMenuSelection();
                    break;
                case 'J':
                    _selectedMenuIndex = 1;
                    HandleMenuSelection();
                    break;
                case 'I':
                    _selectedMenuIndex = 2;
                    HandleMenuSelection();
                    break;
                case 'E':
                case 'X':
                    _selectedMenuIndex = 4; // Exit is at index 4
                    HandleMenuSelection();
                    break;
            }
        }
    }
    
    private void HandleMenuSelection()
    {
        // Pause demo when menu option is selected
        _demoActive = false;
        
        switch (_selectedMenuIndex)
        {
            case 0: // Start a New Game
                // First prompt for starting level
                _enteringStartingLevel = true;
                _startingLevelInput = "1";
                _selectedStartingLevel = 1;
                break;
                
            case 1: // Join an Existing Game
                // Prompt for game ID
                _enteringGameId = true;
                _gameIdInput = "";
                break;
                
            case 2: // Initials
                _enteringInitials = true;
                _initialsInput = "";
                break;
                
            case 3: // Configure Server
                _enteringServerConfig = true;
                _serverAddressInput = _config.ServerAddress;
                _serverPortInput = _config.ServerPort.ToString();
                break;
                
            case 4: // Exit
                OnExit?.Invoke();
                break;
        }
    }
    
    private void HandleInitialsInput(Key key)
    {
        var keyStr = key.ToString();
        
        // Handle backspace
        if (keyStr.Contains("Backspace"))
        {
            if (_initialsInput.Length > 0)
            {
                // Use range operator instead of Substring to avoid allocation
                _initialsInput = _initialsInput[..^1];
            }
            return;
        }
        
        // Handle Escape to cancel
        if (keyStr.Contains("Esc") || keyStr.Contains("Escape"))
        {
            _enteringInitials = false;
            _initialsInput = "";
            // Resume demo if past animation phase
            double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
            if (elapsedSeconds >= 2.0 && !_enteringStartingLevel && !_enteringPlayerCount && !_enteringGameId && !_enteringServerConfig)
            {
                _demoActive = true;
            }
            return;
        }
        
        // Get character from key
        char? ch = GetCharFromKey(key);
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
                    OnInitialsChanged?.Invoke(_initialsInput);
                    _enteringInitials = false;
                    // Resume demo if past animation phase
                    double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
                    if (elapsedSeconds >= 2.0)
                    {
                        _demoActive = true;
                    }
                }
            }
        }
    }
    
    private void HandleStartingLevelInput(Key key)
    {
        var keyStr = key.ToString();
        
        // Handle backspace
        if (keyStr.Contains("Backspace"))
        {
            if (_startingLevelInput.Length > 0)
            {
                // Use range operator instead of Substring to avoid allocation
                _startingLevelInput = _startingLevelInput[..^1];
            }
            return;
        }
        
        // Handle Escape to cancel
        if (keyStr.Contains("Esc") || keyStr.Contains("Escape"))
        {
            _enteringStartingLevel = false;
            _startingLevelInput = "1";
            _selectedStartingLevel = 1;
            // Resume demo if past animation phase
            double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
            if (elapsedSeconds >= 2.0)
            {
                _demoActive = true;
            }
            return;
        }
        
        // Handle Enter to confirm
        if (keyStr.Contains("Enter"))
        {
            if (int.TryParse(_startingLevelInput, out int level) && level >= 1 && level <= 50)
            {
                _selectedStartingLevel = level;
                _enteringStartingLevel = false;
                // Now prompt for player count
                _enteringPlayerCount = true;
                _playerCountInput = "1";
                // Demo stays paused during player count input
            }
            return;
        }
        
        // Get character from key
        char? ch = GetCharFromKey(key);
        if (ch.HasValue)
        {
            // Only allow digits
            if (ch.Value >= '0' && ch.Value <= '9')
            {
                // Limit to 2 digits (max level 50)
                if (_startingLevelInput.Length < 2)
                {
                    _startingLevelInput += ch.Value;
                }
            }
        }
    }
    
    private void HandlePlayerCountInput(Key key)
    {
        var keyStr = key.ToString();
        
        // Handle backspace
        if (keyStr.Contains("Backspace"))
        {
            if (_playerCountInput.Length > 0)
            {
                // Use range operator instead of Substring to avoid allocation
                _playerCountInput = _playerCountInput[..^1];
            }
            return;
        }
        
        // Handle Escape to cancel
        if (keyStr.Contains("Esc") || keyStr.Contains("Escape"))
        {
            _enteringPlayerCount = false;
            _playerCountInput = "1";
            // Go back to level selection
            _enteringStartingLevel = true;
            _startingLevelInput = _selectedStartingLevel.ToString();
            // Demo stays paused during input
            return;
        }
        
        // Handle Enter to confirm
        if (keyStr.Contains("Enter"))
        {
            if (int.TryParse(_playerCountInput, out int count) && count >= 1 && count <= 5)
            {
                _maxPlayers = count;
                _enteringPlayerCount = false;
                // Set the starting level in game state before starting
                _gameState.Level = _selectedStartingLevel;
                OnStartMultiplayerGame?.Invoke(count);
            }
            return;
        }
        
        // Get character from key
        char? ch = GetCharFromKey(key);
        if (ch == null)
            return;
        
        // Validate character (0-9)
        if (ch >= '0' && ch <= '9')
        {
            if (_playerCountInput.Length < 1)
            {
                _playerCountInput = ch.Value.ToString();
            }
            else if (_playerCountInput.Length == 1)
            {
                // Avoid string concatenation - use string interpolation
                string newInput = $"{_playerCountInput}{ch.Value}";
                if (int.TryParse(newInput, out int count) && count >= 1 && count <= 5)
                {
                    _playerCountInput = newInput;
                }
            }
        }
    }
    
    private void HandleGameIdInput(Key key)
    {
        var keyStr = key.ToString();
        
        // Handle backspace
        if (keyStr.Contains("Backspace"))
        {
            if (_gameIdInput.Length > 0)
            {
                // Use range operator instead of Substring to avoid allocation
                _gameIdInput = _gameIdInput[..^1];
            }
            return;
        }
        
        // Handle Escape to cancel
        if (keyStr.Contains("Esc") || keyStr.Contains("Escape"))
        {
            _enteringGameId = false;
            _gameIdInput = "";
            // Resume demo if past animation phase
            double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
            if (elapsedSeconds >= 2.0)
            {
                _demoActive = true;
            }
            return;
        }
        
        // Handle Enter to confirm
        if (keyStr.Contains("Enter"))
        {
            if (_gameIdInput.Length == 6)
            {
                _enteringGameId = false;
                OnJoinGame?.Invoke(_gameIdInput.ToUpper());
            }
            return;
        }
        
        // Get character from key
        char? ch = GetCharFromKey(key);
        if (ch == null)
            return;
        
        // Validate character (A-Z, 0-9)
        if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
        {
            // Convert to uppercase
            char upperChar = char.ToUpper(ch.Value);
            
            if (_gameIdInput.Length < 6)
            {
                _gameIdInput += upperChar;
                
                // If we have 6 characters, automatically submit
                if (_gameIdInput.Length == 6)
                {
                    _enteringGameId = false;
                    OnJoinGame?.Invoke(_gameIdInput);
                }
            }
        }
    }
    
    public void ShowWaitingForPlayers(string gameId, int maxPlayers, bool isHost)
    {
        bool wasAlreadyWaiting = _waitingForPlayers;
        _waitingForPlayers = true;
        _showMenu = false;
        _currentGameId = gameId;
        _maxPlayers = maxPlayers;
        _currentPlayerCount = isHost ? 1 : 0; // Host counts as 1
        
        Console.WriteLine($"[DEBUG] ShowWaitingForPlayers: gameId='{gameId}', _currentGameId='{_currentGameId}', wasAlreadyWaiting={wasAlreadyWaiting}");
        
        // Only reset timer if this is the first time showing the waiting screen
        if (!wasAlreadyWaiting)
        {
            _timeRemaining = MultiplayerWaitTimeSeconds;
            _joinWaitStartTime = DateTime.Now;
            _joinedPlayers.Clear();
            if (isHost)
            {
                _joinedPlayers.Add(_config.Initials); // Host is first player
            }
        }
    }
    
    public void UpdatePlayerJoin(string playerInitials)
    {
        if (!_joinedPlayers.Contains(playerInitials))
        {
            _joinedPlayers.Add(playerInitials);
        }
        _currentPlayerCount = _joinedPlayers.Count;
    }
    
    public void UpdatePlayerCount(int currentPlayers, int maxPlayers, int timeRemaining)
    {
        _currentPlayerCount = currentPlayers;
        _maxPlayers = maxPlayers;
        _timeRemaining = timeRemaining;
    }
    
    public void StartGame()
    {
        _waitingForPlayers = false;
        // Calculate level info for display (same format as level progression)
        int hiveCount = _gameState.GetHiveCountForLevel(_gameState.Level);
        int snipesPerHive = _gameState.GetSnipesPerHiveForLevel(_gameState.Level);
        int totalSnipes = hiveCount * snipesPerHive;
        string levelMessage = $"LEVEL {_gameState.Level} - {hiveCount} HIVES with {totalSnipes} SNIPES";
        StartClearingEffect(levelMessage, isStartingNewGame: true);
    }
    
    private char? GetCharFromKey(Key key)
    {
        // Get the string representation of the key
        string keyStr = key.ToString();
        
        // If the string is empty, no character
        if (string.IsNullOrEmpty(keyStr))
            return null;
        
        // For single character keys, return the character
        // This includes letters, numbers, and some special characters
        if (keyStr.Length == 1)
        {
            char ch = keyStr[0];
            // Return if it's a printable ASCII character
            if (ch >= 32 && ch <= 126)
                return ch;
        }
        
        // Check if it starts with "Key." (e.g., "Key.A", "Key.D0" for digit 0, etc.)
        if (keyStr.StartsWith("Key."))
        {
            // Use range operator instead of Substring to avoid allocation
            string keyPart = keyStr[4..]; // Remove "Key." prefix
            
            // Handle digit keys (D0-D9)
            if (keyPart.StartsWith("D") && keyPart.Length == 2 && char.IsDigit(keyPart[1]))
            {
                return keyPart[1]; // Return the digit
            }
            
            // Handle letter keys (single letter after "Key.")
            if (keyPart.Length == 1 && char.IsLetter(keyPart[0]))
            {
                return keyPart[0];
            }
        }
        
        return null;
    }
    
    private void DrawMenu(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Calculate menu position (centered below banner, with clear separation)
        int bannerEndY = height / 4 + 9; // Banner ends at 1/4 + 9 rows
        int menuBoxHeight = _menuItems.Length + 4; // Items + 2 gaps + title border + bottom border
        int menuStartY = bannerEndY + 5; // 5 rows gap after banner
        
        // Calculate box dimensions
        int boxWidth = 40; // Fixed width for menu box
        int boxX = (width - boxWidth) / 2; // Center horizontally
        int boxY = menuStartY;
        int padding = 2; // Padding from box borders
        
        // Draw box border (using single-line characters)
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        
        // Top border with title
        Move(boxX, boxY);
        this.AddChar('┌');
        // Draw horizontal line with title
        string title = " Options ";
        int titleStartX = boxX + (boxWidth - title.Length) / 2;
        for (int x = boxX + 1; x < boxX + boxWidth - 1; x++)
        {
            Move(x, boxY);
            if (x >= titleStartX && x < titleStartX + title.Length)
            {
                this.AddChar(title[x - titleStartX]);
            }
            else
            {
                this.AddChar('─');
            }
        }
        Move(boxX + boxWidth - 1, boxY);
        this.AddChar('┐');
        
        // Calculate actual menu items (with gaps after Initials and Configure Server)
        int menuItemCount = _menuItems.Length + 2; // +2 for gaps after Initials and Configure Server
        
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
                        initialsPart = $"{_initialsInput}▊"; // Caret at second position
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
                SetAttribute(new DrawingAttribute(Color.Blue, Color.White));
                // Fill the entire line width (minus borders)
                for (int x = boxX + 1; x < boxX + boxWidth - 1; x++)
                {
                    Move(x, menuY);
                    this.AddChar(' ');
                }
            }
            else
            {
                // Not selected: white text, blue background
                SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
            }
            
            // Draw menu text character by character (left-justified with padding)
            // Highlight first letter in yellow
            Move(menuX, menuY);
            bool firstLetterDrawn = false;
            foreach (char c in menuText)
            {
                if (!firstLetterDrawn && char.IsLetter(c))
                {
                    // First letter - draw in cyan
                    SetAttribute(new DrawingAttribute(Color.Cyan, i == _selectedMenuIndex ? Color.White : Color.Blue));
                    this.AddChar(c);
                    firstLetterDrawn = true;
                    // Reset to menu color
                    SetAttribute(new DrawingAttribute(i == _selectedMenuIndex ? Color.Blue : Color.White, i == _selectedMenuIndex ? Color.White : Color.Blue));
                }
                else
                {
                    // Regular character - use current menu color
                    this.AddChar(c);
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
                            this.AddChar(c);
                        }
                        else if (c == '_')
                        {
                            // Placeholder - use current selection color
                            this.AddChar(c);
                        }
                        else
                        {
                            // Typed letter - use purple
                            SetAttribute(new DrawingAttribute(Color.Magenta, i == _selectedMenuIndex ? Color.White : Color.Blue));
                            this.AddChar(c);
                            // Reset to menu color
                            SetAttribute(new DrawingAttribute(i == _selectedMenuIndex ? Color.Blue : Color.White, i == _selectedMenuIndex ? Color.White : Color.Blue));
                        }
                    }
                }
                else
                {
                    // Draw initials in Cyan
                    SetAttribute(new DrawingAttribute(Color.Cyan, i == _selectedMenuIndex ? Color.White : Color.Blue));
                    this.AddString(initialsPart);
                    // Reset to menu color
                    SetAttribute(new DrawingAttribute(i == _selectedMenuIndex ? Color.Blue : Color.White, i == _selectedMenuIndex ? Color.White : Color.Blue));
                }
            }
            
            itemIndex++;
            
            // Add gap after Initials option (index 2) and Configure Server (index 3)
            if (i == 2 || i == 3)
            {
                itemIndex++; // Skip a row for gap
            }
        }
        
        // Left and right borders for each menu item row
        for (int row = 1; row <= menuItemCount; row++)
        {
            int y = boxY + row;
            SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
            Move(boxX, y);
            this.AddChar('│');
            Move(boxX + boxWidth - 1, y);
            this.AddChar('│');
        }
        
        // Bottom border (after all menu items)
        int bottomY = boxY + menuItemCount + 1;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(boxX, bottomY);
        this.AddChar('└');
        for (int x = boxX + 1; x < boxX + boxWidth - 1; x++)
        {
            Move(x, bottomY);
            this.AddChar('─');
        }
        Move(boxX + boxWidth - 1, bottomY);
        this.AddChar('┘');
        
        // Draw server status at the bottom of the screen
        DrawServerStatus(width, height);
    }
    
    private void DrawServerStatus(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Check server status periodically
        if ((DateTime.Now - _lastServerCheck).TotalSeconds >= ServerCheckIntervalSeconds)
        {
            // Only start a new check if one isn't already running
            if (_serverStatusCheckTask == null || _serverStatusCheckTask.IsCompleted)
            {
                CheckServerStatus();
            }
            _lastServerCheck = DateTime.Now;
        }
        
        string serverUrl = _config.GetServerUrl();
        // Avoid string concatenation - build status text efficiently
        Color statusColor;
        string statusSuffix;
        if (_serverStatus == true)
        {
            statusColor = Color.Green;
            statusSuffix = " [ONLINE]";
        }
        else if (_serverStatus == false)
        {
            statusColor = Color.Red;
            statusSuffix = " [OFFLINE]";
        }
        else
        {
            statusColor = Color.Gray;
            statusSuffix = " [CHECKING...]";
        }
        string statusText = $"Server: {serverUrl}{statusSuffix}";
        
        // Draw at bottom of screen (one row up from absolute bottom)
        int statusY = height - 2;
        int statusX = (width - statusText.Length) / 2;
        
        SetAttribute(new DrawingAttribute(statusColor, Color.Blue));
        Move(statusX, statusY);
        this.AddString(statusText);
    }
    
    private void CheckServerStatus()
    {
        // Run async check in background with cancellation support
        _serverStatusCheckTask = Task.Run(async () =>
        {
            try
            {
                // Check if cancellation was requested before starting
                if (_serverStatusCheckCancellation.Token.IsCancellationRequested)
                    return;
                    
                string serverUrl = _config.GetServerUrl();
                
                // Since server uses HTTP/2 only (no HTTP/1.1 health check endpoint),
                // we need to test gRPC connectivity instead
                // Enable HTTP/2 unencrypted support for the check
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                
                using (var channel = Grpc.Net.Client.GrpcChannel.ForAddress(serverUrl))
                {
                    // Try to connect by creating a client (lightweight test)
                    // Don't actually make a call, just test if channel can be created
                    var testClient = new NSnipes.GrpcServer.GameService.GameServiceClient(channel);
                    
                    // Try a simple call with a short timeout to test connectivity
                    // Use a non-existent game ID to avoid side effects, but test if server responds
                    try
                    {
                        // Link cancellation tokens
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            _serverStatusCheckCancellation.Token,
                            new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);
                            
                        var request = new NSnipes.GrpcServer.JoinRequest
                        {
                            GameId = "TEST_CONNECTION",
                            PlayerId = "TEST",
                            Initials = "TEST"
                        };
                        await testClient.JoinGameAsync(request, cancellationToken: linkedCts.Token).ConfigureAwait(false);
                        // If we get here, server is responding (even if game doesn't exist)
                        if (!_serverStatusCheckCancellation.Token.IsCancellationRequested)
                        {
                            _serverStatus = true;
                        }
                    }
                    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound || 
                                                             ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
                    {
                        // Server responded (game not found is expected), so server is online
                        if (!_serverStatusCheckCancellation.Token.IsCancellationRequested)
                        {
                            _serverStatus = true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout or cancellation - only update status if not cancelled
                        if (!_serverStatusCheckCancellation.Token.IsCancellationRequested)
                        {
                            _serverStatus = false;
                        }
                    }
                    catch
                    {
                        // Other errors - assume server is offline (only if not cancelled)
                        if (!_serverStatusCheckCancellation.Token.IsCancellationRequested)
                        {
                            _serverStatus = false;
                        }
                    }
                }
            }
            catch
            {
                // Only update status if not cancelled
                if (!_serverStatusCheckCancellation.Token.IsCancellationRequested)
                {
                    _serverStatus = false;
                }
            }
        }, _serverStatusCheckCancellation.Token);
    }
    
    private void DrawIntroPlayer(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Clear previous position if it was on screen and different from current
        if (_introPlayerPrevX != _introPlayerX && _introPlayerPrevX >= 0 && _introPlayerPrevX < width)
        {
            SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
            // Clear the 2x3 player area
            for (int row = 0; row < 3; row++)
            {
                int clearY = _introPlayerY + row;
                if (clearY >= 0 && clearY < height)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        int clearX = _introPlayerPrevX + col;
                        if (clearX >= 0 && clearX < width)
                        {
                            Move(clearX, clearY);
                            this.AddChar(' ');
                        }
                    }
                }
            }
        }
        
        // Only draw if player is on screen
        if (_introPlayerX < -2 || _introPlayerX > width)
            return;
        
        // Get current time for animation
        DateTime now = DateTime.Now;
        var eyes = now.Millisecond < 500 ? "ÔÔ" : "OO";
        var mouth = now.Millisecond < 500 ? "◄►" : "◂▸";
        
        // Draw player at intro position
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        
        // Draw eyes
        if (_introPlayerX >= 0 && _introPlayerX + 1 < width && _introPlayerY >= 0 && _introPlayerY < height)
        {
            Move(_introPlayerX, _introPlayerY);
            this.AddString(eyes);
        }
        
        // Draw mouth
        if (_introPlayerX >= 0 && _introPlayerX + 1 < width && _introPlayerY + 1 >= 0 && _introPlayerY + 1 < height)
        {
            Move(_introPlayerX, _introPlayerY + 1);
            this.AddString(mouth);
        }
        
        // Draw initials
        if (_introPlayerX >= 0 && _introPlayerX + 1 < width && _introPlayerY + 2 >= 0 && _introPlayerY + 2 < height)
        {
            Move(_introPlayerX, _introPlayerY + 2);
            this.AddString(_config.Initials);
        }
    }
    
    private void DrawBanner(int startX, int screenHeight)
    {
        if (!IsInitialized)
            return;
            
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        
        // Banner is 7 rows tall, with 1 blank row above and below
        // Position banner higher up (about 1/3 from top) to leave room for menu below
        int bannerStartY = screenHeight / 4; // 1/4 from top instead of center
        
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
                        if (x >= 0 && x < Frame.Width)
                        {
                            Move(x, y);
                            this.AddChar(letter[row][col]);
                        }
                    }
                }
            }
        }
    }
    
    private void DrawClearingEffect(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        const int StatusBarHeight = 2; // First 2 rows reserved for status information
        
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
        
        // Draw expanding rectangle and reveal map underneath
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        
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
                    Move(x, y);
                    this.AddChar('*');
                }
                else if (!inMessageArea && _getMapCharAtPosition != null)
                {
                    // Outside rectangle and not in message area - draw map character
                    char mapChar = _getMapCharAtPosition(x, y - StatusBarHeight);
                    Move(x, y);
                    this.AddChar(mapChar);
                }
                // If in message area, skip drawing here (will draw message below)
            }
        }
        
        _clearingRectSize = newRectSize;
        
        // Draw message centered on screen (if provided) with spacing around it
        if (!string.IsNullOrEmpty(_clearingMessage) && !string.IsNullOrEmpty(messageWithSpacing))
        {
            // Draw blank rows above and below the message (spaces, not asterisks)
            SetAttribute(new DrawingAttribute(Color.White, Color.Black));
            
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
                        Move(x, y);
                        this.AddChar(' '); // Use spaces, not asterisks
                    }
                }
            }
            
            // Draw the message on top
            Move(messageX, messageY);
            this.AddString(messageWithSpacing);
        }
        
        // When rectangle covers entire screen, transition to game
        if (newRectSize >= maxSize)
        {
            // Normal game start or respawn
            _isActive = false;
            _clearingScreen = false;
            Visible = false; // Hide the intro screen
            
            // Only call OnStartGame if we're actually starting a new game (from menu)
            // If this is a respawn, just end the clearing effect without resetting game state
            if (_isStartingNewGame)
            {
                OnStartGame?.Invoke(_gameState.Level);
            }
            else
            {
                // This is a respawn - notify that clearing effect completed
                OnRespawnComplete?.Invoke();
            }
            
            _isStartingNewGame = false; // Reset flag
        }
    }
    
    private void DrawStartingLevelInput(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Fill screen with blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            this.AddString(new string(' ', width));
        }
        
        // Draw prompt
        string prompt = "Select Starting Level (1-50):";
        int promptX = (width - prompt.Length) / 2;
        int promptY = height / 2 - 2;
        
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(promptX, promptY);
        this.AddString(prompt);
        
        // Draw input with caret - use string interpolation instead of concatenation
        string inputDisplay = $"{_startingLevelInput}▊";
        int inputX = (width - inputDisplay.Length) / 2;
        int inputY = promptY + 2;
        
        SetAttribute(new DrawingAttribute(Color.Magenta, Color.Blue));
        Move(inputX, inputY);
        this.AddString(inputDisplay);
        
        // Draw instructions
        string instructions = "Press ENTER to confirm, ESC to cancel";
        int instX = (width - instructions.Length) / 2;
        int instY = inputY + 2;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(instX, instY);
        this.AddString(instructions);
    }
    
    private void DrawPlayerCountInput(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Fill screen with blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            this.AddString(new string(' ', width));
        }
        
        // Draw prompt
        string prompt = "Enter number of players (1-5):";
        int promptX = (width - prompt.Length) / 2;
        int promptY = height / 2 - 2;
        
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(promptX, promptY);
        this.AddString(prompt);
        
        // Draw input with caret - use string interpolation instead of concatenation
        string inputDisplay = $"{_playerCountInput}▊";
        int inputX = (width - inputDisplay.Length) / 2;
        int inputY = promptY + 2;
        
        SetAttribute(new DrawingAttribute(Color.Magenta, Color.Blue));
        Move(inputX, inputY);
        this.AddString(inputDisplay);
        
        // Draw instructions
        string instructions = "Press ENTER to confirm, ESC to cancel";
        int instX = (width - instructions.Length) / 2;
        int instY = inputY + 2;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(instX, instY);
        this.AddString(instructions);
    }
    
    private void DrawGameIdInput(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Fill screen with blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            this.AddString(new string(' ', width));
        }
        
        // Draw prompt
        string prompt = "Enter Game ID (6 characters):";
        int promptX = (width - prompt.Length) / 2;
        int promptY = height / 2 - 2;
        
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(promptX, promptY);
        this.AddString(prompt);
        
        // Draw input with caret
        string inputDisplay = _gameIdInput.PadRight(6, '_');
        if (_gameIdInput.Length < 6)
        {
            inputDisplay = $"{_gameIdInput}▊{new string('_', 5 - _gameIdInput.Length)}";
        }
        int inputX = (width - inputDisplay.Length) / 2;
        int inputY = promptY + 2;
        
        SetAttribute(new DrawingAttribute(Color.Magenta, Color.Blue));
        Move(inputX, inputY);
        foreach (char c in inputDisplay)
        {
            if (c == '▊')
            {
                this.AddChar(c);
            }
            else if (c == '_')
            {
                SetAttribute(new DrawingAttribute(Color.Gray, Color.Blue));
                this.AddChar(c);
                SetAttribute(new DrawingAttribute(Color.Magenta, Color.Blue));
            }
            else
            {
                this.AddChar(c);
            }
        }
        
        // Draw instructions
        string instructions = "Press ESC to cancel";
        int instX = (width - instructions.Length) / 2;
        int instY = inputY + 2;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(instX, instY);
        this.AddString(instructions);
    }
    
    private void DrawWaitingForPlayers(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Fill screen with blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            this.AddString(new string(' ', width));
        }
        
        // Update time remaining
        int elapsed = (int)(DateTime.Now - _joinWaitStartTime).TotalSeconds;
        _timeRemaining = Math.Max(0, 60 - elapsed);
        
        // Draw game ID (show "Connecting..." only if game ID is null/empty, otherwise show actual game ID)
        string displayGameId = string.IsNullOrEmpty(_currentGameId) ? "Connecting..." : _currentGameId;
        string gameIdText = $"Game ID: {displayGameId}";
        
        // Debug output (only log occasionally to avoid spam)
        if (DateTime.Now.Millisecond % 1000 < 50) // Log roughly once per second
        {
            Console.WriteLine($"[DEBUG] DrawWaitingForPlayers: _currentGameId='{_currentGameId}', displayGameId='{displayGameId}'");
        }
        
        int gameIdX = (width - gameIdText.Length) / 2;
        int gameIdY = height / 4;
        SetAttribute(new DrawingAttribute(Color.Yellow, Color.Blue));
        Move(gameIdX, gameIdY);
        this.AddString(gameIdText);
        
        // Draw waiting message
        string waitingText = "Waiting for players...";
        int waitingX = (width - waitingText.Length) / 2;
        int waitingY = gameIdY + 3;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(waitingX, waitingY);
        this.AddString(waitingText);
        
        // Draw player count
        string countText = $"{_currentPlayerCount} of {_maxPlayers} players joined";
        int countX = (width - countText.Length) / 2;
        int countY = waitingY + 2;
        Move(countX, countY);
        this.AddString(countText);
        
        // Draw time remaining
        string timeText = $"Time remaining: {_timeRemaining} seconds";
        int timeX = (width - timeText.Length) / 2;
        int timeY = countY + 2;
        Move(timeX, timeY);
        this.AddString(timeText);
        
        // Draw joined players list
        if (_joinedPlayers.Count > 0)
        {
            int listY = timeY + 3;
            string listHeader = "Players joined:";
            int listHeaderX = (width - listHeader.Length) / 2;
            Move(listHeaderX, listY);
            this.AddString(listHeader);
            
            int playerY = listY + 2;
            foreach (var initials in _joinedPlayers)
            {
                string playerText = $"  • {initials}";
                int playerX = (width - playerText.Length) / 2;
                SetAttribute(new DrawingAttribute(Color.Cyan, Color.Blue));
                Move(playerX, playerY);
                this.AddString(playerText);
                playerY++;
            }
        }
        
        // Draw instructions
        string instructions = "Press ESC to cancel";
        int instX = (width - instructions.Length) / 2;
        int instY = height - 2;
        SetAttribute(new DrawingAttribute(Color.Gray, Color.Blue));
        Move(instX, instY);
        this.AddString(instructions);
    }
    
    private void HandleServerConfigInput(Key key)
    {
        var keyStr = key.ToString();
        
        // Handle backspace
        if (keyStr.Contains("Backspace"))
        {
            if (_editingServerAddress && _serverAddressInput.Length > 0)
            {
                // Use range operator instead of Substring to avoid allocation
                _serverAddressInput = _serverAddressInput[..^1];
            }
            else if (!_editingServerAddress && _serverPortInput.Length > 0)
            {
                // Use range operator instead of Substring to avoid allocation
                _serverPortInput = _serverPortInput[..^1];
            }
            return;
        }
        
        // Handle Tab to switch fields
        if (keyStr.Contains("Tab"))
        {
            _editingServerAddress = !_editingServerAddress;
            return;
        }
        
        // Handle Escape to cancel
        if (keyStr.Contains("Esc") || keyStr.Contains("Escape"))
        {
            _enteringServerConfig = false;
            _serverAddressInput = "";
            _serverPortInput = "";
            _editingServerAddress = true;
            // Resume demo if past animation phase
            double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
            if (elapsedSeconds >= 2.0)
            {
                _demoActive = true;
            }
            return;
        }
        
        // Handle Enter to confirm
        if (keyStr.Contains("Enter"))
        {
            if (_editingServerAddress)
            {
                // Switch to port field
                _editingServerAddress = false;
                return;
            }
            else
            {
                // Save configuration
                if (!string.IsNullOrWhiteSpace(_serverAddressInput) && 
                    int.TryParse(_serverPortInput, out int port) && port > 0 && port <= 65535)
                {
                    _config.ServerAddress = _serverAddressInput.Trim();
                    _config.ServerPort = port;
                    _config.Save();
                    _serverStatus = null; // Reset status to force recheck
                    _enteringServerConfig = false;
                    _serverAddressInput = "";
                    _serverPortInput = "";
                    _editingServerAddress = true;
                    // Resume demo if past animation phase
                    double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
                    if (elapsedSeconds >= 2.0)
                    {
                        _demoActive = true;
                    }
                }
                return;
            }
        }
        
        // Get character from key
        char? ch = GetCharFromKey(key);
        if (ch == null)
            return;
        
        if (_editingServerAddress)
        {
            // Allow alphanumeric, dots, hyphens for address
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || 
                (ch >= '0' && ch <= '9') || ch == '.' || ch == '-')
            {
                if (_serverAddressInput.Length < 50) // Reasonable limit
                {
                    _serverAddressInput += char.ToLower(ch.Value);
                }
            }
        }
        else
        {
            // Only digits for port
            if (ch >= '0' && ch <= '9')
            {
                string newPort = _serverPortInput + ch.Value;
                if (int.TryParse(newPort, out int port) && port >= 0 && port <= 65535)
                {
                    _serverPortInput = newPort;
                }
            }
        }
    }
    
    private void DrawServerConfigInput(int width, int height)
    {
        if (!IsInitialized)
            return;
        
        // Fill screen with blue background
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            this.AddString(new string(' ', width));
        }
        
        // Draw title
        string title = "Configure Server";
        int titleX = (width - title.Length) / 2;
        int titleY = height / 2 - 4;
        SetAttribute(new DrawingAttribute(Color.Yellow, Color.Blue));
        Move(titleX, titleY);
        this.AddString(title);
        
        // Draw address prompt
        string addressPrompt = "Server Address:";
        int addressPromptX = (width - 50) / 2;
        int addressPromptY = titleY + 2;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(addressPromptX, addressPromptY);
        this.AddString(addressPrompt);
        
        // Draw address input
        string addressDisplay = _serverAddressInput.Length > 0 ? _serverAddressInput : _config.ServerAddress;
        int addressInputX = addressPromptX;
        int addressInputY = addressPromptY + 1;
        SetAttribute(new DrawingAttribute(_editingServerAddress ? Color.Magenta : Color.Gray, Color.Blue));
        Move(addressInputX, addressInputY);
        this.AddString(addressDisplay);
        if (_editingServerAddress && _serverAddressInput.Length == 0)
        {
            this.AddChar('▊');
        }
        
        // Draw port prompt
        string portPrompt = "Server Port:";
        int portPromptX = addressPromptX;
        int portPromptY = addressInputY + 2;
        SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
        Move(portPromptX, portPromptY);
        this.AddString(portPrompt);
        
        // Draw port input
        string portDisplay = _serverPortInput.Length > 0 ? _serverPortInput : _config.ServerPort.ToString();
        int portInputX = portPromptX;
        int portInputY = portPromptY + 1;
        SetAttribute(new DrawingAttribute(!_editingServerAddress ? Color.Magenta : Color.Gray, Color.Blue));
        Move(portInputX, portInputY);
        this.AddString(portDisplay);
        if (!_editingServerAddress && _serverPortInput.Length == 0)
        {
            this.AddChar('▊');
        }
        
        // Draw instructions
        string instructions = "Press TAB to switch fields, ENTER to confirm, ESC to cancel";
        int instX = (width - instructions.Length) / 2;
        int instY = portInputY + 2;
        SetAttribute(new DrawingAttribute(Color.Gray, Color.Blue));
        Move(instX, instY);
        this.AddString(instructions);
    }
    
    /// <summary>
    /// Cleanup method to cancel background server status checks
    /// Should be called when IntroScreen is no longer needed
    /// </summary>
    public void Cleanup()
    {
        _serverStatusCheckCancellation.Cancel();
        
        // Wait for ongoing check to complete (with timeout)
        if (_serverStatusCheckTask != null && !_serverStatusCheckTask.IsCompleted)
        {
            try
            {
                _serverStatusCheckTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore exceptions during cleanup
            }
        }
        
        _serverStatusCheckCancellation.Dispose();
    }
    
    // ========== Demo Animation Methods ==========
    
    private void InitializeDemoMode(int width, int height)
    {
        _demoActive = true;
        _demoUpdateTimer = DateTime.Now;
        _demoSpawnTimer = DateTime.Now;
        
        // Initialize players at positions flanking the centered logo
        int bannerWidth = 7 * 7 + 6 * 2;
        int bannerX = (width - bannerWidth) / 2;
        int bannerStartY = height / 4;
        int playerY = bannerStartY + 1 + 3;
        
        if (_demoPlayers.Count == 0)
        {
            _demoPlayers.Add(new DemoPlayer("BD", Terminal.Gui.Drawing.Color.White, "demo_player_1")
            {
                X = bannerX + bannerWidth + 5,
                Y = playerY,
                PreviousX = bannerX + bannerWidth + 5,
                PreviousY = playerY,
                IsAlive = true
            });
            _demoPlayers.Add(new DemoPlayer("NP", Terminal.Gui.Drawing.Color.Yellow, "demo_player_2")
            {
                X = bannerX - 7,
                Y = playerY,
                PreviousX = bannerX - 7,
                PreviousY = playerY,
                IsAlive = true
            });
        }
        
        // Clear existing demo entities
        _demoSnipes.Clear();
        _demoBullets.Clear();
        
        // Invalidate cached bounds
        _cachedMenuBounds = null;
        _cachedLogoBounds = null;
    }
    
    private (int x, int y, int width, int height) GetMenuBounds(int width, int height)
    {
        if (_cachedMenuBounds.HasValue)
            return _cachedMenuBounds.Value;
        
        int bannerEndY = height / 4 + 9;
        int menuBoxHeight = _menuItems.Length + 4;
        int menuStartY = bannerEndY + 5;
        int boxWidth = 40;
        int boxX = (width - boxWidth) / 2;
        
        var bounds = (x: boxX, y: menuStartY, width: boxWidth, height: menuBoxHeight + 2);
        _cachedMenuBounds = bounds;
        return bounds;
    }
    
    private (int x, int y, int width, int height) GetLogoBounds(int width, int height)
    {
        if (_cachedLogoBounds.HasValue)
            return _cachedLogoBounds.Value;
        
        int bannerWidth = 7 * 7 + 6 * 2;
        int bannerX = (width - bannerWidth) / 2;
        int bannerStartY = height / 4;
        
        var bounds = (x: bannerX, y: bannerStartY, width: bannerWidth, height: 9);
        _cachedLogoBounds = bounds;
        return bounds;
    }
    
    private static double CalculateSquaredDistance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return dx * dx + dy * dy;
    }
    
    private static bool IsWithinRadiusSquared(double x1, double y1, double x2, double y2, double radiusSquared)
    {
        return CalculateSquaredDistance(x1, y1, x2, y2) <= radiusSquared;
    }
    
    private bool IsPositionValid(double x, double y, int width, int height, 
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds,
        ReadOnlySpan<DemoPlayer> players, ReadOnlySpan<Snipe> snipes)
    {
        // Check boundaries
        if (x < 0 || x >= width || y < 0 || y >= height)
            return false;
        
        // Check menu overlap
        if (x >= menuBounds.x && x < menuBounds.x + menuBounds.w &&
            y >= menuBounds.y && y < menuBounds.y + menuBounds.h)
            return false;
        
        // Check logo overlap
        if (x >= logoBounds.x && x < logoBounds.x + logoBounds.w &&
            y >= logoBounds.y && y < logoBounds.y + logoBounds.h)
            return false;
        
        // Check player overlap
        foreach (var player in players)
        {
            if (player.IsAlive && IsWithinRadiusSquared(x, y, player.X, player.Y, CollisionRadiusSquared * 4))
                return false;
        }
        
        // Check snipe overlap
        foreach (var snipe in snipes)
        {
            if (snipe.IsAlive && IsWithinRadiusSquared(x, y, snipe.X, snipe.Y, CollisionRadiusSquared * 4))
                return false;
        }
        
        return true;
    }
    
    private (double x, double y) GetRandomValidPosition(int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds,
        ReadOnlySpan<DemoPlayer> players, ReadOnlySpan<Snipe> snipes, int maxAttempts = 50)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            double x = _demoRandom.Next(0, width);
            double y = _demoRandom.Next(0, height);
            
            if (IsPositionValid(x, y, width, height, menuBounds, logoBounds, players, snipes))
                return (x, y);
        }
        
        // Fallback: return a position away from menu/logo
        return (_demoRandom.Next(0, width / 4), _demoRandom.Next(0, height / 4));
    }
    
    private List<(int dx, int dy)> GetValidDirections(double x, double y, int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds, bool isPlayer = false)
    {
        var directions = new List<(int dx, int dy)>(8);
        var allDirections = new[] { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };
        
        // Sprite dimensions: players are 2x3, snipes are 2x1 (arrow + '@' or '@' + arrow, side by side)
        int spriteWidth = isPlayer ? 2 : 2;
        int spriteHeight = isPlayer ? 3 : 1;
        
        foreach (var (dx, dy) in allDirections)
        {
            double newX = x + dx;
            double newY = y + dy;
            
            // Check boundaries - account for sprite size
            if (newX < 0 || newX + spriteWidth - 1 >= width || newY < 0 || newY + spriteHeight - 1 >= height)
                continue;
            
            // Check menu overlap - check all sprite positions
            bool overlapsMenu = false;
            for (int sx = 0; sx < spriteWidth && !overlapsMenu; sx++)
            {
                for (int sy = 0; sy < spriteHeight && !overlapsMenu; sy++)
                {
                    double checkX = newX + sx;
                    double checkY = newY + sy;
                    if (checkX >= menuBounds.x && checkX < menuBounds.x + menuBounds.w &&
                        checkY >= menuBounds.y && checkY < menuBounds.y + menuBounds.h)
                    {
                        overlapsMenu = true;
                    }
                }
            }
            if (overlapsMenu)
                continue;
            
            // Check logo overlap - check all sprite positions
            bool overlapsLogo = false;
            for (int sx = 0; sx < spriteWidth && !overlapsLogo; sx++)
            {
                for (int sy = 0; sy < spriteHeight && !overlapsLogo; sy++)
                {
                    double checkX = newX + sx;
                    double checkY = newY + sy;
                    if (checkX >= logoBounds.x && checkX < logoBounds.x + logoBounds.w &&
                        checkY >= logoBounds.y && checkY < logoBounds.y + logoBounds.h)
                    {
                        overlapsLogo = true;
                    }
                }
            }
            if (overlapsLogo)
                continue;
            
            directions.Add((dx, dy));
        }
        
        return directions;
    }
    
    private void UpdateDemo(int width, int height)
    {
        // Cache frame time
        _cachedFrameTime = DateTime.Now;
        
        // Throttle updates
        if ((_cachedFrameTime - _demoUpdateTimer).TotalMilliseconds < DemoUpdateIntervalMs)
            return;
        
        _demoUpdateTimer = _cachedFrameTime;
        
        // Get bounds (cached)
        var menuBounds = GetMenuBounds(width, height);
        var logoBounds = GetLogoBounds(width, height);
        
        // Phase 1: Capture snapshots (read-only, thread-safe)
        var playerSnapshots = _demoPlayers.ToArray();
        var snipeSnapshots = _demoSnipes.ToArray();
        var bulletSnapshots = _demoBullets.ToArray();
        
        // Phase 2: Parallel AI calculations (read-only operations)
        var playerMovements = new ConcurrentDictionary<int, (int dx, int dy)>();
        var snipeMovements = new ConcurrentDictionary<int, (int dx, int dy)>();
        
        // Calculate player movements in parallel
        Parallel.For(0, playerSnapshots.Length, i =>
        {
            var player = playerSnapshots[i];
            if (!player.IsAlive) return;
            
            var movement = CalculatePlayerMovement(player, width, height, menuBounds, logoBounds, playerSnapshots, snipeSnapshots);
            playerMovements[i] = movement;
        });
        
        // Calculate snipe movements in parallel
        Parallel.For(0, snipeSnapshots.Length, i =>
        {
            var snipe = snipeSnapshots[i];
            if (!snipe.IsAlive) return;
            
            var movement = CalculateSnipeMovement(snipe, width, height, menuBounds, logoBounds, playerSnapshots);
            snipeMovements[i] = movement;
        });
        
        // Phase 3: Sequential state updates (modifications to shared collections)
        for (int i = 0; i < _demoPlayers.Count; i++)
        {
            if (!_demoPlayers[i].IsAlive) continue;
            
            if (playerMovements.TryGetValue(i, out var movement))
            {
                _demoPlayers[i].PreviousX = _demoPlayers[i].X;
                _demoPlayers[i].PreviousY = _demoPlayers[i].Y;
                
                // Update position with boundary clamping (players are 2x3, so account for sprite size)
                double newX = _demoPlayers[i].X + movement.dx;
                double newY = _demoPlayers[i].Y + movement.dy;
                _demoPlayers[i].X = Math.Max(0, Math.Min(width - 2, newX)); // width - 2 because sprite is 2 wide
                _demoPlayers[i].Y = Math.Max(0, Math.Min(height - 3, newY)); // height - 3 because sprite is 3 tall
                
                // Only update direction if it changed (persistence counter handles this)
                if (movement.dx != _demoPlayers[i].DirectionX || movement.dy != _demoPlayers[i].DirectionY)
                {
                    _demoPlayers[i].DirectionX = movement.dx;
                    _demoPlayers[i].DirectionY = movement.dy;
                }
                _demoPlayers[i].LastMoveTime = _cachedFrameTime;
            }
        }
        
        for (int i = 0; i < _demoSnipes.Count; i++)
        {
            if (!_demoSnipes[i].IsAlive) continue;
            
            if (snipeMovements.TryGetValue(i, out var movement))
            {
                _demoSnipes[i].PreviousX = _demoSnipes[i].X;
                _demoSnipes[i].PreviousY = _demoSnipes[i].Y;
                
                // Update position with boundary clamping (snipes are 2x1, so account for sprite size)
                double newX = _demoSnipes[i].X + movement.dx;
                double newY = _demoSnipes[i].Y + movement.dy;
                _demoSnipes[i].X = (int)Math.Max(0, Math.Min(width - 2, newX)); // width - 2 because sprite is 2 wide (arrow + '@')
                _demoSnipes[i].Y = (int)Math.Max(0, Math.Min(height - 1, newY)); // height - 1 because sprite is 1 tall
                _demoSnipes[i].DirectionX = movement.dx;
                _demoSnipes[i].DirectionY = movement.dy;
                _demoSnipes[i].LastMoveTime = _cachedFrameTime;
            }
        }
        
        // Update bullets
        for (int i = _demoBullets.Count - 1; i >= 0; i--)
        {
            var bullet = _demoBullets[i];
            
            // Check if bullet expired
            if ((_cachedFrameTime - bullet.CreatedAt).TotalSeconds > Bullet.LifetimeSeconds)
            {
                _demoBullets.RemoveAt(i);
                continue;
            }
            
            // Check if bullet hit menu or logo
            bool hitMenu = bullet.X >= menuBounds.x && bullet.X < menuBounds.x + menuBounds.width &&
                          bullet.Y >= menuBounds.y && bullet.Y < menuBounds.y + menuBounds.height;
            bool hitLogo = bullet.X >= logoBounds.x && bullet.X < logoBounds.x + logoBounds.width &&
                          bullet.Y >= logoBounds.y && bullet.Y < logoBounds.y + logoBounds.height;
            
            if (hitMenu || hitLogo)
            {
                _demoBullets.RemoveAt(i);
                continue;
            }
            
            // Store previous position before updating
            double prevX = bullet.X;
            double prevY = bullet.Y;
            bullet.PreviousX = prevX;
            bullet.PreviousY = prevY;
            bullet.Update();
            
            // Handle screen edge bouncing (matching game's wall bouncing behavior)
            bool bounced = false;
            
            // Check left/right edges
            if (bullet.X < 0)
            {
                bullet.BounceX();
                bullet.X = 0;
                bounced = true;
            }
            else if (bullet.X >= width)
            {
                bullet.BounceX();
                bullet.X = width - 1;
                bounced = true;
            }
            
            // Check top/bottom edges
            if (bullet.Y < 0)
            {
                bullet.BounceY();
                bullet.Y = 0;
                bounced = true;
            }
            else if (bullet.Y >= height)
            {
                bullet.BounceY();
                bullet.Y = height - 1;
                bounced = true;
            }
            
            // If bullet bounced, move it back to previous position to avoid getting stuck
            if (bounced)
            {
                bullet.X = prevX;
                bullet.Y = prevY;
            }
        }
        
        // Phase 4: Collision detection (sequential)
        HandleCollisions(width, height);
        
        // Phase 5: Spawning
        if ((_cachedFrameTime - _demoSpawnTimer).TotalMilliseconds >= SnipeSpawnIntervalMs &&
            _demoSnipes.Count < MaxDemoSnipes)
        {
            SpawnDemoSnipe(width, height, menuBounds, logoBounds);
            _demoSpawnTimer = _cachedFrameTime;
        }
        
        // Phase 6: Shooting
        HandlePlayerShooting(width, height, menuBounds, logoBounds);
    }
    
    private (int dx, int dy) CalculatePlayerMovement(DemoPlayer player, int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds,
        DemoPlayer[] allPlayers, Snipe[] allSnipes)
    {
        // Check if it's time to move
        if ((_cachedFrameTime - player.LastMoveTime).TotalMilliseconds < DemoPlayer.MoveIntervalMs)
            return (0, 0);
        
        // Occasionally pause (don't move)
        if (_demoRandom.Next(100) < PlayerPauseChancePercent)
            return (0, 0);
        
        // Get valid directions (players are 2x3 sprites)
        var validDirections = GetValidDirections(player.X, player.Y, width, height, menuBounds, logoBounds, isPlayer: true);
        if (validDirections.Count == 0)
            return (0, 0);
        
        // Direction persistence: continue in same direction if still valid and haven't reached max persistence
        bool shouldChangeDirection = player.DirectionPersistenceCount >= DemoPlayer.MaxDirectionPersistence ||
                                     (player.DirectionPersistenceCount >= DemoPlayer.MinDirectionPersistence &&
                                      _demoRandom.Next(100) < 20); // 20% chance to change after min persistence
        
        if (!shouldChangeDirection && player.DirectionX != 0 || player.DirectionY != 0)
        {
            // Try to continue in current direction
            var currentDir = (player.DirectionX, player.DirectionY);
            if (validDirections.Contains(currentDir))
            {
                player.DirectionPersistenceCount++;
                return currentDir;
            }
        }
        
        // Need to change direction - check player avoidance first
        double minPlayerDistSquared = double.MaxValue;
        DemoPlayer? nearestPlayer = null;
        foreach (var other in allPlayers)
        {
            if (other == player || !other.IsAlive) continue;
            double distSq = CalculateSquaredDistance(player.X, player.Y, other.X, other.Y);
            if (distSq < minPlayerDistSquared)
            {
                minPlayerDistSquared = distSq;
                nearestPlayer = other;
            }
        }
        
        // If too close to another player, move away
        if (nearestPlayer != null && minPlayerDistSquared < PlayerAvoidanceRadius * PlayerAvoidanceRadius)
        {
            double dx = player.X - nearestPlayer.X;
            double dy = player.Y - nearestPlayer.Y;
            int dirX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int dirY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            
            if (validDirections.Contains((dirX, dirY)))
            {
                player.DirectionPersistenceCount = 0; // Reset persistence when avoiding
                return (dirX, dirY);
            }
        }
        
        // Check snipe homing
        double minSnipeDistSquared = double.MaxValue;
        Snipe? nearestSnipe = null;
        foreach (var snipe in allSnipes)
        {
            if (!snipe.IsAlive) continue;
            double distSq = CalculateSquaredDistance(player.X, player.Y, snipe.X, snipe.Y);
            if (distSq < minSnipeDistSquared)
            {
                minSnipeDistSquared = distSq;
                nearestSnipe = snipe;
            }
        }
        
        // If snipe is within homing radius, move toward it
        if (nearestSnipe != null && minSnipeDistSquared < PlayerSnipeHomingRadius * PlayerSnipeHomingRadius)
        {
            double dx = nearestSnipe.X - player.X;
            double dy = nearestSnipe.Y - player.Y;
            int dirX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int dirY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            
            if (validDirections.Contains((dirX, dirY)))
            {
                player.DirectionPersistenceCount = 0; // Reset persistence when homing
                return (dirX, dirY);
            }
        }
        
        // Random movement - reset persistence counter
        var newDir = validDirections[_demoRandom.Next(validDirections.Count)];
        player.DirectionPersistenceCount = 0;
        return newDir;
    }
    
    private (int dx, int dy) CalculateSnipeMovement(Snipe snipe, int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds,
        DemoPlayer[] allPlayers)
    {
        // In demo, snipes move more frequently (every update cycle if possible)
        // Check if it's time to move (use shorter interval for demo)
        const int DemoSnipeMoveIntervalMs = 100; // Move more frequently in demo
        if ((_cachedFrameTime - snipe.LastMoveTime).TotalMilliseconds < DemoSnipeMoveIntervalMs)
            return (0, 0);
        
        // Get or initialize direction persistence count for this snipe
        if (!_snipeDirectionPersistence.TryGetValue(snipe.SnipeId, out int persistenceCount))
        {
            persistenceCount = 0;
            _snipeDirectionPersistence[snipe.SnipeId] = 0;
        }
        
        // Get valid directions
        var validDirections = GetValidDirections(snipe.X, snipe.Y, width, height, menuBounds, logoBounds, isPlayer: false);
        if (validDirections.Count == 0)
            return (0, 0);
        
        // Check if snipe is at or near screen edge - if so, turn around
        bool nearLeftEdge = snipe.X <= 1;
        bool nearRightEdge = snipe.X >= width - 3; // width - 3 because snipe is 2 wide (arrow + '@')
        bool nearTopEdge = snipe.Y <= 1;
        bool nearBottomEdge = snipe.Y >= height - 2; // height - 2 because snipe is 1 tall
        
        if (nearLeftEdge || nearRightEdge || nearTopEdge || nearBottomEdge)
        {
            // Turn around - reverse direction
            int reverseX = -snipe.DirectionX;
            int reverseY = -snipe.DirectionY;
            
            if (validDirections.Contains((reverseX, reverseY)))
            {
                _snipeDirectionPersistence[snipe.SnipeId] = 0; // Reset persistence when turning around
                return (reverseX, reverseY);
            }
            
            // If reverse direction not valid, pick a direction away from the edge
            var awayFromEdgeDirs = new List<(int dx, int dy)>();
            if (nearLeftEdge) awayFromEdgeDirs.Add((1, 0));
            if (nearRightEdge) awayFromEdgeDirs.Add((-1, 0));
            if (nearTopEdge) awayFromEdgeDirs.Add((0, 1));
            if (nearBottomEdge) awayFromEdgeDirs.Add((0, -1));
            
            foreach (var dir in awayFromEdgeDirs)
            {
                if (validDirections.Contains(dir))
                {
                    _snipeDirectionPersistence[snipe.SnipeId] = 0; // Reset persistence when turning around
                    return dir;
                }
            }
        }
        
        // Direction persistence: continue in same direction if still valid and haven't reached max persistence
        bool shouldChangeDirection = persistenceCount >= SnipeMaxDirectionPersistence ||
                                     (persistenceCount >= SnipeMinDirectionPersistence &&
                                      _demoRandom.Next(100) < 15); // 15% chance to change after min persistence
        
        if (!shouldChangeDirection && snipe.DirectionX != 0 || snipe.DirectionY != 0)
        {
            // Try to continue in current direction
            var currentDir = (snipe.DirectionX, snipe.DirectionY);
            if (validDirections.Contains(currentDir))
            {
                _snipeDirectionPersistence[snipe.SnipeId] = persistenceCount + 1;
                return currentDir;
            }
        }
        
        // Random movement - reset persistence counter
        var validDirs = GetValidDirections(snipe.X, snipe.Y, width, height, menuBounds, logoBounds, isPlayer: false);
        if (validDirs.Count > 0)
        {
            var newDir = validDirs[_demoRandom.Next(validDirs.Count)];
            _snipeDirectionPersistence[snipe.SnipeId] = 0;
            return newDir;
        }
        
        return (0, 0);
    }
    
    private void HandlePlayerShooting(int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds)
    {
        var playerSnapshots = _demoPlayers.ToArray();
        var snipeSnapshots = _demoSnipes.ToArray();
        
        foreach (var player in _demoPlayers)
        {
            if (!player.IsAlive) continue;
            
            // Check if player is in burst fire mode
            bool inBurstMode = false;
            int burstCountRemaining = 0;
            if (_playerBurstFire.TryGetValue(player.PlayerId, out var burstState))
            {
                // Check if enough time has passed for next bullet in burst
                if ((_cachedFrameTime - burstState.lastBurstTime).TotalMilliseconds >= BurstFireIntervalMs)
                {
                    inBurstMode = true;
                    burstCountRemaining = burstState.count - 1;
                    if (burstCountRemaining <= 0)
                    {
                        // Burst complete, remove from dictionary
                        _playerBurstFire.Remove(player.PlayerId);
                        inBurstMode = false;
                    }
                }
            }
            
            // Check if it's time to shoot (normal shot or burst shot)
            bool canShoot = false;
            if (inBurstMode)
            {
                // In burst mode - can shoot if interval has passed
                canShoot = true;
            }
            else
            {
                // Normal shot - check regular interval
                canShoot = (_cachedFrameTime - player.LastShootTime).TotalMilliseconds >= DemoPlayer.ShootIntervalMs;
            }
            
            if (!canShoot)
                continue;
            
            // Count bullets for this player
            int bulletCount = _demoBullets.Count(b => b.PlayerId == player.PlayerId);
            if (bulletCount >= MaxDemoBulletsPerPlayer)
                continue;
            
            // Find nearest snipe
            Snipe? nearestSnipe = null;
            double minDistSquared = double.MaxValue;
            foreach (var snipe in snipeSnapshots)
            {
                if (!snipe.IsAlive) continue;
                double distSq = CalculateSquaredDistance(player.X, player.Y, snipe.X, snipe.Y);
                if (distSq < minDistSquared)
                {
                    minDistSquared = distSq;
                    nearestSnipe = snipe;
                }
            }
            
            if (nearestSnipe != null)
            {
                // Calculate direction to snipe
                double dx = nearestSnipe.X - player.X;
                double dy = nearestSnipe.Y - player.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                
                if (dist > 0)
                {
                    // Normalize and set velocity (reduced speed for demo)
                    double velX = (dx / dist) * DemoSnipeBulletSpeed;
                    double velY = (dy / dist) * DemoSnipeBulletSpeed;
                    
                    _demoBullets.Add(new Bullet(player.X, player.Y, velX, velY, null, player.PlayerId));
                    
                    if (inBurstMode)
                    {
                        // Update burst state
                        _playerBurstFire[player.PlayerId] = (burstCountRemaining, _cachedFrameTime);
                    }
                    else
                    {
                        // Normal shot - check if we should start a burst
                        if (_demoRandom.Next(100) < BurstFireChancePercent)
                        {
                            // Start burst fire
                            _playerBurstFire[player.PlayerId] = (BurstFireCount - 1, _cachedFrameTime); // -1 because we just fired one
                        }
                        
                        // Update last shoot time for normal shots
                        player.LastShootTime = _cachedFrameTime;
                    }
                }
            }
        }
    }
    
    private void HandleCollisions(int width, int height)
    {
        // Bullet-snipe collisions
        for (int i = _demoBullets.Count - 1; i >= 0; i--)
        {
            var bullet = _demoBullets[i];
            
            for (int j = 0; j < _demoSnipes.Count; j++)
            {
                var snipe = _demoSnipes[j];
                if (!snipe.IsAlive) continue;
                
                if (IsWithinRadiusSquared(bullet.X, bullet.Y, snipe.X, snipe.Y, BulletSnipeCollisionRadiusSquared))
                {
                    // Bullet hits snipe
                    snipe.IsAlive = false;
                    _demoBullets.RemoveAt(i);
                    
                    // Respawn snipe
                    var menuBounds = GetMenuBounds(width, height);
                    var logoBounds = GetLogoBounds(width, height);
                    RespawnSnipe(snipe, width, height, menuBounds, logoBounds);
                    break;
                }
            }
        }
        
        // Bullet-player collisions
        for (int i = _demoBullets.Count - 1; i >= 0; i--)
        {
            var bullet = _demoBullets[i];
            
            foreach (var player in _demoPlayers)
            {
                if (!player.IsAlive || bullet.PlayerId == player.PlayerId) continue;
                
                if (IsWithinRadiusSquared(bullet.X, bullet.Y, player.X, player.Y, CollisionRadiusSquared))
                {
                    // Bullet hits player
                    player.IsAlive = false;
                    _demoBullets.RemoveAt(i);
                    
                    // Respawn player
                    var menuBounds = GetMenuBounds(width, height);
                    var logoBounds = GetLogoBounds(width, height);
                    RespawnPlayer(player, width, height, menuBounds, logoBounds);
                    break;
                }
            }
        }
    }
    
    private void SpawnDemoSnipe(int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds)
    {
        var playerSpan = CollectionsMarshal.AsSpan(_demoPlayers);
        var snipeSpan = CollectionsMarshal.AsSpan(_demoSnipes);
        
        // Spawn from menu edges
        var edges = new[] { "top", "bottom", "left", "right" };
        string edge = edges[_demoRandom.Next(edges.Length)];
        
        double x, y;
        switch (edge)
        {
            case "top":
                x = menuBounds.x + _demoRandom.Next(menuBounds.w);
                y = menuBounds.y - 2;
                break;
            case "bottom":
                x = menuBounds.x + _demoRandom.Next(menuBounds.w);
                y = menuBounds.y + menuBounds.h + 2;
                break;
            case "left":
                x = menuBounds.x - 2;
                y = menuBounds.y + _demoRandom.Next(menuBounds.h);
                break;
            default: // right
                x = menuBounds.x + menuBounds.w + 2;
                y = menuBounds.y + _demoRandom.Next(menuBounds.h);
                break;
        }
        
        // Validate position
        if (IsPositionValid(x, y, width, height, menuBounds, logoBounds, playerSpan, snipeSpan))
        {
            // Randomly choose TypeA or TypeB for variety
            SnipeType snipeType = _demoRandom.Next(2) == 0 ? SnipeType.TypeA : SnipeType.TypeB;
            _demoSnipes.Add(new Snipe((int)x, (int)y, snipeType));
        }
    }
    
    private void RespawnPlayer(DemoPlayer player, int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds)
    {
        var playerSpan = CollectionsMarshal.AsSpan(_demoPlayers);
        var snipeSpan = CollectionsMarshal.AsSpan(_demoSnipes);
        
        var (x, y) = GetRandomValidPosition(width, height, menuBounds, logoBounds, playerSpan, snipeSpan);
        
        player.X = x;
        player.Y = y;
        player.PreviousX = x;
        player.PreviousY = y;
        player.IsAlive = true;
        player.LastMoveTime = _cachedFrameTime;
    }
    
    private void RespawnSnipe(Snipe snipe, int width, int height,
        in (int x, int y, int w, int h) menuBounds, in (int x, int y, int w, int h) logoBounds)
    {
        var playerSpan = CollectionsMarshal.AsSpan(_demoPlayers);
        var snipeSpan = CollectionsMarshal.AsSpan(_demoSnipes);
        
        var (x, y) = GetRandomValidPosition(width, height, menuBounds, logoBounds, playerSpan, snipeSpan);
        
        snipe.X = (int)x;
        snipe.Y = (int)y;
        snipe.PreviousX = (int)x;
        snipe.PreviousY = (int)y;
        snipe.IsAlive = true;
        snipe.LastMoveTime = _cachedFrameTime;
        // Reset direction persistence on respawn
        _snipeDirectionPersistence[snipe.SnipeId] = 0;
    }
    
    private void DrawDemoPlayers(int width, int height)
    {
        if (!IsInitialized) return;
        
        foreach (var player in _demoPlayers)
        {
            if (!player.IsAlive) continue;
            
            // Clear previous position
            if (player.PreviousX >= 0 && player.PreviousX < width &&
                player.PreviousY >= 0 && player.PreviousY < height)
            {
                SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
                for (int row = 0; row < 3; row++)
                {
                    int clearY = (int)player.PreviousY + row;
                    if (clearY >= 0 && clearY < height)
                    {
                        for (int col = 0; col < 2; col++)
                        {
                            int clearX = (int)player.PreviousX + col;
                            if (clearX >= 0 && clearX < width)
                            {
                                Move(clearX, clearY);
                                this.AddChar(' ');
                            }
                        }
                    }
                }
            }
            
            // Draw player
            int playerX = (int)player.X;
            int playerY = (int)player.Y;
            
            if (playerX >= 0 && playerX < width && playerY >= 0 && playerY < height)
            {
                DateTime now = DateTime.Now;
                var eyes = now.Millisecond < 500 ? "ÔÔ" : "OO";
                var mouth = now.Millisecond < 500 ? "◄►" : "◂▸";
                
                SetAttribute(new DrawingAttribute(player.Color, Color.Blue));
                
                // Draw eyes
                if (playerX >= 0 && playerX + 1 < width && playerY >= 0 && playerY < height)
                {
                    Move(playerX, playerY);
                    this.AddString(eyes);
                }
                
                // Draw mouth
                if (playerX >= 0 && playerX + 1 < width && playerY + 1 >= 0 && playerY + 1 < height)
                {
                    Move(playerX, playerY + 1);
                    this.AddString(mouth);
                }
                
                // Draw initials
                if (playerX >= 0 && playerX + 1 < width && playerY + 2 >= 0 && playerY + 2 < height)
                {
                    Move(playerX, playerY + 2);
                    this.AddString(player.Initials);
                }
            }
        }
    }
    
    private void DrawDemoSnipes(int width, int height)
    {
        if (!IsInitialized) return;
        
        foreach (var snipe in _demoSnipes)
        {
            if (!snipe.IsAlive) continue;
            
            // Clear previous position (snipe is 2 wide: arrow + '@' or '@' + arrow)
            if (snipe.PreviousX >= 0 && snipe.PreviousX < width &&
                snipe.PreviousY >= 0 && snipe.PreviousY < height)
            {
                SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
                // Clear both positions (arrow and '@')
                Move(snipe.PreviousX, snipe.PreviousY);
                this.AddChar(' ');
                
                // Clear arrow position (to the side, not below)
                int arrowX = snipe.PreviousDirectionX < 0 ? snipe.PreviousX : snipe.PreviousX + 1;
                int charX = snipe.PreviousDirectionX < 0 ? snipe.PreviousX + 1 : snipe.PreviousX;
                
                if (arrowX >= 0 && arrowX < width && snipe.PreviousY >= 0 && snipe.PreviousY < height)
                {
                    Move(arrowX, snipe.PreviousY);
                    this.AddChar(' ');
                }
                if (charX >= 0 && charX < width && snipe.PreviousY >= 0 && snipe.PreviousY < height && charX != arrowX)
                {
                    Move(charX, snipe.PreviousY);
                    this.AddChar(' ');
                }
            }
            
            // Draw snipe (matching game format: arrow to the side, not underneath)
            if (snipe.X >= 0 && snipe.X < width && snipe.Y >= 0 && snipe.Y < height)
            {
                // Set color based on snipe type: TypeA = magenta, TypeB = green (matching game)
                var snipeColor = snipe.Type == SnipeType.TypeA ? Color.Magenta : Color.Green;
                SetAttribute(new DrawingAttribute(snipeColor, Color.Blue));
                
                // Draw order depends on direction (matching game):
                // Moving left: arrow first, then '@'
                // Moving right or other: '@' first, then arrow
                if (snipe.DirectionX < 0)
                {
                    // Moving left - draw arrow first, then character
                    if (snipe.X >= 0 && snipe.X < width && snipe.Y >= 0 && snipe.Y < height)
                    {
                        Move(snipe.X, snipe.Y);
                        this.AddChar(snipe.GetDirectionArrow());
                    }
                    
                    if (snipe.X + 1 >= 0 && snipe.X + 1 < width && snipe.Y >= 0 && snipe.Y < height)
                    {
                        Move(snipe.X + 1, snipe.Y);
                        this.AddChar(snipe.GetDisplayChar());
                    }
                }
                else
                {
                    // Moving right or other directions - draw character first, then arrow
                    if (snipe.X >= 0 && snipe.X < width && snipe.Y >= 0 && snipe.Y < height)
                    {
                        Move(snipe.X, snipe.Y);
                        this.AddChar(snipe.GetDisplayChar());
                    }
                    
                    if (snipe.X + 1 >= 0 && snipe.X + 1 < width && snipe.Y >= 0 && snipe.Y < height)
                    {
                        Move(snipe.X + 1, snipe.Y);
                        this.AddChar(snipe.GetDirectionArrow());
                    }
                }
            }
        }
    }
    
    private void DrawDemoBullets(int width, int height)
    {
        if (!IsInitialized) return;
        
        foreach (var bullet in _demoBullets)
        {
            // Clear previous position
            int prevX = (int)bullet.PreviousX;
            int prevY = (int)bullet.PreviousY;
            if (prevX >= 0 && prevX < width && prevY >= 0 && prevY < height)
            {
                SetAttribute(new DrawingAttribute(Color.White, Color.Blue));
                Move(prevX, prevY);
                this.AddChar(' ');
            }
            
            // Draw bullet
            int bulletX = (int)bullet.X;
            int bulletY = (int)bullet.Y;
            if (bulletX >= 0 && bulletX < width && bulletY >= 0 && bulletY < height)
            {
                SetAttribute(new DrawingAttribute(Color.Red, Color.Blue));
                Move(bulletX, bulletY);
                this.AddChar('*');
            }
        }
    }
}

// Demo player class using C# 14 primary constructor
internal class DemoPlayer(string initials, Terminal.Gui.Drawing.Color color, string playerId)
{
    public double X { get; set; }
    public double Y { get; set; }
    public double PreviousX { get; set; }
    public double PreviousY { get; set; }
    public int DirectionX { get; set; }
    public int DirectionY { get; set; }
    public DateTime LastMoveTime { get; set; } = DateTime.Now;
    public DateTime LastShootTime { get; set; } = DateTime.Now;
    public bool IsAlive { get; set; } = true;
    public string Initials { get; } = initials;
    public Terminal.Gui.Drawing.Color Color { get; } = color;
    public string PlayerId { get; } = playerId;
    public int DirectionPersistenceCount { get; set; } = 0; // How many moves in current direction
    public const int MoveIntervalMs = 100; // Move more frequently
    public const int ShootIntervalMs = 1200; // Reduced shooting frequency for demo
    public const int MinDirectionPersistence = 3; // Minimum moves before changing direction
    public const int MaxDirectionPersistence = 8; // Maximum moves before changing direction
}



