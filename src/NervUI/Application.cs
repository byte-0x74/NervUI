using Mochi.DearImGui;
using NervUI.Common;
using NervUI.Entities;
using NervUI.Framework;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace NervUI;

public unsafe class Application
{
    public ApplicationOptions Options { get; private set; }
    public NervWindow Window { get; private set; }

    internal List<Layer> Layers = new();

    internal NervFont DefaultFont;

    internal List<NervFont> Fonts = new();

    public static Application CreateApplication(ApplicationOptions options)
    {
        Core.Initialize();

        Application app = new Application();
        app.Options = options;

        return app;
    }

    public void PushLayer(Layer layer)
        => Layers.Add(layer);
    
    public void SetMenuBarCallback(Action action)
        => Window.MenuBarCallback = action;
    
    public void SetDockspaceCallback(Action<uint> action)
        => Window.DockSpaceCallback = action;

    public void SetStyleCallback(Action action)
        => Window.StyleCallback = action;

    public void SetDefaultFont(NervFont font)
        => DefaultFont = font;
    public void PopFont()
        => ImGui.PopFont();
    public unsafe void PushFont(string fontName)
    {
        var font = Fonts.Find(c => c.Name == fontName);
        if (font == null)
        {
            Core.Log($"Failed to load font {fontName}.", LogType.ERROR, ConsoleColor.Red);
            return;
        }

        ImGui.PushFont(font.FontData);
    }
    public void AddFont(NervFont font)
    {
        var io = ImGui.GetIO();
        Fonts.Add(font);
        font.FontData = io->Fonts->AddFontFromFileTTF(font.Path, font.Size);
        font.Loaded = true;
    }

    public void Run()
        => Window.Run();

    public void CreateWindow()
    {
        var nativeWindowSettings = new NativeWindowSettings
        {
            Title = Options.Title,
            Size = new Vector2i(Options.Width, Options.Height),
            APIVersion = new Version(Options.OpenGlVersion.Major, Options.OpenGlVersion.Minor),
            API = ContextAPI.OpenGL,
            Profile = ContextProfile.Core
        };

        this.Window = new NervWindow(nativeWindowSettings, Options, this);
    }

    public static void DisableLogs()
        => Core.LogsEnabled = false;

    ~Application()
    {
        Layers.Clear();
        DefaultFont = null;
        Fonts.Clear();
    }
    
    //AOT Does not support reflection so this method is disabled for now maybe will use mapper to do the trick
    /*public void PushLayer<T>()
    {
        //=> Layers.Add(((Layer)Activator.CreateInstance(typeof(T))));
    }*/
}