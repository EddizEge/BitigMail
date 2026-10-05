using System.Security.Cryptography;

namespace BitigMail.LocalHost.Security;
public sealed class AsposeLicenseConfigurationStore
{
    public const int MaxPlainBytes=1024*1024,MaxProtectedBytes=2*1024*1024; private const string Identity="aspose_license_config"; private readonly string _path; private readonly IImapCredentialProtector _protector;private readonly object _gate=new();
    public AsposeLicenseConfigurationStore(string runtimeDir,IImapCredentialProtector protector){_path=Path.Combine(runtimeDir,"sdk-license.protected");_protector=protector;}
    public void SaveFromFile(string selectedPath){lock(_gate){string full=Path.GetFullPath(selectedPath);var info=new FileInfo(full);if(!info.Exists||info.Attributes.HasFlag(FileAttributes.Directory)||info.Attributes.HasFlag(FileAttributes.ReparsePoint)||info.Length>MaxPlainBytes)throw new InvalidDataException("Lisans dosyası geçersiz veya boyut sınırını aşıyor.");RejectStorageReparse();byte[] bytes=ReadBounded(full,MaxPlainBytes);try{string encoded=Convert.ToBase64String(bytes);byte[] cipher=_protector.Protect(encoded,Identity);if(cipher.Length>MaxProtectedBytes)throw new InvalidDataException("Korumalı lisans yapılandırması boyut sınırını aşıyor.");string temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){fs.Write(cipher);fs.Flush(true);}File.Move(temp,_path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}finally{CryptographicOperations.ZeroMemory(bytes);}}}
    public byte[]? Load(){lock(_gate){RejectStorageReparse();if(!File.Exists(_path))return null;var info=new FileInfo(_path);if(info.Attributes.HasFlag(FileAttributes.ReparsePoint)||info.Length>MaxProtectedBytes)throw new InvalidDataException("Korumalı lisans yapılandırması geçersiz.");byte[] cipher=ReadBounded(_path,MaxProtectedBytes);try{string encoded=_protector.Unprotect(cipher,Identity);byte[] plain=Convert.FromBase64String(encoded);if(plain.Length>MaxPlainBytes){CryptographicOperations.ZeroMemory(plain);throw new InvalidDataException("Lisans yapılandırması boyut sınırını aşıyor.");}return plain;}finally{CryptographicOperations.ZeroMemory(cipher);}}}
    private void RejectStorageReparse(){string dir=Path.GetDirectoryName(_path)!;Directory.CreateDirectory(dir);for(string? part=dir;part is not null;part=Directory.GetParent(part)?.FullName)if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Bağlantılı lisans depolama yolu kabul edilmez.");if(File.Exists(_path)&&(File.GetAttributes(_path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Bağlantılı lisans yapılandırması kabul edilmez.");}
    private static byte[] ReadBounded(string path,int max){using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(fs.Length>max)throw new InvalidDataException("Lisans yapılandırması boyut sınırını aşıyor.");using var ms=new MemoryStream();byte[] buffer=new byte[81920];int read;while((read=fs.Read(buffer))>0){if(ms.Length+read>max)throw new InvalidDataException("Lisans yapılandırması okuma sırasında büyüdü.");ms.Write(buffer,0,read);}return ms.ToArray();}
    public static byte[] ReadSelectedLicense(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? part = full; part is not null; part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bağlantılı lisans yolu kabul edilmez.");
        if (!File.Exists(full)) throw new InvalidDataException("Lisans dosyası bulunamadı.");
        byte[] result = ReadBounded(full, MaxPlainBytes);
        if (result.Length == 0) throw new InvalidDataException("Lisans dosyası boş.");
        return result;
    }
}
