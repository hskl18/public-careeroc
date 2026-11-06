import { describe, expect, it } from "vitest";
import {
  getActionLabel,
  getStatusMeta,
  isReconnectableGmailAccount,
  needsGmailSetup,
} from "./inbox-account-status";
import type { InboxAccountSetup } from "./inbox-accounts";

function account(connectionStatus: string, overrides: Partial<InboxAccountSetup> = {}) {
  return {
    id: "inbox-1",
    provider: "gmail",
    emailAddress: "alex@demo.careeroc.example",
    connectionStatus,
    nextStep: "Continue with Google.",
    backfillMonths: 3,
    backfillAllHistory: false,
    ...overrides,
  } as InboxAccountSetup;
}

describe("needsGmailSetup", () => {
  it.each([
    ["a connected inbox", account("Connected"), false],
    ["an unfinished setup", account("Waiting for Google"), true],
    ["deleted imported data", account("Imported data deleted"), false],
    ["a disconnected inbox", account("Disconnected", { nextStep: "Gmail access was disconnected." }), false],
    ["another provider", account("Waiting for Google", { provider: "demo" }), false],
  ])("is %s -> %s", (_, setup, expected) => {
    expect(needsGmailSetup(setup)).toBe(expected);
  });
});

describe("isReconnectableGmailAccount", () => {
  it("covers expired authorization and broken metadata, not a fresh setup", () => {
    expect(isReconnectableGmailAccount(account("Reconnect required"))).toBe(true);
    expect(isReconnectableGmailAccount(account("Waiting for Google", { isValid: false }))).toBe(true);
    expect(isReconnectableGmailAccount(account("Waiting for Google"))).toBe(false);
  });
});

describe("getStatusMeta and getActionLabel", () => {
  it("asks to reconnect when Google rejected the stored authorization", () => {
    const setup = account("Reconnect required");

    expect(getStatusMeta(setup).label).toBe("Reconnect");
    expect(getActionLabel(setup)).toBe("Reconnect with Google");
  });

  it("asks to finish an OAuth flow that never completed", () => {
    const setup = account("OAuth not completed");

    expect(getStatusMeta(setup)).toMatchObject({ label: "Finish setup", className: expect.stringContaining("red") });
    expect(getActionLabel(setup)).toBe("Reconnect with Google");
  });

  it("continues a setup that is waiting on Google", () => {
    const setup = account("Waiting for Google");

    expect(getStatusMeta(setup)).toMatchObject({ label: "Finish setup", className: expect.stringContaining("yellow") });
    expect(getActionLabel(setup)).toBe("Continue with Google");
  });
});
