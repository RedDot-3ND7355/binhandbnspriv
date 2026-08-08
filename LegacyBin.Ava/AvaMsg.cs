using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace LegacyBin.Ava
{
    /// <summary>
    /// Minimal MessageBox replacement (Avalonia has no built-in MessageBox).
    /// Buttons: Ok (Info), Yes/No (Ask), Ok only (Error/Warn).
    /// </summary>
    public static class AvaMsg
    {
        public static async Task Show(Window owner, string text, string title, bool error = false)
        {
            await ShowCore(owner, text, title,
                error ? MessageBoxIcon.Error : MessageBoxIcon.Info, new[] { "OK" },
                defaultYesIndex: -1, defaultCancelIndex: 0).ConfigureAwait(true);
        }

        public static Task Warn(Window owner, string text, string title = "Warning")
        {
            return ShowCore(owner, text, title, MessageBoxIcon.Warning, new[] { "OK" },
                defaultYesIndex: -1, defaultCancelIndex: 0);
        }

        public static Task Error(Window owner, string text, string title = "Error")
        {
            return Show(owner, text, title, error: true);
        }

        /// <summary>Returns true on Yes.</summary>
        public static Task<bool> Ask(Window owner, string text, string title = "Confirm")
        {
            return ShowCore(owner, text, title, MessageBoxIcon.Question, new[] { "Yes", "No" },
                defaultYesIndex: 0, defaultCancelIndex: 1);
        }

        private static Task<bool> ShowCore(Window owner, string text, string title,
            MessageBoxIcon icon, string[] buttons, int defaultYesIndex, int defaultCancelIndex)
        {
            var tcs = new TaskCompletionSource<bool>();
            var win = new Window
            {
                Title = title ?? "Message",
                Width = 460,
                MaxWidth = 620,
                CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.Height
            };

            var grid = new Grid { Margin = new Thickness(18), RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };

            var msg = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 550,
                Foreground = Brushes.White
            };
            grid.Children.Add(msg);
            Grid.SetRow(msg, 0);
            Grid.SetColumn(msg, 0);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            foreach (var label in buttons)
            {
                var b = new Button
                {
                    Content = label,
                    MinWidth = 88,
                    Margin = new Thickness(6, 0, 0, 0),
                    Classes = { "dialog" }
                };
                var captured = label;
                b.Click += (s, e) =>
                {
                    int idx = Array.IndexOf(buttons, captured);
                    tcs.TrySetResult(idx == defaultYesIndex);
                    win.Close();
                };
                panel.Children.Add(b);
            }
            grid.Children.Add(panel);
            Grid.SetRow(panel, 1);

            win.Content = grid;
            win.Opened += (s, e) =>
            {
                Button fallback = null;
                if (defaultYesIndex >= 0 && defaultYesIndex < panel.Children.Count)
                {
                    fallback = panel.Children[defaultYesIndex] as Button;
                }
                else if (defaultCancelIndex >= 0 && defaultCancelIndex < panel.Children.Count)
                {
                    fallback = panel.Children[defaultCancelIndex] as Button;
                }
                fallback?.Focus();
            };

            if (owner != null)
            {
                win.ShowDialog(owner);
            }
            else
            {
                win.Show();
            }
            return tcs.Task;
        }

        private enum MessageBoxIcon
        {
            Info,
            Warning,
            Error,
            Question
        }
    }
}
