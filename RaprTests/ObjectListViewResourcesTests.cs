using System.Runtime.CompilerServices;

using BrightIdeasSoftware;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rapr.Tests
{
    [TestClass]
    public class ObjectListViewResourcesTests
    {
        [TestMethod]
        public void ObjectListViewIconsLoadWithoutBinaryFormatter()
        {
            // Rapr's module initializer installs the decoder; in the app it runs before any list exists.
            RuntimeHelpers.RunModuleConstructor(typeof(MyObjectListView).Module.ModuleHandle);

            // These are loaded by ObjectListView's static constructor, which used to throw on .NET 9+.
            Assert.AreEqual(16, ObjectListView.SortAscendingImage.Width);
            Assert.AreEqual(16, ObjectListView.SortDescendingImage.Height);
        }
    }
}
