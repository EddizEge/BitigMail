using System.Security.Cryptography;
using System.Text.Json;
using Aspose.Email;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Recovery;
using BitigMail.Engine.Storage;

if (args.Length != 2) return 2;
try
{
    byte[] licenseBytes;using(var input=Console.OpenStandardInput()){using var memory=new MemoryStream();byte[] buffer=new byte[81920];int read;while((read=input.Read(buffer))>0){if(memory.Length+read>1024*1024)return 11;memory.Write(buffer,0,read);}licenseBytes=memory.ToArray();}var startup=new AsposeSdkStartupService(licenseBytes.Length==0?null:licenseBytes);if(licenseBytes.Length>0)CryptographicOperations.ZeroMemory(licenseBytes);startup.EnsureReady();
    string requestPath=Path.GetFullPath(args[0]), resultPath=Path.GetFullPath(args[1]);
    if(new FileInfo(requestPath).Length>1024*1024)return 3;
    DamagedStoreWorkerRequest request=JsonSerializer.Deserialize<DamagedStoreWorkerRequest>(File.ReadAllText(requestPath))??throw new InvalidDataException();
    if(request.SchemaVersion!=DamagedStoreResultValidator.CurrentSchemaVersion||request.MaxFolders is <1 or >100_000||request.MaxItems is <1 or >250_000||request.MaxDepth is <1 or >128||DateTime.UtcNow.Ticks>request.DeadlineUtcTicks)return 4;
    string root=Path.GetFullPath(request.OutputRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
    if(!resultPath.StartsWith(root,StringComparison.OrdinalIgnoreCase))return 5;
    var messages=new List<RecoveredMimeFile>();var failures=new List<RecoveryFailure>();bool complete=true;string outcome="healthy_extraction";int nonMailItemCount=0;
    using var source=new FileStream(request.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.SequentialScan);
    string before=Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();source.Position=0;
    if(!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(before),System.Text.Encoding.UTF8.GetBytes(request.SourceSha256)))return 6;
    try
    {
        using var store=PersonalStorage.FromStream(source,new PersonalStorageLoadOptions{Writable=false,LeaveStreamOpen=true});
        FolderInfo rootFolder;
        try{rootFolder=store.RootFolder;}catch(Exception){failures.Add(new("root",null,"root_unreadable"));Write("unreadable_source",null,false);return 0;}
        var visitedFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var visitedMessages=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var queue=new Queue<(FolderInfo Folder,int Depth,string ParentPath)>();queue.Enqueue((rootFolder,0,""));
        while(queue.Count>0&&visitedFolders.Count<request.MaxFolders&&messages.Count<request.MaxItems&&failures.Count<DamagedStoreResultValidator.MaxFailures&&DateTime.UtcNow.Ticks<=request.DeadlineUtcTicks)
        {
            var (folder,depth,parentPath)=queue.Dequeue();string folderId=folder.EntryIdString??"";if(!visitedFolders.Add(folderId))continue;if(depth>request.MaxDepth){failures.Add(new("folder_depth",folderId,"depth_limit"));complete=false;continue;}
            string folderName=folder.DisplayName??"[Adsız]";string folderPath=depth==0?"":string.IsNullOrEmpty(parentPath)?folderName:parentPath+"/"+folderName;try{foreach(string id in folder.EnumerateMessagesEntryId()){if(visitedMessages.Count>=request.MaxItems||failures.Count>=DamagedStoreResultValidator.MaxFailures||DateTime.UtcNow.Ticks>request.DeadlineUtcTicks){complete=false;break;}Extract(id,folderId,folderName,folderPath);}}catch(Exception){complete=false;if(failures.Count<DamagedStoreResultValidator.MaxFailures)failures.Add(new("message_enumeration",folderId,"normal_enumeration_failed"));try{foreach(string id in store.FindMessages(folderId)){if(visitedMessages.Count>=request.MaxItems||failures.Count>=DamagedStoreResultValidator.MaxFailures||DateTime.UtcNow.Ticks>request.DeadlineUtcTicks){complete=false;break;}Extract(id,folderId,folderName,folderPath);}}catch(Exception){if(failures.Count<DamagedStoreResultValidator.MaxFailures)failures.Add(new("message_fallback",folderId,"fallback_enumeration_failed"));}}
            try{foreach(FolderInfo child in folder.EnumerateFolders()){if(queue.Count+visitedFolders.Count>=request.MaxFolders||failures.Count>=DamagedStoreResultValidator.MaxFailures||DateTime.UtcNow.Ticks>request.DeadlineUtcTicks){complete=false;break;}queue.Enqueue((child,depth+1,folderPath));}}catch(Exception){complete=false;if(failures.Count<DamagedStoreResultValidator.MaxFailures)failures.Add(new("folder_enumeration",folderId,"normal_enumeration_failed"));try{foreach(string id in store.FindSubfolders(folderId)){if(queue.Count+visitedFolders.Count>=request.MaxFolders||failures.Count>=DamagedStoreResultValidator.MaxFailures||DateTime.UtcNow.Ticks>request.DeadlineUtcTicks){complete=false;break;}try{FolderInfo? child=store.GetFolderById(id);if(child is not null)queue.Enqueue((child,depth+1,folderPath));}catch(Exception){if(failures.Count<DamagedStoreResultValidator.MaxFailures)failures.Add(new("folder",id,"open_failed"));}}}catch(Exception){if(failures.Count<DamagedStoreResultValidator.MaxFailures)failures.Add(new("folder_fallback",folderId,"fallback_enumeration_failed"));}}
        }
        if(queue.Count>0||visitedMessages.Count>=request.MaxItems||failures.Count>=DamagedStoreResultValidator.MaxFailures||DateTime.UtcNow.Ticks>request.DeadlineUtcTicks){complete=false;if(failures.Count<DamagedStoreResultValidator.MaxFailures)failures.Add(new("store",null,"bounded_scan_incomplete"));}
        outcome=failures.Count==0&&complete?"healthy_extraction":messages.Count>0?"partial_recovered":"unreadable_source";

        void Extract(string? id,string folderId,string folderName,string folderPath)
        {if(string.IsNullOrWhiteSpace(id)||visitedMessages.Count>=request.MaxItems||!visitedMessages.Add(id))return;string? full=null;try{using var msg=store.ExtractMessage(id);if(!string.Equals(msg.MessageClass,"IPM.Note",StringComparison.OrdinalIgnoreCase)&&!msg.MessageClass.StartsWith("IPM.Note.",StringComparison.OrdinalIgnoreCase)){nonMailItemCount++;return;}string folderHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folderId))).ToLowerInvariant(),messageHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant();string name=$"folders/{folderHash}/{messageHash}.eml";full=Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(full)!);msg.Save(full,SaveOptions.DefaultEml);using var fs=File.OpenRead(full);messages.Add(new(name,Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(),fs.Length,folderName,folderHash,messageHash,startup.Status.Qualification,folderPath));}catch(Exception){if(full is not null&&File.Exists(full))try{File.Delete(full);}catch{}if(failures.Count<25_000)failures.Add(new("message",id,"extract_failed"));complete=false;}}
    }
    catch(Exception){outcome=messages.Count>0?"partial_recovered":"unreadable_source";complete=false;failures.Add(new("store",null,"open_or_scan_failed"));}
    source.Position=0;string after=Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();if(!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(after),System.Text.Encoding.UTF8.GetBytes(request.SourceSha256)))return 7;
    Write(outcome,null,complete);return 0;
    void Write(string state,int? total,bool enumerationComplete){var result=new DamagedStoreWorkerResult(request.SchemaVersion,request.InvocationId,request.JobId,request.SourceSha256,state,total,messages,failures,enumerationComplete,nonMailItemCount);string temp=resultPath+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(result));File.Move(temp,resultPath,true);}
}
catch{return 10;}
