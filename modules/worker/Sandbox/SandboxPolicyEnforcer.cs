using System.Net;

namespace MuniClaw.Worker.Sandbox;

public static class SandboxPolicyEnforcer
{
    private static readonly HashSet<string> DeniedSockets = new(StringComparer.OrdinalIgnoreCase)
    {
        "/var/run/docker.sock",
        "/run/docker.sock",
        "/var/run/dockershim.sock",
        "/run/containerd/containerd.sock",
        "/var/run/containerd/containerd.sock"
    };

    private static readonly HashSet<string> DeniedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "169.254.169.254",
        "fd00:ec2::254",
        "metadata.google.internal",
        "instance-data"
    };

    public static void ValidatePathAccess(string sandboxRootDirectory, string requestedPath)
    {
        var fullRequestedPath = Path.GetFullPath(requestedPath);
        var fullSandboxRoot = Path.GetFullPath(sandboxRootDirectory);

        // Explicitly check for host Docker socket access
        if (DeniedSockets.Any(socket => fullRequestedPath.Equals(socket, StringComparison.OrdinalIgnoreCase) ||
                                       fullRequestedPath.EndsWith("docker.sock", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException($"Access to host Docker socket '{requestedPath}' is strictly denied by platform policy.");
        }

        // Enforce sandbox root boundary
        if (!fullRequestedPath.StartsWith(fullSandboxRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Access to path '{requestedPath}' is denied: outside task sandbox boundary '{sandboxRootDirectory}'.");
        }
    }

    public static void ValidateNetworkTarget(string hostOrIp)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
        {
            return;
        }

        var trimmed = hostOrIp.Trim();

        // Check explicit denied hostnames or metadata IPs
        if (DeniedHostnames.Contains(trimmed))
        {
            throw new UnauthorizedAccessException($"Network access to cloud metadata service '{trimmed}' is strictly denied.");
        }

        // Check if IP is in the 169.254.0.0/16 link-local range
        if (IPAddress.TryParse(trimmed, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && bytes[0] == 169 && bytes[1] == 254)
            {
                throw new UnauthorizedAccessException($"Network access to link-local metadata address '{trimmed}' is strictly denied.");
            }
        }
    }

    public static IDictionary<string, string> SanitizeEnvironment(string homeDirectory, string tmpDirectory)
    {
        var env = new Dictionary<string, string>();

        // Copy safe environment variables
        foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
        {
            var key = de.Key.ToString();
            var val = de.Value?.ToString() ?? string.Empty;

            if (key == null) continue;

            // Strip sensitive cloud credentials, host Docker, or control plane keys
            if (key.StartsWith("AWS_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("AZURE_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("GOOGLE_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("DOCKER_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("MUNICLAW_CONTROL_PLANE", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("OPENCODE_SERVER_PASSWORD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            env[key] = val;
        }

        // Set unprivileged isolated user directories
        env["HOME"] = homeDirectory;
        env["USER"] = "municlaw-task";
        env["TMPDIR"] = tmpDirectory;
        env["TEMP"] = tmpDirectory;
        env["TMP"] = tmpDirectory;

        return env;
    }
}
