using System.Threading.Tasks;

namespace BitigMail.LocalHost.Dialogs;

public interface IFilePickerService
{
    Task<FilePickResult> PickSourceOstAsync();
    Task<FilePickResult> PickTargetPstAsync(string? sourceHandle = null);
    Task<FilePickResult> PickSplitSourceAsync();
    Task<FilePickResult> PickOutputDirAsync();
    Task<FilePickResult> PickMimeSourceAsync(string mode = "eml-files");
    Task<FilePickResult> PickArchiveSourceAsync(string mode = "eml-files");
    Task<FilePickResult> PickEmlxSourceAsync(string mode = "tree");
    Task<FilePickResult> PickOutlookEmlSourceAsync();
    Task<FilePickResult> PickAsposeLicenseAsync();
    Task<FilePickResult> PickArchiveBackupAsync();
}
