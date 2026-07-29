using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Breakpoint;

class Program
{
    static void Main()
    {
        var window = new DesktopWindow
        {
            Title = "UBI",
            Size = new Vector2D<int>(1280, 720),
            Position = new Vector2D<int>(300, 180),
            WindowBorder = Silk.NET.Windowing.WindowBorder.Hidden,
            TransparentBackground = true,
            TopMost = true,
            ClearColor = new Vector4(0f, 0f, 0.2f, 1f)
        };

        Texture2D? ubi = null;
        Font? font = null;

        window.Loaded += () =>
        {
            //ubi = window.Content.LoadTexture("ubi", "Assets/ubi.png");
            font = window.Content.LoadFont("font", "Assets/Fonts/font.ttf");
        };

        window.Rendered += _ =>
        {
            if (ubi is not null)
            {
                window.Draw(ubi, new Vector2(120, 120), new Vector2(128, 128));
            }
            if (font is not null)
            {
                window.Draw(font, "Coming Soon!", new Vector2(480, 340));
            }
        };

        window.Closing += () =>
        {
            Console.WriteLine("Closing...");
        };

        window.Run();
    }
}
