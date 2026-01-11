using Terminal.Gui;
using System.Linq;

namespace NSnipes;

public class PlayerScoreInfo
{
    public string Initials { get; set; } = "";
    public int Score { get; set; } = 0;
}

public class GameOverScreen
{
    // State
    private bool _isActive = false;
    private bool _bannerScrolling = false;
    private bool _waitingForEnter = false;
    
    private DateTime _bannerStartTime;
    private int _bannerScrollPosition = 0;
    private List<PlayerScoreInfo> _playerScores = new List<PlayerScoreInfo>();
    
    // Events
    public event Action? OnReturnToIntro; // Called when ENTER is pressed to return to intro screen
    
    // GAME OVER banner definition (7 rows tall, each letter is 7 characters wide)
    private static readonly string[] BannerG = new[]
    {
        " █████ ",
        "█      ",
        "█      ",
        "█   ███",
        "█     █",
        "█     █",
        " █████ "
    };
    
    private static readonly string[] BannerA = new[]
    {
        " █████ ",
        "█     █",
        "█     █",
        "███████",
        "█     █",
        "█     █",
        "█     █"
    };
    
    private static readonly string[] BannerM = new[]
    {
        "█     █",
        "██   ██",
        "█ █ █ █",
        "█  █  █",
        "█     █",
        "█     █",
        "█     █"
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
    
    private static readonly string[] BannerO = new[]
    {
        " █████ ",
        "█     █",
        "█     █",
        "█     █",
        "█     █",
        "█     █",
        " █████ "
    };
    
    private static readonly string[] BannerV = new[]
    {
        "█     █",
        "█     █",
        "█     █",
        "█     █",
        " █   █ ",
        "  █ █  ",
        "   █   "
    };
    
    private static readonly string[] BannerR = new[]
    {
        "██████ ",
        "█     █",
        "█     █",
        "██████ ",
        "█   █  ",
        "█    █ ",
        "█     █"
    };
    
    public bool IsActive => _isActive;
    public bool IsWaitingForEnter => _waitingForEnter;
    
    public void Show(List<PlayerScoreInfo> playerScores)
    {
        _isActive = true;
        _bannerScrolling = true;
        _waitingForEnter = false;
        _bannerStartTime = DateTime.Now;
        _bannerScrollPosition = 0;
        _playerScores = playerScores.OrderByDescending(p => p.Score).ToList();
        
        // Clear the screen with blue background
        if (Application.Driver != null)
        {
            int width = Application.Driver.Cols;
            int height = Application.Driver.Rows;
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
            for (int y = 0; y < height; y++)
            {
                Application.Driver.Move(0, y);
                Application.Driver.AddStr(new string(' ', width));
            }
        }
    }
    
    public void Hide()
    {
        _isActive = false;
        _bannerScrolling = false;
        _waitingForEnter = false;
    }
    
    public void Draw()
    {
        if (Application.Driver == null || !_isActive)
            return;
            
        int width = Application.Driver.Cols;
        int height = Application.Driver.Rows;
        
        // Always clear screen with blue background first
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        for (int y = 0; y < height; y++)
        {
            Application.Driver.Move(0, y);
            Application.Driver.AddStr(new string(' ', width));
        }
        
        if (_bannerScrolling)
        {
            // Animate GAME OVER banner scrolling in from left
            // GAME (4 letters) + 3 gaps (2 cols each) + word gap (6 cols) + OVER (4 letters) + 3 gaps (2 cols each)
            double elapsedSeconds = (DateTime.Now - _bannerStartTime).TotalSeconds;
            int bannerWidth = (7 * 4 + 3 * 2) + 6 + (7 * 4 + 3 * 2); // GAME + gap + OVER
            int targetX = (width - bannerWidth) / 2; // Center position
            int startX = -bannerWidth; // Start completely off screen to the left
            
            if (elapsedSeconds >= 2.0)
            {
                // Animation complete, center the banner
                _bannerScrollPosition = targetX;
                _bannerScrolling = false;
                _waitingForEnter = true;
            }
            else
            {
                // Calculate scroll position (ease-in-out)
                double progress = elapsedSeconds / 2.0;
                progress = progress * progress * (3.0 - 2.0 * progress);
                _bannerScrollPosition = (int)(startX + (targetX - startX) * progress);
            }
            
            DrawBanner(_bannerScrollPosition, height);
        }
        else if (_waitingForEnter)
        {
            DrawScreen(width, height);
        }
    }
    
    public bool HandleKey(dynamic e)
    {
        if (!_isActive || !_waitingForEnter)
            return false;
        
        // Only ENTER key returns to intro screen
        if (e.KeyCode == KeyCode.Enter)
        {
            Hide();
            OnReturnToIntro?.Invoke();
            return true;
        }
        
        return true; // Consume other keys but don't do anything
    }
    
    private void DrawBanner(int startX, int screenHeight)
    {
        if (Application.Driver == null)
            return;
            
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        
        // Banner is 7 rows tall, positioned in upper third of screen
        int bannerStartY = screenHeight / 4;
        
        // Draw GAME (4 letters) with 2-column gaps between letters
        string[][] gameLetters = { BannerG, BannerA, BannerM, BannerE };
        int currentX = startX;
        
        for (int letterIndex = 0; letterIndex < gameLetters.Length; letterIndex++)
        {
            string[] letter = gameLetters[letterIndex];
            int letterX = currentX;
            
            for (int row = 0; row < 7; row++)
            {
                int y = bannerStartY + 1 + row;
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
            
            // Move to next letter position (7 for letter + 2 for gap)
            currentX += 9;
        }
        
        // Add gap between words (6 columns)
        currentX += 6;
        
        // Draw OVER (4 letters) with 2-column gaps between letters
        string[][] overLetters = { BannerO, BannerV, BannerE, BannerR };
        
        for (int letterIndex = 0; letterIndex < overLetters.Length; letterIndex++)
        {
            string[] letter = overLetters[letterIndex];
            int letterX = currentX;
            
            for (int row = 0; row < 7; row++)
            {
                int y = bannerStartY + 1 + row;
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
            
            // Move to next letter position (7 for letter + 2 for gap)
            currentX += 9;
        }
    }
    
    private void DrawScreen(int width, int height)
    {
        // Draw game over screen with banner and player scores
        // Note: Screen is already cleared with blue background in Draw() method
        
        // Draw GAME OVER banner (centered)
        int bannerWidth = (7 * 4 + 3 * 2) + 6 + (7 * 4 + 3 * 2); // GAME + gap + OVER
        int bannerX = (width - bannerWidth) / 2;
        DrawBanner(bannerX, height);
        
        // Draw player scores below banner (with gap)
        int bannerStartY = height / 4;
        int bannerHeight = 7 + 2; // 7 rows + 1 above + 1 below
        int scoresStartY = bannerStartY + bannerHeight + 5; // 5 lines gap
        
        // Draw "-< SCORES >-" header above scores
        string scoresHeader = "-< SCORES >-";
        int headerX = (width - scoresHeader.Length) / 2;
        int headerY = scoresStartY;
        if (Application.Driver != null)
        {
            Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
            Application.Driver.Move(headerX, headerY);
            Application.Driver.AddStr(scoresHeader);
        }
        
        // Draw player scores below header
        int scoresListStartY = scoresStartY + 2; // 2 lines below header
        
        if (_playerScores != null && _playerScores.Count > 0)
        {
            for (int i = 0; i < _playerScores.Count; i++)
            {
                var player = _playerScores[i];
                int y = scoresListStartY + i;
                
                if (y < height - 2) // Leave room for "Press ENTER" message
                {
                    string scoreText = $"{player.Initials}: {player.Score}";
                    int x = (width - scoreText.Length) / 2;
                    
                    // Top player in cyan, others in yellow
                    if (i == 0)
                    {
                        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Cyan, Color.Blue));
                    }
                    else
                    {
                        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Yellow, Color.Blue));
                    }
                    
                    Application.Driver.Move(x, y);
                    Application.Driver.AddStr(scoreText);
                }
            }
        }
        
        // Draw "Press ENTER" message at bottom
        string enterMessage = "Press ENTER to continue";
        int enterX = (width - enterMessage.Length) / 2;
        int enterY = height - 2;
        Application.Driver.SetAttribute(new Terminal.Gui.Attribute(Color.White, Color.Blue));
        Application.Driver.Move(enterX, enterY);
        Application.Driver.AddStr(enterMessage);
    }
}
