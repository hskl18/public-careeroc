# Gmail Push Readiness Plan

Status: design-only.
Current production path remains bounded polling plus user-triggered `sync-now`.

## Lifecycle

1. Request `users.watch` for each connected Gmail inbox after OAuth completes.
2. Persist `historyId`, expiration, and watch status on inbox metadata.
3. Refresh each watch before Google's seven-day expiration window.
4. If watch renewal fails, fall back to the existing polling worker and surface the stale watch in admin diagnostics.

## Delivery Path

Pub/Sub should deliver to a small authenticated webhook or Cloud Run shim.
The webhook verifies an HMAC or equivalent shared secret before calling the existing `InboxSyncCoordinator` path for the owning user.
The webhook should enqueue work and return quickly; it should not parse Gmail content inline.

## Trust Boundary

- Never trust the Gmail address from the webhook payload alone.
- Resolve inbox ownership from persisted watch metadata.
- Reuse the same Gmail token protection and sync lease path as polling.
- Do not mutate applications/reminders directly from the push handler.

## Fallback

Polling remains enabled as recovery when watch setup, renewal, Pub/Sub delivery, or webhook verification fails.
Push should only replace polling cadence after production metrics prove polling cost or freshness is the bottleneck.
