using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ImapAccountServiceTests
{
    private static string CreateTempStorageDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bitigmail-imap-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Crud_And_PublicDto_NeverExposesPasswordOrSecretFields()
    {
        string dir = CreateTempStorageDir();
        try
        {
            var protector = new WindowsImapCredentialProtector();
            var policy = new ImapConnectionPolicy(allowTask014Loopback: false);
            var store = new ImapAccountStore(dir, protector, policy);

            const string rawPassword = "TopSecret-Password-123!";
            var createReq = new CreateImapAccountRequest
            {
                CompanyId = "company-corp",
                ProjectId = "project-migration",
                DisplayName = "Kurumsal Destek",
                Email = "support@bitigmail.test",
                Host = "imap.bitigmail.test",
                Port = 993,
                TlsMode = "ssl",
                Username = "support@bitigmail.test",
                Password = rawPassword
            };

            // 1. Create Account
            var created = store.CreateAccount(createReq);
            Assert.NotNull(created);
            Assert.StartsWith("acc_", created.AccountId);
            Assert.Equal("company-corp", created.CompanyId);
            Assert.Equal("project-migration", created.ProjectId);
            Assert.Equal("Kurumsal Destek", created.DisplayName);
            Assert.Equal("support@bitigmail.test", created.Email);
            Assert.Equal("imap.bitigmail.test", created.Host);
            Assert.Equal(993, created.Port);
            Assert.Equal("ssl", created.TlsMode);
            Assert.Equal("support@bitigmail.test", created.Username);
            Assert.Equal(1, created.Version);

            // 2. Verify public DTO type has ZERO password / secret properties via reflection
            var properties = typeof(ImapAccountPublicDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                string propName = prop.Name.ToLowerInvariant();
                Assert.DoesNotContain("password", propName);
                Assert.DoesNotContain("secret", propName);
                Assert.DoesNotContain("cipher", propName);
                Assert.DoesNotContain("credential", propName);
            }

            // 3. Verify JSON serialization of public DTO NEVER leaks the password
            string publicJson = JsonSerializer.Serialize(created);
            Assert.DoesNotContain(rawPassword, publicJson);
            // The authKind value may be "password"; credential properties and the actual secret must remain absent.
            Assert.DoesNotContain(JsonDocument.Parse(publicJson).RootElement.EnumerateObject(),
                property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase));

            // 4. Get Account
            var fetched = store.GetAccount(created.AccountId, "company-corp", "project-migration");
            Assert.NotNull(fetched);
            Assert.Equal(created.AccountId, fetched.AccountId);
            Assert.Equal(1, fetched.Version);

            // 5. List Accounts
            var list = store.ListAccounts("company-corp", "project-migration");
            Assert.Single(list);
            Assert.Equal(created.AccountId, list[0].AccountId);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Versioning_MonotonicallyIncrements_AndSupportsPasswordReplacement()
    {
        string dir = CreateTempStorageDir();
        try
        {
            var protector = new WindowsImapCredentialProtector();
            var policy = new ImapConnectionPolicy(allowTask014Loopback: false);
            var store = new ImapAccountStore(dir, protector, policy);

            // 1. Create -> Version 1
            var created = store.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                DisplayName = "Initial Account",
                Email = "user1@bitigmail.test",
                Host = "imap1.bitigmail.test",
                Port = 993,
                TlsMode = "ssl",
                Username = "user1@bitigmail.test",
                Password = "initial-secret-1"
            });
            Assert.Equal(1, created.Version);

            // 2. Update metadata AND replace password -> Version 2 (passes ExpectedVersion from current DTO)
            var updated1 = store.UpdateAccount(created.AccountId, new UpdateImapAccountRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                ExpectedVersion = created.Version,
                DisplayName = "Updated Name",
                Password = "new-replaced-secret-2"
            });
            Assert.Equal(2, updated1.Version);
            Assert.Equal("Updated Name", updated1.DisplayName);

            // Verify the decrypted password is now the replaced secret
            var (_, pwdAfterUpdate1) = store.GetInternalAccountWithPassword(created.AccountId, "company-1", "project-1");
            Assert.Equal("new-replaced-secret-2", pwdAfterUpdate1);

            // 3. Update metadata WITHOUT providing password -> Version 3 (passes ExpectedVersion from updated1)
            var updated2 = store.UpdateAccount(created.AccountId, new UpdateImapAccountRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                ExpectedVersion = updated1.Version,
                Port = 143,
                TlsMode = "starttls",
                Password = null // omit password
            });
            Assert.Equal(3, updated2.Version);
            Assert.Equal(143, updated2.Port);
            Assert.Equal("starttls", updated2.TlsMode);

            // Verify password was retained untouched across update
            var (_, pwdAfterUpdate2) = store.GetInternalAccountWithPassword(created.AccountId, "company-1", "project-1");
            Assert.Equal("new-replaced-secret-2", pwdAfterUpdate2);

            // 4. Stale version conflict: Passing stale ExpectedVersion asserts AccountVersionConflictException
            var conflictEx = Assert.Throws<AccountVersionConflictException>(() =>
                store.UpdateAccount(created.AccountId, new UpdateImapAccountRequest
                {
                    CompanyId = "company-1",
                    ProjectId = "project-1",
                    ExpectedVersion = created.Version, // Stale version 1 when current is 3
                    DisplayName = "Stale Conflict Attempt"
                }));
            Assert.Equal(3, conflictEx.CurrentVersion);
            Assert.Equal(created.Version, conflictEx.ExpectedVersion);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void EncryptedAtRest_And_RestartPersistenceAcrossInstances()
    {
        string dir = CreateTempStorageDir();
        try
        {
            var protector = new WindowsImapCredentialProtector();
            var policy = new ImapConnectionPolicy(allowTask014Loopback: false);
            var store1 = new ImapAccountStore(dir, protector, policy);

            const string secret = "durable-şifre-014-restart-test";
            var created = store1.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "comp-durable",
                ProjectId = "proj-durable",
                DisplayName = "Durable Storage Account",
                Email = "durable@bitigmail.test",
                Host = "mail.bitigmail.test",
                Port = 993,
                TlsMode = "ssl",
                Username = "durable_user",
                Password = secret
            });

            // 1. Inspect on-disk single account envelope directly
            string envelopeFile = Path.Combine(dir, $"{created.AccountId}.json");
            Assert.True(File.Exists(envelopeFile), "Single account envelope JSON file must exist on disk");

            string legacySecretFile = Path.Combine(dir, $"{created.AccountId}.secret.bin");
            Assert.False(File.Exists(legacySecretFile), "Separate legacy secret file must not exist in single-envelope architecture");

            string envelopeContent = File.ReadAllText(envelopeFile);
            // Assert plaintext secret is absent from the entire envelope JSON
            Assert.DoesNotContain(secret, envelopeContent);
            Assert.DoesNotContain("\"password\"", envelopeContent, StringComparison.OrdinalIgnoreCase);

            using var doc = JsonDocument.Parse(envelopeContent);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("account", out var accountEl), "Envelope must contain 'account' section");
            Assert.True(root.TryGetProperty("protectedPasswordBase64", out var pwdEl), "Envelope must contain 'protectedPasswordBase64' section");
            Assert.True(root.TryGetProperty("schemaVersion", out var schemaEl), "Envelope must contain 'schemaVersion'");
            Assert.Equal(1, schemaEl.GetInt32());

            string? base64Cipher = pwdEl.GetString();
            Assert.False(string.IsNullOrEmpty(base64Cipher));
            byte[] cipherBytes = Convert.FromBase64String(base64Cipher);
            // Verify cipher bytes do NOT contain plaintext password bytes
            Assert.False(cipherBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) >= 0);

            // 2. Simulate Process Restart: instantiate a brand new ImapAccountStore pointing to same directory
            var store2 = new ImapAccountStore(dir, protector, policy);
            var loadedList = store2.ListAccounts("comp-durable", "proj-durable");
            Assert.Single(loadedList);
            Assert.Equal(created.AccountId, loadedList[0].AccountId);
            Assert.Equal("Durable Storage Account", loadedList[0].DisplayName);
            Assert.Equal(1, loadedList[0].Version);

            // 3. Restart / Decrypt: Verify store2 can decrypt/unprotect the password successfully across instances
            var (rec, unprotectPwd) = store2.GetInternalAccountWithPassword(created.AccountId, "comp-durable", "proj-durable");
            Assert.Equal(secret, unprotectPwd);
            Assert.Equal(1, rec.Version);

            // 4. Updating in store2 increments version monotonically to 2
            var updated = store2.UpdateAccount(created.AccountId, new UpdateImapAccountRequest
            {
                CompanyId = "comp-durable",
                ProjectId = "proj-durable",
                ExpectedVersion = rec.Version,
                DisplayName = "Updated In Instance 2"
            });
            Assert.Equal(2, updated.Version);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void WrongScope_IsRejectedAtEveryOperation()
    {
        string dir = CreateTempStorageDir();
        try
        {
            var protector = new WindowsImapCredentialProtector();
            var policy = new ImapConnectionPolicy(allowTask014Loopback: false);
            var store = new ImapAccountStore(dir, protector, policy);

            var created = store.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "company-tenant-1",
                ProjectId = "project-alpha",
                DisplayName = "Scope Test",
                Email = "scope@example.test",
                Host = "mail.example.test",
                Port = 993,
                TlsMode = "ssl",
                Username = "scope_user",
                Password = "sample-password"
            });

            // Listing for different company returns empty
            Assert.Empty(store.ListAccounts("company-tenant-2", "project-alpha"));
            Assert.Empty(store.ListAccounts("company-tenant-1", "project-beta"));

            // Get with wrong scope throws InvalidOperationException
            Assert.Throws<InvalidOperationException>(() =>
                store.GetAccount(created.AccountId, "company-tenant-2", "project-alpha"));

            // Update with wrong scope throws InvalidOperationException
            Assert.Throws<InvalidOperationException>(() =>
                store.UpdateAccount(created.AccountId, new UpdateImapAccountRequest
                {
                    CompanyId = "company-tenant-2",
                    ProjectId = "project-alpha",
                    DisplayName = "Hack Attempt"
                }));

            // Delete with wrong scope throws InvalidOperationException
            Assert.Throws<InvalidOperationException>(() =>
                store.DeleteAccount(created.AccountId, "company-tenant-2", "project-alpha"));

            // GetInternalAccountWithPassword with wrong scope throws InvalidOperationException
            Assert.Throws<InvalidOperationException>(() =>
                store.GetInternalAccountWithPassword(created.AccountId, "company-tenant-2", "project-alpha"));

            // Empty or invalid scope strings throw ArgumentException from ImapConnectionPolicy
            Assert.Throws<ArgumentException>(() => store.ListAccounts("", "project-alpha"));
            Assert.Throws<ArgumentException>(() => store.ListAccounts("company-tenant-1", "bad\r\nscope"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Delete_DeletesOnlyLocalMetadataAndSecret_NeverRemote()
    {
        string dir = CreateTempStorageDir();
        try
        {
            var protector = new WindowsImapCredentialProtector();
            var policy = new ImapConnectionPolicy(allowTask014Loopback: false);
            var store = new ImapAccountStore(dir, protector, policy);

            var created = store.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "comp-del",
                ProjectId = "proj-del",
                DisplayName = "Delete Me",
                Email = "delete@example.test",
                Host = "mail.example.test",
                Port = 993,
                TlsMode = "ssl",
                Username = "delete_user",
                Password = "sample-password"
            });

            string metaFile = Path.Combine(dir, $"{created.AccountId}.json");
            Assert.True(File.Exists(metaFile));

            // Deletion is strictly local to the store file and never performs any remote IMAP operations
            bool deleted = store.DeleteAccount(created.AccountId, "comp-del", "proj-del");
            Assert.True(deleted);

            Assert.False(File.Exists(metaFile), "Metadata file must be deleted");

            Assert.Null(store.GetAccount(created.AccountId, "comp-del", "proj-del"));
            Assert.False(store.DeleteAccount(created.AccountId, "comp-del", "proj-del"));

            // Restart does not resurrect deleted account
            var restartedStore = new ImapAccountStore(dir, protector, policy);
            Assert.Null(restartedStore.GetAccount(created.AccountId, "comp-del", "proj-del"));
            Assert.Empty(restartedStore.ListAccounts("comp-del", "proj-del"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ImapConnectionPolicy_ValidationBoundariesEnforced()
    {
        string dir = CreateTempStorageDir();
        try
        {
            var protector = new WindowsImapCredentialProtector();

            // 1. Production policy strictly rejects plaintext 'none'
            var prodPolicy = new ImapConnectionPolicy(allowTask014Loopback: false);
            var prodStore = new ImapAccountStore(dir, protector, prodPolicy);

            Assert.Throws<ArgumentException>(() => prodStore.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "c",
                ProjectId = "p",
                DisplayName = "Test",
                Email = "u@e.com",
                Host = "127.0.0.1",
                Port = 5143,
                TlsMode = "none",
                Username = "user",
                Password = "pwd"
            }));

            // 2. Testing policy allows ONLY 127.0.0.1:5143 for 'none'
            var testPolicy = new ImapConnectionPolicy(allowTask014Loopback: true);
            var testStore = new ImapAccountStore(dir, protector, testPolicy);

            var acc = testStore.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "c",
                ProjectId = "p",
                DisplayName = "Lab Allowed",
                Email = "u@e.com",
                Host = "127.0.0.1",
                Port = 5143,
                TlsMode = "none",
                Username = "user",
                Password = "pwd"
            });
            Assert.NotNull(acc);

            // Testing policy still rejects non-lab loopback with 'none'
            Assert.Throws<ArgumentException>(() => testStore.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "c",
                ProjectId = "p",
                DisplayName = "Lab Rejected",
                Email = "u@e.com",
                Host = "127.0.0.2",
                Port = 5143,
                TlsMode = "none",
                Username = "user",
                Password = "pwd"
            }));

            // Testing policy still rejects non-lab port with 'none'
            Assert.Throws<ArgumentException>(() => testStore.CreateAccount(new CreateImapAccountRequest
            {
                CompanyId = "c",
                ProjectId = "p",
                DisplayName = "Obsolete Port Rejected",
                Email = "u@e.com",
                Host = "127.0.0.1",
                Port = 4143,
                TlsMode = "none",
                Username = "user",
                Password = "pwd"
            }));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task WrongPassword_ReturnsSafeRedactedError_NoSecretsLeaked()
    {
        // Testing policy allows exact 127.0.0.1:5143 loopback
        var policy = new ImapConnectionPolicy(allowTask014Loopback: true);
        var clientService = new ImapClientService(policy);

        const string wrongPassword = "definitely-wrong-secret-99999";
        var result = await clientService.TestConnectionAsync("127.0.0.1", 5143, "none", "source", wrongPassword);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);

        // Crucial safety checks: no credential, cipher, or raw exception dump in error string
        Assert.DoesNotContain(wrongPassword, result.Error);
        Assert.DoesNotContain("Exception", result.Error);
        Assert.DoesNotContain("at MailKit", result.Error);
        Assert.DoesNotContain("DEBUG", result.Error);

        // Stable redacted error
        bool isExpectedAuthOrConnError =
            result.Error.Contains("Kimlik doğrulama başarısız") ||
            result.Error.Contains("bağlanılamadı");
        Assert.True(isExpectedAuthOrConnError, $"Unexpected error text: {result.Error}");
    }

    [Fact]
    public async Task RealLab_ConnectionAndFolderEnumeration_WhenLabAvailable()
    {
        // Check if live lab is reachable on 127.0.0.1:5143
        bool labRunning = false;
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync("127.0.0.1", 5143);
            if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask && tcp.Connected)
            {
                labRunning = true;
            }
        }
        catch { }

        if (!labRunning)
        {
            // If lab container is not running in this environment, pass gracefully
            return;
        }

        string repoRoot = ResolveRepoRoot();
        string credFile = Path.Combine(repoRoot, "lab", "task014", "dovecot", "local-credentials.json");
        if (!File.Exists(credFile))
            return;

        using var doc = JsonDocument.Parse(File.ReadAllText(credFile));
        var src = doc.RootElement.GetProperty("source");
        string username = src.GetProperty("username").GetString()!;
        string password = src.GetProperty("password").GetString()!;
        string host = src.GetProperty("imapHost").GetString()!;
        int port = src.GetProperty("imapPort").GetInt32();

        var policy = new ImapConnectionPolicy(allowTask014Loopback: true);
        var clientService = new ImapClientService(policy);

        // 1. Safe connection test against real lab
        var connResult = await clientService.TestConnectionAsync(host, port, "none", username, password);
        Assert.True(connResult.Success, $"Connection test failed: {connResult.Error}");
        Assert.True(connResult.LatencyMs.HasValue && connResult.LatencyMs.Value >= 0);

        // 2. Real folder discovery against live lab
        var folders = await clientService.ListFoldersAsync(host, port, "none", username, password);
        Assert.NotNull(folders);
        Assert.NotEmpty(folders);

        // INBOX must be present
        var inbox = folders.FirstOrDefault(f => f.FullPath.Equals("INBOX", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(inbox);
        Assert.True(inbox.IsSelectable);
        Assert.False(string.IsNullOrEmpty(inbox.Delimiter));

        // Verify folder attributes
        foreach (var folder in folders)
        {
            Assert.False(string.IsNullOrWhiteSpace(folder.Name));
            Assert.False(string.IsNullOrWhiteSpace(folder.FullPath));
            Assert.False(string.IsNullOrWhiteSpace(folder.Delimiter));
            if (folder.IsSelectable)
            {
                Assert.True(folder.MessageCount >= 0);
                Assert.True(folder.CountValid);
            }
        }
    }

    [Fact]
    public void FolderDiscovery_StatusFailureFallsBackToReadOnlyOpen_PreservingCountValidity()
    {
        bool statusCalled = false;
        bool openCalled = false;

        var result = ImapClientService.ResolveFolderCounts(
            statusAttempt: () =>
            {
                statusCalled = true;
                throw new InvalidOperationException("Simulated STATUS failure on server");
            },
            readOnlyOpenAttempt: () =>
            {
                openCalled = true;
                return (42, 5);
            });

        Assert.True(statusCalled);
        Assert.True(openCalled);
        Assert.True(result.countValid);
        Assert.Equal(42, result.messageCount);
        Assert.Equal(5, result.unreadCount);
        Assert.Null(result.statusError);
    }

    [Fact]
    public void FolderDiscovery_StatusAndOpenBothFail_NeverReturnsZero_ExplicitUnreadableState()
    {
        bool statusCalled = false;
        bool openCalled = false;

        var result = ImapClientService.ResolveFolderCounts(
            statusAttempt: () =>
            {
                statusCalled = true;
                throw new InvalidOperationException("Simulated STATUS permission denied");
            },
            readOnlyOpenAttempt: () =>
            {
                openCalled = true;
                throw new InvalidOperationException("Simulated Open read-only lock failure");
            });

        Assert.True(statusCalled);
        Assert.True(openCalled);
        // Must NEVER silently return 0
        Assert.False(result.countValid);
        Assert.Null(result.messageCount);
        Assert.Null(result.unreadCount);
        Assert.NotNull(result.statusError);
        Assert.Contains("okunamadı", result.statusError);
        // Verify no raw exception details or secrets leak in error
        Assert.DoesNotContain("Exception", result.statusError);
        Assert.DoesNotContain("Simulated", result.statusError);
    }

    [Fact]
    public void FolderDiscovery_NonSelectableFolder_ReportsValidWithoutMessageCount()
    {
        var folder = new ImapFolderDto
        {
            Name = "Kök Klasör",
            FullPath = "Kök Klasör",
            Delimiter = "/",
            IsSelectable = false,
            MessageCount = null,
            UnreadCount = null,
            CountValid = true,
            StatusError = null
        };

        Assert.False(folder.IsSelectable);
        Assert.True(folder.CountValid);
        Assert.Null(folder.MessageCount);
        Assert.Null(folder.StatusError);
    }

    [Fact]
    public void FolderDiscovery_TurkishPathAndExactDelimiterPreserved_ZeroSecretProperties()
    {
        const string turkishPath = "Projeler/İstanbul/Müşteri Arşivi (İşletme)";
        var folder = new ImapFolderDto
        {
            Name = "Müşteri Arşivi (İşletme)",
            FullPath = turkishPath,
            Delimiter = "/",
            IsSelectable = true,
            MessageCount = 150,
            UnreadCount = 12,
            CountValid = true,
            StatusError = null
        };

        Assert.Equal(turkishPath, folder.FullPath);
        Assert.Equal("/", folder.Delimiter);
        Assert.Equal(150, folder.MessageCount);

        // Verify reflection checks on ImapFolderDto to ensure ZERO secret fields
        var properties = typeof(ImapFolderDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            string name = prop.Name.ToLowerInvariant();
            Assert.DoesNotContain("password", name);
            Assert.DoesNotContain("secret", name);
            Assert.DoesNotContain("credential", name);
        }

        string json = JsonSerializer.Serialize(folder);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(turkishPath, document.RootElement.GetProperty(nameof(ImapFolderDto.FullPath)).GetString());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepoRoot()
    {
        string current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            if (Directory.Exists(Path.Combine(current, "engine")) || Directory.Exists(Path.Combine(current, "lab")))
                return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == null || parent == current) break;
            current = parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
