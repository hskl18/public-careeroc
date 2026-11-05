import { describe, expect, it } from "vitest";
import recording from "./demo-snapshot.json";

type Recorded = { body: Record<string, unknown> };
const { responses } = recording as unknown as { responses: Record<string, Recorded> };

// The hosted demo shows whatever was recorded, so a recording of a broken run ships as a broken demo.
describe("recorded demo", () => {
  it("captured a finished pipeline", () => {
    const { inbox } = responses["GET /api/dashboard?view=active"].body as {
      inbox: { backfillStatus: string; lastAnalysisError: string | null; pendingAnalysisEmails: number };
    };

    expect(inbox.backfillStatus).toBe("complete");
    expect(inbox.lastAnalysisError).toBeNull();
    expect(inbox.pendingAnalysisEmails).toBe(0);
  });

  it("captured a real model's review that waits for approval", () => {
    const proposals = responses["GET /api/profile/memory/proposals"].body as unknown as {
      kind: string;
      value: string;
    }[];
    const entry = proposals.find((proposal) => proposal.kind === "orchestrator");
    const review = JSON.parse(entry?.value ?? "{}") as {
      status: string;
      modelPath: string;
      proposedReminders: unknown[];
    };

    expect(review.status).toBe("review_required");
    // A timed-out model falls back to the deterministic review, which has no approval to show.
    expect(review.modelPath).toMatch(/^(ollama|openai)\//);
    expect(review.proposedReminders.length).toBeGreaterThan(0);
  });
});
