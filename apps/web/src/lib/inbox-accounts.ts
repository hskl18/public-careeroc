export type InboxAccountSetup = {
  id: string;
  provider: string;
  providerLabel: string;
  emailAddress: string;
  setupMode: string;
  connectionStatus: string;
  nextStep: string;
  createdAtUtc: string;
  lastSyncedAtUtc: string | null;
  backfillMonths: number | null;
  backfillAllHistory: boolean;
  initialBackfillCompletedAtUtc: string | null;
  isValid?: boolean | null;
  invalidReason?: string | null;
};

export type InboxSetupResponse = {
  accounts: InboxAccountSetup[];
};
