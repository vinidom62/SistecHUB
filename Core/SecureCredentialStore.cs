using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SistecHub.Core;

/// <summary>Guarda tokens sensíveis com DPAPI (âmbito da máquina, partilhado entre utilizadores).</summary>
internal static class SecureCredentialStore
{
    static readonly byte[] Entropy = "SistecHub.Credentials.v1"u8.ToArray();

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    sealed class CredentialPayload
    {
        public string GlpiUserToken { get; set; } = "";
        public string GroqApiKey { get; set; } = "";
    }

    public static (string GlpiUserToken, string GroqApiKey) Load(string settingsDirectory)
    {
        var path = GetFilePath(settingsDirectory);
        if (!File.Exists(path))
            return ("", "");

        if (TryUnprotectFile(path, DataProtectionScope.LocalMachine, out var machinePayload))
            return (machinePayload.GlpiUserToken, machinePayload.GroqApiKey);

        if (TryUnprotectFile(path, DataProtectionScope.CurrentUser, out var userPayload))
            return (userPayload.GlpiUserToken, userPayload.GroqApiKey);

        return ("", "");
    }

    public static void Save(string settingsDirectory, string glpiUserToken, string groqApiKey)
    {
        Directory.CreateDirectory(settingsDirectory);

        var payload = new CredentialPayload
        {
            GlpiUserToken = glpiUserToken.Trim(),
            GroqApiKey = groqApiKey.Trim(),
        };
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        var protectedBytes = ProtectedData.Protect(jsonBytes, Entropy, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(GetFilePath(settingsDirectory), protectedBytes);
    }

    static bool TryUnprotectFile(string path, DataProtectionScope scope, out CredentialPayload payload)
    {
        payload = new CredentialPayload();
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, Entropy, scope);
            var json = Encoding.UTF8.GetString(jsonBytes);
            var parsed = JsonSerializer.Deserialize<CredentialPayload>(json, JsonOptions);
            if (parsed is null)
                return false;

            payload = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string GetFilePath(string settingsDirectory) =>
        Path.Combine(settingsDirectory, "credentials.dat");
}
