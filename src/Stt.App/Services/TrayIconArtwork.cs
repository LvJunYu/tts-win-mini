using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Stt.Core.Models;

namespace Stt.App.Services;

public static class TrayIconArtwork
{
    private static readonly Color GlyphColor = Color.FromArgb(252, 248, 238);
    private static readonly Color ReadyBadgeColor = Color.FromArgb(43, 125, 82);
    private static readonly Color RecordingBadgeColor = Color.FromArgb(180, 50, 43);
    private static readonly Color StatusDotColor = Color.FromArgb(250, 246, 238);

    public static Icon Create(AppSessionState state)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        DrawBadge(graphics, state);
        DrawGlyph(graphics, state);

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static void DrawBadge(Graphics graphics, AppSessionState state)
    {
        var badgeBounds = new RectangleF(4f, 4f, 24f, 24f);
        var shadowBounds = new RectangleF(4.7f, 5.6f, 22.6f, 22.6f);

        using var shadowBrush = new SolidBrush(Color.FromArgb(36, 7, 11, 16));
        using var backgroundBrush = new SolidBrush(GetBackgroundColor(state));
        using var outlinePen = new Pen(Color.FromArgb(64, 255, 255, 255), 1f);
        using var highlightPen = new Pen(Color.FromArgb(44, 255, 255, 255), 1.2f);
        using var shadowPath = CreateRoundedRectanglePath(shadowBounds, 7f);
        using var badgePath = CreateRoundedRectanglePath(badgeBounds, 7f);

        graphics.FillPath(shadowBrush, shadowPath);
        graphics.FillPath(backgroundBrush, badgePath);
        graphics.DrawPath(outlinePen, badgePath);
        graphics.DrawArc(highlightPen, 7f, 7f, 18f, 8f, 198, 144);
    }

    private static void DrawGlyph(Graphics graphics, AppSessionState state)
    {
        switch (state)
        {
            case AppSessionState.Idle:
            case AppSessionState.Starting:
            case AppSessionState.Recording:
            case AppSessionState.Processing:
            case AppSessionState.Ready:
            case AppSessionState.Error:
                DrawMicrophoneGlyph(graphics, state);
                break;
        }
    }

    private static void DrawMicrophoneGlyph(Graphics graphics, AppSessionState state)
    {
        using var brush = new SolidBrush(GlyphColor);
        using var pen = new Pen(GlyphColor, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        FillRoundedRectangle(graphics, brush, new RectangleF(11f, 7.5f, 10f, 12.5f), 4.8f);
        graphics.DrawArc(pen, 9.5f, 13.2f, 13f, 10.5f, 8, 164);
        graphics.DrawLine(pen, 16f, 23f, 16f, 25.6f);
        graphics.DrawLine(pen, 12.4f, 25.7f, 19.6f, 25.7f);

        switch (state)
        {
            case AppSessionState.Starting:
                DrawStatusDot(graphics);
                break;
            case AppSessionState.Processing:
                DrawStatusDot(graphics);
                break;
            case AppSessionState.Error:
                DrawErrorAccent(graphics);
                break;
        }
    }

    private static void DrawStatusDot(Graphics graphics)
    {
        using var accentBrush = new SolidBrush(StatusDotColor);
        using var accentPen = new Pen(Color.FromArgb(112, 255, 255, 255), 1f);
        graphics.FillEllipse(accentBrush, 20.2f, 6.2f, 5.8f, 5.8f);
        graphics.DrawEllipse(accentPen, 20.2f, 6.2f, 5.8f, 5.8f);
    }

    private static void DrawErrorAccent(Graphics graphics)
    {
        using var accentBrush = new SolidBrush(Color.FromArgb(143, 39, 34));
        using var pen = new Pen(Color.FromArgb(252, 248, 238), 1.75f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        graphics.FillEllipse(accentBrush, 19.2f, 5.8f, 7f, 7f);
        graphics.DrawLine(pen, 21f, 7.5f, 24.6f, 11.1f);
        graphics.DrawLine(pen, 24.6f, 7.5f, 21f, 11.1f);
    }

    private static Color GetBackgroundColor(AppSessionState state)
    {
        return state switch
        {
            AppSessionState.Recording => RecordingBadgeColor,
            AppSessionState.Processing => RecordingBadgeColor,
            AppSessionState.Error => RecordingBadgeColor,
            AppSessionState.Idle => ReadyBadgeColor,
            AppSessionState.Starting => ReadyBadgeColor,
            AppSessionState.Ready => ReadyBadgeColor,
            _ => ReadyBadgeColor
        };
    }

    private static void FillRoundedRectangle(
        Graphics graphics,
        Brush brush,
        RectangleF rectangle,
        float radius)
    {
        using var path = CreateRoundedRectanglePath(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
