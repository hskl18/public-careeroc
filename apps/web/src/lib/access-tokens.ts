export type AccessToken = {
  id: string;
  name: string;
  tokenPrefix: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  lastUsedAtUtc: string | null;
  revokedAtUtc: string | null;
};
