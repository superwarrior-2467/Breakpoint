namespace Breakpoint;

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
