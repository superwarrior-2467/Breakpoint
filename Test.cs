using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public static class Test
{
    public static void Run()
    {
        using Image<Rgba32> img = new(100, 100);

        img.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                
            });
        });
    }
}
