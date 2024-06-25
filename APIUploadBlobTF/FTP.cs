using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Serilog.Events;
using System.Collections.Concurrent;


namespace APIUploadBlobTF
{
    public class FTP
    {
        #region StartFTPProcess
        public static void StartFTPProcess(ILogger log, BlobContainerClient container, APIRouting route, List<string> files)
        {
            if (route.FailureNum != 0)                  //  If this connection has failed before
            {
                if (route.LastFailureTime.HasValue)     //  And there's a failure time
                {
                    if (DateTime.Now <= route.LastFailureTime.Value)    //  Has the failure time elapsed? 
                    {                                   //  Nope, so don't try this connection this time
                        APIUploadTrigger.SendLog(log, "Connection had failed for " + route.PrinterAbbr + " and the delay time hasn't yet elapsed, quitting", LogEventLevel.Information);
                        return;
                    }
                }
            }
                                                        //  Test connection before going nuts with threads
            List<SftpClient> ftpClients = OpenFTPServers(log, route, 1);
            if (ftpClients.Count == 0)                  //  If the connection failed don't create threaded connections
            {
                Errors.HandleError(log, route);
                return;
            }
            else
            {
                ftpClients[0].Disconnect();             //  Disconnect the test connection
                ftpClients[0].Dispose();
                                                        //  Now open X number of connections based on file count
                ftpClients = OpenFTPServers(log, route, files.Count);
                if (ftpClients.Count == 0)              //  If connections failed, quit here
                {
                    Errors.HandleError(log, route);
                    return;
                }
                                                        //  Create a list of lists of files, one per thread
                List<List<string>> filesToSend = [];
                for (int index = 0; index < ftpClients.Count; ++index)
                    filesToSend.Add([]);
                int iCounter = 0;
                foreach (string file in files)          //  Add the files round robin until they're all assigned
                {
                    filesToSend[iCounter].Add(file);
                    ++iCounter;
                    if (iCounter == ftpClients.Count)
                        iCounter = 0;
                }
                List<Task> taskList = [];
                                                        //  Create a status result list that's thread safe
                ConcurrentStack<FileStatus> fileResults = new();
                iCounter = 0;
                object folderLock = new();
                foreach (SftpClient ftpClient in ftpClients)
                {                                       //  Start each sender thread
                    taskList.Add(Task.Factory.StartNew((() => SendAllFilesViaFTP(log, ftpClient, route, container, route.PrinterAbbr + "/", filesToSend[iCounter++], fileResults, folderLock, 10000))));
                }
                Task.WaitAll(taskList.ToArray());       //  Wait for all the threads to exit before moving on
                iCounter = 0;
                foreach (SftpClient ftpClient in ftpClients)
                {
                    if (ftpClient.IsConnected)          //  Disconnect each connection if still open
                        ftpClient.Disconnect();
                    ftpClient.Dispose();
                }
                if (! fileResults.IsEmpty)              //  Write any results to the output data table
                    Utils.WriteFileStatusToDB(log, fileResults.ToList<FileStatus>());
                if (fileResults.Count == files.Count)   //  Were all files sent?
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
        public static List<SftpClient> OpenFTPServers(ILogger log, APIRouting ftpData, int iFileCount)
        {
            List<SftpClient> ftpClients = new();
            try
            {
                if (iFileCount > ftpData.MaxConnections)    //  If there are more files than max connections
                {                                           //  Ad max connections # of FTP objects to list
                    for (int index = 0; index < ftpData.MaxConnections; ++index)
                        ftpClients.Add(new SftpClient(ftpData.URL, ftpData.Port, ftpData.Username, ftpData.Password));
                }
                else                                        //  Otherwise use use one connection
                    ftpClients.Add(new SftpClient(ftpData.URL, ftpData.Port, ftpData.Username, ftpData.Password));

                foreach (SftpClient ftpClient in ftpClients) //  Connect each FTP object to the server
                {
                    APIUploadTrigger.SendLog(log, "Connecting to " + ftpData.URL + ", port " + ftpData.Port.ToString(), LogEventLevel.Information);
                    ftpClient.Connect();
                }
            }
            catch (Exception e)
            {
                try
                {                                           //  If there's a connection error, disconnect everything
                    foreach (BaseClient baseClient in ftpClients)
                        baseClient.Disconnect();
                }
                catch (Exception) { }
                APIUploadTrigger.SendLog(log, "Error connecting to FTP server: " + e.ToString(), LogEventLevel.Error);
                return [];
            }
            return ftpClients;
        }
        #endregion

        #region SendAllFilesViaFTP
        public static void SendAllFilesViaFTP(ILogger log, SftpClient client, APIRouting route, BlobContainerClient container, string prefix, List<string> sFiles, ConcurrentStack<FileStatus> fileResults, object folderLock, int iSegmentSize)
        {
            foreach (string sFile in sFiles)        //  For each file in the list, read the blob with that name
            {                                       //  Send it via FTP, save the status and delete the blob
                APIUploadTrigger.SendLog(log, "Reading blob " + prefix + sFile, LogEventLevel.Information);
                byte[] bData = Blobs.ReadBlobData(log, container, prefix + sFile);
                if (bData.Length != 0 && sFiles.LastIndexOf(sFile) != -1 && FTP.SendFileToFTPServer(log, client, route, sFile, bData, folderLock))
                {
                    fileResults.Push(new FileStatus()
                    {
                        Filename = sFile,
                        DestinationName = prefix + sFile,
                        DestinationPath = "sftp://" + route.URL + route.Path,
                        DestinationDateTime = DateTime.Now
                    });
                    Blobs.DeleteBlob(log, container, prefix + sFile);
                }
            }
        }
        #endregion

        #region SendFileToFTPServer
        public static bool SendFileToFTPServer(ILogger log, SftpClient client, APIRouting route, string sFilename, byte[] bData, object FolderLock)
        {
            if (!string.IsNullOrEmpty(route.Path))      //  If there's a destination folder
            {
                lock (FolderLock)                       //  Lock the thread so there aren't multiple parallels attempts to create it
                {
                    bool boolSettingsValue = Utils.GetBoolSettingsValue("CreateDirectory", true);
                    if (!client.Exists(route.Path))     //  If the folder doesn't exist
                    {
                        if (boolSettingsValue)          //  And this is allowed to create it
                        {
                            try
                            {                           //  Try creating the destination folder
                                APIUploadTrigger.SendLog(log, "Directory " + route.Path + " does not exist...creating", LogEventLevel.Information);
                                client.CreateDirectory(route.Path);
                            }
                            catch (Exception ex)
                            {
                                APIUploadTrigger.SendLog(log, "Exception creating folder: " + ex.ToString(), LogEventLevel.Error);
                            }
                        }
                        else                            //  If it doesn't exist and not allowed to create it, complain and leave
                        {
                            APIUploadTrigger.SendLog(log, "Directory " + route.Path + "doesn't exist and CreateDirectory setting is false...quitting", LogEventLevel.Error);
                            return false;
                        }
                    }
                }                                       //  Release the thread lock
            }

            string sFullName = route.Path + "/" + sFilename;
            sFullName = sFullName.Replace("//", "/");
            if (client.Exists(sFullName))               //  If the file exists and it's allowed to delete it
            {
                if (Utils.GetBoolSettingsValue("DeleteExistingFile", true))
                {
                    APIUploadTrigger.SendLog(log, "File " + sFullName + " exists...attempting to delete", LogEventLevel.Information);
                    try
                    {
                        client.DeleteFile(sFullName);   //  Try deleting the existing file
                    }
                    catch (Exception)
                    {
                        APIUploadTrigger.SendLog(log, "Exception deleting " + sFullName + ", will try overwrite", LogEventLevel.Information);
                    }
                }
                else
                {
                    APIUploadTrigger.SendLog(log, "File " + sFullName + " exists...attempting to overwrite", LogEventLevel.Information);
                }
            }
            APIUploadTrigger.SendLog(log, "Uploading " + sFullName + " from FTP", LogEventLevel.Information);
            try
            {                                           //  Finally, try sending the file to the FTP server
                using Stream stream = new MemoryStream(bData);
                client.UploadFile(stream, sFullName);
            }
            catch (Exception e)
            {
                APIUploadTrigger.SendLog(log, "Exception uploading to FTP: " + e.ToString(), LogEventLevel.Error);
                return false;
            }
            return true;
        }
        #endregion
    }
}
