# CLAUDE

Purpose
-------
This document gives a concise, practical guide for using Anthropic Claude (or similar large language models) with this repository.

When to use
-----------
- Use Claude for high-level design, summarization, code explanation, and non-sensitive content generation.
- Do not use Claude for processing or storing secrets, personal data, or any content that must remain confidential.

Usage notes
-----------
- Keep prompts minimal and specific. Prefer deterministic instructions and include examples when needed.
- Provide model inputs that exclude secrets. Use environment variables or secure vaults for any credentials.

Security & privacy
------------------
- Never commit API keys or secrets to source control. Store keys in secure environment variables (example: CLAUDE_API_KEY).
- Treat model outputs as untrusted: validate, sanitize, and review before using in production.

Attribution
-----------
This repository may reference or integrate with external LLM services. Check each service's terms before use.
