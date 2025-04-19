using Terminal.Gui;

namespace NSnipes;

public class Game : Window
{
    private List<Label> _rows = [];
    private readonly Map _map;
    public Game()
    {
         _map = new Map();
        Title = "NSnipes";

        ColorScheme = new ColorScheme()
        {
           Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Disabled = new Terminal.Gui.Attribute(Color.Blue, Color.Black),

        };

        Application.SizeChanging += (s, e) =>
        {
             DrawFrame();
        };
        Application.AddTimeout(TimeSpan.FromMilliseconds(50), DrawFrame);
        DrawFrame();

    }


    private bool DrawFrame()
    {
        int currentWidth = Application.Driver!.Cols;
        int currentHeight = Application.Driver!.Rows;
        int frameWidth = currentWidth - 2;

        Application.Driver!.SetAttribute(ColorScheme!.Disabled);
        // draw the maze
        for (int r = 1; r < currentHeight - 1; r++)
        {
            Application.Driver!.Move(1, r);
            Application.Driver!.AddStr(_map.FullMap[r].Substring(0, frameWidth));
        }

        // draw the player
        var eyes = DateTime.Now.Millisecond < 500 ? "ÔÔ" : "OO";
        var mouth = "◄►";
        var initials = "BD";

        Application.Driver!.SetAttribute(ColorScheme!.Focus);
        Application.Driver!.Move(frameWidth/2-1, currentHeight/2-1);
        Application.Driver!.AddStr(eyes);
        Application.Driver!.Move(frameWidth / 2 - 1, currentHeight/2);
        Application.Driver!.AddStr(mouth);
        Application.Driver!.Move(frameWidth / 2 - 1, currentHeight/2+1);
        Application.Driver!.AddStr(initials);


        return true;
    }

}
