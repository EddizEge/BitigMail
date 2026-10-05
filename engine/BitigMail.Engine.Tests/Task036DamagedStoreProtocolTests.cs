using System.Text.Json;
using BitigMail.Engine.Recovery;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class Task036DamagedStoreProtocolTests
{
    [Fact] public void AcceptsOnlyCurrentInvocationAndVerifiedMimeFiles()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Output, "one.eml"), "Subject: one\r\n\r\nbody");
        fixture.WriteResult("partial_recovered", [fixture.Message("one.eml")], [new("folder", "AA", "enumeration_failed")], false);
        var validated = new DamagedStoreResultValidator().Validate(fixture.Request, fixture.Result, fixture.Source);
        Assert.True(validated.Partial); Assert.Equal(1, validated.RecoveredCount); Assert.Null(validated.OriginalTotal);
    }

    [Fact] public void RejectsStaleInvocationEvenWhenEarlierResultWasSuccessful()
    {
        using var fixture = new Fixture(); fixture.WriteResult("healthy_extraction", [], [], true, invocationId:"old");
        Assert.Throws<InvalidDataException>(()=>new DamagedStoreResultValidator().Validate(fixture.Request,fixture.Result,fixture.Source));
    }

    [Fact] public void RejectsOrphanAndTraversalOutputs()
    {
        using var fixture = new Fixture(); File.WriteAllText(Path.Combine(fixture.Output,"orphan.eml"),"x"); fixture.WriteResult("failed",[],[new("root",null,"unreadable")],false);
        Assert.Throws<InvalidDataException>(()=>new DamagedStoreResultValidator().Validate(fixture.Request,fixture.Result,fixture.Source));
        File.Delete(Path.Combine(fixture.Output,"orphan.eml")); fixture.WriteResult("partial_recovered",[new("../escape.eml","00",0)],[],false);
        Assert.Throws<InvalidDataException>(()=>new DamagedStoreResultValidator().Validate(fixture.Request,fixture.Result,fixture.Source));
    }

    [Fact] public void RejectsChangedSourceAndFabricatedHealthyOutcome()
    {
        using var fixture = new Fixture(); fixture.WriteResult("healthy_extraction",[],[new("folder",null,"failed")],false);
        Assert.Throws<InvalidDataException>(()=>new DamagedStoreResultValidator().Validate(fixture.Request,fixture.Result,fixture.Source));
        fixture.WriteResult("failed",[],[],false); File.AppendAllText(fixture.Source,"changed");
        Assert.Throws<InvalidDataException>(()=>new DamagedStoreResultValidator().Validate(fixture.Request,fixture.Result,fixture.Source));
    }

    private sealed class Fixture:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"bitigmail-036-"+Guid.NewGuid().ToString("N")); public string Source{get;} public string Output{get;} public string Result{get;} public DamagedStoreWorkerRequest Request{get;}
        public Fixture(){Directory.CreateDirectory(Root);Source=Path.Combine(Root,"source.pst");File.WriteAllText(Source,"known-original");Output=Path.Combine(Root,"staging");Directory.CreateDirectory(Output);Result=Path.Combine(Output,"result.json");Request=new(1,Guid.NewGuid().ToString("N"),"job-test",Source,DamagedStoreResultValidator.HashFile(Source),Output,100,100,16,DateTime.UtcNow.AddMinutes(5).Ticks);}
        public RecoveredMimeFile Message(string relative){string full=Path.Combine(Output,relative);return new(relative,DamagedStoreResultValidator.HashFile(full),new FileInfo(full).Length);}
        public void WriteResult(string outcome,IReadOnlyList<RecoveredMimeFile> messages,IReadOnlyList<RecoveryFailure> failures,bool complete,string? invocationId=null){var value=new DamagedStoreWorkerResult(1,invocationId??Request.InvocationId,Request.JobId,Request.SourceSha256,outcome,null,messages,failures,complete);File.WriteAllText(Result,JsonSerializer.Serialize(value));}
        public void Dispose(){try{Directory.Delete(Root,true);}catch{}}
    }
}
