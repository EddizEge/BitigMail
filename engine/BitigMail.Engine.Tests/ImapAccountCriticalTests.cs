using BitigMail.Engine.Imap;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

/// <summary>Independent disk-failure and ownership acceptance; uses real Windows DPAPI and file locks.</summary>
public sealed class ImapAccountCriticalTests
{
    private static ImapAccountStore Open(string path) => new(path, new WindowsImapCredentialProtector(), new ImapConnectionPolicy());
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "bitigmail-task014-account-faults", Guid.NewGuid().ToString("N"));
    private static CreateImapAccountRequest Create() => new()
    {
        CompanyId="company-root", ProjectId="project-root", DisplayName="Synthetic account", Email="account@example.test",
        Host="imap.example.test", Port=993, TlsMode="ssl", Username="account", Password="original-synthetic-secret"
    };
    private static UpdateImapAccountRequest Update(long version)
    {
        var request = new UpdateImapAccountRequest
        {
            CompanyId="company-root", ProjectId="project-root", Host="changed.example.test", Password="replacement-synthetic-secret", ExpectedVersion=version
        };
        return request;
    }

    [Fact]
    public void FailedDiskUpdateKeepsOldPasswordMetadataAndVersionAcrossRestart()
    {
        string directory=NewDirectory(); var store=Open(directory); var account=store.CreateAccount(Create());
        string path=Path.Combine(directory,account.AccountId+".json");
        using (var blocker=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => store.UpdateAccount(account.AccountId,Update(account.Version)));
            var snapshot=store.GetInternalAccountWithPassword(account.AccountId,"company-root","project-root");
            Assert.Equal("imap.example.test",snapshot.Record.Host);
            Assert.Equal(account.Version,snapshot.Record.Version);
            Assert.Equal("original-synthetic-secret",snapshot.Password);
        }
        var restarted=Open(directory).GetInternalAccountWithPassword(account.AccountId,"company-root","project-root");
        Assert.Equal("imap.example.test",restarted.Record.Host);
        Assert.Equal(account.Version,restarted.Record.Version);
        Assert.Equal("original-synthetic-secret",restarted.Password);
    }

    [Fact]
    public void FailedDiskDeleteDoesNotReportSuccessOrRemoveMemoryAccount()
    {
        string directory=NewDirectory(); var store=Open(directory); var account=store.CreateAccount(Create());
        string path=Path.Combine(directory,account.AccountId+".json");
        using (var blocker=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => store.DeleteAccount(account.AccountId,"company-root","project-root"));
            Assert.NotNull(store.GetAccount(account.AccountId,"company-root","project-root"));
        }
        Assert.NotNull(Open(directory).GetAccount(account.AccountId,"company-root","project-root"));
    }

    [Fact]
    public void CallerCannotMutateStoredAccountThroughConnectionSnapshot()
    {
        string directory=NewDirectory(); var store=Open(directory); var account=store.CreateAccount(Create());
        var first=store.GetInternalAccountWithPassword(account.AccountId,"company-root","project-root");
        first.Record.Host="injected.example.test";
        first.Record.Version=long.MaxValue;
        var second=store.GetInternalAccountWithPassword(account.AccountId,"company-root","project-root");
        Assert.Equal("imap.example.test",second.Record.Host);
        Assert.Equal(account.Version,second.Record.Version);
        Assert.Equal("original-synthetic-secret",second.Password);
    }

    [Fact]
    public async Task ConcurrentEditsHaveOneDurableWinnerAndKeepFrozenSnapshot()
    {
        string directory=NewDirectory(); var store=Open(directory); var account=store.CreateAccount(Create());
        var frozen=store.GetInternalAccountWithPassword(account.AccountId,"company-root","project-root");
        var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>
        {
            try { store.UpdateAccount(account.AccountId,Update(account.Version)); return "updated"; }
            catch (AccountVersionConflictException) { return "conflict"; }
        })));
        Assert.Single(outcomes,x=>x=="updated");
        Assert.Single(outcomes,x=>x=="conflict");
        Assert.Equal(account.Version,frozen.Record.Version);
        Assert.Equal("imap.example.test",frozen.Record.Host);
        Assert.Equal("original-synthetic-secret",frozen.Password);
        var reloaded=Open(directory).GetInternalAccountWithPassword(account.AccountId,"company-root","project-root");
        Assert.Equal(account.Version+1,reloaded.Record.Version);
        Assert.Equal("changed.example.test",reloaded.Record.Host);
        Assert.Equal("replacement-synthetic-secret",reloaded.Password);
    }
}
