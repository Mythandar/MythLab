# Wake-on-LAN and status

1. Edit a device and enter the wired adapter's MAC address, hostname/IP, and enable Wake-on-LAN.
2. Choose Automatic for the interface, or select an active adapter if multiple networks overlap. Blank broadcast uses that adapter's real subnet broadcast. A configured broadcast must be local, or 255.255.255.255.
3. Choose ICMP status, or TCP with a port for a service expected to be running. A blocked ping or stopped service can appear Offline even when the computer is powered on.
4. Save. Wake and Test Wake appear on the card. Test Wake checks current availability, sends packets if needed and watches the configured status check. Hover the detail text for the full diagnostic.
5. Cancel wake stops pending packets and the wait; already-sent packets cannot be recalled.

Enable wake in the target BIOS/UEFI and NIC settings as appropriate for that hardware. Keep its network adapter powered and connected. Discovery cannot determine support. An already-online device sends no packets and cannot newly verify wake support. Verified records an observed Offline → wake → Online transition; it does not prove the packet caused it.

Defaults: UDP 9; three packets per burst, 250 ms between packets; one retry burst after 1000 ms. Edit changes these per device. Settings controls the status interval (30 s), shared probe timeout (750 ms), wake wait (120 s after sending), and wake polling (3 s). Wake & Connect and connection-specific readiness arrive in Milestone F.

If wake times out, check the target MAC, selected interface and broadcast, BIOS/NIC wake options, power state, VLAN isolation and chosen availability check. No matching/ambiguous interface errors require editing the configuration. Changed DHCP addresses can require updating the saved IP; status resolves a configured hostname each check. Remote wake relays and arbitrary remote-subnet broadcasts are not implemented.

Structured logs in Data/logs include packet destination, selected interface, status transitions and failure categories. Normal operations need no administrator elevation. Tests use fake wake transport and never send real magic packets; validate waking a known supported computer manually.
