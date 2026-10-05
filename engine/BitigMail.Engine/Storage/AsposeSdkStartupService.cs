using Aspose.Email.Mapi;

namespace BitigMail.Engine.Storage;

public sealed record AsposeSdkStatus(string LicenseState, string InitializationState, bool RestartRequired, string Qualification, string? ErrorCode);

public sealed class AsposeSdkStartupService
{
    public static AsposeSdkStatus CurrentStatus { get; private set; } = new("unlicensed","not_initialized",false,"LICENSED_OUTPUT_ACCEPTANCE_PENDING",null);
    private readonly AsposeSdkStatus _status;
    public AsposeSdkStartupService(string? configuredLicensePath)
    {
        string licenseState = "unlicensed", qualification = "LICENSED_OUTPUT_ACCEPTANCE_PENDING"; string? error = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(configuredLicensePath))
            {
                string full = Path.GetFullPath(configuredLicensePath); using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                new Aspose.Email.License().SetLicense(stream); licenseState = "licensed_loaded"; qualification = "LICENSED_OUTPUT_ACCEPTANCE_PENDING";
            }
        }
        catch (Exception ex) { licenseState = "configuration_error"; error = ex.GetType().Name; }
        try
        {
            using var message = new MapiMessage(); message.SetProperty(KnownPropertyList.Body, "BitigMail SDK serial startup warmup"); message.SetProperty(KnownPropertyList.InternetCodepage, 65001L);
            _status = new(licenseState, "initialized", false, qualification, error); CurrentStatus=_status;
        }
        catch (Exception ex) { _status = new(licenseState, "initialization_error", false, qualification, ex.GetType().Name); CurrentStatus=_status; }
    }
    public AsposeSdkStartupService(byte[]? configuredLicenseBytes)
    {
        string state="unlicensed",qualification="LICENSED_OUTPUT_ACCEPTANCE_PENDING";string? error=null;
        try{if(configuredLicenseBytes is {Length:>0}){using var stream=new MemoryStream(configuredLicenseBytes,false);new Aspose.Email.License().SetLicense(stream);state="licensed_loaded";}}
        catch(Exception ex){state="configuration_error";error=ex.GetType().Name;}
        finally{if(configuredLicenseBytes is not null)System.Security.Cryptography.CryptographicOperations.ZeroMemory(configuredLicenseBytes);}
        try{using var message=new MapiMessage();message.SetProperty(KnownPropertyList.Body,"BitigMail SDK serial startup warmup");message.SetProperty(KnownPropertyList.InternetCodepage,65001L);_status=new(state,"initialized",false,qualification,error);}catch(Exception ex){_status=new(state,"initialization_error",false,qualification,ex.GetType().Name);}CurrentStatus=_status;
    }
    public AsposeSdkStatus Status => _status;
    public void EnsureReady() { if (_status.InitializationState != "initialized" || _status.LicenseState == "configuration_error") throw new InvalidOperationException($"Aspose SDK hazır değil: {_status.ErrorCode ?? _status.InitializationState}"); }
}
