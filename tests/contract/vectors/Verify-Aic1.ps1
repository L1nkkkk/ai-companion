param([string]$VectorsPath = (Join-Path $PSScriptRoot 'audio-vectors.json'))
$ErrorActionPreference = 'Stop'
# Test-only C# codec, independent of Unity and Python. Guid.ToByteArray() is
# deliberately not used: R1 uses network-order UUID bytes, not mixed endian.
Add-Type -TypeDefinition @'
using System;

public sealed class R1VectorHeader {
    public int version, kind, channels, flags;
    public uint session_epoch, frame_seq, sample_rate, segment_index, offset_samples;
    public string stream_id, turn_id;
}
public static class R1VectorCodec {
    static void Require(bool ok) { if (!ok) throw new FormatException("Invalid AIC1 test frame"); }
    static uint U32(byte[] b, int o) {
        return (uint)b[o] | ((uint)b[o+1]<<8) | ((uint)b[o+2]<<16) | ((uint)b[o+3]<<24);
    }
    static void Put(byte[] b, int o, uint n) {
        for(int i=0;i<4;i++) b[o+i]=(byte)(n>>(8*i));
    }
    static string NetworkGuid(byte[] b, int o) {
        // Parse a hex textual UUID rather than invoking mixed-endian Guid(byte[]).
        return Guid.ParseExact(Convert.ToHexString(b, o, 16), "N").ToString();
    }
    public static R1VectorHeader Decode(byte[] b) {
        Require(b.Length>=64 && b.Length<=9664);
        Require(b[0]==65 && b[1]==73 && b[2]==67 && b[3]==49);
        var h = new R1VectorHeader {
            version=b[4],kind=b[5],channels=b[6],flags=b[7],
            session_epoch=U32(b,8),frame_seq=U32(b,12),sample_rate=U32(b,16),
            stream_id=NetworkGuid(b,24),turn_id=NetworkGuid(b,40),
            segment_index=U32(b,56),offset_samples=U32(b,60)
        };
        Require(h.version==1 && (h.kind==1 || h.kind==2) && h.channels==1 && h.flags==0);
        Require(h.session_epoch>0 && U32(b,20)==b.Length-64 && (b.Length-64)%2==0);
        Require(h.sample_rate==(h.kind==1 ? 16000u : 24000u));
        Require(h.stream_id!=Guid.Empty.ToString());
        Require((ulong)h.offset_samples+(ulong)(b.Length-64)/2<=uint.MaxValue);
        Require(h.kind==1 ? h.turn_id==Guid.Empty.ToString() && h.segment_index==0 : h.turn_id!=Guid.Empty.ToString());
        return h;
    }
    public static byte[] Encode(R1VectorHeader h, byte[] payload) {
        byte[] b = new byte[64+payload.Length];
        b[0]=65;b[1]=73;b[2]=67;b[3]=49;
        b[4]=(byte)h.version;b[5]=(byte)h.kind;b[6]=(byte)h.channels;b[7]=(byte)h.flags;
        Put(b,8,h.session_epoch);Put(b,12,h.frame_seq);Put(b,16,h.sample_rate);Put(b,20,(uint)payload.Length);
        Convert.FromHexString(Guid.Parse(h.stream_id).ToString("N")).CopyTo(b,24);
        Convert.FromHexString(Guid.Parse(h.turn_id).ToString("N")).CopyTo(b,40);
        Put(b,56,h.segment_index);Put(b,60,h.offset_samples);
        payload.CopyTo(b,64);Decode(b);return b;
    }
    public static short PcmSample(byte[] b, int index) {
        int at=64+index*2;
        return unchecked((short)(b[at] | (b[at+1]<<8)));
    }
    public static bool Rejected(byte[] b) {
        try { Decode(b);return false; } catch(FormatException) { return true; }
    }
}
'@
$document = Get-Content -LiteralPath $VectorsPath -Raw | ConvertFrom-Json
foreach ($vector in $document.vectors) {
    $bytes = [Convert]::FromHexString($vector.frame_hex)
    $actual = [R1VectorCodec]::Decode($bytes)
    $expected = [R1VectorHeader]::new()
    foreach ($property in $vector.header.PSObject.Properties) {
        if ($actual.($property.Name).ToString() -cne $property.Value.ToString()) {
            throw "C# field mismatch: $($vector.name) / $($property.Name)"
        }
        $expected.($property.Name) = $property.Value
    }
    $encoded = [R1VectorCodec]::Encode($expected, [Convert]::FromHexString($vector.payload_hex))
    if ([Convert]::ToHexString($encoded).ToLowerInvariant() -cne $vector.frame_hex) {
        throw "C# serialization mismatch: $($vector.name)"
    }
    for ($i=0; $i -lt $vector.pcm_s16.Count; $i++) {
        if ([R1VectorCodec]::PcmSample($bytes, $i) -ne $vector.pcm_s16[$i]) { throw 'PCM sign/endian mismatch' }
    }
}
$base = [Convert]::FromHexString($document.vectors[1].frame_hex)
$rejected = 0
foreach ($mutation in @(@(0,0),@(4,2),@(5,0),@(6,2),@(7,1),@(16,0),@(20,3))) {
    $bad = [byte[]]$base.Clone()
    $bad[$mutation[0]] = $mutation[1]
    if (-not [R1VectorCodec]::Rejected($bad)) { throw 'C# accepted malformed field' }
    $rejected++
}
foreach ($length in @(63, ($base.Length+2), 9665)) {
    $bad = [byte[]]::new($length)
    [Array]::Copy($base, $bad, [Math]::Min($base.Length,$length))
    if (-not [R1VectorCodec]::Rejected($bad)) { throw 'C# accepted malformed length' }
    $rejected++
}
[ordered]@{
    language='C#'; runtime=[System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    vectors=$document.vectors.Count; malformedFramesRejected=$rejected; status='passed'
} | ConvertTo-Json -Compress
