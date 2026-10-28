using System;
using System.Diagnostics;
using System.Drawing;
using System.Formats.Nrbf;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;

using BrightIdeasSoftware;

namespace Rapr
{
    /// <summary>
    /// ObjectListView's icons (sort arrows, filter images) are stored as BinaryFormatter-serialized
    /// bitmaps, which .NET 9 and later refuse to read, so its static constructor throws and no list
    /// can be created. The bitmaps are just PNG data, so this reads them with the NRBF decoder, which
    /// parses the format without deserializing objects, and gives ObjectListView that resource
    /// manager before any of its controls is created.
    /// </summary>
    internal static class ObjectListViewResources
    {
        private const string ResourcesTypeName = "BrightIdeasSoftware.Properties.Resources";

        [ModuleInitializer]
        internal static void Install()
        {
            Type? resources = typeof(ObjectListView).Assembly.GetType(ResourcesTypeName);
            FieldInfo? resourceManager = resources?.GetField("resourceMan", BindingFlags.Static | BindingFlags.NonPublic);

            if (resources == null || resourceManager == null)
            {
                Trace.TraceWarning("ObjectListView's resource manager was not found; its icons may fail to load.");
                return;
            }

            resourceManager.SetValue(null, new NrbfBitmapResourceManager(ResourcesTypeName, resources.Assembly));
        }

        /// <summary>
        /// Returns the PNG data inside a BinaryFormatter-serialized System.Drawing.Bitmap.
        /// </summary>
        internal static byte[] ReadSerializedBitmap(byte[] serializedBitmap)
        {
            var record = (ClassRecord)NrbfDecoder.Decode(new MemoryStream(serializedBitmap));
            if (record.TypeName.FullName != "System.Drawing.Bitmap")
            {
                throw new InvalidDataException($"Expected a serialized bitmap, found {record.TypeName.FullName}.");
            }

            return (record.GetArrayRecord("Data") as SZArrayRecord<byte>)?.GetArray()
                ?? throw new InvalidDataException("The serialized bitmap has no data.");
        }

        private sealed class NrbfBitmapResourceManager : ResourceManager
        {
            private readonly Assembly assembly;

            public NrbfBitmapResourceManager(string baseName, Assembly assembly)
                : base(baseName, assembly)
            {
                this.assembly = assembly;
            }

            public override object? GetObject(string name, CultureInfo? culture)
            {
                using (Stream stream = this.assembly.GetManifestResourceStream(this.BaseName + ".resources")
                    ?? throw new MissingManifestResourceException(this.BaseName))
                using (var reader = new ResourceReader(stream))
                {
                    reader.GetResourceData(name, out _, out byte[] data);

                    // GDI+ needs the stream for as long as the bitmap lives, so it isn't disposed.
                    return new Bitmap(new MemoryStream(ReadSerializedBitmap(data)));
                }
            }
        }
    }
}
