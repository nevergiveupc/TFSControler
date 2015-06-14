using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System.Net;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Microsoft.TeamFoundation.VersionControl.Common;

namespace CompileServer
{
    enum MappingDirOption
    {
        PendEdit,
        PendDelete,
        PendRename,
        PendAdd,
    }
    
    class TFSControler
    {
        private string serverUrl;
        private TfsTeamProjectCollection _tfs;
        private VersionControlServer _vcs;
        private Workspace _workspace;
        private long changeId;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="serverUrl">服务器地址</param>
        /// <param name="userName">用户名</param>
        /// <param name="passWd">密码</param>
        /// <param name="domain">域</param>
        public TFSControler(string serverUrl, string userName, string passWd, string domain)
        {
            //项目集合后面一定要补全"/Services/v3.0/LocationService.asmx"
            //serverUrl = "http://cetsoft-tfs:8080/tfs/软件部公用区域" + "/Services/v3.0/LocationService.asmx";
            this.serverUrl = serverUrl + "/Services/v3.0/LocationService.asmx";
            //用户登录校验信息
            NetworkCredential cer = new NetworkCredential(userName, passWd, domain);
            //连接到TFS服务器 
            _tfs = new TfsTeamProjectCollection(new Uri(serverUrl), cer);
            _vcs = _tfs.GetService(typeof(VersionControlServer)) as VersionControlServer;
            _vcs.NonFatalError += new ExceptionEventHandler(version_NonFatalError);
        }

        private void version_NonFatalError(object sender, ExceptionEventArgs e)
        {
            if (e.Exception == null)
            {
                return;
            }

            string msg = e.Exception.Message;
            msg += "\r\n";
            msg += e.Exception.StackTrace;
        }

        /// <summary>
        /// 获取最新版本到本地
        /// </summary>
        /// <param name="filePath">本地路径（应该是文件，而不是文件夹）</param>
        public void GetLatestVersion(string filePath)
        {
            string serverPath = _workspace.GetServerItemForLocalItem(filePath);
            GetLatestVersion(serverPath, filePath);   
        }


        /// <summary>
        /// 下载最新文件到本地
        /// </summary>
        /// <param name="sourcePath">服务器路径</param>
        /// <param name="localPath">本地路径</param>
        public void GetLatestVersion(string serverPath, string filePath)
        {
            Dictionary<string, int> FileVersion = new Dictionary<string, int>();
            string TFSDir = Environment.CurrentDirectory + "\\TFSTemp\\TFSVersion.txt";
            bool isEditFileVersion = false;
            try
            {
                using (FileStream filestream = new FileStream(TFSDir, FileMode.Open, FileAccess.Read))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    FileVersion = (Dictionary<string, int>)(bf.Deserialize(filestream));
                }
            }
            catch (System.Exception ex)
            {
            }
            ItemSet items = null;
            try
            {
                items = _vcs.GetItems(serverPath, RecursionType.Full);
            }
            catch
            {
                return;
            }
            if (items == null)
                return ;
            foreach (Item item in items.Items)
            {
                string serverItem = item.ServerItem;          //如：$/MySolution/CommDll/CommDll.sln 
                string path;
                try
                {
                    path = serverItem.Substring(serverPath.Length);
                }
                catch
                {
                    path = "";
                }
                string localItem = filePath + "\\" + path;     //存储到本地的路径 
                localItem = localItem.Replace("/", "\\");

                if (item.ItemType == ItemType.Folder)
                    if (!Directory.Exists(localItem))       //如果目录不存在则创建 
                        Directory.CreateDirectory(localItem);
                else if (item.ItemType == ItemType.File)
                {
                    int pos = path.LastIndexOf("/");
                    string FileName = path;
                    if (pos != -1)
                        FileName = path.Substring(pos + 1);
                    if (!FileVersion.Keys.Contains(serverItem))
                    {
                        item.DownloadFile(localItem);            //下载到本地文件 
                        FileVersion.Add(serverItem, item.ChangesetId);
                        isEditFileVersion = true;
                    }
                    else if (FileVersion[serverItem] != item.ChangesetId)
                    {                                                                                                       
                        item.DownloadFile(localItem);
                        FileVersion[serverItem] = item.ChangesetId;
                        isEditFileVersion = true;
                    }
                }          
            }
            if (isEditFileVersion)
            {
                using (FileStream filestream = new FileStream(TFSDir, FileMode.Open, FileAccess.Write))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    bf.Serialize(filestream, FileVersion);
                }
            }
        }


        /// <summary> 
        /// Check out the file from the server
        /// </summary>
        /// <param name="filePath">the path of local file</param>
        public void CheckOutFile(string filePath)
        {
            try
            {
                GetLatestVersion(filePath);
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// 将服务器的文件夹映射到本地，并执行checkout
        /// </summary>
        /// <param name="workSpaceName"></param>
        /// <param name="dirPath"></param>
        /// <param name="locPath"></param>
        public void CheckOutMappingDir(string workSpaceName, string dirPath, string locPath)
        {
            RemoveMappingDir(workSpaceName);
            _workspace = _vcs.CreateWorkspace(workSpaceName, _vcs.AuthenticatedUser);//创建工作区
            //添加映射
            WorkingFolder wf = new WorkingFolder(dirPath, locPath);

            if (!_workspace.IsServerPathMapped(dirPath))
            {
                _workspace.CreateMapping(wf);   //创建映射。我们也可以这样: ws.Map(serverItem,localItem); 
            }
            else
            {
                _workspace.DeleteMapping(wf);
                _workspace.CreateMapping(wf);
            }
            CheckOutFile(locPath);
        }

        public void MappingDirForRename(string workSpaceName, string dirPath, string locPath, Dictionary<string, string> renameDic)
        {
            RemoveMappingDir(workSpaceName);
            _workspace = _vcs.CreateWorkspace(workSpaceName, _vcs.AuthorizedUser);//创建工作区
            //添加映射
            WorkingFolder wf = new WorkingFolder(dirPath, locPath);

            if (!_workspace.IsServerPathMapped(dirPath))
            {
                _workspace.CreateMapping(wf);   //创建映射。我们也可以这样: ws.Map(serverItem,localItem); 
            }
            else
            {
                _workspace.DeleteMapping(wf);
                _workspace.CreateMapping(wf);
            }
            //_workspace.Get();
            string[] fileList = Directory.GetFileSystemEntries(locPath, "*", SearchOption.AllDirectories);
            List<string> tempList = new List<string>();
            foreach (string file in fileList)
            {
                if (File.Exists(file))
                {
                    tempList.Add(file);
                }
            }
            _workspace.Get(VersionSpec.Latest, GetOptions.Overwrite);//更新至最新
            foreach (KeyValuePair<string, string> pair in renameDic)
            {
                int renameCount = _workspace.PendRename(pair.Key,pair.Value,LockLevel.Unchanged,true,true);
            }
        }

        public void MappingDirForDelete(string workSpaceName, string dirPath, string locPath)
        {
            RemoveMappingDir(workSpaceName);  
            _workspace = _vcs.CreateWorkspace(workSpaceName, _vcs.AuthorizedUser);//创建工作区
            //添加映射
            WorkingFolder wf = new WorkingFolder(dirPath, locPath);

            if (!_workspace.IsServerPathMapped(dirPath))
            {
                _workspace.CreateMapping(wf);   //创建映射。我们也可以这样: ws.Map(serverItem,localItem); 
            }
            else
            {
                _workspace.DeleteMapping(wf);
                _workspace.CreateMapping(wf);
            }
            string[] fileList = Directory.GetFileSystemEntries(locPath, "*", SearchOption.AllDirectories);
            List<string> tempList = fileList.ToList();
            tempList.Add(locPath);
            fileList = tempList.ToArray();
            _workspace.Get(VersionSpec.Latest,GetOptions.Overwrite);//更新至最新
            int pend = _workspace.PendDelete(new string[]{locPath},RecursionType.Full,LockLevel.Unchanged,true);//此处需要查看用户权限，如果没有锁定权限，默认采用Unchanged
        }

        public void MappingDirForEdit(string workSpaceName, string dirPath, string locPath)
        {
            RemoveMappingDir(workSpaceName);  
            _workspace = _vcs.CreateWorkspace(workSpaceName, _vcs.AuthorizedUser);//创建工作区
            //添加映射
            WorkingFolder wf = new WorkingFolder(dirPath, locPath);

            if (!_workspace.IsServerPathMapped(dirPath))
            {
                _workspace.CreateMapping(wf);   //创建映射。我们也可以这样: ws.Map(serverItem,localItem); 
            }
            else
            {
                _workspace.DeleteMapping(wf);
                _workspace.CreateMapping(wf);
            }
            string[] fileList;
            try
            {
                fileList = Directory.GetFileSystemEntries(locPath, "*", SearchOption.AllDirectories);
                List<string> tempList = new List<string>();
                foreach (string file in fileList)
                {
                    if (File.Exists(file))
                    {
                        tempList.Add(file);
                    }
                }
            }
            catch
            {
                fileList = new string[] { locPath };
            }
            _workspace.Get(VersionSpec.Latest,GetOptions.Overwrite);//更新至最新
            int pend = _workspace.PendEdit(fileList.ToArray(),RecursionType.Full,"",LockLevel.Unchanged);//此处需要查看用户权限，如果没有锁定权限，默认采用Unchanged
        }

        public void MappingFileForEdit(string workSpaceName, string serverItem, string localItem)
        {
            RemoveMappingDir(workSpaceName);
            _workspace = _vcs.CreateWorkspace(workSpaceName, _vcs.AuthorizedUser);//创建工作区
            //添加映射
            WorkingFolder wf = new WorkingFolder(serverItem, localItem);

            if (!_workspace.IsServerPathMapped(serverItem))
            {
                _workspace.CreateMapping(wf);   //创建映射。我们也可以这样: ws.Map(serverItem,localItem); 
            }
            else
            {
                _workspace.DeleteMapping(wf);
                _workspace.CreateMapping(wf);
            }
            //_workspace.Get();
            string[] fileList;
            try
            {
                fileList = Directory.GetFileSystemEntries(localItem, "*", SearchOption.AllDirectories);
                List<string> tempList = new List<string>();
                foreach (string file in fileList)
                {
                    if (File.Exists(file))
                    {
                        tempList.Add(file);
                    }
                }
            }
            catch
            {
                fileList = new string[] { localItem };
            }
            _workspace.Get(VersionSpec.Latest, GetOptions.Overwrite);//更新至最新
            int pend = _workspace.PendEdit(fileList.ToArray(), RecursionType.Full, "", LockLevel.Unchanged);//此处需要查看用户权限，如果没有锁定权限，默认采用Unchanged

        }

        public void RemoveMappingDir(string workSpaceName)
        {
            Workspace[] workspaces = _vcs.QueryWorkspaces(workSpaceName, _vcs.AuthorizedUser, Workstation.Current.Name);
            if (workspaces.Length != 0)
            {
                _vcs.DeleteWorkspace(workSpaceName,_vcs.AuthorizedUser);
            }          
        }
        /// <summary>
        /// check out 一个文件夹
        /// 不需要映射
        /// </summary>
        /// <param name="DirPath"></param>
        public void CheckOutDir(String DirPath, string savePath)
        {
            ItemSet items = _vcs.GetItems(DirPath, RecursionType.Full);
            
            foreach (Item item in items.Items)
            {           
                string serverItem = item.ServerItem;//如：$/MySolution/CommDll/CommDll.sln
                string path;
                try
                {
                    path = serverItem.Substring(DirPath.Length);
                }
                catch
                {
                    path = "";
                }
                string localItem = savePath + "\\" + path;     //存储到本地的路径
                localItem = localItem.Replace("/", "\\");

                //创建目录或下载文件 
                if (item.ItemType == ItemType.Folder)
                {
                    if (!Directory.Exists(localItem))       //如果目录不存在则创建 
                    {
                        Directory.CreateDirectory(localItem);
                    }
                }
                else if (item.ItemType == ItemType.File)
                {
                    item.DownloadFile(localItem);            //下载到本地文件 
                }
            }
        }

        public void CheckOutDir(String DirPath, string savePath,RecursionType type)
        {
            ItemSet items = _vcs.GetItems(DirPath, type);

            foreach (Item item in items.Items)
            {
                string serverItem = item.ServerItem;//如：$/MySolution/CommDll/CommDll.sln
                string path;
                try
                {
                    path = serverItem.Substring(DirPath.Length);
                }
                catch
                {
                    path = "";
                }
                string localItem = savePath + "\\" + path;     //存储到本地的路径
                localItem = localItem.Replace("/", "\\");

                //创建目录或下载文件 
                if (item.ItemType == ItemType.Folder)
                {
                    if (!Directory.Exists(localItem))       //如果目录不存在则创建 
                    {
                        Directory.CreateDirectory(localItem);
                    }
                }
                else if (item.ItemType == ItemType.File)
                {
                    item.DownloadFile(localItem);            //下载到本地文件 
                }
            }
        }

        public void CheckOutFile(String DirPath, string savePath)
        {
            ItemSet items = _vcs.GetItems(DirPath, RecursionType.Full);

            foreach (Item item in items.Items)
            {
                string serverItem = item.ServerItem;//如：$/MySolution/CommDll/CommDll.sln
                string localItem = savePath;     //存储到本地的路径
                localItem = localItem.Replace("/", "\\");

                //创建目录或下载文件 
                if (item.ItemType == ItemType.Folder)
                {
                    if (!Directory.Exists(localItem))       //如果目录不存在则创建 
                    {
                        Directory.CreateDirectory(localItem);
                    }
                }
                else if (item.ItemType == ItemType.File)
                {
                    item.DownloadFile(localItem);            //下载到本地文件 
                }
            }
        }

        public void CheckOutDirWithVersion(String DirPath, string savePath)
        {
            ItemSet items = _vcs.GetItems(DirPath, RecursionType.Full);
            Dictionary<string, int> FileVersion = new Dictionary<string, int>();

            foreach (Item item in items.Items)
            {
                string serverItem = item.ServerItem;                                       //如：$/MySolution/CommDll/CommDll.sln
                string path;
                try
                {
                    path = serverItem.Substring(DirPath.Length);
                }
                catch
                {
                    path = "";
                }
                string localItem = savePath + "\\" + path;     //存储到本地的路径
                localItem = localItem.Replace("/", "\\");

                //创建目录或下载文件 
                if (item.ItemType == ItemType.Folder)
                {
                    if (!Directory.Exists(localItem))       //如果目录不存在则创建 
                    {
                        Directory.CreateDirectory(localItem);
                    }
                }
                else if (item.ItemType == ItemType.File)
                {
                    string filename = string.Empty;
                    int pos = serverItem.LastIndexOf('/');
                    if (pos != -1)
                        filename = serverItem.Substring(pos + 1);
                    item.DownloadFile(localItem);            //下载到本地文件 
                    if (filename != "" && !FileVersion.Keys.Contains(serverItem))
                        FileVersion.Add(serverItem, item.ChangesetId);
                }
            }
            if (FileVersion != null && FileVersion.Keys.Count > 0)
            {
                int pos = savePath.LastIndexOf("\\");
                if (pos != -1)
                {
                    string tfsFilePath = savePath.Substring(0, pos + 1) + "TFSTemp\\";
                    string tfsFileName = "TFSVersion.txt";
                    if (!Directory.Exists(tfsFilePath))
                        Directory.CreateDirectory(tfsFilePath);
                    using (FileStream filestream = new FileStream(tfsFilePath + tfsFileName, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        BinaryFormatter bf = new BinaryFormatter();
                        bf.Serialize(filestream, FileVersion);
                        filestream.Close();
                    }
                }
            }
        }

        /// <summary>
        /// delete the file from server, not check in
        /// </summary>
        /// <param name="filePath"></param>
        public void DeleteFile(string filePath)
        {
            try
            {
                _workspace.PendDelete(_workspace.GetServerItemForLocalItem(filePath));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


        /// <summary>
        /// 新增文件到服务器 
        /// </summary>
        /// <param name="filePath">本地文件路径</param>
        public void AppendNewFile(string filePath)
        {
            try
            {
                _workspace.Refresh();
                _workspace.CreateMapping(GetWorkingFolder(filePath));
                _workspace.PendAdd(filePath);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public WorkingFolder GetWorkingFolder(string filePath)
        {
            return _workspace.GetWorkingFolderForServerItem(_workspace.GetServerItemForLocalItem(filePath));
        }


        /// <summary>
        /// 签入文件 
        /// </summary>
        /// <param name="filePath">文件路径（可这本地路径，也可为服务器路径）</param>
        /// <param name="comment">签入说明（是否可为空取决于TFS的配置）</param>
        public void CheckInFile(string filePath, string comment)
        {
            string[] filePaths = { filePath };
            CheckInFiles(filePaths, comment);
        }


        /// <summary>
        /// 签入多个文件 
        /// </summary>
        /// <param name="filePaths">文件路径</param>
        /// <param name="comment">签入说明</param>
        public bool CheckInFiles(string[] filePaths, string comment)
        {
            if (filePaths != null && filePaths.Length > 0)
            {
                try
                {
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        int result = 0;
                        if (!ExistInServer(filePaths[i]))
                            AppendNewFile(filePaths[i]);
                    }
                   PendingChange[] tempPendingChange = GetFileChanges(filePaths);
                   if (tempPendingChange != null && tempPendingChange.Length > 0)
                   {
                       int iResult = _workspace.CheckIn(tempPendingChange, comment);
                       return true;
                   }
                   else
                       return false;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return false;
        }

        public bool CheckInFiles(string[] filePaths, string comment,CheckinNote checkinnote)
        {
            if (filePaths != null && filePaths.Length > 0)
            {
                try
                {
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        int result = 0;
                        if (!ExistInServer(filePaths[i]))
                            AppendNewFile(filePaths[i]);
                    }
                    PendingChange[] tempPendingChange = GetFileChanges(filePaths);
                    if (tempPendingChange != null && tempPendingChange.Length > 0)
                    {
                        int iResult = _workspace.CheckIn(tempPendingChange, comment,checkinnote,null,null);
                        return true;
                    }
                    else
                        return false;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return false;
        }

        public bool CheckInRenameFiles(string[] filePaths, string comment,CheckinNote checkInNote)
        {
            if (filePaths != null && filePaths.Length > 0)
            {
                try
                {
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        if (!ExistInServer(filePaths[i]))
                            AppendNewFile(filePaths[i]);
                    }
                    PendingChange[] tempPendingChange = GetFileRenameChanges(filePaths);    
                    if (tempPendingChange != null && tempPendingChange.Length > 0)
                    {
                        int iResult = _workspace.CheckIn(tempPendingChange, comment, checkInNote,null,null);
                        return true;
                    }
                    else
                        return false;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary> 
        /// 这段代码从零开始完整地演示了从TFS上下载文件到本地的过程 
        /// </summary> 
        public void DownloadFilesFromTFS()
        {
            //第一步：连接到TFS服务器 
            string tpcURL = "http://192.168.83.62:8080";
            TfsTeamProjectCollection tpc = new TfsTeamProjectCollection(new Uri(tpcURL));
            VersionControlServer version = tpc.GetService(typeof(VersionControlServer)) as VersionControlServer;

            //第二步：创建工作区(Worksapce)，如果已经存在就不创建 
            string worksapce = "WorkSpaceTest01";
            Workspace ws;
            Workspace[] wss = version.QueryWorkspaces(worksapce, Environment.UserName, Environment.MachineName);//查询工作区 
            if (wss.Length == 0)
            {
                ws = version.CreateWorkspace(worksapce);//创建工作区 
            }
            else
            {
                ws = wss[0];
            }

            string serverPath = "$/MySolution/CommDll";
            string savePath = "E:\\TFS62\\MySolution\\CommDll";
            //第三步：获取最新版本，也可以使用GetItems其他重载获取特定版本 
            ItemSet items = version.GetItems(serverPath, RecursionType.Full);
            foreach (Item item in items.Items)
            {
                string serverItem = item.ServerItem;                                       //如：$/MySolution/CommDll/CommDll.sln 
                string localItem = savePath + serverItem.Substring(serverPath.Length);     //存储到本地的路径 
                localItem = localItem.Replace("/", "\\");

                //第四步：做映射（Mapping） 
                if (!ws.IsServerPathMapped(serverItem))
                {
                    ws.Map(serverItem, localItem);
                }

                //第五步：创建目录或下载文件 
                if (item.ItemType == ItemType.Folder)
                {
                    if (!Directory.Exists(localItem))       //如果目录不存在则创建 
                        Directory.CreateDirectory(localItem);
                }
                else if (item.ItemType == ItemType.File)
                    item.DownloadFile(localItem);            //下载到本地文件 
            }
        }

        public PendingChange[] GetFileChanges(string[] filePaths)
        {
            int length = filePaths.Length;
            PendingChange[] changes = new PendingChange[length];
            PendingChange[] pendings = _workspace.GetPendingChanges(ItemSpec.FromStrings(filePaths, RecursionType.Full));

            //注意锁定的处理
            if (changes == null || pendings == null)
                return null;
            if (changes != null && pendings != null && changes.Length != pendings.Length)
                return null;
            for (int i = 0; i < filePaths.Length; i++)
            {
                filePaths[i] = filePaths[i].Replace("\\\\", "\\");
                for (int j = 0; j < pendings.Length; j++)
                {
                    if (pendings[j].LocalItem == filePaths[i])
                    {
                        changes[i] = pendings[j];
                        break;
                    }
                }
            }
            return changes;
        }

        public PendingChange[] GetFileChanges(string[] filePaths, Workspace _workspace)
        {
            int length = filePaths.Length;
            PendingChange[] changes = new PendingChange[length];
            PendingChange[] pendings = _workspace.GetPendingChanges(ItemSpec.FromStrings(filePaths, RecursionType.Full));

            //注意锁定的处理
            if (changes == null || pendings == null)
                return null;
            if (changes != null && pendings != null && changes.Length != pendings.Length)
                return null;
            for (int i = 0; i < filePaths.Length; i++)
            {
                for (int j = 0; j < pendings.Length; j++)
                {
                    if (pendings[j].LocalItem == filePaths[i])
                    {
                        changes[i] = pendings[j];
                        break;
                    }
                }
            }
            return changes;
        }

        public PendingChange[] GetFileRenameChanges(string[] filePaths)
        {
            int length = filePaths.Length;
            PendingChange[] changes = new PendingChange[length];
            PendingChange[] pendings = _workspace.GetPendingChanges();

            //注意锁定的处理
            if (changes == null || pendings == null)
                return null;
            if (changes != null && pendings != null && changes.Length != pendings.Length)
                return null;
            return pendings;
        }

        public Changeset GetChangeSet(string serverPath)
        {
            Changeset changeSet=null;
            Item item = _vcs.GetItem(serverPath);            
            if (item != null)
                changeSet = _vcs.GetChangeset(item.ItemId);
            return changeSet;
        }

        public PendingChange GetPendingChange(string filePath)
        {
            return _vcs.GetPendingChange(GetFileChangeId(filePath));
        }

        /// <summary>
        /// 获取ChangeId
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public int GetFileChangeId(string filePath)
        {
            return _vcs.GetItem(filePath).ChangesetId;
        }

        /// <summary>
        /// 得到一个文件夹下所有文件的最新ChangeId
        /// </summary>
        /// <returns></returns>
        public int GetDirLastChangeId(string dirPath)
        {
            ItemSet items = this._vcs.GetItems(dirPath, RecursionType.Full);
            //获取最新的变更集
            int LastRevision = 0;
            foreach (Item item in items.Items)
            {
                if (item.ChangesetId > LastRevision)
                    LastRevision = item.ChangesetId;
            }
            return LastRevision;
        }

        /// <summary>
        /// 文件是否存在于服务器
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool ExistInServer(string filePath)
        {
            bool result = true;
            try
            {
                GetFileChangeId(filePath);
            }
            catch (Exception ex)
            {
                result = false;
            }
            return result;
        }

        /// <summary>
        /// 断开TFS连接 
        /// </summary>
        public void CloseTfs()
        {
            _tfs.Dispose();
        }

        public bool LoginTFS(string serverUrl, string userName, string passWd, string domain)
        {
            bool _loginResult = true;
            try
            {
                TfsTeamProjectCollection tpc = new TfsTeamProjectCollection(new Uri(serverUrl), new System.Net.NetworkCredential(userName, passWd, domain));
                VersionControlServer version = tpc.GetService(typeof(VersionControlServer)) as VersionControlServer;
                ItemSpec spec = new ItemSpec("$/", RecursionType.OneLevel);
                ItemSet its = version.GetItems(spec, VersionSpec.Latest, DeletedState.NonDeleted, ItemType.Folder, true);
            }
            catch (Exception exception)
            {
                if (exception.Message.IndexOf("TF30063") >= 0)
                    _loginResult = false;
            }
            return _loginResult;
        }

        public ItemSet GetItemsFromDir(string DirPath, RecursionType recursionType)
        {
            try
            {
                ItemSet items = _vcs.GetItems(DirPath, recursionType);
                return items;
            }
            catch
            {
                return null;
            }
        }
    }
}
