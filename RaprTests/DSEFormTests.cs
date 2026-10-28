using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;

using BrightIdeasSoftware;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rapr.Lang;
using Rapr.Utils;

namespace Rapr.Tests
{
    // These tests build WinForms controls and pump window messages. Run them on their own
    // instead of alongside the other test classes.
    [STATestClass]
    [DoNotParallelize]
    public class DSEFormTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SuccessfulDriverExportsClearTheReminderOnlyForBackedUpDrivers(bool exportAllSelected)
        {
            WithOperationForm((form, list) =>
            {
                SetField(form, "driverStore", new ExportTestDriverStore());
                var first = CreateDriver("first.inf", 1);
                var second = CreateDriver("second.inf", 1);
                list.SetObjects(new[] { first, second });
                Invoke(form, "CtxMenuSelectUnusedDrivers_Click", null, EventArgs.Empty);

                var exported = exportAllSelected ? new[] { first, second } : new[] { first };
                var task = (Task)typeof(DSEForm)
                    .GetMethod("ExportDrivers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { exported, @"C:\TestBackup" });
                var timeout = Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    Application.DoEvents();
                    Assert.IsTrue(timeout.Elapsed < TimeSpan.FromSeconds(10), "Driver export did not complete.");
                }

                task.GetAwaiter().GetResult();
                var status = (ToolStripStatusLabel)typeof(DSEForm)
                    .GetField("lblStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                string reminder = Language.ResourceManager.GetString("Status_Review_Unused_Drivers", Language.Culture);
                Assert.AreEqual(!exportAllSelected, status.Text.Contains(reminder));
                Assert.AreEqual(exportAllSelected ? Color.LightGreen : Color.Yellow, status.BackColor);

                Invoke(form, "UpdateCheckedItemSize");

                Assert.AreEqual(!exportAllSelected, status.Text.Contains(reminder));
                list.CheckedObjects = new[] { first };
                Invoke(form, "UpdateCheckedItemSize");

                Assert.IsFalse(status.Text.Contains(reminder));
            });
        }

        [TestMethod]
        [DataRow(DSEForm.Status.Normal, false)]
        [DataRow(DSEForm.Status.Success, true)]
        [DataRow(DSEForm.Status.Error, true)]
        public void OperationResultsKeepTheUnusedDriverReminderWithoutHidingErrors(DSEForm.Status result, bool operation)
        {
            WithOperationForm((form, list) =>
            {
                list.SetObjects(new[] { CreateDriver("unused.inf", 1) });
                Invoke(form, "CtxMenuSelectUnusedDrivers_Click", null, EventArgs.Empty);
                if (operation)
                {
                    Invoke(form, "StartOperation");
                }

                string message = result == DSEForm.Status.Error
                    ? Language.Message_Export_Drivers_Error
                    : Language.Message_Export_Drivers_Success;
                Invoke(form, "ShowStatus", result, message, null, false);
                if (operation)
                {
                    Invoke(form, "EndOperation");
                }

                var status = (ToolStripStatusLabel)typeof(DSEForm)
                    .GetField("lblStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                string reminder = Language.ResourceManager.GetString("Status_Review_Unused_Drivers", Language.Culture);
                StringAssert.Contains(status.Text, message);
                StringAssert.Contains(status.Text, reminder);
                Assert.AreEqual(
                    result == DSEForm.Status.Error ? Color.FromArgb(0xFF, 0x00, 0x33) : Color.Yellow,
                    status.BackColor);
            });
        }

        [TestMethod]
        public void UnusedDriverReviewWarningSurvivesSelectionUpdates()
        {
            WithOperationForm((form, list) =>
            {
                var unused = CreateDriver("unused.inf", 1);
                var active = CreateDriver("active.inf", 1);
                active.DeviceName = "Active device";
                list.SetObjects(new[] { unused, active });
                var status = (ToolStripStatusLabel)typeof(DSEForm)
                    .GetField("lblStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                string reminder = Language.ResourceManager.GetString("Status_Review_Unused_Drivers", Language.Culture);

                Invoke(form, "CtxMenuSelectUnusedDrivers_Click", null, EventArgs.Empty);

                Assert.AreEqual(Color.Yellow, status.BackColor);
                StringAssert.Contains(status.Text, reminder);

                list.CheckedObjects = new[] { unused, active };
                Invoke(form, "UpdateCheckedItemSize");

                Assert.AreEqual(Color.Yellow, status.BackColor);
                StringAssert.Contains(status.Text, reminder);
                StringAssert.Contains(
                    status.Text,
                    string.Format(Language.Status_Selected_Drivers, 2, DriverStoreEntry.GetBytesReadable(0)));

                list.CheckedObjects = new[] { active };
                Invoke(form, "UpdateCheckedItemSize");

                Assert.AreNotEqual(Color.Yellow, status.BackColor);
                Assert.IsFalse(status.Text.Contains(reminder));

                list.CheckedObjects = Array.Empty<DriverStoreEntry>();
                Invoke(form, "UpdateCheckedItemSize");

                Assert.AreEqual(Language.Status_No_Drivers_Selected, status.Text);
                Assert.AreNotEqual(Color.Yellow, status.BackColor);
            });
        }

        [TestMethod]
        [DataRow("CtxMenuSelectUnusedDrivers_Click", false)]
        [DataRow("ButtonSelectUnusedDrivers_Click", false)]
        [DataRow("CtxMenuSelectUnusedDrivers_Click", true)]
        [DataRow("ButtonSelectUnusedDrivers_Click", true)]
        public void UnusedDriverSelectionRequiresConfirmationBeforeChangingChecks(string handler, bool confirmed)
        {
            WithSelectionForm((form, list) =>
            {
                var active = CreateDriver("active.inf", 1);
                active.DeviceName = "Active device";
                var unused = CreateDriver("unused.inf", 1);
                list.SetObjects(new[] { active, unused });
                list.CheckedObjects = new[] { active };
                var testForm = (SelectionTestForm)form;
                testForm.SelectionConfirmed = confirmed;
                testForm.OnConfirmation = () =>
                    CollectionAssert.AreEquivalent(new[] { active }, list.CheckedObjects.Cast<DriverStoreEntry>().ToArray());

                Invoke(form, handler, null, EventArgs.Empty);

                Assert.AreEqual(1, testForm.ConfirmationCount);
                CollectionAssert.AreEquivalent(
                    new[] { confirmed ? unused : active },
                    list.CheckedObjects.Cast<DriverStoreEntry>().ToArray());
            });
        }

        [TestMethod]
        [DataRow("CtxMenuSelectUnusedDrivers_Click")]
        [DataRow("ButtonSelectUnusedDrivers_Click")]
        public void DisabledUnusedDriverWarningSelectsWithoutConfirmation(string handler)
        {
            // Settings is internal to Rapr, so reach it through ApplicationSettingsBase.
            var settings = (ApplicationSettingsBase)typeof(DSEForm).Assembly
                .GetType("Rapr.Properties.Settings")
                .GetProperty("Default", BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);
            object previous = settings["WarnBeforeSelectingUnusedDrivers"];
            settings["WarnBeforeSelectingUnusedDrivers"] = false;

            try
            {
                WithSelectionForm((form, list) =>
                {
                    var unused = CreateDriver("unused.inf", 1);
                    list.SetObjects(new[] { unused });

                    Invoke(form, handler, null, EventArgs.Empty);

                    Assert.AreEqual(0, ((SelectionTestForm)form).ConfirmationCount);
                    CollectionAssert.AreEquivalent(new[] { unused }, list.CheckedObjects.Cast<DriverStoreEntry>().ToArray());
                });
            }
            finally
            {
                settings["WarnBeforeSelectingUnusedDrivers"] = previous;
            }
        }

        [TestMethod]
        [DataRow(true, false)]
        [DataRow(false, true)]
        public void UnavailableUnusedDriverSelectionDoesNotShowConfirmation(bool operationInProgress, bool offline)
        {
            WithSelectionForm((form, list) =>
            {
                list.SetObjects(new[] { CreateDriver("unused.inf", 1) });
                SetField(form, "operationInProgress", operationInProgress);
                if (offline)
                {
                    SetField(form, "driverStore", new DismUtil(@"C:\ReviewImage"));
                }

                Invoke(form, "CtxMenuSelectUnusedDrivers_Click", null, EventArgs.Empty);

                Assert.AreEqual(0, ((SelectionTestForm)form).ConfirmationCount);
            });
        }

        [TestMethod]
        [DataRow("CtxMenuSelectOldDrivers_Click")]
        [DataRow("CtxMenuSelectUnusedDrivers_Click")]
        public void AssociatedDriversWithoutDisplayNamesAreNotCleanupCandidates(string handler)
        {
            WithSelectionForm((form, list) =>
            {
                var associated = CreateDriver("driver.inf", 1);
                associated.DeviceId = "ACTIVE-DEVICE";
                associated.DevicePresent = true;
                list.SetObjects(new[] { associated, CreateDriver("driver.inf", 2) });
                Invoke(form, handler, null, EventArgs.Empty);
                Assert.IsFalse(list.CheckedObjects.Contains(associated));
            });
        }

        [TestMethod]
        public void PendingSelectionUpdateCannotUnlockARunningOperation()
        {
            WithOperationForm((form, list) =>
            {
                SetField(form, "driverStore", new NativeDriverStore());
                var driver = CreateDriver("driver.inf", 1);
                list.SetObjects(new[] { driver });
                list.CheckedObjects = new[] { driver };

                Invoke(form, "StartOperation");
                Invoke(form, "UpdateCheckedItemSize");

                foreach (string name in new[] { "buttonDeleteDriver", "buttonExportDrivers", "cbForceDeletion", "exportSelectedDriverListToolStripMenuItem" })
                {
                    Assert.IsFalse(IsActionEnabled(form, name), name);
                }

                var opening = new System.ComponentModel.CancelEventArgs();
                Invoke(form, "ContextMenuStrip_Opening", null, opening);
                Assert.IsTrue(opening.Cancel);

                Invoke(form, "EndOperation");

                foreach (string name in new[] { "buttonDeleteDriver", "buttonExportDrivers", "cbForceDeletion", "exportSelectedDriverListToolStripMenuItem" })
                {
                    Assert.IsTrue(IsActionEnabled(form, name), name);
                }
            });
        }

        [TestMethod]
        public void SelectionUpdateDoesNotEnableUnsupportedOfflineActions()
        {
            WithOperationForm((form, list) =>
            {
                SetField(form, "driverStore", new DismUtil(@"C:\ReviewImage"));
                var driver = CreateDriver("driver.inf", 1);
                list.SetObjects(new[] { driver });
                list.CheckedObjects = new[] { driver };

                Invoke(form, "UpdateCheckedItemSize");

                Assert.IsTrue(IsActionEnabled(form, "buttonDeleteDriver"));
                Assert.IsFalse(IsActionEnabled(form, "cbForceDeletion"));
                Assert.IsFalse(IsActionEnabled(form, "buttonExportDrivers"));

                Invoke(form, "EndOperation");

                Assert.IsFalse(IsActionEnabled(form, "buttonSelectOldDrivers"));
                Assert.IsFalse(IsActionEnabled(form, "buttonSelectUnusedDrivers"));

                SetField(form, "driverStore", new PnpUtil());
                Invoke(form, "EndOperation");
                Assert.IsFalse(IsActionEnabled(form, "buttonExportAllDrivers"));
            });
        }

        [TestMethod]
        [DataRow("CtxMenuSelectOldDrivers_Click")]
        [DataRow("CtxMenuSelectUnusedDrivers_Click")]
        public void CleanupSelectionClearsStaleChecksWhenNoCandidatesExist(string handler)
        {
            WithSelectionForm((form, list) =>
            {
                var active = CreateDriver("active.inf", 1);
                active.DeviceName = "Active device";
                list.SetObjects(new[] { active });
                list.CheckedObjects = new[] { active };

                Invoke(form, handler, null, EventArgs.Empty);

                Assert.AreEqual(0, list.CheckedObjects.Count);
            });
        }

        [TestMethod]
        [DataRow("CtxMenuSelectOldDrivers_Click")]
        [DataRow("CtxMenuSelectUnusedDrivers_Click")]
        public void CleanupSelectionReplacesStaleChecksWithTheMatchingCandidates(string handler)
        {
            WithSelectionForm((form, list) =>
            {
                var active = CreateDriver("driver.inf", 2);
                active.DeviceName = "Active device";
                var unused = CreateDriver("driver.inf", 1);
                list.SetObjects(new[] { active, unused });
                list.CheckedObjects = new[] { active };

                Invoke(form, handler, null, EventArgs.Empty);

                CollectionAssert.AreEquivalent(new[] { unused }, list.CheckedObjects.Cast<DriverStoreEntry>().ToArray());
            });
        }

        [TestMethod]
        [DataRow("[Unknown]")]
        [DataRow(null)]
        [DataRow("")]
        public void OldDriverSelectionDoesNotMergeUnresolvedInfIdentities(string unresolvedInfName)
        {
            WithSelectionForm((form, list) =>
            {
                var unresolvedOld = CreateDriver(unresolvedInfName, 1);
                var unresolvedNew = CreateDriver(unresolvedInfName, 2);
                var resolvedOld = CreateDriver("resolved.inf", 1);
                var resolvedNew = CreateDriver("resolved.inf", 2);
                list.SetObjects(new[] { unresolvedOld, unresolvedNew, resolvedOld, resolvedNew });

                Invoke(form, "CtxMenuSelectOldDrivers_Click", null, EventArgs.Empty);

                CollectionAssert.AreEquivalent(new[] { resolvedOld }, list.CheckedObjects.Cast<DriverStoreEntry>().ToArray());
            });
        }

        [TestMethod]
        public void TypedDriverSelectionCanBeToggledAndInverted()
        {
            WithSelectionForm((form, list) =>
            {
                var first = CreateDriver("first.inf", 1);
                var second = CreateDriver("second.inf", 1);
                var third = CreateDriver("third.inf", 1);
                list.CreateControl();
                list.SetObjects(new[] { first, second, third });
                list.SelectedObjects = new[] { first, second };
                list.CheckedObjects = new[] { first, second };

                Invoke(form, "CtxMenuSelect_Click", null, EventArgs.Empty);
                Assert.AreEqual(0, list.CheckedObjects.Count);

                Invoke(form, "CtxMenuSelect_Click", null, EventArgs.Empty);
                CollectionAssert.AreEquivalent(new[] { first, second }, list.CheckedObjects.ToArray());

                Invoke(form, "CtxMenuInvertSelection_Click", null, EventArgs.Empty);
                CollectionAssert.AreEquivalent(new[] { third }, list.CheckedObjects.ToArray());
            });
        }

        [TestMethod]
        public void CheckBoxesCanBeTurnedOnAfterTheListHasAWindow()
        {
            // Like the main form: the list's window already exists when checkboxes are turned on,
            // and attaching the state image list makes the native list view recreate itself.
            using (var list = new MyObjectListView { UseCompatibleStateImageBehavior = false })
            {
                Assert.AreNotEqual(IntPtr.Zero, list.Handle);

                list.CheckBoxes = true;

                Assert.AreEqual(2, list.StateImageList.Images.Count);
                list.SetObjects(new[] { CreateDriver("first.inf", 1) });
                list.CheckedObjects = list.Objects.Cast<DriverStoreEntry>().ToList();
                Assert.HasCount(1, list.CheckedObjects);
            }
        }

        [TestMethod]
        public void ViewStateRoundTripsIncludingTheSecondarySort()
        {
            using (var previous = CreateListWithColumns())
            using (var current = CreateListWithColumns())
            {
                previous.AllColumns[0].Width = 173;
                previous.AllColumns[1].IsVisible = false;
                previous.PrimarySortColumn = previous.AllColumns[0];
                previous.PrimarySortOrder = SortOrder.Descending;
                previous.SecondarySortColumn = previous.AllColumns[1];
                previous.SecondarySortOrder = SortOrder.Ascending;
                previous.ShowGroups = false;

                Assert.IsTrue(current.RestoreState(previous.SaveState()));
                Assert.AreEqual(173, current.AllColumns[0].Width);
                Assert.IsFalse(current.AllColumns[1].IsVisible);
                Assert.AreSame(current.AllColumns[0], current.PrimarySortColumn);
                Assert.AreEqual(SortOrder.Descending, current.PrimarySortOrder);
                Assert.AreSame(current.AllColumns[1], current.SecondarySortColumn);
                Assert.AreEqual(SortOrder.Ascending, current.SecondarySortOrder);
                Assert.IsFalse(current.ShowGroups);
            }
        }

        [TestMethod]
        public void ViewStateFromOlderVersionsIsIgnored()
        {
            using (var list = CreateListWithColumns())
            {
                // Older versions saved a BinaryFormatter stream, which starts with these bytes.
                byte[] binaryFormatterState = { 0x00, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };

                Assert.IsFalse(list.RestoreState(binaryFormatterState));
                Assert.IsFalse(list.RestoreState(Array.Empty<byte>()));
                Assert.AreEqual(2, list.AllColumns.Count);
            }
        }

        private static MyObjectListView CreateListWithColumns()
        {
            var list = new MyObjectListView();
            foreach (string aspect in new[] { nameof(DriverStoreEntry.DriverInfName), nameof(DriverStoreEntry.DriverClass) })
            {
                var column = new OLVColumn(aspect, aspect);
                list.AllColumns.Add(column);
                list.Columns.Add(column);
            }

            return list;
        }

        private static DriverStoreEntry CreateDriver(string infName, int version)
        {
            return new DriverStoreEntry
            {
                DriverInfName = infName,
                DriverClass = "System",
                DriverPkgProvider = "Test provider",
                DriverVersion = new Version(version, 0),
                DriverDate = new DateTime(2026, 1, 1)
            };
        }

        private static void WithSelectionForm(Action<DSEForm, MyObjectListView> test)
        {
            // Avoid startup/elevation; only the in-memory selection controls are exercised.
            var form = (SelectionTestForm)RuntimeHelpers.GetUninitializedObject(typeof(SelectionTestForm));
            GC.SuppressFinalize(form);
            form.SelectionConfirmed = true;

            using (var list = new MyObjectListView { CheckBoxes = true })
            using (var status = new ToolStripStatusLabel())
            {
                list.Columns.Add(new OLVColumn { AspectName = "DriverInfName" });
                SetField(form, "lstDriverStoreEntries", list);
                SetField(form, "lblStatus", status);
                SetField(form, "driverStore", new DismUtil());
                SetField(form, "driversWithNewerDate", new HashSet<DriverStoreEntry>());
                SetField(form, "unusedDriversToReview", new HashSet<DriverStoreEntry>());
                test(form, list);
            }
        }

        private static void SetField(DSEForm form, string name, object value)
        {
            typeof(DSEForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
        }

        private static void Invoke(DSEForm form, string name, params object[] arguments)
        {
            typeof(DSEForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, arguments);
        }

        private static void WithOperationForm(Action<DSEForm, MyObjectListView> test)
        {
            WithSelectionForm((form, list) =>
            {
                var controls = new List<IDisposable>();

                try
                {
                    foreach (var field in typeof(DSEForm).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    {
                        if (field.GetValue(form) == null
                            && (typeof(Control).IsAssignableFrom(field.FieldType) || typeof(ToolStripItem).IsAssignableFrom(field.FieldType)))
                        {
                            var control = Activator.CreateInstance(field.FieldType);
                            field.SetValue(form, control);
                            controls.Add((IDisposable)control);
                        }
                    }

                    test(form, list);
                }
                finally
                {
                    foreach (var control in controls)
                    {
                        control.Dispose();
                    }
                }
            });
        }

        private static bool IsActionEnabled(DSEForm form, string name)
        {
            var action = typeof(DSEForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            return action is Control control ? control.Enabled : ((ToolStripItem)action).Enabled;
        }

        private sealed class SelectionTestForm : DSEForm
        {
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool SelectionConfirmed { get; set; }

            public int ConfirmationCount { get; private set; }

            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Action OnConfirmation { get; set; }

            protected override bool ConfirmUnusedDriverSelection()
            {
                this.ConfirmationCount++;
                this.OnConfirmation?.Invoke();
                return this.SelectionConfirmed;
            }
        }

        private sealed class ExportTestDriverStore : IDriverStore
        {
            public DriverStoreType Type => DriverStoreType.Online;

            public string OfflineStoreLocation => null;

            public bool SupportAddInstall => false;

            public bool SupportForceDeletion => false;

            public bool SupportDeviceNameColumn => true;

            public bool SupportExportDriver => true;

            public bool SupportExportAllDrivers => true;

            public List<DriverStoreEntry> EnumeratePackages() => throw new NotSupportedException();

            public bool DeleteDriver(DriverStoreEntry entry, bool forceDelete) => throw new NotSupportedException();

            public bool AddDriver(string path, bool install) => throw new NotSupportedException();

            public bool ExportDriver(DriverStoreEntry entry, string destinationPath) => true;

            public bool ExportAllDrivers(string destinationPath) => throw new NotSupportedException();
        }
    }
}
