using Terminal.Gui;

namespace NSnipes;

public class Game : Window
{
    private readonly Map _map;
    private readonly Player _player;

    public Game()
    {
        _map = new Map();
        _player = new Player(75, 50);
        Title = "NSnipes";

        ColorScheme = new ColorScheme()
        {
            Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Disabled = new Terminal.Gui.Attribute(Color.Blue, Color.Black),

        };

        Application.AddTimeout(TimeSpan.FromMilliseconds(20), DrawFrame);
        Application.KeyDown += HandleKeyDown;
        Application.SizeChanging += (s, e) =>
        {
            DrawFrame();
        };

        DrawFrame();
    }

    private void HandleKeyDown(object? sender, Key e)
    {
        switch (e.KeyCode)
        {
            case KeyCode.CursorRight:
                _player.X++;
                if (_player.X > _map.MapWidth)
                    _player.X = 0;
                break;
            case KeyCode.CursorLeft:
                _player.X--;
                if (_player.X < 0)
                    _player.X = _map.MapWidth;
                break;
            case KeyCode.CursorUp:
                _player.Y--;
                if (_player.Y < 0)
                    _player.Y = _map.MapHeight;
                break;
            case KeyCode.CursorDown:
                _player.Y++;
                if (_player.Y > _map.MapHeight)
                    _player.Y = 0;
                break;
        }
    }

    private bool DrawFrame()
    {
        int currentWidth = Application.Driver!.Cols;
        int currentHeight = Application.Driver!.Rows;
        int frameWidth = currentWidth - 2;
        int frameHeight = currentHeight - 2;

        var map = _map.GetMap(frameWidth, frameHeight, _player.X, _player.Y);

        Application.Driver!.SetAttribute(ColorScheme!.Disabled);

        // draw the maze
        for (int r = 0; r < frameHeight; r++)
        {
            Application.Driver!.Move(1, r+1);
            Application.Driver!.AddStr(map[r]);  
        }

        // draw the player
        var eyes = DateTime.Now.Millisecond < 500 ? "ÔÔ" : "OO";
        var mouth = DateTime.Now.Millisecond < 500 ? "◄►" : "◂▸";

        Application.Driver!.SetAttribute(ColorScheme!.Focus);
        Application.Driver!.Move(frameWidth/2-1, currentHeight/2-1);
        Application.Driver!.AddStr(eyes);
        Application.Driver!.Move(frameWidth / 2 - 1, currentHeight/2);
        Application.Driver!.AddStr(mouth);
        Application.Driver!.SetAttribute(ColorScheme!.Normal);
        Application.Driver!.Move(frameWidth / 2 - 1, currentHeight/2+1);
        Application.Driver!.AddStr(_player.Initials);

        return true;
    }

}
