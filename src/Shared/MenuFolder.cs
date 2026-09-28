namespace Loupedeck.Tiles
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using Birchpad.Core.Menu;

    // One key on a menu page: it either runs something or opens a deeper page.
    public sealed class MenuItem
    {
        public MenuItem(String id, String label)
        {
            this.Id = id;
            this.Label = label;
        }

        public String Id { get; }

        public String Label { get; }

        public BitmapColor Color { get; set; } = TileRenderer.Idle;

        // Custom artwork; when null the key shows its label on Color.
        public Func<PluginImageSize, BitmapImage> Image { get; set; }

        // Runs on a worker thread when pressed.
        public Func<Boolean> Run { get; set; }

        // Opens a deeper page when pressed. With Run set too, Run also runs, once per press, while the page opens.
        public Func<IReadOnlyList<MenuItem>> Open { get; set; }
    }

    // A category key that opens nested pages whose items are listed at run time.
    // Layout on the 3x3 keypad: the top-left key is always Back, the other eight hold items,
    // and a list longer than eight gives its last key to More, which pages through the rest.
    public abstract class MenuFolder : PluginDynamicFolder
    {
        private const String BackName = "nav:back";
        private const String MoreName = "nav:more";
        private const String ItemPrefix = "item:";
        private const String BlankPrefix = "blank:";

        private readonly Object gate = new Object();
        private readonly List<Func<IReadOnlyList<MenuItem>>> stack = new List<Func<IReadOnlyList<MenuItem>>>();
        private IReadOnlyList<MenuItem> items = Array.Empty<MenuItem>();
        private Dictionary<String, MenuItem> byKey = new Dictionary<String, MenuItem>(StringComparer.Ordinal);
        private Int32 page;

        // Bumped on every navigation, so a slow page build for a page the user already left is thrown away.
        private Int32 generation;

        // One refresh at a time; a request during a build runs once more afterwards.
        private Boolean refreshing;
        private Boolean refreshAgain;

        protected MenuFolder(String displayName, String groupName)
        {
            this.DisplayName = displayName;
            this.GroupName = groupName;
        }

        // The first page when the folder opens.
        protected abstract IReadOnlyList<MenuItem> Root();

        // The folder's own key on the home page.
        protected virtual BitmapImage FolderImage(PluginImageSize imageSize) =>
            TileRenderer.Command(this.DisplayName, TileRenderer.Idle, imageSize);

        // The host otherwise adds its own Back key at the top left and shifts every key by one, and that Back
        // leaves the whole folder. EncoderArea moves the host's navigation off the nine keys; our own Back takes
        // the same top-left key and returns one page at a time.
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.EncoderArea;

        public override BitmapImage GetButtonImage(PluginImageSize imageSize) => this.FolderImage(imageSize);

        public override String GetButtonDisplayName(PluginImageSize imageSize) => this.DisplayName;

        // Opening the folder always starts at its first page. Pages are built off the host thread,
        // because some read files, run AppleScript, or ask Agent Hub over HTTP.
        public override Boolean Activate()
        {
            this.Navigate(stack =>
            {
                stack.Clear();
                stack.Add(this.Root);
            });
            return base.Activate();
        }

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType deviceType)
        {
            lock (this.gate)
            {
                var slots = MenuLayout.Arrange(this.items.Count, this.page);
                var names = new String[MenuLayout.KeyCount];
                for (var i = 0; i < slots.Length; i++)
                {
                    names[i] = slots[i].Role switch
                    {
                        KeyRole.Back => this.CreateCommandName(BackName),
                        KeyRole.More => this.CreateCommandName(MoreName),
                        KeyRole.Item => this.CreateCommandName(ItemPrefix + KeyFor(this.items[slots[i].ItemIndex].Id)),
                        _ => this.CreateCommandName(BlankPrefix + i),
                    };
                }

                return names;
            }
        }

        public override void RunCommand(String actionParameter)
        {
            if (actionParameter == BackName)
            {
                Boolean atRoot;
                lock (this.gate)
                {
                    atRoot = this.stack.Count <= 1;
                }

                // Back on the first page leaves the folder for the home page.
                if (atRoot)
                {
                    this.Close();
                    return;
                }

                this.Navigate(stack => stack.RemoveAt(stack.Count - 1));
                return;
            }

            if (actionParameter == MoreName)
            {
                lock (this.gate)
                {
                    this.page = (this.page + 1) % MenuLayout.PageCount(this.items.Count);
                }

                this.Redraw();
                return;
            }

            MenuItem item = null;
            if (actionParameter != null && actionParameter.StartsWith(ItemPrefix, StringComparison.Ordinal))
            {
                lock (this.gate)
                {
                    this.byKey.TryGetValue(actionParameter.Substring(ItemPrefix.Length), out item);
                }
            }

            if (item?.Open != null)
            {
                var open = item.Open;
                this.Navigate(stack => stack.Add(open));
                if (item.Run == null)
                {
                    return;
                }
            }

            if (item?.Run != null)
            {
                var run = item.Run;
                var log = this.Plugin?.Log;
                Task.Run(() =>
                {
                    try
                    {
                        if (!run())
                        {
                            log?.Warning($"{this.DisplayName}: '{item.Id}' failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.Error(ex, $"{this.DisplayName}: '{item.Id}' threw");
                    }

                    this.Refresh();
                });
            }
        }

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == BackName)
            {
                return TileRenderer.Command("Back", TileRenderer.Empty, imageSize);
            }

            if (actionParameter == MoreName)
            {
                return TileRenderer.Command("More ›", TileRenderer.Empty, imageSize);
            }

            var item = this.Lookup(actionParameter);
            if (item == null)
            {
                return TileRenderer.Command(String.Empty, TileRenderer.Empty, imageSize);
            }

            try
            {
                return item.Image?.Invoke(imageSize) ?? TileRenderer.Command(item.Label, item.Color, imageSize);
            }
            catch (Exception ex)
            {
                this.Plugin?.Log?.Error(ex, $"{this.DisplayName}: drawing '{item.Id}' failed");
                return TileRenderer.Command(item.Label, item.Color, imageSize);
            }
        }

        public override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == BackName)
            {
                return "Back";
            }

            if (actionParameter == MoreName)
            {
                return "More";
            }

            return this.Lookup(actionParameter)?.Label ?? String.Empty;
        }

        // Rebuilds the current page from live data and redraws it; safe from any thread and never blocks the caller.
        protected void Refresh()
        {
            Func<IReadOnlyList<MenuItem>> current;
            Int32 generationAtStart;
            lock (this.gate)
            {
                if (this.stack.Count == 0)
                {
                    return;
                }

                if (this.refreshing)
                {
                    this.refreshAgain = true;
                    return;
                }

                this.refreshing = true;
                current = this.stack[this.stack.Count - 1];
                generationAtStart = this.generation;
            }

            Task.Run(() =>
            {
                var built = this.SafeBuild(current);
                Boolean again;
                lock (this.gate)
                {
                    if (generationAtStart == this.generation)
                    {
                        this.SetItems(built);
                    }

                    this.refreshing = false;
                    again = this.refreshAgain;
                    this.refreshAgain = false;
                }

                this.Redraw();
                if (again)
                {
                    this.Refresh();
                }
            });
        }

        // Changes the page stack, shows an empty page at once, and builds the new page in the background.
        private void Navigate(Action<List<Func<IReadOnlyList<MenuItem>>>> change)
        {
            Func<IReadOnlyList<MenuItem>> target;
            Int32 generationNow;
            lock (this.gate)
            {
                change(this.stack);
                this.page = 0;
                this.generation++;
                generationNow = this.generation;
                this.SetItems(Array.Empty<MenuItem>());
                target = this.stack.Count > 0 ? this.stack[this.stack.Count - 1] : null;
            }

            this.Redraw();
            if (target == null)
            {
                return;
            }

            Task.Run(() =>
            {
                var built = this.SafeBuild(target);
                lock (this.gate)
                {
                    if (generationNow != this.generation)
                    {
                        return;
                    }

                    this.SetItems(built);
                }

                this.Redraw();
            });
        }

        private void Redraw()
        {
            this.ButtonActionNamesChanged();
            this.CommandImageChanged(null);
        }

        // Caller holds gate.
        private void SetItems(IReadOnlyList<MenuItem> built)
        {
            this.items = built;
            var map = new Dictionary<String, MenuItem>(StringComparer.Ordinal);
            foreach (var item in built)
            {
                map[KeyFor(item.Id)] = item;
            }

            this.byKey = map;
        }

        private MenuItem Lookup(String actionParameter)
        {
            if (actionParameter == null || !actionParameter.StartsWith(ItemPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            lock (this.gate)
            {
                return this.byKey.TryGetValue(actionParameter.Substring(ItemPrefix.Length), out var item) ? item : null;
            }
        }

        // Item IDs can hold folder paths and user-written names; action names only ever see a short hex hash of them.
        private static String KeyFor(String id)
        {
            var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id ?? String.Empty));
            return Convert.ToHexString(hash, 0, 8);
        }

        private IReadOnlyList<MenuItem> SafeBuild(Func<IReadOnlyList<MenuItem>> build)
        {
            try
            {
                return build() ?? Array.Empty<MenuItem>();
            }
            catch (Exception ex)
            {
                this.Plugin?.Log?.Error(ex, $"{this.DisplayName}: building a page failed");
                return Array.Empty<MenuItem>();
            }
        }
    }
}
