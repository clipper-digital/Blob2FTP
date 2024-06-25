using Azure.Storage.Blobs;
using FluentFTP;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using System.Collections.Concurrent;


namespace APIUploadBlobTF
{
    public class FTP21
    {
        #region StartFTPProcess
        public static void StartFTPProcess(ILogger log, BlobContainerClient container, APIRouting route, List<string> files)
        {
            if (route.FailureNum != 0)
            {
                if (route.LastFailureTime.HasValue)
                {
                    if (DateTime.Now <= route.LastFailureTime.Value)
                    {
                        APIUploadTrigger.SendLog(log, "Connection had failed for " + route.PrinterAbbr + " and the delay time hasn't yet elapsed, quitting", LogEventLevel.Information);
                        return;
                    }
                }
            }

            List<FtpClient> ftpClients = FTP21.OpenFTPServers(log, route, 1);
            if (ftpClients.Count == 0)
            {
                Errors.HandleError(log, route);
                return;
            }
            else
            {
                ftpClients[0].Disconnect();
                ftpClients[0].Dispose();
                ftpClients = FTP21.OpenFTPServers(log, route, files.Count);
                if (ftpClients.Count == 0)
                {
                    Errors.HandleError(log, route);
                    return;
                }
                List<List<string>> filesToSend = [];
                for (int index = 0; index < ftpClients.Count; ++index)
                    filesToSend.Add(new List<string>());
                int iCounter = 0;
                foreach (string file in files)
                {
                    filesToSend[iCounter].Add(file);
                    ++iCounter;
                    if (iCounter == ftpClients.Count)
                        iCounter = 0;
                }
                List<Task> taskList = [];
                ConcurrentStack<FileStatus> fileResults = new();
                iCounter = 0;
                object folderLock = new();
                foreach (FtpClient ftpClient in ftpClients)
                {
                    taskList.Add(Task.Factory.StartNew((Action)(() => FTP21.SendAllFilesViaFTP(log, ftpClient, route, container, route.PrinterAbbr + "/", filesToSend[iCounter++], fileResults, folderLock, 10000))));
                }
                Task.WaitAll(taskList.ToArray());
                iCounter = 0;
                foreach (FtpClient ftpClient in ftpClients)
                {
                    if (ftpClient.IsConnected)
                        ftpClient.Disconnect();
                    ftpClient.Dispose();
                }
                if (!fileResults.IsEmpty)
                    Utils.WriteFileStatusToDB(log, fileResults.ToList<FileStatus>());
                if (fileResults.Count == files.Count)
                {
                    APIUploadTrigger.SendLog(log, "** All " + fileResults.Count.ToString() + " files were successfully uploaded", LogEventLevel.Information);
                    if (route.FailureNum == 0)
                        return;
                    Errors.ResetFailureNum(log, route);
                }
                else
                {
                    APIUploadTrigger.SendLog(log, "Only " + fileResults.Count.ToString() + " file(s) out of " + files.Count.ToString() + " were uploaded", LogEventLevel.Information);
                }
            }

        }
        #endregion

        #region OpenFTPServers
        public static List<FtpClient> OpenFTPServers(ILogger log, APIRouting ftpData, int iFileCount)
        {

            List<FtpClient> ftpClients = [];
            if (iFileCount > ftpData.MaxConnections)
            {
                for (int index = 0; index < ftpData.MaxConnections; index++)
                    ftpClients.Add(new FtpClient(ftpData.URL, ftpData.Username, ftpData.Password, ftpData.Port));
            }
            else
                ftpClients.Add(new FtpClient(ftpData.URL, ftpData.Username, ftpData.Password, ftpData.Port));

            foreach (FtpClient ftpClient in ftpClients)
            {
                APIUploadTrigger.SendLog(log, "Connecting to " + ftpData.URL + ", port " + ftpData.Port.ToString(), LogEventLevel.Information);
                FtpProfile profile = ftpClient.AutoConnect();
                if (profile == null)
                {
                    APIUploadTrigger.SendLog(log, "Error connecting to FTP server", LogEventLevel.Error);
                    foreach (FtpClient client in ftpClients)
                    {
                        if (client.IsConnected)
                            client.Disconnect();
                        client.Dispose();
                    }
                    return [];
                }
            }
            return ftpClients;
        }
        #endregion

        #region SendAllFilesViaFTP
        public static void SendAllFilesViaFTP(ILogger log, FtpClient client, APIRouting route, BlobContainerClient container, string prefix, List<string> sFiles, ConcurrentStack<FileStatus> fileResults, object folderLock, int iSegmentSize)
        {
            foreach (string sFile in sFiles)
            {
                APIUploadTrigger.SendLog(log, "Reading blob " + prefix + sFile, LogEventLevel.Information);
                byte[] bData = Blobs.ReadBlobData(log, container, prefix + sFile);
                bool bSuccess = FTP21.SendFileToFTPServer(log, client, route, sFile, bData, folderLock);
                if (bData.Length != 0 && sFiles.LastIndexOf(sFile) != -1 && bSuccess)
                {
                    fileResults.Push(new FileStatus()
                    {
                        Filename = sFile,
                        DestinationName = prefix + sFile,
                        DestinationPath = "ftp://" + route.URL + route.Path,
                        DestinationDateTime = DateTime.Now
                    });
                    Blobs.DeleteBlob(log, container, prefix + sFile);
                }
                if (!bSuccess)
                    break;
            }
        }
        #endregion

        #region SendFileToFTPServer
        public static bool SendFileToFTPServer(ILogger log, FtpClient client, APIRouting route, string sFilename, byte[] bData, object FolderLock)
        {
            if (!string.IsNullOrEmpty(route.Path))
            {
                lock (FolderLock)
                {
                    bool boolSettingsValue = Utils.GetBoolSettingsValue("CreateDirectory", true);
                    if (!client.DirectoryExists(route.Path))
                    {
                        if (boolSettingsValue)
                        {
                            try
                            {
                                APIUploadTrigger.SendLog(log, "Directory " + route.Path + " does not exist...creating", LogEventLevel.Information);
                                client.CreateDirectory(route.Path);
                            }
                            catch (Exception ex)
                            {
                                APIUploadTrigger.SendLog(log, "Exception creating folder: " + ex.ToString(), LogEventLevel.Error);
                            }
                        }
                        else
                        {
                            APIUploadTrigger.SendLog(log, "Directory " + route.Path + "doesn't exist and CreateDirectory setting is false...quitting", LogEventLevel.Error);
                            return false;
                        }
                    }
                }
            }
            if (client.FileExists(sFilename))
            {
                if (Utils.GetBoolSettingsValue("DeleteExistingFile", true))
                {
                    APIUploadTrigger.SendLog(log, "File " + sFilename + " exists...attempting to delete", LogEventLevel.Information);
                    try
                    {
                        client.DeleteFile(sFilename);
                    }
                    catch (Exception)
                    {
                        APIUploadTrigger.SendLog(log, "Exception deleting " + sFilename + ", will try overwrite", LogEventLevel.Information);
                    }
                }
                else
                {
                    APIUploadTrigger.SendLog(log, "File " + sFilename + " exists...attempting to overwrite", LogEventLevel.Information);
                }
            }
            string sFullPath = route.Path + "/" + sFilename;
            sFullPath = sFullPath.Replace("//", "/");
            APIUploadTrigger.SendLog(log, "Uploading " + sFullPath + " from FTP21", LogEventLevel.Information);
            try
            {
                client.UploadBytes(bData, sFullPath);
            }
            catch (Exception e)
            {
                Errors.HandleError(log, route);
                APIUploadTrigger.SendLog(log, "Exception uploading to FTP: " + e.ToString(), LogEventLevel.Error);
                return false;
            }
            return true;
        }
        #endregion
    }
}
