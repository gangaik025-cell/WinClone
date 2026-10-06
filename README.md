# WinClone

**Experimental Windows system cloning application built with C# and WPF.**

## Overview

WinClone is an experimental Windows desktop project exploring a guided system-cloning workflow. Its current UI can discover physical disks, identify the disk containing the current Windows installation, select source and destination disks, analyze a proposed clone, create and verify a Volume Shadow Copy Service (VSS) snapshot, prepare a clone layout, lock and validate a plan, simulate the workflow, and exercise a clone backend against a temporary test image.

The source also contains a prototype physical-clone implementation, including partition, Windows, boot, and recovery steps. That method is not connected to the normal UI workflow and has not been validated on a real disk.

## Current Status

WinClone is currently an **experimental/prototype project**.

The normal UI workflow is read-only with respect to the selected physical clone destination and is focused on analysis, validation, simulation, and test-image work. Preparing a VSS snapshot does create a Windows shadow copy, and the test backend writes a temporary image file. The physical disk-write clone path is **not enabled through the normal UI**.

Real physical cloning has not been demonstrated or successfully tested. Do not interpret analysis, simulations, self-tests, or test-image results as evidence that physical cloning works.

## Current Features

The current source includes:

- Disk and partition discovery through PowerShell Storage cmdlets, including identification of the current Windows disk.
- Source and destination selection, disk analysis, capacity checks, and a proposed clone-layout preview.
- A review and typed-confirmation step, plan locking, disk rechecks, and write-guard validation.
- VSS snapshot creation and verification for the Windows source volume.
- A no-write clone simulation and engine self-test paths.
- A test clone target that writes and reads a temporary image under the system temporary directory (`WinClone-Test\clone-test.img`), rather than opening a physical-disk write handle.
- A prototype physical-clone method in the code-behind. It contains DiskPart, Robocopy, BCDBoot, Windows recovery, and cleanup steps, but is not wired to a normal UI action and has not been verified on real hardware.

These features describe code paths present in the project; they are not a claim of production readiness or successful physical cloning.

## Technology

Technologies and Windows components referenced by the current project include:

- C# and .NET 10 for Windows
- WPF
- PowerShell, including Windows Storage cmdlets and CIM queries for disk and VSS information
- Windows Volume Shadow Copy Service (VSS)
- DiskPart, Robocopy, and BCDBoot in the dormant physical-clone prototype
- Windows Recovery Environment (WinRE) handling in the dormant prototype

DiskPart, Robocopy, BCDBoot, and WinRE operations are part of the prototype implementation, not the normal UI clone workflow.

## Architecture

The project is primarily implemented in the WPF window code-behind. The existing application code contains the UI workflow, disk discovery, VSS handling, clone analysis and planning, write-guard checks, simulation, test-image backend, clone-engine prototype, and boot/recovery logic. `Models/DiskInfo.cs` defines disk and partition data models.

## Workflow

The current UI workflow is:

```text
Detect Drives
→ Select Source/Destination
→ Analyze Clone
→ Review/Confirm
→ Prepare Windows Snapshot
→ Prepare Clone Layout
→ Lock Clone Plan
→ Validate Write Guard
→ Safe Simulation/Test
```

This is an analysis, validation, and test workflow. **The current UI does not perform a real physical clone.** VSS preparation creates a Windows shadow copy; the test backend writes a temporary test image. Neither step writes a clone to the selected physical destination.

## Safety Warning

> **THIS PROJECT IS EXPERIMENTAL.**
>
> Do not use the physical cloning implementation on important disks. The physical clone implementation is not production-ready. The destination disk may be destroyed if destructive functionality is enabled.

No safety checks in this prototype guarantee against data loss, and no result guarantees that a cloned system will boot or recover.

## Limitations

- This is a prototype, not a production cloning utility.
- The physical clone path is not enabled through the normal UI.
- Physical cloning has not been validated on a real disk.
- Disk identity and destination targeting are not yet proven robust enough for production use.
- Boot configuration and WinRE code are prototype logic; successful boot or recovery is not guaranteed.
- Snapshot lifecycle, transaction handling, failure recovery, and cleanup need further validation.

## Future Roadmap

The following items are **FUTURE WORK**, not completed features:

- Stronger stable disk identity and safer destination targeting
- Improved transaction and safety handling
- Centralized workflow state
- Improved VSS lifecycle management
- Safer partition targeting
- Better cleanup and recovery after failures
- Stronger boot and WinRE verification
- Validation using disposable VHD/VHDX images
- Any future physical clone support, after extensive testing and safety review

## Build Instructions

Build on Windows with a compatible .NET SDK that supports the project's `net10.0-windows` target and WPF:

```powershell
dotnet build .\WinClone.slnx
```

You can also build the project directly:

```powershell
dotnet build .\WinClone\WinClone.csproj
```

A compatible Windows/.NET SDK environment is required. These commands compile the project; they do not validate physical cloning.

## Project Structure

```text
WinClone/
├── WinClone.slnx
└── WinClone/
    ├── App.xaml
    ├── App.xaml.cs
    ├── AssemblyInfo.cs
    ├── MainWindow.xaml
    ├── MainWindow.xaml.cs
    ├── Models/
    │   └── DiskInfo.cs
    └── WinClone.csproj
```

## GitHub Repository Description

Experimental Windows system cloning application built with C# and WPF.

## License

License not yet specified.
