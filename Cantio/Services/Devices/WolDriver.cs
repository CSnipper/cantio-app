using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Cantio.Models;

namespace Cantio.Services.Devices;

/// <summary>
/// Uniwersalny sterownik Wake-on-LAN. Umie tylko włączać (magic packet).
/// </summary>
public sealed class WolDriver : IDisplayDeviceDriver
{
    public async Task<bool> PowerOnAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(device.Mac)) return false;
        try
        {
            // Seria 5 pakietów co 2 s (~10 s), respektuj anulowanie.
            for (int i = 0; i < 5; i++)
            {
                await SendMagicPacketAsync(device.Mac, ct);
                if (i < 4) await Task.Delay(2000, ct);
            }
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WolDriver] PowerOn failed: {ex.Message}");
            return false;
        }
    }

    public Task<bool> PowerOffAsync(ProjectionDevice device, CancellationToken ct = default)
        => Task.FromResult(false); // WoL nie umie wyłączać

    public Task<DevicePowerState> GetStateAsync(ProjectionDevice device, CancellationToken ct = default)
        => Task.FromResult(DevicePowerState.Unknown); // bez IP nie sprawdzimy

    /// <summary>
    /// Wysyła pojedynczy magic packet (6×0xFF + 16×MAC) broadcastem na UDP:9.
    /// Akceptuje MAC z separatorami ':' / '-' lub bez.
    /// </summary>
    public static async Task SendMagicPacketAsync(string mac, CancellationToken ct = default)
    {
        var bytes = ParseMac(mac);
        if (bytes is null) throw new FormatException($"Nieprawidłowy MAC: {mac}");

        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) Array.Copy(bytes, 0, packet, 6 + i * 6, 6);

        using var udp = new UdpClient { EnableBroadcast = true };
        await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
    }

    private static byte[]? ParseMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var hex = mac.Replace(":", "").Replace("-", "").Replace(" ", "").Trim();
        if (hex.Length != 12) return null;
        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2),
                    System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return null;
        }
        return bytes;
    }
}
