using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using GameHub.Core.Data;

namespace GameHub.Core.Security;

/// <summary>Criptografa segredos (chaves de API, tokens) antes de gravar no banco.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

/// <summary>
/// Usa o DPAPI do Windows (escopo do usuário atual): só a mesma conta do Windows, na mesma máquina,
/// consegue decifrar. Uma cópia do banco levada para outro PC não expõe as chaves.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GameHub.v1.secrets");

    public string Protect(string plaintext) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedText) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser));
}

/// <summary>Leitura e gravação de segredos nas configurações, sempre cifrados.</summary>
public sealed class SecretStore
{
    private readonly SettingsRepository _settings;
    private readonly ISecretProtector _protector;

    public SecretStore(SettingsRepository settings, ISecretProtector protector)
    {
        _settings = settings;
        _protector = protector;
    }

    public string? Get(string key)
    {
        var stored = _settings.Get(key);
        if (string.IsNullOrEmpty(stored)) return null;
        try { return _protector.Unprotect(stored); }
        catch (CryptographicException) { return null; } // ex.: backup restaurado em outra conta do Windows
        catch (FormatException) { return null; }
    }

    public void Set(string key, string? value) =>
        _settings.Set(key, string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value.Trim()));

    public bool Has(string key) => !string.IsNullOrEmpty(Get(key));
}
