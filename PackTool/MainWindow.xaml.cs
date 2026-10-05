using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PackTool;

public partial class MainWindow : Window
{
    private static readonly Brush ZoneHotBrush = new SolidColorBrush(Color.FromRgb(0x27, 0xC5, 0xA8));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xA1, 0xB3));
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x27, 0xC5, 0xA8));
    private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x52, 0x52));

    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/icon.ico"));
        WireDropZone(InputZone, InputZoneRect, InputZoneText, InputDir);
        WireDropZone(OutputZone, OutputZoneRect, OutputZoneText, OutputDir);
        GenKey.Click += (_, _) => GenerateWrapKey();
        CopyServer.Click += (_, _) => CopyToClipboard(ServerSnippet);
        CopyClient.Click += (_, _) => CopyToClipboard(ClientSnippet);
        PackBtn.Click += async (_, _) => await PackAsync();
        MinBtn.Click += (_, _) => SendSysCommand(SC_MINIMIZE);
        MaxBtn.Click += (_, _) =>
            SendSysCommand(WindowState == WindowState.Maximized ? SC_RESTORE : SC_MAXIMIZE);
        CloseBtn.Click += (_, _) => Close();
        StateChanged += (_, _) =>
            MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    // ---- window chrome -------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE = 0xF120;

    /// <summary>Sends the caption command through the system path so the
    /// native minimize/maximize/restore animations still play (assigning
    /// WindowState directly bypasses them on borderless windows).</summary>
    private void SendSysCommand(int sc) =>
        PostMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, (IntPtr)sc, IntPtr.Zero);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // dark title bar (harmless now that we draw our own, keeps taskbar/alt-tab consistent)
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2 (Win11; silently ignored on Win10)
        int round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    // ---- directory pickers (click + drag-drop) ----------------------------

    private void WireDropZone(Border zone, System.Windows.Shapes.Rectangle rect,
                              TextBlock hint, TextBox target)
    {
        zone.MouseLeftButtonUp += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = "选择文件夹" };
            if (dlg.ShowDialog(this) == true)
                SetDir(target, dlg.FolderName);
        };
        zone.DragOver += (_, e) =>
        {
            e.Handled = true;
            e.Effects = IsDirDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            rect.Stroke = ZoneHotBrush;
        };
        zone.DragLeave += (_, _) => rect.Stroke = MutedBrush;
        zone.Drop += (_, e) =>
        {
            e.Handled = true;
            rect.Stroke = MutedBrush;
            if (IsDirDrop(e))
                SetDir(target, ((string[])e.Data.GetData(DataFormats.FileDrop)!)[0]);
        };
    }

    private static bool IsDirDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return false;
        return Directory.Exists(paths[0]);
    }

    private void SetDir(TextBox target, string path)
    {
        target.Text = path;
        target.Foreground = (Brush)FindResource("TextBrush");
    }

    // ---- wrap key -----------------------------------------------------------

    private void GenerateWrapKey()
    {
        var key = Rssync3.GenerateWrapKey();
        WrapKey.Text = key;
        KeyHint.Text = "已生成并填入，请同时保存到密码管理器：" + key;
        KeyHint.Visibility = Visibility.Visible;
    }

    private void CopyToClipboard(TextBox box)
    {
        if (box.Text.Length > 0) Clipboard.SetText(box.Text);
    }

    // ---- packing ------------------------------------------------------------

    private sealed record UiEvent(string Kind, string? Text = null,
                                  int Done = 0, int Total = 0,
                                  PackSummary? Summary = null, Snippets? Snippets = null);

    private async Task PackAsync()
    {
        var inputDir = InputDir.Text.Trim();
        var outputDir = OutputDir.Text.Trim();
        var wrapKey = WrapKey.Text.Trim();
        var mcmetaLock = McmetaLock.Text.Trim();
        var mcmetaBind = McmetaBind.Text.Trim();
        if (inputDir.Length == 0 || inputDir == "未选择" ||
            outputDir.Length == 0 || outputDir == "未选择")
        {
            SetStage("请先选择输入和输出目录", MutedBrush);
            return;
        }

        PackBtn.IsEnabled = false;
        SnippetCard.Visibility = Visibility.Collapsed;
        Fill.Value = 0;
        SetStage("启动打包…", MutedBrush);

        IProgress<UiEvent> ui = new Progress<UiEvent>(ApplyUiEvent);
        try
        {
            var outcome = await Task.Run(() => PackService.Pack(
                inputDir, outputDir, wrapKey, mcmetaLock, mcmetaBind,
                text => ui.Report(new UiEvent("stage", text)),
                (done, total, name) => ui.Report(new UiEvent("progress",
                    Text: $"加密中 {done} / {total} — {name}", Done: done, Total: total))));

            if (outcome.Ok && outcome.Summary is { } s && outcome.Snippets is { } sn)
            {
                ui.Report(new UiEvent("done", Summary: s, Snippets: sn,
                    Text: PackService.FormatSummary(s, outcome.Seconds)));
            }
            else
            {
                ui.Report(new UiEvent("error", Text: "失败：" + outcome.Error));
            }
        }
        catch (Exception e)
        {
            ui.Report(new UiEvent("error", Text: "失败：" + e.Message));
        }
        PackBtn.IsEnabled = true;
    }

    private void ApplyUiEvent(UiEvent e)
    {
        switch (e.Kind)
        {
            case "stage":
                SetStage(e.Text!, MutedBrush);
                break;
            case "progress":
                Fill.Value = e.Total > 0 ? e.Done * 100.0 / e.Total : 0;
                SetStage(e.Text!, MutedBrush);
                break;
            case "done":
                Fill.Value = 100;
                SetStage(e.Text!, OkBrush);
                if (e.Snippets is { } sn)
                {
                    ServerSnippet.Text = sn.Server;
                    ClientSnippet.Text = sn.Client;
                    SnippetNote.Text = sn.Note;
                    SnippetCard.Visibility = Visibility.Visible;
                }
                break;
            case "error":
                SetStage(e.Text!, ErrBrush);
                break;
        }
    }

    private void SetStage(string text, Brush brush)
    {
        Stage.Text = text;
        Stage.Foreground = brush;
    }
}
