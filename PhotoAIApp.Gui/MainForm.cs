using PhotoAIApp.Core;
using System.Diagnostics;
using System.Drawing;
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
    private readonly CheckBox _overwriteJsonCheckBox = new() { Text = "Overwrite JSON", Checked = true, AutoSize = true };
    private readonly CheckBox _overwriteXmpCheckBox = new() { Text = "Overwrite XMP", Checked = false, AutoSize = true };
    private readonly CheckBox _addTagsCheckBox = new() { Text = "Add Tags", Checked = false, AutoSize = true };
    private readonly CheckBox _dryRunCheckBox = new() { Text = "Dry Run", Checked = true, AutoSize = true };
    private readonly Button _browseLibraryRootButton = new() { Text = "Browse...", Width = 120, Height = 34 };
    private readonly Button _browseSelectedFolderButton = new() { Text = "Browse...", Width = 120, Height = 34 };
    private readonly Button _refreshModelsButton = new() { Text = "Refresh models", Width = 150, Height = 34 };
    private readonly Button _runButton = new() { Text = "Run scan", Width = 130, Height = 36 };
    private readonly Button _pauseButton = new() { Text = "Pause", Width = 100, Height = 36, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Stop", Width = 100, Height = 36, Enabled = false };
    private readonly Button _openLogButton = new() { Text = "Open log", Width = 120, Height = 36, Enabled = false };
    private readonly TextBox _logTextBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10F)
    };

    private CancellationTokenSource? _cancellationTokenSource;
    private PhotoAiPauseController? _pauseController;
    private string? _lastRunLogPath;

    public MainForm()
    {
        Text = "PhotoAI Immich Sidecar Tool";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        Width = 1300;
        Height = 820;
        MinimumSize = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterScreen;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        main.Controls.Add(CreatePathPanel(), 0, 0);
        main.Controls.Add(CreateOptionsPanel(), 0, 1);
        main.Controls.Add(CreateButtonPanel(), 0, 2);
        main.Controls.Add(_logTextBox, 0, 3);

        Controls.Add(main);

        _modelPreferenceComboBox.Items.Add(new ModelPreferenceItem("Qwen PC preferred (fallback to Unraid MiniCPM-V)", PhotoAiModelPreference.QwenPcWithUnraidFallback));
        _modelPreferenceComboBox.Items.Add(new ModelPreferenceItem("Unraid MiniCPM-V only (no fallback)", PhotoAiModelPreference.UnraidMiniCpmOnly));
        _modelPreferenceComboBox.SelectedIndex = 0;
        _modelPreferenceComboBox.SelectedIndexChanged += (_, _) => ApplyModelPreferenceToInputs();
        ApplyModelPreferenceToInputs();

        _browseLibraryRootButton.Click += (_, _) => BrowseForFolder(_libraryRootTextBox, "Choose Immich library root / safety root");
        _browseSelectedFolderButton.Click += (_, _) => BrowseForFolder(_selectedFolderTextBox, "Choose folder to scan", _libraryRootTextBox.Text);
        _runButton.Click += async (_, _) => await RunScanAsync();
        _pauseButton.Click += (_, _) => TogglePause();
        _cancelButton.Click += (_, _) => _cancellationTokenSource?.Cancel();
        _openLogButton.Click += (_, _) => OpenLastRunLog();
        _refreshModelsButton.Click += async (_, _) => await RefreshModelsAsync();
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
            RowCount = 7,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        AddPathRow(panel, 0, "Immich library root:", _libraryRootTextBox, _browseLibraryRootButton);
        AddPathRow(panel, 1, "Selected folder:", _selectedFolderTextBox, _browseSelectedFolderButton);
        AddPathRow(panel, 2, "Preferred model:", _modelPreferenceComboBox, null);
        AddPathRow(panel, 3, "PC Ollama URL:", _ollamaTextBox, null);
        AddPathRow(panel, 4, "PC model:", _modelComboBox, _refreshModelsButton);
        AddPathRow(panel, 5, "Unraid Ollama URL:", _fallbackOllamaTextBox, null);
        AddPathRow(panel, 6, "Unraid model:", _fallbackModelTextBox, null);

        group.Controls.Add(panel);
        return group;
    }

    private static void AddPathRow(TableLayoutPanel panel, int row, string label, Control inputControl, Button? button)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var labelControl = new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0, 0, 8, 8)
        };

        inputControl.Dock = DockStyle.Fill;
        inputControl.Margin = new Padding(0, 0, 8, 8);

        panel.Controls.Add(labelControl, 0, row);
        panel.Controls.Add(inputControl, 1, row);

        if (button is not null)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 8);
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
        ConfigureOptionCheckBox(_overwriteJsonCheckBox, 210);
        ConfigureOptionCheckBox(_overwriteXmpCheckBox, 230);

        row1.Controls.Add(_recursiveCheckBox);
        row1.Controls.Add(_forceCheckBox);
        row1.Controls.Add(_overwriteJsonCheckBox);
        row1.Controls.Add(_overwriteXmpCheckBox);

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
            Text = "Limit, 0 = all:",
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
            Padding = new Padding(0, 0, 0, 12),
            Margin = new Padding(0, 0, 0, 8)
        };

        _runButton.Margin = new Padding(0, 0, 10, 0);
        _pauseButton.Margin = new Padding(0, 0, 10, 0);
        _cancelButton.Margin = new Padding(0, 0, 10, 0);
        _openLogButton.Margin = new Padding(0, 0, 10, 0);

        panel.Controls.Add(_runButton);
        panel.Controls.Add(_pauseButton);
        panel.Controls.Add(_cancelButton);
        panel.Controls.Add(_openLogButton);

        return panel;
    }

    private static void BrowseForFolder(TextBox target, string description, string? fallbackPath = null)
    {
        string initialPath = FirstExistingDirectory(target.Text, fallbackPath, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

        using var dialog = new SafeFolderPickerForm(description, initialPath);
        if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            target.Text = dialog.SelectedPath;
        }
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

        public string SelectedPath { get; private set; }

        public SafeFolderPickerForm(string description, string initialPath)
        {
            Text = description;
            Width = 900;
            Height = 650;
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
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var help = new Label
            {
                Text = "Safe folder picker: select a folder only. This picker does not expose shell delete/rename commands.",
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8)
            };

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            pathPanel.Controls.Add(_pathTextBox, 0, 0);
            pathPanel.Controls.Add(_upButton, 1, 0);
            pathPanel.Controls.Add(_refreshButton, 2, 0);

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 10, 0, 0)
            };
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
            _okButton.Click += (_, _) => SelectedPath = _pathTextBox.Text;

            LoadInitialPath(initialPath);
        }

        private void LoadInitialPath(string initialPath)
        {
            _tree.Nodes.Clear();
            string path = Directory.Exists(initialPath) ? initialPath : FirstExistingDirectory(initialPath);
            TreeNode node = CreateNode(path);
            _tree.Nodes.Add(node);
            PopulateChildren(node);
            node.Expand();
            _tree.SelectedNode = node;
            SetSelectedPath(path);
        }

        private void NavigateUp()
        {
            string current = _pathTextBox.Text;
            DirectoryInfo? parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parent is null || !parent.Exists)
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
        _lastRunLogPath = null;
        _openLogButton.Enabled = false;
        _cancellationTokenSource = new CancellationTokenSource();
        _pauseController = new PhotoAiPauseController();

        var progress = new Progress<PhotoAiScanProgress>(item => AppendLog(item.Message));
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
                    OverwriteJson = _overwriteJsonCheckBox.Checked,
                    OverwriteXmp = _overwriteXmpCheckBox.Checked,
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

            AppendLog("");
            AppendLog("Scan complete.");
            AppendLog($"Images found:        {summary.ImagesFound}");

            if (summary.DryRun)
            {
                AppendLog("Mode:                DRY RUN / PREVIEW");
                AppendLog($"Would process:       {summary.WouldProcess}");
                AppendLog($"Would write JSON:    {summary.WouldWriteJsonSidecar}");
                AppendLog($"Would write XMP:     {summary.WouldWriteXmpSidecar}");
                AppendLog($"Would skip JSON:     {summary.WouldSkipJsonSidecar}");
                AppendLog($"Would skip XMP:      {summary.WouldSkipXmpSidecar}");
                AppendLog($"Existing JSON:       {summary.ExistingJsonSidecars}");
                AppendLog($"Existing XMP:        {summary.ExistingXmpSidecars}");
                AppendLog($"Would skip:          {summary.Skipped}");
                AppendLog($"Would block/fail:    {summary.Failed}");
            }
            else
            {
                AppendLog($"Completed:           {summary.Completed}");
                AppendLog($"Skipped:             {summary.Skipped}");
                AppendLog($"Failed:              {summary.Failed}");
                AppendLog($"Parse/XMP skipped:   {summary.ParseFailed}");
                AppendLog($"XMP written:         {summary.XmpWritten}");
                AppendLog($"JSON write skipped:  {summary.JsonWriteSkipped}");
                AppendLog($"XMP write skipped:   {summary.XmpWriteSkipped}");
                AppendLog($"Anomaly log:         {summary.RunLogPath}");
            }
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
            _pauseController?.Resume();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            _pauseController = null;
            SetRunningState(false);
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
        _overwriteJsonCheckBox.Enabled = !running;
        _overwriteXmpCheckBox.Enabled = !running;
        _addTagsCheckBox.Enabled = !running;
        _dryRunCheckBox.Enabled = !running;
        _limitNumeric.Enabled = !running;
    }

    private void AppendLog(string message)
    {
        if (_logTextBox.InvokeRequired)
        {
            _logTextBox.Invoke(() => AppendLog(message));
            return;
        }

        _logTextBox.AppendText(message + Environment.NewLine);
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
}
