using System.Numerics;
using System.Reflection.PortableExecutable;
using Foster.Framework;

namespace Game;

public static class GUI
{
    private static Batcher Batch;
    private static SpriteFont font;

    private static object pressedValue;
    private static Vector2[] pressedPosition = new Vector2[5];
    private static Vector2[] releasedPosition = new Vector2[5];
    private static Rect viewport;

    public static bool AnyHovered { private set; get; }
    public static bool AnyTriggered { private set; get; }
    public static bool AnyPressed { private set; get; }
    public static bool AnyActive { private set; get; }
    public static bool AnyInteraction => AnyTriggered | AnyActive | AnyPressed | AnyHovered;

    public enum TextAlignMode
    {
        Left,
        Center,
        Right
    }

    public static void Init(ref SpriteFont inFont)
    {
        Batch = new();
        Batch.Clear();

        font = inFont;
    }

    public static void NewFrame()
    {
        AnyHovered = false;
        AnyTriggered = false;
        AnyPressed = false;
        AnyActive = false;

        if (Input.Mouse.LeftPressed) pressedPosition[(int)MouseButtons.Left] = Input.Mouse.Position;
        if (Input.Mouse.LeftReleased) releasedPosition[(int)MouseButtons.Left] = Input.Mouse.Position;

        if (Input.Mouse.MiddlePressed) pressedPosition[(int)MouseButtons.Middle] = Input.Mouse.Position;
        if (Input.Mouse.MiddleReleased) releasedPosition[(int)MouseButtons.Middle] = Input.Mouse.Position;

        if (Input.Mouse.RightPressed) pressedPosition[(int)MouseButtons.Right] = Input.Mouse.Position;
        if (Input.Mouse.RightReleased) releasedPosition[(int)MouseButtons.Right] = Input.Mouse.Position;

        viewport = new Rect(0.0f, 0.0f, App.WidthInPixels, App.HeightInPixels);

        Batch.Clear();
    }

    public static void Render()
    {
        Batch.Render();
    }

    //
    // Widgets
    //

    public static bool Button(Rect rect, ReadOnlySpan<char> text, bool disabled = false)
    {
        var hovered = rect.Hovered() & !disabled;
        var active = rect.Active() & !disabled;

        RectColored(rect, new Color(0, 0x64));
        RectLine(rect, Color.White);

        if (active) RectColored(rect.Inflate(-2.0f), Color.White);

        Batch.Text(font, text, AlignCenter(rect, GetTextRect(font, text)).Position, active ? Color.Black : Color.White);

        return rect.Triggered() && !disabled;
    }

    private static Dictionary<int, float[]> graphData = [];

    public static void GraphValue(int graphID, Rect rect, float value, float min, float max, int historyLength)
    {
        if (!graphData.ContainsKey(graphID))
        {
            graphData[graphID] = new float[historyLength];
        }

        var history = graphData[graphID];

        for (int i = 0; i < historyLength - 1; i++) history[i] = history[i + 1];
        history[historyLength - 1] = (value - min) / (max - min);

        for (int i = 0; i < historyLength - 1; i++)
        {
            float d = ((float)1.0f) / historyLength * rect.Width;
            float x0 = rect.Left + d * i;
            float x1 = rect.Left + d * (i + 1);

            var v0 = history[i];
            var v1 = history[i + 1];

            var from = new Vector2(x0, rect.Bottom - v0 * rect.Height);
            var to = new Vector2(x1 + 4.0f, rect.Bottom - v1 * rect.Height);

            Batch.Quad(from, from + Vector2.UnitY, to + Vector2.UnitY, to, Color.White);
        }
    }

    public static bool HueWheel(Rect rect, ref float hue)
    {
        float prevHue = hue;
        float v = (Input.Mouse.Position.Clamp(rect).X - rect.Position.X) / rect.Width;

        if (rect.Active())
        {
            hue = Math.Clamp(v, 0.0f, 1.0f);
        }

        var rc = rect.Inflate(-2.0f);

        // Hue gradient, not accurate but w/e doesnt matter
        int steps = (int)rc.Width / 6 + 1;

        for (int i = 0; i < steps; i++)
        {
            float v0 = (float)(i + 0) / steps * 360;
            float v1 = (float)(i + 1) / steps * 360;

            Color cl = ColorFromHSV(v0, 1.0f, 1.0f);
            Color cr = ColorFromHSV(v1, 1.0f, 1.0f);

            float w = (i < steps - 1) ? 6.0f : rc.Width;
            Batch.Rect(rc.CutLeft(w), cl, cr, cr, cl);
        }

        // Border
        RectLine(rect, Color.White);

        // Pointer

        rc = rect.Inflate(-2.0f);
        var pointerX = (int)(rc.Position.X + rc.Width * hue);

        // var currentColor = ColorFromHSV(hue * 360, 0.3f, 1.0f);
        // var pos = new Vector2(pointerX, rc.Position.Y);
        // float oy = 3.0f; // offset y
        //Batch.Triangle(pos + new Vector2(0, +2 + oy), pos + new Vector2(-4, -5 + oy), pos + new Vector2(+5, -5 + oy), Color.Black);
        //Batch.Triangle(pos + new Vector2(0, -0 + oy), pos + new Vector2(-3, -5 + oy), pos + new Vector2(+4, -5 + oy), currentColor);

        Batch.Rect(new Rect(pointerX - 1, rc.Y, 3.0f, rc.Height), Color.Black);
        Batch.Rect(new Rect(pointerX, rc.Y, 1.0f, rc.Height), Color.White);

        return prevHue != hue;
    }

    public static bool RadioInline<T>(Rect rect, ref T en) where T : Enum
    {
        var numItems = Enum.GetValues(typeof(T)).Length;

        var r0 = rect;

        RectColored(rect, new Color(0, 0x64));
        RectLine(rect, Color.White);

        var oldVal = en;

        foreach (int item in Enum.GetValues(typeof(T)))
        {
            var r1 = r0.CutLeft(rect.Width / numItems);

            if (r1.Triggered()) en = (T)Enum.ToObject(typeof(T), item);

            var rt = r1.AlignCenter(GetTextRect(font, Enum.GetName(typeof(T), item))).Position;
            var eq = (int)(object)en == item;

            if (eq)
            {
                RectColored(r1.Inflate(-2), Color.White);
                Batch.Text(font, Enum.GetName(typeof(T), item), rt, Color.Black);
            }
            else
            {
                //if (item != numItems - 1) batch.Rect(new Rect(r1.RightLine.From, r1.RightLine.To + Vector2.UnitX), Color.White);
                Batch.Text(font, Enum.GetName(typeof(T), item), rt, Color.White);
            }
        }

        return (int)(object)oldVal != (int)(object)en;
    }

    private enum BoolEnum
    {
        Disabled, Enabled
    };

    public static bool RadioInline(Rect rect, ref bool value)
    {
        var eval = value ? BoolEnum.Enabled : BoolEnum.Disabled;
        var changed = RadioInline(rect, ref eval);
        value = eval == BoolEnum.Enabled;
        return changed;
    }

    private static void SliderLogic(Rect rect, ref float value, float min, float max, out float prevValue, out float alpha)
    {
        prevValue = value;
        alpha = (value - min) / (max - min);

        if (rect.Active())
        {
            float new_alpha = (Input.Mouse.Position.Clamp(rect).X - rect.Position.X) / rect.Width;
            float v = new_alpha * (max - min) + min;
            value = Math.Clamp(v, min, max);
        }
    }

    public static bool Slider(Rect rect, ReadOnlySpan<char> text, ref float value, float min, float max)
    {
        SliderLogic(rect, ref value, min, max, out var prevValue, out var alpha);

        var rc = rect.Inflate(-2.0f);
        var rt = rc.AlignCenter(GetTextRect(font, text));

        RectColored(rc, new Color(0, 0x64));
        Batch.Text(font, text, rt.Position, Color.White);

        Batch.PushScissor(new RectInt((int)rc.X, (int)rc.Y, (int)(rc.Width * alpha), (int)rc.Height));
        RectColored(rc, Color.White);
        Batch.Text(font, text, rt.Position, Color.Black);
        Batch.PopScissor();

        // Border
        RectLine(rect, Color.White);

        return prevValue != value;
    }

    public static bool Slider(Rect rect, ReadOnlySpan<char> textLeft, ReadOnlySpan<Char> textRight, ref float value, float min, float max)
    {
        SliderLogic(rect, ref value, min, max, out var prevValue, out var alpha);

        var rc = rect.Inflate(-2);

        var rtl = rc.AlignLeft(GetTextRect(font, textLeft), 8);
        var rtr = rc.AlignRight(GetTextRect(font, textRight), 8);

        RectColored(rc, new Color(0, 0x64));
        Batch.Text(font, textLeft, rtl.Position, Color.White);
        Batch.Text(font, textRight, rtr.Position, Color.White);

        Batch.PushScissor(new RectInt((int)rc.X, (int)rc.Y, (int)(rc.Width * alpha), (int)rc.Height));
        RectColored(rc, Color.White);
        Batch.Text(font, textLeft, rtl.Position, Color.Black);
        Batch.Text(font, textRight, rtr.Position, Color.Black);
        Batch.PopScissor();

        // Border
        RectLine(rect, Color.White);

        return prevValue != value;
    }

    private static void SliderLogic(Rect rect, ref int value, int min, int max, out int prevValue, out float alpha)
    {
        prevValue = value;
        alpha = (float)(value - min) / (max - min);

        if (rect.Active())
        {
            float new_alpha = (Input.Mouse.Position.Clamp(rect).X - rect.Position.X) / rect.Width;
            int v = (int)Math.Round(new_alpha * (max - min) + min);
            value = Math.Clamp(v, min, max);
        }
    }

    public static bool Slider(Rect rect, ReadOnlySpan<char> text, ref int value, int min, int max)
    {
        SliderLogic(rect, ref value, min, max, out var prevValue, out var alpha);

        var rc = rect.Inflate(-2.0f);
        var rt = rc.AlignCenter(GetTextRect(font, text));

        RectColored(rc, new Color(0, 0x64));
        Batch.Text(font, text, rt.Position, Color.White);

        Batch.PushScissor(new RectInt((int)rc.X, (int)rc.Y, (int)(rc.Width * alpha), (int)rc.Height));
        RectColored(rc, Color.White);
        Batch.Text(font, text, rt.Position, Color.Black);
        Batch.PopScissor();

        // Border
        RectLine(rect, Color.White);

        return prevValue != value;
    }

    public static bool Slider(Rect rect, ReadOnlySpan<char> textLeft, ReadOnlySpan<char> textRight, ref int value, int min, int max)
    {
        SliderLogic(rect, ref value, min, max, out var prevValue, out var alpha);

        var rc = rect.Inflate(-2.0f);

        var rtl = rc.AlignLeft(GetTextRect(font, textLeft), 8);
        var rtr = rc.AlignRight(GetTextRect(font, textRight), 8);

        RectColored(rc, new Color(0, 0x64));
        Batch.Text(font, textLeft, rtl.Position, Color.White);
        Batch.Text(font, textRight, rtr.Position, Color.White);

        Batch.PushScissor(new RectInt((int)rc.X, (int)rc.Y, (int)(rc.Width * alpha), (int)rc.Height));
        RectColored(rc, Color.White);
        Batch.Text(font, textLeft, rtl.Position, Color.Black);
        Batch.Text(font, textRight, rtr.Position, Color.Black);
        Batch.PopScissor();

        // Border
        RectLine(rect, Color.White);

        return prevValue != value;
    }

    public static bool SliderInfinite(Rect rect, ReadOnlySpan<char> text, ref float value, float speed)
    {
        float prevValue = value;

        if (rect.Pressed()) pressedValue = value;

        if (rect.Active())
        {
            float d = Input.Mouse.Position.X - pressedPosition[(int)MouseButtons.Left].X;
            value = (float)pressedValue + d * (1.0f / rect.Width) * speed * InteractionSpeedModifier();
        }

        var rc = rect.Inflate(-2.0f);
        var rt = rc.AlignCenter(GetTextRect(font, text));

        RectColored(rc, new Color(0, 0x64));
        Batch.Text(font, text, rt.Position, Color.White);

        /* Batch.PushScissor(new RectInt((int)rc.X, (int)rc.Y, (int)(rc.Width * alpha), (int)rc.Height));
        Batch.Rect(rc, Color.White);
        Batch.Text(font, text, rt.Position, Color.Black);
        Batch.PopScissor(); */

        // Border
        RectLine(rect, Color.White);

        return prevValue != value;
    }

    public static bool Selectable(in Rect rect, ReadOnlySpan<char> text, bool isSelected)
    {
        var pressed = rect.Pressed();
        var hovered = rect.Hovered();

        RectColored(rect, new Color(0, 0x64));
        RectLine(rect, (hovered || isSelected) ? Color.White : Color.Black);

        if (isSelected) RectColored(rect.Inflate(-2.0f), Color.White);

        TextRect(rect.Inflate(-4), text, isSelected ? Color.Black : Color.White, false, TextAlignMode.Left);

        return pressed;
    }

    static bool childActive = false;
    static Rect activeChildRect = default;
    static float maxChildHeight = 0.0f;
    static float currentChildScrollY = 0.0f;

    public static Rect BeginChild(in Rect rect)
    {
        Panel(rect);
        var outRect = rect.Inflate(-4);
        currentChildScrollY += rect.Hovered() ? -Input.Mouse.Wheel.Y * 48 : 0.0f;
        //currentChildScrollY = Math.Clamp(currentChildScrollY, 0, Math.Max(maxChildHeight - outRect.Height, 0));
        activeChildRect = outRect;
        childActive = true;
        maxChildHeight = 0.0f;
        Batch.PushScissor(rect.Inflate(-4).ToRectInt());
        return outRect.Translate(0, -currentChildScrollY);
    }

    public static void EndChild()
    {
        Batch.PopScissor();
        childActive = false;
    }

    //
    // Primitives
    //

    public static void Panel(in Rect rect, bool hovered = false, bool active = false)
    {
        RectColored(rect, new Color(0, 0x64));
        RectLine(rect, (hovered || active) ? Color.White : Color.Black);

        if (active) RectColored(rect.Inflate(-2.0f), Color.White);
    }

    public static void TextLine(ref Rect rect, ReadOnlySpan<char> text)
    {
        var r0 = rect.CutTop(18.0f);
        Batch.Text(font, text, r0.Position, Color.White);
    }

    public static void Text(Vector2 position, ReadOnlySpan<char> text, bool shadow = false, bool background = false)
    {
        if (background)
        {
            var textRect = GetTextRect(font, text);
            textRect.Position += position;
            RectColored(textRect.Inflate(4), new Color(0x00, 0xAA));
        }

        if (shadow) Batch.Text(font, text, position + Vector2.One, Color.Black);

        Batch.Text(font, text, position, Color.White);
    }

    public static void TextRect(Rect rect, ReadOnlySpan<char> text, Color color, bool shadow = false, TextAlignMode alignMode = TextAlignMode.Center)
    {
        var r0 = rect;

        switch (alignMode)
        {
            case TextAlignMode.Left:
                r0 = rect.AlignLeft(GetTextRect(font, text));
                break;
            case TextAlignMode.Center:
                r0 = rect.AlignCenter(GetTextRect(font, text));
                break;
            case TextAlignMode.Right:
                r0 = rect.AlignRight(GetTextRect(font, text));
                break;
        }

        r0 = r0.Floor();
        if (shadow) Batch.Text(font, text, r0.Position + Vector2.One, Color.Black);
        Batch.Text(font, text, r0.Position, color);
    }

    public static void TextRect(Rect rect, ReadOnlySpan<char> text, bool shadow = false, TextAlignMode alignMode = TextAlignMode.Center)
    {
        var r0 = rect;

        switch (alignMode)
        {
            case TextAlignMode.Left:
                r0 = rect.AlignLeft(GetTextRect(font, text));
                break;
            case TextAlignMode.Center:
                r0 = rect.AlignCenter(GetTextRect(font, text));
                break;
            case TextAlignMode.Right:
                r0 = rect.AlignRight(GetTextRect(font, text));
                break;
        }

        r0 = r0.Floor();
        if (shadow) Batch.Text(font, text, r0.Position + Vector2.One, Color.Black);
        Batch.Text(font, text, r0.Position, Color.White);
    }

    public static void RectColored(Rect rect, Color color, bool rounding = true)
    {
        if (childActive) maxChildHeight = MathF.Max(maxChildHeight, rect.Bottom - activeChildRect.Top);
        AnyHovered |= rect.Hovered();

        rect = rect.Floor();
        if (rounding) Batch.RectRounded(rect, 2.0f, color);
        else Batch.Rect(rect, color);
    }

    public static void RectLine(Rect rect, Color color)
    {
        if (childActive) maxChildHeight = MathF.Max(maxChildHeight, rect.Bottom - activeChildRect.Top);
        AnyHovered |= rect.Hovered();

        rect = rect.Floor();
        Batch.RectRoundedLine(rect, 2.0f, 1.0f, color);
    }

    public static void RectTexture(Rect rect, Rect clip, Texture texture)
    {
        if (childActive) maxChildHeight = MathF.Max(maxChildHeight, rect.Bottom - activeChildRect.Top);

        AnyHovered |= rect.Hovered();
        Batch.SetTexture(texture);
        clip.Position /= texture.Size;
        clip.Size /= texture.Size;

        Batch.Quad(rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft, clip.TopLeft, clip.TopRight, clip.BottomRight, clip.BottomLeft, Color.White);
    }

    public static void Quad(Quad position, Color color)
    {
        Batch.Quad(position.A, position.B, position.C, position.D, color);
    }

    public static void QuadTexture(Quad position, Quad uv, Texture texture, Color color, bool flipHoriUV = false)
    {
        Batch.SetTexture(texture);
        Batch.Quad(position.A, position.B, position.C, position.D, flipHoriUV ? uv.B : uv.A, flipHoriUV ? uv.A : uv.B, flipHoriUV ? uv.D : uv.C, flipHoriUV ? uv.C : uv.D, color);
    }

    public static void QuadTexture(Quad position, Quad uv, Texture texture, bool flipHoriUV = false)
    {
        QuadTexture(position, uv, texture, Color.White, flipHoriUV);
    }

    public static void Circle(Vector2 position, float radius, Color color)
    {
        position = position.Floor();
        Batch.Circle(position, radius, 8, color);
    }

    public static void CircleLine(Vector2 position, float radius, Color fillColor, Color lineColor)
    {
        position = position.Floor();
        Batch.Circle(position, radius, 6, fillColor);
        Batch.CircleLine(position, radius, 1.7f, 6, lineColor);
    }


    //
    // Rect Interaction
    //

    public static Rect Translate(this Rect rect, float x, float y)
    {
        return new Rect(rect.X + x, rect.Y + y, rect.Width, rect.Height);
    }

    public static bool Active(this Rect rect, MouseButtons buttons = MouseButtons.Left)
    {
        bool v = rect.Contains(pressedPosition[(int)buttons]) && Input.Mouse.Down(buttons);
        AnyActive |= v;
        return v;
    }

    public static bool Triggered(this Rect rect, MouseButtons buttons = MouseButtons.Left)
    {
        bool v = rect.Contains(pressedPosition[(int)buttons]) && rect.Contains(releasedPosition[(int)buttons]) && Input.Mouse.Released(buttons);
        AnyTriggered |= v;
        return v;
    }

    public static bool Pressed(this Rect rect, MouseButtons buttons = MouseButtons.Left)
    {
        bool v = rect.Contains(pressedPosition[(int)buttons]) && Input.Mouse.Pressed(buttons);
        AnyPressed |= v;
        return v;
    }

    public static bool Hovered(this Rect rect)
    {
        bool v = rect.Contains(Input.Mouse.Position);
        AnyHovered |= v;
        return v;
    }

    //
    // Rect Layout
    //

    public static Rect Viewport => viewport;

    public static Rect CutLeft(this ref Rect rect, float w)
    {
        var n = new Rect(rect.X, rect.Y, w, rect.Height);
        rect = new Rect(rect.X + w, rect.Y, rect.Width - w, rect.Height);
        return n;
    }

    public static Rect CutRight(this ref Rect rect, float w)
    {
        var n = new Rect(rect.X + rect.Width - w, rect.Y, w, rect.Height);
        rect = new Rect(rect.X, rect.Y, rect.Width - w, rect.Height);
        return n;
    }

    public static Rect CutTop(this ref Rect rect, float h)
    {
        var n = new Rect(rect.X, rect.Y, rect.Width, h);
        rect = new Rect(rect.X, rect.Y + h, rect.Width, rect.Height - h);
        return n;
    }

    public static Rect CutBottom(this ref Rect rect, float h)
    {
        var n = new Rect(rect.X, rect.Y + rect.Height - h, rect.Width, h);
        rect = new Rect(rect.X, rect.Y, rect.Width, rect.Height - h);
        return n;
    }

    public static Rect GetLeft(this Rect rect, float w)
    {
        return new Rect(rect.X, rect.Y, w, rect.Height);
    }

    public static Rect GetRight(this Rect rect, float w)
    {
        return new Rect(rect.X + rect.Width - w, rect.Y, w, rect.Height);
    }

    public static Rect GetTop(this Rect rect, float h)
    {
        return new Rect(rect.X, rect.Y, rect.Width, h);
    }

    public static Rect GetBottom(this Rect rect, float h)
    {
        return new Rect(rect.X, rect.Y + rect.Height - h, rect.Width, h);
    }

    public static Rect AlignCenter(this Rect container, Rect rect)
    {
        return new Rect(container.TopLeft + (container.Size - rect.Size) / 2, container.BottomRight - (container.Size - rect.Size) / 2);
    }

    public static Rect AlignCenter(this Rect container, float width, float height)
    {
        var size = new Vector2(width, height);
        return new Rect(container.TopLeft + (container.Size - size) / 2, container.BottomRight - (container.Size - size) / 2);
    }

    public static Rect AlignLeft(this Rect container, Rect rect, float padding = 0.0f)
    {
        Vector2 from = container.TopLeft + (Vector2.UnitY * (container.Size.Y - rect.Size.Y)) / 2;
        return new Rect(from + Vector2.UnitX * padding, from + rect.Size + Vector2.UnitX * padding);
    }

    public static Rect AlignRight(this Rect container, Rect rect, float padding = 0.0f)
    {
        Vector2 to = container.BottomRight - (Vector2.UnitY * (container.Size.Y - rect.Size.Y)) / 2;
        return new Rect(to - rect.Size - Vector2.UnitX * padding, to - Vector2.UnitX * padding);
    }

    public static Rect GetTextRect(SpriteFont font, ReadOnlySpan<char> text)
    {
        Vector2 size = font.SizeOf(text);
        return new Rect(0, 0, size.X, size.Y);
    }

    public static Rect Floor(this Rect rect)
    {
        return new Rect(
            MathF.Floor(rect.X),
            MathF.Floor(rect.Y),
            MathF.Floor(rect.Width),
            MathF.Floor(rect.Height)
        );
    }

    public static RectInt ToRectInt(this Rect rect)
    {
        return new RectInt((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
    }

    public static Quad ToQuad(this Rect rect)
    {
        return new Quad(rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft);
    }

    //
    // Utility
    //

    private static float InteractionSpeedModifier()
    {
        return 1.0f * (Input.Keyboard.Down(Keys.LeftShift) ? 4.0f : 1.0f) * (Input.Keyboard.Down(Keys.LeftControl) ? 0.25f : 1.0f);
    }

    public static Color ColorFromHSV(float hue, float saturation, float value)
    {
        int hi = Convert.ToInt32(MathF.Floor(hue / 60)) % 6;
        float f = hue / 60 - MathF.Floor(hue / 60);

        value *= 255;
        int v = Convert.ToInt32(value);
        int p = Convert.ToInt32(value * (1 - saturation));
        int q = Convert.ToInt32(value * (1 - f * saturation));
        int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

        if (hi == 0)
            return new Color((byte)v, (byte)t, (byte)p, 255);
        else if (hi == 1)
            return new Color((byte)q, (byte)v, (byte)p, 255);
        else if (hi == 2)
            return new Color((byte)p, (byte)v, (byte)t, 255);
        else if (hi == 3)
            return new Color((byte)p, (byte)q, (byte)v, 255);
        else if (hi == 4)
            return new Color((byte)t, (byte)p, (byte)v, 255);
        else
            return new Color((byte)v, (byte)p, (byte)q, 255);
    }
}