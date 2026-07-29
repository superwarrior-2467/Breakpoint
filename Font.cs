using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using Silk.NET.OpenGL;

/// <summary>Describes the location and layout metrics of a glyph in a <see cref="Font"/> atlas.</summary>
/// <remarks>
/// Metrics are expressed at the rasterization size passed to <see cref="Font.FromFile"/>.
/// <see cref="DesktopWindow.Draw(Font, string, Vector2, Color, float, float)"/> applies the
/// requested scale at render time.
/// </remarks>
public readonly struct GlyphInfo
{
    /// <summary>Gets the normalized left texture coordinate.</summary>
    public float U0 { get; }
    /// <summary>Gets the normalized top texture coordinate.</summary>
    public float V0 { get; }
    /// <summary>Gets the normalized right texture coordinate.</summary>
    public float U1 { get; }
    /// <summary>Gets the normalized bottom texture coordinate.</summary>
    public float V1 { get; }
    /// <summary>Gets the glyph ink width, in pixels.</summary>
    public float Width { get; }
    /// <summary>Gets the glyph ink height, in pixels.</summary>
    public float Height { get; }
    /// <summary>Gets the horizontal offset from the pen position to the ink bounds, in pixels.</summary>
    public float BearingX { get; }
    /// <summary>Gets the vertical offset from the pen position to the ink bounds, in pixels.</summary>
    public float BearingY { get; }
    /// <summary>Gets the horizontal distance to advance the pen after this glyph, in pixels.</summary>
    public float Advance { get; }

    /// <summary>Initializes a new instance of the <see cref="GlyphInfo"/> structure.</summary>
    /// <param name="u0">The normalized left texture coordinate.</param>
    /// <param name="v0">The normalized top texture coordinate.</param>
    /// <param name="u1">The normalized right texture coordinate.</param>
    /// <param name="v1">The normalized bottom texture coordinate.</param>
    /// <param name="width">The glyph ink width, in pixels.</param>
    /// <param name="height">The glyph ink height, in pixels.</param>
    /// <param name="bearingX">The horizontal ink offset, in pixels.</param>
    /// <param name="bearingY">The vertical ink offset, in pixels.</param>
    /// <param name="advance">The horizontal pen advance, in pixels.</param>
    public GlyphInfo(float u0, float v0, float u1, float v1, float width, float height, float bearingX, float bearingY, float advance)
    {
        U0 = u0;
        V0 = v0;
        U1 = u1;
        V1 = v1;
        Width = width;
        Height = height;
        BearingX = bearingX;
        BearingY = bearingY;
        Advance = advance;
    }
}

/// <summary>Represents a rasterized glyph atlas and the character metrics required to render text.</summary>
/// <remarks>
/// A font combines a single <see cref="Texture2D"/> atlas with per-character metrics. Create it
/// through <see cref="ContentManager.LoadFont"/> when possible so that its lifetime is managed
/// with the window's content. Only characters included during creation can be rendered.
/// </remarks>
public sealed class Font : IDisposable
{
    /// <summary>The fixed width used for generated glyph atlases.</summary>
    private const int AtlasMaxWidth = 512;
    /// <summary>The number of empty pixels inserted between packed glyphs.</summary>
    private const int GlyphPadding = 1;

    /// <summary>The printable ASCII character set used when no character set is supplied.</summary>
    private static readonly char[] DefaultCharset = BuildAsciiCharset();

    /// <summary>Gets the application-defined identifier assigned when the font was created.</summary>
    public string Key { get; }
    /// <summary>Gets the GPU texture containing glyph coverage in its alpha channel.</summary>
    public Texture2D Texture { get; }
    /// <summary>Gets the recommended line-to-line distance, in pixels, at the atlas rasterization size.</summary>
    public float LineHeight { get; }

    /// <summary>Maps each supported character to its atlas location and layout metrics.</summary>
    private readonly Dictionary<char, GlyphInfo> _glyphs;
    /// <summary>Indicates whether the owned texture has been disposed.</summary>
    private bool _disposed;

    /// <summary>Initializes a font from its prepared texture atlas and glyph metrics.</summary>
    private Font(string key, Texture2D texture, float lineHeight, Dictionary<char, GlyphInfo> glyphs)
    {
        Key = key;
        Texture = texture;
        LineHeight = lineHeight;
        _glyphs = glyphs;
    }

    /// <summary>Attempts to get the metrics for a supported character.</summary>
    /// <param name="c">The character to look up.</param>
    /// <param name="glyph">When this method returns <see langword="true"/>, the character metrics.</param>
    /// <returns><see langword="true"/> when <paramref name="c"/> is included in this font; otherwise, <see langword="false"/>.</returns>
    public bool TryGetGlyph(char c, out GlyphInfo glyph) => _glyphs.TryGetValue(c, out glyph);

    /// <summary>Measures the space required to render text with this font.</summary>
    /// <param name="text">The text to measure. Newline characters start a new line.</param>
    /// <param name="scale">The uniform rendering scale. The default is <c>1.0f</c>.</param>
    /// <returns>The width of the widest line and the combined line height, in pixels.</returns>
    /// <remarks>Characters that are not included in the font do not contribute to the measured width.</remarks>
    public Vector2 MeasureString(string text, float scale = 1f)
    {
        float width = 0f;
        float maxLineWidth = 0f;
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                maxLineWidth = MathF.Max(maxLineWidth, width);
                width = 0f;
                lines++;
                continue;
            }

            if (TryGetGlyph(c, out var glyph))
                width += glyph.Advance;
        }

        maxLineWidth = MathF.Max(maxLineWidth, width);
        return new Vector2(maxLineWidth * scale, lines * LineHeight * scale);
    }

    /// <summary>Loads a font file and creates a GPU atlas for the specified characters.</summary>
    /// <param name="gl">The OpenGL API used to upload the glyph atlas.</param>
    /// <param name="key">An application-defined identifier for the resulting font.</param>
    /// <param name="filePath">The path to a TrueType or OpenType font file.</param>
    /// <param name="pixelSize">The glyph rasterization size, in pixels.</param>
    /// <param name="charset">The characters to rasterize, or <see langword="null"/> for printable ASCII.</param>
    /// <returns>A font containing a texture atlas and metrics for the requested character set.</returns>
    /// <remarks>
    /// This factory does not cache results. Use <see cref="ContentManager.LoadFont"/> for
    /// cache-backed loading and manager-controlled ownership.
    /// </remarks>
    public static Font FromFile(GL gl, string key, string filePath, float pixelSize, IReadOnlyList<char>? charset = null)
    {
        charset ??= DefaultCharset;

        var collection = new FontCollection();
        FontFamily family = collection.Add(filePath);
        SixLabors.Fonts.Font sixLaborsFont = family.CreateFont(pixelSize, FontStyle.Regular);

        var glyphImages = new Dictionary<char, Image<Rgba32>>();
        var glyphAdvances = new Dictionary<char, float>();
        var glyphBearings = new Dictionary<char, (float X, float Y)>();

        try
        {
            foreach (char c in charset)
            {
                string s = c.ToString();
                var measureOptions = new TextOptions(sixLaborsFont);

                FontRectangle inkBounds = TextMeasurer.MeasureBounds(s, measureOptions);
                FontRectangle advanceBounds = TextMeasurer.MeasureAdvance(s, measureOptions);

                glyphAdvances[c] = advanceBounds.Width;

                if (inkBounds.Width <= 0f || inkBounds.Height <= 0f)
                {
                    // Whitespace and similar glyphs have no visible ink but still advance the pen.
                    glyphBearings[c] = (0f, 0f);
                    continue;
                }

                int w = Math.Max(1, (int)MathF.Ceiling(inkBounds.Width));
                int h = Math.Max(1, (int)MathF.Ceiling(inkBounds.Height));

                var glyphImage = new Image<Rgba32>(w, h);
                var drawOptions = new RichTextOptions(sixLaborsFont)
                {
                    Origin = new PointF(-inkBounds.X, -inkBounds.Y)
                };

                glyphImage.Mutate(ctx => ctx.DrawText(drawOptions, s, SixLabors.ImageSharp.Color.White));

                glyphImages[c] = glyphImage;
                glyphBearings[c] = (inkBounds.X, inkBounds.Y);
            }

            var sizes = glyphImages.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value.Width, kvp.Value.Height));
            var (positions, atlasWidth, atlasHeight) = PackGlyphs(sizes, AtlasMaxWidth, GlyphPadding);

            using var atlasImage = new Image<Rgba32>(Math.Max(1, atlasWidth), Math.Max(1, atlasHeight));
            atlasImage.Mutate(ctx =>
            {
                foreach (var (c, rect) in positions)
                    ctx.DrawImage(glyphImages[c], new Point(rect.X, rect.Y), 1f);
            });

            var texture = Texture2D.FromImage(gl, atlasImage);

            var glyphs = new Dictionary<char, GlyphInfo>();
            foreach (char c in charset)
            {
                float advance = glyphAdvances.TryGetValue(c, out var a) ? a : 0f;
                var (bearingX, bearingY) = glyphBearings.TryGetValue(c, out var b) ? b : (0f, 0f);

                if (positions.TryGetValue(c, out var rect))
                {
                    float u0 = rect.X / (float)atlasWidth;
                    float v0 = rect.Y / (float)atlasHeight;
                    float u1 = (rect.X + rect.Width) / (float)atlasWidth;
                    float v1 = (rect.Y + rect.Height) / (float)atlasHeight;

                    glyphs[c] = new GlyphInfo(u0, v0, u1, v1, rect.Width, rect.Height, bearingX, bearingY, advance);
                }
                else
                {
                    glyphs[c] = new GlyphInfo(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, advance);
                }
            }

            // Font metrics use design units, so convert the line height to atlas pixels.
            float lineHeight = sixLaborsFont.FontMetrics.LineHeight * sixLaborsFont.Size / sixLaborsFont.FontMetrics.UnitsPerEm;

            return new Font(key, texture, lineHeight, glyphs);
        }
        finally
        {
            foreach (var image in glyphImages.Values)
                image.Dispose();
        }
    }

    /// <summary>Packs rasterized glyph rectangles into rows within a fixed-width texture atlas.</summary>
    /// <remarks>Glyphs are sorted by height to reduce unused row space. This runs only during font creation.</remarks>
    private static (Dictionary<char, AtlasRect> Positions, int Width, int Height) PackGlyphs(
        Dictionary<char, (int Width, int Height)> sizes, int maxWidth, int padding)
    {
        var positions = new Dictionary<char, AtlasRect>();
        int x = 0, y = 0, rowHeight = 0;

        // Shelf packing is ordered from tallest to shortest to reduce unused row space.
        foreach (var kvp in sizes.OrderByDescending(k => k.Value.Height))
        {
            var (w, h) = kvp.Value;

            if (x + w > maxWidth)
            {
                x = 0;
                y += rowHeight + padding;
                rowHeight = 0;
            }

            positions[kvp.Key] = new AtlasRect(x, y, w, h);
            x += w + padding;
            rowHeight = Math.Max(rowHeight, h);
        }

        int atlasHeight = y + rowHeight;
        return (positions, maxWidth, atlasHeight);
    }

    /// <summary>Builds the inclusive printable ASCII range used by default font creation.</summary>
    private static char[] BuildAsciiCharset()
    {
        // Printable ASCII spans space (32) through tilde (126).
        var chars = new char[126 - 32 + 1];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = (char)(32 + i);
        return chars;
    }

    /// <summary>Represents the pixel bounds assigned to a glyph in the atlas.</summary>
    private readonly record struct AtlasRect(int X, int Y, int Width, int Height);

    /// <summary>Disposes the GPU texture atlas owned by this font.</summary>
    /// <remarks>This method is idempotent.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        Texture.Dispose();
        _disposed = true;
    }
}
