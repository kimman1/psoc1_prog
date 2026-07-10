using System.IO.Ports;

namespace PSoC1Programmer;

public partial class MainForm : Form
{
    private readonly ComboBox _cbPort;
    private readonly Button _btnRefresh;
    private readonly Button _btnDetect;
    private readonly Label _lblDeviceValue;
    private readonly TextBox _txtFile;
    private readonly Button _btnBrowse;
    private readonly CheckBox _chkVerify;
    private readonly CheckBox _chkResetAfter;
    private readonly Button _btnFlash;
    private readonly Button _btnErase;
    private readonly Button _btnReadDevice;
    private readonly Button _btnChecksum;
    private readonly Button _btnCancel;
    private readonly RichTextBox _txtConsole;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblStatus;

    private CancellationTokenSource? _cts;
    private Thread? _workerThread;

    public MainForm()
    {
        Text = "PSoC 1 Programmer";
        Size = new Size(900, 700);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        Icon = CreateIcon();

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.FromArgb(30, 30, 30),
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // COM port
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // Device
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // File
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));  // Buttons
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));  // Progress
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));  // Status
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Console
        Controls.Add(mainPanel);

        // Row 0: COM port
        var portPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
        };
        portPanel.Controls.Add(CreateLabel("Cổng COM:", 100));
        _cbPort = new ComboBox
        {
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(220, 220, 220),
        };
        portPanel.Controls.Add(_cbPort);
        _btnRefresh = CreateButton("⟳", 36, (s, e) => RefreshPorts());
        portPanel.Controls.Add(_btnRefresh);
        RefreshPorts();
        mainPanel.Controls.Add(portPanel, 0, 0);

        // Row 1: Device
        var devPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
        };
        devPanel.Controls.Add(CreateLabel("Thiết bị:", 100));
        _lblDeviceValue = new Label
        {
            Text = "Chưa kết nối",
            Width = 220,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(180, 180, 180),
            BackColor = Color.Transparent,
        };
        devPanel.Controls.Add(_lblDeviceValue);
        _btnDetect = CreateButton("Nhận dạng", 120, DetectDevice);
        devPanel.Controls.Add(_btnDetect);
        mainPanel.Controls.Add(devPanel, 0, 1);

        // Row 2: File
        var filePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
        };
        filePanel.Controls.Add(CreateLabel("File .hex:", 100));
        _txtFile = new TextBox
        {
            Width = 450,
            ReadOnly = true,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
        };
        filePanel.Controls.Add(_txtFile);
        _btnBrowse = CreateButton("Chọn file...", 110, BrowseFile);
        filePanel.Controls.Add(_btnBrowse);
        mainPanel.Controls.Add(filePanel, 0, 2);

        // Row 3: Buttons
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
        };
        _chkVerify = new CheckBox
        {
            Text = "Xác minh (đọc lại)",
            ForeColor = Color.FromArgb(220, 220, 220),
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0),
            BackColor = Color.Transparent,
        };
        btnPanel.Controls.Add(_chkVerify);
        _chkResetAfter = new CheckBox
        {
            Text = "Reset sau khi flash",
            ForeColor = Color.FromArgb(220, 220, 220),
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0),
            Checked = true,
            BackColor = Color.Transparent,
        };
        btnPanel.Controls.Add(_chkResetAfter);
        _btnFlash = CreateButton("▶ Flash", 120, StartFlash, isPrimary: true);
        btnPanel.Controls.Add(_btnFlash);
        _btnErase = CreateButton("✕ Xoá", 100, StartErase);
        btnPanel.Controls.Add(_btnErase);
        _btnReadDevice = CreateButton("Đọc Chip", 100, ReadDeviceInfo);
        btnPanel.Controls.Add(_btnReadDevice);
        _btnChecksum = CreateButton("Checksum", 110, ReadChecksum);
        btnPanel.Controls.Add(_btnChecksum);
        _btnCancel = CreateButton("■ Huỷ", 80, CancelOperation);
        _btnCancel.Enabled = false;
        btnPanel.Controls.Add(_btnCancel);
        mainPanel.Controls.Add(btnPanel, 0, 3);

        // Row 4: Progress bar
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 0,
            ForeColor = Color.FromArgb(0, 120, 215),
            BackColor = Color.FromArgb(45, 45, 45),
        };
        mainPanel.Controls.Add(_progressBar, 0, 4);

        // Row 5: Status
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Sẵn sàng",
            ForeColor = Color.FromArgb(180, 180, 180),
            BackColor = Color.Transparent,
        };
        mainPanel.Controls.Add(_lblStatus, 0, 5);

        // Row 6: Console
        _txtConsole = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(15, 15, 15),
            ForeColor = Color.FromArgb(0, 200, 0),
            Font = new Font("Cascadia Code", 10F),
            BorderStyle = BorderStyle.FixedSingle,
            WordWrap = false,
        };
        mainPanel.Controls.Add(_txtConsole, 0, 6);
    }

    // ── Helpers ──

    private Label CreateLabel(string text, int width)
    {
        return new Label
        {
            Text = text,
            Width = width,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(180, 180, 180),
            BackColor = Color.Transparent,
        };
    }

    private Button CreateButton(string text, int width, EventHandler click, bool isPrimary = false)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 1, BorderColor = isPrimary ? Color.FromArgb(0, 90, 170) : Color.FromArgb(70, 70, 70) },
            Cursor = Cursors.Hand,
            BackColor = isPrimary ? Color.FromArgb(0, 103, 192) : Color.FromArgb(55, 55, 55),
            ForeColor = isPrimary ? Color.White : Color.FromArgb(220, 220, 220),
        };
        btn.Click += click;
        return btn;
    }

    private void RefreshPorts()
    {
        var prev = _cbPort.SelectedItem?.ToString();
        _cbPort.Items.Clear();
        foreach (var p in SerialPort.GetPortNames())
            _cbPort.Items.Add(p);
        if (prev is not null && _cbPort.Items.Contains(prev))
            _cbPort.SelectedItem = prev;
        else if (_cbPort.Items.Count > 0)
            _cbPort.SelectedIndex = 0;
    }

    private void BrowseFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Intel HEX files (*.hex)|*.hex|All files (*.*)|*.*",
            Title = "Chọn file firmware .hex",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            _txtFile.Text = dlg.FileName;
    }

    // ── Operations ──

    private void DetectDevice(object? sender, EventArgs e)
    {
        RunOnWorker("Nhận dạng thiết bị", prog =>
        {
            using var p = OpenProgrammer();
            var id = p.GetDeviceId();
            var name = p.GetDeviceName();
            var fw = p.GetFirmwareId();
            Log($"Device ID: 0x{id:X4} ({id})");
            Log($"Device name: {name}");
            Log($"Firmware version: 0x{fw:X4}");
            SetDeviceLabel($"{name} (ID: 0x{id:X4})");
        });
    }

    private void ReadDeviceInfo(object? sender, EventArgs e)
    {
        RunOnWorker("Đọc thông tin chip", prog =>
        {
            using var p = OpenProgrammer();
            var id = p.GetDeviceId();
            var name = p.GetDeviceName();
            var fw = p.GetFirmwareId();
            Log($"Device ID: 0x{id:X4} ({id})");
            Log($"Device: {name}");
            Log($"Firmware: 0x{fw:X4}");
            SetDeviceLabel($"{name} (ID: 0x{id:X4})");
        });
    }

    private void ReadChecksum(object? sender, EventArgs e)
    {
        RunOnWorker("Đọc checksum", prog =>
        {
            using var p = OpenProgrammer();
            var csum = p.ReadChecksum();
            Log($"Checksum (2 byte, BE): {Convert.ToHexString(csum)}");
        });
    }

    private void StartFlash(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtFile.Text))
        {
            Log("⚠ Vui lòng chọn file .hex trước!");
            return;
        }
        if (!File.Exists(_txtFile.Text))
        {
            Log($"⚠ File không tồn tại: {_txtFile.Text}");
            return;
        }

        RunOnWorker("Flash", prog =>
        {
            using var p = OpenProgrammer();
            p.Flash(_txtFile.Text, _chkVerify.Checked, Log);
            if (_chkResetAfter.Checked)
            {
                Log("Đang reset thiết bị...");
                p.ResetDevice();
                Log("Đã reset thiết bị.");
            }
        });
    }

    private void StartErase(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            "Bạn có chắc muốn xoá toàn bộ bộ nhớ chương trình?",
            "Xác nhận xoá",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        RunOnWorker("Xoá", prog =>
        {
            using var p = OpenProgrammer();
            Log("Đang xoá bộ nhớ...");
            p.EraseMemory();
            Log("Đã xoá bộ nhớ.");
        });
    }

    private void CancelOperation(object? sender, EventArgs e)
    {
        _cts?.Cancel();
    }

    // ── Worker infrastructure ──

    private PSoC1Prog OpenProgrammer()
    {
        if (_cbPort.SelectedItem is not string port)
            throw new InvalidOperationException("Vui lòng chọn cổng COM!");
        Log($"Đang kết nối {port}...");
        var p = new PSoC1Prog(port);
        Log($"Đã kết nối {port}.");
        return p;
    }

    private void RunOnWorker(string action, Action<PSoC1Prog?> workerAction)
    {
        if (_workerThread?.IsAlive == true)
        {
            Log("⚠ Đang có tác vụ chạy, vui lòng đợi...");
            return;
        }

        SetControlsEnabled(false);
        _progressBar.MarqueeAnimationSpeed = 30;
        _lblStatus.Text = $"Đang {action.ToLower()}...";
        _cts = new CancellationTokenSource();

        _workerThread = new Thread(() =>
        {
            try
            {
                workerAction(null);
                if (_cts.IsCancellationRequested)
                    Invoke(() => Log("⚠ Đã bị huỷ."));
                else
                    Invoke(() => _lblStatus.Text = "Hoàn thành.");
            }
            catch (Exception ex)
            {
                Invoke(() =>
                {
                    Log($"✕ Lỗi: {ex.Message}");
                    _lblStatus.Text = "Lỗi!";
                });
            }
            finally
            {
                Invoke(() =>
                {
                    SetControlsEnabled(true);
                    _progressBar.MarqueeAnimationSpeed = 0;
                    if (_lblStatus.Text == "Đang...")
                        _lblStatus.Text = "Sẵn sàng";
                });
            }
        })
        { IsBackground = true };
        _workerThread.Start();
    }

    private void SetControlsEnabled(bool enabled)
    {
        _btnFlash.Enabled = enabled;
        _btnErase.Enabled = enabled;
        _btnDetect.Enabled = enabled;
        _btnReadDevice.Enabled = enabled;
        _btnChecksum.Enabled = enabled;
        _btnBrowse.Enabled = enabled;
        _btnRefresh.Enabled = enabled;
        _cbPort.Enabled = enabled;
        _btnCancel.Enabled = !enabled;
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => Log(message));
            return;
        }
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _txtConsole.AppendText($"[{timestamp}] {message}\n");
        _txtConsole.ScrollToCaret();
    }

    private void SetDeviceLabel(string text)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetDeviceLabel(text));
            return;
        }
        _lblDeviceValue.Text = text;
        _lblDeviceValue.ForeColor = Color.FromArgb(0, 200, 0);
    }

    // ── Icon ──

    private static Icon CreateIcon()
    {
        // Create a simple 16x16 icon programmatically
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        // Draw a small chip icon
        using var pen = new Pen(Color.White, 1.5f);
        using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
        // Chip body
        g.FillRectangle(brush, 3, 3, 10, 10);
        g.DrawRectangle(pen, 3, 3, 10, 10);
        // Pins
        g.DrawLine(pen, 1, 5, 3, 5);
        g.DrawLine(pen, 1, 8, 3, 8);
        g.DrawLine(pen, 1, 11, 3, 11);
        g.DrawLine(pen, 13, 5, 15, 5);
        g.DrawLine(pen, 13, 8, 15, 8);
        g.DrawLine(pen, 13, 11, 15, 11);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
