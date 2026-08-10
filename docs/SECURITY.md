# Security and privacy boundary

The extension is a local usage display. It must not become an authentication broker.

- Do not read browser cookies, browser local storage, Claude session keys, Codex token files, or API keys.
- Let Codex CLI own its authentication through `codex app-server`.
- Let Claude Code supply status-line telemetry through the user's configured command.
- Cache only provider name, usage percentages, reset timestamps, observation time, plan label when provided, and schema metadata.
- Redact child-process arguments, environment variables, stdin, and stderr from diagnostics.
- Use explicit opt-in for modifying Claude status-line configuration and provide restore.
- Treat all provider JSON as untrusted input: size-limit, validate, and reject malformed or excessive payloads.
