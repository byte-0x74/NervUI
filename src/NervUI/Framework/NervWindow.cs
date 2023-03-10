using System.Diagnostics;
using System.Numerics;
using Mochi.DearImGui;
using Mochi.DearImGui.Internal;
using Mochi.DearImGui.OpenTK;
using NervUI.Common;
using NervUI.Entities;
using NervUI.Framework.Modules;
using NervUI.Platforms;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NervUI.Framework;

public unsafe class NervWindow : NativeWindow
{
    //Render and Platform controller
    private PlatformBackend _platformBackend;
    private RendererBackend _rendererBackend;

    //Actions
    internal Action<uint> DockSpaceCallback;
    internal Action MenuBarCallback;
    internal Action StyleCallback;
    internal Action WindowLoad;
    internal Action ApplicationClosing;
    internal Action<FileDropEventArgs> FileDrop;
    internal Action<FocusedChangedEventArgs> FocusChanged;
    internal Action<MouseButtonEventArgs> MouseDown;
    internal Action<KeyboardKeyEventArgs> KeyUp;
    internal Action<KeyboardKeyEventArgs> KeyDown;
    
    internal Application Instance;
    private ApplicationOptions _options;
    private bool _firstTime = true;

    private const string GLSL_VERSION_130 = "#version 130";
    private Vector2 mousePos = new Vector2(0, 0);

    /// <summary>
    /// Create new NervWindow Instance
    /// </summary>
    /// <param name="nativeWindowSettings">OpenTK's native Window settings</param>
    /// <param name="options">NervUI Application options</param>
    /// <param name="instance">Instance of Application</param>
    public NervWindow(NativeWindowSettings nativeWindowSettings, ApplicationOptions options, Application instance) : base(nativeWindowSettings)
    {
        Context.MakeCurrent();
        
        Instance = instance;
        _options = options;
        VSync = options.VSync ? VSyncMode.On : VSyncMode.Off;
        WindowBorder = options.NativeWindow ? options.WindowBorder : WindowBorder.Hidden;
        WindowState = options.WindowState;
        StyleCallback = options.StyleCallback;
        
        GL.LoadBindings(new GLFWBindingsContext());
        CenterWindow();
        
        SetupImGui();
        
        //Log render info
        {
            Core.Log("--------------------Render Information--------------------", LogType.INFO, ConsoleColor.Yellow);
            Core.Log($"Render API:           {API}", LogType.INFO, ConsoleColor.Yellow);
            Core.Log($"Render API Version:   {APIVersion.Major}.{APIVersion.Minor}", LogType.INFO, ConsoleColor.Yellow);
            Core.Log($"Video Render Device:  {GL.GetString(StringName.Renderer)}", LogType.INFO, ConsoleColor.Yellow);
            Core.Log($"Video Vendor:         {GL.GetString(StringName.Vendor)}", LogType.INFO, ConsoleColor.Yellow);
            Core.Log($"Video Driver:         {GL.GetString(StringName.Version)}", LogType.INFO, ConsoleColor.Yellow);
            Core.Log($"ImGui Version:         {ImGui.IMGUI_VERSION}", LogType.INFO, ConsoleColor.Yellow);
            Core.Log("----------------------------------------------------------", LogType.INFO, ConsoleColor.Yellow);
        }

        if (WindowLoad != null)
            WindowLoad();
    }

    internal void Run()
    {
        ImGuiIO* io = ImGui.GetIO();
        while (!GLFW.WindowShouldClose(WindowPtr))
        {
            ProcessEvents();
            
            _rendererBackend.NewFrame();
            _platformBackend.NewFrame();
            ImGui.NewFrame();
            
            FileDialog.RenderFileDialog();

            SetupDockspace();
            if (MessageBox.showMB)
            {
                //TODO Allow custom MessageBox Titles
                ImGui.OpenPopup("MessageBoxPopup");
                MessageBox.RenderMessageBox();
            }
            NervUIMetrics.Render();
        
            foreach (var layer in Instance.Layers)
                layer.OnUIRender();
        
            ImGui.Render();
            GLFW.GetFramebufferSize(WindowPtr, out var displayW, out var displayH);
            GL.Viewport(0, 0, displayW, displayH);
            GL.ClearColor(0.45f, 0.55f, 0.6f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            _rendererBackend.RenderDrawData(ImGui.GetDrawData());

            if (io->ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
            {
                var backupCurrentContext = GLFW.GetCurrentContext();
                ImGui.UpdatePlatformWindows();
                ImGui.RenderPlatformWindowsDefault();
                GLFW.MakeContextCurrent(backupCurrentContext);
            }

            GLFW.SwapBuffers(WindowPtr);
        }
    }

    private void SetupDockspace()
    {
        ImGuiIO* io = ImGui.GetIO();
        
        var dockNodeFlags = ImGuiDockNodeFlags.None;
        var windowFlags = ImGuiWindowFlags.NoDocking;
        windowFlags |= MenuBarCallback != null ? ImGuiWindowFlags.MenuBar : 0;

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(viewport->WorkPos.X, viewport->WorkPos.Y), ImGuiCond.None, new Vector2(0, 0));
        ImGui.SetNextWindowSize(viewport->WorkSize);
        ImGui.SetNextWindowViewport(viewport->ID);

        if (_options.NativeWindow)
        {
            windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
                           ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus |
                           ImGuiWindowFlags.NoNavFocus;
        }
        else
        {
            windowFlags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        }

        
        windowFlags |= dockNodeFlags.HasFlag(ImGuiDockNodeFlags.PassthruCentralNode)
            ? ImGuiWindowFlags.NoBackground
            : 0;
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        bool t = true;
        if (_options.NativeWindow)
            ImGui.Begin(_options.Title, default, windowFlags);
        else
            ImGui.Begin(_options.Title, &t, windowFlags);
        ImGui.PopStyleVar();
        
        if (io->ConfigFlags.HasFlag(ImGuiConfigFlags.DockingEnable))
        {
            var dockspace_id = ImGui.GetID("OpenGLAppDockspace");
            ImGui.DockSpace(dockspace_id, new Vector2(0, 0), dockNodeFlags);

            if (_firstTime)
            {
                _firstTime = false;

                ImGuiInternal.DockBuilderRemoveNode(dockspace_id);
                ImGuiInternal.DockBuilderAddNode(dockspace_id, dockNodeFlags);
                ImGuiInternal.DockBuilderSetNodeSize(dockspace_id, viewport->Size);

                if (DockSpaceCallback != null)
                    DockSpaceCallback(dockspace_id);//dockNodeFlags
            }
        }
        
        if (MenuBarCallback != null)
            if (ImGui.BeginMenuBar())
            {
                MenuBarCallback();
                ImGui.EndMenuBar();
            }
    }

    private void SetupImGui()
    {
        ImGui.CreateContext();
        ImGuiIO* io = ImGui.GetIO();

        io->ConfigFlags |= _options.Docking ? ImGuiConfigFlags.DockingEnable : 0;
        io->ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io->ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

        LoadFonts();

        ImGuiStyle* style = ImGui.GetStyle();
        
        if (StyleCallback == null)
            Util.SetStyle();
        else
            StyleCallback();
        
        if (io->ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
        {
            style->WindowRounding = 1f;
            style->Colors[(int)ImGuiCol.WindowBg].W = 1f;
        }

        _platformBackend = new(this, true);
        _rendererBackend = new(GLSL_VERSION_130);
    }

    private void LoadFonts()
    {
        ImGuiIO* io = ImGui.GetIO();
        
        //Load Default font
        if (Instance.DefaultFont != null)
        {
            Instance.DefaultFont.FontData =
                io->Fonts->AddFontFromFileTTF(Instance.DefaultFont.Path, Instance.DefaultFont.Size);
                io->FontDefault = Instance.DefaultFont.FontData;
            Instance.DefaultFont.Loaded = true;
        }

        //Load additional font(s)
        foreach (var font in Instance.Fonts.Where(c => c.Loaded == false))
        {
            Core.Log($"Loaded font {font.Name} from {font.Path}", LogType.DEBUG);
            font.FontData = io->Fonts->AddFontFromFileTTF(font.Path, font.Size);
            font.Loaded = true;
        }
    }
    
    ~NervWindow()
    {
        _platformBackend.Dispose();
        _rendererBackend.Dispose();
        DockSpaceCallback = null;
        MenuBarCallback = null;
        StyleCallback = null;
        Instance = null;
    }
    
    protected override void OnFileDrop(FileDropEventArgs e)
    {
        if (FileDrop != null)
            FileDrop(e);
        base.OnFileDrop(e);
    }

    protected override void OnFocusedChanged(FocusedChangedEventArgs e)
    {
        if (FocusChanged != null)
            FocusChanged(e);
        base.OnFocusedChanged(e);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (MouseDown != null)
            MouseDown(e);
        if (e.Button == MouseButton.Left && !_options.NativeWindow)
        {
            if (MousePosition.Y <= 20)
            {
                var p = Process.GetCurrentProcess();
                Windows.DragMove(p.MainWindowHandle);
            }
            
            if (MousePosition.X >= _options.Width - 20 && MousePosition.Y <= 20)
            {
                if (ApplicationClosing != null)
                    ApplicationClosing();
                Environment.Exit(0);
            }

        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        mousePos.X = e.X;
        mousePos.Y = e.Y;
        base.OnMouseMove(e);
    }

    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        if (KeyUp != null)
            KeyUp(e);
        base.OnKeyUp(e);
    }

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        if (KeyDown != null)
            KeyDown(e);
        base.OnKeyDown(e);
    }
}

