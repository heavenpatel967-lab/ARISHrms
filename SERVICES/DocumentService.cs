using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    public class EmployeeDocument
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public string DocCategory { get; set; }
        public string DocName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadDate { get; set; } = DateTime.Now;
        public DateTime? ExpiryDate { get; set; }
        public bool IsExpired { get; set; } = false;
    }

    public static class DocumentService
    {
        private static readonly string DocumentStorageRoot = Path.Combine(
            Directory.GetCurrentDirectory(), "Employees", "Documents");

        static DocumentService()
        {
            // Ensure storage directory exists
            Directory.CreateDirectory(DocumentStorageRoot);
        }

        /// <summary>
        /// Upload employee document
        /// </summary>
        public static int UploadDocument(string empCode, string docCategory, string docName, 
                                        string sourceFilePath, DateTime? expiryDate = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Create employee folder
                        string empFolder = Path.Combine(DocumentStorageRoot, empCode);
                        Directory.CreateDirectory(empFolder);

                        // Copy file to storage
                        string fileName = Path.GetFileName(sourceFilePath);
                        string destFilePath = Path.Combine(empFolder, fileName);

                        // Handle duplicate filenames
                        int counter = 1;
                        while (File.Exists(destFilePath))
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(sourceFilePath);
                            string ext = Path.GetExtension(sourceFilePath);
                            fileName = $"{nameWithoutExt}_{counter}{ext}";
                            destFilePath = Path.Combine(empFolder, fileName);
                            counter++;
                        }

                        File.Copy(sourceFilePath, destFilePath, false);
                        long fileSize = new FileInfo(destFilePath).Length;

                        // Record in database
                        var doc = new EmployeeDocument
                        {
                            EmpCode = empCode,
                            DocCategory = docCategory,
                            DocName = docName,
                            FilePath = destFilePath,
                            FileSize = fileSize,
                            UploadDate = DateTime.Now,
                            ExpiryDate = expiryDate,
                            IsExpired = false
                        };

                        string insertSql = @"
                            INSERT INTO EmployeeDocuments (EmpCode, DocCategory, DocName, FilePath, FileSize, UploadDate, ExpiryDate, IsExpired)
                            VALUES (@EmpCode, @DocCategory, @DocName, @FilePath, @FileSize, @UploadDate, @ExpiryDate, @IsExpired);
                            SELECT last_insert_rowid();";

                        int docId = connection.QuerySingle<int>(insertSql, doc, transaction);
                        transaction.Commit();

                        return docId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error uploading document: {ex.Message}", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Download document from storage
        /// </summary>
        public static byte[] DownloadDocument(int documentId, out string fileName)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var doc = connection.QuerySingleOrDefault<EmployeeDocument>(
                    "SELECT * FROM EmployeeDocuments WHERE Id = @Id",
                    new { Id = documentId });

                if (doc == null || !File.Exists(doc.FilePath))
                {
                    fileName = null;
                    return null;
                }

                fileName = doc.DocName;
                return File.ReadAllBytes(doc.FilePath);
            }
        }

        /// <summary>
        /// Get all documents for an employee
        /// </summary>
        public static List<EmployeeDocument> GetEmployeeDocuments(string empCode, string category = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                string query = category == null ?
                    "SELECT * FROM EmployeeDocuments WHERE EmpCode = @EmpCode ORDER BY UploadDate DESC" :
                    "SELECT * FROM EmployeeDocuments WHERE EmpCode = @EmpCode AND DocCategory = @Category ORDER BY UploadDate DESC";

                object param = category == null ?
                    (object)new { EmpCode = empCode } :
                    new { EmpCode = empCode, Category = category };

                return connection.Query<EmployeeDocument>(query, param).ToList();
            }
        }

        /// <summary>
        /// Delete document
        /// </summary>
        public static bool DeleteDocument(int documentId)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var doc = connection.QuerySingleOrDefault<EmployeeDocument>(
                    "SELECT * FROM EmployeeDocuments WHERE Id = @Id",
                    new { Id = documentId });

                if (doc == null)
                    return false;

                try
                {
                    // Delete file from storage
                    if (File.Exists(doc.FilePath))
                        File.Delete(doc.FilePath);

                    // Delete from database
                    connection.Execute("DELETE FROM EmployeeDocuments WHERE Id = @Id", new { Id = documentId });

                    return true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error deleting document: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Get documents expiring soon (within specified days)
        /// </summary>
        public static List<EmployeeDocument> GetExpiringDocuments(int daysThreshold = 30)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                DateTime targetDate = DateTime.Now.AddDays(daysThreshold);

                return connection.Query<EmployeeDocument>(
                    @"SELECT * FROM EmployeeDocuments 
                      WHERE ExpiryDate IS NOT NULL 
                      AND ExpiryDate <= @TargetDate
                      AND ExpiryDate > @Today
                      AND IsExpired = 0
                      ORDER BY ExpiryDate ASC",
                    new { TargetDate = targetDate, Today = DateTime.Now }).ToList();
            }
        }

        /// <summary>
        /// Get expired documents
        /// </summary>
        public static List<EmployeeDocument> GetExpiredDocuments()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<EmployeeDocument>(
                    @"SELECT * FROM EmployeeDocuments 
                      WHERE ExpiryDate IS NOT NULL 
                      AND ExpiryDate < @Today
                      ORDER BY ExpiryDate DESC",
                    new { Today = DateTime.Now.Date }).ToList();
            }
        }

        /// <summary>
        /// Update document expiry status
        /// </summary>
        public static void UpdateExpiryStatus(int documentId)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var doc = connection.QuerySingleOrDefault<EmployeeDocument>(
                    "SELECT * FROM EmployeeDocuments WHERE Id = @Id",
                    new { Id = documentId });

                if (doc?.ExpiryDate == null)
                    return;

                bool isExpired = doc.ExpiryDate < DateTime.Now.Date;

                connection.Execute(
                    "UPDATE EmployeeDocuments SET IsExpired = @IsExpired WHERE Id = @Id",
                    new { IsExpired = isExpired ? 1 : 0, Id = documentId });
            }
        }

        /// <summary>
        /// Get document statistics
        /// </summary>
        public static (int TotalDocuments, int ExpiringSoon, int Expired) GetDocumentStats(int expiringThreshold = 30)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                int totalDocs = connection.QuerySingle<int>("SELECT COUNT(*) FROM EmployeeDocuments");

                int expiringDocs = connection.QuerySingle<int>(
                    @"SELECT COUNT(*) FROM EmployeeDocuments 
                      WHERE ExpiryDate IS NOT NULL 
                      AND ExpiryDate <= DATE('now', @Days)
                      AND ExpiryDate > DATE('now')
                      AND IsExpired = 0",
                    new { Days = $"+{expiringThreshold} days" });

                int expiredDocs = connection.QuerySingle<int>(
                    @"SELECT COUNT(*) FROM EmployeeDocuments 
                      WHERE ExpiryDate IS NOT NULL 
                      AND ExpiryDate < DATE('now')");

                return (totalDocs, expiringDocs, expiredDocs);
            }
        }

        /// <summary>
        /// Get documents by category (for compliance reporting)
        /// </summary>
        public static List<EmployeeDocument> GetDocumentsByCategory(string category)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<EmployeeDocument>(
                    @"SELECT * FROM EmployeeDocuments 
                      WHERE DocCategory = @Category
                      ORDER BY EmpCode, UploadDate DESC",
                    new { Category = category }).ToList();
            }
        }

        /// <summary>
        /// Archive old documents (move to archive folder)
        /// </summary>
        public static int ArchiveOldDocuments(int olderthanMonths = 12)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                DateTime archiveThreshold = DateTime.Now.AddMonths(-olderthanMonths);

                var oldDocs = connection.Query<EmployeeDocument>(
                    @"SELECT * FROM EmployeeDocuments 
                      WHERE UploadDate < @Threshold",
                    new { Threshold = archiveThreshold }).ToList();

                string archiveRoot = Path.Combine(DocumentStorageRoot, "Archive");
                Directory.CreateDirectory(archiveRoot);

                int archivedCount = 0;

                foreach (var doc in oldDocs)
                {
                    try
                    {
                        if (File.Exists(doc.FilePath))
                        {
                            string archivePath = Path.Combine(archiveRoot, $"{doc.EmpCode}_{doc.Id}_{Path.GetFileName(doc.FilePath)}");
                            File.Move(doc.FilePath, archivePath, true);
                            archivedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error archiving document {doc.Id}: {ex.Message}");
                    }
                }

                return archivedCount;
            }
        }
    }
}
