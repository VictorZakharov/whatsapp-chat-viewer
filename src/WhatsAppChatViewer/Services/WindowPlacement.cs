using Avalonia;
using Avalonia.Controls;

namespace WhatsAppChatViewer.Services;

public static class WindowPlacement
{
    private const int ScreenMarginPixels = 24;

    public static void CenterWithinOwnerScreen(Window window, WindowBase owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var workingArea = screen.WorkingArea;
        var scaling = Math.Max(0.1, screen.Scaling);
        var maximumWidth = Math.Max(320, (workingArea.Width - (ScreenMarginPixels * 2)) / scaling);
        var maximumHeight = Math.Max(240, (workingArea.Height - (ScreenMarginPixels * 2)) / scaling);
        var width = Math.Min(
            double.IsFinite(window.Width) && window.Width > 0 ? window.Width : maximumWidth,
            maximumWidth);
        var height = Math.Min(
            double.IsFinite(window.Height) && window.Height > 0 ? window.Height : maximumHeight,
            maximumHeight);

        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = width;
        window.Height = height;

        var widthPixels = Math.Min(workingArea.Width, (int)Math.Ceiling(width * scaling));
        var heightPixels = Math.Min(workingArea.Height, (int)Math.Ceiling(height * scaling));
        window.Position = new PixelPoint(
            workingArea.X + ((workingArea.Width - widthPixels) / 2),
            workingArea.Y + ((workingArea.Height - heightPixels) / 2));
    }
}
