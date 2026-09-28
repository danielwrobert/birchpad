using System.Threading;

namespace Birchpad.Core.Mac;

/// <summary>
/// macOS automation through <c>open</c> and <c>osascript</c>.
/// Keystrokes and menu clicks need Accessibility permission for the calling process.
/// </summary>
public sealed class MacAutomation
{
    private readonly Action<string>? log;

    public MacAutomation(Action<string>? log = null)
    {
        this.log = log;
    }

    public const string ClaudeBundleId = "com.anthropic.claudefordesktop";

    public const string ItermBundleId = "com.googlecode.iterm2";

    /// <summary>The ChatGPT desktop app, which hosts Codex.</summary>
    public const string CodexBundleId = "com.openai.codex";

    /// <summary>Opens a URL or custom-scheme link such as <c>claude://</c>.</summary>
    public bool OpenUrl(string url) => this.Run("/usr/bin/open", [url], TimeSpan.FromSeconds(5)).ExitCode == 0;

    /// <summary>Brings an app to the front by bundle ID.</summary>
    public bool Activate(string bundleId) => this.Run("/usr/bin/open", ["-b", bundleId], TimeSpan.FromSeconds(5)).ExitCode == 0;

    /// <summary>Runs AppleScript and returns its trimmed output, or null on failure.</summary>
    public string? AppleScript(string script, TimeSpan? timeout = null)
    {
        var result = this.Run("/usr/bin/osascript", ["-e", script], timeout ?? TimeSpan.FromSeconds(10));
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Clicks a menu item by path, for example <c>"Edit", "Clear Buffer"</c> or
    /// <c>"Shell", "Broadcast Input", "Broadcast Input to All Panes in Current Tab"</c>.
    /// Works for items with no shortcut; the app does not need to be in front.
    /// </summary>
    public bool ClickMenu(string bundleId, params string[] path) =>
        path.Length >= 2 && this.AppleScript(MenuScript(bundleId, path)) is not null;

    internal static string MenuScript(string bundleId, string[] path)
    {
        // Build "menu item X of menu 1 of menu item Y of menu Z of menu bar item Z of menu bar 1" from the leaf up.
        var reference = $"menu bar item {Quote(path[0])} of menu bar 1";
        reference = $"menu {Quote(path[0])} of {reference}";
        for (var i = 1; i < path.Length - 1; i++)
        {
            reference = $"menu 1 of menu item {Quote(path[i])} of {reference}";
        }

        reference = $"menu item {Quote(path[^1])} of {reference}";
        return $$"""
            tell application "System Events"
              tell (first process whose bundle identifier is {{Quote(bundleId)}})
                click {{reference}}
              end tell
            end tell
            """;
    }

    /// <summary>
    /// Brings an app forward and pastes <paramref name="text"/> into its focused field, then presses Return
    /// when <paramref name="submit"/> is set. The clipboard's text is put back afterwards.
    /// </summary>
    public bool PasteInto(string bundleId, string text, bool submit) =>
        this.AppleScript(PasteScript(bundleId, text, submit), TimeSpan.FromSeconds(15)) is not null;

    /// <summary>Pastes into whatever app is in front, then presses Return when <paramref name="submit"/> is set.</summary>
    public bool PasteIntoFront(string text, bool submit) =>
        this.AppleScript(PasteFrontScript(text, submit), TimeSpan.FromSeconds(15)) is not null;

    internal static string PasteFrontScript(string text, bool submit) => $$"""
        set previous to ""
        try
          set previous to the clipboard as text
        end try
        set the clipboard to {{Quote(text)}}
        tell application "System Events"
          keystroke "v" using command down
          {{(submit ? "delay 0.2\n      key code 36" : string.Empty)}}
        end tell
        delay 0.2
        set the clipboard to previous
        """;

    internal static string PasteScript(string bundleId, string text, bool submit) => $$"""
        set previous to ""
        try
          set previous to the clipboard as text
        end try
        set the clipboard to {{Quote(text)}}
        tell application id {{Quote(bundleId)}} to activate
        delay 0.3
        tell application "System Events"
          keystroke "v" using command down
          {{(submit ? "delay 0.2\n      key code 36" : string.Empty)}}
        end tell
        delay 0.2
        set the clipboard to previous
        """;

    /// <summary>
    /// Brings an app forward and presses a key with Command held, plus Shift when <paramref name="shift"/> is set.
    /// Used for menu items whose name changes with state, such as "Hide Sidebar" and "Show Sidebar".
    /// </summary>
    public bool CommandKey(string bundleId, char key, bool shift = false) =>
        this.AppleScript(CommandKeyScript(bundleId, key, shift)) is not null;

    internal static string CommandKeyScript(string bundleId, char key, bool shift) => $$"""
        tell application id {{Quote(bundleId)}} to activate
        delay 0.2
        tell application "System Events" to keystroke {{Quote(key.ToString())}} using {{(shift ? "{command down, shift down}" : "command down")}}
        """;

    /// <summary>
    /// Presses the first button in an app's front window whose accessibility name is <paramref name="name"/>,
    /// such as a sidebar entry in Claude Desktop that no link or menu reaches. Returns false when none is found.
    /// </summary>
    public bool PressButton(string bundleId, string name) =>
        this.JavaScript(PressButtonScript, [bundleId, name], TimeSpan.FromSeconds(15)) == "pressed";

    /// <summary>
    /// Opens a page in Claude Desktop's Chat sidebar by pressing its button, switching to the Chat view first
    /// when the sidebar is not showing (in the Code view it holds other buttons).
    /// </summary>
    public bool OpenClaudeSidebarPage(string name)
    {
        this.Activate(ClaudeBundleId);
        if (this.PressButton(ClaudeBundleId, name))
        {
            return true;
        }

        if (!this.OpenUrl("claude://claude.ai/chat"))
        {
            return false;
        }

        Thread.Sleep(1500);
        return this.PressButton(ClaudeBundleId, name);
    }

    // Depth-first search that stops at the first match and skips text and images, so it takes about a second.
    internal const string PressButtonScript = """
        function run(argv) {
          const bundleId = argv[0], target = argv[1];
          const se = Application('System Events');
          const procs = se.processes.whose({ bundleIdentifier: bundleId });
          if (procs.length === 0) return 'missing';
          const windows = procs[0].windows();
          if (windows.length === 0) return 'missing';
          let found = null;
          function walk(el, depth) {
            if (found || depth > 24) return;
            let kids = [];
            try { kids = el.uiElements(); } catch (e) { return; }
            for (let i = 0; i < kids.length && !found; i++) {
              const k = kids[i];
              let role = '';
              try { role = k.role(); } catch (e) {}
              if (role === 'AXButton') {
                let n = '';
                try { n = k.name(); } catch (e) {}
                if (n === target) { found = k; }
                continue;
              }
              if (role === 'AXStaticText' || role === 'AXImage') continue;
              walk(k, depth + 1);
            }
          }
          walk(windows[0], 0);
          if (!found) return 'missing';
          found.actions.byName('AXPress').perform();
          return 'pressed';
        }
        """;

    /// <summary>Runs a JavaScript for Automation script with arguments and returns its trimmed output, or null on failure.</summary>
    public string? JavaScript(string script, IReadOnlyList<string> args, TimeSpan? timeout = null)
    {
        var file = Path.Combine(Path.GetTempPath(), "birchpad-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(script)))[..12] + ".js");
        try
        {
            if (!File.Exists(file))
            {
                File.WriteAllText(file, script);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.log?.Invoke($"cannot write script: {ex.Message}");
            return null;
        }

        var result = this.Run("/usr/bin/osascript", ["-l", "JavaScript", file, .. args], timeout ?? TimeSpan.FromSeconds(10));
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>Opens a Claude Desktop Code session by its Claude Code session ID.</summary>
    public bool OpenClaudeDesktopSession(string sessionId) =>
        this.OpenUrl($"claude://resume?session={Uri.EscapeDataString(sessionId)}");

    /// <summary>Sends Escape to the front app. The caller brings the right app forward first.</summary>
    public bool SendEscape() =>
        this.AppleScript("tell application \"System Events\" to key code 53") is not null;

    /// <summary>
    /// Selects the iTerm session whose working directory is <paramref name="path"/> and brings it forward.
    /// Needs iTerm shell integration for <c>session.path</c>. Returns false when no session matches.
    /// </summary>
    public bool FocusItermSessionAtPath(string path) =>
        this.AppleScript(ItermScript(path, sendEscape: false)) == "found";

    /// <summary>Sends Escape to the iTerm session whose working directory is <paramref name="path"/>.</summary>
    public bool SendEscapeToItermSessionAtPath(string path) =>
        this.AppleScript(ItermScript(path, sendEscape: true)) == "found";

    /// <summary>
    /// Types <paramref name="text"/> into the iTerm session whose working directory is <paramref name="path"/>,
    /// then presses Return when <paramref name="newline"/> is set. iTerm need not be in front.
    /// </summary>
    public bool WriteToItermSessionAtPath(string path, string text, bool newline) =>
        this.AppleScript(ItermScript(path, sendEscape: false, write: (text, newline))) == "found";

    internal static string ItermScript(string path, bool sendEscape, (string Text, bool Newline)? write = null)
    {
        var target = Quote(path.TrimEnd('/'));
        var action = write is { } w
            ? $"tell s to write text {Quote(w.Text)} newline {(w.Newline ? "YES" : "NO")}"
            : sendEscape
            ? "tell s to write text (character id 27) newline NO"
            : "select w\n            tell t to select\n            tell s to select\n            activate";
        return $$"""
            tell application id "{{ItermBundleId}}"
              repeat with w in windows
                repeat with t in tabs of w
                  repeat with s in sessions of t
                    set p to ""
                    try
                      set p to (variable named "session.path") of s
                    end try
                    if p is {{target}} or p is ({{target}} & "/") then
                      {{action}}
                      return "found"
                    end if
                  end repeat
                end repeat
              end repeat
            end tell
            return "missing"
            """;
    }

    /// <summary>Quotes a string as an AppleScript string literal.</summary>
    internal static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private (int ExitCode, string Output) Run(string file, string[] args, TimeSpan timeout)
    {
        try
        {
            var (exitCode, output, error) = ProcessRunner.Run(file, args, timeout);
            if (exitCode != 0)
            {
                this.log?.Invoke($"{Path.GetFileName(file)} exited {exitCode}: {error.Trim()}");
            }

            return (exitCode, output);
        }
        catch (Exception ex)
        {
            this.log?.Invoke($"{Path.GetFileName(file)} failed: {ex.Message}");
            return (-1, string.Empty);
        }
    }
}
