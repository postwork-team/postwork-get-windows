using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

class Program
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    const uint GA_ROOT = 2;

    static bool DEBUG = false;

    static int Main(string[] args)
    {
        try
        {
            int delayMs = 0;
            foreach (var a in args)
            {
                if (a.Equals("--debug", StringComparison.OrdinalIgnoreCase)) DEBUG = true;
                if (a.StartsWith("--delay=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(a.Substring("--delay=".Length), out delayMs);
                }
            }
            if (delayMs > 0) System.Threading.Thread.Sleep(delayMs);

            IntPtr hwnd = ParseHwndFromArgs(args);
            if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
            if (!IsWindow(hwnd)) { D("No valid window handle."); return 0; }

            // ascend to top-level window
            IntPtr topHwnd = GetAncestor(hwnd, GA_ROOT);
            if (topHwnd == IntPtr.Zero) topHwnd = hwnd;

            var root = AutomationElement.FromHandle(topHwnd);
            if (root == null) { D("FromHandle returned null."); return 0; }

            GetWindowThreadProcessId(topHwnd, out var pid);
            var procName = TryGetProcessName(pid)?.ToLowerInvariant() ?? "";
            D($"PID={pid} process='{procName}', hwnd=0x{topHwnd.ToInt64():X}");

            if (procName.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) < 0 &&
                procName.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) < 0 &&
                procName.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) < 0)
            {
                D("Not a supported browser.");
                return 0;
            }

            // 1) ControlView
            var url = FindUrlFromAddressBar(root, useRawView:false);
            if (url == null)
            {
                D("ControlView failed; trying RawView…");
                // 2) RawView fallback
                url = FindUrlFromAddressBar(root, useRawView:true);
            }

            if (url != null) Console.Write(url);
            else D("URL not found.");

            return 0;
        }
        catch (Exception ex) { D("Error: " + ex); return 0; }
    }

    static IntPtr ParseHwndFromArgs(string[] args)
    {
        if (args.Length == 0) return IntPtr.Zero;
        foreach (var a in args)
        {
            // skip flags
            if (a.StartsWith("--")) continue;
            // hex like 0x1234
            if (a.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(a.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var hx))
                    return new IntPtr((long)hx);
            }
            // decimal
            if (long.TryParse(a, out var dec))
                return new IntPtr(dec);
        }
        return IntPtr.Zero;
    }

    static string TryGetProcessName(int pid)
    {
        try { return System.Diagnostics.Process.GetProcessById(pid).ProcessName; }
        catch { return ""; }
    }

    static string FindViewName(bool raw) => raw ? "RawView" : "ControlView";

    static string FindElementLabel(AutomationElement el)
    {
        try { return el.Current.Name ?? ""; } catch { return ""; }
    }

    static string? FindUrlFromAddressBar(AutomationElement root, bool useRawView)
    {
        var walker = useRawView ? TreeWalker.RawViewWalker : TreeWalker.ControlViewWalker;
        var q = new Queue<(AutomationElement el, int depth)>();
        q.Enqueue((root, 0));
        int visited = 0, MAX_NODES = 1200, MAX_DEPTH = 10;

        while (q.Count > 0 && visited < MAX_NODES)
        {
            var (node, depth) = q.Dequeue();
            visited++;

            try
            {
                if (node.Current.ControlType == ControlType.Edit)
                {
                    object patObj;
                    if (node.TryGetCurrentPattern(ValuePattern.Pattern, out patObj) && patObj is ValuePattern vp)
                    {
                        var value = (vp.Current.Value ?? "").Trim();
                        if (DEBUG && value.Length > 0 && value.Length < 1024)
                        {
                            var name = SafeStr(node, AutomationElement.NameProperty);
                            var auto = SafeStr(node, AutomationElement.AutomationIdProperty);
                            var help = SafeStr(node, AutomationElement.HelpTextProperty);
                            D($"[{FindViewName(useRawView)}] Edit candidate name='{name}' autoId='{auto}' help='{help}' value='{Trunc(value, 180)}'");
                        }
                        if (IsUrlLike(value)) return NormalizeUrl(value);
                    }
                }
            }
            catch { /* stale element etc. */ }

            if (depth < MAX_DEPTH)
            {
                AutomationElement child = null;
                try { child = walker.GetFirstChild(node); } catch { }
                while (child != null)
                {
                    q.Enqueue((child, depth + 1));
                    try { child = walker.GetNextSibling(child); } catch { break; }
                }
            }
        }

        D($"[{FindViewName(useRawView)}] visited={visited}, depthLimit={MAX_DEPTH} -> no URL");
        return null;
    }

    static string SafeStr(AutomationElement el, AutomationProperty prop)
    {
        try { return (el.GetCurrentPropertyValue(prop) as string) ?? ""; } catch { return ""; }
    }

    static string Trunc(string s, int max) => (s.Length <= max) ? s : s.Substring(0, max) + "…";

    static bool IsUrlLike(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;

        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (s.IndexOf(' ') >= 0) return false;     // no spaces
        if (s.IndexOf('.') < 0) return false;      // at least one dot

        var domainish = new Regex(@"^[a-z0-9\-]+(\.[a-z0-9\-]+)+(:\d+)?(/.*)?$", RegexOptions.IgnoreCase);
        return domainish.IsMatch(s);
    }

    static string NormalizeUrl(string s)
    {
        if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            return s;

        if (s.IndexOf('.') >= 0 && s.IndexOf(' ') < 0) return "https://" + s;
        return s;
    }

    static void D(string msg) { if (DEBUG) Console.Error.WriteLine(msg); }
}
