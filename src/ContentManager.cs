using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Silk.NET.OpenGL;

/// <summary>
/// Loads, caches, and owns texture and font resources associated with an OpenGL context.
/// </summary>
/// <remarks>
/// Resources are cached by case-insensitive keys. This manager owns every resource it returns;
/// disposing it disposes all cached <see cref="Texture2D"/> and <see cref="Font"/> instances.
/// </remarks>
public sealed class ContentManager : IDisposable
{
    /// <summary>The pixel size used when a font size is not specified.</summary>
    private const float DefaultFontPixelSize = 48f;

    /// <summary>The OpenGL API used to create managed GPU resources.</summary>
    private readonly GL _gl;
    /// <summary>Texture cache indexed by case-insensitive application-defined keys.</summary>
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Font cache indexed by case-insensitive application-defined keys.</summary>
    private readonly Dictionary<string, Font> _fonts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new <see cref="ContentManager"/> for the specified OpenGL context.</summary>
    /// <param name="gl">The Silk.NET OpenGL API used to upload resources.</param>
    public ContentManager(GL gl)
    {
        _gl = gl;
    }

    /// <summary>Loads a texture from a file or returns the texture already cached under the specified key.</summary>
    /// <param name="key">The case-insensitive cache key.</param>
    /// <param name="filePath">The path of an image readable by ImageSharp.</param>
    /// <returns>The cached or newly uploaded texture.</returns>
    /// <remarks>When <paramref name="key"/> is already present, <paramref name="filePath"/> is not read.</remarks>
    public Texture2D LoadTexture(string key, string filePath)
    {
        if (_textures.TryGetValue(key, out var existing))
            return existing;

        var texture = Texture2D.FromFile(_gl, filePath);
        _textures[key] = texture;
        return texture;
    }

    /// <summary>Gets a previously loaded texture.</summary>
    /// <param name="key">The case-insensitive cache key.</param>
    /// <returns>The cached texture.</returns>
    /// <exception cref="KeyNotFoundException">No texture is cached under <paramref name="key"/>.</exception>
    public Texture2D GetTexture(string key)
    {
        if (!_textures.TryGetValue(key, out var texture))
            throw new KeyNotFoundException($"No existe la textura '{key}'.");
        return texture;
    }

    /// <summary>Loads a TrueType or OpenType font and creates its GPU glyph atlas, or returns a cached font.</summary>
    /// <param name="key">The case-insensitive cache key.</param>
    /// <param name="filePath">The path to the TrueType or OpenType font file.</param>
    /// <param name="pixelSize">The glyph rasterization size, in pixels. The default is 48.</param>
    /// <returns>The cached or newly generated font.</returns>
    /// <remarks>
    /// A font created by this method includes the printable ASCII character set. Reusing a key
    /// returns the original instance without rereading the file or regenerating its atlas.
    /// </remarks>
    public Font LoadFont(string key, string filePath, float pixelSize = DefaultFontPixelSize)
    {
        if (_fonts.TryGetValue(key, out var existing))
            return existing;

        var font = Font.FromFile(_gl, key, filePath, pixelSize);
        _fonts[key] = font;
        return font;
    }

    /// <summary>Gets a previously loaded font.</summary>
    /// <param name="key">The case-insensitive cache key.</param>
    /// <returns>The cached font.</returns>
    /// <exception cref="KeyNotFoundException">No font is cached under <paramref name="key"/>.</exception>
    public Font GetFont(string key)
    {
        if (!_fonts.TryGetValue(key, out var font))
            throw new KeyNotFoundException($"No existe la fuente '{key}'.");
        return font;
    }

    /// <summary>Disposes all resources owned by this manager.</summary>
    /// <remarks>This method clears both caches and may be called more than once.</remarks>
    public void Dispose()
    {
        foreach (var texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();

        foreach (var font in _fonts.Values)
            font.Dispose();
        _fonts.Clear();
    }
}

/// <summary>Represents a two-dimensional OpenGL texture uploaded from ImageSharp image data.</summary>
/// <remarks>
/// A texture owns its GPU handle and must be disposed while its originating OpenGL context is
/// valid. Instances obtained from <see cref="ContentManager"/> are owned by that manager.
/// </remarks>
public sealed class Texture2D : IDisposable
{
    /// <summary>Gets the OpenGL texture object handle.</summary>
    public uint Handle { get; }
    /// <summary>Gets the texture width, in pixels.</summary>
    public int Width { get; }
    /// <summary>Gets the texture height, in pixels.</summary>
    public int Height { get; }

    /// <summary>The OpenGL API used to release the texture handle.</summary>
    private readonly GL _gl;
    /// <summary>Indicates whether the GPU handle has been released.</summary>
    private bool _disposed;

    /// <summary>Initializes a wrapper for an already created OpenGL texture handle.</summary>
    private Texture2D(GL gl, uint handle, int width, int height)
    {
        _gl = gl;
        Handle = handle;
        Width = width;
        Height = height;
    }

    /// <summary>Loads an image from disk and uploads it as an RGBA texture.</summary>
    /// <param name="gl">The OpenGL API used to create the texture.</param>
    /// <param name="filePath">The path of an image readable by ImageSharp.</param>
    /// <returns>A newly created GPU texture.</returns>
    /// <remarks>The loaded image is disposed after its pixel data has been uploaded.</remarks>
    public static Texture2D FromFile(GL gl, string filePath)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(filePath);
        return FromImage(gl, image);
    }

    /// <summary>Uploads an in-memory ImageSharp image to the GPU as an RGBA texture.</summary>
    /// <param name="gl">The OpenGL API used to create the texture.</param>
    /// <param name="image">The source image. Ownership remains with the caller.</param>
    /// <returns>A newly created GPU texture with linear filtering and clamp-to-edge wrapping.</returns>
    /// <remarks>
    /// This method is shared by file-based texture loading and <see cref="Font"/> atlas creation.
    /// It uploads pixels immediately and generates mipmaps before returning.
    /// </remarks>
    public static unsafe Texture2D FromImage(GL gl, Image<Rgba32> image)
    {
        byte[] pixelData = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixelData);

        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        fixed (byte* ptr = pixelData)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                ptr);
        }

        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        return new Texture2D(gl, handle, image.Width, image.Height);
    }

    /// <summary>Releases the underlying OpenGL texture object.</summary>
    /// <remarks>This method is idempotent.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _gl.DeleteTexture(Handle);
        _disposed = true;
    }
}
