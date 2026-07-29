using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.Windowing;

class Program
{
    static void Main()
    {
        var window = new DesktopWindow
        {
            Title = "UBI",
            Size = new Vector2D<int>(200, 200),
            Position = new Vector2D<int>(1000, 500),
            WindowBorder = Silk.NET.Windowing.WindowBorder.Hidden,
            TransparentBackground = true,
            TopMost = true,
            ClearColor = new Vector4(0f, 0f, 0f, 1f)
        };

        Texture2D? ubi = null;

        window.Loaded += () =>
        {
            //ubi = window.Content.LoadTexture("ubi", "Assets/ubi.png");
        };

        window.Rendered += _ =>
        {
            if (ubi is not null)
            {
                window.Draw(ubi, new Vector2(120, 120), new Vector2(128, 128));
            }
        };

        window.Closing += () =>
        {
            Console.WriteLine("Cerrando UBI...");
        };

        window.Run();
    }
}
