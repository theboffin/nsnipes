using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;
using DrawingAttribute = Terminal.Gui.Drawing.Attribute;

namespace NSnipes;

/// <summary>
/// Helper methods for Terminal.Gui v2 View classes to simplify common drawing operations
/// </summary>
public static class ViewHelpers
{
    /// <summary>
    /// Adds a string to the view at the current cursor position by converting each character to a Rune
    /// Uses ReadOnlySpan to avoid allocations when passing string slices or spans
    /// </summary>
    public static void AddString(this View view, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return;
            
        foreach (char c in text)
        {
            view.AddRune(new System.Text.Rune(c));
        }
    }
    
    /// <summary>
    /// Adds a character as a Rune to the view at the current cursor position
    /// </summary>
    public static void AddChar(this View view, char c)
    {
        view.AddRune(new System.Text.Rune(c));
    }
}
