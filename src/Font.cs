using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using Silk.NET.OpenGL;
using ISColor = SixLabors.ImageSharp.Color;
using SixLaborsFont = SixLabors.Fonts.Font;

namespace Breakpoint;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    /// cache-backed loading and manager-controlled ownership. Glyphs are measured once, packed into
    /// a single atlas layout, and rasterized directly at their packed position — no intermediate
    /// per-glyph images are allocated.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="charset"/> is empty.</exception>
    public static Font FromFile(GL gl, string key, string filePath, float pixelSize, IReadOnlyList<char>? charset = null)
    {
        charset ??= DefaultCharset;
        if (charset.Count == 0)
            throw new ArgumentException("El conjunto de caracteres no puede estar vacío.", nameof(charset));

        var collection = new FontCollection();
        FontFamily family = collection.Add(filePath);
        SixLaborsFont sixLaborsFont = family.CreateFont(pixelSize, FontStyle.Regular);

        GlyphMetrics[] metrics = MeasureGlyphs(sixLaborsFont, charset);
        AtlasRect?[] positions = PackGlyphs(metrics, AtlasMaxWidth, GlyphPadding, out int atlasWidth, out int atlasHeight);

        using Image<Rgba32> atlasImage = new(Math.Max(1, atlasWidth), Math.Max(1, atlasHeight));
        RasterizeGlyphs(atlasImage, sixLaborsFont, metrics, positions);

        Texture2D texture = Texture2D.FromImage(gl, atlasImage);
        Dictionary<char, GlyphInfo> glyphs = BuildGlyphLookup(metrics, positions, atlasWidth, atlasHeight);

        // Font metrics use design units, so convert the line height to atlas pixels.
        FontMetrics fontMetrics = sixLaborsFont.FontMetrics;
        float lineHeight = fontMetrics.HorizontalMetrics.LineHeight * sixLaborsFont.Size / fontMetrics.UnitsPerEm;

        return new Font(key, texture, lineHeight, glyphs);
    }

    /// <summary>Measures the ink bounds and advance width of every character in a charset.</summary>
    /// <remarks>This runs once per font load and does not rasterize anything.</remarks>
    private static GlyphMetrics[] MeasureGlyphs(SixLaborsFont font, IReadOnlyList<char> charset)
    {
        var measureOptions = new TextOptions(font);
        var metrics = new GlyphMetrics[charset.Count];

        for (int i = 0; i < charset.Count; i++)
        {
            char c = charset[i];
            string text = c.ToString();

            FontRectangle inkBounds = TextMeasurer.MeasureBounds(text, measureOptions);
            FontRectangle advanceBounds = TextMeasurer.MeasureAdvance(text, measureOptions);

            if (inkBounds.Width <= 0f || inkBounds.Height <= 0f)
            {
                // Whitespace and similar glyphs have no visible ink but still advance the pen.
                metrics[i] = new GlyphMetrics(c, text, advanceBounds.Width, 0f, 0f, 0, 0);
            }
            else
            {
                int width = Math.Max(1, (int)MathF.Ceiling(inkBounds.Width));
                int height = Math.Max(1, (int)MathF.Ceiling(inkBounds.Height));
                metrics[i] = new GlyphMetrics(c, text, advanceBounds.Width, inkBounds.X, inkBounds.Y, width, height);
            }
        }

        return metrics;
    }

    /// <summary>Packs glyph ink rectangles into rows within a fixed-width texture atlas.</summary>
    /// <remarks>Glyphs are packed tallest-first to reduce unused row space. This runs once per font load.</remarks>
    private static AtlasRect?[] PackGlyphs(GlyphMetrics[] metrics, int maxWidth, int padding, out int atlasWidth, out int atlasHeight)
    {
        var positions = new AtlasRect?[metrics.Length];

        int[] order = Enumerable.Range(0, metrics.Length)
            .Where(i => metrics[i].InkWidth > 0)
            .OrderByDescending(i => metrics[i].InkHeight)
            .ToArray();

        int x = 0, y = 0, rowHeight = 0;
        foreach (int i in order)
        {
            ref readonly GlyphMetrics m = ref metrics[i];

            if (x + m.InkWidth > maxWidth)
            {
                x = 0;
                y += rowHeight + padding;
                rowHeight = 0;
            }

            positions[i] = new AtlasRect(x, y, m.InkWidth, m.InkHeight);
            x += m.InkWidth + padding;
            rowHeight = Math.Max(rowHeight, m.InkHeight);
        }

        atlasWidth = maxWidth;
        atlasHeight = Math.Max(1, y + rowHeight);
        return positions;
    }

    /// <summary>Draws every packed glyph directly onto the atlas image at its assigned position.</summary>
    /// <remarks>
    /// A single <see cref="RichTextOptions"/> and <see cref="Brush"/> instance is reused across all
    /// glyphs; only <see cref="RichTextOptions.Origin"/> changes per glyph. No intermediate per-glyph
    /// images are created.
    /// </remarks>
    private static void RasterizeGlyphs(Image<Rgba32> atlasImage, SixLaborsFont font, GlyphMetrics[] metrics, AtlasRect?[] positions)
    {
        var drawOptions = new RichTextOptions(font);
        SolidBrush brush = Brushes.Solid(ISColor.White);

        atlasImage.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    if (positions[i] is not { } rect)
                        continue;

                    ref readonly GlyphMetrics m = ref metrics[i];

                    drawOptions.Origin = new PointF(
                        rect.X - m.BearingX,
                        rect.Y - m.BearingY);

                    canvas.DrawText(
                        drawOptions,
                        m.Text,
                        brush,
                        null);
                }
            });
        });
    }

    /// <summary>Builds the final per-character glyph metrics lookup from measured and packed data.</summary>
    private static Dictionary<char, GlyphInfo> BuildGlyphLookup(GlyphMetrics[] metrics, AtlasRect?[] positions, int atlasWidth, int atlasHeight)
    {
        var glyphs = new Dictionary<char, GlyphInfo>(metrics.Length);

        for (int i = 0; i < metrics.Length; i++)
        {
            ref readonly GlyphMetrics m = ref metrics[i];

            if (positions[i] is { } rect)
            {
                float u0 = rect.X / (float)atlasWidth;
                float v0 = rect.Y / (float)atlasHeight;
                float u1 = (rect.X + rect.Width) / (float)atlasWidth;
                float v1 = (rect.Y + rect.Height) / (float)atlasHeight;

                glyphs[m.Character] = new GlyphInfo(u0, v0, u1, v1, rect.Width, rect.Height, m.BearingX, m.BearingY, m.Advance);
            }
            else
            {
                glyphs[m.Character] = new GlyphInfo(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, m.Advance);
            }
        }

        return glyphs;
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

    /// <summary>Disposes the GPU texture atlas owned by this font.</summary>
    /// <remarks>This method is idempotent.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        Texture.Dispose();
        _disposed = true;
    }

    /// <summary>Holds the measured, pre-rasterization metrics of a single character.</summary>
    /// <param name="Character">The measured character.</param>
    /// <param name="Text">A cached single-character string, reused for measuring and drawing to avoid repeated allocation.</param>
    /// <param name="Advance">The horizontal pen advance, in pixels.</param>
    /// <param name="BearingX">The horizontal ink offset from the pen position, in pixels.</param>
    /// <param name="BearingY">The vertical ink offset from the pen position, in pixels.</param>
    /// <param name="InkWidth">The ceiling-rounded ink width, in pixels, or <c>0</c> for glyphs with no visible ink.</param>
    /// <param name="InkHeight">The ceiling-rounded ink height, in pixels, or <c>0</c> for glyphs with no visible ink.</param>
    private readonly record struct GlyphMetrics(char Character, string Text, float Advance, float BearingX, float BearingY, int InkWidth, int InkHeight);

    /// <summary>Represents the pixel bounds assigned to a glyph in the atlas.</summary>
    private readonly record struct AtlasRect(int X, int Y, int Width, int Height);
}
