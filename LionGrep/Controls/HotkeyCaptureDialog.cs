using System.Text;
using System.Threading.Tasks;
using LionGrep.Core.Logic;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace LionGrep.Controls;

/// <summary>
/// Modal "press a hotkey now" capture dialog. Listens for the next non-modifier keystroke,
/// reads the live Ctrl/Shift/Alt/Win state from <see cref="InputKeyboardSource"/>, formats the
/// canonical "Ctrl+Shift+F2" string, validates it (parseable + not reserved), and auto-closes.
///
/// <para>Returns <c>null</c> when the user cancels with the Esc key or the Cancel button. Reserved
/// combos (Ctrl+Enter, Ctrl+Alt+Enter) and unparseable presses surface an inline error and the
/// dialog stays open so the user can try again.</para>
/// </summary>
public static class HotkeyCaptureDialog
{
    public static async Task<string?> CaptureAsync(XamlRoot xamlRoot)
    {
        var instructions = new TextBlock
        {
            Text = "Press a hotkey now — e.g. Ctrl+1, Alt+F2. Esc to cancel.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        var preview = new TextBlock
        {
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MinHeight = 24,
            Opacity = 0.85,
        };
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            FontSize = 12,
            Visibility = Visibility.Collapsed,
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 380, MinHeight = 90 };
        panel.Children.Add(instructions);
        panel.Children.Add(preview);
        panel.Children.Add(error);

        // The Grid is what actually receives focus and KeyDown — ContentDialog's content panel
        // doesn't take focus on its own. IsTabStop + an explicit Focus() call after Loaded does it.
        var grid = new Grid
        {
            IsTabStop = true,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        grid.Children.Add(panel);

        string? captured = null;
        ContentDialog? dialogRef = null;

        grid.KeyDown += (_, e) =>
        {
            // Modifier-only presses don't form a hotkey by themselves. Eat them so they don't
            // bubble to a dialog button or anywhere else.
            if (IsModifierKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Escape)
                return;     // let the dialog's CloseButton handle it

            var hotkey = BuildHotkeyString(e.Key);
            preview.Text = hotkey;
            e.Handled = true;

            if (HotkeyParser.IsReserved(hotkey))
            {
                error.Text = "Reserved by LionGrep (Search / Replace).";
                error.Visibility = Visibility.Visible;
                return;
            }
            if (!HotkeyParser.TryParse(hotkey, out HotkeyParser.ParsedHotkey _))
            {
                error.Text = "That combination isn't a usable hotkey.";
                error.Visibility = Visibility.Visible;
                return;
            }

            captured = hotkey;
            dialogRef?.Hide();
        };

        grid.Loaded += (_, _) => grid.Focus(FocusState.Programmatic);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Assign hotkey",
            Content = grid,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
        };
        dialogRef = dialog;

        await dialog.ShowAsync();
        return captured;
    }

    private static bool IsModifierKey(VirtualKey k) =>
        k is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
          or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
          or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
          or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static string BuildHotkeyString(VirtualKey key)
    {
        var sb = new StringBuilder();
        if (IsKeyDown(VirtualKey.Control)) sb.Append("Ctrl+");
        if (IsKeyDown(VirtualKey.Shift)) sb.Append("Shift+");
        if (IsKeyDown(VirtualKey.Menu)) sb.Append("Alt+");
        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows)) sb.Append("Win+");
        sb.Append(KeyName(key));
        return sb.ToString();
    }

    /// <summary>Maps a <see cref="VirtualKey"/> to the token form HotkeyParser understands —
    /// "1" / "A" / "F2" / "Enter". Anything outside those ranges is returned by name (which the
    /// parser will reject and the dialog will surface as "not a usable hotkey").</summary>
    private static string KeyName(VirtualKey k)
    {
        if (k >= VirtualKey.Number0 && k <= VirtualKey.Number9)
            return ((int)k - (int)VirtualKey.Number0).ToString();
        if (k >= VirtualKey.A && k <= VirtualKey.Z)
            return ((char)k).ToString();
        if (k >= VirtualKey.F1 && k <= VirtualKey.F24)
            return "F" + (k - VirtualKey.F1 + 1);
        return k switch
        {
            VirtualKey.Enter => "Enter",
            _ => k.ToString(),
        };
    }
}
