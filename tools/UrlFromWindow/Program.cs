using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

class Program
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    static int Main(string[] args)
    {
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            if (args.Length > 0 && long.TryParse(args[0], out var parsed))
                hwnd = new IntPtr(parsed);
            if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) { Console.Write(""); return 0; }

            var root = AutomationElement.FromHandle(hwnd);
            if (root == null) { Console.Write(""); return 0; }

            GetWindowThreadProcessId(hwnd, out var pid);
            var procName = "";
            try { procName = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch {}

            var pn = procName.ToLowerInvariant();
            if (!(pn.Contains("chrome") || pn.Contains("msedge") || pn.Contains("firefox")))
            {
                Console.Write(""); return 0;
            }

            var url = FindUrlFromAddressBar(root);
            Console.Write(url ?? "");
            return 0;
        }
        catch { Console.Write(""); return 0; }
    }

    static string? FindUrlFromAddressBar(AutomationElement root)
    {
        var walker = TreeWalker.ControlViewWalker;
        var q = new Queue<(AutomationElement el, int depth)>();
        q.Enqueue((root, 0));
        const int MAX_NODES = 600, MAX_DEPTH = 8;
        int visited = 0;

        while (q.Count > 0 && visited < MAX_NODES)
        {
            var (node, depth) = q.Dequeue();
            visited++;

            if (node.Current.ControlType == ControlType.Edit &&
                node.TryGetCurrentPattern(ValuePattern.Pattern, out var pat) &&
                pat is ValuePattern vp)
            {
                var value = (vp.Current.Value ?? "").Trim();
                if (IsUrlLike(value)) return NormalizeUrl(value);

                var name = (node.Current.Name ?? "").ToLowerInvariant();
                var help = SafeStr(node, AutomationElement.HelpTextProperty).ToLowerInvariant();
                var auto = SafeStr(node, AutomationElement.AutomationIdProperty).ToLowerInvariant();
                if ((name.Contains("address") || name.Contains("omnibox") ||
                     help.Contains("address") || auto.Contains("address")) &&
                    IsUrlLike(value))
                    return NormalizeUrl(value);
            }

            if (depth < MAX_DEPTH)
            {
                var child = walker.GetFirstChild(node);
                while (child != null)
                {
                    q.Enqueue((child, depth + 1));
                    child = walker.GetNextSibling(child);
                }
            }
        }
        return null;
    }

    static string SafeStr(AutomationElement el, AutomationProperty p)
    {
        try { return (el.GetCurrentPropertyValue(p) as string) ?? ""; } catch { return ""; }
    }

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

        // Bare domain heuristic (no spaces)
        if (s.IndexOf(' ') >= 0) return false;
        // At least one dot
        if (s.IndexOf('.') < 0) return false;

        var domainish = new System.Text.RegularExpressions.Regex(
            @"^[a-z0-9\-]+(\.[a-z0-9\-]+)+(:\d+)?(/.*)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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

        // If it looks like a bare domain and has no spaces, prefix https://
        if (s.IndexOf('.') >= 0 && s.IndexOf(' ') < 0)
            return "https://" + s;

        return s;
    }
}


