# Development Status

WinClone is an experimental prototype. This file records the state visible in the current source; it does not certify that disk cloning or recovery works.

## What Currently Exists

- A WPF interface for detecting disks, selecting source and destination disks, and reviewing clone analysis.
- PowerShell-based disk and partition discovery, with a check for the disk containing the current Windows installation.
- A VSS snapshot creation and verification path for the Windows volume.
- Clone layout preview, plan locking, disk rechecks, and write-guard validation.
- A no-write simulation and an engine self-test path.
- A test target backend that reads and writes a temporary test image rather than opening a physical disk for writing.

These are implemented code paths; their presence does not establish production reliability.

## Experimental or Not Validated

- The physical clone method exists in the code-behind but is not connected to the normal UI workflow.
- Real physical cloning has not been validated on a real disk.
- Disk identity, destination targeting, VSS lifecycle, transaction handling, and failure cleanup need more work before any production use.
- Boot and Windows Recovery Environment steps exist in the dormant physical-clone prototype, but successful boot and recovery are not established.

## Future Development

All items below are future work:

- Stronger stable disk identity and safer partition targeting
- Centralized workflow state and improved transaction/safety handling
- Better VSS lifecycle management and failure cleanup/recovery
- Stronger boot and WinRE verification
- Disposable VHD/VHDX validation
- Consider physical clone support only after extensive validation
