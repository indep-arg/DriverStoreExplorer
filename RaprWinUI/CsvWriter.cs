using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Rapr.Utils;

namespace Rapr.WinUI
{
    /// <summary>
    /// Writes the driver list as CSV in the user's list separator: a BOM so Excel reads it as UTF-8,
    /// and text that could start a spreadsheet formula escaped. The same rules as CsvExporter in the
    /// WinForms app, which writes through its own SaveFileDialog.
    /// </summary>
    public static class CsvWriter
    {
        private const string CsvQuote = "\"";

        // Spreadsheet apps treat a cell starting with one of these as a formula. Provider and
        // device names come from third-party INF files, so they shouldn't be able to become one.
        private static readonly char[] FormulaPrefixes = { '=', '+', '-', '@', '\t', '\r' };

        public static void Write(string path, IEnumerable<DriverStoreEntry> driverStoreEntries)
        {
            var csvDelimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator;

            using (FileStream fileStream = new FileStream(path, FileMode.Create))
            // Excel only reads the file as UTF-8 when it starts with a BOM; without it,
            // non-ASCII provider and device names are garbled.
            using (StreamWriter fileWriter = new StreamWriter(fileStream, new UTF8Encoding(true)))
            {
                fileWriter.WriteLine(string.Join(csvDelimiter, Sanitize(DriverStoreEntry.GetFieldNames(), csvDelimiter)));

                foreach (DriverStoreEntry entry in driverStoreEntries)
                {
                    fileWriter.WriteLine(string.Join(csvDelimiter, Sanitize(entry.GetFieldValues(), csvDelimiter)));
                }
            }
        }

        internal static string[] Sanitize(string[] values, string csvDelimiter)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                {
                    values[i] = string.Empty;
                    continue;
                }

                if (values[i].Length > 0
                    && Array.IndexOf(FormulaPrefixes, values[i][0]) >= 0
                    && !double.TryParse(values[i], NumberStyles.Float, CultureInfo.CurrentCulture, out _))
                {
                    values[i] = "'" + values[i];
                }

                if (values[i].Contains(csvDelimiter)
                    || values[i].Contains(CsvQuote)
                    || values[i].Contains("\r")
                    || values[i].Contains("\n"))
                {
                    values[i] = CsvQuote + values[i].Replace(CsvQuote, CsvQuote + CsvQuote) + CsvQuote;
                }
            }

            return values;
        }
    }
}
