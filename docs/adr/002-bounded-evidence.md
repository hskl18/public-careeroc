# ADR 002: Bounded Snapshots for Agents

Status: accepted

## Context

careeroc needs the text of recruiting email to classify it, match it to an application, and show the evidence behind a status.
The multi-agent review and outside assistants need far less: counts, statuses, role patterns, and the latest resume summary.
Email text is untrusted input, and a model that reads it can be steered by it.

## Decision

The API keeps each message body, cut to `Gmail:BodyCharacterLimit` (12,000 characters by default), so the pipeline and the review queue can show their evidence.
Attachments become evidence cards; their raw bytes are not stored.
Everything that leaves the API for an agent is a bounded snapshot built by the API: application company, role, status, and dates, resume summary and skills, counts, and confirmed memory.
Snapshots never contain email bodies, OAuth tokens, or credentials, and the agent runtime has no access to careeroc's database.

## Consequences

A prompt injection in an email can at worst skew a suggestion; it cannot read other data or write anything.
Agents cannot quote a specific email, so their findings cite statuses and counts rather than messages.
The stored bodies are still personal data, which is why per-user scoping, deletion controls, and the demo's 24-hour lifetime apply to them.
