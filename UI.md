# careeroc UI contract

This is the source of truth for interface work in `apps/web`.
When a new surface is ambiguous, make it look and behave like the Dashboard.
The product name in the UI is careeroc, always lowercase.

## What the UI is for

The workspace is an operating console for one job seeker, not a generic CRUD shell.
Every surface should help answer one of these questions:

- Is my inbox connected and being analyzed?
- What changed recently, and what actually needs action now?
- Which applications are active, interviewing, rejected, or at offer?
- What evidence supports each state, and where can I correct it?
- What do the agents recommend, and have I approved it?

## Public pages and the workspace

Public pages (`/` and `/tech`) explain the open-source project and lead into the demo.
They use a compact nav (Overview, Architecture), a two-column first screen with copy on the left and a real product surface on the right, short step-based sections instead of long feature lists, and mascots as occasional brand anchors.
Hero-scale type belongs only here.

Workspace pages (`/app/*`) are tools.
They use the constrained workspace shell (`max-w-[104rem]`, `px-5`, `py-6`, `gap-4`), compact cards for bounded content, dense tables only where comparison matters, and no marketing copy.

## Navigation

The primary nav stays small: Dashboard, Applications, Analytics, Resume.

Everything else is reached in context:

- Inbox from setup prompts, the sync pill, Settings, or the user menu
- Review from "needs attention" items that are blocked
- Updates from the dashboard's recent activity
- Settings, Memory, and Admin (admins only) from the user menu

Human review is never a default destination.
Routine evidence checks belong to the review agent; review UI appears only when automation is blocked and a correction or trust decision is needed.

## Surfaces

| Surface | Owns |
|---|---|
| Header | Brand, primary nav, a compact sync pill, the user menu, and in demo mode a short notice that the data is fictional; never full workflow controls |
| Dashboard `/app` | The current operating picture: ranked recent changes, near-term actions, sync health, a capped application summary |
| Applications `/app/applications` | Browsing every record: search, status filters, aligned rows with status and priority, a selected preview, links to detail and Gmail |
| Application detail | Why the record has its status: pipeline stage, agent brief and next move, open actions or terminal outcome, contacts, evidence grouped by thread, original Gmail links, correction buttons |
| Review `/app/review` | Only ambiguous items: uncertain company, role, or match, agencies, likely scams |
| Updates `/app/updates` | The full activity timeline |
| Analytics `/app/analytics` | Funnel health: counts, stage distribution, company and role tables, drill-downs; never next actions |
| Resume `/app/resume` | Versions, extracted fields, source links, corrections, recruiter-style evaluation, suggestions tied to target roles |
| Memory `/app/memory` | Confirmed and pending memory, the multi-agent review with its trace, and Approve or Reject for a review that proposes reminders or needs a person |
| Inbox `/app/inbox` | Gmail connection, backfill window, sync health and diagnostics, contact history, disconnect and imported-data deletion |
| Settings `/app/settings` | Candidate context, timezone, assistant access tokens, data controls |
| Admin `/app/admin` | Configuration checks, queue counts, failed-job retry, and recent model calls, enforced by the API |

When a dashboard section grows longer than its neighbor, move the full list to its own page and keep a capped summary with a link.
Rejected and closed applications must look terminal and show no open follow-up work.

## Layout and components

1. A top summary card names the surface and exposes its main action.
2. Supporting panels sit in a responsive grid.
3. Full data views are dense and scannable.
4. Long history moves to a dedicated route.

Component roles stay consistent: a workspace composes a page, a section holds one product concept, a panel is a focused tool or summary, a card is a repeated item or bounded detail, a pill is a state or count, a button is a command, and inputs edit values.
Avoid cards inside cards unless the inner card is a repeated record, an evidence item, or an input cluster.
Prefer normal page scroll over nested scroll panes, especially when reading evidence.
Build shared components for repeated product concepts (status pill, sync status, application summary, recent changes, workflow explorer), not for repeated CSS.

## Buttons and pills

Buttons are commands with verbs: Open applications, Open Gmail, Connect Gmail, Save, Not an OA, Rerun.
Pills are states and counts: Synced, Rejected, P3, 1 message.
Every button has disabled and error states, and no global style may override a pill's status colors.
Show product labels, never raw backend enum values; `formatEnumLabel` turns `oa_invitation` and `Oa Pending` into "OA Invitation" and "OA Pending".
An agent proposal is listed with what it would create and stays inert until the user presses Approve.

## Visual system

The palette is light only: white and off-white surfaces, dark navy text, muted blue-gray secondary text, brand blue for commands, navigation, sync, and information, green for connected and success, red for rejected, destructive, and failed, and amber (the `--yellow` tokens) for warnings, pending approval, and blocked work.
Text colors keep at least 4.5:1 contrast on white, and every interactive element shows a visible focus ring.
No dark mode, no dark dashboard panels on public pages, and no CSS gradients.
Avoid one-note pages that are all blue, beige, or gray.

Eyebrow labels are short, uppercase, and mono; panel headings are compact; body text is brief and concrete.
No viewport-width font sizes and no negative letter spacing.
Long company names, roles, senders, and email addresses wrap or truncate without covering controls.

Mascots clarify state (loading, mail sync progress, profile icons, public step flows); they are not decoration for every card or navbar.
SVG backgrounds stay transparent and keep the existing pixel style.

## States and freshness

Every live surface distinguishes these states, shown next to the affected panel rather than only in logs:

- **live**: fresh API data
- **loading**: a refresh is in flight
- **fallback**: static or offline data, visibly marked
- **stale**: data is valid but background work has not advanced
- **blocked**: the user or the backend must fix something

Never show "Paused" unless the backend reports a paused state.
The header sync pill is a summary (connected or not, processed of total, a link to Inbox); the Inbox sync panel holds the detail (last sync, progress, stuck reason, job counts).

"Needs attention" shows only actionable reminders: due recently or soon, not completed, dismissed, or closed, and not stale, far-future, or low-signal.
When in doubt, keep low-confidence automation out of prominent dashboard areas and show it on detail surfaces.
Show received time inside mail-derived sections and reserve "last update" for record metadata.

## Checklist

- Does the page have one job and feel like part of the Dashboard system?
- Is the primary nav still four items?
- Are buttons commands and pills states, with readable status colors?
- Is evidence next to the state or correction it supports?
- Are empty, loading, fallback, stale, and blocked states visible?
- Do long labels and emails stay readable on mobile and desktop?
- Does the page avoid asking the user to review work the review agent should own?
