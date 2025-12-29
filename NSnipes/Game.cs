using Terminal.Gui;

namespace NSnipes;

public class Game : Window
{
    private readonly Map _map;
    private readonly Player _player;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private bool _mapDrawn = false;

    public Game()
    {
        _map = new Map();
        var (x, y) = FindRandomValidPosition();
        _player = new Player(x, y);
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

        // Timer is for animating the player (eyes / mouth), and initial map draw
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

        // Ensure we have valid dimensions
        if (currentWidth < 3 || currentHeight < 3)
            return;

        int frameWidth = currentWidth;
        int frameHeight = currentHeight;

        _lastFrameWidth = frameWidth;
        _lastFrameHeight = frameHeight;

        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        Application.Driver.SetAttribute(ColorScheme!.Disabled);

        // draw the maze - start at (0, 0) since there's no border
        for (int r = 0; r < frameHeight; r++)
        {
            Application.Driver.Move(0, r);
            Application.Driver.AddStr(map[r]);
        }

        DrawPlayer();
        _mapDrawn = true; // Mark that map has been drawn
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

    private void DrawPlayer()
    {
        int currentWidth = Application.Driver!.Cols;
        int currentHeight = Application.Driver!.Rows;

        int frameWidth = _lastFrameWidth != 0 ? _lastFrameWidth : currentWidth;
        int frameHeight = _lastFrameHeight != 0 ? _lastFrameHeight : currentHeight;

        // draw the player
        // _player.X, _player.Y represents top-left corner of player
        // Map.GetMap centers viewport on (_player.X, _player.Y)
        // So top-left in viewport is at (frameWidth/2, frameHeight/2)
        int topLeftCol = frameWidth / 2;
        int topLeftRow = frameHeight / 2;

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
