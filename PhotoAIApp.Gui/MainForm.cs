using PhotoAIApp.Core;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PhotoAIApp.Gui;

public sealed class MainForm : Form
{
    private readonly TextBox _libraryRootTextBox = new() { Text = @"P:\" };
    private readonly TextBox _selectedFolderTextBox = new() { Text = @"P:\photoai-test" };
    private readonly TextBox _ollamaTextBox = new() { Text = PhotoAiDefaults.QwenPcOllamaBaseUrl };
    private readonly ComboBox _modelComboBox = new() { Text = PhotoAiDefaults.QwenPcModel, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _modelPreferenceComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _fallbackOllamaTextBox = new() { Text = PhotoAiDefaults.UnraidOllamaBaseUrl };
    private readonly TextBox _fallbackModelTextBox = new() { Text = PhotoAiDefaults.UnraidModel };
    private readonly NumericUpDown _limitNumeric = new() { Minimum = 0, Maximum = 100000, Value = 0, Width = 100 };
    private readonly CheckBox _recursiveCheckBox = new() { Text = "Subfolders", AutoSize = true };
    private readonly CheckBox _forceCheckBox = new() { Text = "Scan Existing", Checked = true, AutoSize = true };
    private readonly CheckBox _overwriteSidecarsCheckBox = new() { Text = "Overwrite XMP+", Checked = true, AutoSize = true };
    private readonly CheckBox _addTagsCheckBox = new() { Text = "Add Tags", Checked = false, AutoSize = true };
    private readonly CheckBox _dryRunCheckBox = new() { Text = "Dry Run", Checked = true, AutoSize = true };
    private readonly Button _browseLibraryRootButton = new() { Text = "Browse...", Width = 120, Height = 40 };
    private readonly Button _browseSelectedFolderButton = new() { Text = "Browse...", Width = 120, Height = 40 };
    private readonly Button _refreshModelsButton = new() { Text = "Refresh models", Width = 150, Height = 40 };
    private readonly Button _runButton = new() { Text = "Run scan", Width = 130, Height = 40 };
    private readonly Button _pauseButton = new() { Text = "Pause", Width = 100, Height = 40, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Stop", Width = 100, Height = 40, Enabled = false };
    private readonly Button _openLogButton = new() { Text = "Open log", Width = 120, Height = 40, Enabled = false };
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
        WordWrap = false,
        Dock = DockStyle.Top,
        Height = 118,
        Font = new Font("Consolas", 10F)
    };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private readonly List<string> _recentStatusLines = [];

    private CancellationTokenSource? _cancellationTokenSource;
    private PhotoAiPauseController? _pauseController;
    private PhotoAiRunProgressSnapshot? _lastSnapshot;
    private string? _lastRunLogPath;

    public MainForm()
    {
        Text = $"PhotoAI Immich Sidecar Tool v{GetDisplayVersion()}";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        Width = 1300;
        Height = 860;
        MinimumSize = new Size(980, 680);
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

        _modelPreferenceComboBox.Items.Add(new ModelPreferenceItem("Qwen PC preferred (fallback to Unraid MiniCPM-V)", PhotoAiModelPreference.QwenPcWithUnraidFallback));
        _modelPreferenceComboBox.Items.Add(new ModelPreferenceItem("Unraid MiniCPM-V only (no fallback)", PhotoAiModelPreference.UnraidMiniCpmOnly));
        _modelPreferenceComboBox.SelectedIndex = 0;
        _modelPreferenceComboBox.SelectedIndexChanged += (_, _) => ApplyModelPreferenceToInputs();
        _toolTip.SetToolTip(_forceCheckBox, "When checked, images with an existing .photoai.json can be considered for re-scan. When unchecked, existing PhotoAI JSON means skip the image.");
        _toolTip.SetToolTip(_overwriteSidecarsCheckBox, "When checked, existing .photoai.json and .jpg.xmp sidecars are regenerated together. When unchecked, existing sidecars are protected and no-op images skip the LLM.");
        ApplyModelPreferenceToInputs();

        _browseLibraryRootButton.Click += (_, _) => ShowInlineFolderPicker(_libraryRootTextBox, "Choose Immich library root / safety root");
        _browseSelectedFolderButton.Click += (_, _) => ShowInlineFolderPicker(_selectedFolderTextBox, "Choose folder to scan", _libraryRootTextBox.Text);
        _runButton.Click += async (_, _) => await RunScanAsync();
        _pauseButton.Click += (_, _) => TogglePause();
        _cancelButton.Click += (_, _) => _cancellationTokenSource?.Cancel();
        _openLogButton.Click += (_, _) => OpenLastRunLog();
        _refreshModelsButton.Click += async (_, _) => await RefreshModelsAsync();
        _statusTimer.Tick += (_, _) => RefreshElapsedStatus();
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
            RowCount = 3
        };
        for (int i = 0; i < 3; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        AddStatusPair(panel, 0, 0, "Phase", _phaseValueLabel);
        AddStatusPair(panel, 1, 0, "Start", _startTimeValueLabel);
        AddStatusPair(panel, 2, 0, "Elapsed", _elapsedValueLabel);
        AddStatusPair(panel, 0, 1, "Remaining", _remainingValueLabel);
        AddStatusPair(panel, 1, 1, "ETA", _etaValueLabel);
        AddStatusPair(panel, 2, 1, "Files", _countsValueLabel);

        panel.Controls.Add(_progressBar, 0, 2);
        panel.SetColumnSpan(_progressBar, 3);

        group.Controls.Add(panel);
        return group;
    }

    private static void AddStatusPair(TableLayoutPanel panel, int column, int row, string label, Label valueLabel)
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0)
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
            Margin = new Padding(0, 0, 0, 2)
        }, 0, 0);
        container.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(container, column, row);
    }

    private static Label CreateStatusValueLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
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
            RowCount = 8,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        AddPathRow(panel, 0, "Immich library root:", _libraryRootTextBox, _browseLibraryRootButton);
        AddPathRow(panel, 1, "Selected folder:", _selectedFolderTextBox, _browseSelectedFolderButton);
        panel.RowStyles.Add(_inlineFolderPickerRowStyle);
        panel.Controls.Add(_inlineFolderPickerHost, 1, 2);
        panel.SetColumnSpan(_inlineFolderPickerHost, 2);
        AddPathRow(panel, 3, "Preferred model:", _modelPreferenceComboBox, null);
        AddPathRow(panel, 4, "PC Ollama URL:", _ollamaTextBox, null);
        AddPathRow(panel, 5, "PC model:", _modelComboBox, _refreshModelsButton);
        AddPathRow(panel, 6, "Unraid Ollama URL:", _fallbackOllamaTextBox, null);
        AddPathRow(panel, 7, "Unraid model:", _fallbackModelTextBox, null);

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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 860));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var row1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        ConfigureOptionCheckBox(_recursiveCheckBox, 165);
        ConfigureOptionCheckBox(_forceCheckBox, 190);
        ConfigureOptionCheckBox(_overwriteSidecarsCheckBox, 220);

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

        ConfigureOptionCheckBox(_addTagsCheckBox, 190);
        ConfigureOptionCheckBox(_dryRunCheckBox, 150);

        var limitLabel = new Label
        {
            Text = "File Limit",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Width = 150,
            Height = 38,
            Margin = new Padding(10, 2, 4, 0)
        };

        _limitNumeric.Width = 110;
        _limitNumeric.Height = 38;
        _limitNumeric.Margin = new Padding(0, 2, 0, 0);

        row2.Controls.Add(_addTagsCheckBox);
        row2.Controls.Add(_dryRunCheckBox);
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
        StyleActionButton(_runButton, Color.FromArgb(46, 125, 50), Color.White);
        StyleActionButton(_pauseButton, Color.FromArgb(249, 168, 37), Color.Black);
        StyleActionButton(_cancelButton, Color.FromArgb(198, 40, 40), Color.White);
        StyleActionButton(_openLogButton, Color.FromArgb(132, 132, 132), Color.White);
        KeepButtonTextColor(_cancelButton, Color.White);
        KeepButtonTextColor(_openLogButton, Color.White);

        panel.Controls.Add(_runButton);
        panel.Controls.Add(_pauseButton);
        panel.Controls.Add(_cancelButton);
        panel.Controls.Add(_openLogButton);

        return panel;
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

    private sealed record ModelPreferenceItem(string Label, PhotoAiModelPreference Preference)
    {
        public override string ToString() => Label;
    }

    private PhotoAiModelPreference CurrentModelPreference()
    {
        return _modelPreferenceComboBox.SelectedItem is ModelPreferenceItem item
            ? item.Preference
            : PhotoAiModelPreference.QwenPcWithUnraidFallback;
    }

    private void ApplyModelPreferenceToInputs()
    {
        bool unraidOnly = CurrentModelPreference() == PhotoAiModelPreference.UnraidMiniCpmOnly;
        _ollamaTextBox.Enabled = !unraidOnly && _cancellationTokenSource is null;
        _modelComboBox.Enabled = !unraidOnly && _cancellationTokenSource is null;
        _refreshModelsButton.Enabled = !unraidOnly && _cancellationTokenSource is null;
        _fallbackOllamaTextBox.Enabled = _cancellationTokenSource is null;
        _fallbackModelTextBox.Enabled = _cancellationTokenSource is null;
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

    private async Task RefreshModelsAsync()
    {
        string ollamaBaseUrl = _ollamaTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ollamaBaseUrl))
        {
            MessageBox.Show(this, "Ollama URL is required.", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _refreshModelsButton.Enabled = false;
        AppendLog($"Querying Ollama models from {ollamaBaseUrl}...");

        try
        {
            var scanner = new PhotoAiScanner();
            string currentModel = _modelComboBox.Text.Trim();
            string[] models = await scanner.GetAvailableOllamaModelsAsync(ollamaBaseUrl);

            _modelComboBox.Items.Clear();
            _modelComboBox.Items.AddRange(models.Cast<object>().ToArray());

            if (models.Length == 0)
            {
                AppendLog("No Ollama models were returned.");
                return;
            }

            string modelToSelect = models.Contains(currentModel, StringComparer.OrdinalIgnoreCase)
                ? currentModel
                : models.Contains(PhotoAiDefaults.Model, StringComparer.OrdinalIgnoreCase)
                    ? PhotoAiDefaults.Model
                    : models[0];

            _modelComboBox.Text = modelToSelect;
            AppendLog($"Loaded {models.Length} Ollama model(s). Selected: {modelToSelect}");
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to query Ollama models: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Ollama model query failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshModelsButton.Enabled = true;
        }
    }

    private async Task RunScanAsync()
    {
        string selectedFolder = _selectedFolderTextBox.Text.Trim();
        string libraryRoot = _libraryRootTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
        {
            MessageBox.Show(this, "Selected folder does not exist.", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
        {
            MessageBox.Show(this, "Immich library root / safety root does not exist.", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!PhotoAiScanner.IsPathUnderRoot(selectedFolder, libraryRoot))
        {
            MessageBox.Show(this,
                "Selected folder must be inside the Immich library root / safety root.",
                "Safety check failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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
            AppendLog(item.Message);
            if (item.Snapshot is not null)
            {
                ApplyProgressSnapshot(item.Snapshot);
            }
        });
        var scanner = new PhotoAiScanner();

        try
        {
            PhotoAiScanSummary summary = await scanner.ScanFolderAsync(
                new PhotoAiScanOptions
                {
                    FolderPath = selectedFolder,
                    SafetyRootPath = libraryRoot,
                    Recursive = _recursiveCheckBox.Checked,
                    Force = _forceCheckBox.Checked,
                    WriteJson = true,
                    WriteXmp = true,
                    OverwriteJson = _overwriteSidecarsCheckBox.Checked,
                    OverwriteXmp = _overwriteSidecarsCheckBox.Checked,
                    AddTags = _addTagsCheckBox.Checked,
                    DryRun = _dryRunCheckBox.Checked,
                    OllamaBaseUrl = _ollamaTextBox.Text.Trim(),
                    Model = _modelComboBox.Text.Trim(),
                    ModelPreference = CurrentModelPreference(),
                    FallbackOllamaBaseUrl = _fallbackOllamaTextBox.Text.Trim(),
                    FallbackModel = _fallbackModelTextBox.Text.Trim(),
                    PauseController = _pauseController,
                    Limit = _limitNumeric.Value > 0 ? (int)_limitNumeric.Value : null
                },
                progress,
                _cancellationTokenSource.Token);

            _lastRunLogPath = summary.RunLogPath;
            _openLogButton.Enabled = !summary.DryRun && File.Exists(_lastRunLogPath);

            PhotoAiRunSummaryDocument summaryDocument = PhotoAiRunSummaryDocument.FromSummary(summary);
            AppendLog("Scan complete. Summary opened in a separate window.");
            _statusTimer.Stop();
            ShowRunSummary(summaryDocument, summary.RunLogPath, summary.DryRun);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled by user.");
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

    private void SetRunningState(bool running)
    {
        _runButton.Enabled = !running;
        _pauseButton.Enabled = running;
        _pauseButton.Text = "Pause";
        _cancelButton.Enabled = running;
        _browseLibraryRootButton.Enabled = !running;
        _browseSelectedFolderButton.Enabled = !running;
        _libraryRootTextBox.Enabled = !running;
        _selectedFolderTextBox.Enabled = !running;
        _ollamaTextBox.Enabled = !running && CurrentModelPreference() != PhotoAiModelPreference.UnraidMiniCpmOnly;
        _modelComboBox.Enabled = !running && CurrentModelPreference() != PhotoAiModelPreference.UnraidMiniCpmOnly;
        _modelPreferenceComboBox.Enabled = !running;
        _fallbackOllamaTextBox.Enabled = !running;
        _fallbackModelTextBox.Enabled = !running;
        _refreshModelsButton.Enabled = !running && CurrentModelPreference() != PhotoAiModelPreference.UnraidMiniCpmOnly;
        _recursiveCheckBox.Enabled = !running;
        _forceCheckBox.Enabled = !running;
        _overwriteSidecarsCheckBox.Enabled = !running;
        _addTagsCheckBox.Enabled = !running;
        _dryRunCheckBox.Enabled = !running;
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
        while (_recentStatusLines.Count > 5)
        {
            _recentStatusLines.RemoveAt(0);
        }

        _logTextBox.Lines = _recentStatusLines.ToArray();
        _logTextBox.SelectionStart = _logTextBox.TextLength;
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
        _etaValueLabel.Text = snapshot.EstimatedFinishTimeDisplay;

        string total = snapshot.TotalFiles?.ToString() ?? "?";
        _countsValueLabel.Text = $"{snapshot.FilesFinished} / {total}";

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
            : timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    private static string FormatSummaryDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
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

    private void OpenLastRunLog()
    {
        if (string.IsNullOrWhiteSpace(_lastRunLogPath) || !File.Exists(_lastRunLogPath))
        {
            MessageBox.Show(this, "No anomaly log file is available yet.", "PhotoAI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastRunLogPath,
            UseShellExecute = true
        });
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

