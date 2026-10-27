using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ClanHub.IntegrationTests.Infrastructure;

/// <summary>
/// Autenticador WebAuthn em software (ES256, attestation "none") para testar o fluxo de passkeys sem navegador.
/// Gera os mesmos JSONs que `PublicKeyCredential.toJSON()` produziria.
/// </summary>
public sealed class VirtualAuthenticator
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    private uint _signCount;
    public string Origin { get; init; } = "http://localhost";

    private static string B64(byte[] b) => WebEncoders.Base64UrlEncode(b);

    // ---- CBOR mínimo ----
    private static byte[] Head(int major, long value)
    {
        var m = (byte)(major << 5);
        if (value < 24) return [(byte)(m | value)];
        if (value < 256) return [(byte)(m | 24), (byte)value];
        return [(byte)(m | 25), (byte)(value >> 8), (byte)value];
    }
    private static byte[] Int(long v) => v >= 0 ? Head(0, v) : Head(1, -1 - v);
    private static byte[] Bytes(byte[] b) => [.. Head(2, b.Length), .. b];
    private static byte[] Text(string s) { var b = Encoding.UTF8.GetBytes(s); return [.. Head(3, b.Length), .. b]; }
    private static byte[] Map(params (byte[] k, byte[] v)[] e) => [.. Head(5, e.Length), .. e.SelectMany(x => x.k.Concat(x.v))];

    private static JsonElement Options(string json)
    {
        var root = JsonDocument.Parse(json).RootElement.Clone();
        return root.TryGetProperty("publicKey", out var pk) ? pk : root;
    }

    private static string RpId(JsonElement options) =>
        options.TryGetProperty("rp", out var rp) && rp.TryGetProperty("id", out var id) ? id.GetString()! : "localhost";

    private byte[] CoseKey()
    {
        var p = _key.ExportParameters(false);
        return Map((Int(1), Int(2)), (Int(3), Int(-7)), (Int(-1), Int(1)), (Int(-2), Bytes(p.Q.X!)), (Int(-3), Bytes(p.Q.Y!)));
    }

    private static byte[] Be32(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    public (string CredentialJson, byte[] UserHandle) Create(string creationOptionsJson)
    {
        var o = Options(creationOptionsJson);
        var challenge = o.GetProperty("challenge").GetString()!;
        var userHandle = WebEncoders.Base64UrlDecode(o.GetProperty("user").GetProperty("id").GetString()!);

        var clientData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = "webauthn.create", challenge, origin = Origin, crossOrigin = false,
        }));
        var cose = CoseKey();
        byte[] authData =
        [
            .. SHA256.HashData(Encoding.UTF8.GetBytes(RpId(o))),
            0x45,                                        // UP | UV | AT
            .. Be32(0),
            .. new byte[16],                             // AAGUID
            (byte)(CredentialId.Length >> 8), (byte)CredentialId.Length, .. CredentialId,
            .. cose,
        ];
        var attestation = Map((Text("fmt"), Text("none")), (Text("attStmt"), Map()), (Text("authData"), Bytes(authData)));

        var json = JsonSerializer.Serialize(new
        {
            id = B64(CredentialId), rawId = B64(CredentialId), type = "public-key", authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new { clientDataJSON = B64(clientData), attestationObject = B64(attestation), transports = new[] { "internal" } },
        });
        return (json, userHandle);
    }

    public string Get(string requestOptionsJson, byte[] userHandle, bool tamperSignature = false)
    {
        var o = Options(requestOptionsJson);
        var challenge = o.GetProperty("challenge").GetString()!;
        var clientData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = "webauthn.get", challenge, origin = Origin, crossOrigin = false,
        }));
        _signCount++;
        byte[] authData = [.. SHA256.HashData(Encoding.UTF8.GetBytes(RpId(o))), 0x05, .. Be32(_signCount)]; // UP | UV
        var signature = _key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        if (tamperSignature) signature[^1] ^= 0xFF;

        return JsonSerializer.Serialize(new
        {
            id = B64(CredentialId), rawId = B64(CredentialId), type = "public-key", authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new { clientDataJSON = B64(clientData), authenticatorData = B64(authData), signature = B64(signature), userHandle = B64(userHandle) },
        });
    }
}