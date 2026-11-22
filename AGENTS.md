# Project instructions

## Language and style

- Use Chinese for chat, design discussions, and review handoffs.
- Use English for source code identifiers and comments.
- Follow `.editorconfig` for C# naming and formatting. The conventions come from `C:\dev\mod\inscryption\!!!3D_RENDERER\AGENTS.md` and its `.editorconfig`.

## Project scope

- Target Windows x64 for the porting tool and generated desktop projects.
- Use C# and .NET 10 for the tool. Keep porting logic in a shared core for the CLI and the WPF desktop tool.
- Release the CLI first in `0.1.0`, with support for Android libGDX source projects and editable desktop project output. Schedule the WPF tool for the next phase and APK analysis for later.
- Work in an output copy of the input project. Generate a Gradle desktop target with LWJGL3, retain the shared Java or Kotlin source, and report compatibility issues. Provide build and packaging commands separately.
- For `0.0.1`, validate a manual Windows port using a publicly licensed Android-only libGDX game.

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
