using PhotoAIApp.Core;
using System.Drawing;
using System.Windows.Forms;

namespace PhotoAIApp.Gui;

public sealed class AdvancedSettingsForm : Form
{
    private enum OllamaTarget
    {
        Primary,
        Fallback
    }

    private readonly TextBox _hostTextBox = new();
    private readonly NumericUpDown _portNumeric = new() { Minimum = 1, Maximum = 65535, Value = 11434, Width = 120 };
    private readonly ComboBox _modelComboBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 360 };
    private readonly NumericUpDown _maxImageSizeNumeric = new() { Minimum = 0, Maximum = 100000, Value = 0, Increment = 1, Width = 120 };
    private readonly CheckBox _fallbackEnabledCheckBox = new() { Text = "Fallback to Unraid MiniCPM-V if primary fails", AutoSize = true };
    private readonly TextBox _fallbackHostTextBox = new();
    private readonly NumericUpDown _fallbackPortNumeric = new() { Minimum = 1, Maximum = 65535, Value = 11434, Width = 120 };
    private readonly ComboBox _fallbackModelComboBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 360 };
    private readonly NumericUpDown _fallbackMaxImageSizeNumeric = new() { Minimum = 0, Maximum = 100000, Value = 0, Increment = 1, Width = 120 };
    private readonly Label _primaryStatusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Dock = DockStyle.Fill };
    private readonly Label _fallbackStatusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Dock = DockStyle.Fill };
    private readonly Button _testConnectionButton = new() { Text = "Test primary", Width = 150, Height = 40 };
    private readonly Button _refreshModelsButton = new() { Text = "Refresh models", Width = 150, Height = 40 };
    private readonly Button _testFallbackButton = new() { Text = "Test fallback", Width = 150, Height = 40 };
    private readonly Button _refreshFallbackModelsButton = new() { Text = "Refresh models", Width = 150, Height = 40 };
    private readonly Button _okButton = new() { Text = "OK", Width = 100, Height = 40, DialogResult = DialogResult.OK };
    private readonly Button _cancelButton = new() { Text = "Cancel", Width = 100, Height = 40, DialogResult = DialogResult.Cancel };
    private readonly ToolTip _toolTip = new();

    public PhotoAiModelProfile SelectedProfile { get; private set; }

    public AdvancedSettingsForm(PhotoAiModelProfile initialProfile)
    {
        SelectedProfile = initialProfile;
        Text = "Advanced AI settings";
        Width = 1020;
        Height = 900;
        MinimumSize = new Size(900, 760);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        BuildLayout();
        LoadProfile(initialProfile);
        ConfigureToolTips();
        UpdateFallbackControlState();

        _testConnectionButton.Click += async (_, _) => await QueryModelsAsync(OllamaTarget.Primary, updateModelList: false, showMessageOnFailure: true);
        _refreshModelsButton.Click += async (_, _) => await QueryModelsAsync(OllamaTarget.Primary, updateModelList: true, showMessageOnFailure: true);
        _testFallbackButton.Click += async (_, _) => await QueryModelsAsync(OllamaTarget.Fallback, updateModelList: false, showMessageOnFailure: true);
        _refreshFallbackModelsButton.Click += async (_, _) => await QueryModelsAsync(OllamaTarget.Fallback, updateModelList: true, showMessageOnFailure: true);
        _fallbackEnabledCheckBox.CheckedChanged += async (_, _) =>
        {
            UpdateFallbackControlState();
            if (_fallbackEnabledCheckBox.Checked)
            {
                await QueryModelsAsync(OllamaTarget.Fallback, updateModelList: true, showMessageOnFailure: false);
            }
        };
        _okButton.Click += (_, _) => SelectedProfile = BuildProfileFromInputs();
        Shown += async (_, _) => await AutoRefreshModelsAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3
        };
        content.Controls.Add(CreatePrimaryGroup(), 0, 0);
        content.Controls.Add(CreateFallbackGroup(), 0, 1);
        content.Controls.Add(CreateNotesGroup(), 0, 2);
        scrollHost.Controls.Add(content);

        root.Controls.Add(scrollHost, 0, 0);
        root.Controls.Add(CreateButtonRow(), 0, 1);
        Controls.Add(root);
        PhotoAiTheme.Apply(this);
    }

    private Control CreatePrimaryGroup()
    {
        var group = new GroupBox
        {
            Text = "Primary Ollama target",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 6
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        AddRow(panel, 0, "Host/IP or URL:", _hostTextBox, _testConnectionButton);
        AddRow(panel, 1, "Port:", _portNumeric, null);
        AddRow(panel, 2, "Model:", _modelComboBox, _refreshModelsButton);
        AddRow(panel, 3, "Max Image Size:", _maxImageSizeNumeric, null);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.Controls.Add(_primaryStatusLabel, 1, 4);
        panel.SetColumnSpan(_primaryStatusLabel, 2);

        group.Controls.Add(panel);
        return group;
    }

    private Control CreateFallbackGroup()
    {
        var group = new GroupBox
        {
            Text = "Fallback Ollama target",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 6
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.Controls.Add(_fallbackEnabledCheckBox, 1, 0);
        panel.SetColumnSpan(_fallbackEnabledCheckBox, 2);
        AddRow(panel, 1, "Fallback host/IP:", _fallbackHostTextBox, _testFallbackButton);
        AddRow(panel, 2, "Fallback port:", _fallbackPortNumeric, null);
        AddRow(panel, 3, "Fallback model:", _fallbackModelComboBox, _refreshFallbackModelsButton);
        AddRow(panel, 4, "Fallback Max Image Size:", _fallbackMaxImageSizeNumeric, null);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.Controls.Add(_fallbackStatusLabel, 1, 5);
        panel.SetColumnSpan(_fallbackStatusLabel, 2);

        group.Controls.Add(panel);
        return group;
    }

    private Control CreateNotesGroup()
    {
        var group = new GroupBox
        {
            Text = "Notes",
            Dock = DockStyle.Top,
            Height = 118,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };
        var notes = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Max Image Size: 0 = original/full-res, 1440 = balanced Qwen 7B baseline, 1080 = smaller compatibility payload. Fallback has its own Max Image Size for the backup server.\r\n\r\nModel lifecycle is automatic: PhotoAIApp lets Ollama use its default keep_alive during the batch, retries transient primary failures once, and unloads configured endpoints after completion/cancel with keep_alive=0s.",
            ForeColor = SystemColors.GrayText
        };
        group.Controls.Add(notes);
        return group;
    }

    private Control CreateButtonRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var helpButton = new Button { Text = "?", Width = 44, Height = 40 };
        helpButton.Click += (_, _) => ShowHelp();
        _toolTip.SetToolTip(helpButton, "Show help for these Advanced AI settings.");
        _cancelButton.Margin = new Padding(8, 0, 0, 0);
        _okButton.Margin = new Padding(8, 0, 0, 0);
        helpButton.Margin = new Padding(8, 0, 0, 0);
        panel.Controls.Add(_cancelButton);
        panel.Controls.Add(_okButton);
        panel.Controls.Add(helpButton);
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control inputControl, Button? button)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.Controls.Add(new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0, 2, 8, 4)
        }, 0, row);

        inputControl.Dock = DockStyle.Fill;
        inputControl.Margin = new Padding(0, 2, 8, 4);
        panel.Controls.Add(inputControl, 1, row);

        if (button is not null)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 2, 0, 4);
            panel.Controls.Add(button, 2, row);
        }
    }

    private void LoadProfile(PhotoAiModelProfile profile)
    {
        _hostTextBox.Text = PhotoAiModelProfile.ExtractHost(profile.OllamaBaseUrl);
        _portNumeric.Value = Math.Clamp(PhotoAiModelProfile.ExtractPort(profile.OllamaBaseUrl), (int)_portNumeric.Minimum, (int)_portNumeric.Maximum);
        _modelComboBox.Text = profile.Model;
        _maxImageSizeNumeric.Value = Math.Clamp(profile.MaxImageDimensionPixels, (int)_maxImageSizeNumeric.Minimum, (int)_maxImageSizeNumeric.Maximum);
        _fallbackEnabledCheckBox.Checked = profile.ModelPreference == PhotoAiModelPreference.QwenPcWithUnraidFallback;
        _fallbackHostTextBox.Text = PhotoAiModelProfile.ExtractHost(profile.FallbackOllamaBaseUrl);
        _fallbackPortNumeric.Value = Math.Clamp(PhotoAiModelProfile.ExtractPort(profile.FallbackOllamaBaseUrl), (int)_fallbackPortNumeric.Minimum, (int)_fallbackPortNumeric.Maximum);
        _fallbackModelComboBox.Text = profile.FallbackModel;
        _fallbackMaxImageSizeNumeric.Value = Math.Clamp(profile.FallbackMaxImageDimensionPixels, (int)_fallbackMaxImageSizeNumeric.Minimum, (int)_fallbackMaxImageSizeNumeric.Maximum);
        _primaryStatusLabel.Text = $"Primary target: {PhotoAiModelProfile.BuildBaseUrl(_hostTextBox.Text, (int)_portNumeric.Value)}";
        _fallbackStatusLabel.Text = $"Fallback target: {PhotoAiModelProfile.BuildBaseUrl(_fallbackHostTextBox.Text, (int)_fallbackPortNumeric.Value)}";
    }

    private void ConfigureToolTips()
    {
        _toolTip.AutoPopDelay = 15000;
        _toolTip.InitialDelay = 350;
        _toolTip.ReshowDelay = 100;
        _toolTip.SetToolTip(_hostTextBox, "Primary Ollama host or full URL. Defaults to this Windows host's IPv4 address so other components can reach the same Ollama server.");
        _toolTip.SetToolTip(_portNumeric, "Primary Ollama port. Default is 11434.");
        _toolTip.SetToolTip(_modelComboBox, "Primary model to use first. You can select a discovered model or type one manually.");
        _toolTip.SetToolTip(_maxImageSizeNumeric, "Longest image edge in pixels. 0 sends original/full resolution. 1440 is the balanced Qwen 7B baseline.");
        _toolTip.SetToolTip(_fallbackEnabledCheckBox, "If the primary model fails for an image, retry that image on the fallback Ollama server/model.");
        _toolTip.SetToolTip(_fallbackHostTextBox, "Fallback Ollama host or full URL, usually the Unraid Ollama server.");
        _toolTip.SetToolTip(_fallbackPortNumeric, "Fallback Ollama port. Default is 11434.");
        _toolTip.SetToolTip(_fallbackModelComboBox, "Fallback model to use only after primary failure. You can select a discovered model or type one manually.");
        _toolTip.SetToolTip(_fallbackMaxImageSizeNumeric, "Longest image edge sent to fallback. 0 sends original/full resolution. Use this separately if fallback hardware needs a smaller payload.");
        _toolTip.SetToolTip(_testConnectionButton, "Test the primary Ollama server by reading /api/tags.");
        _toolTip.SetToolTip(_refreshModelsButton, "Load available model names from the primary Ollama server.");
        _toolTip.SetToolTip(_testFallbackButton, "Test the fallback Ollama server by reading /api/tags.");
        _toolTip.SetToolTip(_refreshFallbackModelsButton, "Load available model names from the fallback Ollama server.");
    }

    private void UpdateFallbackControlState()
    {
        bool enabled = _fallbackEnabledCheckBox.Checked;
        _fallbackHostTextBox.Enabled = enabled;
        _fallbackPortNumeric.Enabled = enabled;
        _fallbackModelComboBox.Enabled = enabled;
        _fallbackMaxImageSizeNumeric.Enabled = enabled;
        _testFallbackButton.Enabled = enabled;
        _refreshFallbackModelsButton.Enabled = enabled;
        _fallbackStatusLabel.Text = enabled
            ? $"Fallback target: {PhotoAiModelProfile.BuildBaseUrl(_fallbackHostTextBox.Text, (int)_fallbackPortNumeric.Value)}"
            : "Fallback disabled.";
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "Primary target: the Ollama server/model tried first. The default host is this Windows PC's IPv4 address.\r\n\r\n" +
            "Fallback target: optional backup Ollama server/model used only if primary fails for an image.\r\n\r\n" +
            "Max Image Size: longest image edge in pixels. 0 sends original/full resolution; 1440 is the balanced Qwen 7B baseline. Fallback has its own Max Image Size so the backup server can use a different payload if needed.\r\n\r\n" +
            "Refresh buttons load model names from each server. You can still type a model name manually.",
            "Advanced AI settings help",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private PhotoAiModelProfile BuildProfileFromInputs()
    {
        string primaryBaseUrl = PhotoAiModelProfile.BuildBaseUrl(_hostTextBox.Text, (int)_portNumeric.Value);
        string fallbackBaseUrl = PhotoAiModelProfile.BuildBaseUrl(_fallbackHostTextBox.Text, (int)_fallbackPortNumeric.Value);
        bool fallbackEnabled = _fallbackEnabledCheckBox.Checked;
        string fallbackModel = string.IsNullOrWhiteSpace(_fallbackModelComboBox.Text)
            ? PhotoAiDefaults.UnraidModel
            : _fallbackModelComboBox.Text.Trim();

        string model = string.IsNullOrWhiteSpace(_modelComboBox.Text) ? PhotoAiDefaults.QwenPcModel : _modelComboBox.Text.Trim();
        PhotoAiModelPreference modelPreference = fallbackEnabled
            ? PhotoAiModelPreference.QwenPcWithUnraidFallback
            : string.Equals(primaryBaseUrl, fallbackBaseUrl, StringComparison.OrdinalIgnoreCase) && string.Equals(model, fallbackModel, StringComparison.OrdinalIgnoreCase)
                ? PhotoAiModelPreference.UnraidMiniCpmOnly
                : PhotoAiModelPreference.PrimaryOnly;

        return new PhotoAiModelProfile
        {
            ProfileId = PhotoAiModelProfileId.Custom,
            DisplayName = "Custom",
            OllamaBaseUrl = primaryBaseUrl,
            Model = model,
            MaxImageDimensionPixels = (int)_maxImageSizeNumeric.Value,
            ModelPreference = modelPreference,
            FallbackOllamaBaseUrl = fallbackBaseUrl,
            FallbackModel = fallbackModel,
            FallbackMaxImageDimensionPixels = (int)_fallbackMaxImageSizeNumeric.Value
        };
    }

    private async Task AutoRefreshModelsAsync()
    {
        await QueryModelsAsync(OllamaTarget.Primary, updateModelList: true, showMessageOnFailure: false);
        if (_fallbackEnabledCheckBox.Checked)
        {
            await QueryModelsAsync(OllamaTarget.Fallback, updateModelList: true, showMessageOnFailure: false);
        }
    }

    private async Task QueryModelsAsync(OllamaTarget target, bool updateModelList, bool showMessageOnFailure)
    {
        bool isFallback = target == OllamaTarget.Fallback;
        string baseUrl = isFallback
            ? PhotoAiModelProfile.BuildBaseUrl(_fallbackHostTextBox.Text, (int)_fallbackPortNumeric.Value)
            : PhotoAiModelProfile.BuildBaseUrl(_hostTextBox.Text, (int)_portNumeric.Value);
        Label statusLabel = isFallback ? _fallbackStatusLabel : _primaryStatusLabel;
        ComboBox comboBox = isFallback ? _fallbackModelComboBox : _modelComboBox;
        string defaultModel = isFallback ? PhotoAiDefaults.UnraidModel : PhotoAiDefaults.QwenPcModel;
        string label = isFallback ? "Fallback" : "Primary";

        SetModelQueryButtonsEnabled(false);
        statusLabel.Text = $"Querying {baseUrl}...";

        try
        {
            var scanner = new PhotoAiScanner();
            string[] models = NormalizeModelListForTarget(await scanner.GetAvailableOllamaModelsAsync(baseUrl), isFallback);
            statusLabel.Text = $"{label} connected: {baseUrl}; {models.Length} model(s) available.";

            if (updateModelList)
            {
                string currentModel = comboBox.Text.Trim();
                comboBox.Items.Clear();
                comboBox.Items.AddRange(models.Cast<object>().ToArray());
                if (models.Length > 0)
                {
                    comboBox.Text = models.Contains(currentModel, StringComparer.OrdinalIgnoreCase)
                        ? currentModel
                        : models.Contains(defaultModel, StringComparer.OrdinalIgnoreCase)
                            ? defaultModel
                            : models[0];
                }
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"{label} connection failed: {ex.Message}";
            if (showMessageOnFailure)
            {
                MessageBox.Show(this, ex.Message, $"{label} Ollama connection failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetModelQueryButtonsEnabled(true);
            UpdateFallbackControlState();
        }
    }

    private static string[] NormalizeModelListForTarget(IEnumerable<string> discoveredModels, bool isFallback)
    {
        return discoveredModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void SetModelQueryButtonsEnabled(bool enabled)
    {
        _testConnectionButton.Enabled = enabled;
        _refreshModelsButton.Enabled = enabled;
        _testFallbackButton.Enabled = enabled && _fallbackEnabledCheckBox.Checked;
        _refreshFallbackModelsButton.Enabled = enabled && _fallbackEnabledCheckBox.Checked;
    }
}
