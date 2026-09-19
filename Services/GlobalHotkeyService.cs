using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Novara.Services;








public static class GlobalHotkeyService
{
    private const uint WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    public const int IdQuickCapture = 1;
    public const int IdLockNow = 2;

    private static IntPtr _hwnd;
    private static bool _subclassInstalled;
    private static SUBCLASSPROC? _proc;
    private static Action? _onQuickCapture;
    private static Action? _onLockNow;
    private static readonly List<int> _registered = new();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);


    public static void Initialize(Microsoft.UI.Xaml.Window window, Action onQuickCapture, Action onLockNow)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _onQuickCapture = onQuickCapture;
        _onLockNow = onLockNow;
        if (!_subclassInstalled && _hwnd != IntPtr.Zero)
        {
            _proc = WndProc;
            _subclassInstalled = SetWindowSubclass(_hwnd, _proc, 0x4E4F51, IntPtr.Zero);
        }
    }


    public static bool Register(int id, uint modifiers, uint virtualKey)
    {
        Unregister(id);
        if (_hwnd == IntPtr.Zero) return false;
        if (RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, virtualKey))
        {
            if (!_registered.Contains(id)) _registered.Add(id);
            return true;
        }
        return false;
    }

    public static void Unregister(int id)
    {
        if (_registered.Remove(id) && _hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, id);
    }

    public static void UnregisterAll()
    {


        if (_hwnd == IntPtr.Zero) { _registered.Clear(); return; }
        foreach (var id in _registered.ToArray()) UnregisterHotKey(_hwnd, id);
        _registered.Clear();



        if (_subclassInstalled && _proc != null) _ = RemoveWindowSubclass(_hwnd, _proc, 0x4E4F51);
        _proc = null;
        _onQuickCapture = null;
        _onLockNow = null;
        _subclassInstalled = false;
        _hwnd = IntPtr.Zero;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint id, IntPtr refData)
    {
        if (msg == WM_HOTKEY)
        {
            var hk = wParam.ToInt32();
            if (hk == IdQuickCapture) _onQuickCapture?.Invoke();
            else if (hk == IdLockNow) _onLockNow?.Invoke();
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam, id, refData);
    }


    public static (uint mods, uint vk) ParsePreset(string? presetId) => presetId switch
    {
        "CtrlAltN" => (MOD_CONTROL | MOD_ALT, 0x4E),
        "AltN" => (MOD_ALT, 0x4E),
        "CtrlShiftSpace" => (MOD_CONTROL | MOD_SHIFT, 0x20),
        _ => (MOD_CONTROL | MOD_SHIFT, 0x4E),
    };
}
