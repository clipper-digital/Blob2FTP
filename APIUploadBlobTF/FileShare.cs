using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure;
using Serilog.Events;
using Microsoft.Extensions.Logging;
using Azure.Storage.Files.Shares;

namespace APIUploadBlobTF
{
    public class FileShare
    {
        #region SendAllFilesViaFileShare
        public static int SendAllFilesViaFileShare(ILogger log, APIRouting route, BlobContainerClient container, string prefix, int iSegmentSize)
        {
            List<FileStatus> fileStatuses = [];

            List<string> stringList = [];
            foreach (Page<BlobHierarchyItem> asPage in container.GetBlobsByHierarchy(delimiter: "/", prefix: prefix).AsPages(pageSizeHint: new int?(iSegmentSize)))
            {
                foreach (BlobHierarchyItem blobHierarchyItem in (IEnumerable<BlobHierarchyItem>)asPage.Values)
                {
                    if (blobHierarchyItem.IsPrefix)
                    {
                        FileShare.SendAllFilesViaFileShare(log, route, container, blobHierarchyItem.Prefix, iSegmentSize);
                    }
                    else
                    {
                        int length = blobHierarchyItem.Blob.Name.LastIndexOf('/');
                        if (length != -1)
                        {
                            blobHierarchyItem.Blob.Name.Substring(0, length);
                            string sFilename = blobHierarchyItem.Blob.Name.Substring(length + 1);
                            byte[] bData = APIUploadBlobTF.Blobs.ReadBlobData(log, container, blobHierarchyItem.Blob.Name);
                            if (bData.Length != 0)
                            {
                                if (!route.AzureFileShare)
                                {
                                    if (FileShare.SendToUNCFileShare(log, route, sFilename, bData))
                                    {
                                        fileStatuses.Add(new FileStatus() { Filename = sFilename, DestinationName = blobHierarchyItem.Blob.Name, DestinationPath = route.Path, DestinationDateTime = DateTime.Now });
                                        APIUploadBlobTF.Blobs.DeleteBlob(log, container, blobHierarchyItem.Blob.Name);
                                        stringList.Add(sFilename);
                                    }
                                }
                                else if (FileShare.SendToAzureFileShare(log, route, sFilename, bData))
                                {
                                    fileStatuses.Add(new FileStatus() { Filename = sFilename, DestinationName = blobHierarchyItem.Blob.Name, DestinationPath = route.Path, DestinationDateTime = DateTime.Now });
                                    APIUploadBlobTF.Blobs.DeleteBlob(log, container, blobHierarchyItem.Blob.Name);
                                    stringList.Add(sFilename);
                                }
                            }
                        }
                    }
                }
            }
            if (fileStatuses != null && fileStatuses.Count != 0)
                Utils.WriteFileStatusToDB(log, fileStatuses);
            return stringList.Count;
        }
        #endregion

        #region SendToUNCFileShare
        public static bool SendToUNCFileShare(ILogger log, APIRouting ftpData, string sFilename, byte[] bData)
        {
            if (string.IsNullOrEmpty(ftpData.Path))
            {
                APIUploadTrigger.SendLog(log, "No file share was specified in PrintEndpointT...quitting", LogEventLevel.Error);
                return false;
            }
            try
            {
                if (!Directory.Exists(ftpData.Path))
                {
                    APIUploadTrigger.SendLog(log, "The specified file share, " + ftpData.Path + ", does not exist", LogEventLevel.Warning);
                    return false;
                }
                if (Directory.Exists(ftpData.Path))
                {
                    string path = Path.Combine(ftpData.Path, sFilename);
                    if (File.Exists(path) && Utils.GetBoolSettingsValue("DeleteExistingFile", true))
                        File.Delete(path);
                    File.WriteAllBytes(path, bData);
                }
            }
            catch (Exception ex)
            {
                APIUploadTrigger.SendLog(log, "Exception in SendToUNCFileShare: " + ex.ToString(), LogEventLevel.Error);
                return false;
            }
            return true;
        }
        #endregion

        #region SendToAzureFileShare
        public static bool SendToAzureFileShare(ILogger log, APIRouting ftpData, string sFilename, byte[] bData)
        {
            string? sConn = Environment.GetEnvironmentVariable("FileShareConnection");
            if (string.IsNullOrEmpty(sConn))
            {
                APIUploadTrigger.SendLog(log, "There is no file share connection string...quitting", LogEventLevel.Error);
                return false;
            }
            try
            {
                if (string.IsNullOrEmpty(ftpData.Path))
                    ftpData.Path = "/";
                APIUploadTrigger.SendLog(log, "Connecting to file share " + ftpData.Path, LogEventLevel.Information);
                string str1 = ftpData.Path.Replace("\\", "/");
                if (str1.StartsWith("/"))
                    str1 = str1.Substring(1);
                string shareName = str1;
                if (str1.Contains('/'))
                    shareName = str1.Substring(0, str1.IndexOf("/"));
                ShareDirectoryClient shareDirectoryClient = new ShareClient(sConn, shareName).GetRootDirectoryClient();
                if (str1.IndexOf('/') != -1)
                {
                    foreach (string subdirectoryName in str1.Substring(shareName.Length + 1).Split('/'))
                    {
                        shareDirectoryClient = shareDirectoryClient.GetSubdirectoryClient(subdirectoryName);
                        if (Utils.GetBoolSettingsValue("CreateDirectory", true))
                            shareDirectoryClient.CreateIfNotExists();
                    }
                }
                ShareFileClient fileClient = shareDirectoryClient.GetFileClient(sFilename);
                string? str2 = Environment.GetEnvironmentVariable("DeleteExistingFile");
                if (string.IsNullOrEmpty(str2))
                    str2 = "true";
                bool result = true;
                if (!bool.TryParse(str2, out result))
                    result = true;
                if (result && (bool)fileClient.Exists())
                    fileClient.Delete();
                APIUploadTrigger.SendLog(log, "Attempting to create " + ftpData.Path + "\\" + sFilename + " and upload data to file share", LogEventLevel.Information);
                fileClient.Create((long)bData.Length);
                using (MemoryStream memoryStream = new(bData))
                    fileClient.Upload(memoryStream);
            }
            catch (Exception e)
            {
                APIUploadTrigger.SendLog(log, "Exception: " + e.ToString(), LogEventLevel.Error);
                return false;
            }
            return true;
        }
    }
    #endregion
}
