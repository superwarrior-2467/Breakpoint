using System.Numerics;
using System.Runtime.CompilerServices;

namespace Breakpoint;

/// <summary>
/// Represents an RGBA color whose components are expressed as normalized floating-point values.
/// </summary>
/// <remarks>
/// Each component is conventionally in the range from <c>0.0f</c> to <c>1.0f</c>.
/// This type does not clamp values, allowing callers to preserve values required by a
/// rendering pipeline. It can tint text and other rendering primitives without taking a
/// dependency on <c>System.Drawing</c> or <c>SixLabors.ImageSharp</c>.
/// </remarks>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>Gets the red component.</summary>
    public float R { get; }
    /// <summary>Gets the green component.</summary>
    public float G { get; }
    /// <summary>Gets the blue component.</summary>
    public float B { get; }
    /// <summary>Gets the alpha, or opacity, component.</summary>
    public float A { get; }

    /// <summary>Initializes a new instance of the <see cref="Color"/> structure.</summary>
    /// <param name="r">The normalized red component.</param>
    /// <param name="g">The normalized green component.</param>
    /// <param name="b">The normalized blue component.</param>
    /// <param name="a">The normalized alpha component. The default is <c>1.0f</c>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Creates a color from 8-bit RGBA components.</summary>
    /// <param name="r">The red component, from <c>0</c> through <c>255</c>.</param>
    /// <param name="g">The green component, from <c>0</c> through <c>255</c>.</param>
    /// <param name="b">The blue component, from <c>0</c> through <c>255</c>.</param>
    /// <param name="a">The alpha component, from <c>0</c> through <c>255</c>. The default is <c>255</c>.</param>
    /// <returns>A <see cref="Color"/> with normalized components.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255)
        => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>Converts this color to a four-component vector in RGBA order.</summary>
    /// <returns>A vector containing <see cref="R"/>, <see cref="G"/>, <see cref="B"/>, and <see cref="A"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector4 ToVector4() => new(R, G, B, A);

    /// <summary>Gets an opaque white color.</summary>
    public static Color White => new(1f, 1f, 1f, 1f);
    /// <summary>Gets an opaque black color.</summary>
    public static Color Black => new(0f, 0f, 0f, 1f);
    /// <summary>Gets an opaque red color.</summary>
    public static Color Red => new(1f, 0f, 0f, 1f);
    /// <summary>Gets an opaque green color.</summary>
    public static Color Green => new(0f, 1f, 0f, 1f);
    /// <summary>Gets an opaque blue color.</summary>
    public static Color Blue => new(0f, 0f, 1f, 1f);
    /// <summary>Gets an opaque yellow color.</summary>
    public static Color Yellow => new(1f, 1f, 0f, 1f);
    /// <summary>Gets a fully transparent black color.</summary>
    public static Color Transparent => new(0f, 0f, 0f, 0f);

    /// <inheritdoc/>
    public bool Equals(Color other)
        => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <summary>Determines whether two colors are equal.</summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    /// <summary>Determines whether two colors are not equal.</summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}
