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
They look like the workspace: the same header and bottom nav (with Overview and Architecture as the items), the same type scale, and panels in a responsive grid.
There is no separate marketing style or hero-scale type.

Workspace pages (`/app/*`) are tools.
They use the workspace shell (`px-5`, `py-6`, `gap-4`), which spans the full window, and dense tables only where comparison matters.

## Navigation

The primary nav stays small: Dashboard, Applications, Analytics, Resume.

Everything else is reached in context:

- Inbox from setup prompts, the sync pill, Settings, or the user menu
- Review from the "Email review" count and button, and from blocked "To do" items
- Updates from the dashboard's recent activity
- Settings and Memory from the user menu

Human review is never a default destination.
Routine evidence checks belong to the review agent; review UI appears only when automation is blocked and a correction or trust decision is needed.

## Surfaces

| Surface | Owns |
|---|---|
| Header | Brand, primary nav, a compact sync pill, the user menu, and in demo mode a short notice that the data is fictional, which also says read-only in the hosted demo; never full workflow controls |
| Dashboard `/app` | One screen that does not scroll on desktop: one row for the multi-agent review, which opens its trace and Approve or Reject in a dialog, then a row of counts, ranked recent activity with short tags beside each company that show their time on hover or tap, and the to-do list, as flat lists with no nested cards |
| Applications `/app/applications` | Browsing every record: search, status filters, aligned rows with stage and urgency, a selected preview, links to detail and Gmail |
| Application detail | Why the record has its status: pipeline stage, a brief and next move, open actions or terminal outcome, contacts, evidence grouped by thread, original Gmail links, correction buttons |
| Review `/app/review` | Only ambiguous items: uncertain company, role, or match, agencies, likely scams |
| Updates `/app/updates` | The full activity timeline |
| Analytics `/app/analytics` | Funnel health: counts, stage distribution, company and role tables, drill-downs; never next actions |
| Resume `/app/resume` | The score, what to fix, and what careeroc read |
| Memory `/app/memory` | Pending, confirmed, and rejected facts that agents can reuse, as three columns |
| Inbox `/app/inbox` | Gmail connection, backfill window, sync health and diagnostics, contact history, disconnect and imported-data deletion |
| Settings `/app/settings` | Candidate context, timezone, assistant access tokens, data controls |

A dashboard list scrolls inside its panel on desktop and keeps a short preview on phones; the full list has its own page and a link.
Rejected and closed applications must look terminal and show no open follow-up work.

## Layout and components

1. A row of counts or the page's one action comes first.
2. Panels sit in a responsive grid below it.
3. Long history moves to a dedicated route.

Component roles stay consistent: a workspace composes a page, a section holds one product concept, a panel is a focused tool or summary, a card is a repeated item or bounded detail, a pill is a state or count, a button is a command, and inputs edit values.
Avoid cards inside cards unless the inner card is a repeated record, an evidence item, or an input cluster.
Build shared components for repeated product concepts (status pill, sync status, application summary, recent changes, workflow explorer), not for repeated CSS.

## Buttons and pills

Buttons are commands with verbs: Open, Open Gmail, Connect Gmail, Save, Not an OA.
Pills are states and counts: Synced, Rejected, Urgent, Due soon.
Every button has disabled and error states, and no global style may override a pill's status colors.
Show product labels, never raw backend enum values; `formatEnumLabel` turns `oa_invitation` and `Oa Pending` into "OA Invitation" and "OA Pending".
An agent proposal is listed with what it would create and stays inert until the user presses Approve.

## Design system

Everything here is short and quick to understand.
`apps/web/eslint.config.mjs` fails the lint on raw sizes, raw colors, uppercase, and letter spacing.

**Copy.**
A title is at most three words, and a line of body text says one thing.
No page descriptions, eyebrow labels above a heading, or sentences that repeat a label.
An empty state or an error is one short line.
Say what a page holds in its nav label, so most workspace pages need no visible title.

**Type.**
One sans font; `font-mono` is only for ids and code.
Four sizes and two weights:

| Role | Class | Size |
|---|---|---|
| Page title | `text-xl font-semibold` | 20px |
| Panel title, count | `text-base font-semibold` | 16px |
| Body, list item, button | `text-sm` | 14px |
| Secondary line, label, pill, small button | `text-xs` | 12px |

Sentence case everywhere, with no letter spacing.

**Color.**
Only the `var(--*)` tokens in `globals.css`.
The palette is light only: white and off-white surfaces, dark navy text, brand blue for commands and navigation, green for success, red for rejected or failed, and amber for warnings and pending approval.
Text keeps at least 4.5:1 contrast on white, and every interactive element shows a focus ring.
No gradients, no dark panels, and shadows only on floating surfaces such as menus and toasts.

**Shape and space.**
Corners are 8px on panels and 6px on controls; pills are round.
Spacing follows a 4px grid: 12px between panels on phones and public pages, 16px in the workspace on desktop, and 16px inside a panel.

**Components.**
| Component | Use |
|---|---|
| `.card` | A panel; never nested for layout |
| `.metric-tile` | A count, one line, label left and number right; it links to what it counts |
| Flat rows with `divide-y` | Any list, with one main line and at most one secondary line |
| `.badge` | A state only |
| `.btn` | A command; primary at most once per panel |
| `<dialog>` | Detail that would otherwise push the page into scrolling |

Open an item by clicking it, not through a separate button; in Applications a click selects the preview, and Open goes to detail.

**Layout.**
Workspace pages fit one screen on desktop and never scroll as a page; long lists scroll inside their panel.
Phones may scroll.
Pages span the full window, and no blank band is left under the content: the last block takes the spare height (`.fill-last` inside a scroll region), and a short list shares it between its rows with `.fill-rows`.
Public pages fill the screen the same way, with the footer below the last block, and fit one screen from 1280x720 up.

**Scale.**
The sizes here are for a 1440x900 window.
Above that the root font size grows with the window, up to 150%, so every screen keeps the same proportions; it never shrinks.

**Characters.**
The pixel characters give the public pages' agents and steps a face.
In the workspace they only clarify state (loading, mail sync, profile icons).
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

"To do" shows only actionable reminders: due recently or soon, not completed, dismissed, or closed, and not stale, far-future, or low-signal.
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
