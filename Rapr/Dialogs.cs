using System;
using System.Linq;
using System.Windows.Forms;

namespace Rapr
{
    /// <summary>
    /// Messages and confirmations shown with the Windows task dialog. Long details go into a
    /// collapsible "See details" section instead of stretching the dialog.
    /// </summary>
    internal static class Dialogs
    {
        // The task dialog doesn't scroll, so very long details are cut. The full text is in the log.
        internal const int MaxDetailLines = 25;

        internal static void ShowMessage(IWin32Window? owner, string text, string caption, TaskDialogIcon icon, string? details = null)
        {
            Show(owner, CreateMessagePage(text, caption, icon, details));
        }

        /// <summary>
        /// Asks before doing something. Cancel is the default button, so Enter doesn't confirm.
        /// </summary>
        /// <returns>True if the user chose <paramref name="confirmText"/>.</returns>
        internal static bool Confirm(
            IWin32Window? owner,
            string heading,
            string text,
            string caption,
            string confirmText,
            string? details,
            string? verificationText,
            out bool verificationChecked)
        {
            TaskDialogPage page = CreateConfirmPage(heading, text, caption, confirmText, details, verificationText);
            TaskDialogButton result = Show(owner, page);

            verificationChecked = page.Verification?.Checked == true;
            return result == page.Buttons[0];
        }

        internal static TaskDialogPage CreateMessagePage(string text, string caption, TaskDialogIcon icon, string? details)
        {
            return new TaskDialogPage
            {
                Caption = caption,
                Text = text,
                Icon = icon,
                Buttons = { TaskDialogButton.OK },
                Expander = CreateExpander(details, text, expanded: false),
            };
        }

        internal static TaskDialogPage CreateConfirmPage(
            string heading,
            string text,
            string caption,
            string confirmText,
            string? details,
            string? verificationText)
        {
            var confirm = new TaskDialogButton(confirmText);
            var page = new TaskDialogPage
            {
                Caption = caption,
                Heading = heading,
                Text = text,
                Icon = TaskDialogIcon.Warning,
                Buttons = { confirm, TaskDialogButton.Cancel },
                DefaultButton = TaskDialogButton.Cancel,
                Expander = CreateExpander(details, text, expanded: true),
            };

            if (!string.IsNullOrEmpty(verificationText))
            {
                page.Verification = new TaskDialogVerificationCheckBox(verificationText);
            }

            return page;
        }

        internal static string LimitLines(string text, int maxLines)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length <= maxLines)
            {
                return text;
            }

            return string.Join(Environment.NewLine, lines.Take(maxLines).Append("…"));
        }

        private static TaskDialogExpander? CreateExpander(string? details, string text, bool expanded)
        {
            details = details?.Trim();
            if (string.IsNullOrEmpty(details) || details == text.Trim())
            {
                return null;
            }

            return new TaskDialogExpander(LimitLines(details, MaxDetailLines))
            {
                Expanded = expanded,
                Position = TaskDialogExpanderPosition.AfterFootnote,
            };
        }

        private static TaskDialogButton Show(IWin32Window? owner, TaskDialogPage page)
        {
            return owner != null
                ? TaskDialog.ShowDialog(owner, page, TaskDialogStartupLocation.CenterOwner)
                : TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        }
    }
}
