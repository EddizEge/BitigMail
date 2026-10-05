namespace BitigMail.LocalHost.Security;
public sealed class SetupProofGate : IDisposable
{
 private readonly object _gate=new();private FirstRunSetupProof? _proof;
 public bool Available { get { lock(_gate)return _proof is not null; } }
 internal void InstallFromNativeChannel(ReadOnlySpan<byte> proof,TimeProvider? clock=null){lock(_gate){_proof?.Dispose();_proof=new FirstRunSetupProof(proof,clock);}}
 public bool TryConsume(string? candidate){lock(_gate)return _proof?.TryConsume(candidate)==true;}
 public void Dispose(){lock(_gate){_proof?.Dispose();_proof=null;}}
}
