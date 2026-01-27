using NSnipes;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.Input;

// Initialize error logger first
ErrorLogger.Initialize();

// Set up global unhandled exception handlers
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    if (e.ExceptionObject is Exception ex)
    {
        ErrorLogger.LogError("Unhandled exception in AppDomain", ex);
    }
    else
    {
        ErrorLogger.LogError($"Unhandled exception: {e.ExceptionObject}");
    }
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    ErrorLogger.LogError("Unobserved task exception", e.Exception);
    e.SetObserved(); // Mark as handled to prevent app crash
};

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

// Set global application exit key to CTRL-C
// Use Key.C.WithCtrl to create CTRL-C key
Application.QuitKey = Key.C.WithCtrl;

var game = new Game(app);

try
{
    // In Terminal.Gui v2, Window should implement IRunnable
    // If not, we may need to add it to the application's view hierarchy
    app.Run(game);
}
finally
{
    // Shutdown error logger
    ErrorLogger.Shutdown();
    
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