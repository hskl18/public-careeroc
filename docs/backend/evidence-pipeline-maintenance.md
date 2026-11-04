# Evidence Pipeline Maintenance

This note covers the operational boundaries around Gmail sync, evidence analysis, reminder evidence refresh, and company names.

## Gmail Sync

Gmail sync uses the mailbox `historyId` after the initial backfill.
The history endpoint can still return message ids that are gone or inaccessible by the time careeroc fetches the full message.

Rules:

- A `404` from `users/me/history` means the history cursor expired or is no longer valid; the sync path may fall back to a broader search.
- A `404` from `users/me/messages/{messageId}` during a history-based fetch is treated as an item-level miss and skipped.
- One missing message must not fail the whole inbox sync.
- The UI may call the bounded dashboard inbox check while first sync or analysis is active, but the backend sync worker remains the owner of regular polling and queue draining.

## Manual Review Boundary

Manual review is a trust boundary, not a display-only flag.

When an email requires manual review:

- Do not create or mutate job applications from that evidence.
- Do not create reminders, status changes, or timeline events from that evidence.
- Do not show reminders backed only by review-blocked evidence as actionable dashboard work.
- Keep the evidence available in the review queue so a human correction can promote it later.

This prevents low-confidence extraction from leaking into automation while still preserving the original source email.

## Recent Updates Ordering

Recent timeline sections should be ordered by the newest activity first.
Importance or category may be shown visually, but it must not override recency in a "most recent" view.

The expected order is:

1. Company groups by latest activity timestamp.
2. Role groups inside a company by latest activity timestamp.
3. Individual updates inside a role by latest activity timestamp.

## Reminder Evidence Refresh

Some older reminders were created before explicit deadline extraction or before the linked email evidence was strong enough.
The backend now refreshes reminder evidence automatically before dashboard and application-detail reads, scoped to the signed user.

Direct endpoint:

```http
POST /api/reminders/{reminderId}/evidence-refresh
```

Behavior:

- Requires the normal signed user context.
- Only refreshes an open reminder owned by the signed user.
- Does not refresh rejected or closed applications.
- Reuses linked `InboxEmail` evidence for the reminder's application.
- Ignores emails that require manual review.
- Prefers category-matched actionable evidence, then other actionable evidence, then other trusted evidence.
- Runs explicit due-date extraction when the source email has no parsed due date yet.
- Updates the reminder when a trusted explicit date is found.
- Leaves the due date displayed as unknown when no exact source deadline exists; the system may keep an internal reminder time, but the UI must not present that fallback as a parsed due date.

Response:

```json
{
  "remindersRefreshed": 1,
  "emailsUpdated": 1
}
```

The dashboard and application detail surfaces do not expose a manual `Rerun` button.
Unknown dates are allowed and should be displayed as unknown rather than guessed.

## Company Name Guards

Suspicious company names, such as temporal phrases, sentence fragments, role fragments, URLs, or recruiter names, are stopped before they become `JobApplication.Company`:

- `CompanyNameNormalizer.IsUsableCompanyName` rejects them.
- Parsed entities are normalized before application matching.
- Applications are created only when the company is usable.
- The parser merge coerces unusable names to `Unknown Company`.
- URL-like values such as `Https`, `https://...`, `www...`, or a bare domain are never company names.

There is no automatic repair of historical rows.
Fix an existing wrong company through the review queue or application detail, which reruns the same workflow rules.

## Guidance Maintenance Agent

The guidance maintenance agent is the only agent allowed to evolve durable markdown guidance from accumulated user feedback.

Boundary:

- It may group repeated correction patterns.
- It may propose edits to `docs/agent-memory/*.md` and `docs/agents/*.soul.md`.
- It must not directly change source code, database rows, or historical application records.
- It must not turn one correction into a broad rule without regression coverage.

Evaluation gate: `dotnet test Careeroc.sln`, which runs the document-contract tests, the parser and feedback regressions, the guidance-store tests, and the pipeline evaluation.
If the gate fails or is skipped, the agent can leave a proposal but must not apply markdown changes.

Corrections reach the guidance only when `Ollama__UserFeedbackFilePath` is set, which Development does by default.
The file is shared by everyone on an install, so production and demo installs leave it unset.

## Verification

Run backend tests after changing sync, workflow, reminder refresh, or company-name logic:

```bash
dotnet test Careeroc.sln
```

Run frontend checks after changing dashboard or application detail behavior:

```bash
pnpm --dir apps/web lint
pnpm --dir apps/web build
```

## Runtime Notes

- Restart the API after backend service, endpoint, or dependency-injection changes.
- Restart or let the Next.js dev server hot-reload after dashboard/application detail UI changes.
