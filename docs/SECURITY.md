# Security and privacy boundary

The extension is a local usage display. It must not become an authentication broker.

- Keep provider cookies and browser storage inside the extension's isolated WebView2 profiles; never export or log their values.
- Let each provider's website own web authentication and token rotation.
- Let the Codex and Claude CLIs own their authentication when a CLI backend or fallback is used.
- Do not read Codex or Claude token files or ask for API keys.
- Cache only provider name, usage percentages, reset timestamps, observation time, plan label when provided, and schema metadata.
- Redact child-process arguments, environment variables, stdin, and stderr from diagnostics.
- Do not modify Claude settings or install a helper executable.
- Treat all provider JSON as untrusted input: size-limit, validate, and reject malformed or excessive payloads.
