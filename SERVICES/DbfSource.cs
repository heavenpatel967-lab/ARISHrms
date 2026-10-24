using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    /// <summary>
    /// Knows where the EasyPay DBF files are. The folder is the one the user picked in
    /// Employee Master -> "Sync DBFS Folder" (remembered between runs).
    /// </summary>
    public static class DbfSource
    {
        public const string NotFoundHelp =
            "Pick your DBFS folder once:\n\nEmployee Master  ->  Sync DBFS Folder\n\n(the folder that has mstsal.DBF and monsum.DBF)";

        public static string Folder => DbfFolderScanner.GetLastFolder();

        /// <summary>Folders to look in, best first: the remembered DBFS folder, then the folder of the chosen MONDATA file.</summary>
        public static List<string> CandidateFolders()
        {
            var list = new List<string>();
            void Add(string f)
            {
                if (string.IsNullOrWhiteSpace(f)) return;
                try { f = Path.GetFullPath(f); } catch { return; }
                if (Directory.Exists(f) && !list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f);
            }

            Add(Folder);
            try
            {
                using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
                c.Open();
                string md = c.QueryFirstOrDefault<string>("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'MonDataPath'");
                if (!string.IsNullOrWhiteSpace(md)) Add(Path.GetDirectoryName(md));
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Finds e.g. monsum.DBF or mstsal.DBF (any letter case; the copy closest to the folder wins).
        /// Looks in every candidate folder; when it is found somewhere other than the remembered DBFS folder,
        /// that folder is remembered, so all screens use the same place from then on.
        /// </summary>
        public static string Find(string fileNameWithoutExtension)
        {
            var folders = CandidateFolders();
            for (int i = 0; i < folders.Count; i++)
            {
                string hit = FindIn(folders[i], fileNameWithoutExtension);
                if (hit == null) continue;
                if (i > 0 || string.IsNullOrWhiteSpace(Folder)) DbfFolderScanner.SaveLastFolder(folders[i]);
                return hit;
            }
            return null;
        }

        /// <summary>"monsum.DBF not found in D:\salary\... " - says exactly where it looked.</summary>
        public static string NotFoundMessage(string fileName)
        {
            var folders = CandidateFolders();
            return folders.Count == 0
                ? $"{fileName} not found - no DBFS folder chosen yet. " + NotFoundHelp.Replace("\n\n", " ").Replace("\n", " ")
                : $"{fileName} not found in: {string.Join("  /  ", folders)}. Choose the folder that contains it in Employee Master -> Sync DBFS Folder.";
        }

        public static string FindIn(string folder, string fileNameWithoutExtension)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                IgnoreInaccessible = true
            };

            return Directory.EnumerateFiles(folder, fileNameWithoutExtension + ".dbf", options)
                .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .FirstOrDefault();
        }

        // ---- MONDATA location is remembered separately: the old app let the user browse to it ----
        public static string GetMonDataPath()
        {
            try
            {
                using (var c = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    c.Open();
                    string saved = c.QueryFirstOrDefault<string>("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'MonDataPath'");
                    if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved)) return saved;
                }
            }
            catch { }
            return Find("mondata");
        }

        public static void SaveMonDataPath(string path)
        {
            try
            {
                using (var c = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    c.Open();
                    c.Execute("INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue) VALUES ('MonDataPath', @p)", new { p = path });
                }
            }
            catch { }
        }
    }

    /// <summary>Per-employee monthly advance limit (default 5000), same behaviour as the old app.</summary>
    public static class AdvanceLimitStore
    {
        public const double DefaultLimit = 5000;

        /// <summary>Employees are stored as 4-digit codes; the same number is the same person ("7" == "0007").</summary>
        public static string Key(string empCode) => EmployeeLookup.Key(empCode);

        public static double Get(string empCode)
        {
            try
            {
                using (var c = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    c.Open();
                    var v = c.QueryFirstOrDefault<double?>("SELECT LimitAmount FROM AdvanceLimits WHERE EmpCode = @k", new { k = Key(empCode) });
                    return v ?? DefaultLimit;
                }
            }
            catch { return DefaultLimit; }
        }

        public static void Set(string empCode, double limit)
        {
            using (var c = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                c.Open();
                c.Execute("INSERT OR REPLACE INTO AdvanceLimits (EmpCode, LimitAmount) VALUES (@k, @l)", new { k = Key(empCode), l = limit });
            }
        }

        public class ImportResult { public int Updated; public int Skipped; public List<(string code, double limit)> Rows = new List<(string, double)>(); }

        /// <summary>
        /// Parses the old app's "Advance Limits" CSV. It looks for a header line (within the first 10 lines)
        /// containing CARD and ADV; otherwise column 2 = card number and column 4 = limit. The card number is
        /// used as the employee code, padded to 4 digits. Rows with a non-numeric card are ignored; rows whose
        /// limit is empty or text are counted as skipped.
        /// </summary>
        public static ImportResult ParseCsv(string[] lines)
        {
            var result = new ImportResult();
            int cardIdx = 1, advIdx = 3, startRow = 1;

            for (int i = 0; i < Math.Min(10, lines.Length); i++)
            {
                string upper = lines[i].ToUpperInvariant();
                if (upper.Contains("CARD") && upper.Contains("ADV"))
                {
                    var headers = lines[i].Split(',');
                    for (int c = 0; c < headers.Length; c++)
                    {
                        string h = headers[c].ToUpperInvariant().Trim();
                        if (h.Contains("CARD")) cardIdx = c;
                        else if (h.Contains("ADV")) advIdx = c;
                    }
                    startRow = i + 1;
                    break;
                }
            }

            for (int i = startRow; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = lines[i].Split(',');
                if (cols.Length <= Math.Max(cardIdx, advIdx)) continue;

                string rawCard = cols[cardIdx].Trim();
                string rawAdv = cols[advIdx].Trim();
                if (string.IsNullOrWhiteSpace(rawCard) || !int.TryParse(rawCard, out _)) continue;

                string empCode = rawCard.PadLeft(4, '0');
                if (decimal.TryParse(rawAdv, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var limit))
                {
                    result.Rows.Add((empCode, (double)Math.Round(limit, 0, MidpointRounding.AwayFromZero)));
                    result.Updated++;
                }
                else result.Skipped++;
            }
            return result;
        }

        public static ImportResult ImportCsv(string path)
        {
            var result = ParseCsv(File.ReadAllLines(path));
            using (var c = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                c.Open();
                using (var t = c.BeginTransaction())
                {
                    foreach (var (code, limit) in result.Rows)
                        c.Execute("INSERT OR REPLACE INTO AdvanceLimits (EmpCode, LimitAmount) VALUES (@k, @l)",
                                  new { k = Key(code), l = limit }, t);
                    t.Commit();
                }
            }
            return result;
        }
    }
}
