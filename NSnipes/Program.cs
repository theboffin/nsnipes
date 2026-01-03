using NSnipes;
using Terminal.Gui;

Application.Init();
Application.UngrabMouse();

// Disable default Escape key quit behavior - we handle Escape ourselves in the Game class
// In Terminal.Gui v2 alpha, set QuitKey to an unused key (F12) so Escape doesn't close the app
try
{
    // Set QuitKey to F12 (unused key) instead of Escape
    Application.QuitKey = new Key(KeyCode.F12);
}
catch (Exception)
{
    // If setting QuitKey fails, our Application.KeyDown handler in Game class 
    // will catch Escape and handle it before default behavior
    // This is a fallback - the handler should still work
}

Application.Run<Game>();
Application.Shutdown();