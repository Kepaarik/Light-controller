using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LedHub.Core;

namespace LedHub.Services.Effects;

/// <summary>
/// Нажатая клавиша: её код (как у <see cref="KeyboardKey.Code"/>), время эффекта, когда её нажали, и порядковый номер нажатия.
/// </summary>
public readonly record struct KeyPress(int Code, double Time, int Index);

/// <summary>
/// Слушает нажатия клавиш во всей системе, пока работает эффект от нажатий. Запоминает только код клавиши
/// и момент нажатия — для вспышки на ней. Создавать нужно в потоке окна: хуку Windows нужен цикл сообщений.
/// </summary>
public sealed class KeyPressTracker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int LLKHF_EXTENDED = 0x01;

    // Нажатие старше этого (во времени эффекта) уже ни на что не влияет
    private const double MaxAge = 8;
    private const int MaxPresses = 64;

    private readonly ConcurrentQueue<int> _pending = new();
    private readonly HashSet<int> _held = new();
    private readonly List<KeyPress> _presses = new();

    // Делегат держим в поле, иначе сборщик мусора уберёт его, пока Windows ещё вызывает хук
    private readonly HookProc _proc;
    private IntPtr _hook;
    private int _count;

    public KeyPressTracker()
    {
        _proc = OnHook;

        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
    }

    /// <summary>
    /// Недавние нажатия, самое свежее последним.
    /// </summary>
    public IReadOnlyList<KeyPress> Presses => _presses;

    /// <summary>
    /// Забирает нажатия, случившиеся после прошлого кадра; time — время эффекта на этом кадре.
    /// </summary>
    public void Update(double time)
    {
        while (_pending.TryDequeue(out var code))
            _presses.Add(new KeyPress(code, time, _count++));

        _presses.RemoveAll(p => time - p.Time > MaxAge);

        if (_presses.Count > MaxPresses)
            _presses.RemoveRange(0, _presses.Count - MaxPresses);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KeyboardHookInfo>(data);
            var key = Normalize((int)info.VirtualKey, (info.Flags & LLKHF_EXTENDED) != 0);

            switch ((int)message)
            {
                // Автоповтор зажатой клавиши — не новое нажатие
                case WM_KEYDOWN or WM_SYSKEYDOWN when _held.Add(key):
                    _pending.Enqueue(key);
                    break;

                case WM_KEYUP or WM_SYSKEYUP:
                    _held.Remove(key);
                    break;
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary>
    /// Приводит код Windows к коду клавиши на клавиатуре: Enter и цифры цифрового блока без Num Lock
    /// приходят с теми же кодами, что основной Enter и стрелки, и отличаются только флагом.
    /// </summary>
    private static int Normalize(int virtualKey, bool isExtended)
    {
        if (virtualKey == 0x0D)
            return isExtended ? KeyboardKey.NumpadEnter : virtualKey;

        if (isExtended)
            return virtualKey;

        return virtualKey switch
        {
            0x2D => 0x60,
            0x23 => 0x61,
            0x28 => 0x62,
            0x22 => 0x63,
            0x25 => 0x64,
            0x0C => 0x65,
            0x27 => 0x66,
            0x24 => 0x67,
            0x26 => 0x68,
            0x21 => 0x69,
            0x2E => 0x6E,
            _ => virtualKey
        };
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookInfo
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
