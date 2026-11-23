# Project instructions

## Language and style

- Use Chinese for chat, design discussions, and review handoffs.
- Use English for source code identifiers and comments.
- Add brief XML documentation to public C# types and members and to internal contracts that need explanation. Use `<summary>` for the purpose and add `<param>`, `<returns>`, or `<exception>` when they clarify caller behavior. Use `<inheritdoc />` for implementations that follow a documented contract.
- Follow `.editorconfig` for C# naming and formatting. The conventions come from `C:\dev\mod\inscryption\!!!3D_RENDERER\AGENTS.md` and its `.editorconfig`.

## Project scope

- Target Windows x64 for the porting tool and generated desktop projects.
- Use C# and .NET 10 for the tool. Keep porting logic in a shared core for the CLI and the WPF desktop tool.
- Use an authorized APK and any supplied companion files or directories as the primary input for end-to-end porting. Produce an editable project with application source, resources, and build files that can build and run a Windows x64 executable.
- Treat the `0.1.0` Gradle libGDX source-project CLI as the baseline for desktop project generation, build, and packaging.
- Generate desktop projects in a separate output directory. For Gradle source inputs, retain the shared Java or Kotlin source. Use LWJGL3 for libGDX desktop targets and report compatibility issues. Provide build and packaging commands separately.
- For `0.0.1`, validate a manual Windows port using a publicly licensed Android-only libGDX game.

## Companion files

- Accept companion files regardless of their extension, including extensionless files, unknown formats, and directory trees. Record original paths, source containers, and content hashes. Preserve the original bytes for formats awaiting an adapter and report missing files or conflicting entries.
- Identify formats using their contents, package metadata, and explicit user configuration. Use format adapters to unpack, normalize, or map files into the desktop project. Allow application-specific adapters and user-supplied readable exports for proprietary formats.
- Track intake, decoding, and verified runtime use separately. A required companion file counts as supported for end-to-end porting when a Windows run exercises the content or behavior it provides. Report any required file or format that still blocks a complete port.

## Release roadmap

Use the following milestone order and acceptance criteria. Review changes to the roadmap with the maintainer.

| Version | Milestone and acceptance criteria |
| --- | --- |
| `0.0.1` | Record the Android baseline and complete a manual Windows x64 port of Tilt Maze. Verify keyboard controls and one complete playthrough. Record the changes needed for inspection and generation. |
| `0.1.0-alpha.1` | Deliver `inspect` and `plan` for Gradle libGDX source projects. Verify module and asset detection, explicit overrides, and actionable diagnostics on Java and Kotlin inputs. |
| `0.1.0-alpha.2` | Deliver `generate` and `build`. Generate an editable desktop project in a separate directory, build and launch it, and verify clean retries and conflict reporting. |
| `0.1.0-beta.1` | Build and play Java and Kotlin fixtures with different project layouts. Verify Android regression builds and diagnostic behavior for unsupported inputs. |
| `0.1.0` | Release the Windows x64 CLI. Verify the quickstart, an editable project export, and a `package` ZIP with a bundled Java runtime on clean Windows. |
| `0.2.0-alpha.1` | Manually recover a playable Windows project from the Tilt Maze APK. Use the original source for comparison during verification. Record the steps for recovering the entry point, source, dependencies, and keyboard controls. |
| `0.2.0-alpha.2` | Automate the libGDX APK route. Inventory all supplied companion files, support ZIP-based OBB, and preserve bytes for unknown formats. Generate editable source, resources, and Gradle files with diagnostics tied to the affected input. |
| `0.2.0-beta.1` | Verify two independent APK fixtures, including a game that consumes OBB content during play. Check missing required files, conflicting entries, and preservation of unknown and extensionless attachments. |
| `0.2.0` | Release the APK-to-project CLI for the validated libGDX inputs. Start from APK and companion files, edit recovered application logic or resources, rebuild a Windows x64 executable, and verify the changed behavior on clean Windows. |
| `0.3.0` | Release the WPF tool with APK and companion file intake, diagnostics, generation, and packaging through the shared core. Verify equivalent CLI and desktop results on the same inputs. |
| `0.4.0` | Formalize the attachment adapter contract. Expand coverage to split APKs, APK sets, supplied asset packs, and loose files or directories. Validate optional import of matching R8 mapping files for obfuscated inputs. |
| `0.5.0` | Pilot an APK-based route for a maintainer-approved engine or application stack beyond libGDX. Validate its application-specific formats and a representative Windows workflow. |
| `1.0.0` | Stabilize the Windows x64 support matrix and the CLI, configuration, diagnostics, and attachment adapter contracts. Verify recovery, editability, building, and runtime use of required attachments for each supported route, including the documented workflow on at least two external projects. |

### Proposed `0.5.0` work for PopCap RSB v4

The released `0.4.0` attachment contract is the baseline for this work. The
local international PvZ2 sample has an RSB v4 header and 588 in-bounds resource
groups with `pgsr` v4 headers. Its Android runtime is an ARMv7 native library.

1. **Report the attachment format independently.** Use a shared probe to
   identify RSB from the file contents and report the version and group count
   in CLI and WPF inspection. Run this probe before the engine compatibility
   check. Verify ordinary `.obb` names, renamed and extensionless inputs, and
   malformed headers. Use package naming to associate expansions with the APK
   and keep explicit ZIP selection compatible with `--obb` and **Add ZIP OBB**.
   Verify that both clients select the correct adapter for each format.
2. **Read the validated RSB v4 layout.** Implement a read-only adapter using
   `IAttachmentAdapter` and the shared resource writer. Check the group and
   entry spans, compression variants, output lengths, and resource paths.
   Preserve the original bundle and record each emitted resource's source group,
   original path, output path, and SHA-256. Diagnose unsupported layouts or
   required resource formats at the affected input. Verify the layout on the
   supplied sample locally and use authored fixtures for repeatable tests.
3. **Verify resource use on Windows.** With an authored fixture, map resources
   from an RSB and from a matching readable export supplied as an asset pack
   or loose files. Tie required resources and ZIP expansion checks to the
   corresponding resource loaders. Check missing resources, conflicting paths,
   truncated or invalid data, and cancellation. Compare the CLI and WPF
   resource manifests. Edit a mapped resource, rebuild the Windows x64
   executable, and verify the changed behavior with mouse clicks on clean
   Windows. Keep identification, decoding, and runtime validation as separate
   results.
4. **Qualify the native engine pilot.** Select the engine with the maintainer
   and confirm usable engine and application source, dependencies, and an
   Android reference run. Produce an editable Windows x64 build and adapt its
   resource loader, platform services, and input. Provide build and packaging
   commands for the native target and verify required attachment use during
   play. Track the PvZ2 sample's native engine as a separate blocker until a
   compatible Windows runtime is available. Record RSB adapter results
   independently of that engine's status.

## Interface review

- For a new or changed class interface, propose the affected member signatures, including relevant internal signatures, before writing their implementations. Put the signature changes in the working tree when the interface can be reviewed there.
- Discuss disagreements and materially different designs with the maintainer. Get confirmation on the signatures before implementing the affected members. Continue independent work while review is pending.
- Follow the approved contracts during implementation. Bring any required signature change back to the maintainer for review.

## Source review markers

- Enclose each contiguous addition or replacement with `/// REVIEW BEGIN - <reason in English>` and `/// REVIEW END`. Match the surrounding indentation. One pair may cover a new file. Reuse an existing pair when revising code inside it.
- Keep each contiguous removed block as `//` comments between `/// REVIEW DELETED BEGIN - <reason in English>` and `/// REVIEW DELETED END`. Preserve the original indentation within the comments. Put the block at the original location or immediately before its replacement. Explain the removal and give its location in the Chinese handoff.
- Keep markers and commented deletions during review. Preserve the maintainer's edits and comments, including comments without a prefix.
- During a review, read every changed source file and check new or unresolved human comments, including inside deleted blocks. Address them at the relevant location and summarize their disposition in Chinese.
- Append replies after the latest complete human comment block. Start them with `/// CODEX RESPONSE - ` and use `///` on continuation lines at the same indentation. Use `///` for other uppercase review prefixes. Keep discussions in chronological order and append corrections as new replies.

## Commits and tags

- Create commits or tags only when the maintainer explicitly requests them. Leave review markup in the working tree by default.
- `// GIT COMMIT - <subject>` and `// GIT TAG - <tag>` are local metadata. Fill the commit marker after confirming its scope and the tag marker after confirming its name. Keep marker edits unstaged.
- Before committing, read any Git markers and remove review markers, commented deletions, review discussion, and Git metadata markers from the source being committed. Keep normal documentation, implementation notes, and actionable TODOs.
- After a successful commit, restore empty Git marker placeholders in their agreed location as unstaged changes if they were used for that commit.
