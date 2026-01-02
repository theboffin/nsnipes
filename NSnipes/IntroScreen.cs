using Terminal.Gui;

namespace NSnipes;

public class IntroScreen
{
    // Events for communication with Game
    public event Action<int>? OnStartGame; // Level
    public event Action? OnExit;
    public event Action<string>? OnInitialsChanged; // New initials
    public event Action? OnReturnToIntro; // When returning to intro screen (e.g., from game over)
    
    // State
    private bool _isActive = true;
    private bool _bannerScrolling = true;
    private bool _showMenu = false;
    private bool _clearingScreen = false;
    private bool _gameOver = false;
    private bool _waitingForGameOverKey = false;
    
    private DateTime _bannerStartTime;
    private int _bannerScrollPosition = 0;
    private int _clearingRectSize = 0;
    private DateTime _clearingStartTime;
    private string _clearingMessage = "";
    
    // Menu state
    private int _selectedMenuIndex = 0;
    private readonly string[] _menuItems = { "Start a New Game", "Join an Existing Game", "Initials", "Exit" };
    private bool _enteringInitials = false;
    private string _initialsInput = "";
    
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
    }
    
    public bool IsActive => _isActive;
    public bool IsClearingScreen => _clearingScreen;
    public bool IsGameOver => _gameOver;
    public bool IsWaitingForGameOverKey => _waitingForGameOverKey;
    
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
        _gameOver = false;
        _waitingForGameOverKey = false;
        _selectedMenuIndex = 0;
        _enteringInitials = false;
        _bannerStartTime = DateTime.Now;
        
        // Clear the screen before showing intro
        if (Application.Driver != null)
        {
            int width = Application.Driver.Cols;
            int height = Application.Driver.Rows;
            int bannerWidth = 7 * 7 + 6 * 2;
            _bannerScrollPosition = -bannerWidth; // Start off-screen
            
            // Clear entire screen with blue background
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
            for (int y = 0; y < height; y++)
            {
                Application.Driver.Move(0, y);
                Application.Driver.AddStr(new string(' ', width));
            }
        }
    }
    
    public void StartClearingEffect(string message)
    {
        _clearingScreen = true;
        _clearingStartTime = DateTime.Now;
        _clearingRectSize = 0;
        _clearingMessage = message;
        _gameOver = false;
        _waitingForGameOverKey = false;
    }
    
    public void ShowGameOver(string message)
    {
        _gameOver = true;
        _clearingScreen = true;
        _clearingStartTime = DateTime.Now;
        _clearingRectSize = 0;
        _clearingMessage = message;
    }
    
    public void Draw()
    {
        if (Application.Driver == null)
            return;
            
        int width = Application.Driver.Cols;
        int height = Application.Driver.Rows;
        
        if (_waitingForGameOverKey)
        {
            DrawGameOverScreen(width, height);
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
            // Banner is centered, draw it and show menu
            int bannerWidth = 7 * 7 + 6 * 2; // 7 letters (7 cols each) + 6 gaps (2 cols each)
            int bannerX = (width - bannerWidth) / 2;
            DrawBanner(bannerX, height);
            
            if (_showMenu)
            {
                DrawMenu(width, height);
            }
        }
    }
    
    public bool HandleKey(dynamic e)
    {
        // Handle game over key press - this must be checked first
        if (_waitingForGameOverKey)
        {
            // Any key press returns to intro screen
            Show();
            OnReturnToIntro?.Invoke(); // Notify Game to reset state
            return true;
        }
        
        // Handle intro screen key press
        if (_isActive && !_clearingScreen)
        {
            HandleIntroScreenKey(e);
            return true;
        }
        
        // If we're in clearing screen but not waiting for game over key, don't handle keys
        // (clearing effect is in progress)
        if (_clearingScreen)
        {
            return false;
        }
        
        return false;
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
                StartClearingEffect($"Level {_gameState.Level}");
                break;
                
            case 1: // Join an Existing Game
                // Do nothing for now
                break;
                
            case 2: // Initials
                _enteringInitials = true;
                _initialsInput = "";
                break;
                
            case 3: // Exit
                OnExit?.Invoke();
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
                    OnInitialsChanged?.Invoke(_initialsInput);
                    _enteringInitials = false;
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
    
    private void DrawGameOverScreen(int width, int height)
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
        // Draw GAME OVER message with spacing
        if (!string.IsNullOrEmpty(_clearingMessage))
        {
            string gameOverMessageWithSpacing = "  " + _clearingMessage + "  ";
            int gameOverMessageX = (width - gameOverMessageWithSpacing.Length) / 2;
            int gameOverMessageY = height / 2;
            
            // Clear area around message
            for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
            {
                int y = gameOverMessageY + rowOffset;
                if (y >= 0 && y < height)
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
    
    private void DrawClearingEffect(int width, int height)
    {
        if (Application.Driver == null)
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
                else if (!inMessageArea && _getMapCharAtPosition != null)
                {
                    // Outside rectangle and not in message area - draw map character
                    char mapChar = _getMapCharAtPosition(x, y - StatusBarHeight);
                    Application.Driver.Move(x, y);
                    Application.Driver.AddRune(mapChar);
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
                DrawGameOverScreen(width, height);
            }
            else
            {
                // Normal game start or respawn
                _isActive = false;
                _clearingScreen = false;
                OnStartGame?.Invoke(_gameState.Level);
            }
        }
    }
}

