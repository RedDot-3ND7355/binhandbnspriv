using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LegacyBin.Ava
{
    public partial class MainWindow : Window
    {
        private BinSession _session;

        public MainWindow()
        {
            InitializeComponent();
        }

        private static string FileName(string p) => string.IsNullOrEmpty(p) ? "" : Path.GetFileName(p);

        private void SetBusy(bool busy)
        {
            BtnOpen.IsEnabled = !busy;
            BtnUnpack.IsEnabled = !busy && _session?.IsOpen == true;
            BtnRepack.IsEnabled = !busy && _session?.IsOpen == true;
            BtnTranslate.IsEnabled = !busy && _session?.IsOpen == true;
            Progress.IsIndeterminate = busy;
            Progress.Value = busy ? 0 : 100;
        }

        private void RefreshTableList()
        {
            TableList.ItemsSource = _session.IsOpen
                ? _session.Content.Lists.Select(l =>
                    "[" + l.ID + "] " + _session.GetTableKind(l) + "  size=" + l.Size
                    + "  records=" + _session.GetRecordCount(l)).ToList()
                : new System.Collections.Generic.List<string>();
        }

        private async void OnOpenClicked(object sender, RoutedEventArgs e)
        {
            string path = await AvaDialogs.PickOpenFileAsync(this, "Open bin file", "bin files", new[] { "*.bin" });
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            SetBusy(true);
            try
            {
                _session = await Task.Run(() =>
                {
                    var s = new BinSession();
                    s.Open(path);
                    return s;
                });
                Status.Text = "Open bin: " + FileName(path) + (_session.Is64Bit ? " [64-bit]" : " [32-bit]")
                    + " — " + _session.Content.ListCount + " tables";
                BinEditOptions.Progress = m => Dispatcher.UIThread.Post(() => Status.Text = m);
                RefreshTableList();
            }
            catch (Exception ex)
            {
                await AvaMsg.Error(this, ex.ToString(), "Open failed");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OnUnpackClicked(object sender, RoutedEventArgs e)
        {
            string dir = await AvaDialogs.PickFolderAsync(this, "Choose output folder for XML files");
            if (string.IsNullOrEmpty(dir) || _session == null)
            {
                return;
            }
            SetBusy(true);
            try
            {
                string d = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(_session.FilePath) + ".files");
                await Task.Run(() => _session.ExportXml(d));
                Status.Text = "Unpacked to " + d;
                await AvaMsg.Show(this, "Unpacked XML to:\n" + d, "Unpack");
            }
            catch (Exception ex)
            {
                await AvaMsg.Error(this, ex.ToString(), "Unpack failed");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OnRepackClicked(object sender, RoutedEventArgs e)
        {
            if (_session == null)
            {
                return;
            }
            string dir = await AvaDialogs.PickFolderAsync(this, "Choose folder containing datafile_*.xml");
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            if (!await AvaMsg.Ask(this,
                "Repack from:\n" + dir + "\n\ninto the open bin file:\n" + _session.FilePath
                + "\n\nThe bin will be read from disk, patched with the XML, and overwritten.\nContinue?",
                "Repack"))
            {
                return;
            }
            SetBusy(true);
            try
            {
                string binPath = _session.FilePath;
                await Task.Run(() => RepackService.Repack(binPath, dir, m => Dispatcher.UIThread.Post(() => Status.Text = m)));
                Status.Text = "Repacked into " + FileName(binPath);
                await AvaMsg.Show(this, "Repacked into:\n" + binPath, "Repack");
            }
            catch (Exception ex)
            {
                await AvaMsg.Error(this, ex.ToString(), "Repack failed");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnTranslateClicked(object sender, RoutedEventArgs e)
        {
            if (_session == null)
            {
                return;
            }
            var win = new TranslateWindow(_session);
            win.Show(this);
        }
    }
}
