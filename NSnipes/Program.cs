using NSnipes;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.Input;

using IApplication app = Application.Create();
app.Init();

// Hide cursor using ANSI escape sequence
try
{
    System.Console.Write("\x1b[?25l"); // Hide cursor
}
catch
{
    // Fall back to Console API if ANSI doesn't work
    try
    {
        System.Console.CursorVisible = false;
    }
    catch
    {
        // Ignore if not available
    }
}

// Disable default Escape key quit behavior - we handle Escape ourselves in the Game class
// Set QuitKey to F12 (unused key) instead of Escape
// In Terminal.Gui v2, Key is created from character
Application.QuitKey = new Key((char)0x7B); // F12 key code

var game = new Game(app);

try
{
    app.Run(game);
}
finally
{
    // Restore cursor visibility when application exits
    try
    {
        System.Console.Write("\x1b[?25h"); // Show cursor ANSI sequence
        System.Console.CursorVisible = true;
    }
    catch
    {
        // Ignore if not available
    }
}