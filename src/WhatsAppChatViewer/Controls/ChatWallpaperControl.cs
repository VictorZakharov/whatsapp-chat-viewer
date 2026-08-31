using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WhatsAppChatViewer.Controls;

public sealed class ChatWallpaperControl : Control
{
    public static readonly StyledProperty<double> ScrollOffsetProperty =
        AvaloniaProperty.Register<ChatWallpaperControl, double>(nameof(ScrollOffset));

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#0B141A"));
    private static readonly Pen PatternPen = new(
        new SolidColorBrush(Color.FromArgb(125, 31, 45, 51)),
        1.35,
        lineCap: PenLineCap.Round,
        lineJoin: PenLineJoin.Round);

    public ChatWallpaperControl()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    static ChatWallpaperControl()
    {
        AffectsRender<ChatWallpaperControl>(ScrollOffsetProperty);
    }

    public double ScrollOffset
    {
        get => GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, new Rect(Bounds.Size));

        const double spacing = 118;
        const double motifTop = 34;
        var absoluteTop = Math.Max(0, ScrollOffset);
        var firstRow = (int)Math.Floor((absoluteTop - motifTop) / spacing) - 1;
        var lastRow = (int)Math.Ceiling((absoluteTop + Bounds.Height - motifTop) / spacing) + 1;
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = -1; column <= (int)(Bounds.Width / spacing) + 1; column++)
            {
                var x = (column * spacing) + ((row & 1) == 0 ? 18 : 72);
                var y = (row * spacing) + motifTop - absoluteTop;
                DrawMotif(context, new Point(x, y), PositiveModulo((row * 7) + (column * 3), 6));
            }
        }
    }

    private static void DrawMotif(DrawingContext context, Point origin, int motif)
    {
        switch (motif)
        {
            case 0:
                DrawFace(context, origin);
                break;
            case 1:
                DrawCamera(context, origin);
                break;
            case 2:
                DrawChat(context, origin);
                break;
            case 3:
                DrawStar(context, origin);
                break;
            case 4:
                DrawPaperPlane(context, origin);
                break;
            default:
                DrawHeart(context, origin);
                break;
        }
    }

    private static void DrawFace(DrawingContext context, Point point)
    {
        context.DrawEllipse(null, PatternPen, new Rect(point.X, point.Y, 34, 34));
        context.DrawEllipse(null, PatternPen, new Rect(point.X + 9, point.Y + 10, 2, 2));
        context.DrawEllipse(null, PatternPen, new Rect(point.X + 23, point.Y + 10, 2, 2));
        context.DrawLine(PatternPen, new Point(point.X + 10, point.Y + 23), new Point(point.X + 17, point.Y + 27));
        context.DrawLine(PatternPen, new Point(point.X + 17, point.Y + 27), new Point(point.X + 25, point.Y + 22));
    }

    private static void DrawCamera(DrawingContext context, Point point)
    {
        context.DrawRectangle(null, PatternPen, new Rect(point.X, point.Y + 7, 40, 27), 5, 5);
        context.DrawRectangle(null, PatternPen, new Rect(point.X + 7, point.Y + 2, 12, 7), 2, 2);
        context.DrawEllipse(null, PatternPen, new Rect(point.X + 14, point.Y + 13, 13, 13));
    }

    private static void DrawChat(DrawingContext context, Point point)
    {
        context.DrawRectangle(null, PatternPen, new Rect(point.X, point.Y, 42, 30), 8, 8);
        context.DrawLine(PatternPen, new Point(point.X + 10, point.Y + 30), new Point(point.X + 6, point.Y + 38));
        context.DrawLine(PatternPen, new Point(point.X + 10, point.Y + 30), new Point(point.X + 17, point.Y + 30));
        context.DrawLine(PatternPen, new Point(point.X + 10, point.Y + 11), new Point(point.X + 32, point.Y + 11));
        context.DrawLine(PatternPen, new Point(point.X + 10, point.Y + 18), new Point(point.X + 26, point.Y + 18));
    }

    private static void DrawStar(DrawingContext context, Point point)
    {
        var points = new[]
        {
            new Point(point.X + 18, point.Y), new Point(point.X + 22, point.Y + 12),
            new Point(point.X + 35, point.Y + 12), new Point(point.X + 25, point.Y + 20),
            new Point(point.X + 29, point.Y + 33), new Point(point.X + 18, point.Y + 25),
            new Point(point.X + 7, point.Y + 33), new Point(point.X + 11, point.Y + 20),
            new Point(point.X + 1, point.Y + 12), new Point(point.X + 14, point.Y + 12)
        };

        for (var index = 0; index < points.Length; index++)
        {
            context.DrawLine(PatternPen, points[index], points[(index + 1) % points.Length]);
        }
    }

    private static void DrawPaperPlane(DrawingContext context, Point point)
    {
        var a = new Point(point.X, point.Y + 8);
        var b = new Point(point.X + 42, point.Y);
        var c = new Point(point.X + 27, point.Y + 36);
        context.DrawLine(PatternPen, a, b);
        context.DrawLine(PatternPen, b, c);
        context.DrawLine(PatternPen, c, new Point(point.X + 18, point.Y + 20));
        context.DrawLine(PatternPen, new Point(point.X + 18, point.Y + 20), a);
        context.DrawLine(PatternPen, new Point(point.X + 18, point.Y + 20), b);
    }

    private static void DrawHeart(DrawingContext context, Point point)
    {
        context.DrawEllipse(null, PatternPen, new Rect(point.X + 4, point.Y + 3, 18, 18));
        context.DrawEllipse(null, PatternPen, new Rect(point.X + 18, point.Y + 3, 18, 18));
        context.DrawLine(PatternPen, new Point(point.X + 5, point.Y + 14), new Point(point.X + 20, point.Y + 35));
        context.DrawLine(PatternPen, new Point(point.X + 35, point.Y + 14), new Point(point.X + 20, point.Y + 35));
    }

    private static int PositiveModulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
