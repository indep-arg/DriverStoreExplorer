using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rapr.Tests
{
    [TestClass]
    public class DialogsTests
    {
        private static readonly Type DialogsType = typeof(MyObjectListView).Assembly.GetType("Rapr.Dialogs", throwOnError: true);

        private static TaskDialogPage CreateMessagePage(string text, string details) =>
            (TaskDialogPage)Call("CreateMessagePage", text, "Caption", TaskDialogIcon.Error, details);

        private static TaskDialogPage CreateConfirmPage(string details, string verificationText) =>
            (TaskDialogPage)Call("CreateConfirmPage", "Delete 2 packages?", "Are you sure?", "Caption", "Delete", details, verificationText);

        private static object Call(string name, params object[] args) =>
            DialogsType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        [TestMethod]
        public void ConfirmationsDefaultToCancel()
        {
            TaskDialogPage page = CreateConfirmPage(null, null);

            Assert.AreEqual("Delete", page.Buttons[0].Text);
            Assert.AreSame(TaskDialogButton.Cancel, page.DefaultButton);
            Assert.IsNull(page.Expander);
            Assert.IsNull(page.Verification);
        }

        [TestMethod]
        public void ConfirmationListsArePlainToSee()
        {
            TaskDialogPage page = CreateConfirmPage("a.inf\r\nb.inf", "Don't show this again");

            Assert.IsTrue(page.Expander.Expanded);
            Assert.AreEqual("a.inf\r\nb.inf", page.Expander.Text);
            Assert.AreEqual("Don't show this again", page.Verification.Text);
        }

        [TestMethod]
        public void ErrorDetailsAreCollapsed()
        {
            TaskDialogPage page = CreateMessagePage("Access denied.", "System.UnauthorizedAccessException: Access denied.\r\n   at ...");

            Assert.IsFalse(page.Expander.Expanded);
        }

        [TestMethod]
        public void DetailsThatRepeatTheMessageAreLeftOut()
        {
            Assert.IsNull(CreateMessagePage("Export failed.", "Export failed.\r\n").Expander);
            Assert.IsNull(CreateMessagePage("Export failed.", null).Expander);
        }

        [TestMethod]
        public void LongDetailsAreCut()
        {
            int max = (int)DialogsType.GetField("MaxDetailLines", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            string details = string.Join("\r\n", Enumerable.Range(1, max + 10).Select(i => $"line {i}"));

            string[] lines = CreateMessagePage("Failed.", details).Expander.Text.Split(new[] { "\r\n" }, StringSplitOptions.None);

            Assert.AreEqual(max + 1, lines.Length);
            Assert.AreEqual($"line {max}", lines[max - 1]);
            Assert.AreEqual("…", lines[max]);
        }
    }
}
