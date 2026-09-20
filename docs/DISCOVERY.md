# LAN discovery — Milestone B

Select an active IPv4 interface, review the Start/End range and Scan. Cancel stops scheduling and retains displayed results. Select an unmanaged row and Add to My Devices to open a prefilled editor.

## Evidence and limits
- Online means an ICMP reply during this scan or the selected local interface.
- Discovered means neighbor-cache/ARP evidence without verified availability. Ping replies are not required.
- Observed time is when this scan gathered evidence, not when a cached neighbor last answered.
- MAC/IP/hostname matches mark a row managed. An IP match can be a DHCP collision; review the existing device rather than automatically overwriting it.
- WoL defaults Disabled. Discovery cannot determine wake support.
- Vendor lookup has an optional offline provider boundary, but no OUI database or online lookup is included.
- VLAN boundaries, Wi-Fi isolation, firewalls, proxy ARP and sleeping devices affect discovery. Missing results do not prove absence.
- This is not continuous monitoring. Managed live status and wake transport are Milestone C.

## Implementation
Core validates decimal IPv4 addresses, contiguous masks and ranges including /31 and /32. Scans are capped at 4096 addresses on the selected interface's subnet. For larger subnets, the initial range contains only the local address; expand it deliberately. Settings controls worker concurrency (default 32, max 64) and ping/DNS timeout (default 750 ms).

WindowsLanProbe enumerates active non-loopback IPv4 interfaces, skipping IPv6-only or disappearing adapters. It revalidates interface identity/address/mask before scanning. GetIpNetTable is sufficient for IPv4 cache evidence and avoids the larger IPv6/native-union binding in GetIpNetTable2. Native offsets and strides use Marshal; invalid/multicast/zero MAC entries are excluded. Cache type is never proof of reachability.

Parallel.ForEachAsync bounds workers over a lazy address range. A process-wide eight-slot gate bounds SendARP calls, which cannot be cancelled natively. Cancelling releases the UI await, but each native call retains its slot until it returns. Rapid cancel/restart does not accumulate unbounded calls. ARP timeout is Windows-managed; configured ping/DNS timeouts are not total per-address deadlines. ICMP follows Windows routing; ARP explicitly selects its source. Overlapping subnets on multiple adapters can therefore have ambiguous ICMP evidence.

Evidence streams before DNS completes; cache merges before and after active probing. DNS failures never discard hosts. Cache read failures produce a visible diagnostic while other probes continue. No administrator elevation or external arp/ping processes are requested.

## Validation
Normal unit/UI tests use fake network probes and never scan the LAN. They cover subnet/broadcast calculations, ranges, non-ping hosts, cache evidence, adapter changes, concurrency, cancellation, prefilled add, managed marking and shutdown.

An explicit optional live smoke enumerates Windows adapters/cache and probes at most one already-cached on-subnet neighbor:

```powershell
dotnet run --project tools/RemoteManager.SmokeTests -c Release -- --network-smoke
```

Sources: [GetIpNetTable](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getipnettable), [MIB_IPNETROW](https://learn.microsoft.com/en-us/windows/win32/api/ipmib/ns-ipmib-mib_ipnetrow_lh), [SendARP](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-sendarp).
