using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using System.Linq;
using DrawingAttribute = Terminal.Gui.Drawing.Attribute;

namespace NSnipes;

public class PlayerScoreInfo
{
    public string Initials { get; set; } = "";
    public int Score { get; set; } = 0;
}

public class GameOverScreen : View
{
    // State
    private bool _isActive = false;
    private bool _bannerScrolling = false;
    private bool _waitingForEnter = false;
    
    private DateTime _bannerStartTime;
    private int _bannerScrollPosition = 0;
    private List<PlayerScoreInfo> _playerScores = new List<PlayerScoreInfo>(5); // Max 5 players
    
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
    
    public GameOverScreen()
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        Visible = false; // Start hidden
    }
    
    public void Show(List<PlayerScoreInfo> playerScores)
    {
        _isActive = true;
        _bannerScrolling = true;
        _waitingForEnter = false;
        _bannerStartTime = DateTime.Now;
        _bannerScrollPosition = 0;
        // Sort in-place to avoid LINQ ToList() allocation
        _playerScores = new List<PlayerScoreInfo>(playerScores.Count);
        _playerScores.AddRange(playerScores);
        _playerScores.Sort((a, b) => b.Score.CompareTo(a.Score)); // Descending order
        Visible = true;
        SetNeedsDraw();
    }
    
    public void Hide()
    {
        _isActive = false;
        _bannerScrolling = false;
        _waitingForEnter = false;
        Visible = false;
        SetNeedsDraw();
    }
    
    protected override bool OnDrawingContent(DrawContext? dc)
    {
        if (dc == null || !IsInitialized || !_isActive)
            return false;
            
        int width = Frame.Width;
        int height = Frame.Height;
        
        // Clear screen with black background first (matches main game)
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        for (int y = 0; y < height; y++)
        {
            Move(0, y);
            for (int x = 0; x < width; x++)
            {
                AddRune(new System.Text.Rune(' '));
            }
        }
        
        if (_bannerScrolling)
        {
            // Animate GAME OVER banner scrolling in from left
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
        
        return true;
    }
    
    public bool HandleKey(Key key)
    {
        if (!_isActive || !_waitingForEnter)
            return false;
        
        // Only ENTER key returns to intro screen
        if (key.ToString().Contains("Enter"))
        {
            Hide();
            OnReturnToIntro?.Invoke();
            return true;
        }
        
        return true; // Consume other keys but don't do anything
    }
    
    private void DrawBanner(int startX, int screenHeight)
    {
        // Banner is 7 rows tall, positioned in upper third of screen
        int bannerStartY = screenHeight / 4;
        
        // First, clear the banner area to black to ensure clean background
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        int bannerWidth = (7 * 4 + 3 * 2) + 6 + (7 * 4 + 3 * 2); // GAME + gap + OVER
        for (int y = bannerStartY; y < bannerStartY + 9 && y < screenHeight; y++)
        {
            for (int x = Math.Max(0, startX - 2); x < Math.Min(Frame.Width, startX + bannerWidth + 2); x++)
            {
                Move(x, y);
                AddRune(new System.Text.Rune(' '));
            }
        }
        
        // Draw GAME OVER banner with white blocks on black background
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        
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
                        if (x >= 0 && x < Frame.Width)
                        {
                            Move(x, y);
                            // Banner characters are white blocks on black background
                            AddRune(new System.Text.Rune(letter[row][col]));
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
                        if (x >= 0 && x < Frame.Width)
                        {
                            Move(x, y);
                            // Banner characters are white blocks on black background
                            AddRune(new System.Text.Rune(letter[row][col]));
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
        
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        Move(headerX, headerY);
        foreach (char c in scoresHeader)
        {
            AddRune(new System.Text.Rune(c));
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
                    
                    // Top player in cyan, others in yellow (on black background)
                    if (i == 0)
                    {
                        SetAttribute(new DrawingAttribute(Color.Cyan, Color.Black));
                    }
                    else
                    {
                        SetAttribute(new DrawingAttribute(Color.Yellow, Color.Black));
                    }
                    
                    Move(x, y);
                    foreach (char c in scoreText)
                    {
                        AddRune(new System.Text.Rune(c));
                    }
                }
            }
        }
        
        // Draw "Press ENTER" message at bottom
        string enterMessage = "Press ENTER to continue";
        int enterX = (width - enterMessage.Length) / 2;
        int enterY = height - 2;
        SetAttribute(new DrawingAttribute(Color.White, Color.Black));
        Move(enterX, enterY);
        foreach (char c in enterMessage)
        {
            AddRune(new System.Text.Rune(c));
        }
    }
}
