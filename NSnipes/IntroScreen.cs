using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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
    private readonly string[] _menuItems = { "Start a New Game", "Join an Existing Game", "Initials", "Configure Server", "Exit" };
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
    private int _timeRemaining = 60;
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
    
    // Dependencies
    private GameConfig _config;
    private GameState _gameState;
    private Func<int, int, char>? _getMapCharAtPosition; // Callback to get map character during clearing effect
    
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
        
        // Check if we're in the intro animation phase (banner scrolling or player exiting)
        double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
        int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
        int targetX = (width - bannerWidth) / 2; // Center position
        int startX = -bannerWidth; // Start completely off screen to the left
        
        // Animation phase: banner scrolling (0-2s) and player exiting (2-4s)
        if (elapsedSeconds < 4.0)
        {
            // Animate banner scrolling in from left (first 2 seconds)
            int bannerX;
            if (elapsedSeconds >= 2.0)
            {
                // Banner animation complete, center the banner
                bannerX = targetX;
                _bannerScrollPosition = targetX;
                if (_bannerScrolling)
                {
                    _bannerScrolling = false;
                    // Don't show menu yet - wait for player to exit
                }
            }
            else
            {
                // Calculate scroll position (ease-in-out)
                double progress = elapsedSeconds / 2.0;
                // Simple ease-in-out: smooth start and end
                progress = progress * progress * (3.0 - 2.0 * progress);
                // Interpolate from startX (off-screen left) to targetX (centered)
                bannerX = (int)(startX + (targetX - startX) * progress);
                _bannerScrollPosition = bannerX;
            }
            
            // Calculate player position - player leads banner (to the right), then exits after banner reaches center
            int bannerStartY = height / 4; // Banner Y position
            _introPlayerY = bannerStartY + 1 + 3; // Position player at middle of banner (row 3 of 7)
            
            // Player animation: leads banner (stays to the right), then exits over 2 seconds after banner reaches center
            bool shouldDrawPlayer = true;
            if (elapsedSeconds < 2.0)
            {
                // During banner animation: player stays ahead (to the right) of banner
                // Player position = banner right edge + some spacing
                int bannerRightEdge = bannerX + bannerWidth;
                _introPlayerX = bannerRightEdge + 10; // 10 characters ahead of banner
            }
            else if (elapsedSeconds < 4.0)
            {
                // After banner reaches center: player exits right over 2 seconds
                double exitProgress = (elapsedSeconds - 2.0) / 2.0; // 0 to 1 over 2 seconds
                int exitStartX = targetX + bannerWidth + 10; // Start where player was when banner reached center
                _introPlayerX = exitStartX + (int)((width + 5 - exitStartX) * exitProgress);
            }
            else
            {
                // Player animation complete, player should be off-screen right
                _introPlayerX = width + 5;
                shouldDrawPlayer = false; // Don't draw player when off-screen
                // Now show the menu
                _showMenu = true;
            }
            
            // Draw banner first (so player appears on top)
            DrawBanner(_bannerScrollPosition, height);
            
            // Draw player only if it should be visible
            if (shouldDrawPlayer)
            {
                DrawIntroPlayer(width, height);
                // Update previous position for next frame
                _introPlayerPrevX = _introPlayerX;
            }
        }
        else
        {
            // Animation complete (after 4 seconds) - show menu
            // Banner is centered, draw it and show menu
            int bannerX = (width - bannerWidth) / 2;
            DrawBanner(bannerX, height);
            
            // Ensure menu is shown after animation completes
            _showMenu = true;
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
                    _selectedMenuIndex = 3;
                    HandleMenuSelection();
                    break;
            }
        }
    }
    
    private void HandleMenuSelection()
    {
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
            _timeRemaining = 60;
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
                    // First letter - draw in yellow
                    SetAttribute(new DrawingAttribute(Color.Yellow, i == _selectedMenuIndex ? Color.White : Color.Blue));
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
            CheckServerStatus();
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
        // Run async check in background
        Task.Run(async () =>
        {
            try
            {
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
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        var request = new NSnipes.GrpcServer.JoinRequest
                        {
                            GameId = "TEST_CONNECTION",
                            PlayerId = "TEST",
                            Initials = "TEST"
                        };
                        await testClient.JoinGameAsync(request, cancellationToken: cts.Token);
                        // If we get here, server is responding (even if game doesn't exist)
                        _serverStatus = true;
                    }
                    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound || 
                                                             ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
                    {
                        // Server responded (game not found is expected), so server is online
                        _serverStatus = true;
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - server might be offline or slow
                        _serverStatus = false;
                    }
                    catch
                    {
                        // Other errors - assume server is offline
                        _serverStatus = false;
                    }
                }
            }
            catch
            {
                _serverStatus = false;
            }
        });
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
}



