using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Breakpoint;

/// <summary>
/// Provides a Silk.NET-backed desktop window with queued sprite and text rendering.
/// </summary>
/// <remarks>
/// Configure window properties before calling <see cref="Run"/>. The <see cref="Loaded"/> event
/// is raised after the OpenGL context, input system, and <see cref="Content"/> manager are ready.
/// During each frame, <see cref="Rendered"/> is raised before queued <see cref="Draw(Texture2D, Vector2, Vector2)"/>
/// calls are flushed. This type is intended for use from the window's event thread.
/// </remarks>
/// <example>
/// <code>
/// using var window = new DesktopWindow { Title = "Sample" };
/// window.Loaded += () => texture = window.Content.LoadTexture("logo", "logo.png");
/// window.Rendered += _ => window.Draw(texture, new Vector2(20, 20), new Vector2(128, 128));
/// window.Run();
/// </code>
/// </example>
public sealed class DesktopWindow : IDisposable
{
    /// <summary>Gets or sets the initial position of the window, in screen pixels.</summary>
    public Vector2D<int> Position { get; set; } = new(50, 50);
    /// <summary>Gets or sets the initial client-area size of the window, in pixels.</summary>
    public Vector2D<int> Size { get; set; } = new(800, 600);
    /// <summary>Gets or sets the title displayed by the native window.</summary>
    public string Title { get; set; } = "UBI";

    /// <summary>Gets or sets the native window border and resize behavior.</summary>
    public WindowBorder WindowBorder { get; set; } = WindowBorder.Resizable;
    /// <summary>Gets or sets whether the framebuffer is created with transparency support.</summary>
    public bool TransparentBackground { get; set; } = false;
    /// <summary>Gets or sets whether the native window remains above non-topmost windows.</summary>
    public bool TopMost { get; set; } = false;
    /// <summary>Gets or sets the initial native window state.</summary>
    public WindowState WindowState { get; set; } = WindowState.Normal;

    /// <summary>Gets or sets the RGBA color used to clear the framebuffer at the start of each frame.</summary>
    public Vector4 ClearColor { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Gets the underlying Silk.NET window after <see cref="Run"/> has been called.</summary>
    /// <value>The native window, or <see langword="null"/> before initialization.</value>
    public IWindow? NativeWindow => _window;
    /// <summary>Gets the OpenGL API after the window has loaded.</summary>
    /// <value>The OpenGL API, or <see langword="null"/> before initialization.</value>
    public GL? GL => _gl;
    /// <summary>Gets the Silk.NET input context after the window has loaded.</summary>
    /// <value>The input context, or <see langword="null"/> before initialization.</value>
    public IInputContext? Input => _input;
    /// <summary>Gets the content manager associated with this window's OpenGL context.</summary>
    /// <value>The initialized content manager.</value>
    /// <exception cref="InvalidOperationException">The window has not reached the <see cref="Loaded"/> stage.</exception>
    public ContentManager Content => _content ?? throw new InvalidOperationException("Content aún no está listo. Usa esto dentro de Loaded.");

    /// <summary>Occurs after the OpenGL context, input context, renderer, and <see cref="Content"/> are initialized.</summary>
    public event Action? Loaded;
    /// <summary>Occurs once per frame after clearing the framebuffer and before queued rendering is flushed.</summary>
    /// <remarks>The event argument is the elapsed time since the preceding rendered frame, in seconds.</remarks>
    public event Action<double>? Rendered;
    /// <summary>Occurs when the native window begins closing, before managed GPU resources are released.</summary>
    public event Action? Closing;

    /// <summary>The underlying Silk.NET window.</summary>
    private IWindow? _window;
    /// <summary>The OpenGL API bound to <see cref="_window"/>.</summary>
    private GL? _gl;
    /// <summary>The input context created for <see cref="_window"/>.</summary>
    private IInputContext? _input;
    /// <summary>The content manager that owns resources loaded for this window.</summary>
    private ContentManager? _content;

    /// <summary>Keys currently reported as pressed by registered keyboards.</summary>
    private readonly HashSet<Key> _pressedKeys = new();
    /// <summary>Mouse buttons currently reported as pressed by registered mice.</summary>
    private readonly HashSet<MouseButton> _pressedMouseButtons = new();
    /// <summary>Sprite draw commands accumulated for the current frame.</summary>
    private readonly List<SpriteCommand> _drawQueue = new();
    /// <summary>Text draw commands accumulated for the current frame.</summary>
    private readonly List<TextCommand> _textDrawQueue = new();

    // Sprite rendering pipeline resources.
    private uint _shaderProgram;
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private int _uTextureLocation;

    // Text rendering pipeline resources.
    private uint _textShaderProgram;
    private uint _textVao;
    private uint _textVbo;
    private uint _textEbo;
    private int _uTextTextureLocation;

    // Reusable text-batching buffers grow on demand and are retained to avoid steady-state allocations.
    private float[] _textVertexBuffer = new float[256 * 4 * TextVertexFloats];
    private uint[] _textIndexBuffer = new uint[256 * 6];

    /// <summary>The number of float values in one text vertex: position, UV coordinates, and color.</summary>
    private const int TextVertexFloats = 8;

    /// <summary>Indicates whether <see cref="Run"/> has already initialized the window.</summary>
    private bool _initialized;

    /// <summary>Gets a snapshot view of keys currently pressed by registered keyboards.</summary>
    public IReadOnlyCollection<Key> PressedKeys => _pressedKeys;
    /// <summary>Gets a snapshot view of mouse buttons currently pressed by registered mice.</summary>
    public IReadOnlyCollection<MouseButton> PressedMouseButtons => _pressedMouseButtons;

    /// <summary>Creates a point-in-time copy of the currently pressed keyboard keys and mouse buttons.</summary>
    /// <returns>An input snapshot that is unaffected by subsequent input events.</returns>
    public InputSnapshot GetPressedInputs()
        => new(_pressedKeys.ToArray(), _pressedMouseButtons.ToArray());

    /// <summary>Creates the native window and starts its event loop.</summary>
    /// <exception cref="InvalidOperationException">The window has already been started.</exception>
    /// <remarks>
    /// This method does not return until the native event loop ends. Configure window properties and
    /// subscribe to events before calling it.
    /// </remarks>
    public void Run()
    {
        if (_initialized)
            throw new InvalidOperationException("Esta ventana ya fue iniciada.");

        var options = WindowOptions.Default;
        options.Position = Position;
        options.Size = Size;
        options.Title = Title;
        options.WindowBorder = WindowBorder;
        options.TransparentFramebuffer = TransparentBackground;
        options.TopMost = TopMost;
        options.WindowState = WindowState;

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _initialized = true;
        _window.Run();
    }

    /// <summary>Queues a texture for rendering during the current frame.</summary>
    /// <param name="texture">The GPU texture to render.</param>
    /// <param name="position">The top-left destination position, in client-area pixels.</param>
    /// <param name="size">The destination size, in pixels.</param>
    /// <remarks>Queued commands are rendered after the <see cref="Rendered"/> event returns.</remarks>
    public void Draw(Texture2D texture, Vector2 position, Vector2 size)
    {
        _drawQueue.Add(new SpriteCommand(texture, position, size));
    }

    /// <summary>Queues a cached texture for rendering during the current frame.</summary>
    /// <param name="textureKey">The key used when the texture was loaded into <see cref="Content"/>.</param>
    /// <param name="position">The top-left destination position, in client-area pixels.</param>
    /// <param name="size">The destination size, in pixels.</param>
    /// <exception cref="InvalidOperationException">Content has not been initialized.</exception>
    /// <exception cref="KeyNotFoundException">No texture is cached under <paramref name="textureKey"/>.</exception>
    public void Draw(string textureKey, Vector2 position, Vector2 size)
    {
        if (_content is null)
            throw new InvalidOperationException("Content no está listo todavía.");

        Draw(_content.GetTexture(textureKey), position, size);
    }

    /// <summary>Queues unscaled, unrotated white text for rendering during the current frame.</summary>
    /// <param name="font">The font that supplies the glyph atlas and metrics.</param>
    /// <param name="text">The text to render.</param>
    /// <param name="position">The text origin, in client-area pixels.</param>
    public void Draw(Font font, string text, Vector2 position)
        => Draw(font, text, position, Color.White, 1f, 0f);

    /// <summary>Queues unscaled, unrotated tinted text for rendering during the current frame.</summary>
    /// <param name="font">The font that supplies the glyph atlas and metrics.</param>
    /// <param name="text">The text to render.</param>
    /// <param name="position">The text origin, in client-area pixels.</param>
    /// <param name="color">The RGBA tint applied to glyph coverage.</param>
    public void Draw(Font font, string text, Vector2 position, Color color)
        => Draw(font, text, position, color, 1f, 0f);

    /// <summary>Queues tinted, uniformly scaled text for rendering during the current frame.</summary>
    /// <param name="font">The font that supplies the glyph atlas and metrics.</param>
    /// <param name="text">The text to render.</param>
    /// <param name="position">The text origin, in client-area pixels.</param>
    /// <param name="color">The RGBA tint applied to glyph coverage.</param>
    /// <param name="scale">The uniform glyph scale.</param>
    public void Draw(Font font, string text, Vector2 position, Color color, float scale)
        => Draw(font, text, position, color, scale, 0f);

    /// <summary>Queues tinted, scaled, and rotated text for rendering during the current frame.</summary>
    /// <param name="font">The font that supplies the glyph atlas and metrics.</param>
    /// <param name="text">The text to render. Newline characters begin a new line.</param>
    /// <param name="position">The rotation origin and initial text origin, in client-area pixels.</param>
    /// <param name="color">The RGBA tint applied to glyph coverage.</param>
    /// <param name="scale">The uniform glyph scale.</param>
    /// <param name="rotation">The clockwise rotation, in radians, around <paramref name="position"/>.</param>
    /// <remarks>Empty text is ignored. Consecutive commands using the same font atlas are batched.</remarks>
    public void Draw(Font font, string text, Vector2 position, Color color, float scale, float rotation)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _textDrawQueue.Add(new TextCommand(font, text, position, color, scale, rotation));
    }

    /// <summary>Determines whether a keyboard key is currently pressed.</summary>
    public bool IsKeyDown(Key key) => _pressedKeys.Contains(key);
    /// <summary>Determines whether a mouse button is currently pressed.</summary>
    public bool IsMouseButtonDown(MouseButton button) => _pressedMouseButtons.Contains(button);

    /// <summary>Requests that the native window close.</summary>
    /// <remarks>If the window has not been created, this method has no effect.</remarks>
    public void Close() => _window?.Close();

    /// <summary>Initializes OpenGL, input tracking, content management, and rendering resources.</summary>
    private void OnLoad()
    {
        _gl = GL.GetApi(_window!);
        _input = _window!.CreateInput();
        _content = new ContentManager(_gl);

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
        }

        InitializeRenderer();

        Loaded?.Invoke();
    }

    /// <summary>Clears the framebuffer, raises the frame event, and flushes queued rendering commands.</summary>
    /// <param name="deltaTime">The elapsed time since the previous frame, in seconds.</param>
    private void OnRender(double deltaTime)
    {
        if (_gl is null)
            return;

        _gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        Rendered?.Invoke(deltaTime);

        FlushDrawQueue();
        FlushTextQueue();
    }

    /// <summary>Raises the closing event and releases resources owned by the window.</summary>
    private void OnClosing()
    {
        Closing?.Invoke();

        _content?.Dispose();
        _content = null;

        if (_shaderProgram != 0) _gl?.DeleteProgram(_shaderProgram);
        if (_vbo != 0) _gl?.DeleteBuffer(_vbo);
        if (_ebo != 0) _gl?.DeleteBuffer(_ebo);
        if (_vao != 0) _gl?.DeleteVertexArray(_vao);

        if (_textShaderProgram != 0) _gl?.DeleteProgram(_textShaderProgram);
        if (_textVbo != 0) _gl?.DeleteBuffer(_textVbo);
        if (_textEbo != 0) _gl?.DeleteBuffer(_textEbo);
        if (_textVao != 0) _gl?.DeleteVertexArray(_textVao);

        _input?.Dispose();
        _input = null;
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode) => _pressedKeys.Add(key);
    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode) => _pressedKeys.Remove(key);
    private void OnMouseDown(IMouse mouse, MouseButton button) => _pressedMouseButtons.Add(button);
    private void OnMouseUp(IMouse mouse, MouseButton button) => _pressedMouseButtons.Remove(button);

    /// <summary>Creates the shader programs and GPU buffers used by sprite and text rendering.</summary>
    /// <exception cref="Exception">A sprite or text shader fails to compile or link.</exception>
    private unsafe void InitializeRenderer()
    {
        if (_gl is null || _window is null)
            return;

        const string vertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec2 aTexCoord;

        out vec2 vTexCoord;

        void main()
        {
            gl_Position = vec4(aPosition, 1.0);
            vTexCoord = aTexCoord;
        }
        """;

        const string fragmentShaderSource = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 FragColor;

        uniform sampler2D uTexture;

        void main()
        {
            FragColor = texture(uTexture, vTexCoord);
        }
        """;

        uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexShaderSource);
        _gl.CompileShader(vertexShader);
        _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vStatus);
        if (vStatus != (int)GLEnum.True)
            throw new Exception("Vertex shader failed: " + _gl.GetShaderInfoLog(vertexShader));

        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentShaderSource);
        _gl.CompileShader(fragmentShader);
        _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fStatus);
        if (fStatus != (int)GLEnum.True)
            throw new Exception("Fragment shader failed: " + _gl.GetShaderInfoLog(fragmentShader));

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vertexShader);
        _gl.AttachShader(_shaderProgram, fragmentShader);
        _gl.LinkProgram(_shaderProgram);
        _gl.GetProgram(_shaderProgram, ProgramPropertyARB.LinkStatus, out int lStatus);
        if (lStatus != (int)GLEnum.True)
            throw new Exception("Program failed to link: " + _gl.GetProgramInfoLog(_shaderProgram));

        _gl.DetachShader(_shaderProgram, vertexShader);
        _gl.DetachShader(_shaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, new float[20], BufferUsageARB.DynamicDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, new uint[] { 0, 1, 2, 2, 3, 0 }, BufferUsageARB.StaticDraw);

        const uint positionLoc = 0;
        const uint texCoordLoc = 1;

        _gl.EnableVertexAttribArray(positionLoc);
        _gl.VertexAttribPointer(positionLoc, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);

        _gl.EnableVertexAttribArray(texCoordLoc);
        _gl.VertexAttribPointer(texCoordLoc, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

        _gl.UseProgram(_shaderProgram);
        _uTextureLocation = _gl.GetUniformLocation(_shaderProgram, "uTexture");
        _gl.Uniform1(_uTextureLocation, 0);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.DepthTest);

        InitializeTextRenderer();
    }

    /// <summary>Creates shader and GPU buffers for the text-rendering pipeline.</summary>
    /// <remarks>
    /// This method runs once during renderer initialization. Its VAO, VBO, and EBO are reused for
    /// all text submitted in subsequent frames.
    /// </remarks>
    private unsafe void InitializeTextRenderer()
    {
        if (_gl is null)
            return;

        const string textVertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;

        out vec2 vTexCoord;
        out vec4 vColor;

        void main()
        {
            gl_Position = vec4(aPosition, 0.0, 1.0);
            vTexCoord = aTexCoord;
            vColor = aColor;
        }
        """;

        const string textFragmentShaderSource = """
        #version 330 core
        in vec2 vTexCoord;
        in vec4 vColor;
        out vec4 FragColor;

        uniform sampler2D uTexture;

        void main()
        {
            // The atlas stores glyph coverage in alpha; per-vertex color permits differently tinted text in one batch.
            float coverage = texture(uTexture, vTexCoord).a;
            FragColor = vec4(vColor.rgb, coverage * vColor.a);
        }
        """;

        uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, textVertexShaderSource);
        _gl.CompileShader(vertexShader);
        _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vStatus);
        if (vStatus != (int)GLEnum.True)
            throw new Exception("Text vertex shader failed: " + _gl.GetShaderInfoLog(vertexShader));

        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, textFragmentShaderSource);
        _gl.CompileShader(fragmentShader);
        _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fStatus);
        if (fStatus != (int)GLEnum.True)
            throw new Exception("Text fragment shader failed: " + _gl.GetShaderInfoLog(fragmentShader));

        _textShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_textShaderProgram, vertexShader);
        _gl.AttachShader(_textShaderProgram, fragmentShader);
        _gl.LinkProgram(_textShaderProgram);
        _gl.GetProgram(_textShaderProgram, ProgramPropertyARB.LinkStatus, out int lStatus);
        if (lStatus != (int)GLEnum.True)
            throw new Exception("Text program failed to link: " + _gl.GetProgramInfoLog(_textShaderProgram));

        _gl.DetachShader(_textShaderProgram, vertexShader);
        _gl.DetachShader(_textShaderProgram, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _textVao = _gl.GenVertexArray();
        _textVbo = _gl.GenBuffer();
        _textEbo = _gl.GenBuffer();

        _gl.BindVertexArray(_textVao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _textVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_textVertexBuffer.Length * sizeof(float)), null, BufferUsageARB.DynamicDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _textEbo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(_textIndexBuffer.Length * sizeof(uint)), null, BufferUsageARB.DynamicDraw);

        const uint positionLoc = 0;
        const uint texCoordLoc = 1;
        const uint colorLoc = 2;
        uint stride = (uint)(TextVertexFloats * sizeof(float));

        _gl.EnableVertexAttribArray(positionLoc);
        _gl.VertexAttribPointer(positionLoc, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(texCoordLoc);
        _gl.VertexAttribPointer(texCoordLoc, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));

        _gl.EnableVertexAttribArray(colorLoc);
        _gl.VertexAttribPointer(colorLoc, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

        _gl.UseProgram(_textShaderProgram);
        _uTextTextureLocation = _gl.GetUniformLocation(_textShaderProgram, "uTexture");
        _gl.Uniform1(_uTextTextureLocation, 0);
    }

    /// <summary>Uploads and draws every queued sprite, then clears the sprite command queue.</summary>
    private unsafe void FlushDrawQueue()
    {
        if (_gl is null || _window is null || _drawQueue.Count == 0)
            return;

        int width = Math.Max(1, _window.Size.X);
        int height = Math.Max(1, _window.Size.Y);

        _gl.UseProgram(_shaderProgram);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        foreach (var draw in _drawQueue)
        {
            float x = draw.Position.X;
            float y = draw.Position.Y;
            float w = draw.Size.X;
            float h = draw.Size.Y;

            float left = (x / width) * 2f - 1f;
            float right = ((x + w) / width) * 2f - 1f;
            float top = 1f - (y / height) * 2f;
            float bottom = 1f - ((y + h) / height) * 2f;

            float[] vertices =
            {
                left,  top,    0f, 0f, 0f,
                right, top,    0f, 1f, 0f,
                right, bottom, 0f, 1f, 1f,
                left,  bottom, 0f, 0f, 1f
            };

            _gl.BindTexture(TextureTarget.Texture2D, draw.Texture.Handle);
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.DynamicDraw);
            _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0);
        }

        _drawQueue.Clear();

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Uploads and draws every queued text command, then clears the text command queue.</summary>
    /// <remarks>
    /// Consecutive commands sharing an atlas texture are combined into one draw call. The method
    /// builds a contiguous vertex and index range for all renderable glyphs in each batch.
    /// </remarks>
    private unsafe void FlushTextQueue()
    {
        if (_gl is null || _window is null || _textDrawQueue.Count == 0)
            return;

        int width = Math.Max(1, _window.Size.X);
        int height = Math.Max(1, _window.Size.Y);

        _gl.UseProgram(_textShaderProgram);
        _gl.BindVertexArray(_textVao);
        _gl.ActiveTexture(TextureUnit.Texture0);

        int i = 0;
        while (i < _textDrawQueue.Count)
        {
            uint currentTextureHandle = _textDrawQueue[i].Font.Texture.Handle;

            int start = i;
            int quadCount = 0;
            while (i < _textDrawQueue.Count && _textDrawQueue[i].Font.Texture.Handle == currentTextureHandle)
            {
                quadCount += CountRenderableGlyphs(_textDrawQueue[i]);
                i++;
            }

            if (quadCount == 0)
                continue;

            EnsureTextBufferCapacity(quadCount);

            int vertexFloatIndex = 0;
            int indexIntIndex = 0;
            uint quadCounter = 0;

            for (int j = start; j < i; j++)
            {
                AppendTextCommand(_textDrawQueue[j], width, height, ref vertexFloatIndex, ref indexIntIndex, ref quadCounter);
            }

            _gl.BindTexture(TextureTarget.Texture2D, currentTextureHandle);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _textVbo);
            fixed (float* vptr = _textVertexBuffer)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertexFloatIndex * sizeof(float)), vptr, BufferUsageARB.DynamicDraw);
            }

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _textEbo);
            fixed (uint* iptr = _textIndexBuffer)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indexIntIndex * sizeof(uint)), iptr, BufferUsageARB.DynamicDraw);
            }

            _gl.DrawElements(PrimitiveType.Triangles, (uint)indexIntIndex, DrawElementsType.UnsignedInt, (void*)0);
        }

        _textDrawQueue.Clear();

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    /// <summary>Counts visible glyph quads that a text command will contribute to a batch.</summary>
    private static int CountRenderableGlyphs(TextCommand cmd)
    {
        int count = 0;
        foreach (char c in cmd.Text)
        {
            if (c == '\n')
                continue;

            if (cmd.Font.TryGetGlyph(c, out var glyph) && glyph.Width > 0f && glyph.Height > 0f)
                count++;
        }
        return count;
    }

    /// <summary>Appends the renderable glyphs of a command to the reusable batch buffers.</summary>
    private void AppendTextCommand(
        TextCommand cmd,
        int screenWidth,
        int screenHeight,
        ref int vertexFloatIndex,
        ref int indexIntIndex,
        ref uint quadCounter)
    {
        float penX = 0f;
        float penY = 0f;
        float cos = MathF.Cos(cmd.Rotation);
        float sin = MathF.Sin(cmd.Rotation);
        Vector4 color = cmd.Color.ToVector4();

        foreach (char c in cmd.Text)
        {
            if (c == '\n')
            {
                penX = 0f;
                penY += cmd.Font.LineHeight;
                continue;
            }

            if (!cmd.Font.TryGetGlyph(c, out var glyph))
                continue;

            if (glyph.Width > 0f && glyph.Height > 0f)
            {
                float px = cmd.Position.X + (penX + glyph.BearingX) * cmd.Scale;
                float py = cmd.Position.Y + (penY + glyph.BearingY) * cmd.Scale;
                float w = glyph.Width * cmd.Scale;
                float h = glyph.Height * cmd.Scale;

                AppendGlyphQuad(
                    px, py, w, h,
                    glyph.U0, glyph.V0, glyph.U1, glyph.V1,
                    color, cos, sin, cmd.Position,
                    screenWidth, screenHeight,
                    ref vertexFloatIndex, ref indexIntIndex, ref quadCounter);
            }

            penX += glyph.Advance;
        }
    }

    /// <summary>Appends one glyph's four vertices and six indices to the reusable text batch buffers.</summary>
    /// <remarks>
    /// Rotation is applied around <paramref name="origin"/>, which is the position passed to
    /// <see cref="Draw(Font, string, Vector2, Color, float, float)"/>.
    /// </remarks>
    private void AppendGlyphQuad(
        float px, float py, float w, float h,
        float u0, float v0, float u1, float v1,
        Vector4 color, float cos, float sin, Vector2 origin,
        int screenWidth, int screenHeight,
        ref int vertexFloatIndex, ref int indexIntIndex, ref uint quadCounter)
    {
        Span<Vector2> corners = stackalloc Vector2[4]
        {
            new Vector2(px, py),
            new Vector2(px + w, py),
            new Vector2(px + w, py + h),
            new Vector2(px, py + h)
        };

        Span<Vector2> uvs = stackalloc Vector2[4]
        {
            new Vector2(u0, v0),
            new Vector2(u1, v0),
            new Vector2(u1, v1),
            new Vector2(u0, v1)
        };

        for (int k = 0; k < 4; k++)
        {
            Vector2 rel = corners[k] - origin;
            Vector2 rotated = new(
                rel.X * cos - rel.Y * sin,
                rel.X * sin + rel.Y * cos);
            Vector2 world = origin + rotated;

            float ndcX = (world.X / screenWidth) * 2f - 1f;
            float ndcY = 1f - (world.Y / screenHeight) * 2f;

            _textVertexBuffer[vertexFloatIndex++] = ndcX;
            _textVertexBuffer[vertexFloatIndex++] = ndcY;
            _textVertexBuffer[vertexFloatIndex++] = uvs[k].X;
            _textVertexBuffer[vertexFloatIndex++] = uvs[k].Y;
            _textVertexBuffer[vertexFloatIndex++] = color.X;
            _textVertexBuffer[vertexFloatIndex++] = color.Y;
            _textVertexBuffer[vertexFloatIndex++] = color.Z;
            _textVertexBuffer[vertexFloatIndex++] = color.W;
        }

        uint baseVertex = quadCounter * 4;
        _textIndexBuffer[indexIntIndex++] = baseVertex + 0;
        _textIndexBuffer[indexIntIndex++] = baseVertex + 1;
        _textIndexBuffer[indexIntIndex++] = baseVertex + 2;
        _textIndexBuffer[indexIntIndex++] = baseVertex + 2;
        _textIndexBuffer[indexIntIndex++] = baseVertex + 3;
        _textIndexBuffer[indexIntIndex++] = baseVertex + 0;

        quadCounter++;
    }

    /// <summary>Ensures that reusable text buffers have sufficient capacity for a glyph batch.</summary>
    /// <remarks>
    /// Buffers grow geometrically when required and are never shrunk, avoiding allocations per
    /// frame once the workload reaches a steady state.
    /// </remarks>
    private void EnsureTextBufferCapacity(int quadCount)
    {
        int neededVertexFloats = quadCount * 4 * TextVertexFloats;
        int neededIndices = quadCount * 6;

        if (_textVertexBuffer.Length < neededVertexFloats)
        {
            int newSize = Math.Max(_textVertexBuffer.Length * 2, neededVertexFloats);
            _textVertexBuffer = new float[newSize];
        }

        if (_textIndexBuffer.Length < neededIndices)
        {
            int newSize = Math.Max(_textIndexBuffer.Length * 2, neededIndices);
            _textIndexBuffer = new uint[newSize];
        }
    }

    /// <summary>Requests window closure and disposes the current input context.</summary>
    /// <remarks>Native closing releases content and GPU rendering resources.</remarks>
    public void Dispose()
    {
        Close();
        _input?.Dispose();
    }

    /// <summary>Represents a point-in-time copy of the keyboard and mouse button state.</summary>
    /// <param name="Keys">The pressed keyboard keys at the time the snapshot was created.</param>
    /// <param name="MouseButtons">The pressed mouse buttons at the time the snapshot was created.</param>
    public readonly record struct InputSnapshot(Key[] Keys, MouseButton[] MouseButtons);

    /// <summary>Stores the parameters of one deferred sprite draw operation.</summary>
    private readonly record struct SpriteCommand(Texture2D Texture, Vector2 Position, Vector2 Size);

    /// <summary>Stores the parameters of one deferred text draw operation.</summary>
    private readonly record struct TextCommand(Font Font, string Text, Vector2 Position, Color Color, float Scale, float Rotation);
}
