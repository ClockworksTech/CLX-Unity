using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Clockworks.CLX
{
    /// <summary>Finds the local IPv4 addresses and where to broadcast on each.</summary>
    static class CLXNetwork
    {
        internal readonly struct LocalAddress
        {
            public readonly IPAddress Address;

            /// <summary>The subnet's directed broadcast, or null if there isn't a usable one.</summary>
            public readonly IPAddress Broadcast;

            public LocalAddress(IPAddress address, IPAddress broadcast)
            {
                Address = address;
                Broadcast = broadcast;
            }
        }

        /// <summary>
        /// Every IPv4 address of an interface that is up. Empty on platforms that
        /// won't list their interfaces (some Android versions refuse), and the
        /// caller then falls back to the system's default.
        /// </summary>
        internal static List<LocalAddress> GetLocalAddresses()
        {
            var result = new List<LocalAddress>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up
                        && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        result.Add(new LocalAddress(info.Address, DirectedBroadcast(info)));
                    }
                }
            }
            catch (Exception)
            {
                // Listing interfaces is a convenience, and every caller copes without it.
            }
            return result;
        }

        static IPAddress DirectedBroadcast(UnicastIPAddressInformation info)
        {
            try
            {
                byte[] address = info.Address.GetAddressBytes();
                // Loopback has no broadcast. Link-local has no routable directed
                // one: sending to 169.254.255.255 fails with EADDRNOTAVAIL, and
                // the limited broadcast reaches those networks anyway.
                if (address[0] == 127 || (address[0] == 169 && address[1] == 254)) return null;

                IPAddress mask = info.IPv4Mask;
                if (mask == null) return null;
                byte[] maskBytes = mask.GetAddressBytes();
                for (int i = 0; i < 4; i++) address[i] |= (byte)~maskBytes[i];
                return new IPAddress(address);
            }
            catch (Exception)
            {
                return null; // IPv4Mask isn't implemented everywhere.
            }
        }
    }
}
