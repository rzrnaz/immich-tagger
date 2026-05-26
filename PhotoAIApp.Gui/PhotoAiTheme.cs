using System.Drawing;
using System.Windows.Forms;

namespace PhotoAIApp.Gui;

internal static class PhotoAiTheme
{
    public static readonly Color Background = Color.FromArgb(246, 242, 236);
    public static readonly Color Surface = Color.FromArgb(234, 226, 216);
    public static readonly Color SurfaceRaised = Color.FromArgb(242, 238, 232);
    public static readonly Color SurfaceInput = Color.FromArgb(248, 244, 238);
    public static readonly Color SelectedSurface = Color.FromArgb(239, 209, 190);
    public static readonly Color Border = Color.FromArgb(224, 204, 174);
    public static readonly Color Text = Color.FromArgb(16, 16, 16);
    public static readonly Color MutedText = Color.FromArgb(70, 70, 70);
    public static readonly Color Accent = Color.FromArgb(255, 101, 42);
    public static readonly Color AccentDark = Color.FromArgb(217, 78, 22);
    public static readonly Color AccentSoft = Color.FromArgb(245, 220, 204);
    public static readonly Color WarmBlue = Color.FromArgb(72, 129, 166);
    public static readonly Color WarmYellow = Color.FromArgb(249, 168, 37);
    public static readonly Color WarmRed = Color.FromArgb(198, 40, 40);

    public static Icon? TryLoadApplicationIcon()
    {
        try
        {
            string executablePath = Application.ExecutablePath;
            return string.IsNullOrWhiteSpace(executablePath) ? null : Icon.ExtractAssociatedIcon(executablePath);
        }
        catch
        {
            return null;
        }
    }

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        StyleControlTree(form);
    }

    private static void StyleControlTree(Control control)
    {
        StyleControl(control);
        foreach (Control child in control.Controls)
        {
            StyleControlTree(child);
        }
    }

    private static void StyleControl(Control control)
    {
        switch (control)
        {
            case Form:
                control.BackColor = Background;
                control.ForeColor = Text;
                break;
            case GroupBox:
                control.BackColor = Surface;
                control.ForeColor = Text;
                break;
            case TableLayoutPanel or FlowLayoutPanel or Panel:
                if (control.BackColor == SystemColors.Control || control.BackColor == Color.Empty)
                {
                    control.BackColor = Background;
                }
                control.ForeColor = Text;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                if (label.ForeColor == SystemColors.GrayText || label.ForeColor == SystemColors.ControlText)
                {
                    label.ForeColor = MutedText;
                }
                break;
            case TextBox textBox:
                textBox.BackColor = SurfaceInput;
                textBox.ForeColor = Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = SurfaceInput;
                comboBox.ForeColor = Text;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = SurfaceInput;
                numeric.ForeColor = Text;
                break;
            case CheckBox checkBox:
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = Text;
                checkBox.FlatStyle = FlatStyle.Standard;
                break;
            case TreeView treeView:
                treeView.BackColor = SurfaceInput;
                treeView.ForeColor = Text;
                treeView.LineColor = Border;
                break;
            case ProgressBar:
                control.BackColor = SurfaceInput;
                control.ForeColor = Accent;
                break;
            case Button button:
                StyleDefaultButton(button);
                break;
            default:
                control.ForeColor = Text;
                break;
        }
    }

    private static void StyleDefaultButton(Button button)
    {
        bool appearsCustom = button.FlatStyle == FlatStyle.Flat
            && button.BackColor != SystemColors.Control
            && button.BackColor != Color.Empty;

        if (!appearsCustom)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = SurfaceRaised;
            button.ForeColor = Text;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
        }
    }
}
