using System.Collections.Concurrent;
using System.Text.Json;
using Aspose.Email.Mapi;
using BitigMail.Engine.Storage;

bool warm = args.Contains("--warm", StringComparer.Ordinal);
void AccessProperties()
{
    using var message = new MapiMessage();
    message.SetProperty(KnownPropertyList.Body, "synthetic initialization canary");
    message.SetProperty(KnownPropertyList.InternetCodepage, 65001);
}
bool serviceWarm = args.Contains("--service", StringComparer.Ordinal);
if (serviceWarm) new AsposeSdkStartupService((string?)null).EnsureReady(); else if (warm) AccessProperties();
ThreadPool.GetMinThreads(out int workers, out int io); ThreadPool.SetMinThreads(Math.Max(workers, 32), io);
using var barrier = new Barrier(32);
var errors = new ConcurrentBag<string>();
await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => {
    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
    try { AccessProperties(); } catch (Exception ex) { errors.Add(ex.GetType().Name + ": " + ex.Message); }
})));
Console.WriteLine(JsonSerializer.Serialize(new { Warm = warm, ServiceWarm = serviceWarm, Concurrency = 32, Failures = errors.Count, Errors = errors.ToArray() }));
return errors.IsEmpty ? 0 : 1;
