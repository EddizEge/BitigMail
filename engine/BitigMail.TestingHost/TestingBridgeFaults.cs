using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Security;
using MailKit;
using MimeKit;

namespace BitigMail.TestingHost;

public enum BridgeTestFaultKind
{
    None,
    LostResponseAfterAppend,
    PauseAfterAppendBeforeReturn,
    PauseBeforeAppend,
    PauseAfterExportItemPersisted,
    FailAppendWithoutWrite
}

public sealed class TestingBridgeFaultService
{
    private readonly string _runtimeDir;
    private readonly object _lock = new();

    public TestingBridgeFaultService(string runtimeDir)
    {
        _runtimeDir = runtimeDir;
    }

    public BridgeTestFaultKind ActiveFault { get; set; } = BridgeTestFaultKind.None;
    public int TargetOrdinal { get; set; } = 1;
    public int AppendCallCount = 0;
    public int ExportFetchCallCount = 0;
    public TaskCompletionSource<bool>? PauseTcs { get; set; }

    public string SignalFilePath => Path.Combine(_runtimeDir, "bridge-pause.signal");

    public void Reset()
    {
        lock (_lock)
        {
            ActiveFault = BridgeTestFaultKind.None;
            TargetOrdinal = 1;
            AppendCallCount = 0;
            ExportFetchCallCount = 0;
            PauseTcs?.TrySetResult(true);
            PauseTcs = null;
            try
            {
                if (File.Exists(SignalFilePath)) File.Delete(SignalFilePath);
            }
            catch { }
        }
    }

    public void WriteSignal(string stage, int ordinal, string folder = "", uint? appendedUid = null)
    {
        try
        {
            var info = new
            {
                stage,
                ordinal,
                folder,
                appendedUid,
                pid = Environment.ProcessId,
                timestamp = DateTimeOffset.UtcNow
            };
            File.WriteAllText(SignalFilePath, JsonSerializer.Serialize(info));
        }
        catch { }
    }
}

public sealed class TestingTransferClientFactory : IImapTransferClientFactory
{
    private readonly IImapTransferClientFactory _innerFactory;
    private readonly TestingBridgeFaultService _faultService;

    public TestingTransferClientFactory(
        IImapTransferClientFactory innerFactory,
        TestingBridgeFaultService faultService)
    {
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
        _faultService = faultService ?? throw new ArgumentNullException(nameof(faultService));
    }

    public IImapTransferClient CreateClient()
    {
        var inner = _innerFactory.CreateClient();
        return new TestingFaultTransferClient(inner, _faultService);
    }
}

public sealed class TestingFaultTransferClient : IImapTransferClient
{
    private readonly IImapTransferClient _inner;
    private readonly TestingBridgeFaultService _faults;
    private string? _connectedHost;
    private int _connectedPort;

    private static readonly HashSet<string> AllowlistedExportFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "INBOX",
        "Gönderilenler",
        "Projeler/İstanbul"
    };

    public TestingFaultTransferClient(IImapTransferClient inner, TestingBridgeFaultService faults)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _faults = faults ?? throw new ArgumentNullException(nameof(faults));
    }

    public void Dispose() => _inner.Dispose();

    private bool IsAuthorizedEndpoint =>
        string.Equals(_connectedHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        && _connectedPort == 5143;

    private bool IsAuthorizedImportTarget(string folderPath)
    {
        return IsAuthorizedEndpoint
            && !string.IsNullOrWhiteSpace(folderPath)
            && folderPath.StartsWith("TASK017-", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAuthorizedExportSource(string folderPath)
    {
        return IsAuthorizedEndpoint
            && !string.IsNullOrWhiteSpace(folderPath)
            && (AllowlistedExportFolders.Contains(folderPath) || folderPath.StartsWith("TASK017-", StringComparison.OrdinalIgnoreCase));
    }

    public Task ConnectAndAuthenticateAsync(string host, int port, string tlsMode, string username, string password, CancellationToken ct)
    {
        _connectedHost = host;
        _connectedPort = port;
        return _inner.ConnectAndAuthenticateAsync(host, port, tlsMode, username, password, ct);
    }

    public Task ConnectAndAuthenticateAsync(string host, int port, string tlsMode, ImapConnectionCredential credential, CancellationToken ct)
    {
        _connectedHost = host;
        _connectedPort = port;
        return _inner.ConnectAndAuthenticateAsync(host, port, tlsMode, credential, ct);
    }

    public Task<uint> GetFolderUidValidityAsync(string folderPath, CancellationToken ct)
        => _inner.GetFolderUidValidityAsync(folderPath, ct);

    public Task<bool> SupportsUserKeywordsAsync(string folderPath, CancellationToken ct)
        => _inner.SupportsUserKeywordsAsync(folderPath, ct);

    public Task<IReadOnlyList<ImapSourceMessageSummary>> InspectSourceFolderAsync(string folderPath, CancellationToken ct)
        => _inner.InspectSourceFolderAsync(folderPath, ct);

    public async Task<uint> AppendMessageAsync(
        string folderPath,
        MimeMessage message,
        MessageFlags flags,
        HashSet<string> keywords,
        DateTimeOffset? internalDate,
        CancellationToken ct)
    {
        bool isTargetGuarded = IsAuthorizedImportTarget(folderPath);
        int callIndex = isTargetGuarded ? Interlocked.Increment(ref _faults.AppendCallCount) : 0;

        if (isTargetGuarded && _faults.ActiveFault == BridgeTestFaultKind.PauseBeforeAppend && callIndex == _faults.TargetOrdinal)
        {
            _faults.WriteSignal("pause-before-append", callIndex, folderPath);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _faults.PauseTcs = tcs;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await tcs.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Test pause timed out waiting for signal release or process kill.");
            }
        }

        if (isTargetGuarded && _faults.ActiveFault == BridgeTestFaultKind.FailAppendWithoutWrite && callIndex == _faults.TargetOrdinal)
        {
            throw new IOException("Simulated network timeout before IMAP server write.");
        }

        // Real append to server
        uint appendedUid = await _inner.AppendMessageAsync(folderPath, message, flags, keywords, internalDate, ct);

        // Pause after server append BEFORE returning to caller
        if (isTargetGuarded && _faults.ActiveFault == BridgeTestFaultKind.PauseAfterAppendBeforeReturn && callIndex == _faults.TargetOrdinal)
        {
            _faults.WriteSignal("pause-after-append-before-return", callIndex, folderPath, appendedUid);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _faults.PauseTcs = tcs;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await tcs.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Test pause timed out waiting for signal release or process kill.");
            }
        }

        // Lost response after successful append to server
        if (isTargetGuarded && _faults.ActiveFault == BridgeTestFaultKind.LostResponseAfterAppend && callIndex == _faults.TargetOrdinal)
        {
            throw new IOException("Simulated network connection dropped / lost response immediately following server APPEND.");
        }

        return appendedUid;
    }

    public Task<IReadOnlyList<uint>> SearchByKeywordAsync(string folderPath, string keyword, CancellationToken ct)
        => _inner.SearchByKeywordAsync(folderPath, keyword, ct);

    public Task<ImapTargetVerificationResult> FetchAndVerifyAsync(string folderPath, uint uid, CancellationToken ct)
        => _inner.FetchAndVerifyAsync(folderPath, uid, ct);

    public async Task<ImapCompositeTargetVerificationResult> VerifyTargetItemAsync(
        string folderPath, uint uid, string keyword, CancellationToken ct)
    {
        if (_faults.ActiveFault == BridgeTestFaultKind.None)
        {
            return await _inner.VerifyTargetItemAsync(folderPath, uid, keyword, ct);
        }

        uint validity = await GetFolderUidValidityAsync(folderPath, ct);
        var message = await FetchAndVerifyAsync(folderPath, uid, ct);
        var matches = await SearchByKeywordAsync(folderPath, keyword, ct);
        return new ImapCompositeTargetVerificationResult
        {
            UidValidity = validity,
            Message = message,
            MatchingKeywordUids = matches
        };
    }

    public Task<IReadOnlyList<ImapMessageHeaderSummary>> FetchHeaderSummariesAsync(string folderPath, CancellationToken ct)
        => _inner.FetchHeaderSummariesAsync(folderPath, ct);

    public async Task<ImapSourceMessageSummary?> FetchSingleSourceMessageAsync(string folderPath, uint uid, CancellationToken ct)
    {
        bool isSourceGuarded = IsAuthorizedExportSource(folderPath);
        int callIndex = isSourceGuarded ? Interlocked.Increment(ref _faults.ExportFetchCallCount) : 0;

        if (isSourceGuarded && _faults.ActiveFault == BridgeTestFaultKind.PauseAfterExportItemPersisted && callIndex == _faults.TargetOrdinal)
        {
            _faults.WriteSignal("pause-after-export-item-persisted", callIndex, folderPath);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _faults.PauseTcs = tcs;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await tcs.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Test pause timed out waiting for signal release or process kill.");
            }
        }

        return await _inner.FetchSingleSourceMessageAsync(folderPath, uid, ct);
    }

    public Task DisconnectAsync(CancellationToken ct)
        => _inner.DisconnectAsync(ct);
}
