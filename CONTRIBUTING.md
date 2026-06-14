# Contributing to CTBL++

CTBL++ welcomes contributors of all kinds — developers, vibe coders, and people with ideas. Feature requests and feedback are just as useful as code.

---

## Source Access Model

CTBL++ is MIT-licensed, but full source access is subject to a **7-day delay**. This exists because CTBL++ is a tamper-resistance tool — if every bypass vector is immediately public, the tool can't do its job.

The repository, architecture, APIs, and non-sensitive code are freely available. Certain enforcement internals (watchdog mechanics, critical-process logic, vault sealing) are distributed as encrypted archives. This is a waiting period, not a vetting process.

### Requesting full source access

1. **Open an issue** using the **Source Access Request** template.
2. In the template, specify your **preferred delivery method** (email, phone, Discord, etc.).
3. A bot will confirm your request and start a **7-day timer**.
4. After 7 days, a reviewer is notified that your request is ready. They will deliver the archive password through the channel you specified.

The password is never posted on GitHub. It is delivered privately by a human reviewer through your chosen channel. If the reviewer needs to verify anything about your request, they'll reach out before delivering.

---

## Development Setup

**Requirements:** Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js](https://nodejs.org/), Cold Turkey Blocker (paid, default install path), and an Administrator terminal.

```bash
git clone https://github.com/Detractless/CtblPlusPlus.git
cd CtblPlusPlus
ctbl.bat          # [1] Build all projects, then [2] Launch Installer
.\Deploy.ps1      # Build & deploy the patched Cold Turkey UI
```

See the [README](README.md) for full build details and architecture overview.

---

## Before You Start

- **Bug fixes and security hardening** are always top priority.
- **New features:** open an issue or discussion first so we can align on scope before you write code.
- Look for [`good first issue`](https://github.com/Detractless/CtblPlusPlus/labels/good%20first%20issue) if you're new to the project.

---

## Security

CTBL++ is a tamper-resistance tool — security is the product, not a side concern.

- **Don't introduce bypass vectors.** If a change makes enforcement easier to circumvent, it won't be merged.
- **Think adversarially.** The user is also the person trying to get past it.
- **Don't log secrets.** HMAC keys, vault contents, and tamper-detection details stay out of logs and error messages.
- **Test service recovery.** If you touch the Engine or watchdogs, verify that killing the process triggers a restart.

Changes touching enforcement-sensitive areas (watchdog logic, DPAPI vault, HMAC signing, account demotion, IFEO, `icacls` hardening) will receive extra scrutiny. Note it in your PR description.

**Bypass reports:** If you find a way to circumvent enforcement, report it as an issue. If publishing it would undermine other users, note that and we'll coordinate a fix before details go public.

---

## Pull Requests

- **One logical change per PR.** Don't mix a bug fix with a refactor.
- **Build clean** (`ctbl.bat` → [1]) and **test manually** before submitting.
- **Describe what changed, why, and how to test it.** Flag enforcement-sensitive changes explicitly.
- Use [Conventional Commits](https://www.conventionalcommits.org/): `fix(engine): prevent crash when queue is empty`, `feat(ui): add delay picker`, etc.
- Scopes: `core`, `engine`, `wd`, `installer`, `ui`, `security`, `build`.

---

## AI Agent Skills

CTBL++ is developed with an AI-assisted workflow. The project ships **skill files** (`.agents/*.md`) that give AI coding agents the context they need — architecture conventions, file placement rules, review checklists, and tool-specific guidance.

If you're using an AI agent to contribute, load the relevant skills before you start.

### Recommended skills

<!-- TODO: Fill in exact skill names and descriptions. -->

| Skill | When to load |
|---|---|
| <!-- e.g. `c-sharp-architect` --> | <!-- e.g. Creating new files or modules --> |
| <!-- e.g. `powershell-engineer` --> | <!-- e.g. Working on Sealing.ps1, Deploy.ps1 --> |
| <!-- e.g. `html-architect` --> | <!-- e.g. Working on the patched web UI --> |
| <!-- e.g. `dev-console` --> | <!-- e.g. Using the dev.ps1 workflow --> |
| <!-- e.g. `code-review` --> | <!-- e.g. Before opening a PR --> |
| <!-- e.g. `codebase-xray` --> | <!-- e.g. Onboarding or navigating unfamiliar code --> |

---

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
