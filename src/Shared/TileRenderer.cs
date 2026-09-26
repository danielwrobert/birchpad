namespace Loupedeck.Tiles
{
    using System;

    using DevDeck.Core.Sessions;

    // Draws every Agent Hub key. Layout is proportional so one design fits each key size.
    public static class TileRenderer
    {
        public static readonly BitmapColor Empty = new BitmapColor(38, 38, 38);
        public static readonly BitmapColor Muted = new BitmapColor(150, 150, 150);
        public static readonly BitmapColor Working = new BitmapColor(46, 107, 230);
        public static readonly BitmapColor Approval = new BitmapColor(245, 166, 35);
        public static readonly BitmapColor Question = new BitmapColor(142, 84, 233);
        public static readonly BitmapColor Done = new BitmapColor(31, 164, 99);
        public static readonly BitmapColor Failed = new BitmapColor(214, 48, 49);
        public static readonly BitmapColor Idle = new BitmapColor(90, 90, 90);

        public static BitmapColor ColorFor(SessionState state) => state switch
        {
            SessionState.Working => Working,
            SessionState.NeedsApproval => Approval,
            SessionState.NeedsInput => Question,
            SessionState.Done => Done,
            SessionState.Failed => Failed,
            _ => Idle,
        };

        public static String LabelFor(SessionState state) => state switch
        {
            SessionState.Working => "Working",
            SessionState.NeedsApproval => "Approve?",
            SessionState.NeedsInput => "Question",
            SessionState.Done => "Done",
            SessionState.Failed => "Failed",
            SessionState.Ended => "Ended",
            _ => "Ready",
        };

        public static String SourceLabel(SessionSource source) => source switch
        {
            SessionSource.ClaudeDesktop => "DESKTOP",
            SessionSource.ClaudeCli => "CLI",
            SessionSource.Codex => "CODEX",
            _ => String.Empty,
        };

        public static String Elapsed(TimeSpan span) =>
            span.TotalSeconds < 60 ? $"{Math.Max(0, (Int32)span.TotalSeconds)}s"
            : span.TotalMinutes < 60 ? $"{(Int32)span.TotalMinutes}m"
            : $"{(Int32)span.TotalHours}h";

        public static BitmapImage Session(SessionSnapshot session, Int32 slot, PluginImageSize imageSize, DateTimeOffset now)
        {
            var size = Pixels(imageSize);
            using (var b = new BitmapBuilder(size, size))
            {
                if (session is null)
                {
                    b.Clear(Empty);
                    b.DrawText($"{slot}", 0, 0, size, size, Muted, size / 4);
                    return b.ToImage();
                }

                b.Clear(ColorFor(session.State));
                var pad = size / 16;
                var small = Math.Max(9, size / 9);
                b.DrawText(SourceLabel(session.Source), pad, pad, size - (2 * pad), small + 2, BitmapColor.White, small);
                b.DrawText(session.DisplayName, pad, small + (2 * pad), size - (2 * pad), size - (3 * small) - (3 * pad), BitmapColor.White, Math.Max(10, size / 7));
                var footer = $"{LabelFor(session.State)} {Elapsed(now - session.StateSince)}";
                b.DrawText(footer, pad, size - small - (2 * pad), size - (2 * pad), small + 2, BitmapColor.White, small);
                return b.ToImage();
            }
        }

        // A three-line tile: small header, name in the middle, small footer.
        public static BitmapImage Info(String header, String name, String footer, BitmapColor background, PluginImageSize imageSize)
        {
            var size = Pixels(imageSize);
            using (var b = new BitmapBuilder(size, size))
            {
                b.Clear(background);
                var pad = size / 16;
                var small = Math.Max(9, size / 9);
                b.DrawText(header ?? String.Empty, pad, pad, size - (2 * pad), small + 2, BitmapColor.White, small);
                b.DrawText(name ?? String.Empty, pad, small + (2 * pad), size - (2 * pad), size - (3 * small) - (3 * pad), BitmapColor.White, Math.Max(10, size / 7));
                b.DrawText(footer ?? String.Empty, pad, size - small - (2 * pad), size - (2 * pad), small + 2, BitmapColor.White, small);
                return b.ToImage();
            }
        }

        public static BitmapImage Command(String label, BitmapColor background, PluginImageSize imageSize, String badge = null)
        {
            var size = Pixels(imageSize);
            using (var b = new BitmapBuilder(size, size))
            {
                b.Clear(background);
                b.DrawText(label, 0, 0, size, size, BitmapColor.White, Math.Max(11, size / 6));
                if (!String.IsNullOrEmpty(badge))
                {
                    var r = size / 7;
                    b.FillCircle(size - r - 4, r + 4, r, BitmapColor.White);
                    b.DrawText(badge, size - (2 * r) - 4, 4, 2 * r, 2 * r, background, Math.Max(9, r));
                }

                return b.ToImage();
            }
        }

        private static Int32 Pixels(PluginImageSize imageSize) => imageSize switch
        {
            PluginImageSize.Width116 => 116,
            PluginImageSize.Width60 => 50,
            _ => 80,
        };
    }
}
