using Foster.Framework;

namespace Game;

abstract class GameMenu
{
    public abstract void Update(Game game);
    public abstract void OnCreate(Game game);
    public abstract void OnDestroy(Game game);
    public abstract void OnShow(Game game);
    public abstract void OnHide(Game game);
}

class MenuManager
{
    Stack<GameMenu> menuStack;
    bool shouldExit;
    GameMenu? pushTarget;

    public MenuManager()
    {
        this.menuStack = [];
        this.shouldExit = false;
        this.pushTarget = null;
    }

    public bool IsTopLevel => menuStack.Count == 0;

    public void Push<T>() where T : GameMenu, new()
    {
        pushTarget = new T();
    }

    public void Exit()
    {
        shouldExit = true;
    }

    public void Update(Game game)
    {
        if (shouldExit)
        {
            if (menuStack.Count > 0)
            {
                var menu = menuStack.Pop();
                menu.OnHide(game);
                menu.OnDestroy(game);

                if (menuStack.Count > 0) menuStack.First().OnShow(game);
            }

            shouldExit = false;
        }

        if (pushTarget != null)
        {
            if (menuStack.Count > 0) menuStack.First().OnShow(game);
            menuStack.Push(pushTarget);
            menuStack.First().OnCreate(game);
            menuStack.First().OnShow(game);
            pushTarget = null;
        }

        if (menuStack.Count > 0) menuStack.First().Update(game);
    }
}

class MenuPause : GameMenu
{
    public override void OnCreate(Game game) { }

    public override void OnDestroy(Game game) { }

    public override void OnShow(Game game) { }

    public override void OnHide(Game game) { }

    public override void Update(Game game)
    {
        static Rect ButtonLine(ref Rect rect) => rect.CutTop(36).GetLeft(rect.Width).Inflate(-4);

        var r0 = GUI.Viewport.AlignCenter(156, 36 * 3 + 4 * 2);
        GUI.Panel(r0);

        var r1 = r0.Inflate(-4);

        if (GUI.Button(ButtonLine(ref r1), "Settings"))
        {
            game.menuManager.Push<MenuSettings>();
        }

        if (GUI.Button(ButtonLine(ref r1), "Return to main menu"))
        {
            game.level.Exit();
            game.menuManager.Exit();
        }

        if (GUI.Button(ButtonLine(ref r1), "Exit game"))
        {
            App.Exit();
        }
    }
}

class MenuBlocks : GameMenu
{
    public override void OnCreate(Game game) { }

    public override void OnDestroy(Game game) { }

    public override void OnShow(Game game) { }

    public override void OnHide(Game game) { }

    public override void Update(Game game)
    {
        var r0 = GUI.Viewport.AlignCenter(392, 248);
        GUI.Panel(r0);

        var r1 = r0.Inflate(-4);

        var itemDataList = Data.GetItemDataList();

        var ry = new Rect();

        for (int i = 1; i < itemDataList.Length; i++)
        {
            if (itemDataList[i].GetType() != typeof(ItemBlock)) continue;
            var data = (ItemBlock)itemDataList[i];

            if ((i - 1) % 8 == 0) ry = r1.CutTop(48.0f);

            var rx = ry.CutLeft(48).Inflate(-3);
            bool hovered = rx.Hovered();
            bool pressed = rx.Pressed();
            bool active = rx.Active();

            GUI.RectColored(rx, new Color(000, 0x88));
            GUI.RectLine(rx, hovered ? new Color(0xAA_AA_AA, 0x88) : Color.Black);
            if (active) GUI.RectLine(rx, Color.White);

            game.DrawBlock(rx.Center, 10, data.PlaceTarget);

            if (pressed)
            {
                var player = (Player)game.level.PlayerTarget!;
                var hotbar = player.Hotbar;

                hotbar[game.selectedHotbarSlot] = new InventorySlot((Item)i, 64);
            }
        }
    }
}

class MenuLevelCreate : GameMenu
{
    public override void OnCreate(Game game) { }

    public override void OnDestroy(Game game) { }

    public override void OnShow(Game game) { }

    public override void OnHide(Game game) { }

    public override void Update(Game game)
    {
        var r0 = GUI.Viewport.AlignCenter(384, 256);
        GUI.Panel(r0);

        game.level.CreateSave($"World {Random.Shared.Next() % 10000:0000}");
        game.menuManager.Exit();
    }
}

class MenuLevelSelect : GameMenu
{
    (DiskStorage.SaveMetaData data, string path)[] saveList = [];
    int selectedSave;

    public override void OnCreate(Game game)
    {
        selectedSave = -1;
        saveList = [];
    }

    public override void OnDestroy(Game game) { }

    public override void OnShow(Game game)
    {
        saveList = DiskStorage.ScanForSaves();
        Array.Sort(saveList, (a, b) => a.data.CreationDate.CompareTo(b.data.CreationDate));
    }

    public override void OnHide(Game game) { }

    public override void Update(Game game)
    {
        var r0 = GUI.Viewport.AlignCenter(384, 432);
        GUI.Panel(r0);

        var r1 = r0.Inflate(-4);
        var rb = r1.CutBottom(36);

        static Rect EntryLine(ref Rect rect) => rect.CutTop(48).GetLeft(rect.Width).Inflate(-4);

        for (int i = 0; i < saveList.Length; i++)
        {
            (var data, var path) = saveList[i];

            var r2 = EntryLine(ref r1);
            var r3 = r2.Inflate(-4);
            var r4 = r3.GetTop(r3.Height / 2);
            var r5 = r3.GetBottom(r3.Height / 2);

            if (r2.Pressed()) selectedSave = i;

            if (selectedSave == i) GUI.RectLine(r2.Inflate(2), Color.White);
            GUI.RectColored(r2, new Color(0x00, 0x64));
            GUI.RectLine(r2, Color.White);

            GUI.TextRect(r4, data.DisplayName, alignMode: GUI.TextAlignMode.Left);
            GUI.TextRect(r5, $"Created: {data.CreationDate}", alignMode: GUI.TextAlignMode.Left);
        }

        if (GUI.Button(rb.CutLeft(96).Inflate(-4), "Create New"))
        {
            game.menuManager.Push<MenuLevelCreate>();
        }

        if (GUI.Button(rb.CutRight(96).Inflate(-4), "Back"))
        {
            game.menuManager.Exit();
        }

        if (GUI.Button(rb.CutRight(96).Inflate(-4), "Enter", disabled: (selectedSave == -1 || selectedSave >= saveList.Length)))
        {
            game.level.LoadSave(saveList[selectedSave].path);
            game.menuManager.Exit();
        }
    }
}

class MenuSettings : GameMenu
{
    int selectedTab = 0;
    Action? onValueChange;

    public override void OnCreate(Game game) { }

    public override void OnDestroy(Game game)
    {
        Config.WriteToDisk();
    }

    public override void OnShow(Game game) { }

    public override void OnHide(Game game) { }

    Rect EntryLine(ref Rect rect) => rect.CutTop(32).GetLeft(rect.Width).Inflate(-4);

    void EntrySlider(ref Rect rect, ReadOnlySpan<char> textLeft, ReadOnlySpan<char> textValue, ref float value, float min, float max)
    {
        var r1 = EntryLine(ref rect);
        GUI.TextRect(r1.CutLeft(112.0f), textLeft, shadow: true, alignMode: GUI.TextAlignMode.Left);
        GUI.Slider(r1, textValue, ref value, min, max);
    }

    void EntrySlider(ref Rect rect, ReadOnlySpan<char> textLeft, ReadOnlySpan<char> textValue, ref int value, int min, int max)
    {
        var r1 = EntryLine(ref rect);
        GUI.TextRect(r1.CutLeft(112.0f), textLeft, shadow: true, alignMode: GUI.TextAlignMode.Left);
        GUI.Slider(r1, textValue, ref value, min, max);
    }

    void EntryRadioInline(ref Rect rect, ReadOnlySpan<char> textLeft, ref bool value)
    {
        var r1 = EntryLine(ref rect);
        GUI.TextRect(r1.CutLeft(112.0f), textLeft, shadow: true, alignMode: GUI.TextAlignMode.Left);
        if (GUI.RadioInline(r1, ref value)) onValueChange?.Invoke();
    }

    public override void Update(Game game)
    {
        var r0 = GUI.Viewport.AlignCenter(384, 432);
        GUI.Panel(r0);

        var r2 = r0.CutTop(32);
        GUI.Panel(r2);

        if (GUI.Selectable(r2.CutLeft(r0.Width / 4).Inflate(-4), "General", selectedTab == 0)) selectedTab = 0;
        if (GUI.Selectable(r2.CutLeft(r0.Width / 4).Inflate(-4), "Video", selectedTab == 1)) selectedTab = 1;
        if (GUI.Selectable(r2.CutLeft(r0.Width / 4).Inflate(-4), "Audio", selectedTab == 2)) selectedTab = 2;
        if (GUI.Selectable(r2.CutLeft(r0.Width / 4).Inflate(-4), "Input", selectedTab == 3)) selectedTab = 3;

        var r1 = r0.Inflate(-4);
        var rb = r1.CutBottom(36);

        // General
        if (selectedTab == 0)
        {
            EntrySlider(ref r1, "Field of View", $"{Config.CameraFieldOfView:0.00}", ref Config.CameraFieldOfView, 15.0f, 120.0f);
            EntrySlider(ref r1, "View Distance", $"{Config.ViewDistance}", ref Config.ViewDistance, Level.MinimumViewDistance, 16);
            EntrySlider(ref r1, "Max Task Count", $"{Config.TaskCountLimit}", ref Config.TaskCountLimit, 1, Environment.ProcessorCount - 1);
        }

        // Video
        if (selectedTab == 1)
        {
            // TODO: Get ranges from config attributes
            EntryRadioInline(ref r1, "Vertical Sync", ref Config.DoVerticalSync);
            EntrySlider(ref r1, "Framerate Cap", $"{Config.MaxFrameRate}", ref Config.MaxFrameRate, 15, 480);
            EntrySlider(ref r1, "Vignette", $"{Config.VignetteStrength:0.00}", ref Config.VignetteStrength, 0.0f, 1.0f);

            onValueChange = game.ReloadResources;
            EntryRadioInline(ref r1, "Texture AA", ref Config.DoTextureAA);
            EntryRadioInline(ref r1, "Cloud Shadows", ref Config.DoCloudShadows);
            EntryRadioInline(ref r1, "Shadows", ref Config.DoShadowMap);
            onValueChange = null;
        }

        // Audio
        if (selectedTab == 2)
        {
            EntrySlider(ref r1, "Master Volume", $"{Config.AudioMasterVolume:0.00}", ref Config.AudioMasterVolume, 0.0f, 2.0f);
            EntrySlider(ref r1, "Step Volume", $"{Config.AudioStepVolume:0.00}", ref Config.AudioStepVolume, 0.0f, 2.0f);
            EntrySlider(ref r1, "Block Volume", $"{Config.AudioBlockVolume:0.00}", ref Config.AudioBlockVolume, 0.0f, 2.0f);
        }

        // Input
        if (selectedTab == 3)
        {
            EntrySlider(ref r1, "Look Sensitivity", $"{Config.MouseSensitivity:0.00}", ref Config.MouseSensitivity, 0.5f, 5.0f);
        }


        if (GUI.Button(rb.CutRight(96).Inflate(-4), "Cancel"))
        {
            Config.ReadFromDisk();
            game.menuManager.Exit();
        }

        if (GUI.Button(rb.CutRight(96).Inflate(-4), "Okay"))
        {
            game.menuManager.Exit();
        }
    }
}