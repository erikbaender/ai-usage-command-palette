# Security and privacy boundary

The extension is a local usage display. It must not become an authentication broker.

- Do not read browser cookies, browser local storage, Claude session keys, Codex token files, or API keys.
- Let Codex CLI own its authentication through `codex app-server`.
- Invoke the authenticated Claude CLI through its normal user context; do not read Claude credentials directly.
- Cache only provider name, usage percentages, reset timestamps, observation time, plan label when provided, and schema metadata.
- Redact child-process arguments, environment variables, stdin, and stderr from diagnostics.
- Do not modify Claude settings or install a helper executable.
- Treat all provider JSON as untrusted input: size-limit, validate, and reject malformed or excessive payloads.
