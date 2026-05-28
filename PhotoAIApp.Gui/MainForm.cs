using PhotoAIApp.Core;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace PhotoAIApp.Gui;

public sealed class MainForm : Form
{
    private readonly TextBox _selectedFolderTextBox = new() { Text = @"P:\" };
    private readonly TreeView _selectedFoldersTreeView = new()
    {
        Height = 330,
        CheckBoxes = true,
        HideSelection = false,
        ShowLines = true,
        ShowPlusMinus = true,
        ShowRootLines = true
    };

    private readonly ComboBox _profileComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _advancedSettingsButton = new() { Text = "Advanced...", Width = 150, Height = 40 };
    private readonly Label _profileSummaryLabel = new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 2, 8, 4)
    };
    private readonly NumericUpDown _limitNumeric = new() { Minimum = 0, Maximum = 100000, Value = 0, Width = 100 };
    private readonly CheckBox _recursiveCheckBox = new() { Text = "Subfolders", Checked = true, AutoSize = true };
    private readonly CheckBox _forceCheckBox = new() { Text = "Scan Existing", Checked = true, AutoSize = true };
    private readonly CheckBox _overwriteSidecarsCheckBox = new() { Text = "Overwrite XMP", Checked = true, AutoSize = true };
    private readonly CheckBox _modelLogCheckBox = new() { Text = "Model Log", Checked = false, AutoSize = true };
    private readonly CheckBox _dryRunCheckBox = new() { Text = "Dry Run", Checked = true, AutoSize = true };
    private readonly CheckBox _syncImmichCheckBox = new() { Text = "Sync Immich", Checked = false, AutoSize = true };
    private readonly Button _browseSelectedFolderButton = new() { Text = "Browse...", Width = 120, Height = 40 };
    private readonly Button _runButton = new() { Text = "Run Scan", Width = 130, Height = 40 };
    private readonly Button _pauseButton = new() { Text = "Pause", Width = 100, Height = 40, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Stop", Width = 100, Height = 40, Enabled = false };
    private readonly Button _openLogButton = new() { Text = "Open Log", Width = 120, Height = 40, Enabled = false };
    private readonly Panel _inlineFolderPickerHost = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = 0,
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 0, 0, 8)
    };
    private readonly RowStyle _inlineFolderPickerRowStyle = new(SizeType.Absolute, 0);
    private SafeFolderPickerForm? _inlineFolderPicker;
    private readonly ToolTip _toolTip = new();
    private readonly Label _phaseValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _startTimeValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _elapsedValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _remainingValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _etaValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _countsValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _primaryRetryValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly Label _fallbackValueLabel = CreateStatusValueLabel(string.Empty);
    private readonly ProgressBar _progressBar = new()
    {
        Dock = DockStyle.Fill,
        Minimum = 0,
        Maximum = 1000,
        Value = 0,
        Style = ProgressBarStyle.Continuous,
        Height = 28
    };
    private readonly TextBox _logTextBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        Dock = DockStyle.Top,
        Height = 320,
        Font = new Font("Consolas", 10F)
    };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _folderTreeAutoLoadTimer = new() { Interval = 500 };
    private readonly List<string> _recentStatusLines = [];

    private CancellationTokenSource? _cancellationTokenSource;
    private PhotoAiPauseController? _pauseController;
    private PhotoAiRunProgressSnapshot? _lastSnapshot;
    private string? _lastRunLogPath;
    private PhotoAiGuiSettings _guiSettings = new();
    private PhotoAiModelProfile _activeProfile = PhotoAiModelProfile.GetPreset(PhotoAiModelProfileId.HighQuality);
    private bool _updatingFolderCheckState;

    public MainForm()
    {
        Text = $"PhotoAI Immich Sidecar Tool v{GetDisplayVersion()}";
        Icon? appIcon = PhotoAiTheme.TryLoadApplicationIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
        }

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        Width = 1300;
        Height = 1500;
        MinimumSize = new Size(980, 820);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        SizeGripStyle = SizeGripStyle.Show;
        StartPosition = FormStartPosition.CenterScreen;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14)
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        main.Controls.Add(CreatePathPanel(), 0, 0);
        main.Controls.Add(CreateOptionsPanel(), 0, 1);
        main.Controls.Add(CreateButtonPanel(), 0, 2);
        main.Controls.Add(CreateStatusPanel(), 0, 3);
        main.Controls.Add(_logTextBox, 0, 4);

        Controls.Add(main);
        PhotoAiTheme.Apply(this);

        InitializeProfilePicker();
        LoadGuiSettings();
        _profileComboBox.SelectedIndexChanged += (_, _) => ApplySelectedProfileFromCombo();
        ConfigureToolTips();

        _browseSelectedFolderButton.Click += (_, _) => ShowInlineFolderPicker(_selectedFolderTextBox, "Choose folder source");
        _selectedFolderTextBox.TextChanged += (_, _) => ScheduleFolderTreeAutoLoad();
        Shown += (_, _) => AutoLoadSelectableFoldersIfAvailable();
        _selectedFoldersTreeView.BeforeExpand += (_, e) => LoadSubfolderNodes(e.Node);
        _selectedFoldersTreeView.AfterCheck += (_, e) =>
        {
            if (e.Node is not null)
            {
                ApplyCheckedStateToDescendants(e.Node, e.Node.Checked);
            }
        };
        _runButton.Click += async (_, _) => await RunScanAsync();
        _pauseButton.Click += (_, _) => TogglePause();
        _cancelButton.Click += (_, _) => _cancellationTokenSource?.Cancel();
        _openLogButton.Click += (_, _) => OpenLastRunLog();
        _advancedSettingsButton.Click += async (_, _) => await ShowAdvancedSettingsAsync();
        _statusTimer.Tick += (_, _) => RefreshElapsedStatus();
        _folderTreeAutoLoadTimer.Tick += (_, _) =>
        {
            _folderTreeAutoLoadTimer.Stop();
            AutoLoadSelectableFoldersIfAvailable();
        };
    }

    private void ConfigureToolTips()
    {
        _toolTip.AutoPopDelay = 15000;
        _toolTip.InitialDelay = 350;
        _toolTip.ReshowDelay = 100;
        _toolTip.SetToolTip(_selectedFolderTextBox, "Folder source to scan. Defaults to P:\\ and automatically loads as an expandable checkbox tree when the path exists.");
        _toolTip.SetToolTip(_selectedFoldersTreeView, "Folder tree for this run. Selected folders are scanned; unselected folders are excluded. Expand folders to reveal subfolders.");

        _toolTip.SetToolTip(_profileComboBox, "Preset AI model/settings. High Quality uses Qwen 7B full resolution; Balanced uses Qwen 7B at 1440px.");
        _toolTip.SetToolTip(_advancedSettingsButton, "Open primary/fallback Ollama server, model, and Max Image Size settings.");
        _toolTip.SetToolTip(_recursiveCheckBox, "Include images in subfolders. Internal .photoai log folders are always ignored.");
        _toolTip.SetToolTip(_forceCheckBox, "When checked, images with existing sidecars can be considered for re-scan. When unchecked, images with all requested output sidecars already present skip the LLM.");
        _toolTip.SetToolTip(_overwriteSidecarsCheckBox, "When checked, existing .jpg.xmp sidecars and enabled .photoai.json model logs are regenerated. When unchecked, existing requested sidecars are protected.");
        _toolTip.SetToolTip(_modelLogCheckBox, "Keep optional .photoai.json model diagnostics next to each image. Leave unchecked for normal Immich XMP-only output; the run/anomaly log is still kept.");
        _toolTip.SetToolTip(_dryRunCheckBox, "Preview what would be scanned/written without calling Ollama or changing files.");
        _toolTip.SetToolTip(_syncImmichCheckBox, "After a successful live scan, ask Immich to Discover new sidecar metadata and then Sync existing sidecar metadata.");
        _toolTip.SetToolTip(_limitNumeric, "Optional maximum number of images to process. 0 means no limit.");
        _toolTip.SetToolTip(_logTextBox, "Shows the 10 most recent run messages. Use Open log after a live run for the full .photoai anomaly log.");
    }

    private Control CreateStatusPanel()
    {
        var group = new GroupBox
        {
            Text = "Live status",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 4
        };
        for (int i = 0; i < 3; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        AddStatusPair(panel, 0, 0, "Phase", _phaseValueLabel);
        AddStatusPair(panel, 1, 0, "Start", _startTimeValueLabel);
        AddStatusPair(panel, 2, 0, "Elapsed", _elapsedValueLabel);
        AddStatusPair(panel, 0, 1, "Remaining", _remainingValueLabel);
        AddStatusPair(panel, 1, 1, "ETA", _etaValueLabel);
        AddStatusPair(panel, 2, 1, "Files", _countsValueLabel);
        AddStatusPair(panel, 0, 2, "Retry", _primaryRetryValueLabel);
        AddStatusPair(panel, 1, 2, "Fallback", _fallbackValueLabel);

        panel.Controls.Add(_progressBar, 0, 3);
        panel.SetColumnSpan(_progressBar, 3);

        group.Controls.Add(panel);
        return group;
    }

    private static void AddStatusPair(TableLayoutPanel panel, int column, int row, string label, Label valueLabel)
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 4),
            Padding = new Padding(0)
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.Controls.Add(new Label
        {
            Text = label.ToUpperInvariant(),
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoEllipsis = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            Margin = new Padding(0)
        }, 0, 0);
        container.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(container, column, row);
    }

    private static Label CreateStatusValueLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };
    }

    private Control CreatePathPanel()
    {
        var group = new GroupBox
        {
            Text = "Scan target",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        AddPathRow(panel, 0, "Folder source:", _selectedFolderTextBox, _browseSelectedFolderButton);
        panel.RowStyles.Add(_inlineFolderPickerRowStyle);
        panel.Controls.Add(_inlineFolderPickerHost, 1, 1);
        panel.SetColumnSpan(_inlineFolderPickerHost, 2);
        AddSelectedFolderChecklistRow(panel, 2);
        AddPathRow(panel, 3, "AI profile:", _profileComboBox, _advancedSettingsButton);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.Controls.Add(new Label
        {
            Text = "Active settings:",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0, 2, 8, 4)
        }, 0, 4);
        panel.Controls.Add(_profileSummaryLabel, 1, 4);
        panel.SetColumnSpan(_profileSummaryLabel, 2);

        group.Controls.Add(panel);
        return group;
    }

    private static void AddPathRow(TableLayoutPanel panel, int row, string label, Control inputControl, Button? button)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var labelControl = new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0, 2, 8, 4)
        };

        inputControl.Dock = DockStyle.Fill;
        inputControl.Margin = new Padding(0, 2, 8, 4);

        panel.Controls.Add(labelControl, 0, row);
        panel.Controls.Add(inputControl, 1, row);

        if (button is not null)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 2, 0, 4);
            panel.Controls.Add(button, 2, row);
        }
    }

    private void AddSelectedFolderChecklistRow(TableLayoutPanel panel, int row)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
        panel.Controls.Add(new Label
        {
            Text = "Folder tree:",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0, 2, 8, 4)
        }, 0, row);

        _selectedFoldersTreeView.Dock = DockStyle.Fill;
        _selectedFoldersTreeView.Margin = new Padding(0, 2, 8, 8);
        panel.Controls.Add(_selectedFoldersTreeView, 1, row);
        panel.SetColumnSpan(_selectedFoldersTreeView, 2);
    }

    private Control CreateOptionsPanel()
    {
        var group = new GroupBox
        {
            Text = "Options",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 12, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 980));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var row1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        ConfigureOptionCheckBox(_recursiveCheckBox, 230);
        ConfigureOptionCheckBox(_forceCheckBox, 250);
        ConfigureOptionCheckBox(_overwriteSidecarsCheckBox, 290);

        row1.Controls.Add(_recursiveCheckBox);
        row1.Controls.Add(_forceCheckBox);
        row1.Controls.Add(_overwriteSidecarsCheckBox);

        var row2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        ConfigureOptionCheckBox(_dryRunCheckBox, 230);
        ConfigureOptionCheckBox(_syncImmichCheckBox, 250);
        ConfigureOptionCheckBox(_modelLogCheckBox, 230);

        var limitLabel = new Label
        {
            Text = "File Limit",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Width = 140,
            Height = 38,
            Margin = new Padding(10, 2, 4, 0)
        };

        _limitNumeric.Width = 110;
        _limitNumeric.Height = 38;
        _limitNumeric.Margin = new Padding(0, 2, 14, 0);

        row2.Controls.Add(_dryRunCheckBox);
        row2.Controls.Add(_syncImmichCheckBox);
        row2.Controls.Add(_modelLogCheckBox);
        row2.Controls.Add(limitLabel);
        row2.Controls.Add(_limitNumeric);

        panel.Controls.Add(row1, 0, 0);
        panel.SetColumnSpan(row1, 2);
        panel.Controls.Add(row2, 0, 1);
        panel.SetColumnSpan(row2, 2);

        group.Controls.Add(panel);
        return group;
    }

    private static void ConfigureOptionCheckBox(CheckBox checkBox, int width)
    {
        checkBox.AutoSize = false;
        checkBox.Width = width;
        checkBox.Height = 38;
        checkBox.TextAlign = ContentAlignment.MiddleLeft;
        checkBox.Margin = new Padding(0, 2, 14, 0);
    }

    private Control CreateButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 14),
            Margin = new Padding(0, 2, 0, 10)
        };

        _runButton.Margin = new Padding(0, 0, 10, 0);
        _pauseButton.Margin = new Padding(0, 0, 10, 0);
        _cancelButton.Margin = new Padding(0, 0, 10, 0);
        _openLogButton.Margin = new Padding(0, 0, 10, 0);
        StyleActionButton(_runButton, PhotoAiTheme.WarmBlue, PhotoAiTheme.SurfaceRaised);
        StyleActionButton(_pauseButton, PhotoAiTheme.WarmYellow, PhotoAiTheme.Text);
        StyleActionButton(_cancelButton, PhotoAiTheme.WarmRed, PhotoAiTheme.SurfaceRaised);
        StyleActionButton(_openLogButton, PhotoAiTheme.WarmBlue, PhotoAiTheme.SurfaceRaised);
        KeepButtonTextColor(_runButton, PhotoAiTheme.SurfaceRaised);
        KeepButtonTextColor(_cancelButton, PhotoAiTheme.SurfaceRaised);
        KeepButtonTextColor(_openLogButton, PhotoAiTheme.SurfaceRaised);

        panel.Controls.Add(_runButton);
        panel.Controls.Add(_pauseButton);
        panel.Controls.Add(_cancelButton);
        var helpButton = new Button { Text = "?", Width = 44, Height = 40 };
        helpButton.Margin = new Padding(0, 0, 10, 0);
        helpButton.Click += (_, _) => ShowMainHelp();
        _toolTip.SetToolTip(helpButton, "Show quick help for the main scan controls.");
        panel.Controls.Add(_openLogButton);
        panel.Controls.Add(helpButton);

        return panel;
    }

    private void ShowMainHelp()
    {
        MessageBox.Show(this,
            "Folder source: defaults to P:\\. When the path exists, the folder tree loads automatically.\r\n\r\n" +
            "Selected folders: select every folder to include and unselect folders to exclude. Click the expand control on a folder to show subfolders; subfolders can also be selected, unselected, and expanded. If no tree is loaded, the typed folder source is scanned directly. Enable Subfolders to recurse within each selected folder. Internal .photoai folders are always ignored.\r\n\r\n" +
            "Scan Existing: re-scan images with existing requested sidecars.\r\n\r\n" +
            "Overwrite XMP: regenerate existing XMP sidecars and enabled model logs.\r\n\r\n" +
            "Model Log: keep optional .photoai.json diagnostics next to each image. Leave unchecked for normal Immich XMP-only output; the run/anomaly log is still kept.\r\n\r\n" +
            "Dry Run: preview without changing files or calling Ollama.\r\n\r\n" +
            "Sync Immich: after a live scan, trigger Immich Sidecar Metadata Discover and then Sync. This is skipped for dry runs.\r\n\r\n" +
            "The live log shows the 10 most recent messages; Open log shows the full run log after a live scan.",
            "PhotoAIApp help",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void StyleActionButton(Button button, Color backColor, Color foreColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = ControlPaint.Dark(backColor);
        button.FlatAppearance.BorderSize = 1;
        button.Font = new Font(button.Font, FontStyle.Bold);
    }

    private static void KeepButtonTextColor(Button button, Color foreColor)
    {
        button.EnabledChanged += (_, _) => button.ForeColor = foreColor;
    }

    private void ShowInlineFolderPicker(TextBox target, string description, string? fallbackPath = null)
    {
        if (_inlineFolderPickerHost.Visible && ReferenceEquals(_inlineFolderPickerHost.Tag, target))
        {
            HideInlineFolderPicker();
            return;
        }

        string initialPath = FirstExistingDirectory(target.Text, fallbackPath, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        string? pickerRootPath = !string.IsNullOrWhiteSpace(fallbackPath) && Directory.Exists(fallbackPath)
            ? fallbackPath
            : null;

        HideInlineFolderPicker();

        _inlineFolderPicker = new SafeFolderPickerForm(description, initialPath, pickerRootPath)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill,
            MinimumSize = Size.Empty
        };
        _inlineFolderPicker.FolderSelected += (_, selectedPath) =>
        {
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                target.Text = selectedPath;
            }

            HideInlineFolderPicker();
        };
        _inlineFolderPicker.PickerCancelled += (_, _) => HideInlineFolderPicker();

        _inlineFolderPickerHost.Tag = target;
        _inlineFolderPickerHost.Height = 360;
        _inlineFolderPickerRowStyle.Height = 360;
        _inlineFolderPickerHost.Controls.Add(_inlineFolderPicker);
        _inlineFolderPickerHost.Visible = true;
        _inlineFolderPicker.Show();
        _inlineFolderPicker.FocusPicker();
    }

    private void HideInlineFolderPicker()
    {
        if (_inlineFolderPicker is not null)
        {
            _inlineFolderPicker.Close();
            _inlineFolderPicker.Dispose();
            _inlineFolderPicker = null;
        }

        _inlineFolderPickerHost.Controls.Clear();
        _inlineFolderPickerHost.Tag = null;
        _inlineFolderPickerHost.Visible = false;
        _inlineFolderPickerHost.Height = 0;
        _inlineFolderPickerRowStyle.Height = 0;
    }

    private static string FirstExistingDirectory(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
    }

    private sealed class SafeFolderPickerForm : Form
    {
        private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
        private readonly TextBox _pathTextBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
        private readonly Button _okButton = new() { Text = "Select", Width = 110, DialogResult = DialogResult.OK };
        private readonly Button _cancelButton = new() { Text = "Cancel", Width = 110, DialogResult = DialogResult.Cancel };
        private readonly Button _upButton = new() { Text = "Up", Width = 90 };
        private readonly Button _refreshButton = new() { Text = "Refresh", Width = 110 };
        private readonly string? _pickerRootPath;

        public string SelectedPath { get; private set; }
        public event EventHandler<string>? FolderSelected;
        public event EventHandler? PickerCancelled;

        public SafeFolderPickerForm(string description, string initialPath, string? pickerRootPath = null)
        {
            _pickerRootPath = !string.IsNullOrWhiteSpace(pickerRootPath) && Directory.Exists(pickerRootPath)
                ? pickerRootPath
                : null;
            Text = description;
            Width = 940;
            Height = 760;
            MinimumSize = new Size(760, 620);
            MinimizeBox = false;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            SelectedPath = initialPath;
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            var help = new Label
            {
                Text = "Safe folder picker: select a folder only. This picker does not expose shell delete/rename commands.",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 30,
                AutoEllipsis = true,
                Margin = new Padding(0, 0, 0, 8)
            };

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = false,
                Height = 38,
                Margin = new Padding(0, 0, 0, 8)
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            pathPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            _pathTextBox.Margin = new Padding(0, 2, 8, 2);
            _upButton.Dock = DockStyle.Fill;
            _upButton.Margin = new Padding(0, 0, 8, 0);
            _refreshButton.Dock = DockStyle.Fill;
            _refreshButton.Margin = new Padding(0);
            pathPanel.Controls.Add(_pathTextBox, 0, 0);
            pathPanel.Controls.Add(_upButton, 1, 0);
            pathPanel.Controls.Add(_refreshButton, 2, 0);

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 48,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
                Margin = new Padding(0)
            };
            _okButton.Height = 40;
            _cancelButton.Height = 40;
            _okButton.Margin = new Padding(8, 0, 0, 0);
            _cancelButton.Margin = new Padding(8, 0, 0, 0);
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            main.Controls.Add(help, 0, 0);
            main.Controls.Add(pathPanel, 0, 1);
            main.Controls.Add(_tree, 0, 2);
            main.Controls.Add(buttonPanel, 0, 3);
            Controls.Add(main);
            PhotoAiTheme.Apply(this);

            _tree.BeforeExpand += (_, e) => PopulateChildren(e.Node);
            _tree.AfterSelect += (_, e) => SetSelectedPath(NodePath(e.Node));
            _upButton.Click += (_, _) => NavigateUp();
            _refreshButton.Click += (_, _) => RefreshCurrentNode();
            _okButton.Click += (_, _) =>
            {
                SelectedPath = _pathTextBox.Text;
                FolderSelected?.Invoke(this, SelectedPath);
            };
            _cancelButton.Click += (_, _) => PickerCancelled?.Invoke(this, EventArgs.Empty);

            LoadInitialPath(initialPath);
        }

        public void FocusPicker()
        {
            _tree.Focus();
        }

        private void LoadInitialPath(string initialPath)
        {
            _tree.Nodes.Clear();
            string selectedPath = Directory.Exists(initialPath) ? initialPath : FirstExistingDirectory(initialPath);
            string rootPath = _pickerRootPath is not null && PhotoAiScanner.IsPathUnderRoot(selectedPath, _pickerRootPath)
                ? _pickerRootPath
                : selectedPath;

            TreeNode rootNode = CreateNode(rootPath);
            _tree.Nodes.Add(rootNode);
            PopulateChildren(rootNode);
            rootNode.Expand();

            TreeNode nodeToSelect = FindOrCreatePathNode(rootNode, selectedPath) ?? rootNode;
            _tree.SelectedNode = nodeToSelect;
            nodeToSelect.EnsureVisible();
            SetSelectedPath(NodePath(nodeToSelect));
        }

        private TreeNode? FindOrCreatePathNode(TreeNode rootNode, string targetPath)
        {
            string rootPath = NodePath(rootNode).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedTarget = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(rootPath, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return rootNode;
            }

            string relative;
            try
            {
                relative = Path.GetRelativePath(rootPath, normalizedTarget);
            }
            catch
            {
                return null;
            }

            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                return null;
            }

            TreeNode current = rootNode;
            foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                PopulateChildren(current);
                TreeNode? next = current.Nodes.Cast<TreeNode>().FirstOrDefault(node =>
                    string.Equals(Path.GetFileName(NodePath(node)), segment, StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    return current;
                }

                next.Expand();
                current = next;
            }

            return current;
        }

        private void NavigateUp()
        {
            string current = _pathTextBox.Text;
            DirectoryInfo? parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parent is null || !parent.Exists)
            {
                return;
            }

            if (_pickerRootPath is not null && !PhotoAiScanner.IsPathUnderRoot(parent.FullName, _pickerRootPath))
            {
                return;
            }

            LoadInitialPath(parent.FullName);
        }

        private void RefreshCurrentNode()
        {
            if (_tree.SelectedNode is null)
            {
                return;
            }

            _tree.SelectedNode.Nodes.Clear();
            _tree.SelectedNode.Nodes.Add(new TreeNode("Loading..."));
            PopulateChildren(_tree.SelectedNode);
            _tree.SelectedNode.Expand();
        }

        private void SetSelectedPath(string path)
        {
            SelectedPath = path;
            _pathTextBox.Text = path;
            _okButton.Enabled = Directory.Exists(path);
        }

        private static TreeNode CreateNode(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string label = string.IsNullOrWhiteSpace(trimmed) ? path : Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = path;
            }

            var node = new TreeNode(label) { Tag = path };
            if (HasChildDirectories(path))
            {
                node.Nodes.Add(new TreeNode("Loading..."));
            }

            return node;
        }

        private static string NodePath(TreeNode? node)
        {
            return node?.Tag as string ?? string.Empty;
        }

        private static void PopulateChildren(TreeNode? node)
        {
            if (node is null)
            {
                return;
            }

            string path = NodePath(node);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            if (node.Nodes.Count == 1 && node.Nodes[0].Tag is null && node.Nodes[0].Text == "Loading...")
            {
                node.Nodes.Clear();
            }
            else if (node.Nodes.Count > 0)
            {
                return;
            }

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(path).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    node.Nodes.Add(CreateNode(directory));
                }
            }
            catch
            {
                // Ignore folders Windows cannot enumerate; this picker is only for safe selection.
            }
        }

        private static bool HasChildDirectories(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).Any();
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed record ProfileComboItem(PhotoAiModelProfileId ProfileId, string Label)
    {
        public override string ToString() => Label;
    }

    private void InitializeProfilePicker()
    {
        _profileComboBox.Items.Clear();
        foreach (PhotoAiModelProfile preset in PhotoAiModelProfile.Presets)
        {
            _profileComboBox.Items.Add(new ProfileComboItem(preset.ProfileId, preset.DisplayName));
        }
        _profileComboBox.Items.Add(new ProfileComboItem(PhotoAiModelProfileId.Custom, "Custom"));
    }

    private void LoadGuiSettings()
    {
        _guiSettings = LoadGuiSettingsFromDisk();
        _activeProfile = _guiSettings.EffectiveProfile;
        SelectProfileComboItem(_guiSettings.SelectedProfileId);
        UpdateProfileSummary();
    }

    private void ApplySelectedProfileFromCombo()
    {
        if (_profileComboBox.SelectedItem is not ProfileComboItem item)
        {
            return;
        }

        if (item.ProfileId == PhotoAiModelProfileId.Custom)
        {
            _activeProfile = _guiSettings.CustomProfile with { ProfileId = PhotoAiModelProfileId.Custom, DisplayName = "Custom" };
            _guiSettings = _guiSettings.WithSelectedProfile(PhotoAiModelProfileId.Custom, _activeProfile);
        }
        else
        {
            _activeProfile = PhotoAiModelProfile.GetPreset(item.ProfileId);
            _guiSettings = _guiSettings.WithSelectedProfile(item.ProfileId, _activeProfile);
        }

        SaveGuiSettingsToDisk(_guiSettings);
        UpdateProfileSummary();
    }

    private async Task ShowAdvancedSettingsAsync()
    {
        using var dialog = new AdvancedSettingsForm(_activeProfile);
        DialogResult result = dialog.ShowDialog(this);
        if (result != DialogResult.OK)
        {
            return;
        }

        ApplyProfile(dialog.SelectedProfile);
        await Task.CompletedTask;
    }

    private void ApplyProfile(PhotoAiModelProfile profile)
    {
        PhotoAiModelProfileId matchedProfile = PhotoAiModelProfile.MatchPreset(profile);
        _activeProfile = matchedProfile == PhotoAiModelProfileId.Custom
            ? profile with { ProfileId = PhotoAiModelProfileId.Custom, DisplayName = "Custom" }
            : PhotoAiModelProfile.GetPreset(matchedProfile);
        _guiSettings = _guiSettings.WithSelectedProfile(matchedProfile, _activeProfile);
        SelectProfileComboItem(matchedProfile);
        SaveGuiSettingsToDisk(_guiSettings);
        UpdateProfileSummary();
    }

    private void SelectProfileComboItem(PhotoAiModelProfileId profileId)
    {
        for (int i = 0; i < _profileComboBox.Items.Count; i++)
        {
            if (_profileComboBox.Items[i] is ProfileComboItem item && item.ProfileId == profileId)
            {
                _profileComboBox.SelectedIndex = i;
                return;
            }
        }

        _profileComboBox.SelectedIndex = 0;
    }

    private void UpdateProfileSummary()
    {
        _profileSummaryLabel.Text = _activeProfile.Summary;
    }

    private static string GuiSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhotoAIApp",
        "settings.json");

    private static PhotoAiGuiSettings LoadGuiSettingsFromDisk()
    {
        try
        {
            if (!File.Exists(GuiSettingsPath))
            {
                return new PhotoAiGuiSettings();
            }

            string json = File.ReadAllText(GuiSettingsPath);
            return JsonSerializer.Deserialize<PhotoAiGuiSettings>(json) ?? new PhotoAiGuiSettings();
        }
        catch
        {
            return new PhotoAiGuiSettings();
        }
    }

    private static void SaveGuiSettingsToDisk(PhotoAiGuiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GuiSettingsPath)!);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GuiSettingsPath, json);
        }
        catch
        {
            // Settings persistence is best-effort; scan execution should not depend on it.
        }
    }

    private void TogglePause()
    {
        if (_pauseController is null)
        {
            return;
        }

        if (_pauseController.IsPaused)
        {
            _pauseController.Resume();
            _pauseButton.Text = "Pause";
            AppendLog("Resume requested. The scan will continue at the next pause checkpoint.");
        }
        else
        {
            _pauseController.Pause();
            _pauseButton.Text = "Resume";
            AppendLog("Pause requested. Current Ollama call/file write will finish, then scanning will pause before the next image.");
        }
    }

    private void ScheduleFolderTreeAutoLoad()
    {
        _folderTreeAutoLoadTimer.Stop();
        _folderTreeAutoLoadTimer.Start();
    }

    private void AutoLoadSelectableFoldersIfAvailable()
    {
        string parentFolder = _selectedFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(parentFolder) || !Directory.Exists(parentFolder))
        {
            _selectedFoldersTreeView.Nodes.Clear();
            return;
        }

        LoadSelectableFolders(showErrors: false);
    }

    private void LoadSelectableFolders(bool showErrors = true)
    {
        string parentFolder = _selectedFolderTextBox.Text.Trim();

        try
        {
            PhotoAiFolderSelection.NormalizeAndValidateSelectedFolders([parentFolder]);
            _selectedFoldersTreeView.Nodes.Clear();
            TreeNode rootNode = CreateFolderTreeNode(parentFolder, isChecked: true);
            _selectedFoldersTreeView.Nodes.Add(rootNode);
            LoadSubfolderNodes(rootNode);
            rootNode.Expand();
        }
        catch (Exception ex)
        {
            _selectedFoldersTreeView.Nodes.Clear();
            if (showErrors)
            {
                MessageBox.Show(this, ex.Message, "Could not load folder tree", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private TreeNode CreateFolderTreeNode(string folderPath, bool isChecked = false)
    {
        string fullPath = Path.GetFullPath(folderPath);
        string label = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(label))
        {
            label = fullPath;
        }

        var node = new TreeNode(label)
        {
            Tag = fullPath,
            Checked = isChecked
        };

        if (HasChildFolders(fullPath))
        {
            node.Nodes.Add(new TreeNode("Loading...") { Tag = null });
        }

        return node;
    }

    private void LoadSubfolderNodes(TreeNode? node)
    {
        if (node?.Tag is not string folderPath || !Directory.Exists(folderPath))
        {
            return;
        }

        if (node.Nodes.Count == 1 && node.Nodes[0].Tag is null && node.Nodes[0].Text == "Loading...")
        {
            node.Nodes.Clear();
        }
        else if (node.Nodes.Count > 0)
        {
            return;
        }

        try
        {
            foreach (string childFolder in Directory.EnumerateDirectories(folderPath)
                .Where(folder => !IsExcludedSelectableFolder(folder))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                node.Nodes.Add(CreateFolderTreeNode(childFolder, isChecked: node.Checked));
            }
        }
        catch
        {
            // Ignore folders Windows cannot enumerate; they simply cannot be selected from this tree.
        }
    }

    private static bool HasChildFolders(string folderPath)
    {
        try
        {
            return Directory.EnumerateDirectories(folderPath).Any(folder => !IsExcludedSelectableFolder(folder));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExcludedSelectableFolder(string folderPath)
    {
        string normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string[] parts = normalizedPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part =>
            string.Equals(part, ".photoai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "@eaDir", StringComparison.OrdinalIgnoreCase));
    }

    private void SetAllSelectableFoldersChecked(bool isChecked)
    {
        foreach (TreeNode node in _selectedFoldersTreeView.Nodes)
        {
            SetNodeAndLoadedChildrenChecked(node, isChecked);
        }
    }

    private void ApplyCheckedStateToDescendants(TreeNode node, bool isChecked)
    {
        if (_updatingFolderCheckState)
        {
            return;
        }

        try
        {
            _updatingFolderCheckState = true;
            SetNodeAndLoadedChildrenChecked(node, isChecked);
        }
        finally
        {
            _updatingFolderCheckState = false;
        }
    }

    private static void SetNodeAndLoadedChildrenChecked(TreeNode node, bool isChecked)
    {
        node.Checked = isChecked;
        foreach (TreeNode childNode in node.Nodes)
        {
            if (childNode.Tag is string)
            {
                SetNodeAndLoadedChildrenChecked(childNode, isChecked);
            }
        }
    }

    private string[] GetCheckedFolderPaths()
    {
        var folders = new List<string>();
        foreach (TreeNode node in _selectedFoldersTreeView.Nodes)
        {
            AddCheckedFolderPaths(node, folders);
        }
        return folders.ToArray();
    }

    private static void AddCheckedFolderPaths(TreeNode node, List<string> folders)
    {
        if (node.Tag is string folderPath && node.Checked && !IsExcludedSelectableFolder(folderPath))
        {
            folders.Add(folderPath);
        }

        foreach (TreeNode childNode in node.Nodes)
        {
            if (childNode.Tag is string)
            {
                AddCheckedFolderPaths(childNode, folders);
            }
        }
    }

    private string[] GetSelectedFoldersForRun(string selectedFolder)
    {
        IEnumerable<string?> selectedFolders = _selectedFoldersTreeView.Nodes.Count > 0
            ? GetCheckedFolderPaths()
            : [selectedFolder];

        return PhotoAiFolderSelection.NormalizeAndValidateSelectedFolders(selectedFolders);
    }

    private static int CountAggregateCandidateImages(IEnumerable<string> selectedFolders, bool recursive, int? totalLimit)
    {
        int total = 0;
        foreach (string folder in selectedFolders)
        {
            int? remainingLimit = totalLimit is > 0 ? totalLimit.Value - total : null;
            if (remainingLimit is <= 0)
            {
                break;
            }

            total += CountCandidateImages(folder, recursive, remainingLimit);
        }

        return total;
    }

    private static int CountCandidateImages(string folderPath, bool recursive, int? limit)
    {
        int count = 0;
        foreach (string path in PhotoAiScanner.GetSupportedImagePaths(folderPath, recursive))
        {
            _ = path;
            count++;
            if (limit is > 0 && count >= limit.Value)
            {
                break;
            }
        }

        return count;
    }

    private static int CountVisitedFiles(PhotoAiScanSummary summary)
    {
        return summary.DryRun
            ? summary.WouldProcess
            : summary.Completed + summary.Failed;
    }

    private async Task RunScanAsync()
    {
        string selectedFolder = _selectedFolderTextBox.Text.Trim();
        string[] selectedFolders;

        try
        {
            selectedFolders = GetSelectedFoldersForRun(selectedFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PhotoAI folder selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetRunningState(true);
        _logTextBox.Clear();
        _recentStatusLines.Clear();
        _lastSnapshot = null;
        StartLiveStatus();
        _statusTimer.Start();
        _lastRunLogPath = null;
        _openLogButton.Enabled = false;
        _cancellationTokenSource = new CancellationTokenSource();
        _pauseController = new PhotoAiPauseController();

        var progress = new Progress<PhotoAiScanProgress>(item =>
        {
            CaptureRunLogPath(item);
            if (!string.Equals(item.EventName, "PROCESS", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog(item.Message);
            }
            if (item.Snapshot is not null)
            {
                ApplyProgressSnapshot(item.Snapshot);
            }
        });
        var scanner = new PhotoAiScanner();

        try
        {
            int? totalLimit = _limitNumeric.Value > 0 ? (int)_limitNumeric.Value : null;
            DateTimeOffset aggregateStartTime = DateTimeOffset.Now;
            int aggregateTotalFiles = CountAggregateCandidateImages(selectedFolders, _recursiveCheckBox.Checked, null);
            int aggregateVisitedFiles = 0;
            int aggregateCompletedOffset = 0;
            int aggregateSkippedOffset = 0;
            int aggregateFailedOffset = 0;
            int aggregatePrimaryRetryAttemptsOffset = 0;
            int aggregateFallbackAttemptsOffset = 0;

            if (selectedFolders.Length > 1)
            {
                AppendLog($"Queued {aggregateTotalFiles} image files across {selectedFolders.Length} selected folders.");
            }

            var summaries = new List<PhotoAiScanSummary>();
            for (int index = 0; index < selectedFolders.Length; index++)
            {
                if (totalLimit is > 0 && aggregateVisitedFiles >= totalLimit.Value)
                {
                    break;
                }

                string folder = selectedFolders[index];
                int? folderLimit = totalLimit is > 0 ? totalLimit.Value - aggregateVisitedFiles : null;
                bool unloadModelsAfterFolder = index == selectedFolders.Length - 1 || totalLimit is > 0;
                AppendLog(selectedFolders.Length == 1
                    ? $"Starting scan: {folder}"
                    : $"Starting folder {index + 1}/{selectedFolders.Length}: {folder}");

                PhotoAiScanSummary folderSummary = await scanner.ScanFolderAsync(
                    new PhotoAiScanOptions
                    {
                        FolderPath = folder,
                        SafetyRootPath = null,
                        Recursive = _recursiveCheckBox.Checked,
                        Force = _forceCheckBox.Checked,
                        WriteJson = _modelLogCheckBox.Checked,
                        WriteXmp = true,
                        OverwriteJson = _overwriteSidecarsCheckBox.Checked,
                        OverwriteXmp = _overwriteSidecarsCheckBox.Checked,
                        AddTags = true,
                        DryRun = _dryRunCheckBox.Checked,
                        OllamaBaseUrl = _activeProfile.OllamaBaseUrl,
                        Model = _activeProfile.Model,
                        ModelPreference = _activeProfile.ModelPreference,
                        FallbackOllamaBaseUrl = _activeProfile.FallbackOllamaBaseUrl,
                        FallbackModel = _activeProfile.FallbackModel,
                        MaxImageDimensionPixels = _activeProfile.MaxImageDimensionPixels,
                        FallbackMaxImageDimensionPixels = _activeProfile.FallbackMaxImageDimensionPixels,
                        PauseController = _pauseController,
                        Limit = folderLimit,
                        UnloadModelsAtEnd = unloadModelsAfterFolder,
                        ProgressCompletedOffset = aggregateCompletedOffset,
                        ProgressSkippedOffset = aggregateSkippedOffset,
                        ProgressFailedOffset = aggregateFailedOffset,
                        ProgressPrimaryRetryAttemptsOffset = aggregatePrimaryRetryAttemptsOffset,
                        ProgressFallbackAttemptsOffset = aggregateFallbackAttemptsOffset,
                        ProgressTotalFiles = selectedFolders.Length > 1 ? aggregateTotalFiles : null,
                        ProgressStartedAt = selectedFolders.Length > 1 ? aggregateStartTime : null
                    },
                    progress,
                    _cancellationTokenSource.Token);
                summaries.Add(folderSummary);
                int folderVisitedFiles = CountVisitedFiles(folderSummary);
                aggregateVisitedFiles += folderVisitedFiles;
                aggregateCompletedOffset += folderSummary.DryRun ? folderSummary.WouldProcess : folderSummary.Completed;
                aggregateSkippedOffset += folderSummary.Skipped;
                aggregateFailedOffset += folderSummary.Failed;
                aggregatePrimaryRetryAttemptsOffset += folderSummary.PrimaryRetryAttempts;
                aggregateFallbackAttemptsOffset += folderSummary.FallbackAttempts;
            }

            PhotoAiScanSummary summary = summaries.Count == 1
                ? summaries[0]
                : PhotoAiScanSummary.Combine(summaries);

            if (summaries.Count > 1 && !summary.DryRun)
            {
                await AppendAggregateRunCompleteAsync(summary, summaries, _cancellationTokenSource.Token);
            }

            _lastRunLogPath = summaries.LastOrDefault(summary => !summary.DryRun && File.Exists(summary.RunLogPath))?.RunLogPath;
            _openLogButton.Enabled = !summary.DryRun && File.Exists(_lastRunLogPath);

            PhotoAiRunSummaryDocument summaryDocument = PhotoAiRunSummaryDocument.FromSummary(summary);
            if (_syncImmichCheckBox.Checked)
            {
                if (summary.DryRun)
                {
                    AppendLog("Sync Immich skipped for dry run.");
                }
                else
                {
                    try
                    {
                        await SyncImmichSidecarMetadataAsync(_cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Sync Immich failed:");
                        AppendLog(ex.Message);
                        await AppendPermanentRunLogLineAsync($"IMMICH SYNC failed: {ex.Message}", CancellationToken.None);
                        MessageBox.Show(this, ex.Message, "Immich sync error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }

            AppendLog("Scan complete. Summary opened in a separate window.");
            _statusTimer.Stop();
            SetRunningState(false);
            ClearLiveStatus();
            ShowRunSummary(summaryDocument, summary.RunLogPath, summary.DryRun);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled by user.");
            EnableLastRunLogIfAvailable();
        }
        catch (Exception ex)
        {
            AppendLog("Fatal error:");
            AppendLog(ex.Message);
            MessageBox.Show(this, ex.Message, "PhotoAI error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _statusTimer.Stop();
            _pauseController?.Resume();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            _pauseController = null;
            SetRunningState(false);
            ClearLiveStatus();
        }
    }

    private async Task SyncImmichSidecarMetadataAsync(CancellationToken cancellationToken)
    {
        ImmichSyncConfig config = LoadImmichSyncConfig();
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            AppendLog("Sync Immich skipped: missing Immich API key. Set PHOTOAI_IMMICH_API_KEY or place immich-sync.json next to the app.");
            await AppendPermanentRunLogLineAsync("IMMICH SYNC skipped: missing Immich API key", cancellationToken);
            return;
        }

        using HttpClient http = CreateImmichHttpClient(config);

        AppendLog("Sync Immich: checking sidecar job...");
        await WaitForImmichSidecarQueueIdleAsync(http, "before discover", cancellationToken);

        AppendLog("Sync Immich: Discover sidecar metadata...");
        await AppendPermanentRunLogLineAsync($"IMMICH SYNC discover requested: endpoint={config.BaseUrl}/api/jobs/sidecar force=false", cancellationToken);
        await RunImmichSidecarQueueCommandAsync(http, force: false, cancellationToken);
        await AppendPermanentRunLogLineAsync("IMMICH SYNC discover accepted", cancellationToken);

        AppendLog("Sync Immich: waiting for Discover to finish...");
        await WaitForImmichSidecarQueueIdleAsync(http, "after discover", cancellationToken);

        AppendLog("Sync Immich: Sync sidecar metadata...");
        await AppendPermanentRunLogLineAsync($"IMMICH SYNC sync requested: endpoint={config.BaseUrl}/api/jobs/sidecar force=true", cancellationToken);
        await RunImmichSidecarQueueCommandAsync(http, force: true, cancellationToken);
        await AppendPermanentRunLogLineAsync("IMMICH SYNC sync accepted", cancellationToken);

        AppendLog("Sync Immich requests sent.");
        await AppendPermanentRunLogLineAsync("IMMICH SYNC requests sent", cancellationToken);
    }

    private async Task AppendPermanentRunLogLineAsync(string message, CancellationToken cancellationToken)
    {
        string? runLogPath = _lastRunLogPath;
        if (string.IsNullOrWhiteSpace(runLogPath) || !File.Exists(runLogPath))
        {
            return;
        }

        string line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        await File.AppendAllTextAsync(runLogPath, line + Environment.NewLine, cancellationToken);
    }

    private static ImmichSyncConfig LoadImmichSyncConfig()
    {
        var config = new ImmichSyncConfig
        {
            BaseUrl = Environment.GetEnvironmentVariable("PHOTOAI_IMMICH_BASE_URL")
                ?? Environment.GetEnvironmentVariable("IMMICH_BASE_URL")
                ?? "http://192.168.1.8:2283",
            ApiKey = Environment.GetEnvironmentVariable("PHOTOAI_IMMICH_API_KEY")
                ?? Environment.GetEnvironmentVariable("IMMICH_API_KEY")
        };

        string configPath = Path.Combine(AppContext.BaseDirectory, "immich-sync.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                ImmichSyncConfig? fileConfig = JsonSerializer.Deserialize<ImmichSyncConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (!string.IsNullOrWhiteSpace(fileConfig?.BaseUrl))
                {
                    config.BaseUrl = fileConfig.BaseUrl;
                }

                if (!string.IsNullOrWhiteSpace(fileConfig?.ApiKey))
                {
                    config.ApiKey = fileConfig.ApiKey;
                }
            }
            catch
            {
                // Fall back to environment/defaults; the caller will report a missing key if none is available.
            }
        }

        config.BaseUrl = (config.BaseUrl ?? "http://192.168.1.8:2283").Trim().TrimEnd('/');
        config.ApiKey = config.ApiKey?.Trim();
        return config;
    }

    private static HttpClient CreateImmichHttpClient(ImmichSyncConfig config)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl!),
            Timeout = TimeSpan.FromSeconds(30)
        };
        http.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
        return http;
    }

    private async Task RunImmichSidecarQueueCommandAsync(HttpClient http, bool force, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var payload = JsonContent.Create(new
            {
                command = "start",
                force
            });

            using HttpResponseMessage response = await http.PutAsync("/api/jobs/sidecar", payload, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if ((int)response.StatusCode == 400 && responseBody.Contains("Job is already running", StringComparison.OrdinalIgnoreCase))
            {
                string operation = force ? "sync" : "discover";
                AppendLog($"Sync Immich: sidecar job already running; waiting before retrying {operation}...");
                await AppendPermanentRunLogLineAsync($"IMMICH SYNC {operation} delayed: sidecar job already running", cancellationToken);
                await WaitForImmichSidecarQueueIdleAsync(http, $"retry {operation}", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Immich sidecar {(force ? "sync" : "discover")} request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
        }

        throw new InvalidOperationException($"Immich sidecar {(force ? "sync" : "discover")} request failed: sidecar job was still running after waiting and retrying.");
    }

    private async Task WaitForImmichSidecarQueueIdleAsync(HttpClient http, string context, CancellationToken cancellationToken)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(10);
        TimeSpan delay = TimeSpan.FromSeconds(5);
        DateTimeOffset deadline = DateTimeOffset.Now + timeout;
        bool loggedWaiting = false;

        while (true)
        {
            ImmichSidecarQueueSnapshot snapshot = await GetImmichSidecarQueueSnapshotAsync(http, cancellationToken);
            if (!snapshot.IsBusy)
            {
                if (loggedWaiting)
                {
                    AppendLog("Sync Immich: sidecar job is idle.");
                    await AppendPermanentRunLogLineAsync($"IMMICH SYNC sidecar job idle: {context}", cancellationToken);
                }
                return;
            }

            if (!loggedWaiting)
            {
                string details = $"active={snapshot.Active}, waiting={snapshot.Waiting}, delayed={snapshot.Delayed}, paused={snapshot.Paused}, queue_active={snapshot.QueueIsActive}";
                AppendLog("Sync Immich: sidecar job is already running; waiting...");
                await AppendPermanentRunLogLineAsync($"IMMICH SYNC waiting for sidecar job idle: {context}; {details}", cancellationToken);
                loggedWaiting = true;
            }

            if (DateTimeOffset.Now >= deadline)
            {
                throw new InvalidOperationException($"Immich sidecar job was still busy after {timeout.TotalMinutes:0} minutes while waiting {context}.");
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static async Task<ImmichSidecarQueueSnapshot> GetImmichSidecarQueueSnapshotAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync("/api/jobs", cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Immich jobs status request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("sidecar", out JsonElement sidecar))
        {
            throw new InvalidOperationException("Immich jobs status response did not include a sidecar queue.");
        }

        bool queueIsActive = false;
        bool queueIsPaused = false;
        if (sidecar.TryGetProperty("queueStatus", out JsonElement queueStatus))
        {
            queueIsActive = GetBooleanProperty(queueStatus, "isActive");
            queueIsPaused = GetBooleanProperty(queueStatus, "isPaused");
        }

        int active = 0;
        int waiting = 0;
        int delayed = 0;
        int paused = 0;
        if (sidecar.TryGetProperty("jobCounts", out JsonElement jobCounts))
        {
            active = GetIntProperty(jobCounts, "active");
            waiting = GetIntProperty(jobCounts, "waiting");
            delayed = GetIntProperty(jobCounts, "delayed");
            paused = GetIntProperty(jobCounts, "paused");
        }

        return new ImmichSidecarQueueSnapshot(queueIsActive, queueIsPaused, active, waiting, delayed, paused);
    }

    private static bool GetBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private sealed record ImmichSidecarQueueSnapshot(bool QueueIsActive, bool QueueIsPaused, int Active, int Waiting, int Delayed, int Paused)
    {
        public bool IsBusy => QueueIsActive || Active > 0 || Waiting > 0 || Delayed > 0 || Paused > 0;
    }

    private sealed class ImmichSyncConfig
    {
        public string? BaseUrl { get; set; }
        public string? ApiKey { get; set; }
    }

    private void SetRunningState(bool running)
    {
        _runButton.Enabled = !running;
        _pauseButton.Enabled = running;
        _pauseButton.Text = "Pause";
        _cancelButton.Enabled = running;
        _browseSelectedFolderButton.Enabled = !running;
        _selectedFoldersTreeView.Enabled = !running;
        _selectedFolderTextBox.Enabled = !running;
        _profileComboBox.Enabled = !running;
        _advancedSettingsButton.Enabled = !running;
        _recursiveCheckBox.Enabled = !running;
        _forceCheckBox.Enabled = !running;
        _overwriteSidecarsCheckBox.Enabled = !running;
        _dryRunCheckBox.Enabled = !running;
        _syncImmichCheckBox.Enabled = !running;
        _limitNumeric.Enabled = !running;
        if (running)
        {
            HideInlineFolderPicker();
        }
    }

    private void AppendLog(string message)
    {
        if (_logTextBox.InvokeRequired)
        {
            _logTextBox.Invoke(() => AppendLog(message));
            return;
        }

        _recentStatusLines.Add(message);
        while (_recentStatusLines.Count > 10)
        {
            _recentStatusLines.RemoveAt(0);
        }

        _logTextBox.Lines = _recentStatusLines.ToArray();
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.SelectionLength = 0;
        _logTextBox.ScrollToCaret();
    }

    private void StartLiveStatus()
    {
        _phaseValueLabel.Text = "Starting...";
        _startTimeValueLabel.Text = string.Empty;
        _elapsedValueLabel.Text = string.Empty;
        _remainingValueLabel.Text = string.Empty;
        _etaValueLabel.Text = string.Empty;
        _countsValueLabel.Text = string.Empty;
        _primaryRetryValueLabel.Text = string.Empty;
        _fallbackValueLabel.Text = string.Empty;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.Value = 0;
    }

    private void ClearLiveStatus()
    {
        _lastSnapshot = null;
        _phaseValueLabel.Text = string.Empty;
        _startTimeValueLabel.Text = string.Empty;
        _elapsedValueLabel.Text = string.Empty;
        _remainingValueLabel.Text = string.Empty;
        _etaValueLabel.Text = string.Empty;
        _countsValueLabel.Text = string.Empty;
        _primaryRetryValueLabel.Text = string.Empty;
        _fallbackValueLabel.Text = string.Empty;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
    }

    private void ApplyProgressSnapshot(PhotoAiRunProgressSnapshot snapshot)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyProgressSnapshot(snapshot));
            return;
        }

        _lastSnapshot = snapshot;
        _phaseValueLabel.Text = snapshot.Phase;
        _startTimeValueLabel.Text = FormatSummaryTimestamp(snapshot.StartedAt);
        _elapsedValueLabel.Text = FormatSummaryDuration(snapshot.Elapsed);
        _remainingValueLabel.Text = snapshot.EstimatedRemainingDisplay;
        _etaValueLabel.Text = snapshot.EstimatedFinishTime is null
            ? snapshot.EstimatedFinishTimeDisplay
            : FormatSummaryTimestamp(snapshot.EstimatedFinishTime);

        string total = snapshot.TotalFiles?.ToString() ?? "?";
        string percent = snapshot.ProgressFraction is null
            ? string.Empty
            : $" ({(int)Math.Round(snapshot.ProgressFraction.Value)}%)";
        _countsValueLabel.Text = $"{snapshot.FilesFinished} / {total}{percent}";
        _primaryRetryValueLabel.Text = snapshot.PrimaryRetryAttempts.ToString();
        _fallbackValueLabel.Text = snapshot.FallbackAttempts.ToString();

        if (snapshot.IsIndeterminate || snapshot.ProgressFraction is null)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.Value = 0;
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            int value = (int)Math.Round(Math.Clamp(snapshot.ProgressFraction.Value, 0.0, 100.0) * 10.0);
            _progressBar.Value = Math.Clamp(value, _progressBar.Minimum, _progressBar.Maximum);
        }
    }

    private void RefreshElapsedStatus()
    {
        if (_lastSnapshot is null)
        {
            return;
        }

        TimeSpan elapsed = DateTimeOffset.Now - _lastSnapshot.StartedAt;
        _elapsedValueLabel.Text = FormatSummaryDuration(elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
    }

    private void ShowRunSummary(PhotoAiRunSummaryDocument summaryDocument, string runLogPath, bool dryRun)
    {
        using var form = new RunSummaryForm(summaryDocument, dryRun ? null : runLogPath);
        form.ShowDialog(this);
    }

    private static string FormatSummaryTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "n/a"
            : $"{timestamp.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} {FormatLocalTimeZoneAbbreviation(timestamp.Value)}";
    }

    private static string FormatLocalTimeZoneAbbreviation(DateTimeOffset timestamp)
    {
        TimeZoneInfo local = TimeZoneInfo.Local;
        string displayName = local.IsDaylightSavingTime(timestamp) ? local.DaylightName : local.StandardName;
        if (displayName.Contains("Mountain", StringComparison.OrdinalIgnoreCase))
        {
            return "MST";
        }

        string abbreviation = string.Concat(displayName
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 0 && char.IsLetter(part[0]))
            .Select(part => char.ToUpperInvariant(part[0])));
        return string.IsNullOrWhiteSpace(abbreviation) ? local.Id : abbreviation;
    }

    private static string FormatSummaryDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatAverageSecondsPerPhoto(TimeSpan duration)
    {
        double seconds = duration < TimeSpan.Zero ? 0.0 : duration.TotalSeconds;
        return $"{seconds:F1} seconds";
    }

    private static async Task AppendAggregateRunCompleteAsync(PhotoAiScanSummary summary, IReadOnlyList<PhotoAiScanSummary> folderSummaries, CancellationToken cancellationToken)
    {
        string? runLogPath = folderSummaries.LastOrDefault(item => !item.DryRun && File.Exists(item.RunLogPath))?.RunLogPath;
        if (string.IsNullOrWhiteSpace(runLogPath))
        {
            return;
        }

        string[] lines =
        [
            string.Empty,
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] AGGREGATE RUN COMPLETE",
            $"  selected_folders={folderSummaries.Count}",
            $"  images_found={summary.ImagesFound}",
            $"  start_time={FormatSummaryTimestamp(summary.StartTime)}",
            $"  stop_time={FormatSummaryTimestamp(summary.StopTime)}",
            $"  elapsed={FormatSummaryDuration(summary.ElapsedTime)}",
            $"  average_time_per_processed_photo={FormatAverageSecondsPerPhoto(summary.AverageTimePerProcessedPhoto)}",
            $"  completed={summary.Completed}",
            $"  skipped={summary.Skipped}",
            $"  failed={summary.Failed}",
            $"  parse_xmp_skipped={summary.ParseFailed}",
            $"  xmp_written={summary.XmpWritten}",
            $"  json_write_skipped={summary.JsonWriteSkipped}",
            $"  xmp_write_skipped={summary.XmpWriteSkipped}",
            $"  model_failures={summary.ModelFailures}",
            $"  primary_retry_attempts={summary.PrimaryRetryAttempts}",
            $"  primary_retry_successes={summary.PrimaryRetrySucceeded}",
            $"  primary_retry_failures={summary.PrimaryRetryFailed}",
            $"  fallback_attempts={summary.FallbackAttempts}",
            $"  fallback_successes={summary.FallbackSucceeded}"
        ];

        await File.AppendAllTextAsync(runLogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, cancellationToken);
    }

    private static string GetDisplayVersion()
    {
        string? version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        }

        return string.IsNullOrWhiteSpace(version) ? "dev" : version;
    }

    private void CaptureRunLogPath(PhotoAiScanProgress progress)
    {
        const string prefix = "Anomaly log: ";
        if (!progress.Message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string path = progress.Message[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _lastRunLogPath = path;
    }

    private void EnableLastRunLogIfAvailable()
    {
        _openLogButton.Enabled = !string.IsNullOrWhiteSpace(_lastRunLogPath) && File.Exists(_lastRunLogPath);
    }

    private void OpenLastRunLog()
    {
        if (string.IsNullOrWhiteSpace(_lastRunLogPath) || !File.Exists(_lastRunLogPath))
        {
            MessageBox.Show(this, "No anomaly log file is available yet.", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastRunLogPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the anomaly log.\r\n\r\n{_lastRunLogPath}\r\n\r\n{ex.Message}", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed class RunSummaryForm : Form
    {
        private readonly PhotoAiRunSummaryDocument _summaryDocument;
        private readonly string? _runLogPath;
        private readonly TextBox _summaryTextBox = new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F)
        };

        public RunSummaryForm(PhotoAiRunSummaryDocument summaryDocument, string? runLogPath)
        {
            _summaryDocument = summaryDocument;
            _runLogPath = runLogPath;

            Text = summaryDocument.Title;
            Width = 820;
            Height = 640;
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(12)
            };
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            _summaryTextBox.Text = summaryDocument.PlainText;
            main.Controls.Add(_summaryTextBox, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 0)
            };
            var closeButton = new Button { Text = "Close", Width = 110, Height = 40, DialogResult = DialogResult.OK };
            var saveTextButton = new Button { Text = "Save text...", Width = 120, Height = 40 };
            var saveJsonButton = new Button { Text = "Save JSON...", Width = 120, Height = 40 };
            var copyButton = new Button { Text = "Copy", Width = 100, Height = 40 };
            var openLogButton = new Button { Text = "Open log", Width = 110, Height = 40, Enabled = !string.IsNullOrWhiteSpace(runLogPath) && File.Exists(runLogPath) };

            closeButton.Margin = new Padding(8, 0, 0, 0);
            saveTextButton.Margin = new Padding(8, 0, 0, 0);
            saveJsonButton.Margin = new Padding(8, 0, 0, 0);
            copyButton.Margin = new Padding(8, 0, 0, 0);
            openLogButton.Margin = new Padding(8, 0, 0, 0);

            saveTextButton.Click += (_, _) => SaveSummaryTextAs();
            saveJsonButton.Click += (_, _) => SaveSummaryJsonAs();
            copyButton.Click += (_, _) => Clipboard.SetText(_summaryDocument.PlainText);
            openLogButton.Click += (_, _) => OpenRunLog();

            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(saveTextButton);
            buttons.Controls.Add(saveJsonButton);
            buttons.Controls.Add(copyButton);
            buttons.Controls.Add(openLogButton);
            main.Controls.Add(buttons, 0, 1);

            AcceptButton = closeButton;
            CancelButton = closeButton;
            Controls.Add(main);
            PhotoAiTheme.Apply(this);
        }

        private void SaveSummaryTextAs()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save PhotoAI run summary text",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = _summaryDocument.SuggestedFileName,
                AddExtension = true,
                DefaultExt = "txt"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _summaryDocument.SaveTextAs(dialog.FileName);
            }
        }

        private void SaveSummaryJsonAs()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save PhotoAI run summary JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = _summaryDocument.SuggestedJsonFileName,
                AddExtension = true,
                DefaultExt = "json"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _summaryDocument.SaveJsonAs(dialog.FileName);
            }
        }

        private void OpenRunLog()
        {
            if (string.IsNullOrWhiteSpace(_runLogPath) || !File.Exists(_runLogPath))
            {
                MessageBox.Show(this, "No anomaly log file is available for this run.", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _runLogPath,
                UseShellExecute = true
            });
        }
    }
}

